"""Integrity, source lineage and WSL boundaries for the collections update."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PILOT = ROOT / "scripts/pilot"
SOURCE = "a" * 40


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, PILOT / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


I = module("collections_payload_installer", "install-collections-update.py")
B = module("collections_payload_builder", "build-collections-package.py")
W = module("collections_payload_wsl", "deploy-collections-from-wsl.py")


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def sql_fixture():
    checks = {
        "CollectionCharges": ("CK_CollectionCharge_Quantity", "CK_CollectionCharge_Amount"),
        "CollectionChargeValuations": ("CK_CollectionValuation_Amount",),
        "CollectionPayments": ("CK_CollectionPayment_Amount",),
        "CollectionCashReceipts": ("CK_CollectionCashReceipt_Amount",),
        "CollectionCashReceiptLines": ("CK_CollectionCashReceiptLine_Amount",),
    }
    sql = "BEGIN TRANSACTION;\n"
    for table in I.TABLES:
        sql += "CREATE TABLE [" + table + "] ([Id] int"
        for constraint in checks[table]:
            sql += ", CONSTRAINT [" + constraint + "] CHECK ([Id] >= 0)"
        sql += ");\n"
        sql += "GRANT SELECT,INSERT ON dbo." + table + " TO prodimt_pedidos_app;\n"
        sql += "DENY UPDATE,DELETE ON dbo." + table + " TO prodimt_pedidos_app;\n"
    sql += "DECLARE @principal int = DATABASE_PRINCIPAL_ID(N'prodimt_pedidos_app');\n"
    sql += "SELECT N'operations.collections.work',N'operations.collections.read',N'operations.collections.receive';\n"
    sql += "INSERT INTO [__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'" + I.MIGRATION + "', N'10.0.8');\n"
    return sql + "COMMIT;\n"


def zip_bytes(files, mode=stat.S_IFREG | 0o644):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as output:
        for name, raw in files.items():
            info = zipfile.ZipInfo(name)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = mode << 16
            output.writestr(info, raw)
    return stream.getvalue()


class CollectionsPayloadTests(unittest.TestCase):
    def files(self):
        files = {
            "inspect-native.py": (PILOT / "inspect-native.py").read_bytes(),
            "native-deployment-support.py": (PILOT / "install-quick-capture-update.py").read_bytes(),
            "frontend-asset-support.py": (PILOT / "install-delivery-flow-update.py").read_bytes(),
            "migration.sql": sql_fixture().encode(),
            "backend/appsettings.json": b"{}",
            "backend/Prodimt.Pedidos.Api.runtimeconfig.json": b"{}",
            "backend/Prodimt.Pedidos.Api.deps.json": b"{}",
            "frontend/index.html": b'<script src="main.js"></script>',
            "frontend/main.js": b'import("./lazy.js");',
            "frontend/lazy.js": b'console.log("synthetic fixture");',
            "frontend/3rdpartylicenses.txt": b"Synthetic fixture notices",
        }
        files.update({"backend/" + name: b"synthetic assembly" for name in I.ASSEMBLIES})
        return files

    def payload(self, folder, changes=None, metadata=None, mode=stat.S_IFREG | 0o644):
        files = self.files() | (changes or {})
        manifest = dict(feature="delivery-collections-v1", sourceCommit=SOURCE, migration=I.MIGRATION,
            migrationsBefore=list(I.BASE_MIGRATIONS), migrationsAfter=[*I.BASE_MIGRATIONS, I.MIGRATION],
            basePair=[I.BASE_RELEASE, I.BASE_RELEASE],
            files={name: dict(bytes=len(raw), sha256=sha(raw)) for name, raw in files.items()})
        manifest.update(metadata or {})
        raw = zip_bytes(files | {"manifest.json": json.dumps(manifest).encode()}, mode)
        path = folder / "payload.zip"
        path.write_bytes(raw)
        return path, sha(raw)

    def load(self, path, checksum):
        with patch.object(I, "EXPECTED_SOURCE", SOURCE), patch.object(I, "EXPECTED_PAYLOAD_SHA256", checksum), \
                patch.object(I, "regular", side_effect=lambda path, *args: Path(path).read_bytes()):
            return I.load_payload(path)

    def test_loads_exact_source_baseline_and_asset_closure(self):
        with tempfile.TemporaryDirectory() as folder:
            path, checksum = self.payload(Path(folder))
            files, manifest = self.load(path, checksum)
        self.assertEqual(self.files(), files)
        self.assertEqual(21, len(manifest["migrationsBefore"]))
        self.assertEqual(22, len(manifest["migrationsAfter"]))
        self.assertEqual(I.MIGRATION, manifest["migrationsAfter"][-1])

    def test_mismatched_hash_never_executes_embedded_support(self):
        with tempfile.TemporaryDirectory() as folder:
            path, checksum = self.payload(Path(folder))
            path.write_bytes(path.read_bytes() + b"tampering")
            with patch.object(I, "module") as execute:
                with self.assertRaisesRegex(I.InstallError, "^payload_hash_mismatch$"):
                    self.load(path, checksum)
                execute.assert_not_called()

    def test_same_length_but_different_baseline_is_rejected(self):
        different = list(I.BASE_MIGRATIONS)
        different[0] = "20260101000000_UnrelatedSchema"
        for metadata in ({"sourceCommit": "b" * 40}, {"feature": "machine-workspace-v1"},
                         {"migrationsBefore": different, "migrationsAfter": different + [I.MIGRATION]},
                         {"basePair": ["unknown-release", I.BASE_RELEASE]}, {"files": {}},
                         {"migration": "20260101000000_WrongMigration"}):
            with self.subTest(metadata=metadata), tempfile.TemporaryDirectory() as folder:
                path, checksum = self.payload(Path(folder), metadata=metadata)
                with self.assertRaises(I.InstallError):
                    self.load(path, checksum)

    def test_untrusted_paths_debug_secrets_and_special_files_are_rejected(self):
        for name in ("../escape", "/absolute", "backend/../escape", "backend//double",
                     "backend/.env", "backend/secret.key", "backend/private.pem",
                     "backend/appsettings.Development.json", "frontend/debug.map"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as folder:
                path, checksum = self.payload(Path(folder), {name: b"fixture"})
                with self.assertRaises(I.InstallError):
                    self.load(path, checksum)
        with tempfile.TemporaryDirectory() as folder:
            path, checksum = self.payload(Path(folder), mode=stat.S_IFLNK | 0o777)
            with self.assertRaisesRegex(I.InstallError, "^payload_path_invalid$"):
                self.load(path, checksum)

    def test_missing_runtime_metadata_and_lazy_chunks_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path, checksum = self.payload(Path(folder), {"frontend/lazy.js": b'import("./missing.js");'})
            with self.assertRaisesRegex(Exception, "^frontend_asset_reference_missing$"):
                self.load(path, checksum)
        for missing in ("backend/Prodimt.Pedidos.Api.runtimeconfig.json", "backend/Prodimt.Pedidos.Api.deps.json"):
            with self.subTest(missing=missing), tempfile.TemporaryDirectory() as folder:
                files = self.files()
                files.pop(missing)
                with patch.object(self, "files", return_value=files):
                    path, checksum = self.payload(Path(folder))
                    with self.assertRaises(I.InstallError):
                        self.load(path, checksum)

    def test_sql_requires_requested_transaction_identity_permissions_and_checks(self):
        original = sql_fixture()
        I.validate_migration(original)
        mutations = [
            original.replace("BEGIN TRANSACTION;", ""), original + "COMMIT;\n",
            original.replace(I.MIGRATION, "WrongMigration"),
            original.replace("CREATE TABLE [CollectionPayments]", "CREATE TABLE [Unrelated]"),
            original.replace("GRANT SELECT,INSERT ON dbo.CollectionPayments", "GRANT SELECT ON dbo.CollectionPayments"),
            original.replace("DENY UPDATE,DELETE ON dbo.CollectionCharges", "DENY UPDATE ON dbo.CollectionCharges"),
            original + "DROP TABLE [Orders];", original + "DELETE FROM [Orders];",
            original + "TRUNCATE TABLE [Customers];", original + "ALTER LOGIN [app] DISABLE;",
            original + "ALTER TABLE [Orders] ADD [Unexpected] int;",
        ]
        for sql in mutations:
            with self.subTest(sql=sql):
                with self.assertRaises(I.InstallError):
                    I.validate_migration(sql)

    def test_runtime_schema_and_permission_profile_match_actual_financial_tables(self):
        expected = {"CollectionCharges", "CollectionChargeValuations", "CollectionPayments",
                    "CollectionCashReceipts", "CollectionCashReceiptLines"}
        self.assertEqual(expected, set(I.TABLES))
        permission_profile = I.expected_permissions()
        for table in expected:
            for action, bit in (("SELECT", 1), ("INSERT", 1), ("UPDATE", 0), ("DELETE", 0)):
                self.assertEqual(bit, permission_profile[table + "_" + action])
        migration = (ROOT / "src/Prodimt.Pedidos.Infrastructure/Persistence/Migrations" / (I.MIGRATION + ".cs")).read_text(encoding="utf-8-sig")
        for check in ("CK_CollectionCharge_Quantity", "CK_CollectionCharge_Amount", "CK_CollectionValuation_Amount",
                      "CK_CollectionPayment_Amount", "CK_CollectionCashReceipt_Amount", "CK_CollectionCashReceiptLine_Amount"):
            self.assertIn(check, migration)
            self.assertIn(check, I.schema_query())


class CollectionsBuilderAndWslTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)

    def builds(self):
        backend = self.folder / "backend"
        frontend = self.folder / "frontend"
        backend.mkdir()
        frontend.mkdir()
        for name in I.ASSEMBLIES:
            (backend / name).write_bytes(b"synthetic reviewed assembly")
        (backend / "Prodimt.Pedidos.Api.runtimeconfig.json").write_text("{}")
        (backend / "Prodimt.Pedidos.Api.deps.json").write_text("{}")
        for name in ("appsettings.json", "appsettings.Production.json"):
            (backend / name).write_bytes((ROOT / "src/Prodimt.Pedidos.Api" / name).read_bytes())
        (frontend / "index.html").write_text('<script src="main.js"></script>')
        markers = [marker.decode() for marker in B.FRONTEND_MARKERS]
        (frontend / "main.js").write_text("console.log(" + json.dumps(markers) + ");")
        (frontend / "3rdpartylicenses.txt").write_text("fixture notices")
        sql = self.folder / "migration.sql"
        sql.write_text(sql_fixture())
        return backend, frontend, sql

    def test_source_requires_current_clean_commit(self):
        for outputs in ([SOURCE, "?? unreviewed.py\n"], ["b" * 40, ""], [SOURCE, "", "b" * 40]):
            with self.subTest(outputs=outputs), patch.object(B.subprocess, "check_output", side_effect=outputs):
                with self.assertRaises(ValueError):
                    B.validate_source(SOURCE)

    def test_builder_produces_pinned_installable_archive_and_exact_checksum_manifest(self):
        backend, frontend, sql = self.builds()
        output = self.folder / "collections.zip"
        with patch.object(B, "validate_source"):
            B.build(backend, frontend, sql, output, SOURCE, I.BASE_RELEASE, I.BASE_RELEASE)
        with zipfile.ZipFile(output) as outer:
            content = {name.removeprefix(B.PREFIX): outer.read(name) for name in outer.namelist()}
        required = {"PRODIMT_Instalar_Cobranza.py", "prodimt-collections-payload.zip",
                    "PRODIMT_Desplegar_Cobranza_WSL.py", "SHA256SUMS.txt", "LEEME.txt"}
        self.assertEqual(required, set(content))
        for row in content["SHA256SUMS.txt"].decode().splitlines():
            checksum, filename = row.split("  ", 1)
            self.assertEqual(sha(content[filename]), checksum)
        self.assertIn(SOURCE, content["PRODIMT_Instalar_Cobranza.py"].decode())
        self.assertIn(sha(content["prodimt-collections-payload.zip"]), content["PRODIMT_Instalar_Cobranza.py"].decode())
        payload = self.folder / "payload.zip"
        payload.write_bytes(content["prodimt-collections-payload.zip"])
        with patch.object(I, "EXPECTED_SOURCE", SOURCE), \
                patch.object(I, "EXPECTED_PAYLOAD_SHA256", sha(payload.read_bytes())), \
                patch.object(I, "regular", side_effect=lambda path, *args: Path(path).read_bytes()):
            _, manifest = I.load_payload(payload)
        self.assertEqual([I.BASE_RELEASE, I.BASE_RELEASE], manifest["basePair"])
        self.assertEqual(22, len(manifest["migrationsAfter"]))

    def test_builder_excludes_pdb_and_rejects_config_drift(self):
        backend, frontend, sql = self.builds()
        output = self.folder / "collections.zip"
        (backend / "debug.pdb").write_bytes(b"fixture debug symbols")
        with patch.object(B, "validate_source"):
            B.build(backend, frontend, sql, output, SOURCE, I.BASE_RELEASE, I.BASE_RELEASE)
        with zipfile.ZipFile(output) as outer:
            raw = outer.read(B.PREFIX + "prodimt-collections-payload.zip")
        with zipfile.ZipFile(io.BytesIO(raw)) as payload:
            self.assertNotIn("backend/debug.pdb", payload.namelist())
        (backend / "appsettings.json").write_text('{"CustomerAccess":{"TokenProtection":{"Key":"synthetic-secret"}}}')
        with patch.object(B, "validate_source"), self.assertRaises(ValueError):
            B.build(backend, frontend, sql, output, SOURCE, I.BASE_RELEASE, I.BASE_RELEASE)

    def test_builder_rejects_symlinks_unknown_base_and_stale_frontend(self):
        backend, frontend, sql = self.builds()
        output = self.folder / "collections.zip"
        with patch.object(B, "validate_source"):
            with self.assertRaisesRegex(ValueError, "^base_release_unexpected$"):
                B.build(backend, frontend, sql, output, SOURCE, "unknown-backend", I.BASE_RELEASE)
            original = (frontend / "main.js").read_bytes()
            (frontend / "main.js").write_text("console.log('old frontend');")
            with self.assertRaisesRegex(ValueError, "^frontend_feature_missing$"):
                B.build(backend, frontend, sql, output, SOURCE)
            (frontend / "main.js").write_bytes(original)
            (backend / "linked.dll").symlink_to(backend / I.ASSEMBLIES[0])
            with self.assertRaisesRegex(ValueError, "^build_symlink_not_allowed$"):
                B.build(backend, frontend, sql, output, SOURCE)

    def test_wsl_requires_complete_unique_checksums_and_regular_payload_files(self):
        for name in W.PACKAGE_FILES:
            (self.folder / name).write_bytes(("fixture:" + name).encode())
        valid = "".join(sha((self.folder / name).read_bytes()) + "  " + name + "\n" for name in sorted(W.PACKAGE_FILES))
        checksums = self.folder / "SHA256SUMS.txt"
        with patch.object(W, "ROOT", self.folder):
            checksums.write_text(valid)
            W.verify_files()
            for invalid in ("", valid.splitlines()[0] + "\n", valid + valid.splitlines()[0] + "\n"):
                with self.subTest(checksums=invalid):
                    checksums.write_text(invalid)
                    with self.assertRaises(RuntimeError):
                        W.verify_files()
            checksums.write_text(valid)
            payload = self.folder / "prodimt-collections-payload.zip"
            saved = payload.read_bytes()
            payload.write_bytes(saved + b"tampering")
            with self.assertRaisesRegex(RuntimeError, "^file_checksum_mismatch$"):
                W.verify_files()
            payload.unlink()
            target = self.folder / "private-file"
            target.write_bytes(saved)
            payload.symlink_to(target)
            with self.assertRaisesRegex(RuntimeError, "^file_checksum_mismatch$"):
                W.verify_files()

    def test_remote_error_output_is_not_echoed_into_local_logs(self):
        synthetic = "synthetic-private-credential"
        result = type("Result", (), dict(returncode=1, stdout="connection failed " + synthetic,
                                          stderr="password=" + synthetic))()
        error_output = io.StringIO()
        with patch.object(W.subprocess, "run", return_value=result), contextlib.redirect_stderr(error_output):
            with self.assertRaises(RuntimeError):
                W.run(["ssh", W.SERVER, "safe-inventory-command"], capture=True)
        self.assertNotIn(synthetic, error_output.getvalue())

    def test_wsl_does_not_apply_after_failed_plan_or_leak_credentials(self):
        calls = []

        def run(arguments, capture=False):
            calls.append(arguments)
            if capture and "--inventory" in arguments[-1]:
                return json.dumps({"status": "inventoried", "releases": {"backend": I.BASE_RELEASE, "frontend": I.BASE_RELEASE},
                                   "migration_ids": list(I.BASE_MIGRATIONS), "health_verified": True})
            if capture:
                return json.dumps({"status": "failed", "issue": "base_release_unexpected"})
            return None

        with patch.object(W, "ROOT", self.folder), patch.object(W, "SOURCE", SOURCE), \
                patch.object(W, "verify_files"), patch.object(W, "run", side_effect=run), \
                patch.object(sys, "argv", ["wrapper", "--apply"]), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(RuntimeError):
                W.main()
        self.assertFalse(any("--apply" in str(argument) for call in calls for argument in call))
        self.assertNotIn("password", json.dumps(calls).lower())
        self.assertTrue((self.folder / "PLAN_COBRANZA.json").exists())

    def test_wsl_checksum_failure_stops_before_any_remote_step(self):
        with patch.object(W, "SOURCE", SOURCE), patch.object(W, "verify_files", side_effect=RuntimeError("checksum")), \
                patch.object(W, "run") as execute, patch.object(sys, "argv", ["wrapper", "--apply"]):
            with self.assertRaises(RuntimeError):
                W.main()
            execute.assert_not_called()


if __name__ == "__main__":
    unittest.main()
