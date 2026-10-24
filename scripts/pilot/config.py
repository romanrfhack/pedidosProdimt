#!/usr/bin/env python3
"""Generate/read private pilot configuration without executing shell input."""
import os
import re
import secrets
import sys
from pathlib import Path

def read_env(path):
    values = {}
    for line in Path(path).read_text(encoding='utf-8').splitlines():
        if line.strip() and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            values[key.strip()] = value.strip()
    return values

def main():
    command, filename = sys.argv[1:3]
    path = Path(filename)
    if command == 'create':
        project, port = sys.argv[3:5]
        if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{2,48}', project):
            raise ValueError('Project must use lowercase letters, digits, underscore or hyphen (3-49 characters).')
        if not port.isdigit() or not 1024 <= int(port) <= 65535:
            raise ValueError('HTTP port must be between 1024 and 65535.')
        path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        credentials = path.parent / 'ACCESO-PRIVADO.txt'
        if path.exists() or credentials.exists():
            raise FileExistsError('Existing private configuration/access file must be recovered and reused, not replaced.')
        values = {
            'PRODIMT_PROJECT': project,
            'PRODIMT_HTTP_PORT': port,
            'PRODIMT_APP_IMAGE': project + '-app:current',
            'PRODIMT_SQL_SA_PASSWORD': secrets.token_hex(24) + 'Aa1!',
            'PRODIMT_APP_SQL_PASSWORD': secrets.token_hex(24) + 'Bb2!',
            'PRODIMT_JWT_KEY': secrets.token_hex(32),
            'PRODIMT_ADMIN_USER': 'prodimt-admin',
            'PRODIMT_ADMIN_PASSWORD': secrets.token_urlsafe(24) + 'Cc3!'
        }
        created = []
        try:
            fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            created.append(path)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                stream.write('\n'.join(key + '=' + value for key, value in values.items()) + '\n')
            fd = os.open(credentials, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            created.append(credentials)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                stream.write('PRODIMT — acceso inicial privado\n')
                stream.write('URL local: http://127.0.0.1:' + port + '/admin/login\n')
                stream.write('Usuario: ' + values['PRODIMT_ADMIN_USER'] + '\n')
                stream.write('Contraseña: ' + values['PRODIMT_ADMIN_PASSWORD'] + '\n')
                stream.write('Estas credenciales se generaron una sola vez. Proteja también .env y el volumen SQL.\n')
        except OSError:
            for created_path in created:
                created_path.unlink(missing_ok=True)
            raise
        print('Private configuration created. Credentials are in ACCESO-PRIVADO.txt; secrets were not printed.')
    elif command == 'get':
        print(read_env(path)[sys.argv[3]])
    elif command == 'provision-sql':
        password = read_env(path)['PRODIMT_APP_SQL_PASSWORD']
        # Generated values are constrained before interpolation into a SQL literal.
        if not re.fullmatch(r'[a-f0-9]{48}Bb2!', password):
            raise ValueError('Invalid generated application password format; do not edit secrets manually.')
        print("IF DB_ID(N'ProdimtPilot') IS NULL CREATE DATABASE [ProdimtPilot];\nGO")
        print("IF SUSER_ID(N'prodimt_app') IS NULL CREATE LOGIN [prodimt_app] WITH PASSWORD=N'" + password + "', CHECK_POLICY=ON;\nGO")
        print("USE [ProdimtPilot];\nGO\nIF USER_ID(N'prodimt_app') IS NULL CREATE USER [prodimt_app] FOR LOGIN [prodimt_app];")
        print("ALTER USER [prodimt_app] WITH LOGIN = [prodimt_app];")
        print("IF IS_ROLEMEMBER(N'db_datareader', N'prodimt_app') = 0 ALTER ROLE [db_datareader] ADD MEMBER [prodimt_app];")
        print("IF IS_ROLEMEMBER(N'db_datawriter', N'prodimt_app') = 0 ALTER ROLE [db_datawriter] ADD MEMBER [prodimt_app];\nGO")
    elif command == 'check':
        values = read_env(path)
        required = ['PRODIMT_PROJECT','PRODIMT_HTTP_PORT','PRODIMT_APP_IMAGE','PRODIMT_SQL_SA_PASSWORD','PRODIMT_APP_SQL_PASSWORD','PRODIMT_JWT_KEY','PRODIMT_ADMIN_USER','PRODIMT_ADMIN_PASSWORD']
        if any(not values.get(key) for key in required):
            raise ValueError('Incomplete private configuration.')
        if path.stat().st_mode & 0o077:
            raise PermissionError('Private .env is readable by group/others. Run chmod 600 on that file.')
        if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{2,48}', values['PRODIMT_PROJECT']):
            raise ValueError('Invalid project name.')
        if not values['PRODIMT_HTTP_PORT'].isdigit() or not 1024 <= int(values['PRODIMT_HTTP_PORT']) <= 65535:
            raise ValueError('Invalid HTTP port.')
        if len(values['PRODIMT_JWT_KEY']) < 32:
            raise ValueError('JWT key is too short.')
        print('Private configuration is complete; secret values were not printed.')
    else:
        raise ValueError('Unknown command.')

if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
