"""V30 #200 B5: the manual's in-app buttons ([Text](app:action) lines) are left out of the website Help page and the PDF."""
import os, re, sys, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


class HelpAppButtons(unittest.TestCase):
    def test_strip_removes_only_whole_app_link_lines(self):
        from make_help import strip_app_buttons
        text = "Before.\n\n[Set them up now](app:keys)\n\nSee [the site](https://example.com).\n"
        out = strip_app_buttons(text)
        self.assertNotIn("app:", out)
        self.assertIn("See [the site](https://example.com).", out)

    def test_help_page_has_the_answers_but_no_app_links(self):
        page = open(os.path.join(HERE, "src/help/index.html"), encoding="utf-8").read()
        self.assertIn("Something doesn", page)
        self.assertIn("My ExpressKeys do nothing.", page)
        self.assertNotIn("(app:", page)
        self.assertNotIn("href=\"app:", page)
        self.assertNotIn("Set them up now", page)

    def test_pdf_script_strips_them_too(self):
        script = open(os.path.join(HERE, "../scripts/make-manual-pdf.py"), encoding="utf-8").read()
        self.assertTrue(re.search(r"\(app:", script), "make-manual-pdf.py must leave out the in-app button lines")


if __name__ == "__main__":
    unittest.main()
