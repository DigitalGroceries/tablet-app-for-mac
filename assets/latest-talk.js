// "Talk about the latest version" button (Help page): always opens the discussion of the newest stable release
// (Zak, 16 Nov 2026: not tied to a version number). Asks GitHub's public API which release is Latest; the
// button's own href (the discussion known when the site was built) stays as the fallback. Cached for an hour.
(function () {
  var el = document.getElementById('talk-latest'); if (!el) return;
  var KEY = 'tafm-latest-talk', HOUR = 3600000;
  function use(u) { if (typeof u === 'string' && /^https:\/\/github\.com\/DigitalGroceries\/tablet-app-for-mac\/discussions\/\d+$/.test(u)) el.href = u; }
  try { var c = JSON.parse(localStorage.getItem(KEY) || 'null'); if (c && Date.now() - c.t < HOUR) { use(c.u); return; } } catch (e) {}
  fetch('https://api.github.com/repos/DigitalGroceries/tablet-app-for-mac/releases/latest')
    .then(function (r) { if (!r.ok) throw 0; return r.json(); })
    .then(function (rel) {
      var u = rel && rel.discussion_url;
      try { if (u) localStorage.setItem(KEY, JSON.stringify({ t: Date.now(), u: u })); } catch (e) {}
      use(u);
    })
    .catch(function () {});
})();
