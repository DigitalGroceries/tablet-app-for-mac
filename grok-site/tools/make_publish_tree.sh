#!/bin/sh
# Local only. Builds publish-repo from /workspace/otaE/sweep (branch r12) without touching the original history:
# 1. clone, 2. rewrite non-GitHub-noreply authors/committers to Zak4Peace (upstream TheSevenPens/dependabot keep theirs),
# 3. drop tags, 4. one commit that vendors the patched OpenTabletDriver, adds the patch file, issue templates, and removes shots.
set -e
P=/workspace/otaE/site/publish-repo; S=/workspace/otaE/site
cd /workspace; rm -rf $P
git clone -q --single-branch -b r12 --no-local /workspace/otaE/sweep $P
cd $P; git remote remove origin; git tag -l | xargs -r git tag -d >/dev/null
FILTER_BRANCH_SQUELCH_WARNING=1 git filter-branch -f --env-filter '
case "$GIT_AUTHOR_EMAIL" in *users.noreply.github.com) ;; *) export GIT_AUTHOR_NAME=Zak4Peace GIT_AUTHOR_EMAIL=Zak4Peace@users.noreply.github.com;; esac
case "$GIT_COMMITTER_EMAIL" in *users.noreply.github.com|noreply@github.com) ;; *) export GIT_COMMITTER_NAME=Zak4Peace GIT_COMMITTER_EMAIL=Zak4Peace@users.noreply.github.com;; esac' -- --all >/dev/null 2>&1
rm -rf .git/refs/original; git reflog expire --expire=now --all; git gc -q --prune=now
git branch -m r12 main
git rm -q --cached external/OpenTabletDriver; git rm -q .gitmodules
rm -rf external/OpenTabletDriver; cp -a /workspace/otaE/sweep/external/OpenTabletDriver external/OpenTabletDriver
find external/OpenTabletDriver -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
mkdir -p patches; cp $S/otd-macos-f13-f15.patch patches/
cat > patches/README.md <<'R'
# OpenTabletDriver patches

`external/OpenTabletDriver` is OpenTabletDriver v0.6.8-rc1 (upstream commit 126f482, LGPL-3.0), vendored with the patches below already applied, so a plain clone builds the same app.

- `otd-macos-f13-f15.patch`: adds F13, F14 and F15 to the macOS virtual keyboard (needed for Photoshop's Option+F13 / Option+F14 rotation).
R
cp -a $S/publish-extra/.github .
git rm -rq --cached shots shots-before 2>/dev/null || true; rm -rf shots shots-before
printf 'shots/\nshots-before/\n' >> .gitignore
git add -A; git -c user.name=Zak4Peace -c user.email=Zak4Peace@users.noreply.github.com commit -qm "Publish: vendor patched OpenTabletDriver, issue templates, drop screenshots"
git log --oneline -1
