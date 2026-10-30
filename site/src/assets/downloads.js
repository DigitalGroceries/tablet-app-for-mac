// Download counter: sums .dmg and .zip downloads across all releases. Public GitHub API, no token,
// cached for an hour; hidden if the API fails or is rate-limited.
(function () {
  var el = document.getElementById('dlcount'); if (!el) return;
  var KEY = 'tafm-dlcount', HOUR = 3600000;
  function show(n) { if (typeof n === 'number' && n > 0) { el.textContent = 'Downloaded ' + n.toLocaleString('en-GB') + (n === 1 ? ' time' : ' times'); el.hidden = false; } }
  try { var c = JSON.parse(localStorage.getItem(KEY) || 'null'); if (c && Date.now() - c.t < HOUR) { show(c.n); return; } } catch (e) {}
  fetch('https://api.github.com/repos/DigitalGroceries/tablet-app-for-mac/releases?per_page=100')
    .then(function (r) { if (!r.ok) throw 0; return r.json(); })
    .then(function (rel) {
      var n = 0;
      rel.forEach(function (r) { (r.assets || []).forEach(function (a) { if (/\.(dmg|zip)$/i.test(a.name)) n += a.download_count || 0; }); });
      try { localStorage.setItem(KEY, JSON.stringify({ t: Date.now(), n: n })); } catch (e) {}
      show(n);
    })
    .catch(function () {});
})();
