#!/usr/bin/env python3
"""Plan/apply one isolated PRODIMT Nginx vhost; never installs host packages."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
from pathlib import Path

DOMAIN = 'pedidos.prodimt.site'
EXPECTED_IPV4 = '194.238.26.70'
UPSTREAM_PORT = 5098
AVAILABLE = Path('/etc/nginx/sites-available/prodimt-pedidos.conf')
ENABLED = Path('/etc/nginx/sites-enabled/prodimt-pedidos.conf')
WEBROOT = Path('/var/www/prodimt-acme')
CERTIFICATE = Path('/etc/letsencrypt/live') / DOMAIN / 'fullchain.pem'
PRIVATE_KEY = CERTIFICATE.parent / 'privkey.pem'
HOOK = Path('/etc/letsencrypt/renewal-hooks/deploy/prodimt-nginx-reload.sh')
BACKUP_DIR = Path('/var/backups/prodimt-nginx')
MARKER = '# PRODIMT managed vhost v1; edit through scripts/pilot/publish-nginx.py\n'
HOOK_TEXT = '#!/bin/sh\n# PRODIMT certificate reload hook v1\nset -eu\nif [ "${RENEWED_LINEAGE:-}" = "/etc/letsencrypt/live/pedidos.prodimt.site" ]; then\n  /usr/sbin/nginx -t >/dev/null 2>&1\n  /bin/systemctl reload nginx\nfi\n'


def run(arguments, timeout=20):
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, timeout=timeout, check=False)
        return result.returncode, result.stdout, result.stderr
    except (OSError, subprocess.TimeoutExpired):
        return 127, '', ''


def require_command(arguments, description, timeout=20):
    code, output, _ = run(arguments, timeout)
    if code:
        raise RuntimeError(description + ' failed. No raw command output was printed.')
    return output


def config(https=False):
    challenge = '''    location ^~ /.well-known/acme-challenge/ {
        root /var/www/prodimt-acme;
        default_type text/plain;
        try_files $uri =404;
    }
'''
    text = MARKER + 'server {\n    listen 80;\n    server_name ' + DOMAIN + ';\n' + challenge
    text += ('    location / { return 308 https://' + DOMAIN + '$request_uri; }\n}\n') if https else '    location / { return 503; }\n}\n'
    if https:
        text += '''
server {
    listen 443 ssl;
    server_name pedidos.prodimt.site;
    ssl_certificate /etc/letsencrypt/live/pedidos.prodimt.site/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/pedidos.prodimt.site/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    client_max_body_size 2m;
    location / {
        proxy_pass http://127.0.0.1:5098;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
        proxy_connect_timeout 5s;
    }
}
'''
    return text


def owned_configuration():
    if AVAILABLE.is_symlink():
        raise RuntimeError('The intended sites-available path is a symlink; inspect it before proceeding.')
    if AVAILABLE.exists():
        if AVAILABLE.stat().st_nlink != 1:
            raise RuntimeError('The intended vhost has multiple hard links; inspect it before proceeding.')
        current = AVAILABLE.read_text(encoding='utf-8')
        if current not in (config(False), config(True)):
            raise RuntimeError('Existing vhost differs from the managed templates. It will not be overwritten.')
    else:
        current = None
    if ENABLED.is_symlink():
        if ENABLED.resolve() != AVAILABLE.resolve():
            raise RuntimeError('The enabled site points somewhere else. It will not be changed.')
    elif ENABLED.exists():
        raise RuntimeError('An unmanaged file occupies the sites-enabled path. It will not be changed.')
    return current


def inspect_nginx():
    # Effective configuration stays in memory; never print it or exception text.
    output = require_command(['nginx', '-T'], 'Nginx configuration inspection')
    conflicts = set()
    sections = re.split(r'^# configuration file (.+):\s*$', output, flags=re.MULTILINE)
    for index in range(1, len(sections), 2):
        active_file = Path(sections[index]).resolve()
        content = re.sub(r'#[^\n]*', '', sections[index + 1])
        for names in re.findall(r'\bserver_name\s+([^;]+);', content):
            tokens = [token.strip('"\'') for token in names.split()]
            if any(name in (DOMAIN, '.prodimt.site', '*.prodimt.site') or name.startswith('~') or '$' in name for name in tokens):
                if active_file != AVAILABLE.resolve():
                    conflicts.add(str(active_file))
    if conflicts:
        raise RuntimeError('An existing vhost covers the target domain: ' + ', '.join(sorted(conflicts)))
    return output


def assert_dns():
    addresses = {row[4][0] for row in socket.getaddrinfo(DOMAIN, 443, type=socket.SOCK_STREAM)}
    if addresses != {EXPECTED_IPV4}:
        raise RuntimeError('DNS must resolve only to the selected VPS IPv4 before this IPv4-only publication. Review A/AAAA records.')


def atomic_write(path, content, mode):
    fd, name = tempfile.mkstemp(prefix='.prodimt-', dir=str(path.parent))
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            stream.write(content)
        os.chmod(name, mode)
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def activate(content, verify=None):
    previous = owned_configuration()
    had_link = ENABLED.is_symlink()
    backup = None
    if previous is not None and previous != content:
        if BACKUP_DIR.is_symlink():
            raise RuntimeError('The vhost backup directory is a symlink; inspect it before proceeding.')
        BACKUP_DIR.mkdir(parents=True, exist_ok=True, mode=0o700)
        backup = BACKUP_DIR / ('prodimt-pedidos-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
                               + '-' + os.urandom(4).hex() + '.conf')
        atomic_write(backup, previous, 0o600)
    atomic_write(AVAILABLE, content, 0o644)
    try:
        if not had_link:
            ENABLED.symlink_to(AVAILABLE)
        require_command(['nginx', '-t'], 'New Nginx configuration test')
        require_command(['systemctl', 'reload', 'nginx'], 'Nginx reload')
        if verify:
            verify()
    except Exception:
        if not had_link and ENABLED.is_symlink() and ENABLED.resolve() == AVAILABLE.resolve():
            ENABLED.unlink()
        if previous is None:
            AVAILABLE.unlink(missing_ok=True)
        else:
            atomic_write(AVAILABLE, previous, 0o644)
        # Restore only our file and request a reload of the valid previous configuration.
        if run(['nginx', '-t'])[0] != 0 or run(['systemctl', 'reload', 'nginx'])[0] != 0:
            raise RuntimeError('Our vhost file was restored, but Nginx could not reload the previous configuration. Inspect the server before continuing.')
        raise
    return str(backup) if backup else None


def check_certificate():
    if not CERTIFICATE.exists() or not PRIVATE_KEY.exists():
        return False
    output = require_command(['openssl', 'x509', '-in', str(CERTIFICATE), '-noout', '-ext', 'subjectAltName'],
                             'Certificate subject inspection')
    domains = set(re.findall(r'DNS:([^,\s]+)', output))
    if domains != {DOMAIN}:
        raise RuntimeError('The certificate name is shared with other domains; this tool will not alter it.')
    return run(['openssl', 'x509', '-in', str(CERTIFICATE), '-noout', '-checkend', '604800'])[0] == 0


def check_existing_renewal():
    renewal = Path('/etc/letsencrypt/renewal') / (DOMAIN + '.conf')
    if CERTIFICATE.exists():
        if not renewal.exists():
            raise RuntimeError('The existing certificate has no Certbot renewal configuration. Review it before publication.')
        text = renewal.read_text(encoding='utf-8')
        if not re.search(r'^authenticator\s*=\s*webroot\s*$', text, re.MULTILINE) or str(WEBROOT) not in text:
            raise RuntimeError('The existing certificate uses another renewal method/webroot. Review it before changing renewal settings.')


def ensure_certificate(email, accept_terms):
    if check_certificate():
        return
    if not email or not re.fullmatch(r'[^\s@]+@[^\s@]+\.[^\s@]+', email):
        raise RuntimeError('Provide --email with the account contact for certificate issuance.')
    if not accept_terms:
        raise RuntimeError('Certificate issuance requires --accept-terms to accept the ACME subscriber agreement explicitly.')
    require_command(['certbot', 'certonly', '--webroot', '--webroot-path', str(WEBROOT), '--cert-name', DOMAIN,
                     '-d', DOMAIN, '--non-interactive', '--agree-tos', '--email', email, '--keep-until-expiring'],
                    'Certificate issuance', timeout=180)
    if not check_certificate():
        raise RuntimeError('Certificate validation failed; HTTPS configuration was not activated.')


def check_challenge():
    challenge_dir = WEBROOT / '.well-known/acme-challenge'
    if any(path.is_symlink() for path in (WEBROOT, WEBROOT / '.well-known', challenge_dir)):
        raise RuntimeError('The challenge path contains a symlink; inspect it before proceeding.')
    challenge_dir.mkdir(parents=True, exist_ok=True)
    name = 'prodimt-ready-' + os.urandom(12).hex()
    path = challenge_dir / name
    token = os.urandom(20).hex()
    try:
        path.write_text(token, encoding='ascii')
        path.chmod(0o644)
        body = require_command(['curl', '--fail', '--silent', '--show-error', '--noproxy', '*', '--max-time', '15',
                                'http://' + DOMAIN + '/.well-known/acme-challenge/' + name], 'Public HTTP challenge test')
        if body != token:
            raise RuntimeError('Public HTTP challenge did not return the exact probe. Check DNS/firewall/vhost.')
    finally:
        path.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--apply-http', action='store_true', help='Create only an ACME HTTP vhost; all other paths return503.')
    actions.add_argument('--apply-https', action='store_true', help='Obtain/reuse certificate and activate HTTPS proxy.')
    parser.add_argument('--email', help='Certificate account contact, required when requesting a certificate.')
    parser.add_argument('--accept-terms', action='store_true', help='Explicitly accept the ACME subscriber agreement when requesting a certificate.')
    args = parser.parse_args()
    current = owned_configuration()
    plan = {'domain': DOMAIN, 'upstream': 'http://127.0.0.1:' + str(UPSTREAM_PORT),
            'vhost_path': str(AVAILABLE), 'enabled_path': str(ENABLED),
            'webroot': str(WEBROOT), 'certificate_name': DOMAIN,
            'current_managed_phase': 'https' if current == config(True) else 'http' if current else 'absent',
            'http_template_sha256': hashlib.sha256(config(False).encode()).hexdigest(),
            'https_template_sha256': hashlib.sha256(config(True).encode()).hexdigest(),
            'mode': 'apply-https' if args.apply_https else 'apply-http' if args.apply_http else 'plan',
            'installs_packages': False, 'changes_other_vhosts': False}
    if not args.apply_http and not args.apply_https:
        print(json.dumps(plan, indent=2))
        return
    if os.geteuid() != 0:
        raise RuntimeError('Run the explicit apply step as root on the selected VPS.')
    for command in ('nginx', 'systemctl', 'curl', 'openssl'):
        if not shutil.which(command):
            raise RuntimeError('Missing prerequisite: ' + command + '. Review the preflight before installing anything.')
    if not AVAILABLE.parent.is_dir() or not ENABLED.parent.is_dir():
        raise RuntimeError('This host does not use the expected sites-available/sites-enabled layout.')
    require_command(['systemctl', 'is-active', '--quiet', 'nginx'], 'Existing Nginx service check')
    inspect_nginx()
    assert_dns()
    if args.apply_http:
        if current == config(True):
            raise RuntimeError('HTTPS already configured. Refusing to downgrade it to HTTP preparation.')
        if WEBROOT.is_symlink():
            raise RuntimeError('The challenge webroot is a symlink; inspect it before proceeding.')
        WEBROOT.mkdir(parents=True, exist_ok=True, mode=0o755)
        activate(config(False), check_challenge)
        print('HTTP ACME path verified. Other HTTP paths return 503; credentials are not exposed. Next: --apply-https --email YOUR_EMAIL --accept-terms')
        return
    if current not in (config(False), config(True)) or not ENABLED.is_symlink():
        raise RuntimeError('Complete --apply-http first and verify the challenge from outside the VPS.')
    ports = require_command(['docker', 'ps', '--filter', 'label=com.docker.compose.project=prodimt-pilot',
                             '--filter', 'label=com.docker.compose.service=app', '--format', '{{.Ports}}'],
                            'Pilot application container check')
    if ports.strip() != '127.0.0.1:5098->8080/tcp':
        raise RuntimeError('The expected isolated pilot application does not publish loopback5098. Review project/port configuration.')
    require_command(['curl', '--fail', '--silent', '--noproxy', '*', '--max-time', '5',
                     'http://127.0.0.1:5098/health/db'], 'Local application database health check')
    if not shutil.which('certbot'):
        raise RuntimeError('Certbot is not installed. Review the preflight before changing host packages.')
    if HOOK.is_symlink() or (HOOK.exists() and (HOOK.stat().st_nlink != 1 or HOOK.read_text() != HOOK_TEXT)):
        raise RuntimeError('An unmanaged renewal hook already occupies the target path.')
    if not Path('/usr/sbin/nginx').is_file() or not Path('/bin/systemctl').is_file():
        raise RuntimeError('Nginx/systemctl paths differ from the Ubuntu renewal hook. Review them before publication.')
    check_challenge()
    check_existing_renewal()
    ensure_certificate(args.email, args.accept_terms)
    HOOK.parent.mkdir(parents=True, exist_ok=True)
    previous_hook = HOOK.read_text() if HOOK.exists() else None
    previous_hook_mode = HOOK.stat().st_mode & 0o777 if HOOK.exists() else None
    atomic_write(HOOK, HOOK_TEXT, 0o755)
    def verify_https():
        require_command(['curl', '--fail', '--silent', '--noproxy', '*', '--max-time', '15',
                         '--resolve', DOMAIN + ':443:127.0.0.1', 'https://' + DOMAIN + '/health/db'], 'Local HTTPS database health check')
    try:
        backup = activate(config(True), verify_https)
    except Exception:
        if previous_hook is None:
            HOOK.unlink(missing_ok=True)
        else:
            atomic_write(HOOK, previous_hook, previous_hook_mode)
        raise
    if backup:
        print('Previous managed vhost saved: ' + backup)
    print('HTTPS configured for https://' + DOMAIN + '/admin/login. Verify public access and the existing Certbot renewal scheduler.')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError) as error:
        print('PRODIMT publication stopped: ' + str(error), file=sys.stderr)
        sys.exit(1)
