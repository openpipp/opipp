"""Writes a SER whose single frame is a 0..255 ramp, for measuring tone mapping."""
import struct
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "ramp.ser"
w, h = 256, 32
frames = 4
with open(path, "wb") as f:
    f.write(b"LUCAM-RECORDER")
    for v in (0, 0, 1, w, h, 8, frames):  # LuID, ColorID(mono), LE, w, h, depth, frames
        f.write(struct.pack("<i", v))
    f.write(b"\0" * 120)
    f.write(struct.pack("<q", 0))
    f.write(struct.pack("<q", 0))
    # A ramp in the top rows for measuring the tone map, plus a bright disc lower down so the
    # engine's object detection has something to find (it silently discards object-less frames).
    for _frame in range(frames):
      for y in range(h):
        row = bytearray(range(256))
        # object well inside the frame (PIPP discards frames with a partially visible object)
        if 4 <= y < h - 4:
            for x in range(100, 156):
                dx, dy = x - w // 2, y - h // 2
                if dx * dx + dy * dy <= 196:
                    row[x] = 255
        f.write(bytes(row))
print(f"wrote {path}: {w}x{h} ramp 0..255")
