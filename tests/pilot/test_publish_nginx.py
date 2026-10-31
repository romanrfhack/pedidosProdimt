import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import socket
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('publish_nginx', Path(__file__).parents[2] / 'scripts/pilot/publish-nginx.py')
publish = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publish)


class PublishNginxTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.available = self.root / 'available/prodimt.conf'
        self.enabled = self.root / 'enabled/prodimt.conf'
        self.available.parent.mkdir()
        self.enabled.parent.mkdir()
        self.patches = [patch.object(publish, 'AVAILABLE', self.available),
                        patch.object(publish, 'ENABLED', self.enabled),
                        patch.object(publish, 'BACKUP_DIR', self.root / 'backups')]
        for item in self.patches:
            item.start()
        self.addCleanup(self.temp.cleanup)
        self.addCleanup(patch.stopall)

    def test_plan_never_runs_commands_or_creates_files(self):
        output = io.StringIO()
        with patch.object(publish.sys, 'argv', ['publish-nginx.py']), patch.object(publish, 'run') as runner:
            with contextlib.redirect_stdout(output):
                publish.main()
        plan = json.loads(output.getvalue())
        self.assertEqual('plan', plan['mode'])
        self.assertEqual('absent', plan['current_managed_phase'])
        self.assertFalse(plan['changes_other_vhosts'])
        self.assertFalse(self.available.exists())
        self.assertFalse(self.enabled.exists())
        runner.assert_not_called()

    def test_unmanaged_vhost_is_never_overwritten(self):
        original = 'server { server_name unrelated.example; }\n'
        self.available.write_text(original)
        with self.assertRaisesRegex(RuntimeError, 'not be overwritten'):
            publish.activate(publish.config(False))
        self.assertEqual(original, self.available.read_text())
        self.assertFalse(self.enabled.exists())

    def test_foreign_enabled_link_is_preserved(self):
        foreign = self.root / 'foreign.conf'
        foreign.write_text('foreign')
        self.enabled.symlink_to(foreign)
        with self.assertRaisesRegex(RuntimeError, 'points somewhere else'):
            publish.owned_configuration()
        self.assertEqual(foreign, self.enabled.resolve())

    def test_hard_link_is_not_replaced(self):
        self.available.write_text(publish.config(False))
        os.link(self.available, self.root / 'another-name.conf')
        with self.assertRaisesRegex(RuntimeError, 'hard links'):
            publish.owned_configuration()

    def test_failed_http_probe_removes_only_new_managed_files(self):
        unrelated = self.enabled.parent / 'other.conf'
        unrelated.write_text('other site')
        with patch.object(publish, 'require_command', return_value=''), patch.object(publish, 'run', return_value=(0, '', '')):
            with self.assertRaisesRegex(RuntimeError, 'probe failed'):
                publish.activate(publish.config(False), lambda: (_ for _ in ()).throw(RuntimeError('probe failed')))
        self.assertFalse(self.available.exists())
        self.assertFalse(self.enabled.is_symlink())
        self.assertEqual('other site', unrelated.read_text())

    def test_failed_https_restores_original_and_keeps_private_backup(self):
        previous = publish.config(False)
        self.available.write_text(previous)
        self.enabled.symlink_to(self.available)
        with patch.object(publish, 'require_command', side_effect=RuntimeError('invalid config')), \
                patch.object(publish, 'run', return_value=(0, '', '')):
            with self.assertRaisesRegex(RuntimeError, 'invalid config'):
                publish.activate(publish.config(True))
        self.assertEqual(previous, self.available.read_text())
        self.assertEqual(self.available, self.enabled.resolve())
        backups = list((self.root / 'backups').glob('*.conf'))
        self.assertEqual(1, len(backups))
        self.assertEqual(previous, backups[0].read_text())
        self.assertEqual(0o600, backups[0].stat().st_mode & 0o777)

    def test_rollback_reload_failure_is_reported(self):
        with patch.object(publish, 'require_command', side_effect=RuntimeError('reload failed')), \
                patch.object(publish, 'run', return_value=(1, '', '')):
            with self.assertRaisesRegex(RuntimeError, 'could not reload'):
                publish.activate(publish.config(False))
        self.assertFalse(self.available.exists())

    def test_effective_foreign_vhost_matching_target_is_blocked(self):
        config = '# configuration file /etc/nginx/conf.d/existing.conf:\nserver { server_name pedidos.prodimt.site; }\n'
        with patch.object(publish, 'require_command', return_value=config):
            with self.assertRaisesRegex(RuntimeError, 'existing vhost covers'):
                publish.inspect_nginx()

    def test_effective_unrelated_vhost_is_allowed(self):
        config = '# configuration file /etc/nginx/conf.d/existing.conf:\nserver { server_name another.example; }\n'
        with patch.object(publish, 'require_command', return_value=config):
            self.assertEqual(config, publish.inspect_nginx())

    def test_dns_rejects_other_ipv4_and_unconfigured_ipv6(self):
        expected = (socket.AF_INET, socket.SOCK_STREAM, 6, '', (publish.EXPECTED_IPV4, 443))
        extra = (socket.AF_INET6, socket.SOCK_STREAM, 6, '', ('2001:db8::10', 443, 0, 0))
        with patch.object(socket, 'getaddrinfo', return_value=[expected]):
            publish.assert_dns()
        for rows in ([expected, extra], [(socket.AF_INET, socket.SOCK_STREAM, 6, '', ('192.0.2.1', 443))]):
            with patch.object(socket, 'getaddrinfo', return_value=rows):
                with self.assertRaisesRegex(RuntimeError, 'DNS must resolve only'):
                    publish.assert_dns()

    def test_certificate_issue_requires_explicit_terms(self):
        with patch.object(publish, 'check_certificate', return_value=False), patch.object(publish, 'require_command') as runner:
            with self.assertRaisesRegex(RuntimeError, 'accept-terms'):
                publish.ensure_certificate('operator@example.com', False)
            runner.assert_not_called()

    def test_existing_valid_certificate_does_not_trigger_issuance(self):
        with patch.object(publish, 'check_certificate', return_value=True), patch.object(publish, 'require_command') as runner:
            publish.ensure_certificate(None, False)
            runner.assert_not_called()

    def test_certificate_issuance_is_scoped_to_one_domain_webroot(self):
        with patch.object(publish, 'check_certificate', side_effect=[False, True]), \
                patch.object(publish, 'require_command', return_value='') as runner:
            publish.ensure_certificate('operator@example.com', True)
        arguments = runner.call_args.args[0]
        self.assertEqual('certonly', arguments[1])
        self.assertIn('--webroot', arguments)
        self.assertNotIn('--nginx', arguments)
        self.assertEqual(publish.DOMAIN, arguments[arguments.index('-d') + 1])
        self.assertEqual(publish.DOMAIN, arguments[arguments.index('--cert-name') + 1])

    def test_http_template_does_not_expose_application(self):
        http = publish.config(False)
        self.assertIn('return 503', http)
        self.assertNotIn('proxy_pass', http)
        self.assertIn('/.well-known/acme-challenge/', http)
        self.assertIn('return 308 https://pedidos.prodimt.site', publish.config(True))


if __name__ == '__main__':
    unittest.main()
