"""Render debug OBJs (front + side views): mesh as points, bones as red dots."""
import sys, os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

def load(p):
    if not os.path.exists(p):
        return np.zeros((0, 3))
    return np.array([[float(x) for x in l.split()[1:4]] for l in open(p) if l.startswith("v ")])

out = sys.argv[1]
pairs = [(a, b) for a, b in zip(sys.argv[2::2], sys.argv[3::2])]
fig, axes = plt.subplots(len(pairs), 2, figsize=(8, 4.2 * len(pairs)), squeeze=False)
for row, (mesh, bones) in enumerate(pairs):
    v, b = load(mesh), load(bones)
    for col, (i, j, lab) in enumerate([(0, 1, "front (x,y)"), (2, 1, "side (z,y)")]):
        ax = axes[row][col]
        if len(v):
            ax.scatter(v[:, i], v[:, j], s=0.3, c="tab:blue", alpha=0.4)
        if len(b):
            ax.scatter(b[:, i], b[:, j], s=6, c="red")
        ax.set_aspect("equal")
        ax.set_title(f"{os.path.basename(mesh)[:-4]} {lab}", fontsize=8)
plt.tight_layout()
plt.savefig(out, dpi=80)
print(out)
