"""Static look at an Acer lighting controller DLL: imports, exports, strings, and the
code around every call into hidapi, so the HID packet layout can be read off.

Usage: python re_dll.py <dll> [context_instructions]
"""
import re
import sys

import capstone
import pefile

path = sys.argv[1]
ctx = int(sys.argv[2]) if len(sys.argv) > 2 else 60
pe = pefile.PE(path)
base = pe.OPTIONAL_HEADER.ImageBase

iat = {}
print("== imports")
for entry in pe.DIRECTORY_ENTRY_IMPORT:
    dll = entry.dll.decode()
    names = []
    for imp in entry.imports:
        name = imp.name.decode() if imp.name else f"ord{imp.ordinal}"
        iat[imp.address] = f"{dll}!{name}"
        names.append(name)
    if not dll.lower().startswith(("api-ms", "vcruntime", "msvcp", "ucrtbase")):
        print(f"  {dll}: {', '.join(names)}")

if hasattr(pe, "DIRECTORY_ENTRY_EXPORT"):
    print("== exports")
    for e in pe.DIRECTORY_ENTRY_EXPORT.symbols:
        print(f"  0x{base + e.address:X} {e.name.decode() if e.name else e.ordinal}")

data = open(path, "rb").read()
print("== interesting strings")
for m in re.finditer(rb"[\x20-\x7e]{5,}", data):
    s = m.group().decode()
    if not re.search(r"[?@$]{2}|^[A-Z]{1,3}[a-z]?$", s):
        print("  a:", s)
for m in re.finditer(rb"(?:[\x20-\x7e]\x00){5,}", data):
    print("  w:", m.group().decode("utf-16le"))

text = next(s for s in pe.sections if s.Name.startswith(b".text"))
code = text.get_data()
code_va = base + text.VirtualAddress
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
insns = list(md.disasm(code, code_va))

def label(i):
    # Resolve `call qword ptr [rip + X]` to the import it hits.
    if i.mnemonic == "call" and "rip" in i.op_str:
        m = re.search(r"rip \+ (0x[0-9a-f]+)", i.op_str)
        if m:
            tgt = i.address + i.size + int(m.group(1), 16)
            return iat.get(tgt)
    return None

print("== hid call sites")
for idx, i in enumerate(insns):
    name = label(i)
    if name and ("hid" in name.lower()):
        print(f"\n---- 0x{i.address:X} -> {name}")
        for j in insns[max(0, idx - ctx): idx + 3]:
            extra = label(j)
            print(f"  0x{j.address:X}: {j.mnemonic} {j.op_str}" + (f"   ; {extra}" if extra else ""))
