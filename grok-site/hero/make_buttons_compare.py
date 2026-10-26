"""400% side-by-side of Zak's traffic lights (top) vs rebuilt (bottom), plus colour-sample table."""
from PIL import Image, ImageDraw
import numpy as np, lights
o = Image.open('App-original.png').convert('RGBA').crop((10, 8, 220, 68))
n4 = Image.open('hero@4x.png').convert('RGBA').crop((40, 32, 880, 272))
def checker(w, h, s=16):
    im = Image.new('RGBA', (w, h), '#2A2A2A'); d = ImageDraw.Draw(im)
    for y in range(0, h, s):
        for x in range(0, w, s):
            if (x // s + y // s) % 2: d.rectangle([x, y, x + s - 1, y + s - 1], fill='#3A3A3A')
    return im
W, H = 840, 240
out = Image.new('RGBA', (W, 2 * H + 90), '#141414'); d = ImageDraw.Draw(out)
for i, (im, lab) in enumerate([(o.resize((W, H), Image.NEAREST), "Zak's screenshot (App-original.png), 400% nearest-neighbour"), (n4, 'Rebuilt hero.svg, native 4x render')]):
    y = 30 + i * (H + 30); bg = checker(W, H); bg.alpha_composite(im); out.paste(bg, (0, y)); d.text((6, y - 20), lab, fill='#E4E4E4')
out.convert('RGB').save('buttons-comparison-400.png')
oa = np.asarray(Image.open('App-original.png').convert('RGB')).astype(int)
na = np.asarray(Image.open('hero@1x.png').convert('RGB')).astype(int)
pts = {'centre': (0, 0), 'mid (0.5 r below centre)': (0, 0.5), 'rim, top (1.5 px inside)': (0, -(lights.R - 1.5) / lights.R), 'rim, bottom (1.5 px inside)': (0, (lights.R - 1.5) / lights.R), 'highlight, top gloss (y = 20)': None, 'highlight, bottom glow peak (y = 50)': (0, (50 - lights.CY) / lights.R)}
rows = ['| Button | Sample | His | New | Max channel diff |', '|---|---|---|---|---|']
for name, cx in zip(('Close (red)', 'Minimise (yellow)', 'Zoom (green)'), lights.CXS):
    for k, v in pts.items():
        x, y = (cx, 20) if v is None else (cx + v[0] * lights.R, lights.CY + v[1] * lights.R)
        x, y = int(round(x)), int(round(y)); a, b = oa[y, x], na[y, x]
        rows.append(f'| {name} | {k} | #{a[0]:02X}{a[1]:02X}{a[2]:02X} | #{b[0]:02X}{b[1]:02X}{b[2]:02X} | {int(abs(a - b).max())} |')
open('buttons-colour-samples.md', 'w').write("# Traffic-light colour samples: Zak's screenshot vs rebuild (1x, pixel at listed point)\n\n" + '\n'.join(rows) + '\n')
print('ok')
