"""Writes docs/assets/support/qr-*.svg from support.json (local; needs OpenCV in /tmp/qrvenv) and checks each decodes back."""
import json, cv2, numpy as np, os
cfg = json.load(open(os.path.join(os.path.dirname(__file__), '..', 'support.json')))
out = os.path.join(os.path.dirname(__file__), '..', 'docs', 'assets', 'support')
enc = cv2.QRCodeEncoder.create(); det = cv2.QRCodeDetector()
for key, data in [('paypal', cfg['paypal']), ('bitcoin', 'bitcoin:' + cfg['bitcoin']), ('monero', 'monero:' + cfg['monero'])]:
    img = enc.encode(data)                      # 1 px per module, includes quiet zone
    n = img.shape[0]
    assert det.detectAndDecode(cv2.resize(img, (n * 8, n * 8), interpolation=cv2.INTER_NEAREST))[0] == data, key
    rects = ''.join(f'M{x},{y}h1v1h-1z' for y in range(n) for x in range(n) if img[y, x] < 128)
    open(f'{out}/qr-{key}.svg', 'w').write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {n} {n}" shape-rendering="crispEdges"><rect width="{n}" height="{n}" fill="#E4E4E4"/><path d="{rects}" fill="#161616"/></svg>')
    print(key, n, 'ok')
