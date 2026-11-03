#!/usr/bin/env python3
"""Install I23 ordering schedules over the installed I22 delivery release.

Read-only unless --apply is supplied. Verified SQL backup precedes all DDL;
exact migration identities, environment and token keys remain guarded.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import signal
import shutil
import stat
import sys
import types
import uuid
import zipfile

REVISION = '20261103-ordering-schedules-on-delivery-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
SQL_BACKUPS = Path('/var/opt/mssql/backup')
I12_BACKEND = 'access-tokens-65560ca3cf98'
I16_RELEASE = 'customer-products-b68982a14d1b'
I18_RELEASE = 'identity-c49df453de9d'
PREVIOUS_PAIRS = (('delivery-0cfe3ef511d8', 'delivery-0cfe3ef511d8'),)
DELIVERY_MIGRATION = '20261103020000_AddDeliveryOperations'
SETUP_MIGRATION = '20261102152307_AddCustomerProductSetup'
IDENTITY_MIGRATION = '20261102235000_AddSecurityAdministration'
PRODUCTION_MIGRATION = '20261103010000_AddProductionAllocation'
MIGRATION = '20261103030000_AddOrderingSchedules'
PRODUCTION_TABLES = ('ProductionBatches', 'ProductionAllocations')
PRODUCTION_PERMISSIONS = ('operations.assign', 'operations.produce', 'operations.allocate')
SUPPORT_FILES = ('inspect-native.py', 'native-deployment-support.py', 'commercial-catalog-support.py',
                 'token-management-support.py', 'customer-products-support.py', 'security-administration-support.py',
                 'delivery-support.py', 'delivery-schema-contract.py', 'validate-ordering-schedules-sql.py')
SQL_FILES = ('migrations-i16.sql', 'migrations-i18.sql', 'migrations-i19.sql', 'migrations-i23.sql',
             'delivery-schema.json', 'validation-baseline.sql')
VALIDATION_REQUIRED = ('validation/Prodimt.Pedidos.DeploymentValidation.dll',
                       'validation/Prodimt.Pedidos.DeploymentValidation.runtimeconfig.json',
                       'validation/Prodimt.Pedidos.DeploymentValidation.deps.json')
VALIDATED_ASSEMBLIES = ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Application.dll',
                        'Prodimt.Pedidos.Domain.dll', 'Prodimt.Pedidos.Infrastructure.dll')


class InstallError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def error_issue(error, inspector=None):
    return str(error) if isinstance(error, (InstallError, getattr(inspector, 'InspectionError', InstallError))) else 'local_operation_failed'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path, limit=40 * 1024 * 1024):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid in (0, 33) and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'file_identity_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'file_too_large')
    return raw


def private(path):
    raw = regular(path)
    meta = path.lstat()
    require(meta.st_uid == 0 and meta.st_mode & 0o7777 == 0o600, 'private_file_identity_unexpected')
    return raw


def safe_name(name):
    pure = PurePosixPath(name)
    return bool(pure.parts) and pure.as_posix() == name and not pure.is_absolute() and name not in ('', '.') \
        and not any(part in ('', '.', '..') for part in pure.parts) and ':' not in name and '\\' not in name


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 700 and len({row.filename for row in rows}) == len(rows)
                and sum(row.file_size for row in rows) <= 100 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and not stat.S_ISLNK(row.external_attr >> 16) for row in rows), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in rows}
    manifest = json.loads(files.pop('manifest.json'))
    required = set(SUPPORT_FILES + SQL_FILES + VALIDATION_REQUIRED) | {'backend/Prodimt.Pedidos.Api.dll', 'backend/appsettings.json', 'frontend/index.html'}
    require(manifest.get('feature') == 'ordering-schedules-on-delivery-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and manifest.get('migrationsBefore') == 17 and manifest.get('migrationsAfter') == 18
            and manifest.get('migrationIds') == [SETUP_MIGRATION, IDENTITY_MIGRATION, PRODUCTION_MIGRATION, DELIVERY_MIGRATION, MIGRATION]
            and manifest.get('previousPairs') == [list(pair) for pair in PREVIOUS_PAIRS]
            and manifest.get('encryptionKeyInPackage') is False and manifest.get('revokeTokens') is False
            and set(manifest.get('files', {})) == set(files) and required <= set(files)
            and all(name in required or name.startswith(('backend/', 'frontend/', 'validation/')) for name in files), 'payload_manifest_unexpected')
    require(all(meta == dict(bytes=len(files[name]), sha256=sha(files[name])) for name, meta in manifest['files'].items()), 'payload_file_hash_mismatch')
    require(all('backend/' + name in files and 'validation/' + name in files
                and files['backend/' + name] == files['validation/' + name] for name in VALIDATED_ASSEMBLIES),
            'validation_application_binary_mismatch')
    require(all(not name.endswith(('.map', '.pdb', '.env', '.key', '.pem', '.pfx', '.p12', '.trace', '.dmp', '.log'))
                and not name.endswith('appsettings.Development.json') for name in files), 'payload_private_or_debug_file')
    module('i19_setup_validator', files['customer-products-support.py']).validate_migration(files['migrations-i16.sql'].decode('utf-8-sig'))
    module('i19_security_validator', files['security-administration-support.py']).validate_migration_sql(files['migrations-i18.sql'].decode('utf-8-sig'))
    validate_production_migration_sql(files['migrations-i19.sql'].decode('utf-8-sig'))
    validate_migration_sql(files['migrations-i23.sql'].decode('utf-8-sig'))
    validation = module('i23_sql_validation', files['validate-ordering-schedules-sql.py'])
    validation.validate_baseline(files['validation-baseline.sql'].decode('utf-8-sig'))
    return files


def validate_production_migration_sql(sql):
    require(isinstance(sql, str) and sql.startswith('BEGIN TRANSACTION;\n')
            and sql.endswith('COMMIT;\nGO\n\n')
            and sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1, 'production_transaction_unexpected')
    revision_update = 'UPDATE [SecurityStates] SET [Revision] = NEWID() WHERE [Id] = 1;'
    require(sql.count(revision_update) == 1, 'security_revision_update_unexpected')
    require(not re.search(r'\b(?:DELETE|UPDATE|TRUNCATE|DROP|USE|GRANT|DENY)\b', sql.replace(revision_update, '').replace('ON DELETE NO ACTION', ''), re.I)
            and re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) == [PRODUCTION_MIGRATION]
            and set(re.findall(r'(?im)^\s*ALTER TABLE \[([^]]+)\]', sql)) == {'Products','CustomerFrequentProducts','OrderLines'}
            and len(re.findall(r'ADD \[RequiresMachine\] bit NOT NULL DEFAULT CAST\(1 AS bit\)', sql)) == 3
            and set(re.findall(r'(?im)^\s*CREATE TABLE \[([^]]+)\]', sql)) == set(PRODUCTION_TABLES)
            and all(key in sql for key in PRODUCTION_PERMISSIONS), 'production_migration_unexpected')
    require(set(re.findall(r'\bINSERT INTO \[([^]]+)\]', sql, re.I)) ==
            {'SecurityPermissions','SecurityRolePermissions','__EFMigrationsHistory'}, 'production_seed_targets_unexpected')
    require(all(name in sql for name in ('CK_ProductionBatch_Quantity','CK_ProductionBatch_Unit',
            'CK_ProductionAllocation_Quantity','CK_ProductionAllocation_Reversal')), 'production_checks_missing')


SCHEDULE_SHAPE = """SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Customers')
  AND ((name=N'AutomaticConfirmationCutoff' AND TYPE_NAME(user_type_id)=N'time' AND is_nullable=1)
    OR (name=N'OrderingScheduleVersion' AND TYPE_NAME(user_type_id)=N'int' AND is_nullable=0))) AS customer_columns,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.RouteOrderSchedules')
  AND ((name=N'RouteCode' AND TYPE_NAME(user_type_id)=N'nvarchar' AND max_length=160 AND is_nullable=0)
    OR (name=N'AutomaticConfirmationCutoff' AND TYPE_NAME(user_type_id)=N'time' AND is_nullable=1)
    OR (name=N'Version' AND TYPE_NAME(user_type_id)=N'int' AND is_nullable=0))) AS route_columns,
 (SELECT COUNT(*) FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.RouteOrderSchedules') AND type=N'PK') AS route_primary_keys,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Customers') AND name=N'OrderingScheduleVersion' AND default_object_id<>0) AS version_defaults,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_Customers_AutomaticConfirmationCutoff',N'CK_RouteOrderSchedules_Cutoff') AND is_disabled=0 AND is_not_trusted=0) AS trusted_checks
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""
EXPECTED_SCHEDULE_SHAPE = dict(customer_columns=2,route_columns=3,route_primary_keys=1,version_defaults=1,trusted_checks=2)
SCHEDULE_PERMISSIONS = "SELECT " + ','.join(
 "HAS_PERMS_BY_NAME(N'dbo.RouteOrderSchedules',N'OBJECT',N'"+op+"') AS ["+op+"]"
 for op in ('SELECT','INSERT','UPDATE','DELETE')) + " FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"
EXPECTED_SCHEDULE_PERMISSIONS = dict(SELECT=1,INSERT=1,UPDATE=1,DELETE=0)
SCHEDULE_GRANTS = """GRANT SELECT,INSERT,UPDATE ON dbo.RouteOrderSchedules TO [prodimt_pedidos_app];
DENY DELETE ON dbo.RouteOrderSchedules TO [prodimt_pedidos_app];"""

def validate_migration_sql(sql):
    require(isinstance(sql,str) and sql.startswith('BEGIN TRANSACTION;\n') and sql.endswith('COMMIT;\nGO\n\n'), 'schedule_transaction_unexpected')
    require(sql.count('BEGIN TRANSACTION;') == sql.count('COMMIT;') == 1 and
        not re.search(r'\b(?:DELETE|UPDATE|TRUNCATE|DROP|USE|GRANT|DENY|EXEC)\b',sql,re.I) and
        re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.",sql) == [MIGRATION] and
        re.findall(r'(?im)^\s*ALTER TABLE \[([^]]+)\]',sql) == ['Customers','Customers','Customers'] and
        re.findall(r'(?im)^\s*CREATE TABLE \[([^]]+)\]',sql) == ['RouteOrderSchedules'] and
        'ADD [AutomaticConfirmationCutoff] time NULL' in sql and
        'ADD [OrderingScheduleVersion] int NOT NULL DEFAULT 0' in sql and
        '[RouteCode] nvarchar(80) NOT NULL' in sql and '[Version] int NOT NULL' in sql and
        all(name in sql for name in ('CK_Customers_AutomaticConfirmationCutoff','CK_RouteOrderSchedules_Cutoff')),
        'schedule_migration_unexpected')


def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


def production_schema_query(definitions=True):
    columns = [('Products','RequiresMachine','bit',1,0),('CustomerFrequentProducts','RequiresMachine','bit',1,0),
      ('OrderLines','RequiresMachine','bit',1,0),('ProductionBatches','Id','uniqueidentifier',16,0),
      ('ProductionBatches','ServiceDate','date',3,0),('ProductionBatches','MachineId','uniqueidentifier',16,0),
      ('ProductionBatches','ProductId','uniqueidentifier',16,0),('ProductionBatches','ProductNameSnapshot','nvarchar',400,0),
      ('ProductionBatches','UnitCode','nvarchar',32,0),('ProductionBatches','Quantity','decimal',9,0),
      ('ProductionBatches','CreatedAt','datetimeoffset',10,0),('ProductionBatches','CreatedBy','nvarchar',240,1),
      ('ProductionBatches','Notes','nvarchar',1000,1),('ProductionAllocations','Id','uniqueidentifier',16,0),
      ('ProductionAllocations','BatchId','uniqueidentifier',16,0),('ProductionAllocations','OrderId','uniqueidentifier',16,0),
      ('ProductionAllocations','OrderLineId','uniqueidentifier',16,0),('ProductionAllocations','CustomerNameSnapshot','nvarchar',400,0),
      ('ProductionAllocations','Quantity','decimal',9,0),('ProductionAllocations','CreatedAt','datetimeoffset',10,0),
      ('ProductionAllocations','CreatedBy','nvarchar',240,1),('ProductionAllocations','ReversedBy','nvarchar',240,1),
      ('ProductionAllocations','ReversedAt','datetimeoffset',10,1),('ProductionAllocations','ReverseReason','nvarchar',1000,1)]
    values = ','.join("(N'%s',N'%s',N'%s',%d,%d)" % row for row in columns)
    defaults = " AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''),N'[',N''),N']',N'')) IN (N'1',N'cast1asbit',N'convertbit,1')" if definitions else ''
    normalized = "REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(definition),N' ',N''),N'(',N''),N')',N''),N'[',N''),N']',N''),CHAR(13),N''),CHAR(10),N'')"
    checks = (" AND ((name IN (N'CK_ProductionBatch_Quantity',N'CK_ProductionAllocation_Quantity') AND " + normalized + "=N'quantity>0')"
        " OR (name=N'CK_ProductionBatch_Unit' AND " + normalized + " IN (N'unitcode=''kg''',N'unitcode=n''kg'''))"
        " OR (name=N'CK_ProductionAllocation_Reversal' AND " + normalized +
        "=N'reversedatisnullandreversedbyisnullandreversereasonisnullorreversedatisnotnullandreversereasonisnotnull'))") if definitions else ''
    return """SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (N'ProductionBatches',N'ProductionAllocations')) AS tables_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductionBatches')) AS batch_columns,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductionAllocations')) AS allocation_columns,
 (SELECT COUNT(*) FROM (VALUES """+values+""") e(t,n,k,l,z) JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.t) AND c.name=e.n
  WHERE TYPE_NAME(c.system_type_id)=e.k AND c.max_length=e.l AND c.is_nullable=e.z
   AND (e.k<>N'decimal' OR (c.precision=18 AND c.scale=2)) AND (e.k<>N'datetimeoffset' OR c.scale=7)) AS valid_columns,
 (SELECT COUNT(*) FROM sys.columns c JOIN sys.default_constraints d ON d.object_id=c.default_object_id
  WHERE c.object_id IN (OBJECT_ID(N'dbo.Products'),OBJECT_ID(N'dbo.CustomerFrequentProducts'),OBJECT_ID(N'dbo.OrderLines'))
   AND c.name=N'RequiresMachine'"""+defaults+""") AS true_defaults,
 (SELECT COUNT(*) FROM sys.key_constraints WHERE type=N'PK' AND parent_object_id IN (OBJECT_ID(N'dbo.ProductionBatches'),OBJECT_ID(N'dbo.ProductionAllocations'))) AS primary_keys,
 (SELECT COUNT(*) FROM (VALUES (N'ProductionBatches',N'MachineId',N'Machines'),(N'ProductionBatches',N'ProductId',N'Products'),
   (N'ProductionAllocations',N'BatchId',N'ProductionBatches'),(N'ProductionAllocations',N'OrderId',N'Orders')) e(t,c,r)
  JOIN sys.foreign_keys f ON f.parent_object_id=OBJECT_ID(N'dbo.'+e.t) AND f.referenced_object_id=OBJECT_ID(N'dbo.'+e.r)
  JOIN sys.foreign_key_columns k ON k.constraint_object_id=f.object_id
  JOIN sys.columns p ON p.object_id=k.parent_object_id AND p.column_id=k.parent_column_id AND p.name=e.c
  JOIN sys.columns r ON r.object_id=k.referenced_object_id AND r.column_id=k.referenced_column_id AND r.name=N'Id'
  WHERE f.is_disabled=0 AND f.is_not_trusted=0 AND f.delete_referential_action=0
   AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1) AS restricted_foreign_keys,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0
  AND parent_object_id IN (OBJECT_ID(N'dbo.ProductionBatches'),OBJECT_ID(N'dbo.ProductionAllocations'))
  AND name IN (N'CK_ProductionBatch_Quantity',N'CK_ProductionBatch_Unit',N'CK_ProductionAllocation_Quantity',N'CK_ProductionAllocation_Reversal')"""+checks+""") AS trusted_checks,
 (SELECT COUNT(*) FROM sys.indexes WHERE is_disabled=0 AND is_unique=0 AND has_filter=0
  AND ((object_id=OBJECT_ID(N'dbo.ProductionBatches') AND name IN (N'IX_ProductionBatches_MachineId',N'IX_ProductionBatches_ProductId',N'IX_ProductionBatches_ServiceDate_MachineId_ProductId'))
   OR (object_id=OBJECT_ID(N'dbo.ProductionAllocations') AND name IN (N'IX_ProductionAllocations_BatchId_ReversedAt',N'IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt')))) AS indexes,
 (SELECT COUNT(*) FROM (VALUES
  (N'ProductionBatches',N'IX_ProductionBatches_MachineId',N'MachineId',1),
  (N'ProductionBatches',N'IX_ProductionBatches_ProductId',N'ProductId',1),
  (N'ProductionBatches',N'IX_ProductionBatches_ServiceDate_MachineId_ProductId',N'ServiceDate',1),
  (N'ProductionBatches',N'IX_ProductionBatches_ServiceDate_MachineId_ProductId',N'MachineId',2),
  (N'ProductionBatches',N'IX_ProductionBatches_ServiceDate_MachineId_ProductId',N'ProductId',3),
  (N'ProductionAllocations',N'IX_ProductionAllocations_BatchId_ReversedAt',N'BatchId',1),
  (N'ProductionAllocations',N'IX_ProductionAllocations_BatchId_ReversedAt',N'ReversedAt',2),
  (N'ProductionAllocations',N'IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt',N'OrderId',1),
  (N'ProductionAllocations',N'IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt',N'OrderLineId',2),
  (N'ProductionAllocations',N'IX_ProductionAllocations_OrderId_OrderLineId_ReversedAt',N'ReversedAt',3)) e(t,i,c,n)
  JOIN sys.indexes i ON i.object_id=OBJECT_ID(N'dbo.'+e.t) AND i.name=e.i
  JOIN sys.index_columns k ON k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal=e.n AND k.is_descending_key=0
  JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id AND c.name=e.c) AS index_key_columns
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


PRODUCTION_SCHEMA = production_schema_query()
PRODUCTION_SHAPE = production_schema_query(False)
EXPECTED_PRODUCTION_SCHEMA = dict(tables_count=2,batch_columns=10,allocation_columns=11,valid_columns=24,
    true_defaults=3,primary_keys=2,restricted_foreign_keys=4,trusted_checks=4,indexes=5,index_key_columns=10)
PRODUCTION_GRANTS = """GRANT SELECT,INSERT ON dbo.ProductionBatches TO [prodimt_pedidos_app];
DENY UPDATE,DELETE ON dbo.ProductionBatches TO [prodimt_pedidos_app];
GRANT SELECT,INSERT,UPDATE ON dbo.ProductionAllocations TO [prodimt_pedidos_app];
DENY DELETE ON dbo.ProductionAllocations TO [prodimt_pedidos_app];
"""
PRODUCTION_SQL_PERMISSIONS = 'SELECT '+','.join(
    "HAS_PERMS_BY_NAME(N'dbo."+table+"',N'OBJECT',N'"+operation+"') AS ["+table+'_'+operation+']'
    for table in PRODUCTION_TABLES for operation in ('SELECT','INSERT','UPDATE','DELETE'))+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'
EXPECTED_PRODUCTION_PERMISSIONS = {table+'_'+operation:int(operation in ('SELECT','INSERT') or
    (table=='ProductionAllocations' and operation=='UPDATE')) for table in PRODUCTION_TABLES for operation in ('SELECT','INSERT','UPDATE','DELETE')}


def security_fingerprint_query():
    # This increment has no security seeds. Preserve all roles, grants and revisions.
    queries = {'users':'SELECT * FROM dbo.AdminUsers ORDER BY Id',
        'roles':'SELECT * FROM dbo.SecurityRoles ORDER BY Id',
        'user_roles':'SELECT * FROM dbo.SecurityUserRoles ORDER BY UserId,RoleId',
        'user_permissions':'SELECT * FROM dbo.SecurityUserPermissions ORDER BY UserId,PermissionId',
        'state':'SELECT * FROM dbo.SecurityStates ORDER BY Id',
        'permissions':'SELECT * FROM dbo.SecurityPermissions ORDER BY Id',
        'role_permissions':'SELECT * FROM dbo.SecurityRolePermissions ORDER BY RoleId,PermissionId'}
    return 'SELECT '+','.join("CONVERT(varchar(64),HASHBYTES('SHA2_256',("+query+
        " FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS ["+key+']' for key,query in queries.items())+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


SECURITY_FINGERPRINT = security_fingerprint_query()
DELIVERY_TABLES = ('CustomerDeliveryLocations','DeliveryAssignments','DeliveryEvents','DeliveryRoutes','DeliveryTrips')
DELIVERY_KEYS = {'CustomerDeliveryLocations': 'CustomerId', 'DeliveryRoutes': 'Code',
                 'DeliveryAssignments': 'Id', 'DeliveryEvents': 'Id', 'DeliveryTrips': 'Id'}
DELIVERY_FINGERPRINT = 'SELECT '+','.join("CONVERT(varchar(64),HASHBYTES('SHA2_256',("+
    'SELECT * FROM dbo.'+table+' ORDER BY '+DELIVERY_KEYS[table]+" FOR JSON PATH,INCLUDE_NULL_VALUES)),2) AS ["+table+']'
    for table in DELIVERY_TABLES)+' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


def migration_sql_for_sqlcmd(files, migration):
    """Return the exact single-increment transaction executed by this installer.

    GO retains the SQLcmd connection/transaction. Grants commit with DDL and
    history, so a permission failure cannot leave an applied partial increment.
    Used unchanged by the isolated native ODBC verification.
    """
    if migration == SETUP_MIGRATION:
        support = module('i19_exact_setup_sql', files['customer-products-support.py'])
        return support.migration_sql_for_sqlcmd(files['migrations-i16.sql'].decode('utf-8-sig'))
    if migration == IDENTITY_MIGRATION:
        support = module('i19_exact_security_sql', files['security-administration-support.py'])
        sql = files['migrations-i18.sql'].decode('utf-8-sig')
        support.validate_migration_sql(sql)
        grants = support.SECURITY_GRANTS
    elif migration == PRODUCTION_MIGRATION:
        sql = files['migrations-i19.sql'].decode('utf-8-sig')
        validate_production_migration_sql(sql)
        grants = PRODUCTION_GRANTS
    elif migration == MIGRATION:
        sql = files['migrations-i23.sql'].decode('utf-8-sig')
        validate_migration_sql(sql)
        # Keep EF's reviewed raw delta; compile its new-column checks only when
        # the migration is absent. Grants share the same transaction after GO.
        ending = 'COMMIT;\nGO\n\n'
        body = sql.removeprefix('BEGIN TRANSACTION;\n').removesuffix(ending)
        require(not re.search(r'(?im)^\s*GO\s*$', body), 'schedule_delta_batch_unexpected')
        escaped = body.replace("'", "''")
        return ("BEGIN TRANSACTION;\nIF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'" +
                MIGRATION + "')\nBEGIN\nEXEC(N'" + escaped + "');\nEND;\nGO\n\n" +
                SCHEDULE_GRANTS + '\nCOMMIT;\nGO\n\n')
    else:
        raise InstallError('migration_not_supported')
    require(sql.endswith('COMMIT;\nGO\n\n'), 'migration_commit_boundary_unexpected')
    return sql.removesuffix('COMMIT;\nGO\n\n') + 'GO\n\n' + grants + '\nCOMMIT;\nGO\n\n'


def make_installer(package):
    files = load_payload(package)
    runtime = module('reviewed_i18_native_support', files['native-deployment-support.py'])
    commercial = module('reviewed_i18_commercial_support', files['commercial-catalog-support.py'])
    token = module('reviewed_i18_token_support', files['token-management-support.py'])
    setup = module('i19_setup_support', files['customer-products-support.py'])
    security = module('i19_security_support', files['security-administration-support.py'])
    delivery = module('i23_delivery_support', files['delivery-support.py'])
    delivery.InstallError = InstallError
    schema = delivery.module('i22_delivery_schema_support', files['delivery-schema-contract.py'])
    contract = json.loads(files['delivery-schema.json'])
    validation = module('i23_exact_sql_validation', files['validate-ordering-schedules-sql.py'])
    setup.InstallError = security.InstallError = InstallError
    runtime.InstallError = InstallError
    token.InstallError = InstallError
    for key in ('ROOT', 'BACKUPS', 'ENV_FILE', 'SERVICE', 'DOMAIN', 'SQL_BACKUPS', 'EXPECTED_SOURCE'):
        setattr(runtime, key, globals()[key])
    base = runtime.MIGRATIONS + [token.PRICE_MIGRATION, token.MIGRATION]
    migrations = base + [SETUP_MIGRATION, IDENTITY_MIGRATION, PRODUCTION_MIGRATION, DELIVERY_MIGRATION, MIGRATION]
    known = (migrations[:-1], migrations)
    SECURITY_SCHEMA, EXPECTED_SECURITY_SCHEMA = security.SECURITY_SCHEMA, security.EXPECTED_SECURITY_SCHEMA
    SECURITY_DATA, SECURITY_PERMISSIONS = security.SECURITY_DATA, security.SECURITY_PERMISSIONS
    EXPECTED_SECURITY_PERMISSIONS = security.EXPECTED_SECURITY_PERMISSIONS

    class ProductionInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.runtime = files, package, runtime
            self.old = {}
            self.new = {part: ROOT / part / 'releases' / ('ordering-schedules-i23-' + EXPECTED_SOURCE[:12]) for part in ('backend', 'frontend')}
            self.stop_requested = False
            self.recovery = None
            self.marker = BACKUPS / 'ordering-schedules-i23-pending.json'
            self.state = dict(revision=REVISION, status='planned', phase='read-only-preflight', changes_made=False,
                database_migrations_run=False, migrations_may_have_changed=False, database_automatically_restored=False,
                password_changed=False, service_restarted=False, health_verified=False, frontend_verified=False,
                secret_values_printed=False, encryption_key_created=False, encryption_key_rotated=False,
                tokens_revoked=False)

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.production-stage-' + uuid.uuid4().hex)
                temporary.mkdir(mode=0o700)
                try:
                    for name, raw in self.expected(part).items():
                        path = temporary / name
                        path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                        override = part + '/' + name in self.overrides
                        mode = 0o640 if override else 0o644
                        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
                        with os.fdopen(fd, 'wb') as stream:
                            stream.write(raw)
                        os.chown(path, 0, 33 if override else 0)
                        path.chmod(mode)
                    temporary.chmod(0o755)
                    temporary.rename(target)
                finally:
                    if temporary.exists():
                        runtime.shutil.rmtree(temporary)
                self.verify_stage(part)

        def verify_security(self):
            data = self.sql_json(SECURITY_DATA)
            require(isinstance(data, dict) and data.get('states_count') == 1
                    and data.get('administrators') == 1 and data.get('system_permissions') == 21
                    and data.get('system_grants') == 21 and data.get('active_administrators', 0) >= 1
                    and data.get('active_deleted_users') == 0, 'security_seed_or_recovery_access_unexpected')

        def history(self, reader):
            rows = reader(self.inspector.SQL_MIGRATIONS)
            require(isinstance(rows, list) and all(isinstance(row, dict) and set(row) == {'MigrationId'} for row in rows), 'migration_history_unexpected')
            ids = [row['MigrationId'] for row in rows]
            require(ids in known, 'migration_history_unexpected')
            return ids

        def read_marker(self):
            if not self.marker.exists() and not self.marker.is_symlink():
                return None
            value = json.loads(private(self.marker))
            require(value.get('source') == EXPECTED_SOURCE and value.get('payload') == EXPECTED_PAYLOAD_SHA256, 'different_update_recovery_pending')
            snapshot = Path(value.get('snapshot', ''))
            require(snapshot.parent == BACKUPS and re.fullmatch(r'ordering-schedules-i23-update-[0-9]{8}T[0-9]{6}Z-[a-f0-9]{12}', snapshot.name), 'recovery_snapshot_invalid')
            runtime.directory_safe(snapshot)
            require(snapshot.stat().st_mode & 0o7777 == 0o700, 'recovery_snapshot_mode_invalid')
            require(tuple(value.get('previous', [])) in PREVIOUS_PAIRS and value.get('historyBefore') in known[:-1], 'recovery_base_invalid')
            require(private(snapshot / 'api.env') == regular(ENV_FILE), 'recovery_environment_changed')
            return value

        def write_marker(self):
            value = dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256, snapshot=str(self.snapshot),
                         previous=[self.old[part].name for part in ('backend', 'frontend')], historyBefore=self.history_before)
            temporary = self.marker.with_name('.production-pending-' + uuid.uuid4().hex)
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'w') as stream:
                json.dump(value, stream)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.marker)

        def schemas(self, reader, strict=False):
            ids = self.history(reader)
            require(reader(commercial.NEW_SCHEMA) == commercial.EXPECTED_SCHEMA, 'commercial_schema_unexpected')
            require(reader(setup.TOKEN_SCHEMA if strict else setup.TOKEN_SHAPE) == setup.EXPECTED_TOKEN_SCHEMA, 'token_management_schema_unexpected')
            require(reader(setup.SETUP_SCHEMA if strict else setup.SETUP_SHAPE) == (setup.EXPECTED_SETUP_SCHEMA if SETUP_MIGRATION in ids else dict.fromkeys(setup.EXPECTED_SETUP_SCHEMA, 0)), 'product_setup_schema_unexpected')
            require(reader(SECURITY_SCHEMA) == (EXPECTED_SECURITY_SCHEMA if IDENTITY_MIGRATION in ids else dict.fromkeys(EXPECTED_SECURITY_SCHEMA, 0)), 'security_schema_unexpected')
            require(reader(PRODUCTION_SCHEMA if strict else PRODUCTION_SHAPE) == (EXPECTED_PRODUCTION_SCHEMA if PRODUCTION_MIGRATION in ids else dict.fromkeys(EXPECTED_PRODUCTION_SCHEMA, 0)), 'production_schema_unexpected')
            require(reader(SCHEDULE_SHAPE) == (EXPECTED_SCHEDULE_SHAPE if MIGRATION in ids else dict.fromkeys(EXPECTED_SCHEDULE_SHAPE, 0)), 'ordering_schedule_schema_unexpected')
            require(schema.matches(reader(schema.schema_query(strict)), contract, strict), 'delivery_schema_unexpected')
            return ids

        def app_permissions(self, ids):
            read = lambda query: self.inspector.query_sql(self.sql_tool, self.fields, query)
            require(not commercial.permission_mismatches(read(commercial.PERMISSION_QUERY), (len(ids),)), 'application_permissions_unexpected')
            require(read(commercial.PRICE_PERMISSIONS) == commercial.EXPECTED_PRICE_PERMISSIONS, 'price_history_permissions_unexpected')
            if SETUP_MIGRATION in ids:
                require(read(setup.SETUP_PERMISSIONS) == setup.EXPECTED_SETUP_PERMISSIONS, 'setup_permissions_unexpected')
            if IDENTITY_MIGRATION in ids:
                require(read(SECURITY_PERMISSIONS) == EXPECTED_SECURITY_PERMISSIONS, 'security_permissions_unexpected')
            if PRODUCTION_MIGRATION in ids:
                require(read(PRODUCTION_SQL_PERMISSIONS) == EXPECTED_PRODUCTION_PERMISSIONS, 'production_permissions_unexpected')
            require(read(delivery.DELIVERY_SQL_PERMISSIONS) == delivery.EXPECTED_DELIVERY_PERMISSIONS, 'delivery_permissions_unexpected')
            if MIGRATION in ids:
                require(read(SCHEDULE_PERMISSIONS) == EXPECTED_SCHEDULE_PERMISSIONS, 'ordering_schedule_permissions_unexpected')

        def preflight(self):
            for name in ('delivery-pending.json', 'ordering-schedules-pending.json'):
                path = BACKUPS / name
                require(not path.exists() and not path.is_symlink(), 'different_update_recovery_pending')
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            self.recovery = self.read_marker()
            current = {}
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_release_link_required')
                current[part] = link.resolve(strict=True)
                require(current[part].parent == ROOT / part / 'releases', 'previous_release_path_unexpected')
            own = current == self.new
            if self.recovery:
                self.snapshot = Path(self.recovery['snapshot'])
                self.old = {part: ROOT / part / 'releases' / name for part, name in zip(('backend', 'frontend'), self.recovery['previous'])}
                require(all(current[part] in (self.old[part], self.new[part]) for part in current), 'recovery_release_changed')
                saved = json.loads(private(self.snapshot / 'process-input.json'))
                process_environment, argv = saved['environment'], saved['argv']
                self.unit_configuration = saved['unit']
                properties = runtime.command(['systemctl', 'show', SERVICE, '--property=ActiveState,MainPID,User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'])
                observed = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
                require({key: observed.get(key, '') for key in self.unit_configuration} == self.unit_configuration, 'recovery_service_configuration_changed')
                require(observed.get('ActiveState') in ('inactive', 'failed', 'active', 'activating'), 'recovery_service_state_unexpected')
                if observed.get('ActiveState') == 'active':
                    self.process(current['backend'])
                cwd = self.old['backend']
                self.history_before = self.recovery['historyBefore']
            else:
                self.old = current
                require(own or tuple(self.old[part].name for part in ('backend', 'frontend')) in PREVIOUS_PAIRS, 'previous_release_not_supported')
                pid, cwd, argv = self.process(self.old['backend'])
                process_environment = None
            token.validate_environment_file(ENV_FILE)
            self.environment = regular(ENV_FILE)
            self.token_encryption_key = token.environment_key(self.environment)
            require(self.token_encryption_key is not None, 'existing_token_encryption_key_required')
            self.inspector = module('i19_inventory', files['inspect-native.py'])
            self.inspector.QUERIES = (commercial.PERMISSION_QUERY, commercial.NEW_SCHEMA, commercial.PRICE_PERMISSIONS,
                setup.TOKEN_SCHEMA, setup.TOKEN_SHAPE, setup.SETUP_SCHEMA, setup.SETUP_SHAPE, setup.SETUP_PERMISSIONS,
                SECURITY_SCHEMA, SECURITY_PERMISSIONS, PRODUCTION_SCHEMA, PRODUCTION_SHAPE, PRODUCTION_SQL_PERMISSIONS,
                SCHEDULE_SHAPE, SCHEDULE_PERMISSIONS, schema.schema_query(True), schema.schema_query(False),
                delivery.DELIVERY_SQL_PERMISSIONS, self.inspector.SQL_MIGRATIONS)
            if process_environment is None:
                process_environment = self.inspector.process_environment(pid)
            cfg, source = self.inspector.configuration(process_environment, argv, cwd)
            require(source.get('environment') == 'Production' and self.inspector.policy_flags(cfg) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True, 'jwt_lifetime_compatible': True,
                'connection_compatible_with_production_policy': True, 'development_seed_enabled': False,
                'automatic_migrations_enabled': False, 'https_redirection_enabled': True}, 'production_configuration_unexpected')
            require(self.inspector.first(cfg, token.KEY_CONFIG, default=None) == self.token_encryption_key, 'token_encryption_key_configuration_conflict')
            self.fields = self.inspector.connection_fields(self.inspector.first(cfg, 'ConnectionStrings:Pedidos', 'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(self.inspector.permitted_connection(self.fields), 'sql_target_unexpected')
            self.sql_tool = next((tool for tool in self.inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool, 'sqlcmd_required')
            read = lambda query: self.inspector.query_sql(self.sql_tool, self.fields, query)
            ids = self.schemas(read)
            if not self.recovery:
                expected = migrations if own else migrations[:-1]
                require(ids == expected, 'release_and_migration_history_disagree')
                self.history_before = ids
            self.app_permissions(ids)
            self.original_dll = regular(self.old['backend'] / 'Prodimt.Pedidos.Api.dll')
            self.overrides = {}
            for name in ('appsettings.json', 'appsettings.Production.json'):
                path = self.old['backend'] / name
                if path.exists():
                    raw = regular(path)
                    if self.recovery:
                        require(raw == private(self.snapshot / name), 'recovery_configuration_changed')
                    self.overrides['backend/' + name] = raw
            require(runtime.shutil.disk_usage(ROOT).free >= 2 * sum(map(len, files.values())) + 100 * 1024 * 1024, 'disk_space_insufficient')
            self.process_input = dict(environment=process_environment, argv=argv, unit=self.unit_configuration)
            executable = argv[0] if Path(argv[0]).is_absolute() else shutil.which(argv[0], path=runtime.CLEAN_ENV['PATH'])
            require(executable is not None, 'validation_dotnet_runtime_unavailable')
            self.dotnet_executable = executable
            self.state.update(base_release_verified=True, env_preserved=True, encryption_key_present=True,
                              migration_ids_before=ids, migrations_before=len(ids))
            if self.recovery:
                self.state.update(status='recovery_pending', phase='completed', recovery_pending=True, private_snapshot=str(self.snapshot))
            else:
                code, body = self.http('/health/db')
                require(code == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
                require(self.http('/admin/operacion') == (200, regular(self.old['frontend'] / 'index.html')), 'previous_frontend_not_served')
                require(self.http('/api/admin/customers')[0] == 401, 'admin_authorization_unexpected')
                self.state['health_verified'] = True
                if own:
                    self.verify_stage('backend')
                    self.verify_stage('frontend')
                    self.health(True)
                    self.state.update(status='already_current', phase='completed', frontend_verified=True, tokens_preserved=True)

        def verify_schema(self):
            ids = self.schemas(self.sql_json, strict=True)
            require(self.sql_json(runtime.SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, checks_count=1, valid_check=1), 'rest_days_schema_unexpected')
            require(self.sql_json(runtime.ALIAS_SCHEMA_QUERY) == dict(columns_count=1, valid_column=1, indexes_count=1, valid_index=1), 'capture_alias_schema_unexpected')
            return len(ids)

        def database_backup(self):
            folder = SQL_BACKUPS / ('prodimt-i23-' + uuid.uuid4().hex)
            folder.mkdir(mode=0o700)
            os.chown(folder, 999, 988)
            folder.chmod(0o700)
            path = folder / 'ProdimtPedidos-copy-only.bak'
            self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + str(path) + "' WITH COPY_ONLY,CHECKSUM;", timeout=300)
            meta = path.lstat()
            require(stat.S_ISREG(meta.st_mode) and meta.st_nlink == 1 and meta.st_uid == 999 and meta.st_gid == 988
                    and path.resolve(strict=True) == path and meta.st_size > 0, 'database_backup_file_unexpected')
            path.chmod(0o600)
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            self.state['verified_database_backup'] = str(path)
            record = self.snapshot / ('database-backup-' + uuid.uuid4().hex + '.json')
            record.write_text(json.dumps(dict(verified_backup=str(path))))
            record.chmod(0o600)

        def backup_and_migrate(self):
            ids = self.schemas(self.sql_json, strict=True)
            require(ids == self.state['migration_ids_before'], 'database_changed_during_staging')
            counts = self.sql_json(runtime.COUNTS_QUERY)
            require(token.valid_counts(counts, ('customers', 'orders', 'lines', 'administrators', 'audits', 'order_audits', 'profiles', 'products', 'machines')), 'database_counts_unrecognized')
            monetary = self.sql_json(commercial.PRICE_COUNTS)
            tokens = self.sql_json(token.TOKEN_COUNTS)
            fingerprint = self.sql_json(setup.DATA_FINGERPRINT)
            security_before = self.sql_json(SECURITY_FINGERPRINT) if IDENTITY_MIGRATION in ids else None
            setup_before = self.sql_json(setup.SETUP_COUNTS) if SETUP_MIGRATION in ids else None
            delivery_before = self.sql_json(DELIVERY_FINGERPRINT)
            self.database_backup()
            require(self.state.get('verified_database_backup'), 'verified_database_backup_required')
            require(regular(ENV_FILE) == self.environment and token.environment_key(self.environment) == self.token_encryption_key, 'configuration_changed_before_migration')
            for migration, name in ((MIGRATION, 'migrations-i23.sql'),):
                if migration in ids:
                    continue
                sql = migration_sql_for_sqlcmd(files, migration)
                self.state.update(database_migrations_run=True, migrations_may_have_changed=True)
                self.sql_run(sql, timeout=180)
                ids = self.history(self.sql_json)
            require(ids == migrations and self.verify_schema() == 18, 'migrated_schema_unexpected')
            self.verify_security()
            if IDENTITY_MIGRATION not in self.state['migration_ids_before']:
                require(self.sql_json(security.ADMIN_ACCESS_PRESERVED) == {'missing': 0}, 'existing_administrator_access_not_preserved')
            if security_before is not None:
                require(self.sql_json(SECURITY_FINGERPRINT) == security_before, 'existing_security_changed')
            if setup_before is not None:
                require(self.sql_json(setup.SETUP_COUNTS) == setup_before, 'existing_product_setup_changed')
            require(self.sql_json(runtime.COUNTS_QUERY) == counts and self.sql_json(commercial.PRICE_COUNTS) == monetary
                    and self.sql_json(token.TOKEN_COUNTS) == tokens and self.sql_json(setup.DATA_FINGERPRINT) == fingerprint, 'existing_data_changed')
            require(self.sql_json(DELIVERY_FINGERPRINT) == delivery_before, 'existing_delivery_changed')
            self.app_permissions(migrations)
            self.state.update(migrations_after=18, migrated_schema_verified=True, existing_data_counts_preserved=True,
                price_history_append_only_verified=True, tokens_preserved=True, security_permissions_verified=True, production_permissions_verified=True, delivery_permissions_verified=True,
                existing_delivery_preserved=True)

        def health(self, new):
            super().health(new)
            if new:
                require(self.http('/api/admin/ordering-schedules')[0] == 401 and self.http('/api/admin/security')[0] == 401 and self.http('/api/admin/security/me')[0] == 401 and self.http('/api/admin/operations/production/')[0] == 401
                        and self.http('/api/admin/delivery')[0] == 401 and self.http('/api/delivery')[0] == 401,
                        'security_endpoint_not_protected')
                pid, cwd, argv = self.process(self.new['backend'])
                cfg, source = self.inspector.configuration(self.inspector.process_environment(pid), argv, cwd)
                require(source.get('environment') == 'Production'
                        and self.inspector.first(cfg, token.KEY_CONFIG, default=None) == self.token_encryption_key
                        and regular(ENV_FILE) == self.environment, 'configuration_or_token_key_changed')
                self.state.update(frontend_verified=True, encryption_key_effective_verified=True)

        def validate_before_apply(self):
            self.state['phase'] = 'isolated-http-and-sql-validation'
            connection = delivery.validation_connection(runtime.SERVER, self.sql_login, self.sql_password)
            def cleanup(database):
                return validation.cleanup_generated_database(self.sql_tool, runtime.SERVER, self.sql_login,
                    self.sql_password, runtime.OPTIONS, runtime.CLEAN_ENV, self.inspector.parse_sql_output, database)
            try:
                portable = delivery.run_portable_validation(files, self.dotnet_executable, connection, runtime.CLEAN_ENV, cleanup)
            except InstallError as error:
                if getattr(error, 'cleanup_database', None):
                    self.state['validation_database_cleanup_required'] = error.cleanup_database
                raise
            self.state['portable_validation'] = portable
            self.state['phase'] = 'isolated-exact-sql-validation'
            operations = types.SimpleNamespace(
                migration_sql_for_sqlcmd=lambda payload: migration_sql_for_sqlcmd(payload, MIGRATION),
                SECURITY_FINGERPRINT=SECURITY_FINGERPRINT, DELIVERY_FINGERPRINT=DELIVERY_FINGERPRINT,
                SCHEDULE_SHAPE=SCHEDULE_SHAPE, EXPECTED_SCHEDULE_SHAPE=EXPECTED_SCHEDULE_SHAPE,
                SCHEDULE_PERMISSIONS=SCHEDULE_PERMISSIONS, EXPECTED_SCHEDULE_PERMISSIONS=EXPECTED_SCHEDULE_PERMISSIONS,
                DELIVERY_SQL_PERMISSIONS=delivery.DELIVERY_SQL_PERMISSIONS,
                EXPECTED_DELIVERY_PERMISSIONS=delivery.EXPECTED_DELIVERY_PERMISSIONS,
                DELIVERY_GRANTS=delivery.DELIVERY_GRANTS)
            try:
                result = validation.validate_exact_sql(files, operations, schema,
                    self.sql_tool, runtime.SERVER, self.sql_login, self.sql_password,
                    runtime.OPTIONS, runtime.CLEAN_ENV, self.inspector.parse_sql_output)
            except validation.ValidationFailure as error:
                if error.cleanup_database:
                    self.state['validation_database_cleanup_required'] = error.cleanup_database
                raise InstallError(str(error)) from None
            self.state.update(exact_sql_validation=result, isolated_validation_verified=True,
                              validation_databases_cleaned=True)

        def validate_only(self):
            self.preflight()
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.validate_before_apply()
            self.state.update(status='validated', phase='completed', changes_made=False,
                              production_changes_made=False)

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.authenticate_admin()
            self.validate_before_apply()
            if not self.recovery:
                self.snapshot = BACKUPS / ('ordering-schedules-i23-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
                self.snapshot.mkdir(mode=0o700)
                (self.snapshot / 'api.env').write_bytes(self.environment)
                (self.snapshot / 'releases.json').write_text(json.dumps({part: str(path) for part, path in self.old.items()}))
                (self.snapshot / 'process-input.json').write_text(json.dumps(self.process_input))
                for name, raw in self.overrides.items():
                    (self.snapshot / name.split('/', 1)[1]).write_bytes(raw)
                for path in self.snapshot.iterdir():
                    path.chmod(0o600)
            self.state.update(changes_made=True, private_snapshot=str(self.snapshot))
            self.stage()
            if not self.recovery:
                self.links(self.old)
                self.process(self.old['backend'])
            require(regular(ENV_FILE) == self.environment and regular(self.old['backend'] / 'Prodimt.Pedidos.Api.dll') == self.original_dll
                    and all(regular(self.old['backend'] / name.split('/', 1)[1]) == raw for name, raw in self.overrides.items()), 'configuration_or_backend_changed_during_staging')
            self.write_marker()
            self.state['phase'] = 'stop-prodimt-api'
            self.stop_requested = True
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            self.state['phase'] = 'database-backup-and-migration'
            self.backup_and_migrate()
            self.state['phase'] = 'activate-releases'
            self.switch(self.new)
            runtime.command(['systemctl', 'start', SERVICE], timeout=45)
            self.state['service_restarted'] = True
            self.state['phase'] = 'verify-application'
            self.health(True)
            self.marker.unlink()
            self.state.update(status='updated', phase='completed', health_verified=True, recovery_pending=False, source_commit=EXPECTED_SOURCE,
                              new_releases={part: str(path) for part, path in self.new.items()})

        def rollback(self):
            if not self.stop_requested:
                return
            try:
                runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            except (Exception, KeyboardInterrupt):
                self.state.update(recovery_manual_required=True, recovery_pending=True, rollback_blocked_service_stop=True)
                return
            self.state['rollback_service_stopped'] = True
            try:
                safe = self.history(self.sql_json) == self.history_before and not self.recovery
            except (Exception, KeyboardInterrupt):
                safe = False
            if not safe:
                self.state.update(recovery_manual_required=True, recovery_pending=True, application_stopped_for_security=True,
                                  database_automatically_restored=False)
                return
            super().rollback()
            self.marker.unlink(missing_ok=True)
            self.state.update(previous_health_verified=True, recovery_pending=False)

    return ProductionInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-ordering-schedules-i23-payload.zip'))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--apply', action='store_true')
    mode.add_argument('--validate-only', action='store_true')
    args = parser.parse_args()
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    installer = make_installer(args.package)
    installer.state['mode'] = 'validate-only' if args.validate_only else 'apply' if args.apply else 'read-only'
    fd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    previous = {}
    try:
        meta = os.fstat(fd)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1 and meta.st_mode & 0o7777 == 0o600,
                'lock_identity_unexpected')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        def interrupted(number, frame):
            raise InstallError('installation_interrupted')
        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            previous[number] = signal.signal(number, interrupted)
        try:
            if args.validate_only:
                installer.validate_only()
            elif args.apply:
                installer.apply()
            else:
                installer.preflight()
                if installer.state['status'] != 'already_current':
                    installer.state['phase'] = 'completed'
        except (Exception, KeyboardInterrupt) as error:
            installer.state.update(status='failed', issue=error_issue(error, getattr(installer, 'inspector', None)))
            for number in previous:
                signal.signal(number, signal.SIG_IGN)
            try:
                installer.rollback()
            except (Exception, KeyboardInterrupt):
                installer.state['rollback_failed'] = True
        print(json.dumps(installer.state, ensure_ascii=False, indent=2))
        return 0 if installer.state['status'] in ('planned', 'updated', 'already_current', 'recovery_pending', 'validated') else 1
    finally:
        for number, handler in previous.items():
            signal.signal(number, handler)
        os.close(fd)


if __name__ == '__main__':
    os.umask(0o022)
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt):
        print(json.dumps(dict(revision=REVISION, status='failed', issue='installation_not_started', secret_values_printed=False)))
        sys.exit(1)
