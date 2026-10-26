"""Hero: the approved app icon (icon-v2) in a dark macOS-style window, as clean vector.
Layout measured from Zak's App-original.png (950 x 1002 window): window corner reaches the edge ~32 px in,
traffic lights 44 px across at centres x = 39.5 / 114.5 / 189, y = 39, icon squircle 824 px at (58, 66)."""
import re
W, H = 950, 1002
R = 32                      # continuous-corner radius (curve spans 1.52866 R = 49 px); matches the visible arc of the original corner (~30 px circle)
def corner_path(w, h, r):
    k = [1.52866, 1.08849, 0.86840, 0.63149, 0.07491, 0.37282, 0.16936]
    a, b, c, d, e, f, g = [v * r for v in k]
    # top-right, then mirrored by sign for the other three (clockwise from top-left straight edge)
    def tr(X, Y, sx, sy, swap):
        pts = [(a, 0), (b, 0), (c, 0), (d, e), (f, g), (g, f), (e, d), (0, c), (0, b), (0, a)]
        out = []
        for (u, v) in pts:
            if swap: u, v = v, u
            out.append((X - sx * u, Y + sy * v))
        return out
    p = []
    seq = [tr(w, 0, 1, 1, False), tr(w, h, 1, -1, True), tr(0, h, -1, -1, False), tr(0, 0, -1, 1, True)]
    s = seq[0]; p.append(f'M{s[0][0]:.3f},{s[0][1]:.3f}')
    for i, s in enumerate(seq):
        if i: p.append(f'L{s[0][0]:.3f},{s[0][1]:.3f}')
        for j in (1, 4, 7):
            p.append('C' + ' '.join(f'{x:.3f},{y:.3f}' for x, y in s[j:j + 3]))
    return ''.join(p) + 'Z'
icon = open('/workspace/otaE/icon-v2/appicon-v2.svg').read()
icon = re.sub(r'<\?xml[^>]*>', '', icon)
inner = icon[icon.index('>', icon.index('<svg')) + 1: icon.rindex('</svg>')]
inner = re.sub(r'<c2pa:[^>]*/>|<c2pa:.*?</c2pa:[^>]*>', '', inner, flags=re.S)
# traffic lights: sampled from Zak's Golden Gate screenshot (see lights.py)
import lights
LD, L = lights.svg()
svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">
<title>Tablet App for Mac</title>
<defs><linearGradient id="win" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1F1F1F"/><stop offset="1" stop-color="#1B1B1B"/></linearGradient>
{LD}
<clipPath id="winclip"><path d="{corner_path(W, H, R)}"/></clipPath></defs>
<path d="{corner_path(W, H, R)}" fill="url(#win)"/>
<path d="{corner_path(W - 1, H - 1, R - 0.5)}" transform="translate(0.5 0.5)" fill="none" stroke="#2E2E2E" stroke-width="1"/>
{L}
<g clip-path="url(#winclip)"><svg x="-42" y="-34" width="1024" height="1024" viewBox="0 0 1024 1024" overflow="visible">{inner}</svg></g>
</svg>'''
open('hero.svg', 'w').write(svg)
open('/tmp/hero.html', 'w').write('<html><body style="margin:0;background:transparent"><img src="file:///workspace/otaE/site/hero/hero.svg" style="display:block;width:950px;height:1002px"></body></html>')
print('ok')
