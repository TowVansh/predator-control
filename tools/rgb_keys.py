"""Light individual key slots. Usage: python rgb_keys.py 5=00ff00 21=ff0000 ...
Slot i lives at buffer offset i*4 as [0, R, G, B]; see KeyMap in the app for names.
"""
import sys

from rgb_test import open_dev, send_frame

buf = bytearray(512)
for arg in sys.argv[1:]:
    slot, color = arg.split("=")
    buf[int(slot) * 4 + 1:int(slot) * 4 + 4] = bytes.fromhex(color)
dev = open_dev()
try:
    send_frame(dev, bytes(buf), 50)
finally:
    dev.close()
print("sent")
