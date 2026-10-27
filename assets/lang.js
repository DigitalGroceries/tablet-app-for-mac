// Language menu (Google Translate page proxy) and, when viewed through the proxy, real download links.
(function () {
  var L = [['en', 'English'], ['es', 'Español'], ['de', 'Deutsch'], ['fr', 'Français'], ['bg', 'Български'], ['ru', 'Русский'], ['ja', '日本語'], ['zh-CN', '中文']];
  var onProxy = /\.translate\.goog$/.test(location.hostname);
  var path = location.pathname, real = 'https://digitalgroceries.github.io' + path;
  var menu = document.getElementById('langmenu');
  if (menu) {
    var ul = menu.querySelector('ul'); ul.innerHTML = '';
    L.forEach(function (l) {
      var href = l[0] === 'en' ? real : 'https://digitalgroceries-github-io.translate.goog' + path + '?_x_tr_sl=en&_x_tr_tl=' + l[0] + '&_x_tr_hl=' + l[0];
      var li = document.createElement('li'), a = document.createElement('a');
      a.href = href; a.textContent = l[1]; a.lang = l[0]; a.className = 'notranslate'; a.setAttribute('translate', 'no');
      li.appendChild(a); ul.appendChild(li);
    });
  }
  // The proxy rewrites links; downloads must go straight to GitHub.
  function fix() {
    [].forEach.call(document.querySelectorAll('a[data-real]'), function (a) {
      if (a.getAttribute('href') !== a.dataset.real) a.setAttribute('href', a.dataset.real);
    });
  }
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[data-real]');
    if (a) { e.preventDefault(); location.href = a.dataset.real; }
  }, true);
  fix(); if (onProxy) { setInterval(fix, 1000); }
})();
// If an image fails to load (a dropped connection), try once more, using the plain PNG/JPEG copy.
(function () {
  function retryImage(img) {
    if (img.dataset.retried || !img.getAttribute('src')) return;
    img.dataset.retried = '1';
    var p = img.parentElement;
    if (p && p.tagName === 'PICTURE') [].slice.call(p.querySelectorAll('source')).forEach(function (s) { s.remove(); });
    var u = img.getAttribute('src');
    img.removeAttribute('srcset');
    img.src = u + (u.indexOf('?') < 0 ? '?' : '&') + 'retry=' + Date.now();
  }
  [].forEach.call(document.querySelectorAll('img'), function (img) {
    img.addEventListener('error', function () { retryImage(img); });
    if (img.complete && img.naturalWidth === 0) retryImage(img);
  });
})();
// Feedback without a GitHub account: the address is put together only when the button is clicked.
(function () {
  var b = document.getElementById('mailfb'), out = document.getElementById('mailfb-out');
  if (!b || !out) return;
  b.addEventListener('click', function () {
    var a = ['organicmachine', 'protonmail.com'].join('@');
    var href = 'mailto:' + a + '?subject=' + encodeURIComponent('Tablet App for Mac feedback');
    out.innerHTML = 'Email <a href="' + href + '">' + a + '</a> and attach your screenshots. ' +
      'Please say which app version you have (it is on Home) and your tablet model.';
    out.hidden = false;
  });
})();
