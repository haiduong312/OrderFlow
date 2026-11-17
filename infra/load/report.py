#!/usr/bin/env python3
"""Publish numeric/allowlisted measurements only; never copy logs, fixtures or raw test output."""
import argparse
import json
import math
from pathlib import Path
import re
from lab import REPO, SPEC, write

READS = ['read-cache-off', 'read-cache-on-cold', 'read-cache-on-warm', 'read-cache-on-quiet']
WRITES = ['last-stock', 'duplicate', 'broker-outage']
ERRORS = ['unexpected_responses', 'infrastructure_errors', 'auth_errors', 'validation_errors', 'server_errors', 'transport_errors']
INVARIANTS = ['stock_violations', 'money_violations', 'duplicate_checkout_orders', 'orders', 'products', 'users']


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise ValueError('Expected a finite nonnegative measurement')
    return value


def fields(data, names):
    return {name: number(data[name]) for name in names}


def backlog(data):
    return fields(data, ['pending', 'failed', 'oldest_age_seconds'])


def measurement(data):
    metrics = data['metrics']
    count = lambda name: number(metrics[name]['values']['count'])
    result = {
        'requests': count('scenario_requests'), 'successful': count('scenario_success'),
        'expected_business_rejections': count('business_rejections'),
        'errors': {name: count(name) for name in ERRORS},
        'latency_ms': fields(metrics['scenario_latency_ms']['values'], ['med', 'p(95)', 'p(99)']),
        'requests_per_second': number(metrics['scenario_requests']['values']['rate']),
        'k6_window_seconds': number(data['state']['testRunDurationMs']) / 1000,
        'runner_wall_seconds': number(data['runner_wall_seconds']),
        'api_responses': {node: count(node + '_responses') for node in ['api1', 'api2']},
        'sql_delta': fields(data['sql_delta'], ['calls', 'rows', 'execution_ms', 'catalog_calls', 'catalog_execution_ms']),
        'ef_sql_log_events': number(data['ef_sql_log_events']),
        'backlog': backlog(data['backlog']), 'invariants': fields(data['invariants'], INVARIANTS),
    }
    assert all(value == 0 for value in result['errors'].values()), 'Failed measurements must not be published as passing'
    assert all(result['invariants'][key] == 0 for key in INVARIANTS[:3])
    assert result['requests'] == result['successful'] + result['expected_business_rejections']
    return result


def hardware(data):
    def safe(value, pattern):
        assert isinstance(value, str) and re.fullmatch(pattern, value)
        return value
    containers = []
    for row in data['container_limits']:
        assert row['service'] in ['api1', 'api2', 'worker1', 'worker2', 'postgres', 'rabbitmq', 'redis', 'proxy']
        containers.append({'service': row['service'], **fields(row, ['memory_bytes', 'nano_cpus']),
            'image_id': safe(row['image_id'], r'sha256:[a-f0-9]{64}')})
    return {'cpu_model': safe(data['cpu_model'], r'[A-Za-z0-9() .@_-]{1,120}'),
        'logical_cpus': number(data['logical_cpus']), 'memory_total': safe(data['memory_total'], r'\d+ kB'),
        'architecture': safe(data['architecture'], r'[A-Za-z0-9_-]{1,30}'),
        'kernel': safe(data['kernel'], r'[A-Za-z0-9._-]{1,100}'), 'container_limits': containers,
        'source_revision': safe(data['source_revision'], r'[a-f0-9]{40}'), 'working_tree_dirty': data['working_tree_dirty'] is True,
        'k6_limit': {'cpus': 0.5, 'memory_bytes': 134217728}}


def build(root):
    manifest = json.loads((root / 'load-environment.json').read_text())
    assert manifest['disposable'] is True and manifest['criteria'] == SPEC
    results = root / 'results'
    read = lambda name: json.loads((results / name).read_text())
    scenarios = {name: measurement(read(name + '/result.json')) for name in READS + WRITES}
    assert all(row['invariants']['products'] == 15 and row['invariants']['users'] == 9
        and row['invariants']['orders'] <= 13 for row in scenarios.values())
    for name in READS:
        expected = 1 if name.endswith('cold') else 1000
        assert scenarios[name]['requests'] == expected
        assert scenarios[name]['latency_ms']['p(95)'] < SPEC['criteria']['read_p95_ceiling_ms']
    for name in ['read-cache-on-warm', 'read-cache-on-quiet']:
        assert scenarios[name]['sql_delta']['catalog_calls'] == 0, 'Do not publish an expiring/mixed cache sample as all-hit'
    assert scenarios['read-cache-on-cold']['sql_delta']['catalog_calls'] > 0
    for name in WRITES:
        assert scenarios[name]['requests'] == 8 and all(n > 0 for n in scenarios[name]['api_responses'].values())
    assert scenarios['last-stock']['successful'] == scenarios['last-stock']['expected_business_rejections'] == 4
    assert scenarios['duplicate']['successful'] == scenarios['broker-outage']['successful'] == 8
    assert [scenarios[name]['invariants']['orders'] for name in WRITES] == [4, 5, 13]
    recovery = read('broker-recovery.json')
    seconds = number(recovery['recovery_wall_seconds'])
    assert seconds <= SPEC['limits']['recovery_budget_seconds']
    assert recovery['backlog_after_recovery']['pending'] == recovery['backlog_after_recovery']['failed'] == 0
    fault = read('fault-results.json')
    assert sum(row['passed_cases'] for row in fault['scenarios']) == SPEC['fault_cases_expected']
    expected = {'callback-cancel', 'callback-expiry', 'commit-before-ack', 'refund-timeout-Succeeded',
        'refund-timeout-TimeoutAfterCapture', 'two-worker-lease', 'two-worker-refund-lease'}
    timing = []
    for row in fault['measured_recovery_boundaries']:
        assert row['scenario'] in expected
        timing.append({'scenario': row['scenario'], 'recovery_wall_ms': number(row['recovery_wall_ms'])})
    assert {row['scenario'] for row in timing} == expected
    # No arbitrary facts, log messages, display names, cookies, source paths or fixture identities copied.
    return {'format_version': 1, 'criteria': SPEC, 'hardware': hardware(read('hardware.json')), 'scenarios': scenarios,
        'broker_recovery': {'wall_seconds': seconds, 'before': backlog(recovery['backlog_after_outage']),
            'after': backlog(recovery['backlog_after_recovery']), 'invariants': fields(recovery['invariants'], INVARIANTS)},
        'deterministic_faults': {'passed': 9, 'failed': 0, 'skipped': 0,
            'wall_seconds_including_setup_build': number(fault['wall_seconds_including_setup_build']),
            'recovery_boundaries': sorted(timing, key=lambda row: row['scenario']),
            'http_percentiles_throughput': None,
            'interpretation': 'Unmeasured HTTP load. Test-only gates with real PostgreSQL/broker/provider HTTP; simulated stops, not OS-kill evidence. Logical advances are separate from wall time.'},
        'limitations': ['Single machine; shared CPU/disk/bridge; no production capacity claim.',
            'Counter throughput uses the complete k6 window, including authenticated setup for write bursts.',
            'HTTP duration excludes connection setup; tiny write bursts and one cold read have no meaningful tail estimate.',
            'SQL totals include control/readiness traffic; catalog_calls excludes separate mutable availability queries.',
            'Warm runs prime 12 products/one category within the 30-second distributed-only TTL; no L1.',
            'Sequential samples, no repeated statistical experiment or isolated bottleneck profile.']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--environment', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = args.environment.resolve()
    assert root.is_relative_to(REPO / '.local')
    output = args.output.resolve()
    assert output.is_relative_to(REPO / 'docs/load-results') and not output.exists(), 'Use a fresh reviewed report path'
    write(output, build(root))
    print('Published allowlisted measurements; no private logs or fixtures copied.')


if __name__ == '__main__':
    main()
