"""Linear disassembly of a VA range in a PE, with import names resolved.

Usage: python dis_range.py <dll> <start_va_hex> <end_va_hex>
"""
import re
import sys

import capstone
import pefile

path, start, end = sys.argv[1], int(sys.argv[2], 16), int(sys.argv[3], 16)
pe = pefile.PE(path)
base = pe.OPTIONAL_HEADER.ImageBase
iat = {imp.address: (imp.name or b"?").decode() for e in pe.DIRECTORY_ENTRY_IMPORT for imp in e.imports}

# Snap start back to the containing function using .pdata so decoding stays aligned.
for rf in getattr(pe, "DIRECTORY_ENTRY_EXCEPTION", []):
    fs, fe = base + rf.struct.BeginAddress, base + rf.struct.EndAddress
    if fs <= start < fe:
        start = fs
        break

img = pe.get_memory_mapped_image()
code = img[start - base:end - base]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
funcs = {base + rf.struct.BeginAddress for rf in getattr(pe, "DIRECTORY_ENTRY_EXCEPTION", [])}
for i in md.disasm(code, start):
    if i.address in funcs:
        print(f"\n===== func {i.address:X}")
    note = ""
    m = re.search(r"rip \+ (0x[0-9a-f]+)", i.op_str)
    if m:
        tgt = i.address + i.size + int(m.group(1), 16)
        note = f"   ; [{iat.get(tgt, hex(tgt))}]"
    if i.mnemonic == "int3":
        continue
    print(f"{i.address:X}: {i.mnemonic} {i.op_str}{note}")
