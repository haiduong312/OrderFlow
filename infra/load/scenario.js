import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const fixture = JSON.parse(open('/config/fixture.json'));
const phase = __ENV.LOAD_PHASE;
const allowed = ['read', 'cold', 'warmup', 'last-stock', 'duplicate', 'broker'];
if (!allowed.includes(phase) || fixture.disposable !== true || !/^[a-f0-9-]{36}$/.test(fixture.run_id)
    || fixture.proxy_ip !== '172.29.244.10' || fixture.users.length !== 8 || fixture.products.length !== 12)
    throw new Error('Only an explicitly provisioned bounded disposable fixture is supported.');
const base = 'https://localhost:8443'; // hosts override below stays inside an internal-only Docker bridge.
const writing = ['last-stock', 'duplicate', 'broker'].includes(phase);
const requests = new Counter('scenario_requests');
const ok = new Counter('scenario_success');
const business = new Counter('business_rejections');
const unexpected = new Counter('unexpected_responses');
const infrastructure = new Counter('infrastructure_errors');
const auth = new Counter('auth_errors');
const validation = new Counter('validation_errors');
const server = new Counter('server_errors');
const transport = new Counter('transport_errors');
const a = new Counter('api1_responses');
const b = new Counter('api2_responses');
const latency = new Trend('scenario_latency_ms', true);
export const options = {
    hosts: { localhost: fixture.proxy_ip },
    setupTimeout: '30s', teardownTimeout: '5s',
    maxRedirects: 0,
    batch: 8, batchPerHost: 8,
    systemTags: ['status', 'method', 'scenario', 'expected_response'],
    summaryTrendStats: ['min', 'max', 'avg', 'med', 'p(95)', 'p(99)'],
    scenarios: { bounded: { executor: 'per-vu-iterations',
        vus: phase === 'duplicate' || phase === 'cold' ? 1 : writing ? 8 : 4,
        iterations: writing || phase === 'cold' ? 1 : phase === 'warmup' ? 10 : 250,
        maxDuration: '30s', gracefulStop: '5s' } },
    thresholds: { unexpected_responses: ['count==0'], infrastructure_errors: ['count==0'], checks: ['rate==1'],
        ...(writing ? {} : { scenario_latency_ms: ['p(95)<2000'] }) },
};
http.setResponseCallback(http.expectedStatuses(200, 201, 409));

function call(method, path, body, token) {
    return http.request(method, base + path, body === null ? null : JSON.stringify(body), {
        timeout: '5s', redirects: 0, headers: { 'Content-Type': 'application/json', ...(token ? { 'X-CSRF-TOKEN': token } : {}) },
    });
}
export function setup() {
    for (const counter of [requests, ok, business, unexpected, infrastructure, auth, validation, server, transport, a, b]) counter.add(0);
    if (!writing) return {};
    const sessions = [];
    const selected = phase === 'duplicate' ? [fixture.users[0]] : fixture.users;
    for (const user of selected) {
        if (!user.email.endsWith('@example.invalid')) throw new Error('Synthetic fixture identities required.');
        const jar = http.cookieJar(); jar.clear(base);
        let response = call('GET', '/api/auth/antiforgery', null);
        if (response.status !== 200) throw new Error('Antiforgery setup failed.');
        response = call('POST', '/api/auth/login', { email: user.email, password: user.password }, response.json('requestToken'));
        if (response.status !== 200) throw new Error('Fixture login failed: HTTP ' + response.status);
        response = call('GET', '/api/auth/antiforgery', null);
        if (response.status !== 200) throw new Error('Authenticated antiforgery setup failed.');
        sessions.push({ cookies: jar.cookiesForURL(base), csrf: response.json('requestToken') });
    }
    return { sessions }; // Ephemeral k6 VU setup data, never included in the public summary.
}
function observe(response) {
    requests.add(1); latency.add(response.timings.duration);
    const node = response.headers['X-Orderflow-Instance'] || response.headers['X-OrderFlow-Instance'];
    if (node === 'api1') a.add(1); else if (node === 'api2') b.add(1); else unexpected.add(1);
    if (response.status === (writing ? 201 : 200)) { ok.add(1); return; }
    if (phase === 'last-stock' && response.status === 409 && response.json('code') === 'insufficient_stock') { business.add(1); return; }
    unexpected.add(1);
    if (response.status === 0) { transport.add(1); infrastructure.add(1); }
    else if (response.status >= 500) { server.add(1); infrastructure.add(1); }
    else if ([401, 403, 429].includes(response.status)) auth.add(1);
    else validation.add(1);
}
export default function (data) {
    if (!writing) {
        const category = phase !== 'cold' && __ITER % 10 === 0;
        const path = category ? '/api/categories/' + fixture.category : '/api/products/' + fixture.products[(__ITER + __VU) % 12];
        const response = call('GET', path, null); observe(response);
        check(response, { 'catalog status is 200': r => r.status === 200 });
        sleep(0.01); return;
    }
    const session = data.sessions[phase === 'duplicate' ? 0 : __VU - 1];
    for (const [name, values] of Object.entries(session.cookies)) http.cookieJar().set(base, name, values[0], { path: '/', secure: true });
    const productId = phase === 'last-stock' ? fixture.last_stock : phase === 'duplicate' ? fixture.duplicate : fixture.broker;
    const body = JSON.stringify({ items: [{ productId, quantity: 1 }] });
    const params = { timeout: '5s', redirects: 0, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrf,
        'Idempotency-Key': fixture.run_id + '-' + phase + '-' + (phase === 'duplicate' ? 'same' : __VU) } };
    if (phase === 'duplicate') {
        const responses = http.batch(Array.from({ length: 8 }, () => ['POST', base + '/api/orders', body, params]));
        for (const response of responses) observe(response);
        check(responses, { 'all eight replays have identical stored response bytes': rows => rows.every(r => r.status === 201 && r.body === rows[0].body) });
    } else {
        const response = http.post(base + '/api/orders', body, params); observe(response);
        check(response, { 'checkout has expected success or stock rejection': r => r.status === 201 || phase === 'last-stock' && r.status === 409 && r.json('code') === 'insufficient_stock' });
    }
}
export function handleSummary(data) {
    const names = ['scenario_requests', 'scenario_success', 'business_rejections', 'unexpected_responses', 'infrastructure_errors',
        'auth_errors', 'validation_errors', 'server_errors', 'transport_errors', 'api1_responses', 'api2_responses', 'scenario_latency_ms'];
    const metrics = Object.fromEntries(names.map(name => [name, data.metrics[name] || null]));
    return { '/results/summary.json': JSON.stringify({ tool: 'k6 2.4.0', phase, metrics, state: data.state, sample_note: writing || phase === 'cold' ? 'Small burst: tail percentiles are descriptive, not a capacity claim.' : 'HTTP duration excludes connection setup; throughput uses k6 measured counter rate.' }, null, 2) };
}
