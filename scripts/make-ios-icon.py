"""Draws the iOS app icon (1024x1024, opaque, square: iOS rounds the corners itself).

The same design as the Android icon (src/Reyfen.Timbratune.Android/Resources/drawable/icon.png,
192 px with its own rounded corners): a blue-to-pink gradient and five rounded bars. Redrawn at
full size rather than scaled up, so it stays sharp. Standard library only.

    python scripts/make-ios-icon.py
"""
import os
import struct
import zlib

SIZE = 1024
SCALE = SIZE / 192  # the Android icon's coordinates
TOP, BOTTOM = (214, 236, 255), (255, 214, 234)
BLUE, PINK, WHITE = (120, 195, 245), (255, 160, 195), (255, 250, 252)
# Bars in the Android icon's pixels: centre x, half width (cap radius), top and bottom.
BARS = [
    (47.0, 8.5, 84, 108, BLUE),
    (71.5, 9.0, 75, 117, PINK),
    (96.0, 8.5, 65, 127, WHITE),
    (120.5, 9.0, 75, 117, PINK),
    (145.0, 8.5, 84, 108, BLUE),
]

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Reyfen.Timbratune.iOS',
                   'Assets.xcassets', 'AppIcon.appiconset', 'icon-1024.png')


def coverage(x, y, cx, r, top, bottom):
    """How much of the pixel centred at (x, y) a vertical capsule covers (signed distance, 1 px edge)."""
    ny = min(max(y, top + r), bottom - r)
    d = ((x - cx) ** 2 + (y - ny) ** 2) ** 0.5 - r
    return min(max(0.5 - d, 0.0), 1.0)


def main():
    bars = [(cx * SCALE, hw * SCALE, t * SCALE, b * SCALE, c) for cx, hw, t, b, c in BARS]
    rows = []
    for y in range(SIZE):
        t = y / (SIZE - 1)
        bg = [TOP[i] + (BOTTOM[i] - TOP[i]) * t for i in range(3)]
        row = bytearray(b'\x00')  # filter: none
        active = [bar for bar in bars if bar[2] - 1 <= y + 0.5 <= bar[3] + 1]
        for x in range(SIZE):
            px = bg
            for cx, r, top, bottom, colour in active:
                if abs(x + 0.5 - cx) > r + 1:
                    continue
                a = coverage(x + 0.5, y + 0.5, cx, r, top, bottom)
                if a > 0:
                    px = [px[i] + (colour[i] - px[i]) * a for i in range(3)]
            row += bytes(int(round(v)) for v in px)
        rows.append(bytes(row))

    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)

    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(b''.join(rows), 9)) + chunk(b'IEND', b'')
    with open(OUT, 'wb') as f:
        f.write(png)
    print(os.path.normpath(OUT))


if __name__ == '__main__':
    main()
