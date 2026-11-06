# 사용: python audit_set.py HeroHQ4 몸_05_전설 -> 칼 축 각도(원본 마젠타 PCA) vs 표 각도, 15도 넘게 다르면 출력 + 전체 대조표
import sys, os, numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.argv += []
RES = r"C:\Users\dignw\OneDrive\Documents\GitHub\LoopRogue\Assets\Resources\Sprites"
DESK = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트"
s, folder = sys.argv[1], sys.argv[2]
K = {"idle": "대기", "walk": "걷기", "attack": "공격", "dash": "대쉬"}; D = {"south": "앞", "west": "옆", "north": "뒤"}
anch = {}
for l in open(os.path.join(RES, s, "weapon_anchor.txt"), encoding="utf-8"):
    p = l.split()
    if len(p) >= 5: anch[p[0]] = (float(p[1]), float(p[2]), int(p[3]), p[4] == "1")
font = ImageFont.truetype(r"C:\Windows\Fonts\malgun.ttf", 14)
bad = []
groups = [f"{k}_{d}" for k in K for d in D] + ["spin_south"]
cells = []
for g in groups:
    kind, d = g.split("_")
    for i in range(8):
        key = f"{g}_{i}"
        src = os.path.join(DESK, folder, "원본", "회전베기" if kind == "spin" else f"{K[kind]}_{D[d]}", f"{i+1:02}.png")
        rgb = np.array(Image.open(src).convert("RGB")).astype(int)
        r, gg, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
        m = ((r > 200) & (b > 200) & (gg < 40) & (abs(r - b) < 30)) if folder in {"몸_08_초월"} else ((r > 190) & (b > 190) & (gg < 90) & (abs(r - b) < 60))  # hq_export.STRICT_MAGENTA와 같게
        ys, xs = np.nonzero(m)
        if len(xs) < 50: bad.append((key, "원본에 칼 없음")); continue
        c = np.cov(np.vstack([xs - xs.mean(), ys - ys.mean()]))
        w, v = np.linalg.eigh(c); vx, vy = v[:, 1]
        orig = np.degrees(np.arctan2(vx, -vy)) % 180
        tab = (anch[key][2] * 360 / 64) % 180
        diff = abs((orig - tab + 90) % 180 - 90)
        if diff > 15: bad.append((key, f"축 각도차 {diff:.0f}도"))
print(len(bad)); [print(b) for b in bad]
