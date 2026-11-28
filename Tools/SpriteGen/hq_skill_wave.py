# 대시 파동(폭 3줄) 전용 고퀄 그림 - 일반~영웅(11-06): 3칸 폭 초승달 파동 앞면이 길을 따라 밀고 나가고(코드, pixelfx 색 단계),
# 지나간 3줄에 pixelfx 속도선(dash-streak, 등급색)이 남는다. 끝에서 파동이 퍼지며 점으로 흩어짐. 판 = (n칸 + 0.6칸) x (3칸 + 0.6칸)
import math, numpy as np
from PIL import Image
import skill_low as SL
CELL = SL.CELL
H2 = lambda s: np.array([int(s[1:][i:i + 2], 16) for i in (0, 2, 4)], float)


def crescent(a, X, cy, B, th0, pal, alpha=1.0, dotted=0, f=0):
    # ")" 모양: 가운데가 앞으로 불룩, 앞쪽 가장자리 하양 -> 뒤로 갈수록 어두운 등급색
    Hh, W = a.shape[:2]
    for y in range(int(cy - B), int(cy + B) + 1):
        if not 0 <= y < Hh: continue
        v = (y - cy) / B
        if abs(v) > 1: continue
        xc = X - 24 * v * v; th = th0 * math.sqrt(max(0, 1 - v * v)) + 0.8
        for x in range(int(xc - th - 1), int(xc) + 1):
            if not 0 <= x < W: continue
            q = (xc - x) / th
            if q < 0 or q > 1: continue
            if dotted and (x + y * 3 + f) % 4 < dotted: continue
            ci = 6 if q < 0.18 else 5 if q < 0.4 else 4 if q < 0.65 else 3 if q < 0.85 else 2
            col = H2(pal[ci]); al = alpha * (1 if q < 0.85 else 0.7)
            src = np.array([*col, 255 * al]); d = a[y, x]
            oa = src[3] / 255 + d[3] / 255 * (1 - src[3] / 255)
            if oa > 0:
                d[:3] = (src[:3] * src[3] / 255 + d[:3] * d[3] / 255 * (1 - src[3] / 255)) / oa; d[3] = oa * 255
        for x in range(int(xc - th - 7), int(xc) + 4):           # 은은한 빛
            if 0 <= x < W and a[y, x, 3] < 40 and not dotted:
                a[y, x] = (*H2(pal[4]), 50 * alpha)


def wave(g, n):
    pal, sp = SL.pal(g); pad = int(0.3 * CELL)
    W = int(n * CELL) + 2 * pad; Hh = int(3 * CELL) + 2 * pad; cy = Hh / 2; B = 1.45 * CELL
    lanes = []
    for j, dy in enumerate((-CELL, 0, CELL)):
        frs, pv = SL.render("melee/dash-streak", {"palette": pal, "sparks": sp, "reach": int(n * CELL), "seed": 30 + j + g * 3}, f"wave{g}_{n}_{j}")
        lanes.append((frs, pv, dy, 0 if dy == 0 else 1))      # 양옆 줄은 한 장 늦게
    T = max(3, n + 1)                                         # 파동이 끝까지 가는 장 수
    out = []
    for f in range(26):
        can = Image.new("RGBA", (W, Hh))
        for frs, pv, dy, lag in lanes:
            k = f - lag
            if 0 <= k < len(frs):
                can.alpha_composite(frs[k], (int(pad - pv[0] - 9), int(cy + dy - (pv[1] - 20))))
        a = np.array(can).astype(float)
        if f <= T:
            e = f / T; X = pad + 10 + (n * CELL - 4) * (1 - (1 - e) ** 1.6)
            for back, al in ((2, 0.25), (1, 0.5)):              # 지나온 자리 잔상
                eb = max(0, f - back * 0.6) / T; Xb = pad + 10 + (n * CELL - 4) * (1 - (1 - eb) ** 1.6)
                if f - back * 0.6 > 0: crescent(a, Xb, cy, B, 5, pal, al)
            crescent(a, X, cy, B, 11, pal)
        elif f <= T + 6:                                      # 끝에서 크게 퍼지며 얇아지고 점으로 흩어짐
            k = (f - T) / 6; X = pad + 6 + n * CELL + 6 * k
            crescent(a, X, cy, B * (1 + 0.12 * k), max(1.2, 9 * (1 - k)), pal, 1 - 0.6 * k, dotted=0 if k < 0.3 else 1 if k < 0.6 else 2, f=f)
        out.append(Image.fromarray(a.clip(0, 255).astype(np.uint8)))
    return out


if __name__ == "__main__":
    import sys
    for g in [int(x) for x in (sys.argv[1:] or ["0", "1", "2", "3"])]:
        print(g, [SL.save(wave(g, n), f"hq_dash_wave_g{g}_{n}") for n in (1, 2, 3, 4)])
