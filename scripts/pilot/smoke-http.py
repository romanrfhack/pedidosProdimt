#!/usr/bin/env python3
"""Exercise only a disposable CI pilot; credentials/JWT never go to stdout."""
import json
import re
import sys
import urllib.error
import urllib.request
from config import read_env

values = read_env(sys.argv[1])
phase = sys.argv[2]
base = 'http://127.0.0.1:' + values['PRODIMT_HTTP_PORT']

def request(path, body=None, token=None, method=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                 headers=headers, method=method)
    try:
        response = urllib.request.urlopen(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, response.read()

def expect(condition, message):
    if not condition:
        raise RuntimeError(message)

def main():
    status, _, data = request('/api/auth/admin/login', {
        'userName': values['PRODIMT_ADMIN_USER'], 'password': values['PRODIMT_ADMIN_PASSWORD']})
    expect(status == 200, 'Generated administrator cannot sign in.')
    token = json.loads(data)['accessToken']
    for route in ('/', '/admin/login', '/admin/pedidos', '/cliente'):
        status, headers, html = request(route)
        expect(status == 200 and 'text/html' in headers.get('Content-Type', ''), 'SPA route not served: ' + route)
    html = request('/')[2].decode()
    scripts = re.findall(r'<script[^>]+src="([^"]+)"', html)
    expect(bool(scripts), 'Production Angular JavaScript was not found.')
    for script in scripts:
        status, headers, body = request('/' + script.lstrip('/'))
        expect(status == 200 and 'javascript' in headers.get('Content-Type', ''), 'Angular asset not served.')
        expect(all(value not in body for value in (b'prodimt-admin-demo', b'demo-customer-token', b'127.0.0.1:5088')), 'Production bundle contains development configuration.')
    for route in ('/api/nonexistent', '/health/nonexistent', '/unknown-resource'):
        status, headers, _ = request(route)
        expect(status == 404 and 'text/html' not in headers.get('Content-Type', ''), 'Reserved/unknown route leaked SPA HTML.')
    expect(request('/api/admin/customers')[0] == 401, 'Administrative API is not protected.')
    expect(request('/api/auth/admin/login', {'userName': 'admin', 'password': 'prodimt-admin-demo'})[0] == 401, 'Demo administrator exists in Production.')
    expect(request('/api/auth/customer-token', {'token': 'demo-customer-token'})[0] == 401, 'Demo customer token exists in Production.')
    for catalog in ('customers', 'machines'):
        status, _, data = request('/api/admin/' + catalog, token=token)
        expect(status == 200 and json.loads(data) == [], 'Pilot contains unexpected ' + catalog + '.')
    status, _, data = request('/api/admin/users', token=token)
    users = json.loads(data)
    expect(status == 200 and len(users) == 1 and users[0]['userName'] == values['PRODIMT_ADMIN_USER'], 'Bootstrap administrator is duplicated/missing.')
    status, _, data = request('/api/admin/products', token=token)
    products = json.loads(data)
    if phase == 'initial':
        expect(status == 200 and products == [], 'Pilot contains demo products.')
        status, _, data = request('/api/admin/products', {'name': 'CI fictional product', 'description': 'Disposable smoke fixture', 'unitCode': 'piece'}, token)
        expect(status == 201, 'Restricted application SQL user cannot create product/audit.')
    else:
        expect(status == 200 and len(products) == 1 and products[0]['name'] == 'CI fictional product', 'Data did not survive bootstrap/container restart.')
    print('Production HTTP smoke passed (' + phase + '): same origin, static assets, auth, no demo, restricted SQL read/write.')

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Only fixed assertions or exception type; response bodies may carry a token.
        print(str(error) if isinstance(error, RuntimeError) else type(error).__name__, file=sys.stderr)
        sys.exit(1)
