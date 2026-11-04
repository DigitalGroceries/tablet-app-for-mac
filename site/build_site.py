#!/usr/bin/env python3
"""Builds the GitHub Pages site (branch gh-pages) from site/src.

site/src holds every text file of the live site exactly as published. Only the version bits that change at each
STABLE release are placeholders, filled from site/site.json:

  {{V}}            current STABLE version, e.g. 18
  {{P1}}, {{P2}}   the two previous STABLE versions (Zak's rule: the site links the two most recent previous ones)

Images and the manual PDF are not kept here (they live on gh-pages). Pass --manual PATH to copy a new
manual PDF to help/tablet-app-for-mac-manual.pdf; with --check
it is compared byte for byte and a different PDF counts as a difference.

Pages removed from site/src are removed from the output too (item 12, #22): any text page file (see PAGE_EXTS)
in the output that has no source here is deleted, or reported as "stale:" with --check. Images, the manual PDF,
.git and KEEP are never touched.

Usage:
  python3 site/build_site.py --out /path/to/gh-pages-checkout [--manual manual.pdf]
  python3 site/build_site.py --out /path/to/gh-pages-checkout --check   # exit 1 if the output would differ
"""
import argparse, json, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'src')
# Text files the generator owns on gh-pages; everything else there (images, PDF) is kept by hand.
PAGE_EXTS = {'.html', '.htm', '.css', '.js', '.svg', '.txt', '.xml', '.json', '.md', '.nojekyll'}
KEEP = {'CNAME'}


def stale_files(out, src=SRC):
    """Page files in the output with no source in site/src (relative paths, sorted)."""
    stale = []
    for root, dirs, files in os.walk(out):
        dirs[:] = [d for d in dirs if d != '.git']
        for f in files:
            rel = os.path.relpath(os.path.join(root, f), out)
            ext = os.path.splitext(f)[1].lower() or (f.lower() if f.startswith('.') else '')
            if rel in KEEP or ext not in PAGE_EXTS:
                continue
            if not os.path.exists(os.path.join(src, rel)):
                stale.append(rel)
    return sorted(stale)


def render(text, cfg):
    p = cfg['previous']
    return (text.replace('{{V}}', str(cfg['current']))
                .replace('{{P1}}', str(p[0])).replace('{{P2}}', str(p[1])))


def manual_matches(new_pdf, published):
    """True when the published manual PDF is byte-for-byte the new one."""
    if not os.path.exists(published):
        return False
    return open(new_pdf, 'rb').read() == open(published, 'rb').read()


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
    stale = stale_files(a.out)
    for rel in stale:
        diff.append(rel)
        if not a.check:
            os.remove(os.path.join(a.out, rel))
    if a.manual:
        # Item 13 (#22): the manual is part of the check too. Compare bytes; copy only when building.
        rel = os.path.join('help', 'tablet-app-for-mac-manual.pdf')
        dst = os.path.join(a.out, rel)
        if not manual_matches(a.manual, dst):
            diff.append(rel)
            if not a.check:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copyfile(a.manual, dst)
    for d in diff:
        if d in stale:
            print(('stale: ' if a.check else 'removed: ') + d)
        else:
            print(('differs: ' if a.check else 'wrote: ') + d)
    if a.check and diff:
        sys.exit(1)
    print('site: %d file(s) %s' % (len(diff), 'differ' if a.check else 'changed'))


if __name__ == '__main__':
    main()
