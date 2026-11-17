#!/usr/bin/env python3
"""Bounded local lab. No arbitrary URL, connection string, SQL or production failpoint interface."""
import datetime as dt
import http.cookiejar
import json
import os
from pathlib import Path
import platform
import re
import ssl
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import secrets

REPO = Path(__file__).resolve().parents[2]
SPEC = json.loads(Path(__file__).with_name('scenarios.json').read_text())
if not __debug__:
    raise RuntimeError('Do not disable the disposable-environment assertions with Python optimization flags')


def write(path, value, private=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n')
    if private:
        path.chmod(0o444)


def assert_local_docker():
    endpoint = json.loads(subprocess.check_output(['docker', 'context', 'inspect'], text=True))[0]['Endpoints']['docker']['Host']
    assert endpoint.startswith('unix://') and not os.environ.get('DOCKER_HOST'), 'Only a local Docker socket/context is allowed'


def validate_fixture(fixture, run_id):
    assert fixture['disposable'] is True and fixture['run_id'] == run_id
    assert fixture['proxy_ip'] == '172.29.244.10' and len(fixture['products']) == 12 and len(fixture['users']) == 8
    ids = fixture['products'] + [fixture[key] for key in ['category', 'last_stock', 'duplicate', 'broker']]
    assert all(str(uuid.UUID(value)) == value for value in ids)
    assert len(set(ids)) == 16
    for index, user in enumerate(fixture['users']):
        assert user['email'] == f'load-{uuid.UUID(run_id).hex}-{index}@example.invalid'
        assert re.fullmatch(r'Aa1![0-9a-f]{32}', user['password'])
    return fixture


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        return None


class Browser:
    def __init__(self, root, port):
        self.base = f'https://localhost:{port}'
        self.opener = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
            urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(root / 'tls.crt'))))

    def request(self, method, path, body=None, headers=None):
        assert re.fullmatch(r'/api/[A-Za-z0-9/_-]+', path), 'Only fixed local API paths are allowed'
        request = urllib.request.Request(self.base + path, method=method,
            data=None if body is None else json.dumps(body).encode(), headers={'Content-Type': 'application/json', **(headers or {})})
        try:
            response = self.opener.open(request, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            data = response.read(256 * 1024)
            return response.status, json.loads(data) if data else None, response.headers

    def token(self):
        status, body, _ = self.request('GET', '/api/auth/antiforgery')
        assert status == 200, 'Antiforgery retrieval failed'
        return {'X-CSRF-TOKEN': body['requestToken']}

    def login(self, email, password):
        status, _, _ = self.request('POST', '/api/auth/login', {'email': email, 'password': password}, self.token())
        assert status == 200, 'Synthetic login failed'


class Lab:
    def __init__(self, directory):
        assert_local_docker()
        self.root = directory.resolve()
        self.manifest = json.loads((self.root / 'load-environment.json').read_text())
        self.run_id = str(uuid.UUID(self.manifest['run_id']))
        self.project = 'orderflow-load-' + uuid.UUID(self.run_id).hex
        assert self.manifest['disposable'] is True and self.manifest['project'] == self.project
        assert self.manifest['criteria'] == SPEC, 'Criteria must match the predeclared version; create a new lab for changed bounds'
        assert self.root.is_relative_to(REPO / '.local'), 'Only ignored .local lab inputs are allowed'
        self.settings = dict(line.split('=', 1) for line in (self.root / 'deployment.env').read_text().splitlines())
        assert self.settings['COMPOSE_PROJECT_NAME'] == self.project
        assert self.settings['ORDERFLOW_PUBLIC_HOST'] == 'localhost'
        assert self.settings['ORDERFLOW_PROXY_IP'] == '172.29.244.10'
        assert self.settings['ORDERFLOW_DEPLOY_SUBNET'] == '172.29.244.0/24'
        self.port = int(self.settings['ORDERFLOW_DEPLOY_PORT'])
        assert self.port == 18446
        self.results = self.root / 'results'
        self.results.mkdir(exist_ok=True)
        active = json.loads((self.results / 'active.json').read_text()) if (self.results / 'active.json').exists() else {}
        self.cache, self.quiet = active.get('cache', False), active.get('quiet', False)
        self.browser = Browser(self.root, self.port)

    def command(self, args, name, timeout=180, env=None):
        with (self.results / (name + '.log')).open('w') as log:
            subprocess.run(args, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=True)

    def compose(self, *args, name='compose', timeout=180):
        env = {**os.environ, **self.settings, 'LOAD_CACHE_ENABLED': str(self.cache).lower(),
               'LOAD_SQL_LOG_LEVEL': 'Warning' if self.quiet else 'Information'}
        command = ['docker', 'compose', '-f', 'compose.deploy.yaml', '-f', 'infra/load/compose.load.yaml']
        if self.manifest['snap_compat']:
            command += ['-f', 'infra/deployment/compose.snap-compat.yaml']
        command += ['--env-file', str(self.root / 'deployment.env'), '--profile', 'deploy', *args]
        self.command(command, name, timeout, env)

    def postgres(self, sql):
        # Only fixed runner-owned observations. Passwords are not command arguments.
        result = subprocess.run(['docker', 'exec', self.project + '-postgres-1', 'psql', '-X', '-q', '-At',
            '-v', 'ON_ERROR_STOP=1', '-U', 'orderflow_bootstrap', '-d', 'orderflow', '-c', sql],
            cwd=REPO, capture_output=True, text=True, timeout=15)
        if result.returncode:
            raise RuntimeError('Local PostgreSQL observation failed')
        return result.stdout.strip()

    def guard(self):
        data = json.loads(subprocess.check_output(['docker', 'inspect', self.project + '-postgres-1'], text=True))[0]
        assert data['Config']['Labels'].get('orderflow.disposable') == 'failure-load-v1'
        assert data['Config']['Labels']['com.docker.compose.project'] == self.project
        assert self.postgres('SELECT "Id"::text FROM public."FailureLoadEnvironment"') == self.run_id
        network = json.loads(subprocess.check_output(['docker', 'network', 'inspect', self.project + '_backend'], text=True))[0]
        assert network['Internal'] is True

    def wait_apis(self):
        nodes, deadline = set(), time.monotonic() + 30
        while time.monotonic() < deadline:
            try:
                status, _, headers = self.browser.request('GET', '/api/health/ready')
                if status == 200:
                    nodes.add(headers.get('X-OrderFlow-Instance'))
                if nodes == {'api1', 'api2'}:
                    return
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(0.1)  # Poll readiness; elapsed time is never proof of race correctness.
        raise RuntimeError('Both owned API replicas did not become ready')

    def up(self):
        self.compose('config', '--quiet', name='config')
        self.compose('build', 'api1', 'worker1', 'proxy', name='build', timeout=600)
        self.compose('up', '-d', '--wait', '--wait-timeout', '120', 'postgres', 'rabbitmq', 'redis', name='dependencies')
        self.compose('run', '--rm', 'migrate', name='migrate')
        assert self.postgres('SELECT (SELECT count(*) FROM identity."Users") + (SELECT count(*) FROM catalog."Products") + (SELECT count(*) FROM ordering."Orders")') == '0', 'A fresh empty environment is required'
        self.postgres('CREATE EXTENSION pg_stat_statements; CREATE TABLE public."FailureLoadEnvironment" ("Id" uuid PRIMARY KEY); '
                      f'INSERT INTO public."FailureLoadEnvironment" VALUES (\'{self.run_id}\');')
        self.compose('run', '--rm', 'bootstrap-admin', name='bootstrap')
        self.compose('run', '--rm', 'initialize-keys', name='initialize-keys')
        self.compose('up', '-d', '--wait', '--wait-timeout', '150', 'api1', 'api2', 'worker1', 'worker2', 'proxy', name='hosts')
        self.guard()
        self.wait_apis()
        self.seed()
        self.hardware()

    def seed(self):
        account = json.loads((self.root / 'bootstrap.json').read_text())['DevelopmentSeed']['Account']
        self.browser.login(account['Email'], account['Password'])
        self.verify_session()
        token = self.browser.token()
        status, category, _ = self.browser.request('POST', '/api/admin/categories', {'name': 'LOAD fixture'}, token)
        assert status == 201
        products = []
        for index in range(15):
            status, product, _ = self.browser.request('POST', '/api/admin/products', {'sku': 'LOAD-' + uuid.uuid4().hex.upper(),
                'name': f'LOAD product {index:02}', 'description': 'Synthetic bounded catalog fixture. ' * 30,
                'price': '12.50', 'categoryId': category['id']}, token)
            assert status == 201
            stock = 4 if index == 12 else 100
            status, _, _ = self.browser.request('POST', f'/api/admin/products/{product["id"]}/stock-receipts',
                {'operationId': str(uuid.uuid4()), 'quantity': stock, 'reason': 'Synthetic LOAD opening receipt'}, token)
            assert status == 201
            products.append(product['id'])
        users = []
        anonymous = Browser(self.root, self.port)
        for index in range(8):
            user = {'email': f'load-{uuid.UUID(self.run_id).hex}-{index}@example.invalid', 'password': 'Aa1!' + secrets.token_hex(16)}
            status, _, _ = anonymous.request('POST', '/api/auth/register', user, anonymous.token())
            assert status == 201
            users.append(user)
        write(self.root / 'fixture.json', {'disposable': True, 'run_id': self.run_id, 'proxy_ip': '172.29.244.10',
            'category': category['id'], 'products': products[:12], 'last_stock': products[12], 'duplicate': products[13], 'broker': products[14], 'users': users}, private=True)
        self.invariants('seed')

    def verify_session(self):
        nodes = set()
        for _ in range(4):
            status, _, headers = self.browser.request('GET', '/api/auth/me')
            assert status == 200, 'Shared cookie failed before any load was generated'
            nodes.add(headers.get('X-OrderFlow-Instance'))
        assert nodes == {'api1', 'api2'}, 'Authenticated fixture must reach both replicas'
        write(self.results / 'session-verification.json', {'requests': 4, 'status': 200, 'nodes': sorted(nodes)})

    def verify(self):
        self.wait_apis()
        account = json.loads((self.root / 'bootstrap.json').read_text())['DevelopmentSeed']['Account']
        self.browser.login(account['Email'], account['Password'])
        self.verify_session()
        self.invariants('verified')

    def hardware(self):
        model = next((line.split(':', 1)[1].strip() for line in Path('/proc/cpuinfo').read_text().splitlines() if line.startswith('model name')), 'unknown')
        memory = dict(line.split(':', 1) for line in Path('/proc/meminfo').read_text().splitlines())
        limits = []
        for service in ['api1', 'api2', 'worker1', 'worker2', 'postgres', 'rabbitmq', 'redis', 'proxy']:
            data = json.loads(subprocess.check_output(['docker', 'inspect', self.project + '-' + service + '-1'], text=True))[0]
            limits.append({'service': service, 'memory_bytes': data['HostConfig']['Memory'], 'nano_cpus': data['HostConfig']['NanoCpus'], 'image_id': data['Image']})
        write(self.results / 'hardware.json', {'cpu_model': model, 'logical_cpus': os.cpu_count(), 'memory_total': memory['MemTotal'].strip(),
            'architecture': platform.machine(), 'kernel': platform.release(), 'container_limits': limits,
            'k6_limit': {'cpus': 0.5, 'memory': '128m'}, 'source_revision': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip(),
            'working_tree_dirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=REPO, text=True))})

    def statistics(self):
        return json.loads(self.postgres('''SELECT json_build_object('calls',coalesce(sum(calls),0),'rows',coalesce(sum(rows),0),
            'execution_ms',coalesce(sum(total_exec_time),0),'catalog_calls',coalesce(sum(calls) FILTER (WHERE query LIKE '%catalog.%'),0),
            'catalog_execution_ms',coalesce(sum(total_exec_time) FILTER (WHERE query LIKE '%catalog.%'),0))
            FROM pg_stat_statements WHERE userid=(SELECT oid FROM pg_roles WHERE rolname='orderflow_api') AND dbid=(SELECT oid FROM pg_database WHERE datname=current_database())'''))

    def invariants(self, name):
        data = json.loads(self.postgres('''WITH r AS (SELECT "ProductId",sum("Quantity") AS n FROM inventory."InventoryReservations" WHERE "Status"='Active' GROUP BY "ProductId"),
            m AS (SELECT "ProductId",sum("OnHandDelta") AS h,sum("ReservedDelta") AS r FROM inventory."StockMovements" GROUP BY "ProductId")
            SELECT json_build_object('stock_violations',(SELECT count(*) FROM inventory."Balances" b LEFT JOIN r ON r."ProductId"=b."ProductId" LEFT JOIN m ON m."ProductId"=b."ProductId"
              WHERE b."Reserved"<0 OR b."Reserved">b."OnHand" OR b."Reserved"<>coalesce(r.n,0) OR b."Reserved"<>coalesce(m.r,0) OR b."OnHand"<>coalesce(m.h,0)),
              'money_violations',(SELECT count(*) FROM ordering."Orders" o WHERE o."Total"<>(SELECT sum(i."LineTotal") FROM ordering."OrderItems" i WHERE i."OrderId"=o."Id")),
              'duplicate_checkout_orders',(SELECT count(*) FROM (SELECT "CheckoutRequestId" FROM ordering."Orders" GROUP BY "CheckoutRequestId" HAVING count(*)>1) x),
              'orders',(SELECT count(*) FROM ordering."Orders"),'products',(SELECT count(*) FROM catalog."Products"),'users',(SELECT count(*) FROM identity."Users"))'''))
        assert data['stock_violations'] == data['money_violations'] == data['duplicate_checkout_orders'] == 0
        assert data['orders'] <= 13 and data['products'] == 15 and data['users'] == 9
        write(self.results / (name + '-invariants.json'), data)
        return data

    def backlog(self):
        return json.loads(self.postgres('''SELECT json_build_object('pending',count(*) FILTER (WHERE "Status" IN ('Pending','InFlight')),
            'failed',count(*) FILTER (WHERE "Status"='Failed'),'oldest_age_seconds',coalesce(max(extract(epoch FROM clock_timestamp()-"OccurredAt")) FILTER (WHERE "Status" IN ('Pending','InFlight')),0)) FROM messaging."OutboxMessages"'''))

    def measured(self, name, phase):
        folder = self.results / name
        folder.mkdir(exist_ok=False)  # Never silently replay an uncertain previous intention.
        folder.chmod(0o777)  # Host parent remains0700; only this bind is writable by non-root k6.
        before, since, start = self.statistics(), dt.datetime.now(dt.timezone.utc).isoformat(), time.monotonic()
        command = ['docker', 'run', '--rm', '--network', self.project + '_backend', '--cpus', '0.5', '--memory', '128m',
            '--read-only', '--tmpfs', '/tmp:rw,nosuid,noexec,size=16m', '--cap-drop', 'ALL',
            '-e', 'SSL_CERT_FILE=/config/tls.crt', '-e', 'K6_NO_USAGE_REPORT=true', '-e', 'LOAD_PHASE=' + phase,
            '-e', 'K6_NO_USAGE_REPORT=true',
            '--mount', f'type=bind,src={self.root / "fixture.json"},dst=/config/fixture.json,readonly',
            '--mount', f'type=bind,src={self.root / "tls.crt"},dst=/config/tls.crt,readonly',
            '--mount', f'type=bind,src={REPO / "infra/load/scenario.js"},dst=/script.js,readonly',
            '--mount', f'type=bind,src={folder},dst=/results']
        if not self.manifest['snap_compat']:
            command += ['--security-opt', 'no-new-privileges:true']
        command += [SPEC['k6_image'], 'run', '/script.js']
        self.command(command, name, timeout=75)
        after = self.statistics()
        data = json.loads((folder / 'summary.json').read_text())
        data.update({'runner_wall_seconds': time.monotonic() - start, 'cache_enabled': self.cache, 'sql_log_level': 'Warning' if self.quiet else 'Information',
            'sql_delta': {key: after[key] - before[key] for key in before}, 'backlog': self.backlog(), 'invariants': self.invariants(name)})
        count = 0
        for node in ['api1', 'api2']:
            logs = subprocess.check_output(['docker', 'logs', '--since', since, self.project + '-' + node + '-1'], stderr=subprocess.DEVNULL, text=True)
            count += logs.count('"EventId":20101')
        data['ef_sql_log_events'] = count
        write(folder / 'result.json', data)
        return data

    def restart_apis(self):
        self.compose('up', '-d', '--wait', '--wait-timeout', '120', '--force-recreate', 'api1', 'api2', name='restart-apis')
        self.wait_apis()
        write(self.results / 'active.json', {'cache': self.cache, 'quiet': self.quiet})

    def clear_fixture_cache(self):
        # Delete only these 13 generated public catalog keys, never FLUSHDB/backplane keys.
        self.guard()
        fixture = validate_fixture(self.fixture, self.run_id)
        keys = ['orderflow:catalog:catalog:v1:product:' + uuid.UUID(value).hex for value in fixture['products']]
        keys += ['orderflow:catalog:catalog:v1:category:' + uuid.UUID(fixture['category']).hex]
        def resp(values):
            values = [value.encode() for value in values]
            return ('*' + str(len(values)) + '\r\n').encode() + b''.join(
                ('$' + str(len(value)) + '\r\n').encode() + value + b'\r\n' for value in values)
        # AUTH goes over stdin only, never argv/log/environment output.
        payload = resp(['AUTH', (self.root / 'redis_password').read_text().strip()]) + resp(['DEL', *keys])
        result = subprocess.run(['docker', 'exec', '-i', self.project + '-redis-1', 'redis-cli', '--pipe', '--pipe-timeout', '5'],
            input=payload, capture_output=True, timeout=10)
        assert result.returncode == 0 and re.search(rb'errors:\s*0,', result.stdout), 'Owned fixture cache reset failed'

    def reads(self):
        self.cache, self.quiet = False, False
        self.restart_apis()
        self.measured('off-prime', 'warmup')
        self.measured('read-cache-off', 'read')
        self.cache = True
        self.restart_apis()
        self.clear_fixture_cache()
        self.measured('read-cache-on-cold', 'cold')
        self.measured('on-prime', 'warmup')
        warm = self.measured('read-cache-on-warm', 'read')
        assert warm['sql_delta']['catalog_calls'] == 0, 'TTL expired or priming incomplete: this is not an all-hit catalog DTO sample'

    def optimize(self):
        assert (self.results / 'read-cache-on-warm/result.json').exists(), 'Measure baseline before optimizing'
        self.cache, self.quiet = True, True
        self.restart_apis()
        self.clear_fixture_cache()
        self.measured('quiet-prime', 'warmup')
        warm = self.measured('read-cache-on-quiet', 'read')
        assert warm['sql_delta']['catalog_calls'] == 0, 'Warm comparison must not include old entries expiring mid-run'

    def correctness(self):
        last = self.measured('last-stock', 'last-stock')
        duplicate = self.measured('duplicate', 'duplicate')
        assert last['metrics']['scenario_success']['values']['count'] == 4
        assert last['metrics']['business_rejections']['values']['count'] == 4
        assert duplicate['metrics']['scenario_success']['values']['count'] == 8
        for result in [last, duplicate]:
            assert all(result['metrics'][node + '_responses']['values']['count'] > 0 for node in ['api1', 'api2'])
        assert self.postgres(f'''SELECT count(*) FROM messaging."IdempotencyRecords" WHERE "Key"='{self.run_id}-duplicate-same' ''') == '1'
        assert self.postgres(f'''SELECT "Reserved" FROM inventory."Balances" WHERE "ProductId"='{self.fixture['last_stock']}' ''') == '4'
        self.compose('stop', 'rabbitmq', name='broker-stop')
        queued = None
        try:
            self.wait_apis()
            result = self.measured('broker-outage', 'broker')
            assert result['metrics']['scenario_success']['values']['count'] == 8
            queued = self.backlog()
            assert queued['pending'] >= 8
        finally:
            start = time.monotonic()
            self.compose('up', '-d', '--wait', '--wait-timeout', '60', 'rabbitmq', name='broker-start')
        deadline = start + 60
        while time.monotonic() < deadline:
            sample = self.backlog()
            assert sample['failed'] == 0
            if sample['pending'] == 0:
                break
            time.sleep(0.1)
        else:
            raise AssertionError('Outbox did not drain within the60s recovery budget')
        write(self.results / 'broker-recovery.json', {'recovery_wall_seconds': time.monotonic() - start,
            'backlog_after_outage': queued, 'backlog_after_recovery': sample, 'invariants': self.invariants('recovery')})


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['create', 'up', 'verify', 'reads', 'optimize', 'correctness', 'faults', 'down'])
    parser.add_argument('--environment', required=True, type=Path)
    parser.add_argument('--snap-compat', action='store_true')
    args = parser.parse_args()
    assert_local_docker()
    if args.action == 'create':
        directory = args.environment.resolve()
        assert directory.is_relative_to(REPO / '.local')
        run_id = uuid.uuid4()
        subprocess.run(['python3', 'infra/deployment/setup.py', '--directory', str(directory), '--project', 'orderflow-load-' + run_id.hex,
            '--port', '18446', '--subnet', '172.29.244.0/24', '--proxy-ip', '172.29.244.10'], cwd=REPO, check=True)
        write(directory / 'load-environment.json', {'disposable': True, 'run_id': str(run_id), 'project': 'orderflow-load-' + run_id.hex,
            'snap_compat': args.snap_compat, 'criteria': SPEC, 'created_utc': dt.datetime.now(dt.timezone.utc).isoformat()}, private=True)
        print('Created explicit disposable environment; criteria and bounds recorded before execution.')
        return
    lab = Lab(args.environment)
    if args.action not in ['up', 'down']:
        lab.guard()
        lab.fixture = validate_fixture(json.loads((lab.root / 'fixture.json').read_text()), lab.run_id)
    if args.action == 'down':
        lab.compose('down', '--timeout', '30', name='down')  # Never -v; preserve volumes/evidence.
    elif args.action == 'faults':
        subprocess.run(['python3', 'infra/load/faults.py', '--environment', str(lab.root)], cwd=REPO, check=True)
    else:
        getattr(lab, args.action)()


if __name__ == '__main__':
    try:
        main()
    except (AssertionError, OSError, ValueError, RuntimeError, subprocess.SubprocessError):
        print('Lab command failed. No target fallback or automatic business replay; inspect private evidence and repair the owned environment.')
        raise SystemExit(1)
