"""Exercise resume verification in the actual shell wrapper; all network I/O is fake."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WRAPPER = ROOT / 'scripts/pilot/deploy-consolidation-c1-from-wsl.sh'


class C1ResumeTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.package = self.root / 'package'
        self.package.mkdir()
        self.wrapper = self.package / 'PRODIMT_Desplegar_C1_WSL.sh'
        self.wrapper.write_bytes(WRAPPER.read_bytes())
        self.source = 'a' * 40
        (self.package / 'source.json').write_text(json.dumps({'sourceCommit': self.source}))
        (self.package / 'PRODIMT_Instalar_C1.py').write_text('synthetic installer, never executed')
        (self.package / 'prodimt-consolidation-c1-payload.zip').write_bytes(b'synthetic payload, never executed')
        (self.package / 'PRODIMT_C1.bundle').write_bytes(b'synthetic bundle, never cloned')
        self.write_sums()
        self.results = self.package / 'resultados-c1/attempt'
        self.results.mkdir(parents=True)
        (self.results / 'package-SHA256SUMS.txt').write_bytes((self.package / 'SHA256SUMS.txt').read_bytes())
        self.report = dict(stats=dict(expected=240, unexpected=0, skipped=0, flaky=0), errors=[],
            suites=[dict(specs=[dict(tests=[{} for _ in range(240)])])])
        report = json.dumps(self.report).encode()
        (self.results / 'playwright.json').write_bytes(report)
        self.gate = dict(schema='prodimt.c1.browser-gate.v1', status='passed', sourceCommit=self.source,
            api='mock', suite='all', expectedCases=240, collected=240, passed=240, failed=0, skipped=0,
            flaky=0, exitCode=0, sqlServerIncluded=False, humanAcceptance=False,
            reportSha256=hashlib.sha256(report).hexdigest())
        self.write_gate()
        self.commands = self.root / 'commands'
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        for name, body in {'scp': 'echo scp >> "$C1_TEST_COMMANDS"',
                           'ssh': 'echo "$*" >> "$C1_TEST_COMMANDS"; exit 79',
                           'git': 'exit 97', 'node': 'exit 98'}.items():
            path = self.bin / name
            path.write_text('#!/bin/bash\n' + body + '\n')
            path.chmod(0o700)

    def write_sums(self):
        (self.package / 'SHA256SUMS.txt').write_text(''.join(
            hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.name + '\n'
            for path in sorted(self.package.iterdir()) if path.is_file() and path.name != 'SHA256SUMS.txt'))

    def write_gate(self):
        (self.results / 'prodimt-c1-browser-gate.json').write_text(json.dumps(self.gate))

    def run_wrapper(self):
        return subprocess.run(['bash', str(self.wrapper), '--resume-from-transfer', str(self.results)],
            env=dict(os.environ, PATH=str(self.bin) + ':' + os.environ['PATH'],
                     C1_TEST_COMMANDS=str(self.commands)), capture_output=True, text=True, timeout=15)

    def test_valid_same_package_gate_resumes_transfer_and_keeps_remote_preflight_and_apply(self):
        result = self.run_wrapper()
        self.assertEqual(result.returncode, 79, result.stderr)
        commands = self.commands.read_text()
        self.assertTrue(commands.startswith('scp\n'))
        self.assertIn('python3 /root/PRODIMT_Instalar_C1.py && python3', commands)
        self.assertIn('--apply --browser-gate /root/prodimt-c1-browser-gate.json', commands)

    def test_failed_incomplete_or_different_source_gate_never_reaches_transfer(self):
        original = self.gate.copy()
        for changes in ({'sourceCommit': 'b' * 40}, {'status': 'failed'}, {'passed': 239}, {'failed': 1},
                        {'skipped': 1}, {'flaky': 1}, {'expectedCases': 1}, {'suite': 'partial'},
                        {'exitCode': False}, {'sqlServerIncluded': 0}, {'humanAcceptance': True}):
            with self.subTest(changes=changes):
                self.gate = original | changes
                self.write_gate()
                self.assertNotEqual(self.run_wrapper().returncode, 0)
                self.assertFalse(self.commands.exists())

    def test_same_source_different_package_hashes_cannot_reuse_gate(self):
        (self.package / 'PRODIMT_Instalar_C1.py').write_text('different installer')
        self.write_sums()
        result = self.run_wrapper()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('El paquete difiere', result.stderr)
        self.assertFalse(self.commands.exists())

    def test_report_hash_mismatch_missing_report_and_wrong_counts_block_transfer(self):
        path = self.results / 'playwright.json'
        original = path.read_bytes()
        for case in ('hash', 'missing', 'counts', 'errors'):
            with self.subTest(case=case):
                path.write_bytes(original)
                report = json.loads(original)
                if case == 'missing':
                    path.unlink()
                elif case == 'hash':
                    path.write_bytes(original + b' ')
                else:
                    if case == 'counts':
                        report['stats']['expected'] = 239
                    else:
                        report['errors'] = [{'message': 'runner error'}]
                    path.write_text(json.dumps(report))
                    self.gate['reportSha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
                    self.write_gate()
                self.assertNotEqual(self.run_wrapper().returncode, 0)
                self.assertFalse(self.commands.exists())

    def test_package_mutation_and_gate_from_outside_package_block_transfer(self):
        original = self.results
        self.results = self.root
        self.assertNotEqual(self.run_wrapper().returncode, 0)
        self.results = original
        (self.package / 'PRODIMT_C1.bundle').write_bytes(b'changed')
        self.assertNotEqual(self.run_wrapper().returncode, 0)
        self.assertFalse(self.commands.exists())


if __name__ == '__main__':
    unittest.main()
