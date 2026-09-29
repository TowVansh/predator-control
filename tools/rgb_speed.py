"""Throughput test: stream a rainbow wave for a few seconds with no inter-packet sleeps."""
import colorsys
import time

from rgb_test import feature, open_dev

dev = open_dev()
frames, t0 = 0, time.time()
try:
    while time.time() - t0 < 6:
        t = time.time() - t0
        buf = bytearray(512)
        for slot in range(128):
            r, g, b = colorsys.hsv_to_rgb(((slot // 6) / 22 + t * 0.5) % 1, 1, 1)
            buf[slot * 4 + 1:slot * 4 + 4] = bytes((int(r * 255), int(g * 255), int(b * 255)))
        feature(dev, [0x12, 0, 0, 0x08, 0, 0, 0])
        for i in range(8):
            dev.write(b"\x00" + bytes(buf[i * 64:(i + 1) * 64]))
        feature(dev, [0x08, 0x02, 0x33, 0x05, 50, 0x08, 0x01])
        frames += 1
finally:
    dev.close()
print(f"{frames} frames in 6s = {frames / 6:.1f} fps")
