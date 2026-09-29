"""Send a per-key frame to the Acer PH16-71 keyboard (USB 04F2:0117, usage page FF02).

Protocol (from AcerUSBController.dll @ 0x180001950):
  feature  12 00 00 08 00 00 00 chk      -> "8 data packets follow"
  write    8 x 64 bytes of key data      (512 bytes total)
  feature  08 02 33 05 <bright> 08 01 chk -> apply per-key mode
  chk = 0xFF - (sum of the 7 bytes before it) & 0xFF

Usage: python rgb_test.py <pattern> [brightness 0-50]
  pattern: white | off | hex bytes repeated across the 512-byte buffer, e.g. 00ff0000
"""
import sys
import time

import hid

VID, PID, PAGE = 0x04F2, 0x0117, 0xFF02


def chk(body):
    return (0xFF - sum(body)) & 0xFF


def feature(dev, body):
    body = bytes(body)
    dev.send_feature_report(b"\x00" + body + bytes([chk(body)]))


def open_dev():
    for d in hid.enumerate(VID, PID):
        if d["usage_page"] == PAGE:
            dev = hid.device()
            dev.open_path(d["path"])
            return dev
    raise SystemExit("keyboard lighting interface not found")


def send_frame(dev, buf, bright):
    assert len(buf) == 512
    feature(dev, [0x12, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00])
    time.sleep(0.02)
    for i in range(8):
        dev.write(b"\x00" + buf[i * 64:(i + 1) * 64])
        time.sleep(0.002)
    time.sleep(0.02)
    feature(dev, [0x08, 0x02, 0x33, 0x05, bright, 0x08, 0x01])


if __name__ == "__main__":
    pat = sys.argv[1] if len(sys.argv) > 1 else "white"
    bright = int(sys.argv[2]) if len(sys.argv) > 2 else 50
    if pat == "white":
        unit = b"\xff"
    elif pat == "off":
        unit = b"\x00"
    else:
        unit = bytes.fromhex(pat)
    buf = (unit * (512 // len(unit) + 1))[:512]
    dev = open_dev()
    try:
        send_frame(dev, buf, bright)
        print("sent", pat, "brightness", bright)
    finally:
        dev.close()
