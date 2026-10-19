#!/usr/bin/env python3
"""Draws the PC Monitor logo: assets/logo.png (512 px) and assets/app.ico.

Dev tool only; the outputs are committed and CI never runs this.
Needs Python 3 and Pillow (pip install pillow). Run from anywhere: python tools/make-icon.py
Every size is drawn from scratch (supersampled, box-filtered) so small sizes get
thicker strokes; the pulse line is dropped below 32 px where it turns to mush.
"""
import io
import math
import os
import struct

from PIL import Image, ImageDraw

# Colours from src/PcMonitor.App/Theme.xaml and ViewModels/Palette.cs.
CARD = (0x18, 0x1B, 0x22, 255)     # CardBrush
BORDER = (0x26, 0x2B, 0x36, 255)   # CardBorderBrush
GREEN = (0x3D, 0xDC, 0x84, 255)    # Palette.Ok
BLUE = (0x4E, 0xA1, 0xFF, 255)     # AccentBrush

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 16  # supersampling factor

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ASSETS = os.path.join(ROOT, "assets")


def draw(size):
    n = size * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def px(v):
        return v * n

    small = size <= 24
    # Background: rounded square, thin border at large sizes only.
    margin = 0.0 if small else 0.03
    radius = 0.20 if small else 0.22
    box = [px(margin), px(margin), px(1 - margin) - 1, px(1 - margin) - 1]
    d.rounded_rectangle(box, radius=px(radius), fill=CARD,
                        outline=None if size < 64 else BORDER,
                        width=0 if size < 64 else max(1, int(px(0.012))))

    # Gauge ring: 270 degrees, gap centred at the bottom, round caps.
    if size <= 16:
        r, w = 0.32, 0.17
    elif size <= 24:
        r, w = 0.32, 0.15
    elif size <= 48:
        r, w = 0.31, 0.12
    else:
        r, w = 0.31, 0.10
    c = 0.5
    outer = [px(c - r - w / 2), px(c - r - w / 2), px(c + r + w / 2), px(c + r + w / 2)]
    # PIL angles: degrees clockwise from 3 o'clock; bottom = 90.
    start, end = 135, 405
    d.arc(outer, start, end, fill=GREEN, width=int(round(px(w))))

    for a in (start, end):
        x = c + r * math.cos(math.radians(a))
        y = c + r * math.sin(math.radians(a))
        d.ellipse([px(x - w / 2), px(y - w / 2), px(x + w / 2), px(y + w / 2)], fill=GREEN)

    # Pulse line across the centre.
    if size >= 32:
        lw = 0.075 if size <= 48 else 0.055
        pts = [(0.27, 0.53), (0.40, 0.53), (0.45, 0.38), (0.52, 0.66),
               (0.57, 0.46), (0.61, 0.53), (0.73, 0.53)]
        pts = [(px(x), px(y)) for x, y in pts]
        width = int(round(px(lw)))
        d.line(pts, fill=BLUE, width=width, joint="curve")
        for x, y in (pts[0], pts[-1]):
            h = width / 2
            d.ellipse([x - h, y - h, x + h, y + h], fill=BLUE)

    return img.resize((size, size), Image.BOX)


def bmp_entry(img):
    """32-bit BGRA DIB (bottom-up) plus an all-zero AND mask, as ICO frames expect."""
    w, h = img.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            r, g, b, a = img.getpixel((x, y))
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    mask_stride = ((w + 31) // 32) * 4
    mask = b"\x00" * (mask_stride * h)
    return header + b"".join(rows) + mask


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path, images):
    entries = []
    for img in images:
        data = png_bytes(img) if img.size[0] >= 256 else bmp_entry(img)
        entries.append((img.size[0], data))
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for size, data in entries:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    with open(path, "wb") as f:
        f.write(out)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    with open(os.path.join(ASSETS, "logo.png"), "wb") as f:
        f.write(png_bytes(draw(512)))
    write_ico(os.path.join(ASSETS, "app.ico"), [draw(s) for s in ICO_SIZES])
    print("wrote assets/logo.png and assets/app.ico")


if __name__ == "__main__":
    main()
