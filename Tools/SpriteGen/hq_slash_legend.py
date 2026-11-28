# 전설 베기 - 호랑이(11-06 사용자): pixelfx 금빛 베기(바로 시작) + 칼에서 금빛 호랑이 영혼(정면 얼굴 + 줄무늬 불꽃 꼬리)이
# 몹에게 덮쳐 포효(입 쩍, 충격파) + 발톱 자국 세 줄 -> 금빛 불씨로 흩어짐. 판 192px, 방향별 판(얼굴은 늘 똑바로)
import os, math, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
C = 192; HC = C // 2; CELL = 51.2
DIR = os.environ.get("DIR", "side")
EX, EY = {"side": (HC + CELL, HC - 2), "up": (HC, HC - CELL - 2), "down": (HC, HC + CELL - 2)}[DIR]
ROT = {"side": 0, "up": 90, "down": -90}[DIR]
SX, SY = {"side": (HC + 14, HC - 6), "up": (HC, HC - 18), "down": (HC, HC + 12)}[DIR]   # 호랑이가 튀어나오는 칼 자리
SRC = os.path.join(S, "pfxframes", "hq_slash_g4")
PAW = os.environ.get("PAW", "1") == "1"
OUT = os.path.join(S, "hqs" if PAW else "hqs_claw", "hq_slash_g4" + ("" if DIR == "side" else "_" + DIR))
slash = [Image.open(os.path.join(SRC, f"{i}.png")).convert("RGBA") for i in range(len(os.listdir(SRC)))][2:]
slash = [np.array(im.rotate(ROT, resample=Image.NEAREST) if ROT else im).astype(float) for im in slash]

OL = np.array([60, 26, 6.]); OR = np.array([246, 150, 30.]); ORD = np.array([196, 96, 18.]); GLD = np.array([255, 206, 90.])
LGT = np.array([255, 236, 160.]); WH = np.array([255, 250, 238.]); WHD = np.array([226, 214, 196.]); ST = np.array([40, 20, 10.])
NOSE = np.array([210, 90, 100.]); MOUTH = np.array([120, 20, 30.]); EYE = np.array([190, 255, 110.]); WHITE = np.array([255, 255, 250.])
CR1 = np.array([235, 70, 60.]); CR2 = np.array([150, 18, 30.]); CR3 = np.array([72, 6, 16.])
NF = 20; LAND = 4
rng = np.random.default_rng(11)


def pfx(name, scale):
    # pixelfx 시트 -> 프레임 목록(효과 픽셀 중심 기준으로 쓰기 위해 전체 bbox 중심도 돌려줌)
    import json
    d = os.path.join(S, "pfxsrc", name); base = os.path.basename(d)
    js = json.load(open(os.path.join(d, base + ".json"))); sh = Image.open(os.path.join(d, base + ".png")).convert("RGBA")
    out = []
    for fr in js["frames"]:
        r = fr["frame"]; im = sh.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))
        out.append(im.resize((int(r["w"] * scale), int(r["h"] * scale)), Image.NEAREST))
    while out and out[0].getbbox() is None: out.pop(0)
    bb = [im.getbbox() for im in out if im.getbbox()]
    cx = (min(b[0] for b in bb) + max(b[2] for b in bb)) / 2; cy = (min(b[1] for b in bb) + max(b[3] for b in bb)) / 2
    return out, cx, cy


CLAW, CLX, CLY = pfx("claw/melee/claw-swipe", 1.6)       # pixelfx 발톱 할퀴기(MIT), 검붉은 팔레트로 렌더
SPK, SPX, SPY = pfx("spark/impacts/hit-spark", 1.0)      # pixelfx 타격 불꽃(MIT)


def over(L, im, x, y):
    # 알파 겹치기
    a = np.array(im).astype(float); h, w = a.shape[:2]
    x0, y0 = int(round(x)), int(round(y)); X0, Y0 = max(0, x0), max(0, y0); X1, Y1 = min(C, x0 + w), min(C, y0 + h)
    if X1 <= X0 or Y1 <= Y0: return
    s = a[Y0 - y0:Y1 - y0, X0 - x0:X1 - x0]; d = L[Y0:Y1, X0:X1]
    sa = s[..., 3:4] / 255; da = d[..., 3:4] / 255; oa = sa + da * (1 - sa)
    d[..., :3] = np.where(oa > 0, (s[..., :3] * sa + d[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6), 0); d[..., 3:4] = oa * 255


def put(L, x, y, col):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < C and 0 <= y < C:
        L[y, x, :3] = col; L[y, x, 3] = 255


BASE = {"side": 0, "up": -90, "down": 90}[DIR]
PAD = np.array([130, 52, 40.]); PADL = np.array([170, 80, 66.])


def tiger_paw(L, cx, cy, ang, sc, fade):
    # 금빛 호랑이 앞발(발바닥 보이게, 발톱 앞으로). 로컬 u = 발톱 방향
    M = np.zeros((C, C), bool); COL = np.zeros((C, C, 3))
    ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    def p(u, v, col):
        x = int(round(cx + (u * ca - v * sa) * sc)); y = int(round(cy + (u * sa + v * ca) * sc))
        if 0 <= x < C and 0 <= y < C: M[y, x] = True; COL[y, x] = col
    def ell(u0, v0, ru, rv, fn):
        for u in np.arange(u0 - ru, u0 + ru + .01, 0.5):
            for v in np.arange(v0 - rv, v0 + rv + .01, 0.5):
                if ((u - u0) / ru) ** 2 + ((v - v0) / rv) ** 2 <= 1: p(u, v, fn(u, v))
    # 팔(줄무늬 털) -> 손바닥 -> 발가락
    ell(-12, 0, 11, 8.5, lambda u, v: ST if ((u - v * 0.5) % 6) < 2 and abs(v) < 6.5 and u < -5 else (GLD if v < -3 else OR if v < 4 else ORD))
    ell(0, 0, 9, 11, lambda u, v: LGT if v < -6 else GLD if v < 0 else OR)
    ell(1, 1, 4.5, 6, lambda u, v: PADL if v < -2 else PAD)
    toes = ((8, -9), (11, -3.2), (11, 3.2), (8, 9))
    for tu, tv in toes:
        ell(tu, tv, 3.6, 3.1, lambda u, v: GLD if u < tu - 1.5 else OR)
        ell(tu + 0.5, tv, 1.8, 1.6, lambda u, v: PAD)
    for tu, tv in toes:                      # 휘어진 큰 발톱
        for s in np.arange(0, 1.001, 0.04):
            u = tu + 3 + 11 * s; v = tv + 5 * s * s * (1 if tv >= 0 else 0.6)
            th = 1.7 * (1 - s) + 0.4
            for o in np.arange(-th, th + .01, 0.5):
                p(u - o * 0.3, v + o, WHITE if abs(o) < th * 0.55 else LGT)
    a_ = M.copy(); ring = np.zeros_like(a_)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        ring |= np.roll(np.roll(a_, dy, 0), dx, 1)
    ring &= ~a_
    allm = a_ | ring
    if fade > 0:
        allm &= ((np.add.outer(np.arange(C) * 7, np.arange(C) * 13)) % 10) >= fade * 10
    L[allm & a_, :3] = COL[allm & a_]; L[allm & ring, :3] = OL; L[allm, 3] = 255


embers = []; frames = []
for f in range(NF):
    L = np.zeros((C, C, 4))
    if f < len(slash):
        L[:] = slash[f]
    # 호랑이 위치: 칼 자리 -> 몹(LAND장에 도착, 살짝 튀어오르는 호)
    k = min(1, f / LAND); e = 1 - (1 - k) ** 2.4
    hx = SX + (EX - SX) * e; hy = SY + (EY - SY) * e - math.sin(k * math.pi) * 10
    shake = (1 if f % 2 else -1) if LAND <= f <= LAND + 3 else 0
    fade = max(0, (f - 10) / 6)
    # 줄무늬 불꽃 꼬리(칼 자리 -> 머리), 꼬리부터 사라짐
    if PAW and f <= 12:
        n = 22
        for j in range(n):
            t_ = j / (n - 1)
            if t_ < fade * 1.4: continue
            tx = SX + (hx - SX) * t_ - (8 if DIR == "side" else 0) * (1 - t_); ty = SY + (hy - SY) * t_ + math.sin(t_ * 9 - f * 1.3) * 3 * (1 - t_)
            r = 1.5 + 8.5 * t_ ** 0.7
            stripe = int(t_ * 9 + f * 0.0) % 3 == 0
            for yy in range(int(ty - r - 1), int(ty + r + 2)):
                for xx in range(int(tx - r - 1), int(tx + r + 2)):
                    d = math.hypot(xx + .5 - tx, yy + .5 - ty)
                    if d <= r and ((xx + yy + f) % 3 or d < r * 0.6):
                        put(L, xx, yy, ST if stripe and d < r * 0.8 else (LGT if d < r * 0.4 else GLD if d < r * 0.75 else OR))
            if j % 3 == 0 and rng.random() < 0.6:
                embers.append([tx + rng.uniform(-r, r), ty + rng.uniform(-r, r), rng.uniform(-.5, .5), rng.uniform(-1.4, -.4), int(rng.integers(3, 7))])
    # 타격 불꽃(pixelfx hit-spark), 닿는 순간
    if 0 <= f - LAND < len(SPK):
        over(L, SPK[f - LAND], EX - SPX, EY - SPY)
    # 앞발: 날아오며 발톱 치켜들고 -> 몹에서 위에서 아래로 할퀴기
    if PAW and fade < 1:
        if f < LAND:
            tilt = -35; sc = 0.6 + 0.4 * k; px_, py_ = hx, hy
        else:
            s_ = min(1, (f - LAND) / 3); tilt = -35 + 85 * (1 - (1 - s_) ** 2); sc = 1.0
            px_, py_ = hx, hy
        ox_, oy_ = (-math.cos(math.radians(BASE)) * 12, -math.sin(math.radians(BASE)) * 12)   # 발톱 끝이 몹에 닿도록 뒤로
        tiger_paw(L, px_ + ox_ + shake, py_ + oy_, BASE + tilt, sc, min(1, fade))
        if fade > 0:
            for _ in range(8):
                embers.append([hx + rng.uniform(-16, 10), hy + rng.uniform(-12, 12), rng.uniform(-.6, .6), rng.uniform(-1.8, -.6), int(rng.integers(4, 9))])
    if not PAW and f < LAND + 1:
        pk = CLAW[min(3, len(CLAW) - 1)]
        for back, al in ((2, 0.25), (1, 0.5), (0, 1.0)):
            kk = max(0, f - back * 0.5) / LAND; kk = min(1, kk); ee = 1 - (1 - kk) ** 2.4
            x = SX + (EX - SX) * ee; y = SY + (EY - SY) * ee - math.sin(kk * math.pi) * 10
            im = pk.copy(); im.putalpha(im.getchannel("A").point(lambda v, al=al: int(v * al)))
            over(L, im, x - CLX, y - CLY)
    # 발톱 자국(pixelfx claw-swipe, 검붉은색), 앞발이 할퀼 때 몹 위에
    if 0 <= f - (LAND + 1) < len(CLAW):
        over(L, CLAW[f - LAND - 1], EX - CLX, EY - CLY)
    alive = []
    for em in embers:                        # 금빛 불씨
        em[0] += em[2]; em[1] += em[3]; em[4] -= 1
        if em[4] > 0:
            alive.append(em); put(L, em[0], em[1], LGT if em[4] > 3 else GLD if em[4] > 1 else ORD)
    embers = alive
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
