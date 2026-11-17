import importlib.util
import json
import tempfile
import subprocess
import sys
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('ci_report', Path(__file__).with_name('report.py'))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class SafeReportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.raw = self.root / 'raw'
        self.public = self.root / 'public'
        self.raw.mkdir()

    def tearDown(self):
        self.temporary.cleanup()

    def xml(self, outcome='Passed'):
        return f'''<TestRun xmlns="{report.NS['t']}">
          <TestDefinitions><UnitTest id="1"><TestMethod className="OrderFlow.Tests.Inventory" name="Reserved_floor"/></UnitTest></TestDefinitions>
          <Results><UnitTestResult testId="1" outcome="{outcome}" testName="private-cookie-secret">
            <Output><StdOut>Cookie: private-cookie-secret</StdOut><ErrorInfo>
              <Message>X-CSRF-TOKEN: private-cookie-secret</Message>
              <StackTrace>at Call(password: private-cookie-secret) in /checkout/tests/Inventory.cs:line 42</StackTrace>
            </ErrorInfo></Output><Attachments>private-cookie-secret</Attachments>
          </UnitTestResult></Results></TestRun>'''

    def test_failed_invariant_fails_gate_and_reports_location_without_secret_values(self):
        (self.raw / 'tests.trx').write_text(self.xml('Failed'))
        self.assertFalse(report.publish(self.raw, self.public, 'unit', 'trx'))
        text = (self.public / 'summary.json').read_text()
        self.assertNotIn('private-cookie-secret', text)
        self.assertNotIn('X-CSRF-TOKEN', text)
        self.assertIn('tests/Inventory.cs:line 42', text)
        self.assertEqual(json.loads(text)['failed'], 1)

    def test_empty_and_skipped_suites_do_not_pass(self):
        self.assertFalse(report.publish(self.raw, self.public, 'integration', 'trx'))
        (self.raw / 'tests.trx').write_text(self.xml('NotExecuted'))
        self.assertFalse(report.publish(self.raw, self.public, 'integration', 'trx'))

    def test_passing_unit_does_not_satisfy_browser_coverage(self):
        (self.raw / 'tests.trx').write_text(self.xml())
        self.assertTrue(report.publish(self.raw, self.public, 'unit', 'trx'))
        self.assertFalse(report.publish(self.raw, self.public, 'browser', 'trx'))

    def test_junit_failure_values_and_arbitrary_attachments_are_omitted(self):
        (self.raw / 'tests.xml').write_text('''<testsuites><testsuite><testcase classname="tests/cart.test.ts" name="private-cookie-secret">
          <failure message="private-cookie-secret">private-cookie-secret</failure>
          <system-out>private-cookie-secret</system-out></testcase></testsuite></testsuites>''')
        (self.raw / 'trace.zip').write_text('private-cookie-secret')
        self.assertFalse(report.publish(self.raw, self.public, 'frontend', 'junit'))
        self.assertEqual([p.name for p in self.public.iterdir()], ['summary.json'])
        self.assertNotIn('private-cookie-secret', (self.public / 'summary.json').read_text())

    def test_malformed_report_cli_fails_without_republishing_a_stale_success_or_raw_error(self):
        self.public.mkdir()
        (self.public / 'summary.json').write_text('{"valid":true}')
        (self.raw / 'tests.trx').write_text('<private-cookie-secret')
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('report.py')),
            '--raw', str(self.raw), '--public', str(self.public), '--suite', 'unit', '--format', 'trx'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(json.loads((self.public / 'summary.json').read_text())['valid'])
        self.assertNotIn('private-cookie-secret', result.stdout + result.stderr + (self.public / 'summary.json').read_text())

    def test_only_explicit_masked_images_are_copied_with_safe_scenario_identity(self):
        import base64
        png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l3sAAAAASUVORK5CYII=')
        folder = self.raw / 'browser' / 'flow_happy_payment'
        folder.mkdir(parents=True)
        (folder / 'safe-failure-0.png').write_bytes(png)
        (folder / 'automatic-raw.png').write_bytes(png)
        (folder / 'error-context.md').write_text('private-cookie-secret')
        (self.raw / 'tests.trx').write_text(self.xml('Failed'))
        self.assertFalse(report.publish(self.raw, self.public, 'unit', 'trx'))
        self.assertEqual((self.public / 'masked-failure-1.png').read_bytes(), png)
        summary = json.loads((self.public / 'summary.json').read_text())
        self.assertEqual(summary['images'], [{'file': 'masked-failure-1.png', 'scenario': 'flow_happy_payment'}])
        self.assertFalse((self.public / 'automatic-raw.png').exists())


if __name__ == '__main__':
    unittest.main()
