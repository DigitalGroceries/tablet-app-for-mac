# Website (GitHub Pages, branch gh-pages)

`site/src` is every text file of the live site as published; `site/build_site.py` renders it into a gh-pages
checkout. Only the STABLE version numbers are placeholders (`{{V}}`, `{{P1}}`, `{{P2}}` from `site.json`).
Images and the manual PDF stay on gh-pages.

    git clone -b gh-pages https://github.com/DigitalGroceries/tablet-app-for-mac pages
    python3 site/build_site.py --out pages --check      # 0 files differ = site/src matches the live site
    python3 site/build_site.py --out pages --manual tablet-app-for-mac-manual-version-N.pdf

New STABLE: set `current`/`previous` in site.json, edit What is new (index.html) and Releases in site/src,
build, commit on gh-pages. Edit the site here, not by hand on gh-pages, so the two stay the same.

Manual PDF: `python3 scripts/make-manual-pdf.py N YYYY-MM-DD out.pdf` (uses site/markdown.py and site/icon-256.png).
