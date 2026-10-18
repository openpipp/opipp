"""Writes a working-geometry SER (32x24, 4 frames) covering every value 0..255 in a gradient."""
import struct
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "grad.ser"
w, h, frames = 32, 24, 4
with open(path, "wb") as f:
    f.write(b"LUCAM-RECORDER")
    for v in (0, 0, 1, w, h, 8, frames):
        f.write(struct.pack("<i", v))
    f.write(b"\0" * 120)
    f.write(struct.pack("<q", 0))
    f.write(struct.pack("<q", 0))
    for _ in range(frames):
        for y in range(h):
            row = bytearray()
            for x in range(w):
                value = (y * w + x) % 256
                dx, dy = x - w // 2, y - h // 2
                if dx * dx + dy * dy <= 16:   # the small disc the engine accepts
                    value = 255
                row.append(value)
            f.write(bytes(row))
print(f"wrote {path}: {w}x{h} gradient x{frames}")
