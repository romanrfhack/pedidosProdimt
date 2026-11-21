"""Exercise deployment ordering and recovery with fake external SQL/systemd I/O."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("machine_workspace_lifecycle",
    ROOT / "scripts/pilot/install-machine-workspace-update.py")
I = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(I)


class MachineWorkspaceLifecycleTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.releases = self.root / "app"
        self.backups = self.root / "backups"
        self.backups.mkdir()
        self.env = self.root / "api.env"
        self.env.write_bytes(b"synthetic environment")
        self.events = []
        self.history_ids = [f"migration-{number:02}" for number in range(20)]
        self.after = self.history_ids + [I.MIGRATION]
        self.manifest = dict(basePair=["previous-backend", "previous-frontend"],
            migrationsBefore=self.history_ids, migrationsAfter=self.after)
        self.current = {}
        for part, release in zip(("backend", "frontend"), self.manifest["basePair"]):
            target = self.releases / part / "releases" / release
            target.mkdir(parents=True)
            for name in I.ASSEMBLIES:
                (target / name).write_bytes(name.encode())
            (target / "appsettings.json").write_bytes(b"{}")
            (self.releases / part / "current").symlink_to(target)
            self.current[part] = target

        events = self.events

        class RuntimeInstaller:
            def expected(self, part):
                return {}

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

            def health(self, new):
                events.append("health-new" if new else "health-old")

            def http(self, path):
                if path.startswith("/api/"):
                    return 401, b"{}"
                return 200, b'{"database":"reachable"}'

            def sql_json(self, query):
                if query == "admin":
                    return dict(database_name="ProdimtPedidos", view_definition=1,
                        sysadmin=1, files_mib=1)
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
            prompt_secret=lambda _: "test-only", ADMIN_QUERY="admin", DATABASE="ProdimtPedidos",
            SQL_BACKUPS=self.root / "sql-backups")
        self.inspector = types.SimpleNamespace(configuration=lambda environment, argv, cwd:
            ({}, {"environment": "Production", "content_root": str(cwd)}))
        self.files = {
            "native-deployment-support.py": b"",
            "inspect-native.py": b"",
            "migration.sql": b"synthetic migration"
        }
        for name, value in [("ROOT", self.releases), ("BACKUPS", self.backups),
                            ("ENV_FILE", self.env), ("EXPECTED_SOURCE", "a" * 40),
                            ("EXPECTED_PAYLOAD_SHA256", "b" * 64)]:
            scope = patch.object(I, name, value)
            scope.start()
            self.addCleanup(scope.stop)
        scope = patch.object(I, "load_payload", return_value=(self.files, self.manifest))
        scope.start()
        self.addCleanup(scope.stop)
        scope = patch.object(I, "module", side_effect=lambda name, raw:
            self.runtime if name == "machine_native_support" else self.inspector)
        scope.start()
        self.addCleanup(scope.stop)
        scope = patch.object(I, "regular", side_effect=lambda path, *args: Path(path).read_bytes())
        scope.start()
        self.addCleanup(scope.stop)

    def installer(self, migrated=False):
        instance = I.make_installer(Path("synthetic-package.zip"))
        instance.old = self.current.copy()
        instance.environment = self.env.read_bytes()
        instance.overrides = {}
        instance.process_environment = {}
        instance.argv = ["dotnet", str(instance.old["backend"] / "Prodimt.Pedidos.Api.dll")]
        instance.unit_configuration = {"User": "www-data"}
        instance.baseline_hashes = {
            name: I.sha((instance.old["backend"] / name).read_bytes()) for name in I.ASSEMBLIES
        }
        instance.configuration = {}
        instance.database_history = self.after if migrated else self.history_ids
        instance.after_history = self.after
        instance.recovery = None
        instance.migration_verified = migrated
        instance.preflight = lambda: self.events.append("preflight")
        instance.stage = lambda: self.events.append("stage")
        instance.database_backup = lambda: self.events.append("verified-backup")
        instance.verify_new_schema = lambda: self.events.append("verify-schema")
        return instance

    def test_backup_completes_before_migration_and_activation(self):
        installer = self.installer()
        installer.apply()
        self.assertLess(self.events.index("verified-backup"), self.events.index("migrate"))
        self.assertLess(self.events.index("verify-schema"), self.events.index("activate-new"))
        self.assertEqual(installer.state["status"], "updated")
        self.assertFalse(installer.marker.exists())

    def test_backup_failure_does_not_run_migration_or_switch_releases(self):
        installer = self.installer()

        def failed_backup():
            raise I.InstallError("backup_verification_failed")

        installer.database_backup = failed_backup
        with self.assertRaisesRegex(I.InstallError, "backup_verification_failed"):
            installer.apply()
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("activate-new", self.events)
        installer.rollback()
        self.assertIn("activate-old", self.events)
        self.assertFalse(installer.marker.exists())

    def test_post_ddl_failure_preserves_marker_for_forward_recovery(self):
        installer = self.installer()

        def failed_health(new):
            if new:
                raise I.InstallError("health_failed")

        installer.health = failed_health
        with self.assertRaisesRegex(I.InstallError, "health_failed"):
            installer.apply()
        installer.rollback()
        self.assertTrue(installer.marker.exists())
        self.assertTrue(installer.state["recovery_pending"])
        self.assertNotIn("activate-old", self.events)

    def test_resume_requires_a_verified_backup_before_activation(self):
        installer = self.installer(migrated=True)
        installer.recovery = {"source": I.EXPECTED_SOURCE}
        installer.snapshot = self.backups / "machine-workspace-update-20261121T100000Z-aaaaaaaaaaaa"
        installer.snapshot.mkdir()

        def failed_backup_verification():
            raise I.InstallError("recovery_backup_unexpected")

        installer.verify_backup = failed_backup_verification
        with self.assertRaisesRegex(I.InstallError, "recovery_backup_unexpected"):
            installer.apply()
        self.assertNotIn("stage", self.events)
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("activate-new", self.events)

    def test_resume_uses_verified_backup_and_does_not_repeat_ddl(self):
        installer = self.installer(migrated=True)
        installer.recovery = {"source": I.EXPECTED_SOURCE}
        installer.snapshot = self.backups / "machine-workspace-update-20261121T100000Z-aaaaaaaaaaaa"
        installer.snapshot.mkdir()
        installer.marker.write_text("{}")
        installer.verify_backup = lambda: self.events.append("verify-backup")
        installer.apply()
        self.assertLess(self.events.index("verify-backup"), self.events.index("activate-new"))
        self.assertNotIn("migrate", self.events)
        self.assertNotIn("verified-backup", self.events)
        self.assertEqual(installer.state["status"], "updated")

    def test_stopped_recovery_preflight_uses_saved_process_configuration(self):
        installer = self.installer(migrated=True)
        snapshot = self.backups / "machine-workspace-update-20261121T100000Z-aaaaaaaaaaaa"
        snapshot.mkdir(mode=0o700)
        (snapshot / "api.env").write_bytes(installer.environment)
        (snapshot / "appsettings.json").write_bytes(b"{}")
        (snapshot / "process-input.json").write_text(json.dumps({
            "environment": {}, "argv": installer.argv,
            "unit": installer.unit_configuration, "hashes": installer.baseline_hashes
        }))
        installer.marker.write_text(json.dumps({
            "source": I.EXPECTED_SOURCE, "payload": I.EXPECTED_PAYLOAD_SHA256,
            "snapshot": str(snapshot),
            "previous": {part: target.name for part, target in self.current.items()}
        }))
        installer.marker.chmod(0o600)
        installer.process = lambda _: self.fail("Stopped recovery must not require a live process.")
        installer.http = lambda _: self.fail("Stopped recovery must not require a running API.")
        installer.app_json = lambda query: [{"MigrationId": name} for name in self.after]
        self.inspector.policy_flags = lambda _: {
            "provider_is_sqlserver": True, "jwt_compatible_with_production_policy": True,
            "jwt_lifetime_compatible": True, "connection_compatible_with_production_policy": True,
            "development_seed_enabled": False, "automatic_migrations_enabled": False,
            "https_redirection_enabled": True
        }
        self.inspector.connection_fields = lambda _: {"server": "synthetic"}
        self.inspector.first = lambda *args, **kwargs: ""
        self.inspector.permitted_connection = lambda _: True
        self.inspector.SQL_TOOLS = [str(self.env)]
        original_stat = Path.stat

        def root_owned(path, *args, **kwargs):
            values = list(original_stat(path, *args, **kwargs))
            values[4] = 0
            return os.stat_result(values)

        with patch.object(Path, "stat", root_owned):
            type(installer).preflight(installer)
        self.assertTrue(installer.migration_verified)
        self.assertTrue(installer.state["recovery_pending"])
        self.assertEqual(installer.snapshot, snapshot)
        self.assertNotIn("process", self.events)
        self.assertNotIn("stop", self.events)
        self.assertNotIn("migrate", self.events)

    def test_already_current_does_not_stop_or_prompt(self):
        installer = self.installer(migrated=True)
        installer.state["status"] = "already_current"
        self.runtime.prompt_secret = lambda _: self.fail("already_current must not prompt")
        installer.apply()
        self.assertEqual(self.events, ["preflight"])


if __name__ == "__main__":
    unittest.main()
