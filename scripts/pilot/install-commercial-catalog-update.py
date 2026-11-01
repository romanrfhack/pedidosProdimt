#!/usr/bin/env python3
"""Install I11 on the pinned native I07/I08 or I09, with verified backup before 11-to-12 migration.

No automatic product consolidation, price assignment, credential reset or SQL restore.
The exact reviewed I07 deployment support is protected by the payload manifest.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import signal
import stat
import sys
import types
import uuid
import zipfile

REVISION = '20261101-commercial-catalog-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
I07 = 'capture-46fed604732e'
I09 = 'customer-access-6cfdc7ff366a'
I10_FRONTEND = 'token-modal-89b51ddc3308'
I08_FRONTENDS = ('client-config-42f6eb4c225a', 'client-config-be3995fdce69')
MIGRATION = '20261101154618_AddSharedProductsAndCustomerPrices'
APPLICATION_USER = 'prodimt_pedidos_app'

PERMISSION_QUERY = """SELECT DB_NAME() AS database_name, USER_NAME() AS application_user,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS migrations_count,
 (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261028202031_AddCustomerCaptureAliases') AS i07,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'Customer' AND IsInternal=0) AS customer_channel,
 (SELECT COUNT(*) FROM dbo.SalesChannels WHERE Type=N'AdminManualCapture' AND IsInternal=1) AS admin_channel,
 IS_ROLEMEMBER(N'db_owner') AS app_is_db_owner, IS_SRVROLEMEMBER(N'sysadmin') AS app_is_sysadmin,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'SELECT') AS read_customers,
 HAS_PERMS_BY_NAME(N'dbo.Customers',N'OBJECT',N'UPDATE') AS update_customers,
 HAS_PERMS_BY_NAME(N'dbo.Products',N'OBJECT',N'SELECT') AS read_products,
 HAS_PERMS_BY_NAME(N'dbo.Products',N'OBJECT',N'UPDATE') AS update_products,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'SELECT') AS read_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'INSERT') AS insert_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerFrequentProducts',N'OBJECT',N'DELETE') AS delete_profiles,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'SELECT') AS read_tokens,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'INSERT') AS insert_tokens,
 HAS_PERMS_BY_NAME(N'dbo.CustomerAccessTokens',N'OBJECT',N'UPDATE') AS update_tokens,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'SELECT') AS read_operations,
 HAS_PERMS_BY_NAME(N'dbo.OrderOperations',N'OBJECT',N'INSERT') AS insert_operations,
 HAS_PERMS_BY_NAME(N'dbo.Orders',N'OBJECT',N'SELECT') AS read_orders,
 HAS_PERMS_BY_NAME(N'dbo.Orders',N'OBJECT',N'INSERT') AS insert_orders,
 HAS_PERMS_BY_NAME(N'dbo.Orders',N'OBJECT',N'UPDATE') AS update_orders,
 HAS_PERMS_BY_NAME(N'dbo.OrderLines',N'OBJECT',N'SELECT') AS read_lines,
 HAS_PERMS_BY_NAME(N'dbo.OrderLines',N'OBJECT',N'INSERT') AS insert_lines,
 HAS_PERMS_BY_NAME(N'dbo.OrderLines',N'OBJECT',N'UPDATE') AS update_lines,
 HAS_PERMS_BY_NAME(N'dbo.OrderAuditLogs',N'OBJECT',N'INSERT') AS insert_order_audit,
 HAS_PERMS_BY_NAME(N'dbo.AuditLogs',N'OBJECT',N'INSERT') AS insert_audit
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_PERMISSIONS = dict(database_name='ProdimtPedidos', application_user=APPLICATION_USER,
    i07=1, customer_channel=1, admin_channel=1, app_is_db_owner=0, app_is_sysadmin=0,
    **{key: 1 for key in ('read_customers', 'update_customers', 'read_products', 'update_products',
        'read_profiles', 'insert_profiles', 'delete_profiles', 'read_tokens', 'insert_tokens', 'update_tokens',
        'read_operations', 'insert_operations', 'read_orders', 'insert_orders', 'update_orders',
        'read_lines', 'insert_lines', 'update_lines', 'insert_order_audit', 'insert_audit')})
PRICE_PERMISSIONS = """SELECT
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceBooks',N'OBJECT',N'SELECT') AS read_books,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceBooks',N'OBJECT',N'INSERT') AS insert_books,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceBooks',N'OBJECT',N'UPDATE') AS update_books,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceBooks',N'OBJECT',N'DELETE') AS delete_books,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceRevisions',N'OBJECT',N'SELECT') AS read_revisions,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceRevisions',N'OBJECT',N'INSERT') AS insert_revisions,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceRevisions',N'OBJECT',N'UPDATE') AS update_revisions,
 HAS_PERMS_BY_NAME(N'dbo.CustomerProductPriceRevisions',N'OBJECT',N'DELETE') AS delete_revisions,
 HAS_PERMS_BY_NAME(N'dbo.ProductConsolidationOperations',N'OBJECT',N'SELECT') AS read_consolidations,
 HAS_PERMS_BY_NAME(N'dbo.ProductConsolidationOperations',N'OBJECT',N'INSERT') AS insert_consolidations,
 HAS_PERMS_BY_NAME(N'dbo.ProductConsolidationOperations',N'OBJECT',N'UPDATE') AS update_consolidations,
 HAS_PERMS_BY_NAME(N'dbo.ProductConsolidationOperations',N'OBJECT',N'DELETE') AS delete_consolidations
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_PRICE_PERMISSIONS = {key: (0 if key in ('delete_books', 'update_revisions', 'delete_revisions',
    'update_consolidations', 'delete_consolidations') else 1) for key in ('read_books', 'insert_books',
    'update_books', 'delete_books', 'read_revisions', 'insert_revisions', 'update_revisions', 'delete_revisions',
    'read_consolidations', 'insert_consolidations', 'update_consolidations', 'delete_consolidations')}
PRICE_COUNTS = """SELECT
 (SELECT COUNT_BIG(*) FROM dbo.CustomerProductPriceBooks) AS books,
 (SELECT COUNT_BIG(*) FROM dbo.CustomerProductPriceRevisions) AS revisions,
 (SELECT COUNT_BIG(*) FROM dbo.ProductConsolidationOperations) AS consolidations,
 (SELECT COUNT_BIG(*) FROM dbo.OrderLines WHERE UnitPriceSnapshot IS NOT NULL OR PriceRevisionId IS NOT NULL) AS priced_lines
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
PRICE_GRANTS = """GRANT SELECT, INSERT, UPDATE ON dbo.CustomerProductPriceBooks TO [prodimt_pedidos_app];
DENY DELETE ON dbo.CustomerProductPriceBooks TO [prodimt_pedidos_app];
GRANT SELECT, INSERT ON dbo.CustomerProductPriceRevisions TO [prodimt_pedidos_app];
DENY UPDATE, DELETE ON dbo.CustomerProductPriceRevisions TO [prodimt_pedidos_app];
GRANT SELECT, INSERT ON dbo.ProductConsolidationOperations TO [prodimt_pedidos_app];
DENY UPDATE, DELETE ON dbo.ProductConsolidationOperations TO [prodimt_pedidos_app];
"""

class InstallError(Exception):
    pass

def require(condition, issue):
    if not condition: raise InstallError(issue)

def sha(raw): return hashlib.sha256(raw).hexdigest()

def regular(path, limit=40*1024*1024):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid in (0, 33) and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'file_identity_unexpected')
        data = stream.read(limit+1)
    require(len(data) <= limit, 'file_too_large')
    return data

def safe_name(name):
    pure = PurePosixPath(name)
    return bool(pure.parts) and pure.as_posix() == name and not pure.is_absolute() and name not in ('', '.') \
        and not any(x in ('', '.', '..') for x in pure.parts) and ':' not in name and '\\' not in name

def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(path) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 250 and len({r.filename for r in rows}) == len(rows)
                and sum(r.file_size for r in rows) <= 100*1024*1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(r.filename) and not r.is_dir() and not r.flag_bits & 1
                    and not stat.S_ISLNK(r.external_attr >> 16) for r in rows), 'payload_path_invalid')
        files = {r.filename: archive.read(r) for r in rows}
    manifest = json.loads(files.pop('manifest.json'))
    required = {'inspect-native.py', 'native-deployment-support.py', 'migrations-i11.sql',
                'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'shared-products-customer-prices-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
        and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 11 and manifest.get('migrationsAfter') == 12
        and set(manifest.get('files', {})) == set(files) and required <= set(files)
        and all(name in required or name.startswith(('backend/', 'frontend/')) for name in files), 'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    sql = files['migrations-i11.sql'].decode('utf-8-sig')
    require(MIGRATION in sql and 'CustomerProductPriceRevisions' in sql
        and not re.search(r'(?im)^\s*(DELETE|UPDATE|TRUNCATE|DROP)\s', sql), 'incremental_sql_unexpected')
    return files

def permission_mismatches(data, allowed=(11, 12)):
    values = data if isinstance(data, dict) else {}
    errors = [name for name, expected in EXPECTED_PERMISSIONS.items() if values.get(name) != expected]
    if values.get('migrations_count') not in allowed: errors.append('migrations_count')
    return errors

def schema_query():
    columns = [
        ('Products','MergedIntoProductId','uniqueidentifier',16,1,0,0),
        ('OrderLines','UnitPriceSnapshot','decimal',9,1,18,4), ('OrderLines','CurrencyCodeSnapshot','nvarchar',6,1,0,0),
        ('OrderLines','PriceRevisionId','uniqueidentifier',16,1,0,0), ('OrderLines','PriceEffectiveFrom','date',3,1,0,0),
        ('CustomerProductPriceBooks','Id','uniqueidentifier',16,0,0,0), ('CustomerProductPriceBooks','CustomerId','uniqueidentifier',16,0,0,0),
        ('CustomerProductPriceBooks','ProductId','uniqueidentifier',16,0,0,0), ('CustomerProductPriceBooks','Version','int',4,0,10,0),
        ('CustomerProductPriceRevisions','Id','uniqueidentifier',16,0,0,0), ('CustomerProductPriceRevisions','PriceBookId','uniqueidentifier',16,0,0,0),
        ('CustomerProductPriceRevisions','Version','int',4,0,10,0), ('CustomerProductPriceRevisions','UnitPrice','decimal',9,1,18,4),
        ('CustomerProductPriceRevisions','CurrencyCode','nvarchar',6,0,0,0), ('CustomerProductPriceRevisions','UnitCode','nvarchar',32,0,0,0),
        ('CustomerProductPriceRevisions','ProductNameSnapshot','nvarchar',400,0,0,0), ('CustomerProductPriceRevisions','EffectiveFrom','date',3,0,0,0),
        ('CustomerProductPriceRevisions','RecordedAt','datetimeoffset',10,0,34,7), ('CustomerProductPriceRevisions','ActorId','nvarchar',400,0,0,0),
        ('CustomerProductPriceRevisions','ActorDisplayName','nvarchar',400,1,0,0), ('CustomerProductPriceRevisions','Reason','nvarchar',1000,0,0,0),
        ('CustomerProductPriceRevisions','OperationId','uniqueidentifier',16,0,0,0), ('CustomerProductPriceRevisions','PayloadHash','nchar',128,0,0,0),
        ('ProductConsolidationOperations','Id','uniqueidentifier',16,0,0,0), ('ProductConsolidationOperations','PayloadHash','nchar',128,0,0,0),
        ('ProductConsolidationOperations','CompletedAt','datetimeoffset',10,0,34,7), ('ProductConsolidationOperations','ResultJson','nvarchar',-1,0,0,0)]
    values = ','.join("(N'%s',N'%s',N'%s',%d,%d,%d,%d)" % row for row in columns)
    # SQL Server reports non-numeric/date precision differently across compatible releases;
    # precision/scale matter for decimals and datetimeoffset, not for textual/GUID columns.
    return """SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE name IN (N'CustomerProductPriceBooks',N'CustomerProductPriceRevisions',N'ProductConsolidationOperations') AND schema_id=SCHEMA_ID(N'dbo')) AS tables_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerProductPriceBooks')) AS book_columns,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.CustomerProductPriceRevisions')) AS revision_columns,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductConsolidationOperations')) AS operation_columns,
 (SELECT COUNT(*) FROM (VALUES """ + values + """) e(t,n,k,l,z,p,s)
  JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.t) AND c.name=e.n
  WHERE TYPE_NAME(c.system_type_id)=e.k AND c.max_length=e.l AND c.is_nullable=e.z
   AND (e.k NOT IN (N'decimal',N'datetimeoffset') OR (c.precision=e.p AND c.scale=e.s))) AS valid_columns,
 (SELECT COUNT(*) FROM sys.indexes WHERE is_disabled=0 AND is_unique=1 AND
   ((object_id=OBJECT_ID(N'dbo.CustomerProductPriceBooks') AND name=N'IX_CustomerProductPriceBooks_CustomerId_ProductId')
    OR (object_id=OBJECT_ID(N'dbo.CustomerProductPriceRevisions') AND name IN (N'IX_CustomerProductPriceRevisions_OperationId',N'IX_CustomerProductPriceRevisions_PriceBookId_Version')))) AS unique_indexes,
 (SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
  JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE i.object_id=OBJECT_ID(N'dbo.CustomerProductPriceRevisions') AND i.name=N'IX_CustomerProductPriceRevisions_PriceBookId_EffectiveFrom_Version' AND i.is_disabled=0
   AND ((ic.key_ordinal=1 AND c.name=N'PriceBookId' AND ic.is_descending_key=0)
    OR (ic.key_ordinal=2 AND c.name=N'EffectiveFrom' AND ic.is_descending_key=1)
    OR (ic.key_ordinal=3 AND c.name=N'Version' AND ic.is_descending_key=1))) AS effective_index_columns,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0 AND
   ((parent_object_id=OBJECT_ID(N'dbo.Products') AND name=N'CK_Products_Consolidated')
    OR (parent_object_id=OBJECT_ID(N'dbo.CustomerProductPriceRevisions') AND name=N'CK_PriceRevision_Amount'))) AS trusted_checks,
 (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0 AND delete_referential_action=0
  AND name IN (N'FK_Products_Products_MergedIntoProductId',N'FK_OrderLines_CustomerProductPriceRevisions_PriceRevisionId',
   N'FK_CustomerProductPriceBooks_Customers_CustomerId',N'FK_CustomerProductPriceBooks_Products_ProductId',N'FK_CustomerProductPriceRevisions_CustomerProductPriceBooks_PriceBookId')) AS trusted_foreign_keys
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""

EXPECTED_SCHEMA = dict(tables_count=3, book_columns=4, revision_columns=14, operation_columns=4,
    valid_columns=27, unique_indexes=3, effective_index_columns=3, trusted_checks=2, trusted_foreign_keys=5)
NEW_SCHEMA = schema_query()

def make_installer(package):
    files = load_payload(package)
    runtime = types.ModuleType('reviewed_i11_deployment_support')
    exec(compile(files['native-deployment-support.py'], 'native-deployment-support.py', 'exec'), runtime.__dict__)
    runtime.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    migrations = runtime.MIGRATIONS + [MIGRATION]

    class CommercialInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package = files, package
            self.old = {}
            self.new = {part: ROOT/part/'releases'/('prices-'+EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, products_consolidated=False, prices_preloaded=False)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent/('.commercial-stage-'+uuid.uuid4().hex)
                temporary.mkdir(mode=0o700)
                try:
                    for name, raw in self.expected(part).items():
                        path = temporary/name
                        path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                        override = part+'/'+name in self.overrides
                        fd = os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o640 if override else 0o644)
                        with os.fdopen(fd,'wb') as stream:
                            stream.write(raw)
                        os.chown(path,0,33 if override else 0)
                        path.chmod(0o640 if override else 0o644)
                    temporary.chmod(0o755)
                    temporary.rename(target)
                finally:
                    if temporary.exists():
                        runtime.shutil.rmtree(temporary)
                self.verify_stage(part)

        def preflight(self):
            for path in (ROOT, BACKUPS, *(ROOT/part/'releases' for part in self.new)):
                runtime.directory_safe(path)
            for part in self.new:
                link = ROOT/part/'current'
                require(link.is_symlink(), 'current_release_link_required')
                self.old[part] = link.resolve(strict=True)
            own = self.old == self.new
            require(own or (self.old['backend'].name == I07 and self.old['frontend'].name in (I07, *I08_FRONTENDS))
                or (self.old['backend'].name == I09 and self.old['frontend'].name in (I09, I10_FRONTEND)), 'previous_release_not_supported')
            require(all(path.parent == ROOT/part/'releases' for part, path in self.old.items()), 'previous_release_path_unexpected')
            pid, cwd, argv = self.process(self.old['backend'])
            self.original_dll = regular(cwd/'Prodimt.Pedidos.Api.dll')
            self.environment = regular(ENV_FILE)
            inspector = types.ModuleType('reviewed_i11_inventory')
            exec(compile(files['inspect-native.py'], 'inspect-native.py', 'exec'), inspector.__dict__)
            configuration, source = inspector.configuration(inspector.process_environment(pid), argv, cwd)
            require(source['environment'] == 'Production' and inspector.policy_flags(configuration) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False, 'https_redirection_enabled': True},
                'production_configuration_unexpected')
            self.fields = inspector.connection_fields(inspector.first(configuration, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(self.fields), 'sql_target_unexpected')
            self.inspector = inspector
            self.sql_tool = next((tool for tool in inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool, 'sqlcmd_required')
            self.app_checks = inspector.query_sql(self.sql_tool, self.fields, PERMISSION_QUERY)
            mismatch = permission_mismatches(self.app_checks, (12,) if own else (11,12))
            self.state['database_checks'] = {key: value for key,value in self.app_checks.items()
                if key not in ('database_name','application_user')}
            self.state['database_mismatches'] = mismatch
            require(not mismatch, 'database_permissions_schema_or_channels_unexpected')
            self.overrides = {}
            for name in ('appsettings.json','appsettings.Production.json'):
                path = self.old['backend']/name
                if path.exists(): self.overrides['backend/'+name] = regular(path)
            require(runtime.shutil.disk_usage(ROOT).free >= 2*sum(map(len,files.values()))+100*1024*1024, 'disk_space_insufficient')
            require(self.http('/health/db')[0] == 200 and json.loads(self.http('/health/db')[1]).get('database') == 'reachable', 'previous_health_failed')
            require(self.http('/admin/catalogos') == (200,regular(self.old['frontend']/'index.html')), 'previous_frontend_not_served')
            self.state['env_preserved'] = True
            if own:
                self.verify_stage('backend'); self.verify_stage('frontend')
                require(inspector.query_sql(self.sql_tool,self.fields,NEW_SCHEMA) == EXPECTED_SCHEMA, 'commercial_schema_unexpected')
                require(inspector.query_sql(self.sql_tool,self.fields,PRICE_PERMISSIONS) == EXPECTED_PRICE_PERMISSIONS, 'price_history_permissions_unexpected')
                self.health(True)
                self.state.update(status='already_current',phase='completed',health_verified=True,frontend_verified=True)

        def verify_schema(self):
            rows = self.sql_json(self.inspector.SQL_MIGRATIONS)
            require(rows in ([{'MigrationId': value} for value in migrations[:-1]], [{'MigrationId': value} for value in migrations]), 'migration_history_unexpected')
            count = len(rows)
            require(self.sql_json(runtime.SCHEMA_QUERY) == dict(columns_count=1,valid_column=1,checks_count=1,valid_check=1), 'rest_days_schema_unexpected')
            require(self.sql_json(runtime.ALIAS_SCHEMA_QUERY) == dict(columns_count=1,valid_column=1,indexes_count=1,valid_index=1), 'capture_alias_schema_unexpected')
            require(self.sql_json('SELECT COUNT_BIG(*) AS invalid FROM dbo.Customers WHERE RestDaysMask NOT BETWEEN 0 AND 127 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;') == {'invalid': 0}, 'rest_days_data_invalid')
            require(self.sql_json("SELECT COUNT_BIG(*) AS invalid FROM dbo.CustomerFrequentProducts WHERE CaptureAlias IS NOT NULL AND (LEN(CaptureAlias) NOT BETWEEN 1 AND 16 OR CaptureAlias COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Z]%') FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;") == {'invalid': 0}, 'capture_alias_data_invalid')
            if count == 12:
                data = self.sql_json(NEW_SCHEMA)
                self.state['commercial_schema_checks'] = data
                require(data == EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            else:
                require(self.sql_json("SELECT (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (N'CustomerProductPriceBooks',N'CustomerProductPriceRevisions',N'ProductConsolidationOperations')) AS tables_count, (SELECT COUNT(*) FROM sys.columns WHERE (object_id=OBJECT_ID(N'dbo.Products') AND name=N'MergedIntoProductId') OR (object_id=OBJECT_ID(N'dbo.OrderLines') AND name IN (N'UnitPriceSnapshot',N'CurrencyCodeSnapshot',N'PriceRevisionId',N'PriceEffectiveFrom'))) AS new_columns FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;") == dict(tables_count=0,new_columns=0), 'partial_commercial_schema_unexpected')
            return count

        def database_backup(self):
            folder = SQL_BACKUPS/('prodimt-i11-'+self.snapshot.name.removeprefix('commercial-catalog-update-'))
            folder.mkdir(mode=0o700); os.chown(folder,999,988); folder.chmod(0o700)
            path = folder/'ProdimtPedidos-copy-only.bak'
            self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'"+str(path)+"' WITH COPY_ONLY,CHECKSUM;",timeout=300)
            meta = path.lstat()
            require(stat.S_ISREG(meta.st_mode) and meta.st_nlink==1 and meta.st_uid==999 and meta.st_gid==988
                and path.resolve(strict=True)==path and meta.st_size>0, 'database_backup_file_unexpected')
            path.chmod(0o600)
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'"+str(path)+"' WITH CHECKSUM;",timeout=300)
            self.state['verified_database_backup'] = str(path)
            record=self.snapshot/'database-backup.json'; record.write_text(json.dumps(dict(verified_backup=str(path)))); record.chmod(0o600)

        def backup_and_migrate(self):
            before=self.verify_schema()
            require(before==self.state['migrations_before'], 'database_changed_during_staging')
            counts=self.sql_json(runtime.COUNTS_QUERY)
            require(isinstance(counts,dict) and set(counts)=={'customers','orders','lines','administrators','audits','order_audits','profiles','products','machines'}
                and all(type(value) is int and value>=0 for value in counts.values()), 'database_counts_unrecognized')
            monetary=self.sql_json(PRICE_COUNTS) if before==12 else dict(books=0,revisions=0,consolidations=0,priced_lines=0)
            require(isinstance(monetary,dict) and set(monetary)=={'books','revisions','consolidations','priced_lines'}
                and all(type(value) is int and value>=0 for value in monetary.values()), 'commercial_counts_unrecognized')
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            if before==11:
                self.state.update(database_migrations_run=True,migrations_may_have_changed=True)
                self.sql_run(files['migrations-i11.sql'].decode('utf-8-sig'),timeout=180)
            require(self.verify_schema()==12, 'migrated_schema_unexpected')
            self.sql_run(PRICE_GRANTS)
            require(self.sql_json(runtime.COUNTS_QUERY)==counts and self.sql_json(PRICE_COUNTS)==monetary, 'existing_data_counts_changed')
            require(not permission_mismatches(self.inspector.query_sql(self.sql_tool,self.fields,PERMISSION_QUERY),(12,)), 'application_permissions_changed')
            permissions=self.inspector.query_sql(self.sql_tool,self.fields,PRICE_PERMISSIONS)
            self.state['price_permissions']=permissions
            require(permissions==EXPECTED_PRICE_PERMISSIONS, 'price_history_permissions_unexpected')
            self.state.update(migrations_after=12,migrated_schema_verified=True,existing_data_counts_preserved=True,price_history_append_only_verified=True)

        def health(self,new):
            super().health(new)
            if new:
                for path in ('/api/admin/products/consolidation/candidates','/api/admin/customers/00000000-0000-0000-0000-000000000000/prices',
                    '/api/customer-orders/00000000-0000-0000-0000-000000000000/next'):
                    require(self.http(path)[0]==401, 'commercial_or_customer_endpoint_not_protected')
                self.state['frontend_verified']=True

        def apply(self):
            self.preflight()
            if self.state['status']=='already_current': return
            self.state['phase']='sql-admin-authentication'
            self.authenticate_admin()
            self.state['phase']='private-snapshot-and-stage'
            self.snapshot=BACKUPS/('commercial-catalog-update-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:12])
            self.snapshot.mkdir(mode=0o700)
            (self.snapshot/'api.env').write_bytes(self.environment)
            (self.snapshot/'releases.json').write_text(json.dumps({part:str(path) for part,path in self.old.items()}))
            for path in self.snapshot.iterdir():path.chmod(0o600)
            for name,raw in self.overrides.items():
                path=self.snapshot/name.split('/',1)[1];path.write_bytes(raw);path.chmod(0o600)
            self.state.update(changes_made=True,private_snapshot=str(self.snapshot))
            self.stage()
            self.links(self.old);self.process(self.old['backend'])
            require(regular(ENV_FILE)==self.environment and regular(self.old['backend']/'Prodimt.Pedidos.Api.dll')==self.original_dll
                and all(regular(self.old['backend']/name.split('/',1)[1])==raw for name,raw in self.overrides.items()), 'configuration_or_backend_changed_during_staging')
            self.state['phase']='stop-prodimt-api';self.stop_requested=True
            runtime.command(['systemctl','stop',SERVICE],timeout=45)
            self.state['phase']='database-backup-and-migration';self.backup_and_migrate()
            self.state['phase']='activate-releases';self.switch(self.new)
            runtime.command(['systemctl','start',SERVICE],timeout=45);self.state['service_restarted']=True
            self.state['phase']='verify-application';self.health(True)
            self.state.update(status='updated',phase='completed',health_verified=True,source_commit=EXPECTED_SOURCE,
                new_releases={part:str(path) for part,path in self.new.items()})

        def rollback(self):
            if self.stop_requested:
                super().rollback();self.state['service_restarted']=True

    return CommercialInstaller()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package',type=Path,required=True)
    parser.add_argument('--apply',action='store_true')
    args=parser.parse_args()
    require(os.geteuid()==0,'root_required')
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    installer=make_installer(args.package)
    installer.state['mode']='apply' if args.apply else 'read-only'
    fd=os.open(LOCK,os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW,0o600)
    previous={}
    try:
        meta=os.fstat(fd)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid==0 and meta.st_nlink==1 and meta.st_mode&0o7777==0o600,'lock_identity_unexpected')
        fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
        def interrupted(number,frame): raise InstallError('installation_interrupted')
        for number in (signal.SIGINT,signal.SIGTERM,signal.SIGHUP):previous[number]=signal.signal(number,interrupted)
        try:
            if args.apply:installer.apply()
            else:
                installer.preflight()
                if installer.state['status']!='already_current':installer.state['phase']='completed'
        except (Exception,KeyboardInterrupt) as error:
            installer.state.update(status='failed',issue=str(error) if isinstance(error,InstallError) else 'local_operation_failed')
            for number in previous:signal.signal(number,signal.SIG_IGN)
            try:installer.rollback()
            except (Exception,KeyboardInterrupt):installer.state['rollback_failed']=True
        print(json.dumps(installer.state,ensure_ascii=False,indent=2))
        return 0 if installer.state['status'] in ('planned','updated','already_current') else 1
    finally:
        for number,handler in previous.items():signal.signal(number,handler)
        os.close(fd)

if __name__=='__main__':
    os.umask(0o022)
    try:sys.exit(main())
    except (Exception,KeyboardInterrupt):
        print(json.dumps(dict(revision=REVISION,status='failed',issue='installation_not_started',secret_values_printed=False)))
        sys.exit(1)
