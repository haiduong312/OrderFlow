#!/usr/bin/env python3
"""Existing deterministic Development/Testing gates with fresh real Testcontainers, never production endpoints."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
import xml.etree.ElementTree as ET
from lab import Lab, REPO, write

CASES = {
    'callback-expiry-race': ('Payments.PaymentCallbackTests.Real_payment_race_with_cancellation_or_expiry_has_one_stock_effect', 2),
    'commit-before-ack': ('Notifications.NotificationTests.Commit_before_ACK_fault_then_channel_restart_redelivers_without_duplicate_effects', 1),
    'refund-timeout-after-success': ('Payments.PaymentRecoveryTests.Lost_refund_response_recovers_by_lookup_after_real_provider_restart_with_same_identity', 2),
    'two-worker-outbox-claim': ('Messaging.OutboxPublishingTests.Independent_workers_contend_before_claim_and_only_one_can_publish', 1),
    'two-worker-outbox-lease': ('Messaging.OutboxPublishingTests.Stale_owner_cannot_complete_after_reclaim_and_newer_publication', 1),
    'outbox-local-ack-crash': ('Messaging.OutboxPublishingTests.Crash_after_confirm_before_SQL_ack_reclaims_expired_lease_and_can_deliver_duplicate', 1),
    'two-worker-refund-lease': ('Payments.PaymentRecoveryTests.Expired_lease_reclaim_fences_old_worker_after_same_effect_was_already_refunded', 1),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--environment', type=Path, required=True)
    args = parser.parse_args()
    lab = Lab(args.environment)
    lab.guard()
    assert not os.environ.get('ORDERFLOW_TEST_POSTGRES'), 'External databases are forbidden'
    env = {k: v for k, v in os.environ.items() if not k.startswith('ORDERFLOW_TELEMETRY_VERIFY')}
    env['ORDERFLOW_FAILURE_LOAD_CONFIG'] = str(lab.root / 'load-environment.json')
    artifacts = lab.results / 'fault-build'
    raw = lab.results / 'fault-raw'
    raw.mkdir(exist_ok=False)
    start = time.monotonic()
    lab.command(['dotnet', 'restore', 'OrderFlow.slnx', '--locked-mode', '--artifacts-path', str(artifacts)], 'fault-restore', 240, env)
    selection = '|'.join('FullyQualifiedName~' + method for method, _ in CASES.values())
    lab.command(['dotnet', 'test', 'tests/OrderFlow.IntegrationTests', '-c', 'Release', '--no-restore', '--artifacts-path', str(artifacts),
        '-m:1', '--filter', selection, '--logger', 'trx;LogFileName=faults.trx', '--results-directory', str(raw)], 'fault-tests', 240, env)
    spec = importlib.util.spec_from_file_location('ci_report', REPO / 'infra/ci/report.py')
    report = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(report)
    assert report.publish(raw, lab.results / 'fault-public', 'integration', 'trx')
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    rows = ET.parse(raw / 'faults.trx').findall('t:Results/t:UnitTestResult', ns)
    assert len(rows) == sum(count for _, count in CASES.values()) == lab.manifest['criteria']['fault_cases_expected'] == 9
    assert set(lab.manifest['criteria']['fault_methods']) == {'OrderFlow.IntegrationTests.' + method for method, _ in CASES.values()}
    findings = []
    for scenario, (method, expected) in CASES.items():
        selected = [r for r in rows if method in r.attrib['testName']]
        assert len(selected) == expected and all(r.attrib['outcome'] == 'Passed' for r in selected)
        findings.append({'scenario': scenario, 'passed_cases': expected, 'http_percentiles': None,
            'http_measurement': 'unmeasured: deterministic production-service/SQL harness, not an HTTP load sample'})
    timing = [json.loads(p.read_text()) for p in (lab.results / 'fault-timing').glob('*.json')]
    assert len(timing) == 7, 'Recovery-boundary evidence is incomplete'
    write(lab.results / 'fault-results.json', {'wall_seconds_including_setup_build': time.monotonic() - start,
        'scenarios': findings, 'measured_recovery_boundaries': timing,
        'environment': 'fresh actual Testcontainers, loopback Development/Testing; no explicit per-container CPU/memory caps in this lane',
        'invariants': 'Existing durable order/stock/inbox/refund/participant assertions passed',
        'clock': 'Logical expiry advances900s and retry/lease5/60s; measured windows use monotonic wall time around the documented boundary',
        'fault_modes': 'In-process service instances and real transports; ACK exception/channel restart and SQL/TCS gates simulate worker stops, not OS kill evidence'})
    print('Failure suite passed with explicit simulated-stop boundaries and real durable dependencies.')


if __name__ == '__main__':
    main()
