# Website (GitHub Pages, branch gh-pages)

`site/src` is every text file of the live site as published; `site/build_site.py` renders it into a gh-pages
checkout. Only the STABLE version numbers are placeholders (`{{V}}`, `{{P1}}`, `{{P2}}` from `site.json`).
Images and the manual PDF stay on gh-pages.

    git clone -b gh-pages https://github.com/DigitalGroceries/tablet-app-for-mac pages
    python3 site/build_site.py --out pages --check      # 0 files differ = site/src matches the live site (also lists stale pages)
    python3 site/build_site.py --out pages --manual tablet-app-for-mac-manual-version-N.pdf

New STABLE: set `current`/`previous` in site.json, edit What is new (index.html) and Releases in site/src,
build, commit on gh-pages. Edit the site here, not by hand on gh-pages, so the two stay the same.

A correction to the website's words is published the same day (build, commit on gh-pages). It does not wait for the
next version of the app: the website is not part of the app.

Three things the app's own tests keep right (`tests/OpenTabletArtist.Tests/Site*Tests.cs`), each a rule of the owner's:

- **Home says what is new in the current version only** (26 Nov 2026). What older versions had is on a page of its own:
  at a new STABLE the notes of the version before move to Releases, which has every version; they do not move down the
  Home page. Home only links there.
- **The Language menu lists the app's languages** (26 Nov 2026). After adding a language to the app:
  `python3 site/sync_languages.py`.
- **Every place that says what the app was tested with names every tested tablet** (25 Nov 2026): ours and the ones
  people using the app have told us work. The list is the table under "Tested system and tablet" in the app's manual.
  Add a tablet there first, then follow the failing tests: README, the manual's first lines, Home, llms.txt, the Help
  page. On Home the sentence at the top names the families only ("Intuos5": the family is enough there, 26 Nov 2026);
  the sentence under the list lower down gives every tablet with its size and model number.

Manual PDF: `python3 scripts/make-manual-pdf.py N YYYY-MM-DD out.pdf` (uses site/markdown.py and site/icon-256.png).

Pages removed from site/src are removed from gh-pages by the next build (text page files only; images and the PDF stay). Test: `python3 site/test_build_site.py`.
