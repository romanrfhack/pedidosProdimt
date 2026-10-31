#!/usr/bin/env python3
"""Inspect the existing native PRODIMT service. No installation or data changes.

Secret configuration is read only into memory. Output contains explicit metadata
and policy booleans. SQL uses fixed read-only queries against one approved target;
passwords travel through a clean subprocess environment, never arguments/output.
Unsupported configuration fails closed rather than guessing.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

SERVICE = 'prodimt-pedidos-api.service'
ROOT = Path('/var/www/prodimt-pedidos')
SERVER = '127.0.0.1,14330'
DATABASE = 'ProdimtPedidos'
LOGIN = 'prodimt_pedidos_app'
DEVELOPMENT_KEY = 'development-only-prodimt-pedidos-jwt-signing-key-change-before-production-2026'
SQL_TOOLS = ('/opt/mssql-tools/bin/sqlcmd', '/opt/mssql-tools18/bin/sqlcmd')
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}

SQL_METADATA = """SELECT DB_NAME() AS database_name,
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS sql_version,
 CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS sql_edition,
 CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS backup_directory,
 HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','BACKUP DATABASE') AS can_backup_database,
 HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE') AS can_create_any_database,
 IS_SRVROLEMEMBER('sysadmin') AS is_sysadmin,
 IS_SRVROLEMEMBER('dbcreator') AS is_dbcreator,
 IS_ROLEMEMBER('db_owner') AS is_db_owner,
 IS_ROLEMEMBER('db_ddladmin') AS is_db_ddladmin,
 HAS_PERMS_BY_NAME('dbo','SCHEMA','ALTER') AS can_alter_dbo,
 HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE') AS can_create_table,
 CAST(CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THEN 0 ELSE 1 END AS int) AS history_exists,
 CAST(CASE WHEN OBJECT_ID(N'dbo.Orders',N'U') IS NULL THEN 0 ELSE 1 END AS int) AS orders_exists,
 (SELECT MAX(CAST(is_unique AS int)) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Orders')
 AND name=N'IX_Orders_CustomerId_OrderDate_SequenceNumber') AS order_sequence_index_unique,
 (SELECT SUM(CAST(size AS bigint))*8/1024 FROM sys.database_files) AS database_files_MiB
 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"""
SQL_MIGRATIONS = 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'
SQL_DUPLICATES = """SELECT COUNT_BIG(*) AS duplicate_order_groups,
 (SELECT COUNT_BIG(*) FROM dbo.Orders) AS orders_count FROM
 (SELECT CustomerId,OrderDate,SequenceNumber FROM dbo.Orders WHERE CustomerId IS NOT NULL
 GROUP BY CustomerId,OrderDate,SequenceNumber HAVING COUNT_BIG(*)>1) AS groups_found
 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"""
QUERIES = (SQL_METADATA, SQL_MIGRATIONS, SQL_DUPLICATES)


class InspectionError(Exception):
    """Only fixed, non-sensitive issue codes may be passed to this exception."""


def command(args, env=None, timeout=15):
    try:
        result = subprocess.run(args, capture_output=True, text=True, stdin=subprocess.DEVNULL,
                                timeout=timeout, check=False, env=CLEAN_ENV if env is None else env)
        return result.returncode, result.stdout
    except (OSError, subprocess.TimeoutExpired, UnicodeError):
        return 127, ''


def read_limited(path):
    try:
        with Path(path).open('rb') as stream:
            raw = stream.read(1024 * 1024 + 1)
        if len(raw) > 1024 * 1024:
            raise InspectionError('configuration_too_large')
        return raw.decode('utf-8-sig')
    except (OSError, UnicodeError):
        raise InspectionError('configuration_unreadable') from None


def add_unique(target, key, value):
    key = key.replace('__', ':').lower()
    if key in target and target[key] != value:
        raise InspectionError('ambiguous_configuration_keys')
    target[key] = value


def process_environment(pid):
    result = {}
    for row in read_limited(f'/proc/{pid}/environ').split('\0'):
        if row and '=' in row:
            key, value = row.split('=', 1)
            if key in result and result[key] != value:
                raise InspectionError('ambiguous_process_environment')
            result[key] = value
    return result


def process_identity(pid):
    # /proc/stat's comm can contain spaces/parentheses; starttime is field 22.
    parts = read_limited(f'/proc/{pid}/stat').rsplit(') ', 1)
    if len(parts) != 2 or len(parts[1].split()) < 20:
        raise InspectionError('process_identity_unavailable')
    return parts[1].split()[19]


def assert_same_process(pid, identity):
    code, current = command(['systemctl', 'show', SERVICE, '--property=MainPID', '--value'])
    if code or current.strip() != str(pid) or process_identity(pid) != identity:
        raise InspectionError('service_changed_during_inspection')


def application_arguments(argv):
    positions = [i for i, arg in enumerate(argv) if Path(arg).name == 'Prodimt.Pedidos.Api.dll']
    if len(positions) == 1:
        args = argv[positions[0] + 1:]
    elif argv and Path(argv[0]).name == 'Prodimt.Pedidos.Api':
        args = argv[1:]
    else:
        raise InspectionError('application_arguments_unrecognized')
    result = {}
    index = 0
    while index < len(args):
        arg = args[index]
        if '=' in arg:
            key, value = arg.split('=', 1)
            key = key.removeprefix('--').removeprefix('/')
        elif arg.startswith('--') and index + 1 < len(args):
            key = arg[2:]
            index += 1
            value = args[index]
        else:
            raise InspectionError('application_arguments_unsupported')
        if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_:.-]*', key):
            raise InspectionError('application_arguments_unsupported')
        # Command-line keys do not receive the environment provider's __ mapping.
        result[key.lower()] = value
        index += 1
    return result


def flatten_json(value, prefix='', result=None):
    result = {} if result is None else result
    if not isinstance(value, dict):
        raise InspectionError('json_configuration_unsupported')
    for key, child in value.items():
        full = (prefix + key).lower()
        if isinstance(child, dict):
            flatten_json(child, full + ':', result)
        elif isinstance(child, list):
            raise InspectionError('json_array_configuration_unsupported')
        else:
            # JSON provider keys are case-insensitive, but do not normalize __.
            if full in result:
                raise InspectionError('ambiguous_configuration_keys')
            result[full] = str(child).lower() if isinstance(child, bool) else child
    return result


def json_object(pairs):
    result = {}
    seen = set()
    for key, value in pairs:
        if key.lower() in seen:
            raise InspectionError('ambiguous_configuration_keys')
        seen.add(key.lower())
        result[key] = value
    return result


def configuration(environment, argv, cwd):
    args = application_arguments(argv)
    host = {}
    for prefix in ('ASPNETCORE_', 'DOTNET_'):
        normalized = {}
        for key, value in environment.items():
            if key.upper().startswith(prefix):
                add_unique(normalized, key[len(prefix):], value)
        host.update(normalized)
    host.update(args)
    name = host.get('environment', 'Production')
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,64}', name):
        raise InspectionError('environment_name_unsupported')
    # Development can add user-secrets; do not claim an effective configuration.
    if name.lower() == 'development':
        raise InspectionError('development_user_secrets_not_inspected')
    content_root = Path(host.get('contentroot', str(cwd)))
    if not content_root.is_absolute():
        raise InspectionError('relative_content_root_unsupported')
    cfg = dict(host)
    files = []
    for filename in ('appsettings.json', f'appsettings.{name}.json'):
        path = content_root / filename
        if path.exists():
            try:
                cfg.update(flatten_json(json.loads(read_limited(path), object_pairs_hook=json_object)))
            except (ValueError, TypeError):
                raise InspectionError('json_configuration_unsupported') from None
            files.append(str(path))
    normalized = {}
    for key, value in environment.items():
        special = next((p for p in ('SQLCONNSTR_', 'SQLAZURECONNSTR_', 'CUSTOMCONNSTR_', 'MYSQLCONNSTR_')
                        if key.upper().startswith(p)), None)
        mapped = 'ConnectionStrings:' + key[len(special):] if special else key
        add_unique(normalized, mapped, value)
    cfg.update(normalized)
    cfg.update(args)
    return cfg, {'environment': name, 'content_root': str(content_root), 'json_files_read': files}


def first(cfg, *keys, default=None):
    # Match IConfiguration's null-coalescing semantics: empty is not missing.
    return next((cfg[key.lower()] for key in keys if cfg.get(key.lower()) is not None), default)


def policy_flags(cfg):
    key = first(cfg, 'Authentication:Jwt:SigningKey', 'PRODIMT_JWT_SIGNING_KEY', default='')
    key_ok = isinstance(key, str) and bool(key.strip()) and len(key.encode()) >= 32
    key_ok = key_ok and key != DEVELOPMENT_KEY and not any(x in key.upper() for x in ('CHANGE_ME', 'REPLACE_WITH'))
    provider = str(first(cfg, 'Persistence:Provider', default='SqlServer'))
    connection = first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING', default='')
    connection_ok = isinstance(connection, str) and bool(connection.strip()) and 'CHANGE_ME' not in connection.upper()
    seed = str(first(cfg, 'DevelopmentSeed:Enabled', default='false')).strip().lower() == 'true'
    migrations = str(first(cfg, 'DevelopmentSeed:ApplyMigrations', default='false')).strip().lower() == 'true'
    try:
        minutes = int(first(cfg, 'Authentication:Jwt:AccessTokenMinutes', default='720'))
    except (ValueError, TypeError):
        minutes = 720
    return {'provider_is_sqlserver': provider.lower() == 'sqlserver',
            'jwt_compatible_with_production_policy': bool(key_ok),
            'jwt_lifetime_compatible': 1 <= minutes <= 1440,
            'connection_compatible_with_production_policy': connection_ok,
            'development_seed_enabled': seed, 'automatic_migrations_enabled': migrations,
            'https_redirection_enabled': str(first(cfg, 'Hosting:UseHttpsRedirection', default='true')).strip().lower() == 'true'}


def connection_fields(connection):
    """Parse the narrow SqlClient format, including escaped quotes and semicolons.

    Unknown options are allowed in memory, but only SQL auth and the exact target
    below can be queried. Malformed strings and unsupported keys fail closed.
    """
    if not isinstance(connection, str) or not connection.strip():
        raise InspectionError('connection_missing')
    token = re.compile(r'''\s*([^=;]+?)\s*=\s*(?:"((?:[^"]|"")*)"|'((?:[^']|'')*)'|([^;'"]*))\s*(?:;|$)''')
    aliases = {'data source': 'server', 'address': 'server', 'addr': 'server', 'network address': 'server',
               'initial catalog': 'database', 'uid': 'user id', 'user': 'user id', 'pwd': 'password',
               'trusted_connection': 'integrated security'}
    values = {}
    pos = 0
    while connection[pos:].strip(' ;\t\r\n'):
        while pos < len(connection) and connection[pos] in ' ;\t\r\n':
            pos += 1
        match = token.match(connection, pos)
        if not match:
            raise InspectionError('connection_format_unsupported')
        name = ' '.join(match[1].lower().split())
        name = aliases.get(name, name)
        value = match[2].replace('""', '"') if match[2] is not None else match[3].replace("''", "'") if match[3] is not None else match[4].strip()
        # Duplicate aliases are legal in SqlClient but deliberately ambiguous here.
        if name in values and values[name] != value:
            raise InspectionError('ambiguous_connection_options')
        values[name] = value
        pos = match.end()
    return values


def permitted_connection(values):
    return (values.get('server') == SERVER and values.get('database') == DATABASE
            and values.get('user id') == LOGIN and bool(values.get('password'))
            and values.get('integrated security', 'false').lower() in ('false', 'no')
            and values.get('authentication', '').lower() in ('', 'sql password')
            and not any(values.get(k) for k in ('attachdbfilename', 'failover partner')))


def query_sql(tool, values, query):
    if tool not in SQL_TOOLS or not permitted_connection(values) or query not in QUERIES:
        raise InspectionError('sql_query_not_authorized')
    # Never inherit SQLCMDINI, SQLCMDSERVER, ODBCINI, or the application's secrets.
    env = dict(CLEAN_ENV, SQLCMDUSER=LOGIN, SQLCMDPASSWORD=values['password'])
    # ODBC sqlcmd rejects -h with -y 0. Give the scalar JSON column a fixed name
    # so both header-bearing and header-free output can be parsed without logs.
    wrapped = 'SELECT (' + query.rstrip().removesuffix(';') + ') AS ProdimtInspectionJson;'
    args = [tool, '-S', SERVER, '-d', DATABASE, '-C', '-b', '-l', '5', '-t', '10',
            '-y', '0', '-w', '65535', '-x', '-Q', 'SET NOCOUNT ON; SET LOCK_TIMEOUT 3000; ' + wrapped]
    code, output = command(args, env=env, timeout=20)
    if code:
        raise InspectionError('sql_read_failed')
    return parse_sql_output(output)


def parse_sql_output(output):
    if not isinstance(output, str) or len(output) > 1024 * 1024:
        raise InspectionError('sql_metadata_format_unrecognized')
    lines = output.strip().splitlines()
    if lines and lines[0].strip() == 'ProdimtInspectionJson':
        if len(lines) < 3 or not re.fullmatch(r'-+', lines[1].strip()):
            raise InspectionError('sql_metadata_format_unrecognized')
        lines = lines[2:]
    try:
        return json.loads(''.join(lines).strip())
    except (ValueError, TypeError):
        raise InspectionError('sql_metadata_format_unrecognized') from None


def sql_inventory(tool, values):
    if not permitted_connection(values):
        return {'queried': False, 'target_matches_expected': False}
    result = {'queried': False, 'target_matches_expected': True}
    if tool is None:
        result['issue'] = 'sqlcmd_not_available'
        return result
    try:
        metadata = query_sql(tool, values, SQL_METADATA)
        if not isinstance(metadata, dict) or metadata.get('database_name') != DATABASE:
            raise InspectionError('sql_database_identity_mismatch')
        safe = {'database_name': DATABASE}
        integer_keys = ('can_backup_database', 'can_create_any_database', 'is_sysadmin', 'is_dbcreator',
                        'is_db_owner', 'is_db_ddladmin', 'can_alter_dbo', 'can_create_table',
                        'history_exists', 'orders_exists', 'order_sequence_index_unique', 'database_files_MiB')
        for key in integer_keys:
            value = metadata.get(key)
            safe[key] = value if isinstance(value, int) and value >= 0 else None
        for key in ('sql_version', 'sql_edition', 'backup_directory'):
            value = metadata.get(key)
            safe[key] = value if isinstance(value, str) and len(value) <= 4000 else None
        result.update(queried=True, metadata=safe)
        if safe['history_exists'] == 1:
            rows = query_sql(tool, values, SQL_MIGRATIONS)
            if not isinstance(rows, list) or any(not isinstance(row, dict) or
                    not re.fullmatch(r'\d{14}_[A-Za-z0-9_]+', str(row.get('MigrationId', ''))) for row in rows):
                raise InspectionError('migration_metadata_unrecognized')
            result['migrations'] = [row['MigrationId'] for row in rows]
        if safe['orders_exists'] == 1:
            counts = query_sql(tool, values, SQL_DUPLICATES)
            count = counts.get('duplicate_order_groups') if isinstance(counts, dict) else None
            if not isinstance(count, int) or count < 0:
                raise InspectionError('duplicate_count_unrecognized')
            result['duplicate_order_groups'] = count
            order_count = counts.get('orders_count')
            result['orders_count'] = order_count if isinstance(order_count, int) and order_count >= 0 else None
    except InspectionError as exc:
        result['issue'] = str(exc)
    return result


def path_metadata(path):
    path = Path(path)
    result = {'path': str(path), 'exists': path.exists(), 'is_symlink': path.is_symlink()}
    if path.exists():
        try:
            st = path.stat()
            result.update(resolved=str(path.resolve()), uid=st.st_uid, gid=st.st_gid,
                          mode=oct(st.st_mode & 0o777), bytes=st.st_size)
        except OSError:
            result['readable_metadata'] = False
    return result


def inspect():
    result = {'mode': 'read-only', 'changes_made': False, 'secret_values_printed': False, 'service': SERVICE}
    code, text = command(['systemctl', 'show', SERVICE, '--no-pager',
                          '--property=MainPID,ActiveState,User,Group,WorkingDirectory,EnvironmentFiles'])
    if code:
        result['issue'] = 'service_metadata_unavailable'
        return result
    props = dict(row.split('=', 1) for row in text.splitlines() if '=' in row)
    result['service_metadata'] = {k: props.get(k) for k in ('ActiveState', 'User', 'Group', 'WorkingDirectory', 'EnvironmentFiles')}
    sql_code, sql_text = command(['systemctl', 'show', 'mssql-server.service', '--no-pager',
                                  '--property=LoadState,ActiveState,User,MainPID'])
    sql_props = dict(row.split('=', 1) for row in sql_text.splitlines() if '=' in row) if sql_code == 0 else {}
    result['native_sql_service'] = {k: sql_props.get(k) for k in ('LoadState', 'ActiveState', 'User', 'MainPID')}
    result['paths'] = [path_metadata(p) for p in (ROOT / 'backend/current', ROOT / 'frontend/current',
                       Path('/etc/prodimt-pedidos/api.env'), Path('/var/opt/mssql/backup'), Path('/var/opt/mssql/data'))]
    result['recent_releases'] = {part: sorted(p.name for p in (ROOT / part / 'releases').glob('*') if p.is_dir())[-4:]
                                 for part in ('backend', 'frontend')}
    result['free_disk_GiB'] = {str(p): round(shutil.disk_usage(p).free / 1024**3, 2)
                              for p in (ROOT, Path('/var/opt/mssql')) if p.exists()}
    try:
        pid = int(props.get('MainPID', '0'))
        if props.get('ActiveState') != 'active' or pid <= 0:
            raise InspectionError('service_not_running')
        identity = process_identity(pid)
        environment = process_environment(pid)
        argv = read_limited(f'/proc/{pid}/cmdline').rstrip('\0').split('\0')
        cwd = Path(f'/proc/{pid}/cwd').resolve(strict=True)
        cfg, source = configuration(environment, argv, cwd)
        result['configuration_sources'] = source
        result['policy_flags'] = policy_flags(cfg)
        dll = cwd / 'Prodimt.Pedidos.Api.dll'
        if dll.is_file():
            # The file is a release candidate at the process cwd; hashing does not
            # prove that an already-running process has loaded these exact bytes.
            result['process_cwd_dll_sha256'] = hashlib.sha256(dll.read_bytes()).hexdigest()
        runtime = cwd / 'Prodimt.Pedidos.Api.runtimeconfig.json'
        if runtime.exists():
            try:
                frameworks = json.loads(read_limited(runtime)).get('runtimeOptions', {}).get('frameworks', [])
                result['release_frameworks'] = [{'name': x['name'], 'version': x['version']} for x in frameworks
                                                if x.get('name') in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')]
            except (ValueError, TypeError, KeyError):
                result['runtimeconfig_unrecognized'] = True
        executable = Path(f'/proc/{pid}/exe').resolve(strict=True)
        dotnet = str(executable) if executable.name == 'dotnet' else shutil.which('dotnet')
        if dotnet:
            rc, output = command([dotnet, '--list-runtimes'])
            result['installed_runtimes'] = [{'name': m[0], 'version': m[1]} for m in
                re.findall(r'(?m)^(Microsoft\.(?:NETCore|AspNetCore)\.App)\s+([0-9A-Za-z.+-]+)\s+\[', output)] if rc == 0 else []
        values = connection_fields(first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING'))
        assert_same_process(pid, identity)
        # Only exact expected identifiers are emitted; unexpected values may contain secrets.
        result['sql_target'] = {'server_matches': values.get('server') == SERVER,
                                'database_matches': values.get('database') == DATABASE,
                                'login_matches': values.get('user id') == LOGIN}
        if not result['policy_flags']['provider_is_sqlserver']:
            result['sql'] = {'queried': False, 'issue': 'provider_not_sqlserver'}
        else:
            tool = next((name for name in SQL_TOOLS if Path(name).is_file() and os.access(name, os.X_OK)), None)
            result['sql'] = sql_inventory(tool, values)
    except InspectionError as exc:
        result['issue'] = str(exc)
    except (OSError, ValueError, TypeError):
        result['issue'] = 'native_inventory_incomplete'
    return result


def main():
    try:
        result = inspect()
    except Exception:
        # Do not expose unexpected exception text/tracebacks carrying config values.
        result = {'mode': 'read-only', 'changes_made': False, 'issue': 'native_inventory_incomplete'}
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
