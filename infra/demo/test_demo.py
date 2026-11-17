import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from demo import Demo, canonical_id, save


class DemoSafetyTests(unittest.TestCase):
    def test_order_input_cannot_inject_routes_or_sql(self):
        for value in ['../admin', "'; DELETE FROM x; --", 'https://example.com']:
            with self.subTest(value=value), self.assertRaises((ValueError, AssertionError)):
                canonical_id(value)

    def test_uncertain_intention_keeps_exact_key_and_payload_after_reload(self):
        with tempfile.TemporaryDirectory() as directory:
            first = object.__new__(Demo)
            first.data_path = Path(directory) / 'demo.json'
            first.data = {'intentions': {}}
            original = first.intention('duplicate', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
            second = object.__new__(Demo)
            second.data_path = first.data_path
            second.data = json.loads(second.data_path.read_text())
            self.assertEqual(original, second.intention('duplicate', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'))
            self.assertEqual(0o600, os.stat(first.data_path).st_mode & 0o777)

    def test_non_disposable_manifest_rejected_before_container_observations(self):
        with tempfile.TemporaryDirectory() as directory, patch('demo.assert_local_docker'), patch('demo.REPO', Path(directory)):
            root = Path(directory) / '.local' / 'demo'
            root.mkdir(parents=True)
            save(root / 'demo-environment.json', {'run_id': 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
                'project': 'orderflow-demo-' + 'a' * 32, 'disposable': False})
            with self.assertRaises(AssertionError):
                Demo(root)


if __name__ == '__main__':
    unittest.main()
