"""Item 30: the website Help page describes Get help as the app manual does (it opens GitHub, not the manual)."""
import os, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
WORDING = "(report a bug, ask a question or send feedback on GitHub)"

class HelpGetHelpWording(unittest.TestCase):
    def test_help_page_matches_manual(self):
        page = open(os.path.join(HERE, "src/help/index.html"), encoding="utf-8").read()
        manual = open(os.path.join(HERE, "../OpenTabletArtist/Assets/manual/manual.md"), encoding="utf-8").read()
        self.assertIn("**Get help** " + WORDING, manual)
        self.assertIn("<strong>Get help</strong> " + WORDING, page)
        self.assertNotIn("<strong>Get help</strong> (this manual)", page)

if __name__ == "__main__":
    unittest.main()
