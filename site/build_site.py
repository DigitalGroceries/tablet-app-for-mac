#!/usr/bin/env python3
"""Builds the GitHub Pages site (branch gh-pages) from site/src.

site/src holds every text file of the live site exactly as published. Only the version bits that change at each
STABLE release are placeholders, filled from site/site.json:

  {{V}}            current STABLE version, e.g. 18
  {{P1}}, {{P2}}   the two previous STABLE versions (Zak's rule: the site links the two most recent previous ones)

Images and the manual PDF are not kept here (they live on gh-pages). Pass --manual PATH to copy a new
manual PDF to help/tablet-app-for-mac-manual.pdf.

Usage:
  python3 site/build_site.py --out /path/to/gh-pages-checkout [--manual manual.pdf]
  python3 site/build_site.py --out /path/to/gh-pages-checkout --check   # exit 1 if the output would differ
"""
import argparse, json, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'src')


def render(text, cfg):
    p = cfg['previous']
    return (text.replace('{{V}}', str(cfg['current']))
                .replace('{{P1}}', str(p[0])).replace('{{P2}}', str(p[1])))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--manual')
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    cfg = json.load(open(os.path.join(HERE, 'site.json')))
    diff = []
    for root, _, files in os.walk(SRC):
        for f in files:
            src = os.path.join(root, f)
            rel = os.path.relpath(src, SRC)
            dst = os.path.join(a.out, rel)
            data = render(open(src, encoding='utf-8').read(), cfg)
            old = open(dst, encoding='utf-8').read() if os.path.exists(dst) else None
            if old != data:
                diff.append(rel)
                if not a.check:
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    open(dst, 'w', encoding='utf-8', newline='').write(data)
    if a.manual and not a.check:
        shutil.copyfile(a.manual, os.path.join(a.out, 'help', 'tablet-app-for-mac-manual.pdf'))
    for d in diff:
        print(('differs: ' if a.check else 'wrote: ') + d)
    if a.check and diff:
        sys.exit(1)
    print('site: %d file(s) %s' % (len(diff), 'differ' if a.check else 'changed'))


if __name__ == '__main__':
    main()
