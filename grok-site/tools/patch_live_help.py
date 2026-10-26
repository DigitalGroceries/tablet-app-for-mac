"""Patch the live help/index.html (gh-pages clone) with linked chapters from a manual.md."""
import sys, re, importlib.util, os
here = os.path.dirname(os.path.abspath(__file__))
code = open(os.path.join(here, 'build_site.py')).read(); code = code[:code.index('CSS = ')]
ns = {'__file__': os.path.join(here, 'build_site.py')}; exec(code, ns)
import help_chapters as hc
site, manual = sys.argv[1], sys.argv[2]
p = os.path.join(site, 'help', 'index.html'); s = open(p).read()
lis, secs = hc.build(ns['md'], open(manual).read(), os.path.join(os.path.dirname(manual), 'img'), os.path.join(site, 'assets'), '/tablet-app-for-mac/')
s = re.sub(r'<aside class="side" aria-label="Chapters">.*?</aside>',
           f'<aside class="side" aria-label="Chapters"><nav aria-labelledby="chapters"><p class="muted" id="chapters">Chapters</p><ol>{lis}</ol></nav></aside>', s, count=1, flags=re.S)
s = s.replace('<h2>Screens</h2>', secs + '\n<h2>Screens</h2>', 1)
if hc.JS not in s: s = s.replace('</body>', hc.JS + '</body>', 1)
open(p, 'w').write(s)
css = os.path.join(site, 'assets', 'site.css'); c = open(css).read()
if 'section.chap' not in c: open(css, 'a').write('\n' + hc.CSS)
print(len(re.findall('class="chap"', s)), 'chapters')
