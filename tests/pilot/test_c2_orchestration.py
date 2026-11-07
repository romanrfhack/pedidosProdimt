import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("c2_orchestration", Path(__file__).resolve().parents[2] / "scripts/dev/validate-c2.py")
c2 = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(c2)


class C2GateTests(unittest.TestCase):
    @staticmethod
    def result():
        return {"errors": [], "stats": {"expected": 1, "unexpected": 0, "skipped": 0, "flaky": 0},
                "suites": [{"specs": [{"tests": [{"status": "expected", "results": [{"status": "passed"}]}]}]}]}

    def test_complete_single_attempt_pass_is_accepted(self):
        self.assertEqual(1, c2.checked_browser(self.result(), 1)["passed"])

    def test_missing_cases_are_rejected_even_when_stats_claim_success(self):
        report = self.result()
        report["suites"] = []
        with self.assertRaises(c2.Failure):
            c2.checked_browser(report, 1)

    def test_global_error_skip_flakiness_failure_and_retry_never_pass(self):
        for key in ("unexpected", "skipped", "flaky"):
            report = self.result()
            report["stats"][key] = 1
            with self.subTest(key=key), self.assertRaises(c2.Failure):
                c2.checked_browser(report, 1)
        report = self.result()
        report["errors"] = [{"message": "global failure"}]
        with self.assertRaises(c2.Failure):
            c2.checked_browser(report, 1)
        report = self.result()
        report["suites"][0]["specs"][0]["tests"][0]["results"].insert(0, {"status": "failed"})
        with self.assertRaises(c2.Failure):
            c2.checked_browser(report, 1)

    def test_environment_excludes_operational_and_legacy_connections(self):
        with patch.dict(os.environ, {"ConnectionStrings__Pedidos": "ProdimtPedidos", "PRODIMT_SQL_TEST_CONNECTION": "operational",
                                     "PRODIMT_API_BASE_URL": "https://pedidos.prodimt.site", "ASPNETCORE_URLS": "http://0.0.0.0:80"}):
            clean = c2.clean_environment()
        self.assertFalse(any(key in clean for key in ("ConnectionStrings__Pedidos", "PRODIMT_SQL_TEST_CONNECTION",
                                                      "PRODIMT_API_BASE_URL", "ASPNETCORE_URLS")))

    def test_gate_rejects_another_or_dirty_source(self):
        with patch.object(c2.subprocess, "check_output", return_value="f" * 40):
            with self.assertRaisesRegex(c2.Failure, "source_mismatch"):
                c2.source_identity("a" * 40)
        with patch.object(c2.subprocess, "check_output", side_effect=["a" * 40, " M changed.cs"]):
            with self.assertRaisesRegex(c2.Failure, "source_dirty"):
                c2.source_identity("a" * 40)

    def test_failure_snapshot_redacts_synthetic_credentials_and_preserves_diagnosis(self):
        snapshot = 'textbox "Contrasena": ' + 'a' * 32 + 'Aa1!\nExpected: 200 Received: 403\n'
        snapshot += 'MSSQL_SA_PASSWORD=' + 'b' * 48 + 'Aa1!\n/cliente#token=c2-synthetic-' + 'c' * 32
        redacted = c2.redact_diagnostics(snapshot)
        self.assertNotIn('Aa1!', redacted)
        self.assertNotIn('c2-synthetic-', redacted)
        self.assertIn('Expected: 200 Received: 403', redacted)
        self.assertEqual(2, redacted.count('[synthetic-password-omitted]'))


if __name__ == "__main__":
    unittest.main()
