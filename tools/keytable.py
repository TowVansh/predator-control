"""Find the key-name table in AcerUSBController.dll and what references it.

Prints each "Key: ..." string with its VA, then scans .data/.rdata for arrays of
pointers to those strings (the zone/key tables) and code that loads them.
"""
import re
import struct
import sys

import capstone
import pefile

pe = pefile.PE(sys.argv[1])
base = pe.OPTIONAL_HEADER.ImageBase
img = pe.get_memory_mapped_image()

names = {}
for m in re.finditer(rb"Key: [\x20-\x7e]+?\x00", img):
    names[base + m.start()] = m.group()[:-1].decode()
print(f"{len(names)} key strings")

# pointer arrays in data
ptr_hits = []
for off in range(0, len(img) - 8, 8):
    v = struct.unpack_from("<Q", img, off)[0]
    if v in names:
        ptr_hits.append((base + off, v))
print(f"{len(ptr_hits)} data pointers to key strings")
for va, v in ptr_hits[:5]:
    print(f"  {va:X} -> {names[v]}")

# code references (lea reg, [rip+X])
text = next(s for s in pe.sections if s.Name.startswith(b".text"))
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.skipdata = True
refs = []
for i in md.disasm(text.get_data(), base + text.VirtualAddress):
    m = re.search(r"rip \+ (0x[0-9a-f]+)", i.op_str)
    if m:
        tgt = i.address + i.size + int(m.group(1), 16)
        if tgt in names:
            refs.append((i.address, names[tgt]))
print(f"{len(refs)} code refs; first {refs[0][0]:X} last {refs[-1][0]:X}" if refs else "no code refs")
for a, n in refs:
    print(f"  {a:X} {n}")
