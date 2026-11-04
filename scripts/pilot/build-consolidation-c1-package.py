#!/usr/bin/env python3
"""Build the pinned, integrated C1 candidate over installed I23 (18 to 19).

Packaging is not deployment evidence. The WSL entry point must pass the
browser gate for this source, and the installer repeats isolated EF/HTTP and
exact-SQL gates before staging or touching production.
"""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import zipfile

ROOT = Path(__file__).resolve().parents[2]
FEATURE = 'consolidation-c1-v1'
BASELINE_SOURCE = 'aa802e210d15c70784f79dea2580709166b843c7'
MIGRATION = '20261104041928_ExpandAuditMetadata'
PREVIOUS_PAIRS = (('ordering-schedules-i23-aa802e210d15', 'ordering-schedules-i23-aa802e210d15'),)
PREFIX = 'prodimt-consolidation-c1-update/'
SOURCE_BRANCH = 'codex/consolidacion-c1'
SUPPORT_PATHS = {
    'inspect-native.py': 'inspect-native.py',
    'native-deployment-support.py': 'install-quick-capture-update.py',
    'commercial-catalog-support.py': 'install-commercial-catalog-update.py',
    'token-management-support.py': 'install-access-token-management-update.py',
    'customer-products-support.py': 'install-customer-products-update.py',
    'security-administration-support.py': 'install-identity-update.py',
    'delivery-support.py': 'install-delivery-update.py',
    'delivery-schema-contract.py': 'delivery-schema-contract.py',
    'ordering-schedules-support.py': 'install-ordering-schedules-on-delivery-update.py',
    'validate-consolidation-c1-sql.py': 'validate-consolidation-c1-sql.py',
}
FRONTEND_MARKERS = (
    b'apiBaseUrl:""', b'/access-tokens', b'/reveal', b'/prices', b'/security',
    b'directPermissionIds', b'roleIds', b'/product-setups', b'/additional-products',
    b'/operations/production', b'requiresMachine', b'/ordering-schedules', b'orderingWindow',
    b'delivery.read.own', b'delivery.manage', b'/delivery', b'/personal/reparto', b'Preparar reparto',
    b'prodimt.production.pending.v1.', b'retrySameOperation',
)
BASELINE_ADDED_COLUMNS = (
    ('Products', 'ExternalCode'), ('Machines', 'ExternalCode'), ('Customers', 'ExternalCode'),
    ('Products', 'UnitCode'), ('Orders', 'CustomerNameSnapshot'), ('Orders', 'HasStoreKey'),
    ('Orders', 'InternalDeliveryInstructions'), ('Orders', 'OrderingProfileVersion'),
    ('Orders', 'PaymentTermCode'), ('Orders', 'RouteCode'), ('Orders', 'RouteStopOrder'), ('Orders', 'Version'),
    ('OrderLines', 'ProductNameSnapshot'), ('OrderLines', 'UnitCode'), ('Customers', 'HasStoreKey'),
    ('Customers', 'InternalDeliveryInstructions'), ('Customers', 'IsOrderProfileApproved'),
    ('Customers', 'OrderingProfileVersion'), ('Customers', 'PaymentTermCode'), ('Customers', 'RouteCode'),
    ('Customers', 'RouteStopOrder'), ('CustomerFrequentProducts', 'InterpretationPosition'),
    ('CustomerFrequentProducts', 'PreferredMachineId'), ('Orders', 'ExternalOrderKey'), ('Orders', 'Source'),
    ('Orders', 'SourcePayloadHash'), ('Customers', 'RestDaysMask'), ('CustomerFrequentProducts', 'CaptureAlias'),
    ('Products', 'MergedIntoProductId'), ('OrderLines', 'CurrencyCodeSnapshot'), ('OrderLines', 'PriceEffectiveFrom'),
    ('OrderLines', 'PriceRevisionId'), ('OrderLines', 'UnitPriceSnapshot'), ('CustomerAccessTokens', 'IsReminderLink'),
    ('CustomerAccessTokens', 'MetadataVersion'), ('CustomerAccessTokens', 'ProtectedToken'),
    ('CustomerFrequentProducts', 'UseCustomerDefaultMachine'), ('AdminUsers', 'IsDeleted'), ('AdminUsers', 'SessionRevision'),
    ('Products', 'RequiresMachine'), ('OrderLines', 'RequiresMachine'), ('CustomerFrequentProducts', 'RequiresMachine'),
    ('Customers', 'AutomaticConfirmationCutoff'), ('Customers', 'OrderingScheduleVersion'),
)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def helper(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def validate_source(source):
    if not re.fullmatch(r'[a-f0-9]{40}', source):
        raise ValueError('source_commit_invalid')
    if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != source:
        raise ValueError('source_commit_not_current')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')
    branch = subprocess.check_output(['git', 'symbolic-ref', '--short', 'HEAD'], cwd=ROOT, text=True).strip()
    if branch != SOURCE_BRANCH:
        raise ValueError('source_branch_unexpected')


def source_bundle(source):
    """Preserve the reviewed source/history without publishing to a remote."""
    actual = subprocess.check_output(['git', 'rev-parse', 'refs/heads/' + SOURCE_BRANCH], cwd=ROOT, text=True).strip()
    if actual != source:
        raise ValueError('bundle_source_not_current')
    with tempfile.TemporaryDirectory(prefix='prodimt-c1-source-') as folder:
        path = Path(folder) / 'PRODIMT_C1.bundle'
        subprocess.run(['git', 'bundle', 'create', str(path), 'refs/heads/' + SOURCE_BRANCH],
                       cwd=ROOT, capture_output=True, check=True)
        heads = subprocess.check_output(['git', 'bundle', 'list-heads', str(path)], cwd=ROOT, text=True).splitlines()
        if heads != [source + ' refs/heads/' + SOURCE_BRANCH]:
            raise ValueError('bundle_heads_unexpected')
        return path.read_bytes()


def archive_bytes(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, raw)
    return stream.getvalue()


def normalize_baseline(sql):
    """Retain EF statements; split reviewed historical DDL/seed batches.

    This fresh UUID baseline is intentionally non-idempotent. I23 needs its
    constraint compiled after the new columns exist. GO preserves its one
    transaction while separating the five reviewed statements. There are no
    application grants here: the validator creates its application user later.
    """
    pilot = ROOT / 'scripts/pilot'
    validator = helper('c1_build_sql_baseline', pilot / 'validate-consolidation-c1-sql.py')
    validator.validate_baseline(sql)
    blocks = list(re.finditer(r'(?ms)^BEGIN TRANSACTION;\n.*?^COMMIT;\nGO\n\n', sql))
    if len(blocks) != 18 or blocks[-1].end() != len(sql):
        raise ValueError('validation_baseline_layout_unexpected')
    i23 = helper('c1_build_i23_contract', pilot / 'install-ordering-schedules-on-delivery-update.py')
    token = helper('c1_build_token_baseline', pilot / 'install-access-token-management-update.py')
    security = helper('c1_build_security_baseline', pilot / 'build-identity-package.py')
    result = sql
    for block in reversed(blocks):
        segment = block.group()
        ids = re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", segment)
        if len(ids) != 1:
            raise ValueError('validation_baseline_increment_unexpected')
        if ids[0] == token.MIGRATION:
            # I12's historical helper accepts only EF's idempotent format.
            # A fresh C1 baseline also supports the actual non-idempotent EF
            # output, with the raw SQL body checked against its source constant.
            if segment.startswith('BEGIN TRANSACTION;\nALTER TABLE '):
                segment = normalize_raw_token_baseline(segment)
            else:
                segment = token.migration_sql_for_sqlcmd(segment)
        elif ids[0] == security.MIGRATION:
            segment = security.migration_sql_for_sqlcmd(segment)
        elif ids[0] == i23.MIGRATION:
            i23.validate_migration_sql(segment)
            body = segment.removeprefix('BEGIN TRANSACTION;\n').removesuffix('COMMIT;\nGO\n\n')
            if re.search(r'(?mi)^\s*GO\s*$', body):
                raise ValueError('validation_schedule_batches_unexpected')
            statements = [value.strip() for value in body.split(';') if value.strip()]
            if len(statements) != 5:
                raise ValueError('validation_schedule_statements_unexpected')
            segment = 'BEGIN TRANSACTION;\n' + ''.join(value + ';\nGO\n\n' for value in statements) + 'COMMIT;\nGO\n\n'
        result = result[:block.start()] + segment + result[block.end():]
    # SQL Server compiles filtered indexes and CHECK expressions against the
    # schema at batch entry. Fresh EF scripts put new columns and those
    # references together. Split only reviewed complete ADD-column statements,
    # never a constraint, an IF guard body or a DECLARE variable operation.
    result = split_raw_column_additions(result)
    validator.validate_baseline(result)
    return result


def split_raw_column_additions(sql):
    pattern = r'ALTER TABLE \[([^]]+)\] ADD \[([^]]+)\] [^;\n@]+;\n'
    additions = re.findall(r'(?m)^ *' + pattern, sql)
    if tuple(additions) != BASELINE_ADDED_COLUMNS:
        raise ValueError('validation_baseline_columns_unexpected')
    def boundary(match):
        # Already separated by a reviewed special-case normalizer above.
        if re.match(r'\n*GO(?:\n|$)', sql[match.end():]):
            return match.group()
        return match.group() + 'GO\n\n'
    return re.sub(r'(?m)^' + pattern, boundary, sql)


def normalize_raw_token_baseline(sql):
    """Split only the reviewed I12 raw EF shape, retaining its atomic SQL."""
    migration = '20261101190000_AddRecoverableCustomerAccessTokens'
    additions = (
        'ALTER TABLE [CustomerAccessTokens] ADD [IsReminderLink] bit NOT NULL DEFAULT CAST(0 AS bit);',
        'ALTER TABLE [CustomerAccessTokens] ADD [MetadataVersion] int NOT NULL DEFAULT 1;',
        'ALTER TABLE [CustomerAccessTokens] ADD [ProtectedToken] nvarchar(1024) NULL;',
    )
    source = (ROOT / ('src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations/' + migration + '.cs')).read_text(encoding='utf-8-sig')
    constants = re.findall(r'public const string RevokeLegacySql = """\n(.*?)\n\s+""";', source, re.S)
    if len(constants) != 1:
        raise ValueError('validation_token_source_constant_unexpected')
    revocation = textwrap.dedent(constants[0])
    prefix = 'BEGIN TRANSACTION;\n' + '\n\n'.join(additions) + '\n\n'
    history = ("INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])\n"
               "VALUES (N'" + migration + "', N'10.0.8');\n\nCOMMIT;\nGO\n\n")
    if sql != prefix + revocation + '\n\n' + history:
        raise ValueError('validation_raw_token_layout_unexpected')
    return prefix + 'GO\n\n' + revocation + '\n\n' + history


def collect_runtime(files, part, directory):
    if not directory.is_dir() or directory.is_symlink():
        raise ValueError('build_directory_invalid')
    settings = {name: (ROOT / 'src/Prodimt.Pedidos.Api' / name).read_bytes()
                for name in ('appsettings.json', 'appsettings.Production.json')}
    for path in sorted(directory.rglob('*')):
        if path.is_symlink():
            raise ValueError('build_symlink_not_allowed')
        if not path.is_file():
            continue
        relative = path.relative_to(directory)
        name = relative.as_posix()
        if any(segment.startswith('.') for segment in relative.parts) or path.suffix.lower() in (
                '.env', '.key', '.pem', '.pfx', '.p12', '.bak', '.cs', '.ts', '.xlsx', '.csv', '.log', '.dmp', '.trace'):
            raise ValueError('build_private_or_source_file_not_allowed')
        if path.suffix.lower() in ('.pdb', '.map') or name == 'appsettings.Development.json':
            continue
        raw = path.read_bytes()
        if part in ('backend', 'validation') and path.name.lower().startswith('appsettings') and path.suffix.lower() == '.json':
            if name not in settings or raw != settings[name]:
                raise ValueError('runtime_settings_not_source_template')
        files[part + '/' + name] = raw


def build(backend, frontend, validation, migration, rollback, baseline_migration, output, source):
    validate_source(source)
    pilot = ROOT / 'scripts/pilot'
    installer = helper('c1_build_installer', pilot / 'install-consolidation-c1.py')
    validator = helper('c1_build_sql', pilot / 'validate-consolidation-c1-sql.py')
    up, down = migration.read_text(encoding='utf-8-sig'), rollback.read_text(encoding='utf-8-sig')
    validator.validate_migration(up)
    validator.validate_rollback(down)
    baseline = normalize_baseline(baseline_migration.read_text(encoding='utf-8-sig'))
    schema = helper('c1_build_delivery_schema', pilot / 'delivery-schema-contract.py')
    contract = schema.contract_from_sql((ROOT / 'tests/pilot/fixtures/i22-delivery-migration.sql').read_text())
    if set(SUPPORT_PATHS) != set(installer.SUPPORT_FILES):
        raise ValueError('installer_support_contract_mismatch')
    files = {name: (pilot / path).read_bytes() for name, path in SUPPORT_PATHS.items()}
    files.update({'migrations-c1.sql': up.encode(), 'rollback-c1.sql': down.encode(),
                  'validation-baseline.sql': baseline.encode(),
                  'delivery-schema.json': json.dumps(contract, sort_keys=True).encode()})
    for part, directory in (('backend', backend), ('frontend', frontend), ('validation', validation)):
        collect_runtime(files, part, directory)
    required = set(installer.VALIDATION_REQUIRED) | {
        'backend/Prodimt.Pedidos.Api.dll', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
        'backend/appsettings.json', 'frontend/index.html', 'frontend/3rdpartylicenses.txt'}
    if not required <= files.keys():
        raise ValueError('required_build_files_missing')
    if any('backend/' + name not in files or 'validation/' + name not in files
           or files['backend/' + name] != files['validation/' + name] for name in installer.VALIDATED_ASSEMBLIES):
        raise ValueError('validation_application_binary_mismatch')
    scripts = b''.join(raw for name, raw in files.items() if name.startswith('frontend/') and name.endswith('.js'))
    if any(marker not in scripts for marker in FRONTEND_MARKERS):
        raise ValueError('frontend_feature_or_same_origin_missing')
    manifest = dict(feature=FEATURE, sourceCommit=source, baselineSource=BASELINE_SOURCE,
                    migration=MIGRATION, migrationsBefore=18, migrationsAfter=19,
                    migrationIds=validator.BASE_IDS + [MIGRATION],
                    previousPairs=[list(pair) for pair in PREVIOUS_PAIRS],
                    revokeTokens=False, encryptionKeyInPackage=False,
                    files={name: dict(bytes=len(raw), sha256=digest(raw)) for name, raw in files.items()})
    payload = archive_bytes(files | {'manifest.json': json.dumps(manifest, sort_keys=True).encode()})
    template = (pilot / 'install-consolidation-c1.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_invalid')
    script = template.replace('REPLACE_SOURCE_COMMIT', source).replace('REPLACE_PAYLOAD_SHA256', digest(payload))
    bundle = {
        'PRODIMT_Instalar_C1.py': script.encode(),
        'prodimt-consolidation-c1-payload.zip': payload,
        'PRODIMT_Desplegar_C1_WSL.sh': (pilot / 'deploy-consolidation-c1-from-wsl.sh').read_bytes(),
        'PRODIMT_C1.bundle': source_bundle(source),
        'source.json': json.dumps(dict(sourceCommit=source, baselineSource=BASELINE_SOURCE,
                                       migration=MIGRATION, migrationsBefore=18, migrationsAfter=19,
                                       deployed=False), sort_keys=True, indent=2).encode(),
        'LEEME.txt': README.encode(),
    }
    bundle['SHA256SUMS.txt'] = ''.join(f'{digest(raw)}  {name}\n' for name, raw in sorted(bundle.items())).encode()
    # Re-check after collecting to avoid packaging a source changed during the build.
    validate_source(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(archive_bytes({PREFIX + name: raw for name, raw in bundle.items()}))
    return dict(sourceCommit=source, baselineSource=BASELINE_SOURCE,
                sha256=digest(output.read_bytes()), payloadSha256=digest(payload),
                bytes=output.stat().st_size, files=len(files), migration=MIGRATION,
                deployed=False, browserGateRequired=True, isolatedSqlGateRequired=True)


README = """PRODIMT — C1: instalación integrada sobre I23 confirmado

Este es un candidato sujeto a validación, no evidencia de despliegue.
Base exacta backend/frontend ordering-schedules-i23-aa802e210d15 y 18 migraciones.
Incluye las correcciones C1, Reparto/RF1 y Horarios I23; amplía sólo la columna
AuditLogs.MetadataJson mediante ExpandAuditMetadata (migración 19).

Desde WSL, verificar SHA256SUMS.txt y ejecutar:
  bash PRODIMT_Desplegar_C1_WSL.sh

La preparación recupera la fuente exacta del bundle y exige pruebas de navegador
con API simulada del mismo commit antes de transferir/instalar. Estas pruebas no
sustituyen los siete casos EF/HTTP con SQL Server ni el gate SQL exacto, que el
instalador ejecuta en bases temporales UUID antes de detener PRODIMT.
No usar instaladores históricos ni ejecutar los archivos SQL manualmente.

El instalador pide las credenciales SQL de forma oculta. Verifica respaldo
COPY_ONLY/CHECKSUM y RESTORE VERIFYONLY antes de cualquier DDL operativo.
Conserva cuentas, concesiones, pedidos, precios, tokens, claves y configuración.
Si otra entrega cambió el release, se detiene sin forzar su instalación.
Si falla después de DDL, repetir el MISMO paquete para recuperación hacia delante;
no hay restauración automática ni downgrade que pueda truncar evidencia.

El cierre esperado es updated/completed, 19 migraciones, salud/backend/frontend
verificados y recovery_pending=false. Una repetición saludable es already_current.
Instalación técnica y aceptación operativa del recorrido real son evidencias distintas.
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('backend', 'frontend', 'validation', 'migration', 'rollback', 'baseline-migration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.backend, args.frontend, args.validation, args.migration, args.rollback,
                           args.baseline_migration, args.output, args.source), indent=2))


if __name__ == '__main__':
    main()
