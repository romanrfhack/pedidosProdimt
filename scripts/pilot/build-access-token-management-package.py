#!/usr/bin/env python3
"""Build the pinned I12 token-management update from a clean source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261101190000_AddRecoverableCustomerAccessTokens'
FEATURE = 'access-token-management-v1'
PREVIOUS = 'prices-148a52dd7428'
PREFIX = 'prodimt-access-token-management-update/'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_source(source):
    if not re.fullmatch(r'[a-f0-9]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')


def archive_bytes(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, raw)
    return stream.getvalue()


def validate_migration(sql):
    forbidden = re.search(r'(?im)^\s*(DELETE|TRUNCATE|DROP|EXEC(?:UTE)?|USE|GRANT|DENY)\s', sql)
    updates = re.findall(r'(?im)^\s*UPDATE\s+([^\s]+)', sql)
    inserts = re.findall(r'(?im)^\s*INSERT\s+(?:INTO\s+)?([^\s(]+)', sql)
    if forbidden or updates != ['dbo.CustomerAccessTokens'] \
            or set(inserts) != {'dbo.AuditLogs', '[__EFMigrationsHistory]'} \
            or re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) != [MIGRATION] \
            or any(marker not in sql for marker in ('ProtectedToken', 'MetadataVersion', 'IsReminderLink',
                'CustomerAccessTokenRevokedForRecoveryUpgrade', 'ProtectedToken IS NULL')) \
            or not re.search(r'SET\s+IsActive\s*=\s*0\s*,\s*MetadataVersion\s*=\s*MetadataVersion\s*\+\s*1\s+OUTPUT', sql, re.I) \
            or re.search(r'\b(TokenHash|EncryptionKey)\b', sql):
        raise ValueError('incremental_migration_unexpected')


def contains_encryption_key(raw):
    def visit(value):
        if not isinstance(value, dict):
            return False
        for key, child in value.items():
            if key.casefold() in ('customeraccesstokens:encryptionkey', 'customeraccesstokens__encryptionkey') and child:
                return True
            if key.casefold() == 'customeraccesstokens' and isinstance(child, dict) \
                    and any(name.casefold() == 'encryptionkey' and data for name, data in child.items()):
                return True
            if visit(child):
                return True
        return False
    return visit(json.loads(raw))


def build(backend, frontend, migration, output, source):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    files = {'inspect-native.py': (pilot / 'inspect-native.py').read_bytes(),
             'native-deployment-support.py': (pilot / 'install-quick-capture-update.py').read_bytes(),
             'commercial-catalog-support.py': (pilot / 'install-commercial-catalog-update.py').read_bytes(),
             'migrations-i12.sql': migration.read_bytes()}
    validate_migration(files['migrations-i12.sql'].decode('utf-8-sig'))
    for part, directory in (('backend', backend), ('frontend', frontend)):
        if not directory.is_dir() or directory.is_symlink():
            raise ValueError('build_directory_invalid')
        for path in sorted(directory.rglob('*')):
            if path.is_symlink():
                raise ValueError('build_symlink_not_allowed')
            if not path.is_file():
                continue
            name = path.relative_to(directory).as_posix()
            if any(segment.startswith('.') for segment in path.relative_to(directory).parts) \
                    or path.suffix.lower() in ('.env', '.key', '.pem', '.pfx', '.p12', '.bak', '.cs', '.ts', '.xlsx', '.csv', '.log', '.dmp', '.trace'):
                raise ValueError('build_private_or_source_file_not_allowed')
            if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
                continue
            raw = path.read_bytes()
            if part == 'backend' and name.startswith('appsettings') and name.endswith('.json') and contains_encryption_key(raw):
                raise ValueError('encryption_key_must_not_be_packaged')
            files[part + '/' + name] = raw
    required = {'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                'backend/appsettings.json', 'frontend/index.html', 'frontend/3rdpartylicenses.txt'}
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'expectedVersion', b'/prices')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsBefore=12, migrationsAfter=13,
                    previousRelease=PREVIOUS, revokeLegacyTokens=True, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-access-token-management-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = '''PRODIMT I12 — Gestión de tokens de acceso

Actualización exclusiva del I11 instalado: backend/frontend prices-148a52dd7428, doce migraciones.
Agrega recuperación segura y edición de metadatos de tokens nuevos. No cambia contraseñas ni Nginx.
REVOCA TODOS LOS TOKENS ANTERIORES durante la migración autorizada: después se deben generar y compartir enlaces nuevos.
Los pedidos, clientes, productos, perfiles, precios e historial se conservan. No envía mensajes automáticamente.

1. Verificar SHA256SUMS.txt en WSL desde la carpeta extraída.
2. Copiar los dos archivos:
scp PRODIMT_Instalar_I12.py prodimt-access-token-management-payload.zip root@194.238.26.70:/root/
3. Comprobar sólo lectura:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I12.py --package /root/prodimt-access-token-management-payload.zip'
Resultado esperado: planned, phase completed. Resolver primero cualquier discrepancia; no ejecutar bootstrap.
4. Guardar o resolver borradores pendientes y aplicar con terminal interactiva:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I12.py --package /root/prodimt-access-token-management-payload.zip --apply'
Solicita usuario y contraseña SQL existentes mediante entrada oculta. No son las credenciales web.
Realiza COPY_ONLY/CHECKSUM y RESTORE VERIFYONLY antes de migrar de doce a trece versiones.
Genera una clave dedicada de cifrado de 32 bytes únicamente si no existe; se guarda de forma privada en api.env.
La clave existente se reutiliza: no borrarla, sustituirla, publicarla ni regenerarla. No usa la clave JWT.
El snapshot privado conserva el entorno y la clave; respaldar ese snapshot junto con la base para recuperación futura.
Nunca comparte la clave en el JSON ni la incluye en releases o en este paquete.

Resultado esperado: updated/completed, migrations_after 13, existing_data_counts_preserved true,
encryption_key_effective_verified true, legacy_tokens_verified true y health_verified/frontend_verified true.
Si falla la aplicación, recupera los enlaces anteriores y conserva SQL y la clave actual. No restaura SQL automáticamente.
Una repetición después de un fallo reutiliza la clave y revoca sólo accesos hash-only activos que haya creado el backend anterior.
Los tokens nuevos cifrados no se revocan por repetir el instalador.
Si ya está actualizado sin accesos anteriores pendientes, already_current verifica sin migrar ni reiniciar.

Después: Ctrl+F5, Configuración de cliente > Tokens de acceso. Generar enlaces nuevos y comprobar copiar/revelar/editar/revocar.
Los enlaces anteriores quedan revocados por decisión expresa del negocio; no intentar recuperarlos desde su hash.
Conservar los respaldos privados y no compartir credenciales o claves en el chat.
'''
    bundle = {'PRODIMT_Instalar_I12.py': script.encode(), 'prodimt-access-token-management-payload.zip': payload,
              'LEEME.txt': readme.encode()}
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in bundle.items()}))
    return dict(sourceCommit=source, sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.migration, args.output, args.source), indent=2))


if __name__ == '__main__':
    main()
