"""Traffic lights rebuilt from Zak's App-original.png (macOS 27 Golden Gate screenshot).
All colours are sampled from that image; geometry measured from it. Layers per button:
base fill, top gloss (linear, fades out by ~45% of the radius), bottom glow (elliptical radial),
and a 1.8 px rim ring that is bright at top and bottom and transparent at the sides."""
from PIL import Image
import numpy as np
SRC = '/workspace/otaE/site/hero/App-original.png'
a = np.asarray(Image.open(SRC).convert('RGB')).astype(float)
CY, R = 37.85, 22.3
CXS = [39.8, 114.2, 188.7]
hx = lambda c: '#%02X%02X%02X' % tuple(int(round(v)) for v in np.clip(c, 0, 255))
def px(x, y): return a[int(round(y)), int(round(x))]
def params(cx):
    base = np.mean([px(cx + s * 0.55 * R, y) for s in (-1, 1) for y in (30, 32, 34)], 0)
    glow = np.mean([px(cx + dx, y) for dx in (-1, 0, 1) for y in (49, 50, 51)], 0)
    t20 = px(cx, 20); op = 0.6
    top = base + (t20 - base) / op
    rim_t = px(cx, 17); rim_b = px(cx, 58)   # 1.5 px inside the lower edge: best-preserved rim row (inferred; outer edge is wand-cut)
    return dict(base=base, glow=glow, top=top, rim_t=rim_t, rim_b=rim_b)
def svg():
    defs, body = [], []
    for i, cx in enumerate(CXS):
        p = params(cx)
        defs.append(f'''<linearGradient id="tg{i}" gradientUnits="userSpaceOnUse" x1="0" y1="{CY-R:.2f}" x2="0" y2="{CY-0.5*R:.2f}"><stop offset="0" stop-color="{hx(p['top'])}"/><stop offset="1" stop-color="{hx(p['top'])}" stop-opacity="0"/></linearGradient>
<radialGradient id="gg{i}" gradientUnits="userSpaceOnUse" cx="{cx}" cy="{CY+0.55*R:.2f}" r="{0.9*R:.2f}" gradientTransform="translate(0 {CY+0.55*R:.2f}) scale(1 0.78) translate(0 {-(CY+0.55*R):.2f})"><stop offset="0" stop-color="{hx(p['glow'])}"/><stop offset="0.45" stop-color="{hx(p['glow'])}" stop-opacity="0.8"/><stop offset="1" stop-color="{hx(p['glow'])}" stop-opacity="0"/></radialGradient>
<linearGradient id="rg{i}" gradientUnits="userSpaceOnUse" x1="0" y1="{CY-R:.2f}" x2="0" y2="{CY+R:.2f}"><stop offset="0" stop-color="{hx(p['rim_t'])}"/><stop offset="0.2" stop-color="{hx(p['rim_t'])}" stop-opacity="0"/><stop offset="0.8" stop-color="{hx(p['rim_b'])}" stop-opacity="0"/><stop offset="1" stop-color="{hx(p['rim_b'])}"/></linearGradient>''')
        body.append(f'''<g><circle cx="{cx}" cy="{CY}" r="{R}" fill="{hx(p['base'])}"/><circle cx="{cx}" cy="{CY}" r="{R}" fill="url(#gg{i})"/><circle cx="{cx}" cy="{CY}" r="{R}" fill="url(#tg{i})"/><circle cx="{cx}" cy="{CY}" r="{R-0.9:.2f}" fill="none" stroke="url(#rg{i})" stroke-width="1.8"/></g>''')
    return '\n'.join(defs), '\n'.join(body)
