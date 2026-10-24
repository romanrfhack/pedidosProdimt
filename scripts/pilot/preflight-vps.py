#!/usr/bin/env python3
"""Read-only VPS inventory. Never prints environment variables or full configs."""
import glob
import json
import os
import re
import shutil
import socket
import subprocess
from pathlib import Path

DOMAIN = 'pedidos.prodimt.site'
EXPECTED_IPV4 = '194.238.26.70'


def run(arguments, timeout=12):
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, timeout=timeout, check=False)
        return result.returncode, result.stdout.strip(), result.stderr.strip()
    except (OSError, subprocess.TimeoutExpired):
        return 127, '', ''


def version(arguments):
    code, output, error = run(arguments)
    return (output or error).splitlines()[0][:240] if code == 0 and (output or error) else None


def nginx_inventory():
    """Inspect config text only: nginx -t/-T can open log files and is not used."""
    pending = [Path('/etc/nginx/nginx.conf')]
    visited, target_files, review_files, unreadable, outside_includes = set(), set(), set(), [], []
    while pending and len(visited) < 400:
        path = pending.pop()
        try:
            path = path.resolve()
            if path in visited:
                continue
            visited.add(path)
            text = path.read_text(encoding='utf-8')
        except (OSError, UnicodeError):
            unreadable.append(str(path))
            continue
        text = re.sub(r'#[^\n]*', '', text)
        for names in re.findall(r'\bserver_name\s+([^;]+);', text):
            for name in names.split():
                name = name.strip('"\'')
                if name == DOMAIN or name == '.prodimt.site' or name == '*.prodimt.site':
                    target_files.add(str(path))
                if name.startswith('~') or '$' in name:
                    review_files.add(str(path))
        for include in re.findall(r'\binclude\s+([^;]+);', text):
            include = include.strip().strip('"\'')
            if '$' in include:
                outside_includes.append(include)
                continue
            include_path = Path(include)
            if not include_path.is_absolute():
                include_path = Path('/etc/nginx') / include_path
            if not str(include_path).startswith('/etc/nginx/'):
                outside_includes.append(str(include_path))
                continue
            pending.extend(Path(name) for name in glob.glob(str(include_path)))
    return {'files_read': len(visited), 'target_or_wildcard_files': sorted(target_files),
            'regex_or_dynamic_server_name_files': sorted(review_files),
            'unreadable_files': unreadable, 'external_or_dynamic_includes_not_read': outside_includes,
            'scan_limit_reached': bool(pending),
            'configuration_test_run': False}


def main():
    memory = {}
    try:
        for line in Path('/proc/meminfo').read_text().splitlines():
            if line.startswith(('MemTotal:', 'MemAvailable:', 'SwapTotal:', 'SwapFree:')):
                key, value, *_ = line.split()
                memory[key.rstrip(':') + '_MiB'] = int(value) // 1024
    except OSError:
        pass
    disks = {}
    for name in ('/', '/opt', '/var/lib/docker'):
        if Path(name).exists():
            usage = shutil.disk_usage(name)
            disks[name] = {'total_GiB': round(usage.total / 1024 ** 3, 2),
                           'free_GiB': round(usage.free / 1024 ** 3, 2)}
    addresses = []
    try:
        addresses = sorted({row[4][0] for row in socket.getaddrinfo(DOMAIN, 443, type=socket.SOCK_STREAM)})
    except socket.gaierror:
        pass
    port_code, port_output, _ = run(['ss', '-H', '-ltn'])
    listeners = []
    if port_code == 0:
        for line in port_output.splitlines():
            columns = line.split()
            if len(columns) >= 4 and columns[3].rsplit(':', 1)[-1] in ('80', '443', '5098'):
                listeners.append(columns[3])
    info_code, info, _ = run(['docker', 'info', '--format', '{{json .}}'])
    docker = {'available': info_code == 0, 'version': version(['docker', '--version']),
              'compose_version': version(['docker', 'compose', 'version', '--short'])}
    if info_code == 0:
        try:
            parsed = json.loads(info)
            if not isinstance(parsed, dict):
                raise ValueError('Expected Docker information object.')
        except (ValueError, TypeError):
            parsed = {}
            docker['available'] = False
        docker.update({'architecture': parsed.get('Architecture'),
                       'memory_MiB': int(parsed.get('MemTotal', 0)) // 1024 ** 2,
                       'running_containers': parsed.get('ContainersRunning'),
                       'root_directory': parsed.get('DockerRootDir')})
        docker_root = parsed.get('DockerRootDir')
        if docker_root and Path(docker_root).exists():
            usage = shutil.disk_usage(docker_root)
            disks[docker_root] = {'total_GiB': round(usage.total / 1024 ** 3, 2),
                                 'free_GiB': round(usage.free / 1024 ** 3, 2)}
        code, rows, _ = run(['docker', 'ps', '-a', '--filter', 'label=com.docker.compose.project=prodimt-pilot',
                            '--format', '{{.Names}}\t{{.Status}}\t{{.Ports}}'])
        docker['pilot_containers'] = rows.splitlines() if code == 0 else []
        code, rows, _ = run(['docker', 'volume', 'ls', '--filter', 'name=prodimt-pilot_sql-data', '--format', '{{.Name}}'])
        docker['pilot_volumes'] = rows.splitlines() if code == 0 else []
    certbot_path = shutil.which('certbot')
    timer_code, timers, _ = run(['systemctl', 'list-timers', '--all', '--no-legend', '--no-pager'])
    os_release = {}
    try:
        for line in Path('/etc/os-release').read_text().splitlines():
            if line.startswith(('ID=', 'VERSION_ID=', 'PRETTY_NAME=')):
                key, value = line.split('=', 1)
                os_release[key] = value.strip('"')
    except OSError:
        pass
    capacity = {'minimum_available_memory_MiB_for_initial_build': 5120,
                'memory_available_sufficient': memory.get('MemAvailable_MiB', 0) >= 5120,
                'minimum_free_disk_GiB': 10,
                'all_reported_disks_sufficient': bool(disks) and all(d['free_GiB'] >= 10 for d in disks.values()),
                'operator_review_required': True}
    result = {
        'mode': 'read-only', 'uid': os.geteuid(), 'domain': DOMAIN, 'expected_ipv4': EXPECTED_IPV4,
        'architecture': os.uname().machine, 'os_release': os_release,
        'memory': memory, 'disks': disks, 'capacity': capacity,
        'dns_addresses_seen_from_vps': addresses, 'expected_ipv4_present': EXPECTED_IPV4 in addresses,
        'ipv6_review_required': any(':' in address for address in addresses),
        'listeners_80_443_5098': listeners, 'listener_inventory_available': port_code == 0,
        'docker': docker,
        'tools': {name: bool(shutil.which(name)) for name in ('python3', 'curl', 'nginx', 'certbot', 'openssl')},
        'nginx_version': version(['nginx', '-v']), 'nginx': nginx_inventory(),
        'certbot_version': version([certbot_path, '--version']) if certbot_path else None,
        'certbot_timer_lines': [line for line in timers.splitlines() if 'certbot' in line.lower()] if timer_code == 0 else [],
        'existing_pilot_paths': {name: Path(name).exists() for name in (
            '/opt/prodimt-piloto/app', '/opt/prodimt-piloto/config/.env',
            '/etc/nginx/sites-available/prodimt-pedidos.conf',
            '/etc/nginx/sites-enabled/prodimt-pedidos.conf',
            '/etc/letsencrypt/live/pedidos.prodimt.site/fullchain.pem')},
        'secrets_or_full_config_printed': False,
        'changes_made': False
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
