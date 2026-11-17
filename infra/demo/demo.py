#!/usr/bin/env python3
"""Small loopback-only operator demo; real APIs, generated accounts, no production failpoints."""
import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import uuid

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'infra/load'))
from lab import Browser, NoRedirect, assert_local_docker  # Reuse the bounded real HTTPS cookie/antiforgery client.

if not __debug__:
    raise RuntimeError('Demo safety assertions must remain enabled')


def save(path, data):
    # Persist the exact intention before network I/O; replacement also keeps credentials private.
    with tempfile.NamedTemporaryFile('w', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        os.chmod(temporary, 0o600)
        json.dump(data, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    descriptor = os.open(path.parent, os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def canonical_id(value):
    assert str(uuid.UUID(value)) == value, 'Use a canonical order UUID'
    return value


class Demo:
    def __init__(self, root):
        assert_local_docker()
        self.root = root.resolve()
        assert self.root.is_relative_to(REPO / '.local') and self.root != REPO / '.local'
        self.config = json.loads((self.root / 'demo-environment.json').read_text())
        self.run_id = str(uuid.UUID(self.config['run_id']))
        self.project = 'orderflow-demo-' + uuid.UUID(self.run_id).hex
        assert self.config['disposable'] is True and self.config['project'] == self.project
        self.env = dict(line.split('=', 1) for line in (self.root / 'deployment.env').read_text().splitlines())
        assert self.env['COMPOSE_PROJECT_NAME'] == self.project
        assert self.env['ORDERFLOW_PUBLIC_HOST'] == 'localhost' and self.env['ORDERFLOW_DEPLOY_PORT'] == '18448'
        assert self.env['ORDERFLOW_DEPLOY_SUBNET'] == '172.29.246.0/24' and self.env['ORDERFLOW_PROXY_IP'] == '172.29.246.10'
        assert self.env['ORDERFLOW_DEPLOY_SIMULATOR_PORT'] == '5093' and self.env['ORDERFLOW_DEPLOY_MAIL_PORT'] == '8028'
        self.results = self.root / 'results'
        self.results.mkdir(exist_ok=True)
        self.browser = Browser(self.root, 18448)
        self.data_path = self.root / 'demo.json'
        self.data = json.loads(self.data_path.read_text()) if self.data_path.exists() else {}
        if self.data:
            assert self.data['customer']['email'] == f'demo-{uuid.UUID(self.run_id).hex}@example.invalid'
            for value in self.data['products'].values():
                canonical_id(value)

    def compose(self, *args, name='compose', timeout=180):
        command = ['docker', 'compose', '-f', 'compose.deploy.yaml', '-f', 'compose.deploy.demo.yaml']
        if self.config['snap_compat']:
            command += ['-f', 'infra/deployment/compose.snap-compat.yaml']
        command += ['--env-file', str(self.root / 'deployment.env'), '--profile', 'deploy', '--profile', 'demo', *args]
        with (self.results / (name + '.log')).open('w') as log:
            subprocess.run(command, cwd=REPO, env={**os.environ, **self.env}, stdout=log, stderr=subprocess.STDOUT,
                timeout=timeout, check=True)

    def sql(self, sql):
        return subprocess.check_output(['docker', 'exec', self.project + '-postgres-1', 'psql', '-X', '-q', '-At',
            '-v', 'ON_ERROR_STOP=1', '-U', 'orderflow_bootstrap', '-d', 'orderflow', '-c', sql], text=True, timeout=15).strip()

    def guard(self):
        record = json.loads(subprocess.check_output(['docker', 'inspect', self.project + '-postgres-1'], text=True, timeout=15))[0]
        assert record['Config']['Labels']['com.docker.compose.project'] == self.project
        assert self.sql('SELECT "Id"::text FROM public."DemoEnvironment"') == self.run_id
        network = json.loads(subprocess.check_output(['docker', 'network', 'inspect', self.project + '_backend'], text=True, timeout=15))[0]
        assert network['Internal'] is True

    def admin(self):
        account = json.loads((self.root / 'bootstrap.json').read_text())['DevelopmentSeed']['Account']
        self.browser.login(account['Email'], account['Password'])
        self.verify_session()

    def customer(self):
        assert self.data, 'Seed this fresh lab first'
        self.browser.login(**self.data['customer'])
        self.verify_session()

    def verify_session(self):
        nodes = set()
        for _ in range(4):
            status, _, headers = self.browser.request('GET', '/api/auth/me')
            assert status == 200, 'Cross-replica session failed'
            nodes.add(headers.get('X-OrderFlow-Instance'))
        assert nodes == {'api1', 'api2'}

    def up(self):
        self.compose('config', '--quiet', name='config')
        self.compose('build', 'api1', 'worker1', 'proxy', 'simulator', name='build', timeout=600)
        self.compose('up', '-d', '--wait', '--wait-timeout', '120', 'postgres', 'rabbitmq', 'redis', 'mailpit', name='dependencies')
        self.compose('run', '--rm', 'migrate', name='migrate')
        assert self.sql('SELECT (SELECT count(*) FROM identity."Users")+(SELECT count(*) FROM catalog."Products")+(SELECT count(*) FROM ordering."Orders")') == '0', 'Fresh databases only; up never resets existing data'
        self.sql('CREATE TABLE public."DemoEnvironment" ("Id" uuid PRIMARY KEY); ' + f'INSERT INTO public."DemoEnvironment" VALUES (\'{self.run_id}\')')
        self.compose('run', '--rm', 'bootstrap-admin', name='bootstrap')
        self.compose('run', '--rm', 'initialize-keys', name='keys')
        self.compose('run', '--rm', 'simulator', 'migrate', name='simulator-migrate')
        self.compose('up', '-d', '--wait', '--wait-timeout', '150', 'simulator', 'api1', 'api2', 'worker1', 'worker2', 'proxy', name='hosts')
        self.guard()
        self.seed()

    def seed(self):
        assert not (self.root / 'seed.started').exists(), 'Do not replay uncertain seed writes; inspect or create a new demo'
        save(self.root / 'seed.started', {'run_id': self.run_id})
        self.admin()
        token = self.browser.token()
        status, category, _ = self.browser.request('POST', '/api/admin/categories', {'name': 'Demo equipment'}, token)
        assert status == 201
        products = {}
        for kind in ['Browse', 'Replay', 'Recovery']:
            status, product, _ = self.browser.request('POST', '/api/admin/products', {'sku': 'DEMO-' + kind.upper() + '-' + uuid.uuid4().hex.upper(),
                'name': 'Demo ' + kind, 'description': 'Synthetic portfolio demo fixture.', 'price': '12.50', 'categoryId': category['id']}, token)
            assert status == 201
            status, _, _ = self.browser.request('POST', f'/api/admin/products/{product["id"]}/stock-receipts',
                {'operationId': str(uuid.uuid4()), 'quantity': 8, 'reason': 'Synthetic demo opening receipt'}, token)
            assert status == 201
            products[kind.lower()] = product['id']
        customer = {'email': f'demo-{uuid.UUID(self.run_id).hex}@example.invalid', 'password': 'Aa1!' + secrets.token_hex(24)}
        anonymous = Browser(self.root, 18448)
        status, _, _ = anonymous.request('POST', '/api/auth/register', customer, anonymous.token())
        assert status == 201
        self.data = {'customer': customer, 'products': products, 'intentions': {}}
        save(self.data_path, self.data)
        self.customer()
        print('Ready: https://localhost:18448 — accounts are private in bootstrap.json and demo.json; no passwords printed.')

    def intention(self, kind, product=None):
        intentions = self.data['intentions']
        if kind not in intentions:
            intentions[kind] = {'key': str(uuid.uuid4()), 'body': {} if product is None else {'items': [{'productId': product, 'quantity': 1}]}}
            save(self.data_path, self.data)
        return intentions[kind]

    def checkout(self, intention):
        return self.browser.request('POST', '/api/orders', intention['body'],
            {**self.browser.token(), 'Idempotency-Key': intention['key']})

    def duplicate(self):
        self.customer()
        intention = self.intention('duplicate', self.data['products']['replay'])
        # Independent cookie clients; the same user/key/body reaches the round-robin replicas.
        peers = [Browser(self.root, 18448), Browser(self.root, 18448)]
        for peer in peers:
            peer.login(**self.data['customer'])
        tokens = [peer.token() for peer in peers]
        def request(index):
            return peers[index].request('POST', '/api/orders', intention['body'], {**tokens[index], 'Idempotency-Key': intention['key']})
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            replies = list(executor.map(request, range(2)))
        assert all(row[0] == 201 for row in replies) and replies[0][1] == replies[1][1]
        order_id = canonical_id(replies[0][1]['id'])
        assert self.sql(f'''SELECT count(*) FROM ordering."Orders" WHERE "Id"='{order_id}' ''') == '1'
        status, product, _ = self.browser.request('GET', '/api/products/' + self.data['products']['replay'])
        assert status == 200 and product['available'] == 7
        print('PASS duplicate requests: same stored order, one reserved unit; replaying this command retains its original key/body.')

    def pay(self, order_id):
        order_id = canonical_id(order_id)
        self.customer()
        status, order, _ = self.browser.request('GET', '/api/orders/' + order_id)
        assert status == 200 and order['status'] == 'PendingPayment', 'Use an eligible order owned by the demo Customer'
        intention = self.intention('payment-' + order_id)
        status, attempt, _ = self.browser.request('POST', f'/api/orders/{order_id}/payment-attempts', {},
            {**self.browser.token(), 'Idempotency-Key': intention['key']})
        assert status == 202
        provider_key = 'payment-' + uuid.UUID(attempt['id']).hex
        simulator = json.loads((self.root / 'simulator.json').read_text())['Simulator']
        # Operator-only loopback adapter; no key is passed into React or a browser page.
        import urllib.request
        provider_client = urllib.request.build_opener(NoRedirect())
        def provider(method, route, body=None, control=False):
            request = urllib.request.Request('http://127.0.0.1:5093' + route, method=method,
                data=None if body is None else json.dumps(body).encode(), headers={'Content-Type': 'application/json',
                    'X-Simulator-Control-Key' if control else 'X-Simulator-Key': simulator['ControlKey' if control else 'ApiKey']})
            with provider_client.open(request, timeout=5) as response:
                data = response.read(256 * 1024)
                return json.loads(data) if data else None
        def submitted():
            try:
                return provider('GET', '/provider/payments/' + provider_key)
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
                return None
        current = until(submitted, bool, 45)
        if current['state'] != 'Succeeded':
            provider('POST', '/control/payments/' + provider_key + '/resolve', {'state': 'Succeeded'}, True)
        signed = provider('POST', '/control/payments/' + provider_key + '/callback', control=True)
        # Browser.request serializes objects; send the exact provider-signed UTF-8 body here.
        body = signed['body'].encode()
        request = urllib.request.Request(f'https://localhost:18448/api/payments/simulator/callback', data=body,
            headers={'Content-Type': 'application/json', 'X-Payment-Key-Id': signed['keyId'],
                'X-Payment-Timestamp': str(signed['timestamp']), 'X-Payment-Signature': signed['signature']}, method='POST')
        with self.browser.opener.open(request, timeout=5) as response:
            assert response.status == 202
        result = until(lambda: self.browser.request('GET', '/api/orders/' + order_id), lambda row: row[0] == 200 and row[1]['status'] == 'Confirmed', 45)
        print('PASS verified capture/reconciliation: ' + result[1]['status'] + ', USD ' + result[1]['total'])

    def outage(self):
        self.customer()
        intention = self.intention('outage', self.data['products']['recovery'])
        assert not intention.get('completed'), 'This recovery intention is resolved; create a new demo for another failure run'
        self.compose('stop', 'rabbitmq', name='broker-stop')
        try:
            for _ in range(2):
                assert self.browser.request('GET', '/api/health/ready')[0] == 200
            status, order, _ = self.checkout(intention)
            assert status == 201
            order_id = canonical_id(order['id'])
            pending = int(self.sql(f'''SELECT count(*) FROM messaging."OutboxMessages" WHERE "AggregateId"='{order_id}' AND "Status" IN ('Pending','InFlight')'''))
            assert pending >= 1, 'The original intention already progressed; do not invent a new identity'
            print('Broker stopped: checkout committed; durable outbox pending >= 1; API ready.')
        finally:
            start = time.monotonic()
            self.compose('up', '-d', '--wait', '--wait-timeout', '60', 'rabbitmq', name='broker-start')
        until(lambda: self.sql(f'''SELECT count(*) FROM messaging."OutboxMessages" WHERE "AggregateId"='{order_id}' AND "Status"='Published' '''), lambda value: value == '1', start + 60 - time.monotonic())
        until(lambda: self.sql(f'''SELECT count(*) FROM notifications."Notifications" WHERE "OrderId"='{order_id}' '''), lambda value: value == '1', start + 60 - time.monotonic())
        intention['completed'] = True
        save(self.data_path, self.data)
        print(f'PASS original event published and one durable notification; recovery {time.monotonic()-start:.2f}s including broker startup.')
        print('Optional realtime dispatch can suspend during outage; restart APIs to resume hints, then refetch durable state.')


def until(operation, condition, seconds):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = operation()
        if condition(result):
            return result
        time.sleep(0.1)  # Poll a concrete provider/SQL state, never infer a race from a sleep.
    raise RuntimeError('Demo state did not converge within the bounded observation window')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['create', 'up', 'check', 'duplicate', 'pay', 'outage', 'down'])
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--order')
    parser.add_argument('--snap-compat', action='store_true')
    args = parser.parse_args()
    assert_local_docker()
    root = args.directory.resolve()
    assert root.is_relative_to(REPO / '.local') and root != REPO / '.local'
    if args.action == 'create':
        run_id = uuid.uuid4()
        project = 'orderflow-demo-' + run_id.hex
        subprocess.run(['python3', 'infra/deployment/setup.py', '--directory', str(root), '--project', project,
            '--port', '18448', '--subnet', '172.29.246.0/24', '--proxy-ip', '172.29.246.10'], cwd=REPO, check=True, timeout=30)
        environment = root / 'deployment.env'
        environment.chmod(0o600)
        with environment.open('a') as stream:
            stream.write('ORDERFLOW_DEPLOY_SIMULATOR_PORT=5093\nORDERFLOW_DEPLOY_MAIL_PORT=8028\n')
        environment.chmod(0o444)
        save(root / 'demo-environment.json', {'run_id': str(run_id), 'project': project, 'disposable': True, 'snap_compat': args.snap_compat})
        print('Created fresh private demo configuration. Run up next.')
        return
    demo = Demo(root)
    if args.action not in ['up', 'down']:
        demo.guard()
    if args.action == 'pay':
        assert args.order, 'Specify the order UUID displayed by the real customer UI'
        demo.pay(args.order)
    elif args.action == 'down':
        demo.compose('down', '--timeout', '30', name='down')  # No volume removal.
    elif args.action == 'check':
        demo.customer()
        print('PASS owned disposable database and authenticated session across both API replicas.')
    else:
        getattr(demo, args.action)()


if __name__ == '__main__':
    try:
        main()
    except (AssertionError, OSError, ValueError, RuntimeError, subprocess.SubprocessError):
        print('Demo stopped; no fallback target or new business identity. Inspect private results and resolve the original intention.')
        raise SystemExit(1)
