#!/usr/bin/env python3
"""Writes the PiPL resource file (.rsrc) that Affinity reads to find a Photoshop-style filter plugin.

Same bytes the Rez compiler makes from a PiPL .r file (checked against gmic-affinity's GmicFilter.rsrc
in tests). No Mac tools needed. Usage: make_rsrc.py OUT.rsrc
"""
import struct
import sys


def pstr(s):
    b = s.encode("mac_roman")
    return bytes([len(b)]) + b


def prop(key, data, length=None):
    length = len(data) if length is None else length
    pad = (4 - len(data) % 4) % 4
    return b"8BIM" + key + struct.pack(">ii", 0, length) + data + b"\0" * pad


def pipl(props):
    return struct.pack(">ii", 0, len(props)) + b"".join(props)


def resource_file(rtype, rid, name, attrs, data):
    data_off = 0x100
    body = struct.pack(">I", len(data)) + data
    map_off = data_off + len(body)
    name_b = pstr(name)
    type_list = struct.pack(">H", 0) + rtype + struct.pack(">HH", 0, 10)
    # The last 4 bytes are a reserved handle; Rez leaves 01 00 00 00 there, so we do too.
    ref = struct.pack(">hH", rid, 0) + bytes([attrs]) + b"\0\0\0" + b"\x01\0\0\0"
    type_off = 28
    name_off = type_off + len(type_list) + len(ref)
    map_len = name_off + len(name_b)
    header = struct.pack(">IIII", data_off, map_off, len(body), map_len)
    rmap = header + b"\0\0\0\0" + b"\x0a\x00" + b"\0\0" + struct.pack(">HH", type_off, name_off) + type_list + ref + name_b
    return header + b"\0" * (data_off - 16) + body + rmap


def smooth_strokes_pipl():
    return pipl([
        prop(b"kind", b"8BFM"),
        prop(b"name", pstr("Smooth pen strokes")),
        prop(b"catg", pstr("Tablet App for Mac")),
        prop(b"vers", struct.pack(">I", 0x00040000)),
        prop(b"mi64", pstr("PluginMain")),
        prop(b"ma64", pstr("PluginMain")),
        # 8-bit RGB and Grey, with the same enable expression and size limit as gmic-affinity, which Affinity
        # is known to accept. A plain "true" enable expression left the menu item greyed out in Affinity 3.3
        # (Zak's test, 11 Nov 2026).
        prop(b"mode", struct.pack(">H", 0x5000)),
        prop(b"enbl", b"in (PSHOP_ImageMode, RGBMode, GrayScaleMode)\0"),
        prop(b"ms32", struct.pack(">ii", 2000000, 2000000)),
        prop(b"pmsa", struct.pack(">I", 1)),
    ])


if __name__ == "__main__":
    out = sys.argv[1]
    with open(out, "wb") as f:
        f.write(resource_file(b"PiPL", 16000, "Smooth pen strokes PiPL", 0x20, smooth_strokes_pipl()))
