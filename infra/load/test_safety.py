import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
import uuid
from unittest.mock import patch, Mock

import lab
import report


class SafetyTests(unittest.TestCase):
    def test_remote_or_overridden_docker_is_rejected_before_provisioning(self):
        for endpoint, override in [('ssh://example.invalid', ''), ('unix:///run/docker.sock', 'tcp://example.invalid:2375')]:
            with self.subTest(endpoint=endpoint), patch.dict(os.environ, {'DOCKER_HOST': override}), \
                    patch('lab.subprocess.check_output', return_value=json.dumps([{'Endpoints': {'docker': {'Host': endpoint}}}])):
                with self.assertRaises(AssertionError):
                    lab.assert_local_docker()

    def test_fixed_local_browser_rejects_paths_that_escape_allowlist_before_io(self):
        browser = object.__new__(lab.Browser)
        browser.opener = Mock()
        browser.base = 'https://localhost:18446'
        for path in ['https://example.invalid/api/orders', '//example.invalid/api/orders', '/api/../orders', '/api/orders?url=https://example.invalid']:
            with self.subTest(path=path), self.assertRaises(AssertionError):
                browser.request('GET', path)
        browser.opener.open.assert_not_called()

    def test_changed_predeclared_criteria_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory, patch('lab.assert_local_docker'):
            root = Path(directory)
            manifest = {'disposable': True, 'run_id': 'a' * 8 + '-aaaa-aaaa-aaaa-' + 'a' * 12,
                'project': 'orderflow-load-' + 'a' * 32, 'criteria': copy.deepcopy(lab.SPEC)}
            manifest['criteria']['limits']['read_vus'] = 100000
            (root / 'load-environment.json').write_text(json.dumps(manifest))
            with self.assertRaisesRegex(AssertionError, 'Criteria'):
                lab.Lab(root)

    @staticmethod
    def measured():
        counts = {'scenario_requests': 8, 'scenario_success': 4, 'business_rejections': 4,
            'api1_responses': 4, 'api2_responses': 4, **dict.fromkeys(report.ERRORS, 0)}
        return {'metrics': {**{k: {'values': {'count': v, 'rate': 2}} for k, v in counts.items()},
            'scenario_latency_ms': {'values': {'med': 10, 'p(95)': 20, 'p(99)': 30}}},
            'state': {'testRunDurationMs': 4000}, 'runner_wall_seconds': 5,
            'sql_delta': dict.fromkeys(['calls', 'rows', 'execution_ms', 'catalog_calls', 'catalog_execution_ms'], 0),
            'ef_sql_log_events': 0, 'backlog': {'pending': 4, 'failed': 0, 'oldest_age_seconds': 1},
            'invariants': dict.fromkeys(report.INVARIANTS, 0)}

    def test_expected_stock_rejections_are_separate_and_private_fields_never_exported(self):
        data = self.measured()
        data['Cookie'] = 'SYNTHETIC-SECRET'
        data['metrics']['scenario_requests']['arbitrary_message'] = 'SYNTHETIC-SECRET'
        result = report.measurement(data)
        self.assertEqual(4, result['expected_business_rejections'])
        self.assertEqual(0, sum(result['errors'].values()))
        self.assertNotIn('SYNTHETIC-SECRET', json.dumps(result))

    def test_errors_and_broken_invariants_cannot_publish_as_success(self):
        for broken in ['infrastructure_errors', 'auth_errors', 'stock_violations']:
            data = self.measured()
            if broken == 'stock_violations':
                data['invariants'][broken] = 1
            else:
                data['metrics'][broken]['values']['count'] = 1
            with self.subTest(broken=broken), self.assertRaises(AssertionError):
                report.measurement(data)

    def test_nonfinite_measurements_cannot_enter_report(self):
        for value in [float('nan'), float('inf'), -1, 'SYNTHETIC-SECRET', True]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                report.number(value)

    def test_foreign_accounts_and_sql_like_fixture_ids_are_rejected(self):
        run_id = str(uuid.uuid4())
        ids = [str(uuid.uuid4()) for _ in range(16)]
        fixture = {'disposable': True, 'run_id': run_id, 'proxy_ip': '172.29.244.10', 'products': ids[:12],
            **dict(zip(['category', 'last_stock', 'duplicate', 'broker'], ids[12:])),
            'users': [{'email': f'load-{uuid.UUID(run_id).hex}-{i}@example.invalid', 'password': 'Aa1!' + 'a' * 32} for i in range(8)]}
        self.assertEqual(fixture, lab.validate_fixture(fixture, run_id))
        bad = copy.deepcopy(fixture)
        bad['users'][0]['email'] = 'personal@example.com'
        with self.assertRaises(AssertionError):
            lab.validate_fixture(bad, run_id)
        bad = copy.deepcopy(fixture)
        bad['last_stock'] = "'; SELECT private_data; --"
        with self.assertRaises(ValueError):
            lab.validate_fixture(bad, run_id)


if __name__ == '__main__':
    unittest.main()
