"""Writes a small SER v3 file for manual smoke tests of the managed engine."""
import struct
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "test.ser"
colour = len(sys.argv) > 2 and sys.argv[2].lower() in ("colour", "color")
w = h = 32
frames = 4
planes = 3 if colour else 1

with open(path, "wb") as f:
    f.write(b"LUCAM-RECORDER")
    f.write(struct.pack("<i", 0))          # LuID
    f.write(struct.pack("<i", 16 if colour else 0))   # ColorID: 16 = RGB, 0 = mono
    f.write(struct.pack("<i", 1))          # little endian
    f.write(struct.pack("<i", w))
    f.write(struct.pack("<i", h))
    f.write(struct.pack("<i", 8))          # 8 bits per plane
    f.write(struct.pack("<i", frames))
    f.write(b"\0" * 40)                    # observer
    f.write(b"\0" * 40)                    # telescope
    f.write(b"\0" * 40)                    # instrument
    f.write(struct.pack("<q", 0))          # local time
    f.write(struct.pack("<q", 0))          # utc
    assert f.tell() == 170 + 8, f.tell()   # 178-byte header

    for n in range(frames):
        for y in range(h):
            for x in range(w):
                dx, dy = x - (w // 2 + n), y - h // 2
                inside = dx * dx + dy * dy <= 16
                if colour:
                    f.write(bytes((255 if inside else 40, 255 if inside else 20, 255 if inside else 60)))
                else:
                    f.write(bytes((255 if inside else 40,)))

print(f"wrote {path}: {w}x{h}, {frames} frames, {'colour' if colour else 'mono'} 8-bit")
