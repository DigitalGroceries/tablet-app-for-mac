#!/usr/bin/env python3
"""Rebuild site/src/help/index.html from the app's own manual (OpenTabletArtist/Assets/manual/manual.md).

V29 (22 Nov 2026): the Help page had been edited by hand and fell behind the manual (it still had the Eraser tab
and Tip Feel "Middle"). From now on run this after every manual change, then build the site as usual.

It keeps the page's head, header, intro card, feedback part and footer, and replaces the chapter list and the
chapters. A manual picture ![alt](img/NAME.png) becomes a figure that loads /tablet-app-for-mac/assets/manual/NAME.webp
(with the .png as fallback). With --images DIR it also writes NAME.png and NAME.webp for every picture the manual
uses into DIR (for gh-pages assets/manual).

Usage: python3 site/make_help.py [--images pages/assets/manual]
"""
import html, os, re, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from markdown import md, inline  # noqa: E402

MANUAL_DIR = os.path.join(ROOT, 'OpenTabletArtist', 'Assets', 'manual')
PAGE = os.path.join(HERE, 'src', 'help', 'index.html')
WEB = '/tablet-app-for-mac/assets/manual/'
MAX_W = 960


def slug(t):
    return re.sub(r'[^a-z0-9]+', '-', t.lower()).strip('-')


def figure(alt, name, first):
    w, h = Image.open(os.path.join(MANUAL_DIR, 'img', name + '.png')).size
    if w > MAX_W:
        w, h = MAX_W, round(h * MAX_W / w)
    a = html.escape(alt, quote=True)
    load = 'eager' if first else 'lazy'
    return (f'<figure><picture><source type="image/webp" srcset="{WEB}{name}.webp">'
            f'<source type="image/png" srcset="{WEB}{name}.png"><img src="{WEB}{name}.webp" alt="{a}" '
            f'width="{w}" height="{h}" loading="{load}" decoding="async"></picture>'
            f'<figcaption>{html.escape(alt, quote=False)}</figcaption></figure>')


def strip_app_buttons(text):
    """V30 #200 B5: a manual line that is only an app link, [Text](app:action), is a button in the app's own manual window.
    It can do nothing on the website, so the line is left out."""
    return re.sub(r'^\[[^\]]+\]\(app:[^)]*\)[ \t]*\n?', '', text, flags=re.M)


def body(text, used, first_fig):
    """markdown.py's md(), plus pictures and numbered steps (md() has neither)."""
    out, chunk = [], []

    def flush():
        if chunk:
            out.append(md('\n'.join(chunk)))
            chunk.clear()

    lines, i = text.split('\n'), 0
    while i < len(lines):
        l = lines[i]
        m = re.match(r'!\[([^\]]*)\]\(img/([^)]+)\.png\)\s*$', l)
        if m:
            flush()
            out.append(figure(m.group(1), m.group(2), first_fig[0]))
            first_fig[0] = False
            used.append(m.group(2))
            i += 1
            continue
        m = re.match(r'(\d+)\. ', l)
        if m:
            flush()
            items = []
            while i < len(lines) and re.match(r'\d+\. ', lines[i]):
                items.append('<li>' + inline(re.sub(r'^\d+\. ', '', lines[i])) + '</li>')
                i += 1
            out.append(f'<ol start="{m.group(1)}">' + ''.join(items) + '</ol>')
            continue
        chunk.append(l)
        i += 1
    flush()
    return '\n'.join(x for x in out if x)


def main():
    images = None
    if '--images' in sys.argv:
        images = sys.argv[sys.argv.index('--images') + 1]
    manual = open(os.path.join(MANUAL_DIR, 'manual.md'), encoding='utf-8').read()
    manual = strip_app_buttons(manual)
    parts = re.split(r'^## (.+)$', manual, flags=re.M)
    chapters = list(zip(parts[1::2], parts[2::2]))   # the title and the text before the first ## are left out
    page = open(PAGE, encoding='utf-8').read()

    a0 = page.index('<aside class="side"')
    a1 = page.index('</aside>', a0) + len('</aside>')
    nav = ('<aside class="side" aria-label="Chapters"><nav aria-labelledby="chapters"><p class="muted" id="chapters">'
           'Chapters</p><ol>' + ''.join(f'<li><a href="#{slug(t)}">{inline(t)}</a></li>' for t, _ in chapters)
           + '</ol></nav></aside>')
    s0 = page.index('<section class="chap"')
    s1 = page.rindex('</section>') + len('</section>')
    used, first = [], [True]
    secs = []
    for t, text in chapters:
        sid = slug(t)
        secs.append(f'<section class="chap" id="{sid}" aria-labelledby="{sid}-h"><h2 id="{sid}-h">{inline(t)}</h2>'
                    + body(text.strip('\n'), used, first)
                    + '<p class="backtop"><a href="#chapters">↑ Back to chapters</a></p></section>')
    new = page[:a0] + nav + page[a1:s0] + '\n'.join(secs) + page[s1:]
    open(PAGE, 'w', encoding='utf-8').write(new)
    print(f'{PAGE}: {len(chapters)} chapters, {len(used)} pictures')
    if images:
        os.makedirs(images, exist_ok=True)
        for name in used:
            im = Image.open(os.path.join(MANUAL_DIR, 'img', name + '.png'))
            im.save(os.path.join(images, name + '.png'))
            im.save(os.path.join(images, name + '.webp'), 'WEBP', quality=88, method=6)
        print(f'{images}: {len(used)} pictures written as .png and .webp')


if __name__ == '__main__':
    main()
