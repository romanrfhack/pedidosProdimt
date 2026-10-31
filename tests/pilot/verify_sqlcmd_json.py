"""Exercise the native inventory parser with the real disposable SQL CLI in CI."""
import importlib.util
from pathlib import Path
import subprocess


spec = importlib.util.spec_from_file_location(
    'native_inventory', Path(__file__).parents[2] / 'scripts/pilot/inspect-native.py')
inventory = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inventory)

# A value longer than the CLI default width detects accidental JSON truncation.
# The generated container password is expanded only inside the test container.
query = ("SET NOCOUNT ON; SELECT (SELECT 1 AS probe, "
         "REPLICATE(N'X',600) AS padding FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) "
         "AS ProdimtInspectionJson;")
command = ['docker', 'exec', 'prodimt-i02-sqlserver', 'bash', '-c',
           'export SQLCMDUSER=sa SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; '
           'exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -d master '
           '-b -l 5 -t 10 -y 0 -w 65535 -x -Q "$1"', 'native-json-probe', query]
output = subprocess.check_output(command, stderr=subprocess.PIPE, timeout=30).decode('utf-8')
result = inventory.parse_sql_output(output)
assert result == {'probe': 1, 'padding': 'X' * 600}, 'Native inventory CLI JSON contract failed.'
print('Native inventory SQL CLI JSON contract passed (600-character field).')
