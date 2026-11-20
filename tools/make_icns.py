# Packs OpenTabletArtist/Assets/macos/AppIcon.iconset into AppIcon.icns the way Apple's own
# `iconutil` does, so it can be rebuilt on Linux. Run:
#   python3 tools/make_icns.py OpenTabletArtist/Assets/macos/AppIcon.iconset OpenTabletArtist/Assets/macos/AppIcon.icns
#
# Why (task #157, 20 Nov 2026): the old file kept the 16 px and 32 px pictures as PNG in the
# icp4/icp5/icp6 slots. macOS does not decode PNG from those slots, so Finder's list view, the
# sidebar and Force Quit showed coloured noise while the big preview looked fine. Apple's
# iconutil stores those two sizes as ic04/ic05 "ARGB": the word ARGB, then the alpha, red, green
# and blue planes, each squeezed with the icns run-length scheme, colours not premultiplied.
# Same layout as electron-builder-binaries packages/icons 1.2.3 (argb.ts), which its authors
# checked byte for byte against iconutil. Every larger size stays PNG (ic07-ic14).
import struct, sys, os
from PIL import Image

ENTRIES = [  # (slot, iconset file, encoding) - the order iconutil writes
    ("ic04", "icon_16x16.png", "argb"),
    ("ic05", "icon_32x32.png", "argb"),
    ("ic07", "icon_128x128.png", "png"),
    ("ic08", "icon_256x256.png", "png"),
    ("ic09", "icon_512x512.png", "png"),
    ("ic10", "icon_512x512@2x.png", "png"),
    ("ic11", "icon_16x16@2x.png", "png"),
    ("ic12", "icon_32x32@2x.png", "png"),
    ("ic13", "icon_128x128@2x.png", "png"),
    ("ic14", "icon_256x256@2x.png", "png"),
]
SIZES = {"ic04": 16, "ic05": 32, "ic07": 128, "ic08": 256, "ic09": 512, "ic10": 1024,
         "ic11": 32, "ic12": 64, "ic13": 256, "ic14": 512}


def pack_bits(data):
    """icns run-length: control >= 0x80 = (control - 125) copies (3..130) of the next byte;
    control < 0x80 = (control + 1) literal bytes (1..128)."""
    out = bytearray()
    n, i = len(data), 0
    while i < n:
        run = 1
        while i + run < n and data[i + run] == data[i] and run < 130:
            run += 1
        if run >= 3:
            out += bytes((0x80 | (run - 3), data[i]))
            i += run
            continue
        literal = bytearray()
        while i < n and len(literal) < 128:
            ahead = 1
            while i + ahead < n and data[i + ahead] == data[i] and ahead < 3:
                ahead += 1
            if ahead >= 3:
                break
            literal.append(data[i])
            i += 1
        out.append(len(literal) - 1)
        out += literal
    return bytes(out)


def unpack_bits(data, count, pos=0):
    """Reverse of pack_bits; returns (bytes, next position). Used to check our own output."""
    out = bytearray()
    while len(out) < count:
        c = data[pos]
        pos += 1
        if c >= 0x80:
            out += bytes([data[pos]]) * (c - 125)
            pos += 1
        else:
            out += data[pos:pos + c + 1]
            pos += c + 1
    if len(out) != count:
        raise ValueError("run crosses a plane boundary")
    return bytes(out), pos


def encode_argb(img):
    r, g, b, a = img.convert("RGBA").split()
    return b"ARGB" + b"".join(pack_bits(p.tobytes()) for p in (a, r, g, b))


def decode_argb(payload, size):
    if payload[:4] != b"ARGB":
        raise ValueError("no ARGB magic")
    planes, pos = [], 4
    for _ in range(4):
        plane, pos = unpack_bits(payload, size * size, pos)
        planes.append(plane)
    if pos != len(payload):
        raise ValueError("trailing bytes after the four planes")
    a, r, g, b = (Image.frombytes("L", (size, size), p) for p in planes)
    return Image.merge("RGBA", (r, g, b, a))


def build(iconset, out_path):
    chunks = []
    for slot, name, enc in ENTRIES:
        path = os.path.join(iconset, name)
        img = Image.open(path)
        if img.size != (SIZES[slot], SIZES[slot]):
            raise SystemExit(f"{name} is {img.size}, {slot} needs {SIZES[slot]} px")
        if enc == "argb":
            payload = encode_argb(img)
            back = decode_argb(payload, SIZES[slot])
            if back.tobytes() != img.convert("RGBA").tobytes():
                raise SystemExit(f"{slot}: ARGB round trip does not match {name}")
        else:
            payload = open(path, "rb").read()
            if payload[:8] != b"\x89PNG\r\n\x1a\n":
                raise SystemExit(f"{name} is not a PNG")
        chunks.append(slot.encode() + struct.pack(">I", 8 + len(payload)) + payload)
    body = b"".join(chunks)
    with open(out_path, "wb") as f:
        f.write(b"icns" + struct.pack(">I", 8 + len(body)) + body)
    print(f"wrote {out_path} ({8 + len(body)} bytes): " + " ".join(e[0] for e in ENTRIES))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__ or "usage: make_icns.py <iconset dir> <out.icns>")
    build(sys.argv[1], sys.argv[2])
