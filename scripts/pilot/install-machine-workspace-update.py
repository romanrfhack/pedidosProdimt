#!/usr/bin/env python3
"""Inventory or install Responsable de Máquina over the reviewed 20-migration base."""
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
import shutil
import stat
import sys
import types
import uuid
import zipfile

EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
MIGRATION = '20261117145405_AddMachineWorkspace'
ASSEMBLIES = ('Prodimt.Pedidos.Api.dll', 'Prodimt.Pedidos.Application.dll',
              'Prodimt.Pedidos.Domain.dll', 'Prodimt.Pedidos.Infrastructure.dll')
SUPPORT = ('inspect-native.py', 'native-deployment-support.py', 'frontend-asset-support.py')
TABLES = ('MachineOperators', 'MachineTeamTemplates', 'MachineTeamChanges',
          'MachineWorkers', 'MachineProductionNotices', 'ProductionLotHandoffs')
HISTORY_QUERY = 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;'


class InstallError(Exception):
    pass


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def module(name, raw):
    result = types.ModuleType(name)
    exec(compile(raw, name, 'exec'), result.__dict__)
    return result


def safe_name(name):
    path = PurePosixPath(name)
    return bool(name) and path.as_posix() == name and not path.is_absolute() and ':' not in name and '\\' not in name \
        and all(part not in ('', '.', '..') and not part.startswith('.') for part in name.split('/'))


def regular(path, limit=100 * 1024 * 1024):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, 'rb') as stream:
        meta = os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid in (0, 33) and meta.st_nlink == 1
                and not meta.st_mode & 0o022, 'file_identity_unexpected')
        raw = stream.read(limit + 1)
    require(len(raw) <= limit, 'file_too_large')
    return raw


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}', EXPECTED_SOURCE)
            and re.fullmatch(r'[a-f0-9]{64}', EXPECTED_PAYLOAD_SHA256), 'installer_not_pinned')
    raw = regular(path)
    require(sha(raw) == EXPECTED_PAYLOAD_SHA256, 'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 1200 and len({row.filename for row in entries}) == len(entries)
                and sum(row.file_size for row in entries) <= 250 * 1024 * 1024, 'payload_size_or_duplicates_invalid')
        require(all(safe_name(row.filename) and not row.is_dir() and not row.flag_bits & 1
                    and stat.S_IFMT(row.external_attr >> 16) in (0, stat.S_IFREG) for row in entries), 'payload_path_invalid')
        files = {row.filename: archive.read(row) for row in entries}
    manifest = json.loads(files.pop('manifest.json'))
    required = set(SUPPORT) | {'migration.sql', 'backend/appsettings.json', 'frontend/index.html',
                              'frontend/3rdpartylicenses.txt', 'backend/Prodimt.Pedidos.Api.runtimeconfig.json',
                              'backend/Prodimt.Pedidos.Api.deps.json', *('backend/' + name for name in ASSEMBLIES)}
    require(manifest.get('feature') == 'machine-workspace-v1' and manifest.get('sourceCommit') == EXPECTED_SOURCE
            and manifest.get('migration') == MIGRATION and len(manifest.get('migrationsBefore', [])) == 20
            and manifest.get('migrationsAfter') == manifest['migrationsBefore'] + [MIGRATION]
            and required <= set(files) and set(manifest.get('files', {})) == set(files), 'payload_manifest_invalid')
    require(all(name in set(SUPPORT) | {'migration.sql'} or name.startswith(('backend/', 'frontend/')) for name in files)
            and all(not name.endswith(('.env', '.key', '.pem', '.pfx', '.pdb', '.map', '.log'))
                    and 'appsettings.Development' not in name for name in files), 'payload_private_file')
    require(all(meta == {'bytes': len(files[name]), 'sha256': sha(files[name])}
                for name, meta in manifest['files'].items()), 'payload_content_mismatch')
    validate_migration(files['migration.sql'].decode('utf-8-sig'))
    assets = module('machine_frontend_assets', files['frontend-asset-support.py'])
    assets.validate_asset_closure({name[9:]: data for name, data in files.items() if name.startswith('frontend/')})
    return files, manifest


def validate_migration(sql):
    require(re.findall(r"VALUES\s*\(N'([^']+)',\s*N'10\.", sql) == [MIGRATION]
            and sql.count('BEGIN TRANSACTION;') == 1 and sql.count('COMMIT;') == 1
            and sql.index('BEGIN TRANSACTION;') < sql.index('CREATE TABLE')
            and all('CREATE TABLE [' + name + ']' in sql for name in TABLES)
            and 'ADD [ReleasedQuantity]' in sql
            and not re.search(r'\b(?:DROP\s+(?:TABLE|COLUMN|DATABASE)|TRUNCATE|DELETE\s+FROM|ALTER\s+LOGIN)\b', sql, re.I),
            'incremental_sql_unexpected')


def schema_query():
    tables = ','.join("(N'" + name + "')" for name in TABLES)
    return f"""SELECT
 (SELECT COUNT(*) FROM (VALUES {tables}) v(t) JOIN sys.tables t
  ON t.schema_id=SCHEMA_ID(N'dbo') AND t.name=v.t) AS tables_count,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductionAllocations')
  AND name=N'ReleasedQuantity' AND TYPE_NAME(system_type_id)=N'decimal'
  AND precision=18 AND scale=2 AND is_nullable=0) AS released_column,
 (SELECT COUNT(*) FROM sys.check_constraints WHERE name=N'CK_ProductionAllocation_Release'
  AND parent_object_id=OBJECT_ID(N'dbo.ProductionAllocations') AND is_disabled=0 AND is_not_trusted=0) AS released_check,
 (SELECT COUNT(*) FROM dbo.SecurityRoles WHERE Id='24240000-0000-0000-0000-000000000001'
  AND IsActive=1 AND IsSystem=1) AS role_present,
 (SELECT COUNT(*) FROM dbo.SecurityPermissions WHERE [Key] IN
  (N'operations.machine.work',N'operations.machine.manage') AND IsActive=1 AND IsSystem=1) AS permissions_count
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"""


def permission_query():
    return 'SELECT ' + ','.join(
        f"HAS_PERMS_BY_NAME(N'dbo.{table}',N'OBJECT',N'{permission}') AS [{table}_{permission}]"
        for table in TABLES for permission in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')) + ' FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'


def expected_permissions():
    result = {f'{table}_{permission}': int(permission in ('SELECT', 'INSERT'))
              for table in TABLES for permission in ('SELECT', 'INSERT', 'UPDATE', 'DELETE')}
    result['MachineOperators_DELETE'] = 1
    result['MachineTeamTemplates_UPDATE'] = 1
    return result


def make_installer(package, base_frontend=None):
    files, manifest = load_payload(package)
    runtime = module('machine_native_support', files['native-deployment-support.py'])
    runtime.InstallError = InstallError
    inspector = module('machine_inventory_support', files['inspect-native.py'])

    class MachineInstaller(runtime.Installer):
        def __init__(self):
            self.files, self.package, self.inspector = files, package, inspector
            self.new = {part: ROOT / part / 'releases' / ('machine-workspace-' + EXPECTED_SOURCE[:12])
                        for part in ('backend', 'frontend')}
            self.old = {}
            self.state = dict(status='planned', phase='inventory', source_commit=EXPECTED_SOURCE,
                              changes_made=False, database_migrations_run=False,
                              database_automatically_restored=False, secret_values_printed=False,
                              health_verified=False, recovery_pending=False)
            self.marker = BACKUPS / 'machine-workspace-pending.json'
            self.stop_requested = False
            self.base_frontend = manifest["basePair"][1] or base_frontend
            self.migration_verified = False
            self.sql_login = self.sql_password = ''

        def app_json(self, query):
            inspector.QUERIES = (query,)
            return inspector.query_sql(self.sql_tool, self.fields, query)

        def history(self, reader):
            rows = reader(HISTORY_QUERY)
            require(isinstance(rows, list) and all(isinstance(row, dict) and set(row) == {'MigrationId'}
                    for row in rows), 'migration_history_invalid')
            return [row['MigrationId'] for row in rows]

        def current_links(self):
            current = {}
            for part in self.new:
                link = ROOT / part / 'current'
                require(link.is_symlink(), 'current_symlink_required')
                target = link.resolve(strict=True)
                require(target.parent == ROOT / part / 'releases', 'release_path_unexpected')
                current[part] = target
            return current

        def preflight(self, inventory=False):
            current = self.current_links()
            for path in (ROOT, BACKUPS, *(ROOT / part / 'releases' for part in self.new)):
                runtime.directory_safe(path)
            self.environment = regular(ENV_FILE)
            self.recovery = None
            if self.marker.exists():
                require(self.marker.stat().st_uid == 0 and self.marker.stat().st_mode & 0o7777 == 0o600,
                        'recovery_marker_identity_unexpected')
                self.recovery = json.loads(regular(self.marker))
                require(self.recovery.get('source') == EXPECTED_SOURCE and self.recovery.get('payload') == EXPECTED_PAYLOAD_SHA256,
                        'another_update_recovery_pending')
                self.snapshot = Path(self.recovery['snapshot'])
                require(self.snapshot.parent == BACKUPS and re.fullmatch(r'machine-workspace-update-[0-9]{8}T[0-9]{6}Z-[a-f0-9]{12}',
                        self.snapshot.name), 'recovery_snapshot_invalid')
                runtime.directory_safe(self.snapshot)
                require(self.snapshot.stat().st_mode & 0o7777 == 0o700, 'recovery_snapshot_mode_unexpected')
                self.old = {part: ROOT / part / 'releases' / self.recovery['previous'][part] for part in self.new}
                require(self.old['backend'].name == manifest['basePair'][0]
                        and (manifest['basePair'][1] is None or self.old['frontend'].name == manifest['basePair'][1])
                        and all(current[part] in (self.old[part], self.new[part]) for part in self.new),
                        'recovery_release_changed')
                require(regular(self.snapshot / 'api.env') == self.environment, 'recovery_environment_changed')
                saved = json.loads(regular(self.snapshot / 'process-input.json'))
                self.process_environment, self.argv, self.unit_configuration = saved['environment'], saved['argv'], saved['unit']
                self.baseline_hashes = saved['hashes']
                properties = runtime.command(['systemctl', 'show', SERVICE,
                    '--property=User,Group,WorkingDirectory,Environment,EnvironmentFiles,FragmentPath,DropInPaths'])
                actual = dict(row.split('=', 1) for row in properties.splitlines() if '=' in row)
                require(actual == self.unit_configuration, 'service_configuration_changed')
                cwd = self.old['backend']
                self.overrides = {f'backend/{name}': regular(self.snapshot / name)
                    for name in ('appsettings.json', 'appsettings.Production.json') if (self.snapshot / name).exists()}
                require(all(regular(cwd / name.split('/', 1)[1]) == raw for name, raw in self.overrides.items())
                        and all(sha(regular(cwd / name)) == value for name, value in self.baseline_hashes.items()),
                        'recovery_base_changed')
            else:
                self.old = current
                pid, cwd, self.argv = self.process(current['backend'])
                self.process_environment = inspector.process_environment(pid)
                self.overrides = {f'backend/{name}': regular(cwd / name) for name in ('appsettings.json', 'appsettings.Production.json')
                                  if (cwd / name).exists()}
                self.baseline_hashes = {name: sha(regular(cwd / name)) for name in ASSEMBLIES}
            cfg, source = inspector.configuration(self.process_environment, self.argv, cwd)
            require(source['environment'] == 'Production' and source['content_root'] == str(cwd)
                    and inspector.policy_flags(cfg) == {
                'provider_is_sqlserver': True, 'jwt_compatible_with_production_policy': True,
                'jwt_lifetime_compatible': True, 'connection_compatible_with_production_policy': True,
                'development_seed_enabled': False, 'automatic_migrations_enabled': False,
                'https_redirection_enabled': True}, 'production_configuration_unexpected')
            self.configuration = cfg
            self.fields = inspector.connection_fields(inspector.first(cfg, 'ConnectionStrings:Pedidos',
                'PRODIMT_PEDIDOS_CONNECTION_STRING', default=''))
            require(inspector.permitted_connection(self.fields), 'database_target_unexpected')
            self.sql_tool = next((tool for tool in inspector.SQL_TOOLS if Path(tool).is_file()), None)
            require(self.sql_tool is not None, 'sqlcmd_required')
            history = self.history(self.app_json)
            if not self.recovery:
                status, body = self.http('/health/db')
                require(status == 200 and json.loads(body).get('database') == 'reachable', 'previous_health_failed')
                self.state['health_verified'] = True
            self.state.update(releases={part: path.name for part, path in current.items()}, migration_ids=history,
                              backend_hashes=self.baseline_hashes, env_preserved=True, recovery_pending=bool(self.recovery))
            if inventory:
                self.state.update(status='inventoried', phase='completed')
                return
            if not self.recovery and current == self.new:
                require(history == manifest['migrationsAfter'], 'current_migration_mismatch')
                self.verify_new_schema()
                for part in self.new:
                    self.verify_stage(part)
                self.health(True)
                self.state.update(status='already_current', phase='completed')
                return
            if not self.recovery:
                require(self.base_frontend is not None and re.fullmatch('[A-Za-z0-9][A-Za-z0-9._-]{0,119}', self.base_frontend),
                        'base_frontend_pin_required_use_inventory')
                require([current['backend'].name, current['frontend'].name] ==
                        [manifest['basePair'][0], self.base_frontend], 'base_release_unexpected')
            require(history in (manifest['migrationsBefore'], manifest['migrationsAfter']), 'migration_history_unexpected')
            if not self.recovery:
                require(history == manifest['migrationsBefore'], 'untracked_migration_state')
            self.migration_verified = history == manifest['migrationsAfter']
            if self.migration_verified:
                self.verify_new_schema()
            else:
                shape = self.app_json("""SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN
 (N'MachineOperators',N'MachineTeamTemplates',N'MachineTeamChanges',N'MachineWorkers',
 N'MachineProductionNotices',N'ProductionLotHandoffs')) AS new_tables,
 (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ProductionAllocations')
 AND name=N'ReleasedQuantity') AS new_columns FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;""")
                require(shape == {'new_tables': 0, 'new_columns': 0}, 'partial_schema_detected')
            require(shutil.disk_usage(ROOT).free >= 2 * sum(len(raw) for raw in files.values()) + 100 * 1024 * 1024,
                    'release_disk_insufficient')
            self.state.update(base_release_verified=True, recovery_pending=bool(self.recovery))

        def stage(self):
            for part, target in self.new.items():
                if target.exists():
                    self.verify_stage(part)
                    continue
                temporary = target.parent / ('.machine-stage-' + uuid.uuid4().hex)
                temporary.mkdir(mode=0o700)
                try:
                    for name, raw in self.expected(part).items():
                        path = temporary / name
                        path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
                        override = part + '/' + name in self.overrides
                        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                                             0o640 if override else 0o644)
                        with os.fdopen(descriptor, 'wb') as stream:
                            stream.write(raw)
                        os.chown(path, 0, 33 if override else 0)
                        path.chmod(0o640 if override else 0o644)
                    for directory in temporary.rglob('*'):
                        if directory.is_dir():
                            directory.chmod(0o755)
                    temporary.chmod(0o755)
                    temporary.rename(target)
                finally:
                    if temporary.exists():
                        shutil.rmtree(temporary)
                self.verify_stage(part)

        def database_backup(self):
            folder = runtime.SQL_BACKUPS / ('prodimt-' + self.snapshot.name)
            backup_root = runtime.SQL_BACKUPS.stat()
            require(stat.S_ISDIR(backup_root.st_mode) and runtime.SQL_BACKUPS.resolve(strict=True) == runtime.SQL_BACKUPS
                    and backup_root.st_uid == 999 and backup_root.st_gid == 988, 'sql_backup_directory_unexpected')
            require(shutil.disk_usage(runtime.SQL_BACKUPS).free >= (self.database_mib + 256) * 1024 * 1024,
                    'backup_disk_insufficient')
            if (self.snapshot / 'database-backup.json').exists():
                self.verify_backup()
                return
            # An earlier BACKUP may have completed before metadata was persisted.
            # Start another uniquely named attempt instead of truncating that file.
            folder = runtime.SQL_BACKUPS / ('prodimt-' + self.snapshot.name + '-' + uuid.uuid4().hex[:8])
            folder.mkdir(mode=0o700)
            os.chown(folder, backup_root.st_uid, backup_root.st_gid)
            path = folder / 'ProdimtPedidos-copy-only.bak'
            self.sql_run("BACKUP DATABASE [ProdimtPedidos] TO DISK=N'" + str(path) + "' WITH COPY_ONLY,CHECKSUM;", timeout=300)
            backup = path.lstat()
            require(stat.S_ISREG(backup.st_mode) and backup.st_nlink == 1 and backup.st_uid == backup_root.st_uid
                    and path.resolve(strict=True) == path and backup.st_size > 0, 'backup_file_unexpected')
            path.chmod(0o600)
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            metadata = self.snapshot / 'database-backup.json'
            metadata.write_text(json.dumps(dict(path=str(path), bytes=backup.st_size)))
            metadata.chmod(0o600)
            self.state['verified_database_backup'] = str(path)

        def verify_backup(self):
            value = json.loads(regular(self.snapshot / 'database-backup.json'))
            path = Path(value['path'])
            require(path.parent.parent == runtime.SQL_BACKUPS and re.fullmatch(re.escape('prodimt-' + self.snapshot.name) + '-[a-f0-9]{8}', path.parent.name)
                    and path.name == 'ProdimtPedidos-copy-only.bak' and path.lstat().st_size == value['bytes']
                    and not path.is_symlink(), 'recovery_backup_unexpected')
            self.sql_run("RESTORE VERIFYONLY FROM DISK=N'" + str(path) + "' WITH CHECKSUM;", timeout=300)
            self.state['verified_database_backup'] = str(path)

        def verify_new_schema(self):
            require(self.app_json(schema_query()) == dict(tables_count=6, released_column=1, released_check=1,
                role_present=1, permissions_count=2), 'machine_schema_unexpected')
            require(self.app_json(permission_query()) == expected_permissions(), 'machine_permissions_unexpected')
            self.state['schema_verified'] = True

        def health(self, new):
            super().health(new)
            if new:
                for path in ('/api/admin/machine-workspace/machines',
                             '/api/admin/machine-workspace/00000000-0000-0000-0000-000000000000/team'):
                    require(self.http(path)[0] == 401, 'machine_endpoint_not_protected')

        def write_marker(self):
            data = dict(source=EXPECTED_SOURCE, payload=EXPECTED_PAYLOAD_SHA256, snapshot=str(self.snapshot),
                        previous={part: path.name for part, path in self.old.items()},
                        migrationVerified=self.migration_verified)
            target = self.marker.with_name('.machine-marker-' + uuid.uuid4().hex)
            descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(descriptor, 'w') as stream:
                json.dump(data, stream)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(target, self.marker)

        def apply(self):
            self.preflight()
            if self.state['status'] == 'already_current':
                return
            self.state['phase'] = 'sql-admin-authentication'
            self.sql_login = runtime.prompt_secret('Usuario administrador SQL existente (oculto): ').strip()
            self.sql_password = runtime.prompt_secret('Contraseña SQL (oculta): ')
            require(0 < len(self.sql_login) <= 128 and 0 < len(self.sql_password) <= 512
                    and not any(c in self.sql_login + self.sql_password for c in '\0\r\n'), 'sql_credentials_invalid')
            admin = self.sql_json(runtime.ADMIN_QUERY)
            require(admin.get('database_name') == runtime.DATABASE and admin.get('view_definition') == 1
                    and (admin.get('sysadmin') == 1 or admin.get('db_owner') == 1 and admin.get('backup_permission') == 1
                         and (admin.get('verify_permission') == 1 or admin.get('dbcreator') == 1)),
                    'sql_admin_permissions_insufficient')
            require(self.history(self.sql_json) == (manifest['migrationsAfter'] if self.migration_verified
                    else manifest['migrationsBefore']), 'administrative_history_mismatch')
            self.database_mib = admin.get('files_mib')
            require(isinstance(self.database_mib, int) and self.database_mib > 0, 'database_size_unrecognized')
            if self.migration_verified:
                self.verify_backup()
            if not self.recovery:
                self.snapshot = BACKUPS / ('machine-workspace-update-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
                self.snapshot.mkdir(mode=0o700)
                (self.snapshot / 'api.env').write_bytes(self.environment)
                (self.snapshot / 'process-input.json').write_text(json.dumps(dict(environment=self.process_environment,
                    argv=self.argv, unit=self.unit_configuration, hashes=self.baseline_hashes)))
                for name, raw in self.overrides.items():
                    (self.snapshot / name.split('/', 1)[1]).write_bytes(raw)
                for path in self.snapshot.iterdir():
                    path.chmod(0o600)
            self.state.update(changes_made=True, private_snapshot=str(self.snapshot))
            self.state['phase'] = 'stage-releases'
            self.stage()
            for part in self.new:
                self.verify_stage(part)
            require(regular(ENV_FILE) == self.environment, 'environment_changed')
            if not self.recovery:
                self.links(self.old)
                self.process(self.old['backend'])
                require(all(sha(regular(self.old['backend'] / name)) == value for name, value in self.baseline_hashes.items()),
                        'base_backend_changed')
            candidate_argv = [str(self.new['backend'] / 'Prodimt.Pedidos.Api.dll')
                if Path(arg).name == 'Prodimt.Pedidos.Api.dll' else arg for arg in self.argv]
            candidate_cfg, candidate_source = inspector.configuration(self.process_environment,
                candidate_argv, self.new['backend'])
            require(candidate_cfg == self.configuration and candidate_source['content_root'] == str(self.new['backend']),
                    'effective_configuration_changed')
            self.write_marker()
            self.state['phase'] = 'stop-service'
            self.stop_requested = True
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            if not self.migration_verified:
                self.state['phase'] = 'backup-and-migrate'
                self.database_backup()
                self.state['migrations_may_have_changed'] = True
                self.sql_run(files['migration.sql'].decode('utf-8-sig'), timeout=120)
                require(self.history(self.sql_json) == manifest['migrationsAfter'], 'post_migration_history_unexpected')
                self.migration_verified = True
                self.verify_new_schema()
                self.state['database_migrations_run'] = True
                self.write_marker()
            self.state['phase'] = 'activate-releases'
            self.switch(self.new)
            runtime.command(['systemctl', 'start', SERVICE], timeout=45)
            self.health(True)
            self.marker.unlink()
            self.state.update(status='updated', phase='completed', health_verified=True, frontend_verified=True,
                              recovery_pending=False, migrations_after=21, releases={part: path.name for part, path in self.new.items()})

        def rollback(self):
            if not self.stop_requested:
                return
            runtime.command(['systemctl', 'stop', SERVICE], timeout=45)
            if self.history(self.sql_json) == manifest['migrationsBefore']:
                self.switch(self.old)
                runtime.command(['systemctl', 'start', SERVICE], timeout=45)
                self.health(False)
                self.marker.unlink(missing_ok=True)
                self.state.update(previous_health_verified=True, recovery_pending=False)
            else:
                self.state.update(recovery_pending=True, application_stopped_for_recovery=True)

    return MachineInstaller()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, default=Path('/root/prodimt-machine-workspace-payload.zip'))
    parser.add_argument('--inventory', action='store_true')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--base-frontend', help='Nombre exacto obtenido en --inventory cuando el paquete no lo fija')
    args = parser.parse_args()
    require(not (args.inventory and args.apply), 'choose_inventory_or_apply')
    require(os.geteuid() == 0, 'root_required')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    installer = make_installer(args.package, args.base_frontend)
    descriptor = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        meta = os.fstat(descriptor)
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid == 0 and meta.st_nlink == 1
                and meta.st_mode & 0o7777 == 0o600, 'lock_identity_unexpected')
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            if args.apply:
                installer.apply()
            else:
                installer.preflight(inventory=args.inventory)
                if installer.state['status'] == 'planned':
                    installer.state['phase'] = 'completed'
        except (Exception, KeyboardInterrupt) as error:
            installer.state.update(status='failed', issue=str(error) if isinstance(error, InstallError) else 'local_operation_failed')
            try:
                installer.rollback()
            except (Exception, KeyboardInterrupt):
                installer.state.update(recovery_pending=True, recovery_check_failed=True)
        print(json.dumps(installer.state, ensure_ascii=False, indent=2))
        return 0 if installer.state['status'] in ('updated', 'already_current', 'planned', 'inventoried') else 1
    finally:
        installer.sql_password = ''
        if hasattr(installer, 'fields'):
            installer.fields.clear()
        os.close(descriptor)


if __name__ == '__main__':
    os.umask(0o077)
    try:
        sys.exit(main())
    except (Exception, KeyboardInterrupt):
        print(json.dumps(dict(status='failed', issue='installation_not_started', changes_made=False)))
        sys.exit(1)
