import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location('inspect_native', Path(__file__).parents[2] / 'scripts/pilot/inspect-native.py')
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)
SECRET = 'fictional-private-value-do-not-print-92873465'


def approved_values():
    return {'server': native.SERVER, 'database': native.DATABASE, 'user id': native.LOGIN, 'password': SECRET}


class NativeInspectionTests(unittest.TestCase):
    def test_configuration_uses_running_environment_and_cli_precedence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'appsettings.json').write_text(json.dumps({'Persistence': {'Provider': 'InMemory'}}))
            (root / 'appsettings.Production.json').write_text(json.dumps({'Persistence': {'Provider': 'SqlServer'}}))
            env = {'DOTNET_ENVIRONMENT': 'Production', 'ASPNETCORE_ENVIRONMENT': 'Staging',
                   'Persistence__Provider': 'InMemory', 'Authentication__Jwt__SigningKey': SECRET}
            cfg, source = native.configuration(env, ['dotnet', 'Prodimt.Pedidos.Api.dll', '--Persistence:Provider=SqlServer'], root)
            self.assertEqual('Production', source['environment'])
            self.assertEqual('SqlServer', cfg['persistence:provider'])
            self.assertTrue(native.policy_flags(cfg)['jwt_compatible_with_production_policy'])
            self.assertNotIn(SECRET, json.dumps(source))

    def test_empty_primary_setting_does_not_fall_back_to_valid_alias(self):
        flags = native.policy_flags({'authentication:jwt:signingkey': '', 'prodimt_jwt_signing_key': SECRET,
                                     'connectionstrings:pedidos': '', 'prodimt_pedidos_connection_string': SECRET})
        self.assertFalse(flags['jwt_compatible_with_production_policy'])
        self.assertFalse(flags['connection_compatible_with_production_policy'])

    def test_production_policy_flags_are_booleans_and_match_whitespace_and_demo_guards(self):
        for key in (native.DEVELOPMENT_KEY, 'CHANGE_ME_' + SECRET, 'REPLACE_WITH_' + SECRET, 'x'):
            self.assertFalse(native.policy_flags({'authentication:jwt:signingkey': key})['jwt_compatible_with_production_policy'])
        flags = native.policy_flags({'developmentseed:enabled': ' true ', 'developmentseed:applymigrations': 'True'})
        self.assertTrue(flags['development_seed_enabled'])
        self.assertTrue(flags['automatic_migrations_enabled'])
        self.assertTrue(all(isinstance(value, bool) for value in flags.values()))

    def test_unknown_configuration_stops_before_sql(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(native, 'query_sql') as sql:
            with self.assertRaisesRegex(native.InspectionError, 'application_arguments_unsupported'):
                native.configuration({}, ['dotnet', 'Prodimt.Pedidos.Api.dll', '--unknown'], Path(directory))
            with self.assertRaisesRegex(native.InspectionError, 'development_user_secrets_not_inspected'):
                native.configuration({'DOTNET_ENVIRONMENT': 'Development'}, ['dotnet', 'Prodimt.Pedidos.Api.dll'], Path(directory))
            sql.assert_not_called()

    def test_malformed_json_and_case_collisions_fail_without_secret_in_error(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / 'appsettings.json'
            for contents in ('{secret:' + SECRET, '{"A":"' + SECRET + '","a":4}'):
                path.write_text(contents)
                with self.assertRaises(native.InspectionError) as error:
                    native.configuration({}, ['dotnet', 'Prodimt.Pedidos.Api.dll'], root)
                self.assertNotIn(SECRET, str(error.exception))

    def test_ambiguous_environment_connection_sources_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(native.InspectionError, 'ambiguous_configuration_keys'):
                native.configuration({'ConnectionStrings__Pedidos': 'a', 'SQLCONNSTR_Pedidos': 'b'},
                                     ['dotnet', 'Prodimt.Pedidos.Api.dll'], Path(directory))

    def test_connection_parser_handles_password_semicolons_and_escaped_quotes(self):
        for quoted, expected in [('"one;two""three"', 'one;two"three'), ("'one;two''three'", "one;two'three")]:
            fields = native.connection_fields('Server=127.0.0.1,14330;Database=ProdimtPedidos;Uid=prodimt_pedidos_app;Pwd=' + quoted)
            self.assertEqual(expected, fields['password'])
            self.assertTrue(native.permitted_connection(fields))

    def test_malformed_or_ambiguous_connection_never_connects(self):
        for text in ('Server=x;Password="unterminated;' + SECRET, 'Server=a;Data Source=b'):
            with self.assertRaises(native.InspectionError) as error:
                native.connection_fields(text)
            self.assertNotIn(SECRET, str(error.exception))
        for key, value in [('server', 'outside.example,14330'), ('database', 'OtherDatabase'),
                           ('user id', 'sa'), ('integrated security', 'true'), ('failover partner', 'other')]:
            values = approved_values()
            values[key] = value
            with patch.object(native, 'command') as command:
                result = native.sql_inventory(native.SQL_TOOLS[0], values)
                self.assertFalse(result['queried'])
                command.assert_not_called()

    def test_sql_password_is_env_only_and_no_parent_startup_variables_are_inherited(self):
        with patch.dict(os.environ, {'SQLCMDINI': '/private/startup.sql', 'SQLCMDSERVER': 'outside', 'OTHER_SECRET': SECRET}):
            with patch.object(native, 'command', return_value=(0, '{"database_name":"ProdimtPedidos"}')) as command:
                native.query_sql(native.SQL_TOOLS[0], approved_values(), native.SQL_METADATA)
        args = command.call_args.args[0]
        env = command.call_args.kwargs['env']
        self.assertNotIn(SECRET, repr(args))
        self.assertNotIn('-P', args)
        self.assertNotIn('-U', args)
        self.assertNotIn('-h', args)
        self.assertNotIn('-W', args)
        self.assertEqual(SECRET, env['SQLCMDPASSWORD'])
        self.assertEqual(native.LOGIN, env['SQLCMDUSER'])
        self.assertEqual(set(native.CLEAN_ENV) | {'SQLCMDPASSWORD', 'SQLCMDUSER'}, set(env))
        self.assertEqual(native.SERVER, args[args.index('-S') + 1])
        self.assertEqual(native.DATABASE, args[args.index('-d') + 1])

    def test_arbitrary_sql_and_tool_paths_cannot_run(self):
        with patch.object(native, 'command') as command:
            for tool, query in [('/tmp/sqlcmd', native.SQL_METADATA), (native.SQL_TOOLS[0], 'BACKUP DATABASE x;')]:
                with self.assertRaisesRegex(native.InspectionError, 'sql_query_not_authorized'):
                    native.query_sql(tool, approved_values(), query)
            command.assert_not_called()

    def test_sql_json_parser_accepts_only_json_or_exact_column_header(self):
        expected = {'database_name': native.DATABASE}
        data = json.dumps(expected)
        self.assertEqual(expected, native.parse_sql_output(data + '\n'))
        self.assertEqual(expected, native.parse_sql_output('ProdimtInspectionJson\n---------------------\n' + data))
        for text in (SECRET + '\n' + data, 'AnotherColumn\n-----\n' + data,
                     'ProdimtInspectionJson\nwarning\n' + data, data + '\n(1 rows affected)', data + data):
            with self.assertRaises(native.InspectionError) as error:
                native.parse_sql_output(text)
            self.assertNotIn(SECRET, str(error.exception))

    def test_sql_errors_and_extra_result_fields_are_not_printed(self):
        with patch.object(native, 'command', return_value=(1, SECRET)):
            result = native.sql_inventory(native.SQL_TOOLS[0], approved_values())
        self.assertEqual('sql_read_failed', result['issue'])
        self.assertNotIn(SECRET, json.dumps(result))
        data = {'database_name': native.DATABASE, 'history_exists': 0, 'orders_exists': 0, 'unrequested': SECRET}
        with patch.object(native, 'query_sql', return_value=data):
            result = native.sql_inventory(native.SQL_TOOLS[0], approved_values())
        self.assertNotIn(SECRET, json.dumps(result))

    def test_subprocess_never_returns_stderr_or_uses_interactive_input(self):
        with patch.object(native.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1, '', SECRET)) as run:
            self.assertEqual((1, ''), native.command(['test']))
        self.assertEqual(subprocess.DEVNULL, run.call_args.kwargs['stdin'])
        self.assertFalse(run.call_args.kwargs.get('shell', False))

    def test_changed_process_is_rejected(self):
        with patch.object(native, 'command', return_value=(0, '52\n')), patch.object(native, 'process_identity', return_value='a'):
            with self.assertRaisesRegex(native.InspectionError, 'service_changed_during_inspection'):
                native.assert_same_process(51, 'a')
        with patch.object(native, 'command', return_value=(0, '51\n')), patch.object(native, 'process_identity', return_value='b'):
            with self.assertRaisesRegex(native.InspectionError, 'service_changed_during_inspection'):
                native.assert_same_process(51, 'a')

    def test_unexpected_exception_is_redacted_at_entrypoint(self):
        output = io.StringIO()
        with patch.object(native, 'inspect', side_effect=ValueError(SECRET)), contextlib.redirect_stdout(output):
            native.main()
        self.assertNotIn(SECRET, output.getvalue())
        self.assertFalse(json.loads(output.getvalue())['changes_made'])


if __name__ == '__main__':
    unittest.main()
