# 영원 베기(시간·무한): pixelfx 금빛 베기(바로 시작) + 칼끝에서 이어진 금빛 빛줄기가 몹 둘레에 무한(∞)을 그리고
# 몹 뒤 금빛 시계판의 바늘이 점점 빨리 돌다 12시에 맞는 순간 번쩍 -> 무한 빛줄기가 금빛 불꽃으로 터지고 시계판이 조각난다.
# 판 256px(가운데 = 용사 몸 가운데). 방향별 판: 베기만 돌리고 시계판·무한은 늘 똑바로
import os, math, shutil, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
C = 256; HC = C // 2; CELL = 51.2
DIR = os.environ.get("DIR", "side")
EX, EY = {"side": (HC + CELL - 2, HC), "up": (HC, HC - CELL + 2), "down": (HC, HC + CELL - 2)}[DIR]
ROT = {"side": 0, "up": 90, "down": -90}[DIR]
SRC = os.path.join(S, "pfxframes", "hq_slash_g8_src")
if not os.path.exists(SRC):
    shutil.copytree(os.path.join(S, "pfxframes", "hq_slash_g8"), SRC)
OUT = os.path.join(S, "pfxframes", "hq_slash_g8" + ("" if DIR == "side" else "_" + DIR))
slash = [Image.open(os.path.join(SRC, f"{i}.png")).convert("RGBA") for i in range(len(os.listdir(SRC)))][2:]
slash = [np.array(im.rotate(ROT, resample=Image.NEAREST) if ROT else im) for im in slash]

WHITE = np.array([255, 255, 250.]); CREAM = np.array([255, 245, 200.]); GOLD = np.array([255, 214, 110.]); GOLD2 = np.array([226, 168, 60.])
BRONZE = np.array([150, 96, 34.]); OLG = np.array([70, 40, 12.]); FACE = np.array([255, 236, 170.])
STRIKE = 12                         # 바늘이 12시에 맞고 번쩍
NF = 26
rng = np.random.default_rng(8)


def put(L, x, y, col):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < C and 0 <= y < C:
        L[y, x, :3] = col; L[y, x, 3] = 255


def line(L, x0, y0, x1, y1, col):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for k in range(n + 1):
        put(L, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n, col)


# 무한(∞): 레미니스케이트, 몹 가운데 기준 가로 40·세로 16
def inf_pt(t):
    a, sx, sy = 1.0, 58, 29
    d = 1 + math.sin(t) ** 2
    return EX + sx * math.cos(t) / d, EY + 2 + sy * math.sin(t) * math.cos(t) / d


TRACE = [inf_pt(-math.pi / 2 + k / 400 * 2 * math.pi * 1.0) for k in range(401)]   # 한 바퀴
sparks = []
sand = []                           # [각도, 반지름, 각속도]
frames = []
for f in range(NF):
    L = np.zeros((C, C, 4))
    # 1) 시계판(몹 뒤): 3장부터 나타나 커지고, 바늘이 점점 빨리 돎, 12장 번쩍 뒤 조각
    if 2 <= f < STRIKE + 1:
        k = min(1, (f - 2) / 3)
        R = (22 + 6 * (1 - (1 - k) ** 2)) * 1.45   # 11-06 사용자: 초월만큼 크게
        cx, cy = EX, EY - 4
        dither = f < 4
        for yy in range(int(cy - R - 2), int(cy + R + 3)):
            for xx in range(int(cx - R - 2), int(cx + R + 3)):
                d = math.hypot(xx + .5 - cx, yy + .5 - cy)
                if dither and (xx + yy) % 2:
                    continue
                if R - 1.5 < d <= R + .5:
                    put(L, xx, yy, GOLD if (yy - cy) < 0 else GOLD2)
                elif R + .5 < d <= R + 1.6:
                    put(L, xx, yy, OLG)
                elif R - 3.2 < d <= R - 1.5 and (xx + yy + f) % 3 == 0:
                    put(L, xx, yy, CREAM)           # 반짝이는 안쪽 테
        for yy in range(int(cy - R), int(cy + R + 1)):    # 판 안: 금빛 체크 채움(위 밝게)
            for xx in range(int(cx - R), int(cx + R + 1)):
                d = math.hypot(xx + .5 - cx, yy + .5 - cy)
                if d <= R - 3.2 and (xx + yy) % 2 == 0 and not dither:
                    put(L, xx, yy, np.array([150, 104, 36.]) if (yy - cy) > d * 0.3 else np.array([196, 142, 52.]))
        gear = -f * 0.22                                    # 바깥 톱니 고리(바늘과 반대로 돎)
        for ang in np.linspace(0, 2 * math.pi, 260):
            r0 = R + 2.5; tooth = (int((ang - gear) / (2 * math.pi) * 24) % 2) == 0
            for rr in np.arange(r0, r0 + (4.5 if tooth else 2.2), 0.7):
                put(L, cx + math.cos(ang) * rr, cy + math.sin(ang) * rr, GOLD2 if rr < r0 + 1.5 else BRONZE)
            put(L, cx + math.cos(ang) * (r0 + (5.2 if tooth else 2.9)), cy + math.sin(ang) * (r0 + (5.2 if tooth else 2.9)), OLG)
        gx, gy = cx, cy - R - 1                             # 12시 보석
        for yy in range(-2, 3):
            for xx in range(-2, 3):
                if abs(xx) + abs(yy) <= 2: put(L, gx + xx, gy + yy, WHITE if abs(xx) + abs(yy) == 0 else np.array([120, 230, 255.]) if abs(xx) + abs(yy) == 1 else OLG)
        for h in range(12):                         # 눈금(3·6·9·12는 길게)
            a = h / 12 * 2 * math.pi
            ln = 6 if h % 3 == 0 else 3
            for o_ in (0, 0.8):
                aa = a + o_ * 0.03
                line(L, cx + math.sin(aa) * (R - 3), cy - math.cos(aa) * (R - 3), cx + math.sin(aa) * (R - 3 - ln), cy - math.cos(aa) * (R - 3 - ln), WHITE if h % 3 == 0 else GOLD)
        for rr in (R * 0.42,):                      # 안쪽 작은 고리
            for ang in np.linspace(0, 2 * math.pi, 64):
                if int(ang * 6 + f) % 2:
                    put(L, cx + math.cos(ang) * rr, cy + math.sin(ang) * rr, GOLD2)
        # 모래시계(11-06 사용자 "세로로 모래시계"): 시계판 위에 세로로 서서 모래가 흘러내림, 번쩍 직전 휙 뒤집힘(바늘은 유리 너머로 보임)
        if f < STRIKE:
            th = 0.0 if f < STRIKE - 2 else math.pi * (f - (STRIKE - 3)) / 3
            ct, st_ = math.cos(th), math.sin(th)
            def pl(x, y, col):
                if dither and (int(x) + int(y)) % 2: return
                put(L, cx + x * ct - y * st_, cy + x * st_ + y * ct, col)
            H = 34
            def gw(y):                              # 유리 반너비: 가운데 잘록, 위아래 불룩
                a_ = abs(y) / (H - 4)
                w = 1.5 + 11.0 * min(1, a_) ** 0.75
                if abs(y) > H - 9: w *= 1 - (abs(y) - (H - 9)) / 5 * 0.3
                return w
            us = min(1, max(0, (f - 2) / (STRIKE - 3)))
            top_from = -(H - 4) + us * (H - 7)      # 위 모래 윗면(줄어듦)
            pile = 3 + us * (H - 9)                 # 아래 모래 더미 높이(늘어남)
            for yy in range(-(H - 4), H - 3):
                w = gw(yy)
                for xx in range(-int(w) + 1, int(w)):
                    if yy < -1 and yy >= top_from:  # 위 모래
                        pl(xx, yy, CREAM if yy == int(top_from) else GOLD if (xx + yy) % 3 else GOLD2)
                    elif yy > 0 and yy >= (H - 4) - pile + abs(xx) * 0.55:   # 아래 더미(가운데 볼록)
                        pl(xx, yy, CREAM if (xx * 2 + yy) % 4 == 0 else GOLD)
                    else:                           # 빈 유리 안: 어두운 유리(시계판과 구분) + 비친 빛줄
                        pl(xx, yy, np.array([150, 190, 200.]) if xx == -int(w) + 2 and (yy % 5) < 3 else np.array([40, 28, 16.]))
                for sg in (-1, 1):                  # 유리 테두리 + 바깥 진한 테
                    pl(sg * w, yy, WHITE if (yy + f) % 4 == 0 else CREAM)
                    pl(sg * (w + 1), yy, OLG)
            if us < 1:                              # 가운데로 떨어지는 모래 줄기
                for yy in range(-1, int((H - 4) - pile)):
                    if (yy + f * 2) % 3: pl(0, yy, CREAM if yy < 3 else GOLD)
            for sg in (-1, 1):                      # 위아래 뚜껑 + 기둥
                for yy in range(H - 4, H):
                    for xx in range(-15, 16):
                        edge = yy == H - 1 or abs(xx) == 15 or yy == H - 4
                        pl(xx, sg * yy, OLG if edge else (GOLD if yy == H - 3 else BRONZE))
                for yy in range(-(H - 4), H - 3):
                    pl(sg * 13, yy, GOLD2); pl(sg * 13 + sg, yy, OLG)
                for yy in (-(H // 2), 0, H // 2):   # 기둥 고리 장식
                    pl(sg * 13, yy, WHITE); pl(sg * 13 - sg, yy, GOLD)
        # 바늘: 점점 빨라져 STRIKE에서 둘 다 12시
        u = (f - 2) / (STRIKE - 2)
        big = 2 * math.pi * (3.0 * u ** 2.2)        # 긴바늘 3바퀴
        small = 2 * math.pi * (0.75 * u ** 2.2) + 0.0
        for ang, ln, col, w in ((small, R * 0.5, BRONZE, 2), (big, R * 0.82, WHITE, 1)):
            ex_, ey_ = cx + math.sin(ang) * ln, cy - math.cos(ang) * ln
            line(L, cx, cy, ex_, ey_, col)
            if w > 1:
                line(L, cx + 1, cy, ex_ + 1, ey_, col)
        for yy in range(-1, 2):
            for xx in range(-1, 2):
                put(L, cx + xx, cy + yy, GOLD if (xx, yy) != (0, 0) else WHITE)
    # 2) 금빛 베기 + 시간 잔상(3장·6장 늦게, 크림빛 체크로 한 번씩 더)
    for lag, tint in ((6, 0.55), (3, 0.8)):
        g = f - lag
        if 0 <= g < len(slash):
            sub = L[HC - 96:HC + 96, HC - 96:HC + 96]
            m = (slash[g][..., 3] > 170) & (slash[g][..., :3].mean(-1) > 120) & ((np.add.outer(np.arange(192), np.arange(192)) + f) % 2 == 0)
            sub[m, :3] = np.clip(slash[g][m, :3] * tint + CREAM * (1 - tint), 0, 255); sub[m, 3] = 255
    if f < len(slash):
        sub = L[HC - 96:HC + 96, HC - 96:HC + 96]    # 반투명 그대로 겹치기(옅은 빛이 시계판을 지우지 않게)
        sa = slash[f][..., 3:4] / 255.0; da = sub[..., 3:4] / 255.0
        oa = sa + da * (1 - sa)
        sub[..., :3] = (slash[f][..., :3] * sa + sub[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
        sub[..., 3:4] = oa * 255
    # 3) 무한 빛줄기: 1장부터 칼끝에서 이어져 10장째 한 바퀴 다 그림, 꼬리는 남고 머리가 반짝
    if 1 <= f < STRIKE + 1:
        k = min(1, (f - 1) / 9)
        n = int(len(TRACE) * (1 - (1 - k) ** 1.6))
        for i in range(n):
            x, y = TRACE[i]
            age = (n - i) / max(1, n)
            col = WHITE if age < 0.08 else CREAM if age < 0.3 else GOLD if (i + f) % 5 else GOLD2
            put(L, x, y, col)
            if age < 0.5:                           # 앞쪽은 두껍게
                put(L, x, y - 1, GOLD if age < 0.2 else GOLD2)
            if i % 7 == 0 and (i + f) % 3 == 0:
                put(L, x + rng.uniform(-2, 2), y + rng.uniform(-2, 2), CREAM)
        if n > 0 and n < len(TRACE):                # 빛줄기 머리 별빛
            hx, hy = TRACE[n - 1]
            for d_ in (1, 2, 3):
                for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    put(L, hx + ddx * d_, hy + ddy * d_, WHITE if d_ < 3 else GOLD)
        if k >= 1 and f < STRIKE:                   # 다 그리면 무한 위로 빛이 흐름
            for j in range(3):
                i = int((f * 37 + j * 133) % len(TRACE))
                x, y = TRACE[i]
                for d_ in (1, 2):
                    for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        put(L, x + ddx * d_, y + ddy * d_, WHITE)
    # 3.5) 모래시계 모래알: 시계판 둘레에서 소용돌이치며 빨려 듦
    if 2 <= f < STRIKE:
        for _ in range(6):
            sand.append([rng.uniform(0, 2 * math.pi), rng.uniform(58, 78), rng.uniform(0.22, 0.35)])
    keep = []
    for g_ in sand:
        g_[0] += g_[2]; g_[1] -= 5.8
        if g_[1] > 6 and f < STRIKE:
            keep.append(g_)
            x, y = EX + math.cos(g_[0]) * g_[1], EY - 4 + math.sin(g_[0]) * g_[1] * 0.8
            put(L, x, y, CREAM if g_[1] < 24 else GOLD); put(L, x - math.sin(g_[0]) * 1.5, y + math.cos(g_[0]) * 1.2, GOLD2)
    sand = keep
    # 4) 번쩍: 바늘이 12시에 맞는 순간 - 빛살 + 무한이 금빛 불꽃으로 + 시계판 조각
    if f == STRIKE:                                 # 모래시계 깨짐: 유리 조각 + 모래 흩날림
        for _ in range(22):
            yy = rng.uniform(-30, 30); xx = rng.choice([-1, 1]) * rng.uniform(2, 12)
            a = math.atan2(yy, xx); sp = rng.uniform(2, 5)
            sparks.append([EX + xx, EY - 4 + yy, math.cos(a) * sp, math.sin(a) * sp - 1, int(rng.integers(6, 10)), [WHITE, CREAM][int(rng.integers(2))], 4])
        for _ in range(30):
            a = rng.uniform(0, 2 * math.pi); sp = rng.uniform(1, 4)
            sparks.append([EX + rng.uniform(-8, 8), EY - 4 + rng.uniform(-20, 20), math.cos(a) * sp, math.sin(a) * sp - 1.5, int(rng.integers(5, 10)), [GOLD, GOLD2][int(rng.integers(2))], 1])
    if f == STRIKE:                                 # 시계판 유리 조각(삼각)
        for _ in range(26):
            a = rng.uniform(0, 2 * math.pi); r = rng.uniform(6, 38); sp = rng.uniform(2.5, 6)
            sparks.append([EX + math.cos(a) * r, EY - 4 + math.sin(a) * r, math.cos(a) * sp, math.sin(a) * sp - 1, int(rng.integers(6, 10)), [CREAM, GOLD, np.array([196, 142, 52.])][int(rng.integers(3))], 4])
    if STRIKE <= f <= STRIKE + 3:                   # 무한 모양 맥박: 1.6배로 커지는 빛 테
        sc = 1.15 + (f - STRIKE) * 0.22
        for i in range(0, len(TRACE), 2):
            x, y = TRACE[i]
            put(L, EX + (x - EX) * sc, EY + 2 + (y - EY - 2) * sc, WHITE if f == STRIKE else CREAM if (i // 2 + f) % 2 else GOLD)
    if f == STRIKE:
        for i in range(0, len(TRACE), 6):
            x, y = TRACE[i]; a = rng.uniform(0, 2 * math.pi); sp = rng.uniform(1.5, 4.5)
            sparks.append([x, y, math.cos(a) * sp, math.sin(a) * sp - 1, int(rng.integers(6, 11)), [WHITE, CREAM, GOLD][int(rng.integers(3))], 1])
        for h in range(16):
            a = h / 16 * 2 * math.pi; R = 41
            x, y = EX + math.sin(a) * R, EY - 4 - math.cos(a) * R
            sparks.append([x, y, math.sin(a) * 3.2, -math.cos(a) * 3.2, int(rng.integers(6, 10)), [GOLD, GOLD2, BRONZE][int(rng.integers(3))], 3])
    if STRIKE <= f <= STRIKE + 2:                   # 빛살 8줄 + 가운데 흰 빛
        ln = 34 + (f - STRIKE) * 10
        ln = 62 + (f - STRIKE) * 14
        for h in range(12):
            a = h / 12 * 2 * math.pi + math.pi / 12 * (f - STRIKE)
            for r in range(4, int(ln)):
                if (r + f) % 4 != 0 or f == STRIKE:
                    put(L, EX + math.cos(a) * r, EY - 4 + math.sin(a) * r, WHITE if r < ln * 0.4 else GOLD)
        for yy in range(-6, 7):
            for xx in range(-6, 7):
                if xx * xx + yy * yy <= (36 if f == STRIKE else 16):
                    put(L, EX + xx, EY - 4 + yy, WHITE)
    if STRIKE + 1 <= f <= STRIKE + 4:               # 퍼지는 금빛 고리
        rr = 22 + (f - STRIKE - 1) * 15
        for ang in np.linspace(0, 2 * math.pi, 220):
            if (f - STRIKE) < 3 or int(ang * 14) % 2:
                put(L, EX + math.cos(ang) * rr, EY - 4 + math.sin(ang) * rr, GOLD if int(ang * 10) % 2 else CREAM)
    alive = []
    for s_ in sparks:                               # 불꽃·조각
        s_[0] += s_[2]; s_[1] += s_[3]; s_[2] *= 0.9; s_[3] = s_[3] * 0.9 + 0.12; s_[4] -= 1
        if s_[4] > 0:
            alive.append(s_)
            if s_[6] == 1:
                put(L, s_[0], s_[1], s_[5] if s_[4] > 2 else GOLD2)
                if s_[4] > 4: put(L, s_[0] - s_[2] * .5, s_[1] - s_[3] * .5, GOLD2)
            elif s_[6] == 4:                        # 유리 조각(작은 삼각)
                for yy in range(3):
                    for xx in range(3 - yy):
                        put(L, s_[0] + xx, s_[1] + yy, s_[5] if (xx + yy) else WHITE)
            else:                                   # 시계판 조각(작은 호 조각)
                for k_ in range(3):
                    put(L, s_[0] + k_, s_[1], s_[5]); put(L, s_[0] + k_, s_[1] + 1, OLG)
    sparks = alive
    frames.append(Image.fromarray(L.clip(0, 255).astype(np.uint8), "RGBA"))
while len(frames) > 3 and np.array(frames[-1])[..., 3].sum() < 255 * 3:
    frames.pop()
os.makedirs(OUT, exist_ok=True)
for fn in os.listdir(OUT):
    os.remove(os.path.join(OUT, fn))
for i, fr in enumerate(frames):
    if np.array(fr)[..., 3].sum() == 0:
        fr.putpixel((0, 0), (0, 0, 0, 1))
    fr.save(os.path.join(OUT, f"{i}.png"))
print("frames", len(frames))
