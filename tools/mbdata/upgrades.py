# usage: upgrades.py <bin dir>   (UpgradeData bytes from mbread RAW=1 BIN=...): each weapon's possible level-up stat modifiers.
from mbraw import *
STAT = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'estat.txt')).read().split()
for p in sorted(glob.glob(os.path.join(sys.argv[1], 'UpgradeData__*.bin'))):
    b = open(p, 'rb').read(); r = R(b); name = header(r)
    n = r.i(); mods = []
    for _ in range(n):
        st = r.i(); ty = r.i(); v = r.f()
        mods.append(f"{STAT[st] if st < len(STAT) else st}{'+' if ty == 0 else 'x' if ty == 1 else '='}{v}")
    print(f"{name:22} {' '.join(mods)}")
