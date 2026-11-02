"""Item 13 (#22): --check covers the manual PDF too. Run: python3 site/test_build_site_manual.py"""
import os, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(HERE, 'build_site.py')
PDF = os.path.join('help', 'tablet-app-for-mac-manual.pdf')


def run(out, *extra):
    return subprocess.run([sys.executable, BUILD, '--out', out, *extra], capture_output=True, text=True)


class ManualCheck(unittest.TestCase):
    def test_check_flags_a_different_manual_and_build_replaces_it(self):
        with tempfile.TemporaryDirectory() as out, tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(run(out).returncode, 0)
            os.makedirs(os.path.join(out, 'help'), exist_ok=True)
            open(os.path.join(out, PDF), 'wb').write(b'%PDF old v17')
            new = os.path.join(tmp, 'v18.pdf'); open(new, 'wb').write(b'%PDF new v18')
            check = run(out, '--manual', new, '--check')
            self.assertEqual(check.returncode, 1)
            self.assertIn('differs: ' + PDF, check.stdout)
            self.assertEqual(open(os.path.join(out, PDF), 'rb').read(), b'%PDF old v17')   # --check copies nothing
            self.assertEqual(run(out, '--manual', new).returncode, 0)
            self.assertEqual(open(os.path.join(out, PDF), 'rb').read(), b'%PDF new v18')
            self.assertEqual(run(out, '--manual', new, '--check').returncode, 0)

    def test_missing_published_manual_is_a_difference(self):
        with tempfile.TemporaryDirectory() as out, tempfile.TemporaryDirectory() as tmp:
            run(out)
            new = os.path.join(tmp, 'm.pdf'); open(new, 'wb').write(b'%PDF')
            self.assertEqual(run(out, '--manual', new, '--check').returncode, 1)


if __name__ == '__main__':
    unittest.main()
