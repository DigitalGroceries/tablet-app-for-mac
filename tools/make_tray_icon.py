# Generates Assets/trayicon.png (18x18 px, @1x) and trayicon@2x.png (36x36 px): a black-on-transparent
# outline of the app-icon pen at 45 degrees (tip down-left, eraser up-right), 1.5 pt stroke, for use
# as a macOS template image (the system tints it for light/dark menu bars). Run:
#   python3 tools/make_tray_icon.py OpenTabletArtist/Assets
import math, sys
from PIL import Image, ImageDraw, ImageChops

SS = 16  # supersampling factor

def render(px, out):
    S = px * SS
    k = S / 18.0                         # supersampled pixels per pt
    stroke = 1.5 * k
    tip, end = (1.6, 16.4), (16.4, 1.6)  # axis in pt
    L = math.dist(tip, end)
    ux, uy = (end[0] - tip[0]) / L, (end[1] - tip[1]) / L
    nx, ny = -uy, ux
    def P(t, w): return ((tip[0] + ux * t + nx * w) * k, (tip[1] + uy * t + ny * w) * k)
    W = 2.6                              # barrel half-width (5.2 pt barrel)
    cone_end = 6.0
    R = W                                # eraser cap radius
    band = L - R - 1.6                   # ferrule line between barrel and eraser
    pen = [P(0, 0), P(cone_end, -W)]     # tip point, cone up to barrel, barrel, rounded eraser cap
    cap_c = L - R
    pen.append(P(cap_c, -W))
    for i in range(1, 30):
        a = -math.pi / 2 + math.pi * i / 30
        pen.append(P(cap_c + R * math.cos(a), R * math.sin(a)))
    pen += [P(cap_c, W), P(cone_end, W)]
    inside = Image.new("L", (S, S), 0); ImageDraw.Draw(inside).polygon(pen, fill=255)
    lines = Image.new("L", (S, S), 0); d = ImageDraw.Draw(lines)
    sw = int(round(stroke * 2))          # centred line twice the width, clipped to the inside = 1.5 pt inner stroke
    d.line(pen + [pen[0]], fill=255, width=sw, joint="curve")
    if px >= 36: d.line([P(cone_end, -W), P(cone_end, W)], fill=255, width=int(round(stroke)))   # cone / barrel joint (@2x only: at 18 px it fills the cone)
    d.line([P(band, -W), P(band, W)], fill=255, width=int(round(stroke)))           # eraser band
    m = ImageChops.multiply(lines, inside)
    mask = m.resize((px, px), Image.BOX)
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0)); img.putalpha(mask)
    img.save(out)

render(18, sys.argv[1] + "/trayicon.png")
render(36, sys.argv[1] + "/trayicon@2x.png")
