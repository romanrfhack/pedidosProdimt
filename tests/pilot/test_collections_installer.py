"""Collections deployment ordering with isolated filesystem and external-I/O boundaries."""
import importlib.util
import inspect
import json
import os
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("collections_installer_lifecycle",
    ROOT / "scripts/pilot/install-collections-update.py")
I = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(I)


def preservation_snapshots():
    receiver = "11111111-1111-1111-1111-111111111111"
    previous = dict(columns_metadata=[], indexes_metadata=[], sql_permissions=[],
        unchanged_counts=[{"table_name": "Orders", "row_count": 4}],
        security_roles=[dict(Id=receiver, Name="Administración – Mostrador", Description="Custom role",
                             IsActive=True, IsSystem=False)],
        security_permissions=[dict(Id="18180000-0000-0000-0001-000000000001", Key="orders.read",
                                   IsActive=True, IsSystem=True)],
        security_grants=[dict(RoleId=receiver, PermissionId="18180000-0000-0000-0001-000000000001")],
        users_hash="a" * 64, user_roles_hash="b" * 64, user_permissions_hash="c" * 64)
    current = json.loads(json.dumps(previous))
    for number, key in ((24, "operations.collections.work"), (25, "operations.collections.receive"),
                        (26, "operations.collections.read")):
        identity = f"18180000-0000-0000-0001-{number:012}"
        current["security_permissions"].append(dict(Id=identity, Key=key, IsActive=True, IsSystem=True))
        current["security_grants"].append(dict(RoleId="18180000-0000-0000-0000-000000000001", PermissionId=identity))
        if number == 24:
            current["security_grants"].append(dict(RoleId="22220000-0000-0000-0000-000000000001", PermissionId=identity))
        else:
            current["security_grants"].append(dict(RoleId=receiver, PermissionId=identity))
    return previous, current


class CollectionsInstallerLifecycleTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        self.releases = self.folder / "app"
        self.backups = self.folder / "backups"
        self.backups.mkdir()
        self.env = self.folder / "api.env"
        self.env.write_bytes(b"SYNTHETIC_PRIVATE_ENV=preserved\n")
        self.events = []
        self.before = list(I.BASE_MIGRATIONS)
        self.after = self.before + [I.MIGRATION]
        self.manifest = dict(basePair=[I.BASE_RELEASE, I.BASE_RELEASE],
            migrationsBefore=self.before, migrationsAfter=self.after)
        self.current = {}
        for part in ("backend", "frontend"):
            target = self.releases / part / "releases" / I.BASE_RELEASE
            target.mkdir(parents=True)
            for name in I.ASSEMBLIES:
                (target / name).write_bytes(("previous:" + name).encode())
            (target / "appsettings.json").write_bytes(b"{}")
            (self.releases / part / "current").symlink_to(target)
            self.current[part] = target

        events = self.events

        class RuntimeInstaller:
            def expected(self, part):
                return {name: ("new:" + name).encode() for name in I.ASSEMBLIES} if part == "backend" else {}

            def verify_stage(self, part):
                events.append("verify-stage:" + part)

            def process(self, expected):
                events.append("process")
                self.unit_configuration = {"User": "www-data"}
                return 101, expected, ["dotnet", str(expected / "Prodimt.Pedidos.Api.dll")]

            def links(self, expected):
                events.append("links")

            def switch(self, target):
                events.append("activate-new" if target == self.new else "activate-old")
                for part, destination in target.items():
                    link = self.root_releases / part / "current"
                    link.unlink()
                    link.symlink_to(destination)

            def health(self, new):
                events.append("health-new" if new else "health-old")

            def http(self, path):
                return (401, b"{}") if path.startswith("/api/") else (200, b'{"database":"reachable"}')

            def sql_json(self, query):
                if query == "admin":
                    return dict(database_name="ProdimtPedidos", view_definition=1, sysadmin=1, files_mib=1)
                if "columns_metadata" in query:
                    return self.preservation_after if self.database_history == self.after_history else self.preservation_before
                return [{"MigrationId": name} for name in self.database_history]

            def sql_run(self, sql, timeout=30):
                events.append("migrate")
                self.database_history = self.after_history

        def command(argv, timeout=30):
            if argv[1] == "show":
                return "User=www-data\n"
            events.append(argv[1])
            return ""

        self.runtime = types.SimpleNamespace(Installer=RuntimeInstaller,
            directory_safe=lambda path: None, command=command,
            prompt_secret=lambda _: "synthetic-private-credential", ADMIN_QUERY="admin",
            DATABASE="ProdimtPedidos", SQL_BACKUPS=self.folder / "sql-backups")
        self.inspector = types.SimpleNamespace(SERVER="127.0.0.1,14330", DATABASE="ProdimtPedidos",
            configuration=lambda environment, argv, cwd: ({}, {"environment": "Production", "content_root": str(cwd)}),
            process_environment=lambda _: {}, SQL_TOOLS=[str(self.env)],
            first=lambda *args, **kwargs: "", connection_fields=lambda _: {"server": "synthetic"},
            permitted_connection=lambda _: True, policy_flags=lambda _: {
                "provider_is_sqlserver": True, "jwt_compatible_with_production_policy": True,
                "jwt_lifetime_compatible": True, "connection_compatible_with_production_policy": True,
                "development_seed_enabled": False, "automatic_migrations_enabled": False,
                "https_redirection_enabled": True,
            })
        self.files = {
            "native-deployment-support.py": b"",
            "inspect-native.py": b"",
            "migration.sql": b"synthetic migration"
        }
        for name, value in [("ROOT", self.releases), ("BACKUPS", self.backups),
                ("ENV_FILE", self.env), ("EXPECTED_SOURCE", "a" * 40),
                ("EXPECTED_PAYLOAD_SHA256", "b" * 64)]:
            scoped = patch.object(I, name, value)
            scoped.start()
            self.addCleanup(scoped.stop)
        scoped = patch.object(I, "load_payload", return_value=(self.files, self.manifest))
        scoped.start()
        self.addCleanup(scoped.stop)
        scoped = patch.object(I, "module", side_effect=lambda name, raw:
            self.runtime if "native_support" in name else self.inspector)
        scoped.start()
        self.addCleanup(scoped.stop)
        scoped = patch.object(I, "regular", side_effect=lambda path, *args: Path(path).read_bytes())
        scoped.start()
        self.addCleanup(scoped.stop)

    def installer(self, migrated=False):
        instance = I.make_installer(Path("synthetic-package.zip"))
        instance.old = self.current.copy()
        instance.root_releases = self.releases
        instance.preservation_before, instance.preservation_after = preservation_snapshots()
        instance.environment = self.env.read_bytes()
        instance.overrides = {}
        instance.process_environment = {}
        instance.argv = ["dotnet", str(instance.old["backend"] / "Prodimt.Pedidos.Api.dll")]
        instance.unit_configuration = {"User": "www-data"}
        instance.baseline_hashes = {name: I.sha((instance.old["backend"] / name).read_bytes()) for name in I.ASSEMBLIES}
        instance.configuration = {}
        instance.database_history = self.after if migrated else self.before
        instance.after_history = self.after
        instance.recovery = None
        instance.migration_verified = migrated
        instance.preflight = lambda: self.events.append("preflight")
        instance.database_backup = lambda: self.events.append("verified-backup")
        instance.verify_new_schema = lambda: self.events.append("verify-schema")
        instance.app_json = lambda query: [{"MigrationId": name} for name in instance.database_history]

        def stage():
            self.events.append("stage")
            for target in instance.new.values():
                target.mkdir(parents=True, exist_ok=True)
                for name in I.ASSEMBLIES:
                    (target / name).write_bytes(("new:" + name).encode())
        instance.stage = stage
        return instance

    def test_rejected_preflight_never_stops_or_stages(self):
        instance = self.installer()
        instance.preflight = lambda: (_ for _ in ()).throw(I.InstallError("base_release_unexpected"))
        with self.assertRaisesRegex(I.InstallError, "^base_release_unexpected$"):
            instance.apply()
        instance.rollback()
        self.assertEqual([], self.events)

    def test_backup_and_schema_are_verified_before_activation(self):
        instance = self.installer()
        instance.apply()
        self.assertLess(self.events.index("verified-backup"), self.events.index("migrate"))
        self.assertLess(self.events.index("verify-schema"), self.events.index("activate-new"))
        self.assertEqual("updated", instance.state["status"])
        self.assertFalse(instance.marker.exists())
        self.assertEqual(self.after, instance.state["migration_ids"])
        self.assertEqual(22, instance.state["migrations_after"])
        self.assertEqual({name: I.sha(("new:" + name).encode()) for name in I.ASSEMBLIES}, instance.state["backend_hashes"])
        self.assertNotEqual(instance.baseline_hashes, instance.state["backend_hashes"])

    def test_backup_failure_rolls_back_application_without_running_ddl(self):
        instance = self.installer()
        instance.database_backup = lambda: (_ for _ in ()).throw(I.InstallError("backup_verification_failed"))
        with self.assertRaisesRegex(I.InstallError, "backup_verification_failed"):
            instance.apply()
        instance.rollback()
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("activate-new", self.events)
        self.assertIn("activate-old", self.events)
        self.assertFalse(instance.marker.exists())

    def test_post_ddl_failure_retains_forward_recovery_and_never_restores_database(self):
        instance = self.installer()
        instance.health = lambda new: (_ for _ in ()).throw(I.InstallError("health_failed"))
        with self.assertRaisesRegex(I.InstallError, "health_failed"):
            instance.apply()
        instance.rollback()
        self.assertTrue(instance.marker.exists())
        self.assertTrue(instance.state["recovery_pending"])
        self.assertNotIn("activate-old", self.events)
        self.assertFalse(instance.state["database_automatically_restored"])

    def test_recovery_requires_verified_backup_and_skips_all_ddl(self):
        instance = self.installer(migrated=True)
        instance.recovery = {"source": I.EXPECTED_SOURCE}
        instance.snapshot = self.backups / "collections-update-20261122T100000Z-aaaaaaaaaaaa"
        instance.snapshot.mkdir()
        instance.marker.write_text("{}")
        (instance.snapshot / "database-preservation.json").write_text(json.dumps(instance.preservation_before))
        instance.verify_backup = lambda: self.events.append("verify-backup")
        instance.apply()
        self.assertLess(self.events.index("verify-backup"), self.events.index("activate-new"))
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("verified-backup", self.events)
        self.assertEqual(self.after, instance.state["migration_ids"])
        self.assertEqual("updated", instance.state["status"])

    def test_recovery_backup_error_blocks_staging_and_activation(self):
        instance = self.installer(migrated=True)
        instance.recovery = {"source": I.EXPECTED_SOURCE}
        instance.snapshot = self.backups / "collections-update-20261122T100000Z-aaaaaaaaaaaa"
        instance.snapshot.mkdir()
        instance.verify_backup = lambda: (_ for _ in ()).throw(I.InstallError("recovery_backup_unexpected"))
        with self.assertRaisesRegex(I.InstallError, "recovery_backup_unexpected"):
            instance.apply()
        self.assertNotIn("stage", self.events)
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("activate-new", self.events)

    def test_stopped_recovery_uses_private_snapshot_without_requiring_live_api(self):
        instance = self.installer(migrated=True)
        snapshot = self.backups / "collections-update-20261122T100000Z-aaaaaaaaaaaa"
        snapshot.mkdir(mode=0o700)
        (snapshot / "api.env").write_bytes(instance.environment)
        (snapshot / "appsettings.json").write_bytes(b"{}")
        (snapshot / "process-input.json").write_text(json.dumps({
            "environment": {}, "argv": instance.argv, "unit": instance.unit_configuration, "hashes": instance.baseline_hashes}))
        instance.marker.write_text(json.dumps({"source": I.EXPECTED_SOURCE, "payload": I.EXPECTED_PAYLOAD_SHA256,
            "snapshot": str(snapshot), "previous": {part: target.name for part, target in self.current.items()}}))
        instance.marker.chmod(0o600)
        instance.process = lambda _: self.fail("Recovery cannot depend on a running API process.")
        instance.http = lambda _: self.fail("Recovery cannot depend on a healthy old API.")
        original_stat = Path.stat

        def root_owned(path, *args, **kwargs):
            values = list(original_stat(path, *args, **kwargs))
            values[4] = 0
            return os.stat_result(values)

        with patch.object(Path, "stat", root_owned):
            type(instance).preflight(instance)
        self.assertTrue(instance.migration_verified)
        self.assertTrue(instance.state["recovery_pending"])
        self.assertNotIn("process", self.events)
        self.assertNotIn("stop", self.events)
        self.assertNotIn("migrate", self.events)

    def test_readonly_inventory_has_no_mutating_steps_and_reports_current_baseline(self):
        instance = self.installer()
        instance.database_history = self.before
        type(instance).preflight(instance, inventory=True)
        self.assertEqual("inventoried", instance.state["status"])
        self.assertEqual(self.before, instance.state["migration_ids"])
        self.assertEqual(instance.baseline_hashes, instance.state["backend_hashes"])
        self.assertFalse(instance.state["changes_made"])
        for mutation in ("stage", "stop", "migrate", "activate-new"):
            self.assertNotIn(mutation, self.events)

    def test_existing_schema_permissions_and_row_counts_are_preserved(self):
        instance = self.installer()
        instance.snapshot = self.backups / "collections-update-20261122T100000Z-aaaaaaaaaaaa"
        instance.snapshot.mkdir(mode=0o700)
        instance.preserve_database(capture=True)
        saved = instance.snapshot / "database-preservation.json"
        self.assertEqual(0o600, saved.stat().st_mode & 0o777)
        original = saved.read_bytes()
        instance.database_history = instance.after_history
        instance.preserve_database()
        self.assertTrue(instance.state["existing_schema_preserved"])
        self.assertTrue(instance.state["existing_sql_permissions_preserved"])
        self.assertTrue(instance.state["existing_row_counts_preserved"])
        for field in ("columns_metadata", "indexes_metadata", "sql_permissions", "unchanged_counts"):
            with self.subTest(field=field):
                initial = instance.preservation_after[field]
                instance.preservation_after[field] = [{"unexpected": "changed"}]
                with self.assertRaisesRegex(I.InstallError, "^existing_database_metadata_or_permissions_changed$"):
                    instance.preserve_database()
                instance.preservation_after[field] = initial
        self.assertEqual(original, saved.read_bytes())

    def test_existing_custom_roles_grants_and_user_auth_cannot_be_rewritten(self):
        before, after = preservation_snapshots()
        I.verify_preservation(before, after)
        for field in ("users_hash", "user_roles_hash", "user_permissions_hash"):
            with self.subTest(field=field):
                modified = json.loads(json.dumps(after))
                modified[field] = "d" * 64
                with self.assertRaisesRegex(I.InstallError, "^existing_database_metadata_or_permissions_changed$"):
                    I.verify_preservation(before, modified)
        for modify in ("role-name", "removed-grant", "old-permission"):
            with self.subTest(modify=modify):
                modified = json.loads(json.dumps(after))
                if modify == "role-name":
                    modified["security_roles"][0]["Name"] = "Altered existing role"
                elif modify == "removed-grant":
                    modified["security_grants"].pop(0)
                else:
                    modified["security_permissions"][0]["IsActive"] = False
                with self.assertRaisesRegex(I.InstallError, "^existing_security_row_changed$"):
                    I.verify_preservation(before, modified)

    def test_append_only_permission_mismatch_blocks_schema_acceptance(self):
        instance = self.installer(migrated=True)
        expected_shape = dict(tables_count=5, checks_count=6, receiver_present=1, permissions_count=3)
        observed = I.expected_permissions()
        observed["CollectionPayments_UPDATE"] = 1
        instance.app_json = lambda query: expected_shape if "tables_count" in query else observed
        with self.assertRaisesRegex(I.InstallError, "^collections_permissions_unexpected$"):
            type(instance).verify_new_schema(instance)
        self.assertNotIn("activate-new", self.events)

    def test_already_current_requires_no_prompt_or_stop(self):
        instance = self.installer(migrated=True)
        instance.state["status"] = "already_current"
        self.runtime.prompt_secret = lambda _: self.fail("Already-current updates cannot prompt.")
        instance.apply()
        self.assertEqual(["preflight"], self.events)


if __name__ == "__main__":
    unittest.main()
