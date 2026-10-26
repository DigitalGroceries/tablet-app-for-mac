import re, subprocess
code=open('/workspace/otaE/site/tools/build_site.py').read(); code=code[:code.index("CSS = ")]
ns={'__file__':'/workspace/otaE/site/tools/build_site.py'}; exec(code,ns)
src=open('/tmp/ghm/OpenTabletArtist/Assets/manual/manual.md').read()
title, _, rest = src.partition('\n')
body=ns['md'](re.sub(r'!\[([^\]]*)\]\(([^)]+)\)',r'IMG[\1](\2)',rest))
body=re.sub(r'<p>IMG\[([^\]]*)\]\(([^)]+)\)</p>',r'<figure><img src="file:///workspace/otaE/sweep/OpenTabletArtist/Assets/manual/\2" alt="\1"><figcaption>\1</figcaption></figure>',body)
# UNDER CONSTRUCTION banner on the cover: set BANNER = '' to remove it.
BANNER = ''
cover = f'''<section class="cover">{BANNER}<img class="icon" src="file:///workspace/otaE/site/docs/assets/icon-256.png" alt="">
<h1>Tablet App for Mac</h1><p class="sub">User manual · Version 6</p><p class="sub2">macOS on Apple silicon · 2026-10-23</p></section>'''
css='''@page{size:A4;margin:16mm 15mm}*{-webkit-print-color-adjust:exact;print-color-adjust:exact}
html,body{background:#161616}body{font:10.5pt/1.5 -apple-system,"Helvetica Neue",Arial,sans-serif;color:#CFCFCF;margin:0}
h1,h2,h3{color:#E4E4E4}h2{font-size:14pt;margin-top:7mm;border-bottom:1px solid #444;padding-bottom:1mm;break-after:avoid}
strong{color:#E4E4E4}code{background:#262626;padding:0 3px;border-radius:3px}
table{border-collapse:collapse;width:100%;font-size:9pt;margin:2mm 0;break-inside:avoid}th,td{border:1px solid #3A3A3A;padding:1.2mm 2mm;text-align:left;vertical-align:top}th{background:#222;color:#E4E4E4}
figure{margin:3mm 0;break-inside:avoid}figure img{max-width:100%;max-height:120mm;display:block;border:1px solid #3A3A3A;border-radius:2mm}figcaption{color:#9A9A9A;font-size:8.5pt;margin-top:1mm}
ul{padding-left:6mm}h2#quick-start,h2#introduction-and-requirements{break-before:page}
.cover{height:260mm;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center;break-after:page;position:relative}
.cover h1{font-size:30pt;margin:6mm 0 2mm}.cover .icon{width:45mm}.sub{font-size:15pt;color:#CFCFCF;margin:0}.sub2{color:#9A9A9A}
.uc{position:absolute;top:0;left:0;right:0;background:repeating-linear-gradient(135deg,#2A2112 0 5mm,#231C10 5mm 10mm);border:1px solid #5A4520;color:#E4E4E4;font-weight:700;letter-spacing:.12em;padding:4mm;font-size:13pt;border-radius:2mm}'''
open('/tmp/manual6.html','w').write(f'<!doctype html><html lang="en-GB"><head><meta charset="utf-8"><title>Tablet App for Mac - User Manual - Version 6</title><style>{css}</style></head><body>{cover}{body}</body></html>')
subprocess.run(['google-chrome','--headless=new','--no-sandbox','--allow-file-access-from-files','--no-pdf-header-footer','--print-to-pdf=/workspace/otaE/V6/manual/tablet-app-for-mac-manual-version-6.pdf','file:///tmp/manual6.html'],stderr=subprocess.DEVNULL)
