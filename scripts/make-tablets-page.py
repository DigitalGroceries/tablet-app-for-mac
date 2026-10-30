"""Build the website's Supported Wacom tablets page from the app's own data.

Same source and same fields as the app's Supported tablets dialog
(SupportedTabletsCatalog.cs): OTD tablet configs (Name, Digitizer Width/Height,
Pen MaxPressure, AuxiliaryButtons ButtonCount) + OTD TABLETS.md (Status, Notes).
Usage: python3 make_tablets_page.py <otd_dir> <site_dir> <app_version> <otd_version>
"""
import glob, html, json, os, sys
from decimal import Decimal, ROUND_HALF_UP

otd, site, app_ver, otd_ver = sys.argv[1:5]

support = {}
for line in open(os.path.join(otd, 'TABLETS.md'), encoding='utf-8'):
    if not line.lstrip().startswith('|'):
        continue
    c = line.split('|')
    if len(c) < 3:
        continue
    name, status = c[1].strip(), c[2].strip()
    if not name or name.lower() == 'tablet' or set(name) <= set('-: ') or set(status) <= set('-: '):
        continue
    support[name.lower()] = (status, '|'.join(c[3:]).strip() if len(c) > 3 else '')

def mac_only(status, notes):
    """Drop note sentences that are about Windows only (the app is for Apple silicon Macs). A
    'Has Quirks' tablet whose only quirk was a Windows one has no known quirk on a Mac."""
    import re
    kept = [x for x in re.split(r'(?<=[.!?])\s+|\s+(?=Windows:)', notes.strip()) if x and not x.startswith('Windows:')]
    cleaned = ' '.join(kept).strip()
    if status == 'Has Quirks' and notes.strip() and not cleaned:
        status = 'Supported'
    return status, cleaned

def mm(x):  # .NET "0" format rounds half away from zero
    return str(Decimal(str(x)).quantize(Decimal('1'), rounding=ROUND_HALF_UP))

rows = []
for f in glob.glob(os.path.join(otd, 'OpenTabletDriver.Configurations/Configurations/**/*.json'), recursive=True):
    d = json.load(open(f, encoding='utf-8-sig'))
    name = (d.get('Name') or '').strip()
    if not name.lower().startswith('wacom '):
        continue
    sp = d.get('Specifications') or {}
    dg = sp.get('Digitizer') or {}
    w, h = dg.get('Width') or 0, dg.get('Height') or 0
    area = f'{mm(w)} × {mm(h)} mm' if w > 0 and h > 0 else '—'
    p = (sp.get('Pen') or {}).get('MaxPressure') or 0
    b = (sp.get('AuxiliaryButtons') or {}).get('ButtonCount') or 0
    st, notes = mac_only(*support.get(name.lower(), ('', '')))
    rows.append((name, area, str(p) if p else '—', str(b), st, notes))
rows.sort(key=lambda r: r[0].lower())  # the dialog's default order: by name

counts = {}
for r in rows:
    counts[r[4] or 'No status'] = counts.get(r[4] or 'No status', 0) + 1
order = ['Supported', 'Has Quirks', 'Missing Features', 'Broken', 'Untested', 'No status']
summary = ', '.join(f'{counts[k]} {k}' for k in order if k in counts)

e = html.escape
trs = []
for i, (name, area, p, b, st, notes) in enumerate(rows, 1):
    cls = {'Supported': 'ok', 'Has Quirks': 'quirk', 'Missing Features': 'miss'}.get(st, 'none')
    pill = f'<span class="pill {cls}">{e(st)}</span>' if st else ''
    tested = ' <span class="muted">(tested with this app)</span>' if name == 'Wacom PTK-440' else ''
    trs.append(f'<tr data-s="{e(st)}"><td class="n">{i}</td><td>{e(name)}{tested}</td><td class="n">{e(area)}</td>'
               f'<td class="n">{e(p)}</td><td class="n">{e(b)}</td><td>{pill}</td><td>{e(notes)}</td></tr>')

tpl = open(os.path.join(site, 'credits/index.html'), encoding='utf-8').read()
head_end = tpl.index('<main id="main">')
foot = tpl[tpl.index('</main>'):]
head = tpl[:head_end]

title = 'Supported Wacom tablets · Tablet App for Mac'
desc = (f'All {len(rows)} Wacom tablets the free Tablet App for Mac driver knows, with the support level the app '
        'shows: Intuos, Intuos Pro, Intuos4, Bamboo, Cintiq and more on Apple silicon Macs.')
import re
head = re.sub(r'<title>.*?</title>', f'<title>{e(title)}</title>', head, count=1, flags=re.S)
head = re.sub(r'(<meta name="description" content=")[^"]*', lambda m: m.group(1) + e(desc), head, count=1)
head = re.sub(r'(<meta property="og:description" content=")[^"]*', lambda m: m.group(1) + e(desc), head, count=1)
head = re.sub(r'(<meta name="twitter:description" content=")[^"]*', lambda m: m.group(1) + e(desc), head, count=1)
head = re.sub(r'(<meta property="og:title" content=")[^"]*', lambda m: m.group(1) + e(title), head, count=1)
head = re.sub(r'(<meta name="twitter:title" content=")[^"]*', lambda m: m.group(1) + e(title), head, count=1)
head = head.replace('/tablet-app-for-mac/credits/"', '/tablet-app-for-mac/tablets/"', 2)  # canonical + og:url
head = head.replace(' aria-current=page', '')
head = head.replace('<a href="/tablet-app-for-mac/tablets/">Tablets</a>',
                    '<a href="/tablet-app-for-mac/tablets/" aria-current=page>Tablets</a>')

main = f'''<main id="main">
<h1>Supported Wacom tablets</h1>
<p class="lead">These are all {len(rows)} Wacom tablets the app's driver knows, with the same support level the app shows when you click <strong>Supported tablets</strong>.</p>
<p>{e(summary)}. The list and support levels come from OpenTabletDriver {e(otd_ver)}, the driver inside Version {e(app_ver)}.</p>
<ul>
<li><strong>Supported</strong>: all tablet functions work.</li>
<li><strong>Has Quirks</strong>: the functions work, but one behaves oddly or needs a workaround (see Notes).</li>
<li><strong>Missing Features</strong>: some tablet functions don't work yet (see Notes).</li>
</ul>
<p>Your tablet's model number (for example PTK-440) is on the label on the back of the tablet. Only the Wacom PTK-440 has been tested with this app. If you use another one, please <a href="/tablet-app-for-mac/#feedback">tell us how it works</a>.</p>
<p><label for="tq">Search</label> <input id="tq" type="search" placeholder="Model, for example PTH-660" autocomplete="off"> <span class="muted" id="tc" aria-live="polite"></span></p>
<table class="tablets">
<caption class="muted">Wacom tablets in OpenTabletDriver {e(otd_ver)}, sorted by name</caption>
<thead><tr><th class="n">#</th><th>Name</th><th class="n">Active area</th><th class="n">Pressure</th><th class="n">Buttons</th><th>Status</th><th>Notes</th></tr></thead>
<tbody>
{chr(10).join(trs)}
</tbody>
</table>
<p class="muted">Not affiliated with Wacom. Model names are Wacom's.</p>
<script>
(function(){{var q=document.getElementById('tq'),c=document.getElementById('tc'),rows=[].slice.call(document.querySelectorAll('table.tablets tbody tr'));
function f(){{var t=q.value.trim().toLowerCase(),n=0;rows.forEach(function(r){{var s=!t||r.textContent.toLowerCase().indexOf(t)>=0;r.hidden=!s;if(s)n++;}});c.textContent=t?(n+' of '+rows.length+' shown'):'';}}
q.addEventListener('input',f);}})();
</script>
'''
os.makedirs(os.path.join(site, 'tablets'), exist_ok=True)
open(os.path.join(site, 'tablets/index.html'), 'w', encoding='utf-8').write(head + main + foot)
print(len(rows), summary)
