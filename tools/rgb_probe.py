"""List the HID interfaces of the Acer per-key keyboard controller (VID 04F2, PID 0117).

Read-only: only enumerates interfaces and prints their usage pages / report info.
"""
import hid

VID, PID = 0x04F2, 0x0117

for d in hid.enumerate(VID, PID):
    print(
        f"iface={d['interface_number']} usage_page=0x{d['usage_page']:04X} usage=0x{d['usage']:04X} "
        f"product={d['product_string']!r} path={d['path'].decode(errors='replace')}"
    )
