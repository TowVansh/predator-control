"""Dump the ordered key-name pointer table from AcerUSBController.dll and the data after it."""
import re
import struct
import sys

import pefile

pe = pefile.PE(sys.argv[1])
base = pe.OPTIONAL_HEADER.ImageBase
img = pe.get_memory_mapped_image()

def cstr(va):
    off = va - base
    end = img.index(b"\x00", off)
    return img[off:end].decode(errors="replace")

start = int(sys.argv[2], 16) if len(sys.argv) > 2 else 0x180012CA0
off = start - base
# walk back to the table start
while True:
    v = struct.unpack_from("<Q", img, off - 8)[0]
    if base <= v < base + len(img) and cstr(v).startswith("Key: "):
        off -= 8
    else:
        break
i = 0
while True:
    v = struct.unpack_from("<Q", img, off + i * 8)[0]
    if not (base <= v < base + len(img)):
        break
    s = cstr(v)
    if s and not all(32 <= ord(c) < 127 for c in s):
        break
    print(f"{i:3d} {s.removeprefix('Key: ') or '<empty>'}")
    i += 1
tail = off + i * 8
print(f"table @ {base + off:X}, {i} entries; next 256 bytes @ {base + tail:X}:")
blob = img[tail:tail + 256]
for r in range(0, len(blob), 32):
    print("  " + blob[r:r + 32].hex(" "))
