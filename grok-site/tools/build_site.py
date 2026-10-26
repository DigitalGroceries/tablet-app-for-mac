"""Builds the static GitHub Pages site (docs/) from the build history and the package licence list."""
import re, html, os, sys
SRC = '/workspace/otaE/R18/for-claude'
OUT = os.environ.get('SITE_OUT') or os.path.join(os.path.dirname(__file__), '..', 'docs')
REPO = 'https://github.com/DigitalGroceries/tablet-app-for-mac'
BASE = '/tablet-app-for-mac/'
DMG = REPO + '/releases/latest/download/Tablet-App-for-Mac.dmg'

def inline(t):
    t = html.escape(t, quote=False)
    t = re.sub(r'`([^`]+)`', r'<code>\1</code>', t)
    t = re.sub(r'\*\*([^*]+)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'\[([^\]]+)\]\((https?://[^)]+)\)', r'<a href="\2">\1</a>', t)
    t = re.sub(r'(?<![">])(https?://[^\s<|)]+)', r'<a href="\1">\1</a>', t)
    return t

def md(text):
    out, lines, i = [], text.split('\n'), 0
    while i < len(lines):
        l = lines[i]
        if l.startswith('```'):
            j = i + 1
            while j < len(lines) and not lines[j].startswith('```'): j += 1
            out.append('<pre>' + html.escape('\n'.join(lines[i+1:j])) + '</pre>'); i = j + 1; continue
        m = re.match(r'(#{1,4}) (.*)', l)
        if m:
            n = len(m.group(1)); slug = re.sub(r'[^a-z0-9]+', '-', m.group(2).lower()).strip('-')
            out.append(f'<h{n} id="{slug}">{inline(m.group(2))}</h{n}>'); i += 1; continue
        if l.startswith('- '):
            out.append('<ul>')
            while i < len(lines) and lines[i].startswith('- '): out.append('<li>' + inline(lines[i][2:]) + '</li>'); i += 1
            out.append('</ul>'); continue
        if l.startswith('|'):
            rows = []
            while i < len(lines) and lines[i].startswith('|'): rows.append(lines[i]); i += 1
            cells = lambda r: [c.strip() for c in re.split(r'(?<!\\)\|', r.strip().strip('|'))]
            out.append('<table><tr>' + ''.join(f'<th>{inline(c)}</th>' for c in cells(rows[0])) + '</tr>')
            for r in rows[2:]: out.append('<tr>' + ''.join(f'<td>{inline(c.replace(chr(92)+"|","|"))}</td>' for c in cells(r)) + '</tr>')
            out.append('</table>'); continue
        if l.strip():
            p = [l]; i += 1
            while i < len(lines) and lines[i].strip() and not re.match(r'(#|- |\||```)', lines[i]): p.append(lines[i]); i += 1
            out.append('<p>' + inline(' '.join(p)) + '</p>'); continue
        i += 1
    return '\n'.join(out)


CSS = """:root{--bg:#161616;--panel:#1E1E1E;--line:#333;--text:#CFCFCF;--head:#E4E4E4;--muted:#9A9A9A}
*{box-sizing:border-box}html{-webkit-text-size-adjust:100%}
body{background:var(--bg);color:var(--text);font:16px/1.6 -apple-system,BlinkMacSystemFont,"Segoe UI","Helvetica Neue",Arial,sans-serif;margin:0}
a{color:#D6D6D6;text-underline-offset:2px}a:focus-visible,summary:focus-visible{outline:2px solid #E4E4E4;outline-offset:2px;border-radius:3px}
.skip{position:absolute;left:-999px}.skip:focus{left:12px;top:8px;background:#2A2A2A;color:#E4E4E4;padding:6px 10px;z-index:9}
header{background:#1C1C1C;border-bottom:1px solid var(--line)}
.bar{max-width:1040px;margin:0 auto;padding:10px 20px;display:flex;align-items:center;gap:8px 22px;flex-wrap:wrap}
.brand{display:flex;align-items:center;gap:10px;color:var(--head);font-weight:600;text-decoration:none;margin-right:auto}.brand img{width:28px;height:28px}
nav ul{list-style:none;margin:0;padding:0;display:flex;gap:4px 18px;flex-wrap:wrap}nav a{color:#B4B4B4;text-decoration:none;padding:4px 0}nav a[aria-current]{color:var(--head);border-bottom:2px solid #8C8C8C}
main{max-width:1040px;margin:0 auto;padding:28px 20px 56px}
h1,h2,h3,h4{color:var(--head);font-weight:600;line-height:1.25}h1{font-size:30px;margin:8px 0 12px}h2{font-size:21px;margin-top:36px;padding-bottom:6px;border-bottom:1px solid var(--line)}h3{font-size:16px;margin:20px 0 6px}
.lead{font-size:18px;color:#C4C4C4;max-width:640px}.muted{color:var(--muted);font-size:14px}
code{background:#262626;padding:1px 5px;border-radius:4px;font-size:14px}
pre{background:var(--panel);border:1px solid var(--line);padding:12px;overflow:auto;font-size:12.5px;line-height:1.45;white-space:pre-wrap;max-height:480px}
table{border-collapse:collapse;width:100%;font-size:14px;margin:12px 0;display:block;overflow-x:auto}th,td{border:1px solid var(--line);padding:6px 9px;text-align:left;vertical-align:top}th{background:#222;color:var(--head)}
.hero{display:flex;gap:28px;align-items:center;flex-wrap:wrap;padding:18px 0 8px}.hero{gap:40px}.heroimg img{width:380px;height:auto;display:block;filter:drop-shadow(0 18px 30px rgba(0,0,0,.45))}.hero>div{flex:1 1 320px}
.btn{display:inline-block;background:#3A3A3A;color:var(--head);border:1px solid #5A5A5A;border-radius:8px;padding:10px 18px;font-weight:600;text-decoration:none;margin:10px 12px 6px 0}.btn:hover{background:#444}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px;margin-top:18px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:14px 16px}.card h3{margin-top:0}.card a{text-decoration:none}
.facts{list-style:none;padding:0;display:flex;flex-wrap:wrap;gap:8px}.facts li{background:#222;border:1px solid var(--line);border-radius:999px;padding:3px 12px;font-size:14px}
.rel{border:1px solid var(--line);border-radius:10px;padding:4px 18px 10px;margin:16px 0;background:var(--panel)}.rel h2{border:0;margin:14px 0 2px}.rel .date{color:var(--muted);font-size:14px}
.tag{font-size:12px;border:1px solid #6A6A6A;border-radius:999px;padding:1px 8px;margin-left:8px;vertical-align:middle;color:#D0D0D0}
.toc{font-size:14px;line-height:2}.toc a{margin-right:10px;white-space:nowrap}
.docs{display:grid;grid-template-columns:220px 1fr;gap:32px}.side{font-size:14px;border-right:1px solid var(--line);padding-right:16px}.side ol{padding-left:20px;margin:0}.side li{color:var(--muted);margin:3px 0}
details{border:1px solid var(--line);border-radius:8px;margin:8px 0;background:var(--panel)}summary{cursor:pointer;padding:8px 12px;color:var(--head)}details pre{margin:0 10px 10px}
footer{border-top:1px solid #2A2A2A;color:#8C8C8C;font-size:13px}footer div{max-width:1040px;margin:0 auto;padding:16px 20px}
.slot{margin:18px auto;max-width:820px}.slot img{width:100%;height:auto;border:1px solid #333;border-radius:8px;display:block}.slot figcaption{color:var(--muted);font-size:13px;margin-top:6px}
.tip{display:flex;gap:18px;align-items:center;flex-wrap:wrap}.tip picture{flex:0 0 340px;max-width:100%}.tip>div{flex:1 1 260px}.coffee.small{aspect-ratio:16/9}
.coffee{display:block;width:100%;height:auto;border-radius:10px;border:1px solid var(--line);filter:brightness(.82)}
.pay{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:16px;margin-top:16px}
.pay .card{display:flex;flex-direction:column;gap:10px}.pay img.qr{width:180px;height:180px;border-radius:6px;align-self:center}
.addr{font:13px/1.5 ui-monospace,SFMono-Regular,Menlo,monospace;background:#141414;border:1px solid var(--line);border-radius:6px;padding:8px 10px;word-break:break-all;overflow-wrap:anywhere;user-select:all;color:#DADADA}
button.copy{font:inherit;font-size:14px;background:#3A3A3A;color:var(--head);border:1px solid #5A5A5A;border-radius:6px;padding:6px 12px;cursor:pointer;align-self:flex-start}button.copy:focus-visible{outline:2px solid #E4E4E4}
.uc{background:repeating-linear-gradient(135deg,#2A2112 0 14px,#231C10 14px 28px);border-bottom:1px solid #5A4520;color:#E4E4E4;text-align:center;font-weight:600;letter-spacing:.08em;padding:8px 12px;font-size:14px}
.dl{background:#1E1E1E;border:1px solid #3A3A3A;border-radius:12px;padding:20px 24px;margin:8px 0 24px}.dl .dlrow{display:flex;flex-wrap:wrap;gap:8px 12px;align-items:center;margin:4px 0 10px}.dl .dlrow .big{margin:0}.dl .big{font-size:22px;padding:14px 28px;margin:4px 0 14px}.istep img{display:block;width:100%;max-width:380px;height:auto;margin:8px 0 14px;border:1px solid #333;border-radius:10px}.dl ol{font-size:17px;line-height:1.7;padding-left:22px;margin:6px 0}
.pending{border:1px dashed #8C8C8C;border-radius:8px;padding:8px 12px;color:#D0D0D0;font-size:14px}
@media (max-width:700px){h1{font-size:25px}.docs{grid-template-columns:1fr}.side{border-right:0;border-bottom:1px solid var(--line);padding:0 0 12px}.heroimg{margin:0 auto}.heroimg img{width:70vw;max-width:320px}body{font-size:15px}}.side a[aria-current]{border-bottom:0}

.attn{background:#1F1A12;border:2px solid #C98A1B;border-left-width:8px;border-radius:12px;padding:20px 24px;margin:20px 0 28px;color:#E4E4E4}
.attn h2{color:#E4B04A;border:0;margin:0 0 10px;font-size:1.35rem;letter-spacing:.02em}
.attn ol{padding-left:1.4em}.attn li{margin:0 0 18px;font-size:1.05rem;line-height:1.5}
.attn strong{color:#E4E4E4}.attn a{color:#E4B04A}
.attn .istep img{display:block;max-width:min(100%,480px);height:auto;margin:10px 0 0;border-radius:10px;border:1px solid #3A3A3A}

.dlcount{color:#9A9A9A;font-size:.95rem;margin:8px 0 0}
.lang{position:relative}.lang summary{list-style:none;cursor:pointer;color:#B4B4B4;padding:4px 10px;border:1px solid #3A3A3A;border-radius:6px;font-size:14px}
.lang summary::-webkit-details-marker{display:none}.lang summary::after{content:" ▾"}.lang[open] summary,.lang summary:hover{color:#E4E4E4;background:#242424}
.langpop{position:absolute;right:0;top:calc(100% + 6px);background:#1E1E1E;border:1px solid #3A3A3A;border-radius:8px;padding:8px 6px;min-width:170px;z-index:20;box-shadow:0 8px 24px rgba(0,0,0,.5)}
.langpop ul{list-style:none;margin:0;padding:0}.langpop a{display:block;padding:5px 10px;color:#CFCFCF;text-decoration:none;border-radius:4px}.langpop a:hover,.langpop a:focus-visible{background:#2A2A2A;color:#E4E4E4}
.langpop .muted{margin:6px 10px 2px;font-size:12px}
""" + __import__('help_chapters').CSS + """"""
PAGES = [('releases', 'Releases'), ('help', 'Help'), ('source', 'Source code'), ('credits', 'Credits and licences'), ('support', 'Support')]

# UNDER CONSTRUCTION banner: set to '' to remove it from every page.
BANNER = ''
TITLES = {'': 'Free Tablet App for Mac: Wacom tablet driver settings for macOS',
          'releases': 'Releases and what is new · Tablet App for Mac',
          'help': 'User manual: Wacom Intuos tablet on Mac · Tablet App for Mac',
          'source': 'Source code (GPL-3.0), build and fork · Tablet App for Mac',
          'credits': 'Credits and licences · Tablet App for Mac, OpenTabletDriver',
          'support': 'Support the free tablet driver app for Mac · Tablet App for Mac'}
SITE_URL = 'https://digitalgroceries.github.io' + BASE
OG_IMG = SITE_URL + 'assets/hero/og-1200x630.png'
def head_meta(slug, title, desc):
    t = html.escape(TITLES.get(slug, title + ' · Tablet App for Mac')); d = html.escape(desc)
    url = SITE_URL + (slug + '/' if slug else '')
    ld = ''
    kw = '<meta name="keywords" content="free tablet app for Mac, tablet app for Mac, Mac tablet, free Wacom driver for Mac, Wacom tablet app Mac, Intuos Mac driver, drawing tablet Mac app, Wacom Intuos4 macOS, OpenTabletDriver">'
    if not slug:
        import json as _j
        ld = '<script type="application/ld+json">' + _j.dumps({"@context": "https://schema.org", "@type": "SoftwareApplication",
            "name": "Tablet App for Mac", "operatingSystem": "macOS", "applicationCategory": "UtilitiesApplication",
            "softwareVersion": latest, "downloadUrl": DMG, "url": url, "license": "https://www.gnu.org/licenses/gpl-3.0.html",
            "description": desc, "keywords": "free tablet app for Mac, tablet app for Mac, Mac tablet, free Wacom driver for Mac, Wacom tablet app Mac, Intuos Mac driver, drawing tablet Mac app, Wacom Intuos4 macOS, OpenTabletDriver", "offers": {"@type": "Offer", "price": "0", "priceCurrency": "GBP"}}) + '</script>'
    return (kw + f'<title>{t}</title><meta name="description" content="{d}"><link rel="canonical" href="{url}">'
            f'<meta property="og:type" content="website"><meta property="og:site_name" content="Tablet App for Mac"><meta property="og:locale" content="en_GB">'
            f'<meta property="og:title" content="{t}"><meta property="og:description" content="{d}"><meta property="og:url" content="{url}">'
            f'<meta property="og:image" content="{OG_IMG}"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630">'
            f'<meta property="og:image:alt" content="Tablet App for Mac app icon on a dark background">'
            f'<meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{t}"><meta name="twitter:description" content="{d}">'
            f'<meta name="twitter:image" content="{OG_IMG}"><meta name="twitter:image:alt" content="Tablet App for Mac app icon on a dark background">{ld}')
def page(slug, title, body, desc):
    nav = f'<li><a href="{BASE}"{" aria-current=page" if slug in ("", "index", None) else ""}>Home</a></li>' + ''.join(f'<li><a href="{BASE}{s}/"{" aria-current=page" if s == slug else ""}>{t}</a></li>' for s, t in PAGES)
    doc = f"""<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="color-scheme" content="dark">{head_meta(slug, title, desc)}
<link rel="alternate" type="text/markdown" href="{BASE}llms.txt"><link rel="icon" href="{BASE}assets/icon-64.png"><link rel="stylesheet" href="{BASE}assets/site.css"></head><body>
<a class="skip" href="#main">Skip to content</a>{BANNER}<header><div class="bar"><a class="brand" href="{BASE}"><img src="{BASE}assets/icon-64.png" alt="" width="28" height="28">Tablet App for Mac</a>
<nav aria-label="Main"><ul>{nav}</ul></nav><details class="lang notranslate" id="langmenu-wrap"><summary aria-label="Language">Language</summary><div class="langpop" id="langmenu"><ul><li><a href="https://digitalgroceries.github.io/tablet-app-for-mac/" lang="en">English</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=es&amp;_x_tr_hl=es" lang="es" class="notranslate" translate="no">Español</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=de&amp;_x_tr_hl=de" lang="de" class="notranslate" translate="no">Deutsch</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=fr&amp;_x_tr_hl=fr" lang="fr" class="notranslate" translate="no">Français</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=bg&amp;_x_tr_hl=bg" lang="bg" class="notranslate" translate="no">Български</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=ru&amp;_x_tr_hl=ru" lang="ru" class="notranslate" translate="no">Русский</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=ja&amp;_x_tr_hl=ja" lang="ja" class="notranslate" translate="no">日本語</a></li><li><a href="https://digitalgroceries-github-io.translate.goog/tablet-app-for-mac/?_x_tr_sl=en&amp;_x_tr_tl=zh-CN&amp;_x_tr_hl=zh-CN" lang="zh-CN" class="notranslate" translate="no">中文</a></li></ul><p class="muted">Machine translation by Google</p></div></details></div></header><main id="main">{body}</main>
<footer><div>Tablet App for Mac is free software under the GNU GPL v3.0, a modified version of OpenTabletArtist, using OpenTabletDriver. Not affiliated with Wacom. No tracking. <a href="{REPO}/issues/new/choose">Report a problem</a> (read once a week) · <a href="{BASE}support/">You can buy me a coffee</a>.</div></footer>{__import__('help_chapters').JS if slug == 'help' else ''}<script src="{BASE}assets/lang.js" defer></script><script src="{BASE}assets/downloads.js" defer></script></body></html>"""
    d = os.path.join(OUT, slug) if slug else OUT
    os.makedirs(d, exist_ok=True); open(os.path.join(d, 'index.html'), 'w').write(doc)

# ---- build history -> Keep a Changelog style -------------------------------------------------------
def classify(t):
    l = t.lower()
    if re.search(r'\bfix|bug|crash|broken|regress|no longer|correct', l): return 'Fixed'
    if re.search(r'\bremov|drop(ped)? ', l): return 'Removed'
    if re.search(r'\badd|new |introduc|support', l): return 'Added'
    return 'Changed'
def clean(t):
    t = re.sub(r'^(Build [A-Z]\d*\.?\d?\s*(\([^)]*\))?:\s*)', '', t)
    t = re.sub(r'^(R\d+\s*(\([^)]*\))?:\s*)', '', t)
    t = re.sub(r',?\s*0\.\d+\.\d+-[A-Z0-9]+\s*$', '', t).strip()
    return t[:1].upper() + t[1:] if t else t
hist = open(f'{SRC}/build-history.md').read()
builds = []
for blk in re.split(r'\n## ', hist)[1:]:
    head, *rest = blk.split('\n'); ver = head.split(' ')[0]
    m = re.search(r'Date: (\S+)', blk); date = m.group(1) if m else ''
    items = [re.sub(r'^- `\w+` [\d:]+ [\d-]+: ', '', l) for l in rest if l.startswith('- `')]
    items = [clean(i) for i in items if 'WIP' not in i and 'wip' not in i.split(':')[0].lower()]
    if items: builds.append((ver, date, items))
builds.reverse()
UNRELEASED = {'Added': ['Disable pen tip and Disable eraser: the switched-off end makes no strokes, also in Scribble.',
                        'Eraser tab parity with Pen: tilt, Feel curve with Customise, live pressure, button cards, Movement and input frame. Shared settings are marked "Shared with Pen".',
                        'Touch ring: the active mode is highlighted live; the on-screen centre button steps the highlight.',
                        'In-app user manual (the ? button).'],
              'Changed': ['Disable pressure sensitivity now draws at a fixed 30% while pressed, in every app.',
                          'Pen tool icon taken from the app icon.',
                          'Touch Ring page: full-width modes, About top right, angle shown only while the ring is touched.',
                          'Text audit across the app: shorter, consistent wording in British English.',
                          'Links now point to this site.']}
def rel_html(ver, date, groups, tag=''):
    anchor = 'v' + ver.lower()
    out = [f'<section class="rel" aria-labelledby="{anchor}"><h2 id="{anchor}"><a href="#{anchor}">{html.escape(ver)}</a>{tag}</h2><div class="date">{date}</div>']
    for g in ('Added', 'Changed', 'Fixed', 'Removed'):
        if groups.get(g): out.append(f'<h3>{g}</h3><ul>' + ''.join(f'<li>{inline(i)}</li>' for i in groups[g]) + '</ul>')
    return ''.join(out) + '</section>'
# Version 1: versions are plain numbers 1 to 99. The earlier internal test builds are folded away.
V1 = {k: list(v) for k, v in UNRELEASED.items()}
V1.setdefault('Changed', [])
V1['Changed'] = ['Version names are now plain numbers: this is Version 1.',
                 'Touch ring: the selected mode is lit, its number shows on the centre button, and the angle and direction sit under the ring.',
                 'Touch ring: name and Function boxes are only as wide as their longest entry.',
                 'Text and options for other systems are hidden.'] + V1['Changed']
V5 = {'Added': ['Pen button test on Tablet › Pen: a held pen button lights on the pen picture, and the line above says which one.',
                'Manual Pen Tip Pressure and Manual Eraser Pressure sliders, in their own frame under Tip Feel and Eraser Feel. Used while pressure sensitivity is off.',
                'Quit prompt for unsaved changes: Cancel, Don\'t Save or Save.',
                'Scribble uses a precise crosshair cursor.'],
      'Changed': ['Links in the app point to this site.',
                  'Starts silently at login: only the menu bar pen icon shows.',
                  'Only one copy runs at a time.',
                  '"More (OTA)" is now "More". "Buttons" is now "Tablet buttons".',
                  'Touch ring: angle and direction under the ring; 12 o\'clock is 0°.'],
      'Fixed': ['Touch ring: a clockwise turn now reads Clockwise.',
                'Touch ring: the dot sits under the finger (it was a quarter turn off).',
                'Fix on Home works while the pen tip or eraser is disabled.',
                'Quitting no longer hangs.'],
      'Removed': ['The duplicate About button at the bottom of the Tablet pages.',
                  'The fixed 30% pressure while pressure sensitivity is off (replaced by the Manual Pressure sliders).']}
rels = [rel_html('Version 7', '2026-10-23', {'Added': ['Install help in the manual and on the home page, with pictures of each macOS message.'], 'Changed': ['Needs macOS 15 or later (the versions .NET 10 supports).']}, '<span class="tag">Latest</span>'), rel_html('Version 6', '2026-10-23', V5), rel_html('Version 1', '2026-10-22', V1)]
def test_name(ver):
    m = re.search(r'R(\d+)$', ver)
    return f'Test build {m.group(1)}' if m else 'Test build'
old = []
for ver, date, items in builds:
    g = {}
    for i in items: g.setdefault(classify(i), []).append(i)
    old.append(rel_html(test_name(ver), date, g))
rels.append('<details class="rel"><summary>Earlier test builds (before Version 1, not released)</summary>' + ''.join(old) + '</details>')
toc = ''

lic = open(f'{SRC}/package-licences.md').read()
summary = lic[lic.index('## Summary'):lic.index('## Full licence texts')]
summary = re.sub(r'^## Summary\n', '', summary).replace(' The Windows-only Windows Ink plugin (GPL-3.0) and VMulti package (MIT) are not part of the Mac build.', '')
texts = []
for blk in lic[lic.index('## Full licence texts'):].split('\n### ')[1:]:
    title, _, body = blk.partition('\n')
    m = re.search(r'```\n(.*?)\n```', body, re.S)
    inner = f'<pre>{html.escape(m.group(1))}</pre>' if m else f'<p class="muted" style="padding:0 12px">{inline(body.strip())}</p>'
    texts.append(f'<details><summary>{html.escape(title)}</summary>{inner}</details>')

os.makedirs(os.path.join(OUT, 'assets'), exist_ok=True)
open(os.path.join(OUT, 'assets', 'site.css'), 'w').write(CSS)
open(os.path.join(OUT, '.nojekyll'), 'w').write('')
latest = '7'
from PIL import Image
SHOTS = '/workspace/otaE/V5/shots-used'
USED = set()
def islot(sid, alt):
    assert sid not in USED, sid; USED.add(sid)
    d = os.path.join(OUT, 'assets', 'shots'); os.makedirs(d, exist_ok=True)
    im = Image.open(f'{SHOTS}/{sid}.png').convert('RGB')
    # Install/setup shots display at ~480 CSS px; keep ≤720 for retina.
    if im.width > 720: im = im.resize((720, round(im.height * 720 / im.width)), Image.LANCZOS)
    im.save(f'{d}/{sid}.png', optimize=True, compress_level=9); im.save(f'{d}/{sid}.webp', quality=80, method=6); w, h = im.size
    return (f'<picture class="istep"><source type="image/webp" srcset="{BASE}assets/shots/{sid}.webp"><img src="{BASE}assets/shots/{sid}.png" alt="{alt}" width="{w}" height="{h}" loading="eager" decoding="async"></picture>')
def slot(sid, alt, cap):
    """A real Mac screenshot (Version 5 set). Each image is used once on the whole site."""
    assert sid not in USED, sid; USED.add(sid)
    d = os.path.join(OUT, 'assets', 'shots'); os.makedirs(d, exist_ok=True)
    im = Image.open(f'{SHOTS}/{sid}.png').convert('RGB')
    # Slots display at max ~820 CSS px; 1200 covers ~1.5× without 4K bloat.
    if im.width > 1200: im = im.resize((1200, round(im.height * 1200 / im.width)), Image.LANCZOS)
    im.save(f'{d}/{sid}.png', optimize=True, compress_level=9); im.save(f'{d}/{sid}.webp', quality=78, method=6)
    w, h = im.size
    return (f'<figure class="slot" data-slot="{sid}"><picture><source type="image/webp" srcset="{BASE}assets/shots/{sid}.webp">'
            f'<img src="{BASE}assets/shots/{sid}.png" alt="{alt}" width="{w}" height="{h}" loading="lazy" decoding="async"></picture>'
            f'<figcaption>{cap}</figcaption></figure>')
S = BASE + 'assets/support/'
def picture(mug, alt, band, cls='coffee'):
    """Wide crop for wide header bands (>= 700 px), 16:9 on narrow layouts and in cards."""
    sq = lambda kind, ext, ws: ', '.join(f'{S}coffee-{mug}-{kind}-{w}.{ext} {w}w' for w in ws)
    wide = (960, 1600); tall = (480, 800, 1200)
    src = ''
    if band:
        src += f'<source media="(min-width:700px)" type="image/webp" srcset="{sq("wide","webp",wide)}" sizes="min(1000px,100vw)">'
        src += f'<source media="(min-width:700px)" type="image/jpeg" srcset="{sq("wide","jpg",wide)}" sizes="min(1000px,100vw)">'
    sizes = '100vw' if band else '(min-width:700px) 340px, 100vw'
    src += f'<source type="image/webp" srcset="{sq("16x9","webp",tall)}" sizes="{sizes}">'
    return (f'<picture>{src}<img class="{cls}" src="{S}coffee-{mug}-16x9-800.jpg" srcset="{sq("16x9","jpg",tall)}" sizes="{sizes}" '
            f'alt="{alt}" width="1280" height="720" loading="lazy" decoding="async"></picture>')
page('', 'Home', f"""<div class="hero"><picture class="heroimg"><source type="image/webp" srcset="{BASE}assets/hero/hero@1x.webp 950w, {BASE}assets/hero/hero@2x.webp 1900w" sizes="(min-width:700px) 380px, 70vw"><img src="{BASE}assets/hero/hero@1x.png" srcset="{BASE}assets/hero/hero@1x.png 950w, {BASE}assets/hero/hero@2x.png 1900w" sizes="(min-width:700px) 380px, 70vw" alt="Tablet App for Mac icon: a drawing tablet and pen in a macOS window" width="950" height="1002" fetchpriority="high"></picture><div>
<h1 style="border:0;margin-top:0">Your Mac tablet, set up the Wacom way</h1><p class="lead">A free alternative to the Wacom driver for Apple silicon Macs, built on OpenTabletDriver. Wacom’s last Intuos4 driver (6.3.41, 2020) only supports up to macOS 11. Laid out like the Wacom settings, so there is nothing new to learn. Tested with a Wacom Intuos4 on macOS 27.</p>
<span class="muted">macOS 15 or later · Apple silicon · <a href="releases/">What is new</a></span></div></div>
<section class="dl" aria-label="Download"><div class="dlrow"><a class="btn big" href="{DMG}" data-real="{DMG}">⬇ Download for Mac<span class="sub">(silicon)</span></a></div><span class="muted">Version {latest} · free · for Apple silicon Macs</span><p id="dlcount" class="dlcount" hidden></p></section>
<section class="attn" aria-labelledby="attn"><h2 id="attn">ATTENTION: TO INSTALL IT, FOLLOW THESE STEPS</h2>
<p>If the Wacom driver is installed, remove it first with Wacom's own uninstaller (Applications › Wacom Tablet › Wacom Tablet Utility), then restart your Mac.</p>
<ol><li>Open the downloaded file (<strong>Tablet-App-for-Mac.dmg</strong>), and drag <strong>Tablet App for Mac</strong> onto the <strong>Applications</strong> folder.</li>
<li>Open the app from Applications. The first time, your Mac says it was not opened. Click <strong>Done</strong>, not Move to Bin.{islot('install-01-not-opened','The message: Tablet App for Mac.app Not Opened, with the Done and Move to Bin buttons')}</li>
<li>Open <strong>System Settings › Privacy &amp; Security</strong>, scroll down to <strong>Security</strong> and click <strong>Open Anyway</strong>.{islot('install-02-open-anyway','System Settings, Privacy and Security, Security section, with the Open Anyway button next to Tablet App for Mac')}</li>
<li>macOS asks once more. Click <strong>Open Anyway</strong>, not the blue Move to Bin button.{islot('install-03-open-anyway-confirm','The message: Open Tablet App for Mac.app?, with Move to Bin in blue, Open Anyway and Done')}</li>
<li>Confirm with Touch ID, or click <strong>Use Password…</strong> and enter your Mac login password. The app then opens, and macOS won't ask again.{islot('install-04-touch-id','The Touch ID prompt for Privacy and Security, with Use Password and Cancel')}</li>
<li>The app opens a setup window with two rows, <strong>Read the pen</strong> and <strong>Move the pointer</strong>. Click each button. Or do it by hand: open <strong>System Settings › Privacy &amp; Security › Input Monitoring</strong> and turn on <strong>Tablet App for Mac</strong>. This lets the app read your pen.{islot('setup-read-pen','Privacy and Security, Input Monitoring, with the switch for Tablet App for Mac turned on')}</li>
<li>Open <strong>System Settings › Privacy &amp; Security › Accessibility</strong> (on macOS 27: <strong>Device Control and Data Access</strong>) and turn on <strong>Tablet App for Mac</strong>. This lets the pen move the pointer. Then quit and reopen the app.{islot('setup-move-pointer','Privacy and Security, Device Control and Data Access, with the switch for Tablet App for Mac turned on')}</li></ol>
<p><strong>Already on Version 6? Install Version 7 by hand once, then redo both permissions once (the setup window guides you). After that, updates install from inside the app; if macOS asks for the permissions again, the setup window guides you.</strong></p>
<p class="muted">If the pen is read but the pointer doesn't move, Accessibility is missing; the app shows a button that opens the right page. After an update, remove the app from both lists with <strong>−</strong>, add it again with <strong>+</strong>, and reopen it. If the Wacom driver is installed, remove it first with Wacom's own uninstaller (Wacom Tablet Utility), then restart your Mac; the app warns you if it finds it. Need help? See the <a href="help/">user manual</a>.</p></section>
{slot('02-home-overview','Home page of Tablet App for Mac','Home: your tablets and their status.')}<ul class="facts"><li>Free and open source (GPL-3.0)</li><li>No tracking or telemetry</li><li>For Apple silicon Macs</li><li>Tested on macOS 27.0.1 with an Intuos4 Small (PTK-440)</li></ul>
<p class="muted">Tested only on that system and tablet. Use at your own risk.</p>
<h2>What is new in Version 7</h2><ul><li>Install help with pictures of each macOS message.</li><li>Touch ring: the dot sits under your finger; 12 o'clock is 0°, and clockwise reads Clockwise.</li><li>Pen button test: a held button lights on the pen picture.</li><li>Manual Pen Tip Pressure and Manual Eraser Pressure sliders, used while pressure sensitivity is off.</li><li>Starts silently at login, in the menu bar only. Only one copy runs.</li><li>Quit asks to save unsaved changes: Cancel, Don't Save or Save.</li></ul>
{slot('34-ring-turn','Touch ring page with the finger on the ring','Touch ring: the active mode is lit, its number is on the centre button, and the angle and direction show under the ring.')}
{slot('05-pen-tool-row','Tablet › Pen page','Tablet › Pen: Tip Feel, Manual Pen Tip Pressure, the pen-button test and Pen input.')}
{slot('63-scribble-draw','Scribble canvas with pen strokes','Scribble: draw to check pressure, with a precise crosshair cursor.')}
<h2>Support</h2><div class="tip">{picture('plain', 'A guitar and a cup of coffee', False, 'coffee small')}<div><p>If you'd like to support future updates, you can <a href="https://digitalgroceries.github.io/tablet-app-for-mac/support/">buy me a coffee</a> with PayPal, Bitcoin or Monero.</p></div></div><h2>Requirements</h2><ul><li>A Mac with Apple silicon.</li><li>macOS 15 or later.</li><li>The OpenTabletDriver daemon (included).</li><li>Input Monitoring and Accessibility permissions (Accessibility is called Device Control and Data Access on macOS 27).</li><li>Uninstall the Wacom driver first, as both can&#39;t run at once.</li></ul>
<h2>For developers</h2><div class="grid"><div class="card"><h3><a href="releases/">Releases</a></h3><p class="muted">Every build and what changed.</p></div>
<div class="card"><h3><a href="help/">Help</a></h3><p class="muted">The user manual.</p></div>
<div class="card"><h3><a href="source/">Source code</a></h3><p class="muted">Repository, origins and build steps.</p></div>
<div class="card"><h3><a href="credits/">Credits and licences</a></h3><p class="muted">OpenTabletDriver and every package used.</p></div></div>
""", 'Free tablet app for Mac: set up a Wacom drawing tablet on macOS, Apple silicon. Pen pressure, buttons, touch ring, ExpressKeys.')
page('releases', 'Releases', f"""<h1>Releases</h1><p>Newest first. Dates are ISO 8601. Downloads are on <a href="{REPO}/releases">GitHub Releases</a>; earlier builds stay available there for going back.</p>{toc}""" + ''.join(rels), 'Release notes for Tablet App for Mac: what changed in each version of the free Wacom tablet settings app for macOS on Apple silicon, newest first.')
CH = ['Before you start', 'Quick start', 'Requirements', 'Install and first launch', 'Home', 'Tablet › Pen', 'Tablet › Eraser', 'Mapping', 'ExpressKeys', 'Touch ring', 'Functions', 'More', 'Scribble', 'Settings', 'Advanced', 'Presets and saving', 'Quitting', 'Wacom driver conflict', 'Updating', 'Going back to an earlier build', 'Uninstalling', 'Troubleshooting', 'Glossary']
import help_chapters as _hc
_MAN = '/workspace/otaE/sweep/OpenTabletArtist/Assets/manual/manual.md'
_lis, _secs = _hc.build(md, open(_MAN).read(), os.path.join(os.path.dirname(_MAN), 'img'), os.path.join(OUT, 'assets'), BASE)
page('help', 'Help', f"""<div class="docs"><aside class="side" aria-label="Chapters"><nav aria-labelledby="chapters"><p class="muted" id="chapters">Chapters</p><ol>{_lis}</ol></nav></aside>
<div><h1>Help</h1><div class="card"><p><strong>User manual for Version {latest}</strong>: <a href="{BASE}help/tablet-app-for-mac-manual.pdf">PDF (A4)</a>. The same text opens in the app with the ? button.</p><p class="muted">Troubleshooting and per-app notes follow.</p></div>
{_secs}
<h2 id="feedback">Report a problem or suggest a feature</h2><p><a class="btn" href="{REPO}/issues/new/choose">Give feedback</a></p><p class="muted">Bug reports ask for the app version (About), macOS version and tablet model. Questions go to <a href="{REPO}/discussions">Discussions</a>. Feedback is read once a week.</p></div></div>""", 'User manual (PDF) and help for Tablet App for Mac, the free app for setting up a Wacom drawing tablet on macOS: pen, eraser, mapping and touch ring.')
page('source', 'Source code', f"""<h1>Source code</h1><p class="lead">The full source is public under the GNU General Public License v3.0.</p>
<a class="btn" href="{REPO}">View on GitHub</a> <a class="btn" href="{REPO}/archive/refs/heads/main.zip">Download ZIP</a>
<h2>Based on</h2><ul><li><a href="https://github.com/TheSevenPens/OpenTabletArtist">OpenTabletArtist</a> by TheSevenPens (GPL-3.0). This app is a modified version; changes are listed in the release notes.</li>
<li><a href="https://github.com/OpenTabletDriver/OpenTabletDriver">OpenTabletDriver</a> (LGPL-3.0), the driver and daemon, included in the repository with a small patch set (see patches).</li></ul>
<h2>Use it, fork it, improve it</h2><p>Anyone may use, copy, change and share this code, and publish an improved version on their own page, under the <a href="{REPO}/blob/main/LICENSE">GNU GPL v3.0</a>. Fork it on GitHub to start. If you share a changed version, share its source under the same licence and say what you changed. The bundled OpenTabletDriver keeps its own LGPL-3.0 licence.</p>
<h2>Build</h2><p>.NET 10 SDK, on an Apple silicon Mac:</p><pre>git clone {REPO}.git
cd tablet-app-for-mac
dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true</pre><p class="muted">On a Mac this also assembles dist/OpenTabletArtist.app. The app is unsigned.</p><p class="muted">Tests: <code>dotnet test tests/OpenTabletArtist.Tests</code> and <code>dotnet test tests/OpenTabletArtist.UiTests</code>.</p>""", 'Source code of Tablet App for Mac under the GPL-3.0: build steps with the .NET 10 SDK, the bundled OpenTabletDriver, and how to fork and improve it.')
page('credits', 'Credits and licences', f"""<h1>Credits and licences</h1>
<h2>OpenTabletDriver</h2><p>Tablet input, the daemon and the plugin API are <a href="https://github.com/OpenTabletDriver/OpenTabletDriver">OpenTabletDriver</a>, © the OpenTabletDriver contributors, under the GNU LGPL v3.0. This app would not exist without it.</p>
<h2>OpenTabletArtist</h2><p>The app is a modified version of <a href="https://github.com/TheSevenPens/OpenTabletArtist">OpenTabletArtist</a> by TheSevenPens, under the GNU GPL v3.0.</p>
<h2>Third-party packages</h2>{md(summary)}<h2>Full licence texts</h2>{''.join(texts)}""", 'Credits and licences for Tablet App for Mac: OpenTabletDriver (LGPL-3.0), OpenTabletArtist (GPL-3.0) and every package the app uses, with full texts.')
print('ok', len(builds), 'builds')

import json
SUP = json.load(open(os.path.join(os.path.dirname(__file__), '..', 'support.json')))
def support_body(photo):
    pend = ''
    def card(key, title, value, extra=''):
        return f"""<div class="card"><h2 style="margin-top:0;border:0">{title}</h2><img class="qr" src="{BASE}assets/support/qr-{key}.svg" alt="{title} QR code" width="180" height="180">
<div class="addr" id="a-{key}">{('<a href="'+html.escape(value)+'" rel="noopener">'+html.escape(value)+'</a>') if key=='paypal' else html.escape(value)}</div>{extra}<button class="copy" type="button" data-copy="a-{key}">Copy {'link' if key=='paypal' else 'address'}</button></div>"""
    pp = f'<a class="btn" href="{html.escape(SUP["paypal"])}" rel="noopener">Donate with PayPal</a>'
    return f"""<h1>You can buy me a coffee</h1>{picture(photo, 'A guitar and a cup of coffee', True)}
<p class="lead">Tablet App for Mac is free. If it helps you, a tip of any amount is welcome. Thank you.</p>{pend}
<div class="pay">{card('paypal','PayPal',SUP['paypal'],pp)}
{card('bitcoin','Bitcoin',SUP['bitcoin'])}{card('monero','Monero',SUP['monero'])}</div>
<details><summary>Banner with all three codes</summary><img src="{BASE}assets/support/banner-qr.png" alt="Banner: You can buy me a coffee, with PayPal, Bitcoin and Monero QR codes" width="2407" height="640" loading="lazy" style="width:100%;height:auto"></details>
<script>document.querySelectorAll('button.copy').forEach(b=>b.addEventListener('click',()=>{{const t=document.getElementById(b.dataset.copy).textContent;navigator.clipboard.writeText(t).then(()=>{{const o=b.textContent;b.textContent='Copied';setTimeout(()=>b.textContent=o,1500)}})}}))</script>"""
page('support', 'Support', support_body('thanks'), 'Support Tablet App for Mac, the free tablet driver app for macOS, with a tip of any amount by PayPal, Bitcoin or Monero. Thank you.')

# llms.txt (llmstxt.org): plain navigation facts.
SITE = 'https://digitalgroceries.github.io' + BASE
open(os.path.join(OUT, 'llms.txt'), 'w').write(f"""# Tablet App for Mac

> Free, open-source (GPL-3.0) settings app for drawing tablets on macOS 15 or later (Apple silicon), built on OpenTabletDriver. Current version: Version {latest}.

Tested only with macOS 27.0.1 and a Wacom Intuos4 Small (PTK-440).

## Download and install

- [Download for Mac (silicon) (Tablet-App-for-Mac.dmg)]({DMG}): latest, tested.
- [Install steps]({SITE}#get): open the .dmg and drag Tablet App for Mac to Applications; open it and click Done at "Not Opened"; in System Settings › Privacy & Security › Security click Open Anyway; click Open Anyway again; confirm with Touch ID or password. Then turn on Tablet App for Mac in Privacy & Security › Input Monitoring (reads the pen) and › Accessibility (moves the pointer), and reopen the app.

## Documentation

- [User manual (PDF)]({SITE}help/tablet-app-for-mac-manual.pdf)
- [Help]({SITE}help/)
- [Release notes]({SITE}releases/)

## Source

- [Source code repository]({REPO})
- [Build steps]({SITE}source/): .NET 10 SDK; `dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true`
- [Repository layout (AGENTS.md)]({REPO}/blob/main/AGENTS.md)
- [Licence: GNU GPL v3.0]({REPO}/blob/main/LICENSE); the bundled OpenTabletDriver is LGPL-3.0.

## Optional

- [Credits and licences]({SITE}credits/)
- [Report a problem]({REPO}/issues/new/choose)
""")

open(os.path.join(OUT, 'robots.txt'), 'w').write(f"User-agent: *\nAllow: /\nSitemap: {SITE_URL}sitemap.xml\n")
open(os.path.join(OUT, 'sitemap.xml'), 'w').write('<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'
    + ''.join(f'<url><loc>{SITE_URL}{s + "/" if s else ""}</loc><lastmod>2026-10-23</lastmod></url>' for s in ['', 'releases', 'help', 'source', 'credits', 'support']) + '</urlset>\n')

import shutil as _sh
for _f in ('lang.js', 'downloads.js'): _sh.copy(os.path.join(os.path.dirname(os.path.abspath(__file__)), _f), os.path.join(OUT, 'assets', _f))
