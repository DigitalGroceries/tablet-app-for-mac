"""Item 12 (#22): pages removed from site/src don't stay published. Run: python3 site/test_build_site.py"""
import os, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(HERE, 'build_site.py')


def run(out, *extra):
    return subprocess.run([sys.executable, BUILD, '--out', out, *extra], capture_output=True, text=True)


class RemovedPages(unittest.TestCase):
    def test_stale_page_is_removed_and_assets_kept(self):
        with tempfile.TemporaryDirectory() as out:
            self.assertEqual(run(out).returncode, 0)
            os.makedirs(os.path.join(out, 'old'))
            for rel in ('old/index.html', 'gone.html', 'pic.png', 'help/m.pdf', 'CNAME', '.git/config'):
                os.makedirs(os.path.dirname(os.path.join(out, rel)) or out, exist_ok=True)
                open(os.path.join(out, rel), 'w').write('x')
            check = run(out, '--check')
            self.assertEqual(check.returncode, 1)
            self.assertIn('stale: gone.html', check.stdout)
            self.assertTrue(os.path.exists(os.path.join(out, 'gone.html')))   # --check changes nothing
            self.assertEqual(run(out).returncode, 0)
            self.assertFalse(os.path.exists(os.path.join(out, 'gone.html')))
            self.assertFalse(os.path.exists(os.path.join(out, 'old', 'index.html')))
            for rel in ('pic.png', 'help/m.pdf', 'CNAME', '.git/config'):
                self.assertTrue(os.path.exists(os.path.join(out, rel)), rel)
            self.assertEqual(run(out, '--check').returncode, 0)


if __name__ == '__main__':
    unittest.main()
