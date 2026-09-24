"""Generate the project's original harmonica icon; no external artwork."""
from pathlib import Path
import math
import struct
import zlib

size = 64
pixels = bytearray()
for y in range(size):
    pixels.append(0)
    for x in range(size):
        dx, dy = max(13 - x, 0, x - 50), max(13 - y, 0, y - 50)
        inside = dx * dx + dy * dy <= 13 * 13
        color = (47, 118, 88, 255) if inside else (0, 0, 0, 0)
        if inside and 12 <= x <= 51 and 22 <= y <= 42:
            color = (229, 239, 219, 255)
            if 26 <= y <= 38 and any(left <= x <= left + 3 for left in (17, 24, 31, 38, 45)):
                color = (47, 118, 88, 255)
        pixels.extend(color)


def chunk(name, data):
    return struct.pack('>I', len(data)) + name + data + struct.pack('>I', zlib.crc32(name + data))


png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(bytes(pixels))) + chunk(b'IEND', b'')
icon = struct.pack('<HHH', 0, 1, 1) + struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(png), 22) + png
target = Path(__file__).resolve().parents[1] / 'src/Harmonica.Desktop/Assets/App.ico'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(icon)
print('Generated original App.ico')
