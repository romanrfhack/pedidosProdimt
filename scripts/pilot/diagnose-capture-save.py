#!/usr/bin/env python3
"""Read only native PRODIMT journals; emit allowlisted error metadata, never log text.

Does not read configuration, connect to SQL, call the API, retry an order or
change services. No third-party packages are required.
"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import resource
import subprocess

REVISION = '20261030-capture-save-diagnosis1'
SERVICE = 'prodimt-pedidos-api.service'
ROOT = Path('/var/www/prodimt-pedidos')
CLEAN_ENV = {'PATH': '/usr/bin:/bin', 'LANG': 'C.UTF-8', 'LC_ALL': 'C.UTF-8'}
EXCEPTIONS = (
    'System.InvalidOperationException', 'System.ArgumentException',
    'System.NullReferenceException', 'System.NotSupportedException',
    'System.Text.Json.JsonException', 'System.Text.Json.JsonReaderException',
    'Microsoft.EntityFrameworkCore.DbUpdateException',
    'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException',
    'Microsoft.Data.SqlClient.SqlException', 'System.Data.SqlClient.SqlException',
    'System.TimeoutException', 'System.OperationCanceledException',
)
FRAMES = (
    'AdminOrderCaptureService', 'OrderCaptureEndpoints', 'EfOrderOperationStore',
    'CatalogTransaction', 'AdminOrderService', 'EfSalesChannelRepository',
    'EfOrderRepository', 'OrderLineFactory',
)
TABLES = frozenset((
    'Orders', 'OrderLines', 'OrderOperations', 'OrderAuditLogs', 'SalesChannels',
    'Customers', 'Products', 'CustomerFrequentProducts', 'Machines',
    'CustomerMachineAssignments', 'OrderChangeNotices', 'AuditLogs',
))
LOG_HEADER = re.compile(r'^(?:trce|dbug|info|warn|fail|crit):\s')


class DiagnosisError(Exception):
    """Only a fixed non-sensitive issue code may be passed."""


def command(args):
    try:
        result = subprocess.run(args, capture_output=True, text=True, errors='replace',
                                stdin=subprocess.DEVNULL, timeout=25, check=False,
                                env=CLEAN_ENV)
    except (OSError, subprocess.TimeoutExpired):
        raise DiagnosisError('read_command_unavailable_or_timeout') from None
    if result.returncode:
        raise DiagnosisError('read_command_failed')
    return result.stdout


def log_events(raw):
    # Console logging may be one multiline journal record, or one record per
    # line. Group at .NET severity headers before matching capture stack frames.
    events, lines, stamp = [], [], None
    invalid = 0
    for row in raw.splitlines():
        try:
            entry = json.loads(row)
        except (ValueError, TypeError):
            invalid += 1
            continue
        if not isinstance(entry, dict) or not isinstance(entry.get('MESSAGE'), str):
            invalid += 1
            continue
        for line in entry['MESSAGE'].splitlines():
            if LOG_HEADER.match(line) and lines:
                events.append((stamp, '\n'.join(lines)))
                lines = []
            if not lines:
                stamp = entry.get('__REALTIME_TIMESTAMP')
            if len(lines) < 600:
                lines.append(line[:16000])
    if lines:
        events.append((stamp, '\n'.join(lines)))
    return events, invalid


def utc_timestamp(stamp):
    if not isinstance(stamp, str) or not re.fullmatch(r'[0-9]{1,17}', stamp):
        return None
    try:
        return datetime.fromtimestamp(int(stamp) / 1_000_000, timezone.utc).isoformat()
    except (ValueError, OverflowError, OSError):
        return None


def classify_event(stamp, message):
    types = [name for name in EXCEPTIONS if re.search(
        r'(?<![A-Za-z0-9_.])' + re.escape(name) + r'(?:\s+\(0x[0-9a-fA-F]+\))?:', message)]
    if not types:
        return None
    # All returned strings below are constants or allowlisted names. Never
    # return exception messages, SQL statements/parameters, URLs or stack text.
    numbers = sorted({int(n) for n in re.findall(
        r'(?m)^\s*Error Number:\s*(-?[0-9]{1,6}),\s*State:\s*[0-9]+,\s*Class:\s*[0-9]+', message)})
    categories = []
    if "Sales channel 'AdminManualCapture' is not configured." in message:
        categories.append('admin_sales_channel_missing')
    if 'Sequence contains more than one element' in message and 'EfSalesChannelRepository' in message:
        categories.append('sales_channel_type_not_unique')
    if 'does not support user-initiated transactions' in message:
        categories.append('execution_strategy_transaction_conflict')
    if set(numbers).intersection((229, 262, 297, 15151, 15247)):
        categories.append('sql_permission_denied')
    if set(numbers).intersection((2601, 2627)):
        categories.append('sql_unique_constraint')
    if set(numbers).intersection((8152, 2628)):
        categories.append('sql_value_too_long')
    if 1205 in numbers:
        categories.append('sql_deadlock')
    if 1934 in numbers:
        categories.append('sql_session_options_incompatible')
    if -2 in numbers:
        categories.append('sql_timeout')
    denied = {name for name in re.findall(
        r"permission was denied on the object '([^']+)'", message) if name in TABLES}
    frames = [frame for frame in FRAMES if re.search(
        r'\bat Prodimt\.Pedidos\.[^\n]*\b' + re.escape(frame) + r'[.+<(]', message)]
    return dict(timestamp_utc=utc_timestamp(stamp), exception_types=types,
                sql_error_numbers=numbers[:12], categories=categories or ['unclassified_exception'],
                denied_tables=sorted(denied), application_frames=frames)


def summarize_journal(raw):
    events, invalid = log_events(raw)
    capture, other = [], []
    for stamp, message in events:
        result = classify_event(stamp, message)
        if result is None:
            continue
        if any(frame in result['application_frames'] for frame in ('AdminOrderCaptureService', 'OrderCaptureEndpoints')):
            capture.append(result)
        else:
            other.append(result)
    return dict(journal_records=len(raw.splitlines()), invalid_records=invalid,
                capture_error_events=capture[-5:], other_error_events=other[-3:],
                capture_error_found=bool(capture))


def diagnose(hours=6):
    report = dict(revision=REVISION, mode='read-only', changes_made=False,
                  database_queries_run=False, api_requests_sent=False,
                  order_replay_attempted=False, raw_log_text_printed=False,
                  secret_values_printed=False, status='failed', phase='preflight')
    try:
        if os.geteuid() != 0:
            raise DiagnosisError('root_required_for_service_journal')
        if not isinstance(hours, int) or not 1 <= hours <= 24:
            raise DiagnosisError('hours_out_of_range')
        report['phase'] = 'service-state'
        state = command(['systemctl', 'show', SERVICE, '--property=ActiveState', '--value']).strip()
        report['service_active'] = state == 'active'
        report['releases'] = {
            part: 'i07' if (ROOT / part / 'current').resolve().name == 'capture-46fed604732e' else 'other_or_missing'
            for part in ('backend', 'frontend')}
        report['phase'] = 'service-journal'
        raw = command(['journalctl', '--unit', SERVICE, '--since', f'-{hours} hours',
                       '--lines', '4000', '--no-pager', '--output=json',
                       '--output-fields=MESSAGE,__REALTIME_TIMESTAMP'])
        report.update(summarize_journal(raw))
        report['window_hours'] = hours
        report['status'] = 'inspected'
        report['phase'] = 'completed'
    except DiagnosisError as exc:
        report['issue'] = str(exc)
    except Exception:
        report['issue'] = 'diagnostic_local_failure'
    return report


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--hours', type=int, choices=range(1, 25), default=6)
    args = parser.parse_args()
    report = diagnose(args.hours)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report['status'] == 'inspected' else 1


if __name__ == '__main__':
    raise SystemExit(main())
