"""Find code that references a string (ASCII or UTF-16) in a PE and disassemble the containing functions.

Usage: python xref.py <pe> <string> [max_funcs]
"""
import re
import sys

import capstone
import pefile

path, needle = sys.argv[1], sys.argv[2]
max_funcs = int(sys.argv[3]) if len(sys.argv) > 3 else 4
pe = pefile.PE(path)
base = pe.OPTIONAL_HEADER.ImageBase
img = pe.get_memory_mapped_image()
iat = {imp.address: (imp.name or b"?").decode() for e in getattr(pe, "DIRECTORY_ENTRY_IMPORT", []) for imp in e.imports}

targets = set()
for enc in (needle.encode(), needle.encode("utf-16le")):
    for m in re.finditer(re.escape(enc), img):
        targets.add(base + m.start())
print("string at:", [hex(t) for t in sorted(targets)])

funcs = sorted((base + rf.struct.BeginAddress, base + rf.struct.EndAddress)
               for rf in getattr(pe, "DIRECTORY_ENTRY_EXCEPTION", []))
text = next(s for s in pe.sections if s.Name.startswith(b".text"))
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.skipdata = True

def rip_target(i):
    m = re.search(r"rip \+ (0x[0-9a-f]+)", i.op_str)
    return i.address + i.size + int(m.group(1), 16) if m else None

hits = []
for i in md.disasm(text.get_data(), base + text.VirtualAddress):
    t = rip_target(i)
    if t is not None and t in targets:
        hits.append(i.address)
print("refs:", [hex(h) for h in hits])

done = set()
for h in hits:
    f = next(((s, e) for s, e in funcs if s <= h < e), None)
    if not f or f in done or len(done) >= max_funcs:
        continue
    done.add(f)
    print(f"\n===== func {f[0]:X}-{f[1]:X} (ref at {h:X})")
    for i in md.disasm(img[f[0] - base:f[1] - base], f[0]):
        note = ""
        t = rip_target(i)
        if t is not None:
            if t in iat:
                note = f"   ; [{iat[t]}]"
            else:
                off = t - base
                s = img[off:off + 64]
                if s[:2].isascii() and s[1:2] == b"\x00":
                    note = "   ; L\"" + s.decode("utf-16le", "ignore").split("\x00")[0] + "\""
                elif s[:4].isascii() and all(32 <= c < 127 for c in s[:4]):
                    note = '   ; "' + s.split(b"\x00")[0].decode("ascii", "ignore") + '"'
        print(f"{i.address:X}: {i.mnemonic} {i.op_str}{note}")
