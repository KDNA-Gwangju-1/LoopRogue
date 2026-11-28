# 사용: python flip_audit.py  -> 모든 몸 세트에서 칼이 거꾸로(칼끝이 손 쪽) 잡힌 장 찾기
# 독립 기준: 원본 마젠타 칼의 두 끝 중 몸 중심에서 먼 쪽이 칼끝. 표 각도의 칼끝 방향과 반대면 의심.
import os, numpy as np
from PIL import Image
RES = r"C:\Users\dignw\OneDrive\Documents\GitHub\LoopRogue\Assets\Resources\Sprites"
DESK = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트"
K = {"idle": "대기", "walk": "걷기", "attack": "공격", "dash": "대쉬"}; D = {"south": "앞", "west": "옆", "north": "뒤"}
for folder in sorted(os.listdir(DESK)):
    if not folder.startswith("몸_"): continue
    n = int(folder[2:4]); s = "HeroHQ" if n == 1 else f"HeroHQ{n-1}"
    anch = {l.split()[0]: int(l.split()[3]) for l in open(os.path.join(RES, s, "weapon_anchor.txt"), encoding="utf-8") if len(l.split()) >= 5}
    sus = []
    for g in [f"{k}_{d}" for k in K for d in D] + ["spin_south"]:
        kind, d = g.split("_")
        for i in range(8):
            key = f"{g}_{i}"
            src = os.path.join(DESK, folder, "원본", "회전베기" if kind == "spin" else f"{K[kind]}_{D[d]}", f"{i+1:02}.png")
            rgb = np.array(Image.open(src).convert("RGB")).astype(int)
            r, gg, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
            m = (r > 190) & (b > 190) & (gg < 90) & (abs(r - b) < 60)
            bg = (abs(rgb - rgb[0, 0]).sum(-1) < 40)
            body = ~m & ~bg
            ys, xs = np.nonzero(m); by, bx = np.nonzero(body)
            if len(xs) < 50: continue
            P = np.vstack([xs, ys]).T.astype(float); c = P.mean(0)
            w, v = np.linalg.eigh(np.cov((P - c).T)); ax = v[:, 1]
            t = (P - c) @ ax
            e1, e2 = c + ax * t.min(), c + ax * t.max()
            bc = np.array([bx.mean(), by.mean()])
            tip = e1 if np.linalg.norm(e1 - bc) > np.linalg.norm(e2 - bc) else e2
            tipdir = (tip - c) / np.linalg.norm(tip - c)
            th = np.radians(anch[key] * 360 / 64); tdir = np.array([np.sin(th), -np.cos(th)])
            dist_gap = abs(np.linalg.norm(e1 - bc) - np.linalg.norm(e2 - bc)) / max(t.max() - t.min(), 1)
            if tipdir @ tdir < 0: sus.append((key, round(dist_gap, 2)))
    print(folder, len(sus), sus)
