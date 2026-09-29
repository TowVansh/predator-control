"""Burn logical CPUs for N seconds (default 90) to exercise the Smart governor.

Usage: python stress.py [seconds] [processes, default = all logical CPUs]
"""
import multiprocessing as mp
import sys
import time


def burn(until):
    x = 0
    while time.time() < until:
        x = (x * 1103515245 + 12345) & 0xFFFFFFFF


if __name__ == "__main__":
    secs = float(sys.argv[1]) if len(sys.argv) > 1 else 90
    until = time.time() + secs
    n = int(sys.argv[2]) if len(sys.argv) > 2 else mp.cpu_count()
    procs = [mp.Process(target=burn, args=(until,)) for _ in range(n)]
    for p in procs:
        p.start()
    for p in procs:
        p.join()
