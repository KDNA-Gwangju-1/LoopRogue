# 일반~전설 고퀄 베기(11-06 사용자 "나머지는 간단하게"): pixelfx 베기(칼 등급 색)를 앞 빈 2장 없이 바로 시작
# 전설(4)만 맞는 순간 칼끝에서 금빛 불꽃이 조금 튐(신화로 넘어가는 다리). 판 192px(가운데 = 용사 몸 가운데)
import os, math, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
C = 192
TIP = (132, 106)                    # pixelfx 칼끝(호 끝점)
GOLD = (255, 214, 110); CREAM = (255, 245, 200); WHITE = (255, 255, 250); DEEP = (200, 130, 40)
for g in range(5):
    src = os.path.join(S, "pfxframes", f"hq_slash_g{g}")
    frs = [np.array(Image.open(os.path.join(src, f"{i}.png")).convert("RGBA")) for i in range(len(os.listdir(src)))][2:]
    if g == 4:
        rng = np.random.default_rng(4); sparks = []
        for f, L in enumerate(frs):
            if f == 1:
                for _ in range(16):
                    a = rng.uniform(-1.6, 1.0); sp = rng.uniform(2, 5)
                    sparks.append([TIP[0], TIP[1], math.cos(a) * sp, math.sin(a) * sp, int(rng.integers(4, 9))])
            alive = []
            for s in sparks:
                s[0] += s[2]; s[1] += s[3]; s[2] *= 0.88; s[3] = s[3] * 0.88 + 0.25; s[4] -= 1
                if s[4] > 0:
                    alive.append(s)
                    x, y = int(s[0]), int(s[1])
                    if 0 <= x < C and 0 <= y < C:
                        L[y, x] = (*(WHITE if s[4] > 5 else GOLD if s[4] > 2 else DEEP), 255)
                        px, py = int(s[0] - s[2] * .6), int(s[1] - s[3] * .6)
                        if 0 <= px < C and 0 <= py < C and s[4] > 3: L[py, px] = (*GOLD, 255)
            sparks = alive
            if f in (1, 2):                 # 칼끝 금빛 별빛
                for d in (1, 2, 3) if f == 1 else (1, 2):
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        x, y = TIP[0] + dx * d, TIP[1] + dy * d
                        L[y, x] = (*(WHITE if d < 2 else CREAM if d < 3 else GOLD), 255)
    out = os.path.join(S, "hqs", f"hq_slash_g{g}"); os.makedirs(out, exist_ok=True)
    for fn in os.listdir(out): os.remove(os.path.join(out, fn))
    while len(frs) > 3 and frs[-1][..., 3].sum() < 255 * 3: frs.pop()
    for i, L in enumerate(frs):
        im = Image.fromarray(L.astype(np.uint8), "RGBA")
        if L[..., 3].sum() == 0: im.putpixel((0, 0), (0, 0, 0, 1))
        im.save(os.path.join(out, f"{i}.png"))
    print(g, len(frs))
