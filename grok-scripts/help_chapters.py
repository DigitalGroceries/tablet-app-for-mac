"""Help page chapters: the user manual rendered as linked sections. Used by build_site.py (V7)
and by patch_live_help.py for the live site."""
import re, os, shutil, subprocess, tempfile
from PIL import Image

JS = """<script>
(function(){var links=[].slice.call(document.querySelectorAll('.side a[href^="#"]'));
var map={};links.forEach(function(a){map[a.getAttribute('href').slice(1)]=a;});
var secs=[].slice.call(document.querySelectorAll('section.chap'));
if(!('IntersectionObserver' in window))return;
var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){links.forEach(function(a){a.removeAttribute('aria-current');});var a=map[e.target.id];if(a)a.setAttribute('aria-current','true');}});},{rootMargin:'-15% 0px -75% 0px'});
secs.forEach(function(s){io.observe(s);});})();
</script>"""

CSS = """html{scroll-behavior:smooth}@media (prefers-reduced-motion:reduce){html{scroll-behavior:auto}}
section.chap{scroll-margin-top:16px}section.chap h2{scroll-margin-top:16px}
.side{position:sticky;top:12px;align-self:start;max-height:calc(100vh - 24px);overflow:auto}
.side ol{padding-left:1.4em;margin:0}.side li{margin:2px 0}.side a{color:#B4B4B4;text-decoration:none;display:inline-block;padding:2px 6px;border-radius:4px}
.side a:hover{color:#E4E4E4;background:#242424}.side a[aria-current]{color:#E4E4E4;background:#2A2A2A;box-shadow:inset 2px 0 0 #8C8C8C}
.chap figure{margin:12px 0}.chap figure img{max-width:100%;height:auto;border:1px solid #3A3A3A;border-radius:8px;background:#1a1a1a}
.chap figcaption{color:#9A9A9A;font-size:14px}.backtop{font-size:14px;margin:14px 0 0}.backtop a{color:#9A9A9A}
"""

# Help figures display at ~820 CSS px; 960 covers ~1.2× retina without 1200px bloat.
HELP_MAX_W = 960
WEBP_Q = 72

def slug(t):
    return re.sub(r'[^a-z0-9]+', '-', t.lower()).strip('-')

def _save_help_pair(im, dst, stem):
    """Write WebP (primary) + small PNG fallback. WebP is the <img src> so Brave/Chrome
    preload scanners do not pull the heavy PNG while WebP is selected."""
    png_path = os.path.join(dst, stem + '.png')
    webp_path = os.path.join(dst, stem + '.webp')
    im.save(webp_path, quality=WEBP_Q, method=6)
    # Full PNG first, then pngquant to a palette fallback (keeps type=image/png for old clients).
    tmp = os.path.join(dst, stem + '.full.png')
    im.save(tmp, optimize=True, compress_level=9)
    try:
        subprocess.run(
            ['pngquant', '--force', '--quality=65-80', '--speed', '1',
             '--output', png_path, tmp],
            check=True, capture_output=True)
        os.remove(tmp)
    except (FileNotFoundError, subprocess.CalledProcessError):
        os.replace(tmp, png_path)
    return im.size

def build(md, manual_md, img_dir, out_assets, base):
    """Returns (chapter_list_html, sections_html). Copies manual images to out_assets/manual/."""
    text = manual_md.split('\n', 1)[1]
    parts = re.split(r'^## ', text, flags=re.M)
    dst = os.path.join(out_assets, 'manual'); os.makedirs(dst, exist_ok=True)
    lis, secs = [], []
    fig_i = 0
    for p in parts[1:]:
        title, _, body = p.partition('\n')
        title = title.strip(); sid = slug(title)
        figs = {}
        def img(m, _sid=sid):
            nonlocal fig_i
            alt, src = m.group(1), m.group(2); name = os.path.basename(src)
            stem, ext = os.path.splitext(name)
            src_path = os.path.join(img_dir, name)
            if os.path.exists(src_path):
                im = Image.open(src_path).convert('RGB')
                if im.width > HELP_MAX_W:
                    im = im.resize((HELP_MAX_W, round(im.height * HELP_MAX_W / im.width)), Image.LANCZOS)
                w, h = _save_help_pair(im, dst, stem)
            else:
                w = h = 0
            # First Help figure (Home) is near the top after text chapters — eager so the
            # first painted frame is not empty. Everything else stays lazy.
            loading = 'eager' if fig_i == 0 else 'lazy'
            fig_i += 1
            k = f'FIGX{len(figs)}X'
            # img src = WebP (small). PNG only as <source type="image/png"> for rare no-WebP clients.
            figs[k] = (
                f'<figure><picture>'
                f'<source type="image/webp" srcset="{base}assets/manual/{stem}.webp">'
                f'<source type="image/png" srcset="{base}assets/manual/{stem}.png">'
                f'<img src="{base}assets/manual/{stem}.webp" alt="{alt}" width="{w}" height="{h}" '
                f'loading="{loading}" decoding="async"></picture><figcaption>{alt}</figcaption></figure>'
            )
            return '\n' + k + '\n'
        body = re.sub(r'!\[([^\]]*)\]\(([^)]+)\)', img, body)
        h = md(body)
        for k, v in figs.items(): h = h.replace(f'<p>{k}</p>', v).replace(k, v)
        lis.append(f'<li><a href="#{sid}">{title}</a></li>')
        secs.append(f'<section class="chap" id="{sid}" aria-labelledby="{sid}-h"><h2 id="{sid}-h">{title}</h2>{h}'
                    f'<p class="backtop"><a href="#chapters">↑ Back to chapters</a></p></section>')
    return ''.join(lis), '\n'.join(secs)
