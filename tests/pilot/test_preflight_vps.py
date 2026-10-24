import contextlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('preflight_vps', Path(__file__).parents[2] / 'scripts/pilot/preflight-vps.py')
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


class PreflightVpsTests(unittest.TestCase):
    def test_inventory_filters_sensitive_docker_fields_and_never_runs_mutation(self):
        commands = []

        def fake_run(arguments, timeout=12):
            commands.append(arguments)
            if arguments[:2] == ['docker', 'info']:
                return 0, json.dumps({'Architecture': 'x86_64', 'MemTotal': 8 * 1024**3,
                                      'ContainersRunning': 3, 'DockerRootDir': '/does-not-exist',
                                      'Username': 'PRIVATE_VALUE_MUST_NOT_PRINT',
                                      'Env': ['SECRET=PRIVATE_VALUE_MUST_NOT_PRINT']}), ''
            if arguments[:2] == ['ss', '-H']:
                return 0, 'LISTEN 0 128 127.0.0.1:5098 0.0.0.0:*\n', ''
            return 0, '', ''

        output = io.StringIO()
        with patch.object(preflight, 'run', side_effect=fake_run), \
                patch.object(preflight, 'nginx_inventory', return_value={'configuration_test_run': False}), \
                patch.object(preflight.socket, 'getaddrinfo', return_value=[]), contextlib.redirect_stdout(output):
            preflight.main()
        content = output.getvalue()
        report = json.loads(content)
        self.assertFalse(report['changes_made'])
        self.assertFalse(report['secrets_or_full_config_printed'])
        self.assertEqual(8192, report['docker']['memory_MiB'])
        self.assertNotIn('PRIVATE_VALUE_MUST_NOT_PRINT', content)
        self.assertIn('MemAvailable_MiB', report['memory'])
        self.assertTrue(report['capacity']['operator_review_required'])
        for arguments in commands:
            self.assertNotIn('restart', arguments)
            self.assertNotIn('reload', arguments)
            self.assertNotIn('inspect', arguments)
            self.assertNotIn('-T', arguments)
            self.assertNotIn('-t', arguments)


if __name__ == '__main__':
    unittest.main()
