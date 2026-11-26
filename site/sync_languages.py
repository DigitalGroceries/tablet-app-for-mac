#!/usr/bin/env python3
"""Makes the website's Language menu list exactly the languages the app has.

Digital Groceries' rule (26 Nov 2026): the website's Language menu always lists the languages the app has.
(Version 29 added Italian, Swedish and Brazilian Portuguese to the app, Version 30 Hindi and Swahili; the website's menu
still listed the first eight.)

The app's list is `Languages` in OpenTabletArtist/Services/Localizer.cs. This script reads it and rewrites
  - the menu written into every page of site/src (what a browser without scripts shows), and
  - the list in site/src/assets/lang.js (what builds the menu in a browser with scripts),
each language in its own name, in the app's order. The pages are translated by Google's page proxy, so every language
also needs the code Google's translator knows it by (GOOGLE below; the same as the app's unless listed).

    python3 site/sync_languages.py            # rewrite site/src
    python3 site/sync_languages.py --check    # change nothing; exit 1 and say what differs

The app's own test suite fails while the two lists differ (tests/OpenTabletArtist.Tests/SiteLanguagesTests.cs), so a
language added to the app cannot be released without its line in the website's menu.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOCALIZER = ROOT / "OpenTabletArtist" / "Services" / "Localizer.cs"
SRC = ROOT / "site" / "src"
SITE = "https://digitalgroceries.github.io"
PROXY = "https://digitalgroceries-github-io.translate.goog"

# The code Google Translate uses, where it is not the app's. translate.google.com/?tl=pt is "Portuguese (Brazil)"
# (Portugal's is pt-PT); checked 26 Nov 2026.
GOOGLE = {"pt-BR": "pt"}


def app_languages():
    """[(code, own name)] from Localizer.Languages, without the "Match system" line."""
    text = LOCALIZER.read_text(encoding="utf-8")
    block = re.search(r"Languages\s*=\s*\{(.*?)\};", text, re.S)
    if not block:
        raise SystemExit(f"Could not find the Languages list in {LOCALIZER}")
    langs = [(code, name) for code, name in re.findall(r'\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)', block.group(1))]
    if not langs or langs[0][0] != "en":
        raise SystemExit(f"The app's language list does not start with English: {langs[:3]}")
    return langs


def menu_items(real, langs):
    """The <li> lines of one page's menu; `real` is that page's own address."""
    path = real[len(SITE):]
    out = []
    for code, name in langs:
        if code == "en":
            out.append(f'<li><a href="{real}" lang="en">{name}</a></li>')
            continue
        tl = GOOGLE.get(code, code)
        out.append(f'<li><a href="{PROXY}{path}?_x_tr_sl=en&amp;_x_tr_tl={tl}&amp;_x_tr_hl={code}" lang="{code}" '
                   f'class="notranslate" translate="no">{name}</a></li>')
    return "".join(out)


MENU = re.compile(r'(<div class="langpop" id="langmenu"><ul>)(.*?)(</ul>)', re.S)
JS_LIST = re.compile(r"var L = \[.*?\];")


def js_list(langs):
    parts = []
    for code, name in langs:
        tl = GOOGLE.get(code)
        parts.append(f"['{code}', '{name}', '{tl}']" if tl else f"['{code}', '{name}']")
    return "var L = [" + ", ".join(parts) + "];"


def wanted():
    """{file: its text with the app's languages}, for every page with a menu and for lang.js."""
    langs = app_languages()
    new = {}
    pages = sorted(p for p in SRC.rglob("*.html") if 'id="langmenu"' in p.read_text(encoding="utf-8"))
    if not pages:
        raise SystemExit(f"No page with a language menu under {SRC}")
    for page in pages:
        text = page.read_text(encoding="utf-8")
        m = MENU.search(text)
        if not m:
            raise SystemExit(f"{page}: the language menu is not written the way this script knows")
        first = re.search(r'<li><a href="([^"]+)" lang="en">', m.group(2))
        if not first or not first.group(1).startswith(SITE + "/"):
            raise SystemExit(f"{page}: the menu's English line does not point at {SITE}")
        new[page] = text[:m.start(2)] + menu_items(first.group(1), langs) + text[m.end(2):]
    js = SRC / "assets" / "lang.js"
    text = js.read_text(encoding="utf-8")
    if not JS_LIST.search(text):
        raise SystemExit(f"{js}: no 'var L = [...]' list")
    new[js] = JS_LIST.sub(lambda _: js_list(langs), text, count=1)
    return langs, new


def main(argv):
    check = "--check" in argv
    langs, new = wanted()
    differ = [p for p, text in new.items() if p.read_text(encoding="utf-8") != text]
    if check:
        for p in differ:
            print(f"DIFFERS from the app's languages: {p.relative_to(ROOT)}")
        print(f"{len(langs)} languages in the app; {len(new) - len(differ)} of {len(new)} website files match.")
        return 1 if differ else 0
    for p in differ:
        p.write_text(new[p], encoding="utf-8")
        print(f"updated {p.relative_to(ROOT)}")
    print(f"{len(langs)} languages: " + ", ".join(name for _, name in langs) + f". {len(differ)} file(s) changed.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
