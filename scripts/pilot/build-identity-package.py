#!/usr/bin/env python3
"""Build the pinned I18 identity update from a clean source revision."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MIGRATION = '20261102235000_AddSecurityAdministration'
FEATURE = 'identity-administration-v1'
PREVIOUS_BACKEND = 'access-tokens-65560ca3cf98'
PREVIOUS_FRONTENDS = ('customer-catalog-afb8f3ad9c33', 'pending-customers-cdbfec3071bc')
PREFIX = 'prodimt-identity-administration-update/'


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


def migration_sql_for_sqlcmd(sql):
    """Split the reviewed EF DDL/seed boundary without changing statements.

    GO sends a second batch on the same sqlcmd connection while the transaction
    remains open. EF InsertData references newly-created tables. For idempotent
    scripts the boundary precedes the WHOLE IF/BEGIN guard, never inside it.
    """
    if not isinstance(sql, str):
        raise ValueError('migration_input_invalid')
    sql = sql.removeprefix('\ufeff')
    marker = "IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision') AND [object_id] = OBJECT_ID(N'[SecurityStates]'))"
    if sql.count(marker) != 2 or sql.count('CREATE TABLE [Security') != 6 \
            or (sql.count('BEGIN TRANSACTION;') != 1 or sql.count('COMMIT;') != 1):
        raise ValueError('migration_batch_layout_unexpected')
    index = sql.index(marker)
    guard = ("IF NOT EXISTS (\n    SELECT * FROM [__EFMigrationsHistory]\n"
             "    WHERE [MigrationId] = N'" + MIGRATION + "'\n)\nBEGIN\n")
    guard_index = sql.rfind(guard, 0, index)
    if guard_index >= 0:
        if sql[guard_index + len(guard):index].strip():
            raise ValueError('migration_seed_guard_unexpected')
        index = guard_index
    prefix, tail = sql[:index], sql[index:]
    if prefix.endswith('GO\n\n'):
        result = sql
    else:
        if re.search(r'(?mi)^GO\s*$', prefix) or prefix.count('CREATE INDEX ') + prefix.count('CREATE UNIQUE INDEX ') != 5:
            raise ValueError('migration_ddl_layout_unexpected')
        result = prefix + 'GO\n\n' + tail
    validate_migration(result)
    return result


def validate_migration(sql):
    forbidden = re.search(r'(?im)^\s*(DELETE|UPDATE|TRUNCATE|DROP|USE|GRANT|DENY)\s', sql)
    inserts = {target.replace('[', '').replace(']', '').removeprefix('dbo.') for target in
               re.findall(r'(?im)^\s*INSERT\s+(?:INTO\s+)?([^\s(]+)', sql)}
    allowed = {'__EFMigrationsHistory', 'SecurityRoles', 'SecurityPermissions',
               'SecurityRolePermissions', 'SecurityUserRoles', 'SecurityStates'}
    first_seed = sql.find("IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Revision')")
    boundary = sql.find('GO\n\n')
    if forbidden or not inserts <= allowed or not {'__EFMigrationsHistory', 'SecurityUserRoles'} <= inserts \
            or re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) != [MIGRATION] \
            or not 0 < boundary < first_seed or sql.count('CREATE TABLE [Security') != 6 \
            or (sql.count('BEGIN TRANSACTION;') != 1 or sql.count('COMMIT;') != 1) \
            or any(marker not in sql for marker in ('IsDeleted', 'SessionRevision', 'security.manage',
                '18180000-0000-0000-0000-000000000001', 'FROM [AdminUsers]')):
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
             'token-management-support.py': (pilot / 'install-access-token-management-update.py').read_bytes(),
             'migrations-i18.sql': migration_sql_for_sqlcmd(migration.read_text(encoding='utf-8-sig')).encode()}
    validate_migration(files['migrations-i18.sql'].decode('utf-8-sig'))
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
    if any(marker not in scripts for marker in (b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'/prices', b'/security', b'directPermissionIds', b'roleIds')):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, migration=MIGRATION, migrationsBefore=13, migrationsAfter=14,
                    previousBackend=PREVIOUS_BACKEND, previousFrontends=list(PREVIOUS_FRONTENDS), revokeTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-identity-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    readme = """PRODIMT I18 — Usuarios, roles y permisos

Actualización de backend I12 access-tokens-65560ca3cf98 con frontend I15 o I17 identificados.
Rechaza I16 u otro release para no sobrescribir trabajo paralelo. Migración SQL13→14.
Incluye usuarios multirrol, permisos por rol y concesiones individuales adicionales.
Las cuentas administrativas existentes reciben Administrador para conservar su acceso.
Usuarios nuevos reciben sólo roles/permisos seleccionados. No cambia contraseñas, claves ni tokens existentes.
La baja de usuario es lógica y conserva historial. Retirar un permiso directo no deniega uno heredado.
Los permisos personalizados necesitan una acción que los consuma para habilitar funciones de aplicación.

1. Verificar SHA256SUMS.txt desde WSL en la carpeta extraída.
2. Copiar instalador y payload:
scp PRODIMT_Instalar_I18.py prodimt-identity-payload.zip root@194.238.26.70:/root/
3. Ejecutar sólo lectura:
ssh root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I18.py --package /root/prodimt-identity-payload.zip'
Esperado: planned/completed, changes_made=false, base_release_verified=true, health_verified=true.
Si falla, resolver la discrepancia sin repetir instaladores anteriores, bootstrap o SQL manual.
4. Resolver guardados pendientes y aplicar con terminal interactiva:
ssh -t root@194.238.26.70 'python3 /root/PRODIMT_Instalar_I18.py --package /root/prodimt-identity-payload.zip --apply'
Solicita credenciales ocultas de administrador SQL existente, no credenciales de la página.
Snapshot privado, COPY_ONLY/CHECKSUM y RESTORE VERIFYONLY antes del DDL.
Config/keys conservadas; reinicia sólo PRODIMT. No restaura SQL automáticamente ante fallo.
Recupera I12 sólo si la revisión y el grafo de seguridad siguen en su estado inicial.
Si hubo cambios I18 o no puede verificarlos, detiene API y reporta recovery_manual_required.
No forzar rollback manual a I12: ese binario daría administración total a usuarios restringidos.
Una repetición ya instalada verifica already_current sin migrar ni volver a conceder roles retirados.

Esperado: updated/completed, migrations_after=14, migrated_schema_verified=true,
existing_data_counts_preserved=true, tokens_preserved=true, security_permissions_verified=true,
health_verified=true, frontend_verified=true, env_preserved=true.
Después: Ctrl+F5 → Seguridad. Revisar Usuarios/Roles/Permisos, crear una cuenta de prueba,
asignar dos roles y un permiso directo; comprobar procedencia y retiro de cada origen.
Comprobar que la última concesión retirada deja de permitir la acción en la siguiente petición.
No eliminar ni desactivar el rol Administrador. No se incluyen recuperación pública, invitaciones o2FA.
La entrega y las pruebas aisladas no acreditan instalación o aceptación de negocio.
"""
    bundle = {'PRODIMT_Instalar_I18.py': script.encode(), 'prodimt-identity-payload.zip': payload,
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
