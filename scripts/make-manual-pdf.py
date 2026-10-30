# Manual PDF, Grok's make_pdf_v7.py recipe (same HTML, CSS and cover), printed with Playwright's Chromium.
# Usage: python3 scripts/make-manual-pdf.py <version> <date> <out.pdf>
# Needs: Python Playwright with its Chromium (pip install playwright; python -m playwright install chromium).
# The md() converter and the cover icon come from this repo's site/ folder; override with $SITE_TOOLS (a folder with
# markdown.py or an older build_site.py) and $SITE_ICON. Zak's rule (27 Oct 2026): make a new PDF for every app version.
import re, sys, os
V, DATE, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
ROOT=os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),'..'))
TOOLS=os.environ.get('SITE_TOOLS') or os.path.join(ROOT,'site')
BS=os.path.join(TOOLS,'markdown.py')
if not os.path.exists(BS): BS=os.path.join(TOOLS,'build_site.py')
code=open(BS).read()
if 'CSS = ' in code: code=code[:code.index("CSS = ")]
ns={'__file__':BS}; exec(code,ns)
ICON=os.environ.get('SITE_ICON') or os.path.join(ROOT,'site','icon-256.png')
MAN=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','OpenTabletArtist','Assets','manual'); MAN=os.path.abspath(MAN)
src=open(MAN+'/manual.md',encoding='utf-8').read()
title,_,rest=src.partition('\n')
body=ns['md'](re.sub(r'!\[([^\]]*)\]\(([^)]+)\)',r'IMG[\1](\2)',rest))
body=re.sub(r'<p>IMG\[([^\]]*)\]\(([^)]+)\)</p>',lambda m:f'<figure><img src="file://{MAN}/{m.group(2)}" alt="{m.group(1)}"><figcaption>{m.group(1)}</figcaption></figure>',body)
cover=f'''<section class="cover"><img class="icon" src="file://{ICON}" alt="">
<h1>Tablet App for Mac</h1><p class="sub">User manual · Version {V}</p><p class="sub2">macOS 15 or later, Apple silicon · {DATE}</p></section>'''
css='''@page{size:A4;margin:16mm 15mm}*{-webkit-print-color-adjust:exact;print-color-adjust:exact}
html,body{background:#161616}body{font:10.5pt/1.5 -apple-system,"Helvetica Neue",Arial,sans-serif;color:#CFCFCF;margin:0}
h1,h2,h3{color:#E4E4E4}h2{font-size:14pt;margin-top:7mm;border-bottom:1px solid #444;padding-bottom:1mm;break-after:avoid}
strong{color:#E4E4E4}code{background:#262626;padding:0 3px;border-radius:3px}
table{border-collapse:collapse;width:100%;font-size:9pt;margin:2mm 0;break-inside:avoid}th,td{border:1px solid #3A3A3A;padding:1.2mm 2mm;text-align:left;vertical-align:top}th{background:#222;color:#E4E4E4}
figure{margin:3mm 0;break-inside:avoid}figure img{max-width:100%;max-height:120mm;display:block;border:1px solid #3A3A3A;border-radius:2mm}figcaption{color:#9A9A9A;font-size:8.5pt;margin-top:1mm}
ul{padding-left:6mm}h2#quick-start,h2#introduction-and-requirements{break-before:page}
.cover{height:260mm;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center;break-after:page;position:relative}
.cover h1{font-size:30pt;margin:6mm 0 2mm}.cover .icon{width:45mm}.sub{font-size:15pt;color:#CFCFCF;margin:0}.sub2{color:#9A9A9A}'''
html_path=os.path.abspath(OUT)+'.html'
open(html_path,'w',encoding='utf-8').write(f'<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><title>Tablet App for Mac - User Manual - Version {V}</title><style>{css}</style></head><body>{cover}{body}</body></html>')
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b=p.chromium.launch(args=['--allow-file-access-from-files']); pg=b.new_page()
    pg.goto('file://'+html_path, wait_until='load')
    pg.pdf(path=OUT, prefer_css_page_size=True, print_background=True)
    b.close()
print('PDF', OUT)
