// Language menu (Google Translate page proxy) and, when viewed through the proxy, real download links.
(function () {
  var L = [['en', 'English'], ['es', 'Español'], ['de', 'Deutsch'], ['fr', 'Français'], ['ja', '日本語'], ['zh-CN', '中文']];
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
