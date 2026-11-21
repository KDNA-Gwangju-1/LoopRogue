# 고퀄 스킬 이펙트 전설(호랑이, 11-06): 금빛 pixelfx 바탕(skill_low) + 금빛 호랑이 앞발(코드) + 검붉은 발톱 자국(pixelfx claw-swipe)
# 대시 = 앞발이 길을 따라 달리듯 찍히고 끝에서 할퀴기 / 회전 = 앞발이 고리를 따라 한 바퀴 돌며 네 모서리에 발톱 자국 / 십자 = 고리 + 네 끝에 발톱 자국
import os, math, json, numpy as np
from PIL import Image
import skill_low as SL
S = SL.S; CELL = SL.CELL; G = 4
OL = np.array([60, 26, 6.]); OR = np.array([246, 150, 30.]); ORD = np.array([196, 96, 18.]); GLD = np.array([255, 206, 90.])
LGT = np.array([255, 236, 160.]); WHITE = np.array([255, 255, 250.]); ST = np.array([40, 20, 10.])
PAD = np.array([130, 52, 40.]); PADL = np.array([170, 80, 66.])
rng = np.random.default_rng(44)


def paw_img(ang, sc, fade=0.0, f=0, C=80):
    # 금빛 호랑이 앞발(전설 베기와 같은 그림) - C판 가운데, 발톱이 ang 방향
    M = np.zeros((C, C), bool); COL = np.zeros((C, C, 3)); cx = cy = C / 2
    ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    def p(u, v, col):
        x = int(round(cx + (u * ca - v * sa) * sc)); y = int(round(cy + (u * sa + v * ca) * sc))
        if 0 <= x < C and 0 <= y < C: M[y, x] = True; COL[y, x] = col
    stp = 0.5 / max(1.0, sc * 1.1)                           # 크게 그릴 때 구멍 안 나게
    def ell(u0, v0, ru, rv, fn):
        for u in np.arange(u0 - ru, u0 + ru + .01, stp):
            for v in np.arange(v0 - rv, v0 + rv + .01, stp):
                if ((u - u0) / ru) ** 2 + ((v - v0) / rv) ** 2 <= 1: p(u, v, fn(u, v))
    ell(-12, 0, 11, 8.5, lambda u, v: ST if ((u - v * 0.5) % 6) < 2 and abs(v) < 6.5 and u < -5 else (GLD if v < -3 else OR if v < 4 else ORD))
    ell(0, 0, 9, 11, lambda u, v: LGT if v < -6 else GLD if v < 0 else OR)
    ell(1, 1, 4.5, 6, lambda u, v: PADL if v < -2 else PAD)
    toes = ((8, -9), (11, -3.2), (11, 3.2), (8, 9))
    for tu, tv in toes:
        ell(tu, tv, 3.6, 3.1, lambda u, v: GLD if u < tu - 1.5 else OR)
        ell(tu + 0.5, tv, 1.8, 1.6, lambda u, v: PAD)
    for tu, tv in toes:
        for s in np.arange(0, 1.001, 0.04 / max(1.0, sc)):
            u = tu + 3 + 11 * s; v = tv + 5 * s * s * (1 if tv >= 0 else 0.6); th = 1.7 * (1 - s) + 0.4
            for o in np.arange(-th, th + .01, stp): p(u - o * 0.3, v + o, WHITE if abs(o) < th * 0.55 else LGT)
    ring = np.zeros_like(M)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): ring |= np.roll(np.roll(M, dy, 0), dx, 1)
    ring &= ~M; allm = M | ring
    if fade > 0: allm &= ((np.add.outer(np.arange(C) * 7, np.arange(C) * 13) + f) % 10) >= fade * 10
    L = np.zeros((C, C, 4), np.uint8)
    L[allm & M, :3] = COL[allm & M]; L[allm & ring, :3] = OL; L[allm, 3] = 255
    return Image.fromarray(L)


def claw_frames(scale):
    d = os.path.join(S, "pfxsrc", "claw", "melee", "claw-swipe")
    js = json.load(open(os.path.join(d, "claw-swipe.json"))); sh = Image.open(os.path.join(d, "claw-swipe.png")).convert("RGBA")
    out = []
    for fr in js["frames"]:
        r = fr["frame"]; im = sh.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))
        out.append(im.resize((int(r["w"] * scale), int(r["h"] * scale)), Image.NEAREST))
    while out and out[0].getbbox() is None: out.pop(0)
    bb = [im.getbbox() for im in out if im.getbbox()]
    return out, (min(b[0] for b in bb) + max(b[2] for b in bb)) / 2, (min(b[1] for b in bb) + max(b[3] for b in bb)) / 2


def embers_step(can, embers):
    a = np.array(can); alive = []
    for e in embers:
        e[0] += e[2]; e[1] += e[3]; e[4] -= 1
        if e[4] > 0:
            alive.append(e); x, y = int(e[0]), int(e[1])
            if 0 <= x < a.shape[1] and 0 <= y < a.shape[0]: a[y, x] = (*(LGT if e[4] > 3 else GLD if e[4] > 1 else ORD), 255)
    embers[:] = alive
    return Image.fromarray(a.astype(np.uint8))


def paste_c(can, im, x, y):
    can.alpha_composite(im, (int(round(x - im.width / 2)), int(round(y - im.height / 2))))


A0 = 0.5                                                   # pixelfx 고리 시작각(오른쪽 아래), 시계 반대 방향으로 돎
turn = lambda f: 1 - (1 - min(1, max(0, f) / 5.1)) ** 1.6     # pixelfx 고리와 같은 진행(0.17초에 한 바퀴)


CLAWC = [(255, 244, 232), (240, 80, 64), (190, 34, 40), (128, 14, 26), (80, 6, 16)]   # 발톱 자국: 하얗게 달궈짐 -> 붉게 -> 검붉게


CLAWPAL = ["#1a0306", "#3a060c", "#7e1018", "#b81c2a", "#e8463e", "#ff9c86", "#ffffff"]   # 검붉은 발톱(하얗게 달궈짐 -> 붉게 -> 검붉게)


def claw_ring(C, R, sy, sc, extra=None, ends=(), post=None):
    # 고리 = 호랑이 앞발이 한 바퀴 돌며 그린다. 발톱 네 개 자리에 pixelfx 회전 고리(검붉은 팔레트, 얇은 칼날)를 네 겹 - 빛 번짐·굵기 변화·불꽃
    HC = C / 2; embers = []; out = []
    rings = []
    for j, o in enumerate((-10, -3.6, 3.6, 10)):
        frs, pv = SL.render("melee/spin-ring", {"palette": CLAWPAL, "sparks": ["#ffffff", "#ff9c86", "#e8463e", "#7e1018"],
                                                "radius": R + o * sc, "sy": sy, "width": 5 if abs(o) > 5 else 6, "seed": 20 + j}, f"claw{j}_{int(R)}")
        rings.append(frs)                                        # 224판, 가운데(112,112)
    CL, CX, CY = claw_frames(1.15)
    for f in range(max(len(r) for r in rings) + 2):
        can = Image.new("RGBA", (C, C))
        if extra: extra(can, f)
        for frs in rings:
            if f < len(frs): can.alpha_composite(frs[f], (int(HC - 112), int(HC - 112)))
        for (px, py) in ends:
            k = f - 7
            if 0 <= k < len(CL): can.alpha_composite(CL[k], (int(px - CX), int(py - CY)))
        if f <= 9:
            th = A0 - turn(f) * 2 * math.pi
            x = HC + math.cos(th) * R; y = HC + math.sin(th) * R * sy
            tang = math.degrees(math.atan2(-math.cos(th) * sy, math.sin(th)))
            paste_c(can, paw_img(tang, sc, max(0, (f - 6) / 3), f), x, y)
            for _ in range(3 if f <= 6 else 7):
                embers.append([x + rng.uniform(-9, 9), y + rng.uniform(-9, 9), rng.uniform(-.5, .5), rng.uniform(-1.4, -.3), int(rng.integers(3, 7))])
        if post: post(can, f, embers)
        can = embers_step(can, embers)
        out.append(can)
    return out


def spin():
    return claw_ring(224, 60, 0.82, 0.9)


def cross():
    # 원형 발톱 고리(회전 베기와 같음)가 닫히면 앞발 넷이 상하좌우로 튀어나가 2칸 끝 몹을 할퀸다
    # 뻗는 길 = 검붉은 pixelfx 속도선(dash-streak), 끝 = 검붉은 발톱 자국(claw-swipe)
    C = 288; HC = 144; D = 2 * CELL; R = 60; sy = 0.82; GO = 5; ARR = 2
    af, pv = SL.render("melee/dash-streak", {"palette": CLAWPAL, "sparks": ["#ffffff", "#ff9c86", "#e8463e", "#7e1018"], "reach": int(D - 30), "seed": 7}, "clawarm")
    ox, oy = pv[0] + 9 - 26, pv[1] - 20                       # 속도선이 용사 몸에서 26px 앞에서 시작
    DIRS = [(0, -1, -90), (1, 0, 0), (0, 1, 90), (-1, 0, 180)]
    def arms(can, f):
        k = f - GO
        if 0 <= k < len(af):
            for rot in (0, 90, 180, 270):
                big = Image.new("RGBA", (C, C)); big.alpha_composite(af[k], (HC - ox, HC - oy))
                can.alpha_composite(big.rotate(rot, resample=Image.NEAREST, center=(HC, HC)))
    def paws(can, f, embers):
        k = f - GO
        if 0 <= k <= ARR + 5:
            e = min(1, (k + 1) / (ARR + 1)); e = 1 - (1 - e) ** 2
            for dx, dy, ang in DIRS:
                r0 = 30; r = r0 + (D - 12 - r0) * e                # 발톱 끝이 몹에 닿게 조금 앞에서 멈춤
                x = HC + dx * r; y = HC + dy * r
                paste_c(can, paw_img(ang, 0.8, max(0, (k - ARR - 1) / 4), f), x, y)
                if k <= ARR + 1:
                    for _ in range(3): embers.append([x + rng.uniform(-7, 7), y + rng.uniform(-7, 7), rng.uniform(-.5, .5), rng.uniform(-1.3, -.3), int(rng.integers(3, 6))])
    ends = [(HC, HC - D), (HC + D, HC), (HC, HC + D), (HC - D, HC)]
    return claw_ring(C, R, sy, 0.9, arms, ends, paws)


def dash(n=3):
    # 금빛 속도선(바탕) + 지나간 칸마다(출발 칸부터 3칸) 호랑이 앞발이 하나씩 찍히고, 그 자리에 검붉은 발톱 자국이 대각선 / \ / 번갈아 남음
    # (도착 칸은 용사와 겹치니까 비움). 판 가로 = 3칸 길 + 0.6칸, 세로 112
    base = SL.dash(G, n); W, Hh = base[0].size; pad = int(0.3 * CELL); cy0 = Hh / 2
    XS = [0] + list(np.linspace(1.35, n - 0.7, n - 1)) if n > 1 else [0]   # 출발 칸 + 1·2번 사이 넓게, 끝은 도착 칸 0.7칸 앞
    CL, CX, CY = claw_frames(1.0)
    def tilt(frs, deg):                                       # 발톱 자국 가운데를 축으로 기울임(/ 또는 \)
        out_ = []
        for im in frs:
            sq = Image.new("RGBA", (96, 96)); sq.alpha_composite(im, (int(48 - CX), int(48 - CY)))
            out_.append(sq.rotate(deg, resample=Image.NEAREST, center=(48, 48)))
        return out_
    CLA, CLB = tilt(CL, -28), tilt([im.transpose(Image.FLIP_LEFT_RIGHT) for im in CL], 28)
    CX = CY = CXF = 48; CLF = CLB; CL = CLA
    Hb = 112; off = (Hb - Hh) // 2; cy = off + cy0; embers = []; out = []
    for f in range(max(len(base), 2 + len(CL))):
        can = Image.new("RGBA", (W, Hb))
        if f < len(base): can.alpha_composite(base[f], (0, off))
        for i in range(n):
            x = pad + CELL * XS[i]; k = f - i                        # 출발 칸부터 한 장 간격
            if k < 0: continue
            frs, cx_ = (CL, CX) if i % 2 == 0 else (CLF, CXF)
            if k < len(frs): can.alpha_composite(frs[k], (int(x - cx_), int(cy - CY)))
            if k < 8:                                         # 앞발이 발톱 자국 방향(/ = 오른쪽 위 -> 왼쪽 아래, \ = 왼쪽 위 -> 오른쪽 아래)으로 휘두름
                sg = -1 if i % 2 == 0 else 1
                dx_, dy_ = sg * math.sin(math.radians(28)), math.cos(math.radians(28))
                e = 1 - (1 - min(1, k / 2.5)) ** 2
                px_, py_ = x + dx_ * (-20 + 38 * e), cy + dy_ * (-20 + 38 * e)
                ang = math.degrees(math.atan2(dy_, dx_)) - sg * (30 - 40 * e)   # 손목을 꺾으며 내려침
                paste_c(can, paw_img(ang, 0.75, max(0, (k - 3) / 4), f), px_, py_)
                if k == 0:
                    for _ in range(7): embers.append([x + rng.uniform(-9, 9), cy + rng.uniform(-8, 8), rng.uniform(-.6, .3), rng.uniform(-1.3, -.2), int(rng.integers(3, 7))])
        can = embers_step(can, embers)
        out.append(can)
    return out


PAWIMG = Image.open(os.path.join(S, "paw_01.png")).convert("RGBA")
def spiritize(im, chunk=2):
    # 11-07 사용자 "손 모양 퀄을 더더더 낮춰야": 색을 7종으로 나누고 2x2 칸을 한 도트로(외곽선·줄무늬 우선) -> 굵고 단순한 도트
    a = np.array(im).astype(float); A = a[..., 3] > 0
    PROTO = [  # (분류, 대표색들)
        (0, [(13, 12, 28)]),                                                    # 외곽선
        (5, [(69, 45, 43), (53, 33, 31), (82, 38, 19), (111, 62, 31)]),         # 줄무늬
        (6, [(254, 254, 254), (202, 176, 166), (181, 153, 149), (115, 97, 93)]),  # 발톱
        (7, [(232, 188, 137), (218, 164, 118), (197, 136, 94), (185, 106, 76), (154, 93, 57)]),  # 발가락 아래·발바닥
        (1, [(146, 67, 4), (190, 97, 7)]), (2, [(218, 120, 11), (239, 143, 17), (252, 165, 25)]),
        (3, [(251, 192, 55), (253, 219, 86), (253, 227, 135)]), (4, [(254, 248, 225), (252, 234, 182)])]
    cols = np.array([c for _, cs in PROTO for c in cs], float); ids = np.array([k for k, cs in PROTO for c in cs])
    d = ((a[..., None, :3] - cols[None, None]) ** 2).sum(-1); cls = np.where(A, ids[d.argmin(-1)], -1)
    H0, W0 = cls.shape; h, w = H0 // chunk, W0 // chunk
    blk = cls[:h * chunk, :w * chunk].reshape(h, chunk, w, chunk).transpose(0, 2, 1, 3).reshape(h, w, -1)
    out_c = np.full((h, w), -1)
    for y in range(h):
        for x in range(w):
            v = blk[y, x]; v = v[v >= 0]
            if len(v) < chunk * chunk / 2: continue
            if (v == 0).sum() >= 2: out_c[y, x] = 0
            elif (v == 5).sum() >= 2: out_c[y, x] = 5
            else: out_c[y, x] = np.bincount(v).argmax()
    PAL = {0: (64, 24, 8), 1: (200, 96, 24), 2: (240, 150, 36), 3: (255, 206, 90), 4: (255, 240, 190), 5: (150, 44, 20), 6: (255, 248, 232), 7: (236, 168, 120)}
    out = np.zeros((h, w, 4), np.uint8)
    for k, c in PAL.items(): out[out_c == k] = (*c, 255)
    return np.array(Image.fromarray(out).resize((w * chunk, h * chunk), Image.NEAREST))


PAWA = spiritize(PAWIMG)
PAWIMG = Image.fromarray(PAWA)
_bb = PAWIMG.getbbox(); PTIP = _bb[2] - 4                          # 발톱 끝 x
PCY = PAWIMG.height * 0.52                                          # 발 세로 가운데(발톱 네 개 가운데)
REARX = PAWIMG.width * 0.32                                         # 이보다 뒤(왼쪽)는 흩어지기 시작


def dash_wave(n):
    # 전설 대시 파동(폭 3줄): 3칸 폭 거대한 금빛 앞발이 길 전체를 한 번에 쓸고 지나가고, 줄마다 길게 이어진 검붉은 발톱 자국 세 줄.
    # 판 = 길 n칸 + 양옆 0.3칸 + 앞뒤 여유 E(앞발이 크니까), 세로 3칸 + 0.6칸. 게임은 판 가운데 = 길 가운데로 그대로 재생
    pad = int(0.3 * CELL); E = 150; W = int(n * CELL) + 2 * pad + 2 * E; Hh = int(3 * CELL) + 2 * pad; cy = Hh / 2
    x0 = E + pad; x1 = E + pad + n * CELL - 0.4 * CELL; SC = 5.2; TIP = 25 * SC; PC = 340   # 발톱 끝은 도착 칸 0.4칸 앞(용사와 안 겹치게), 바깥 발톱이 양옆 줄에 닿게
    T = n + 2                                                   # 앞발이 끝까지 가는 장 수
    lanes = (cy - CELL, cy, cy + CELL)
    CLW = [(255, 246, 236), (255, 150, 120), (232, 60, 52), (170, 24, 36), (110, 10, 22), (70, 4, 14)]
    CLB, CLBX, CLBY = claw_frames(1.0)
    CLB_ = CLB
    embers = []; out = []
    tipx = lambda f: x0 + (x1 - x0) * (1 - (1 - min(1, f / T)) ** 1.8)
    def arc(ff):                                                # 발톱 끝 위치·앞발 각도: 위에서 시작해 아래로 내려치며 돌진
        e = 1 - (1 - min(1, max(0, ff) / T)) ** 1.6
        return tipx(ff), cy - 0.75 * CELL * math.cos(math.pi * e), -32 + 60 * e
    for f in range(T + 20):
        can = Image.new("RGBA", (W, Hh)); a = np.array(can).astype(float)
        # 발톱 자국: 앞발이 휘두르며 쓸고 간 호를 따라 발톱 4줄. 발톱 끝이 지나간 순간부터 생기고, 식으면 붉게 -> 검붉게 -> 점선
        for k in range(3):                                           # 줄(3칸 폭)마다 발톱 자국 1줄씩, 공격 범위 3줄 x n칸 전체
            j_, m_ = k, 1; off = (j_ - 1) * CELL
            NS = int(x1 - x0 + 0.5 * CELL)
            for st in range(0, NS + 1):
                q = st / NS
                age = f - (T + 1 + 2.5 * q)                          # 앞발이 흩어질 때 앞쪽부터 차례로 드러남
                if age < 0: break
                life = 12 + (st + k * 5) % 4
                if age > life: continue
                taper = 0.45 + 0.55 * math.sin(math.pi * q) ** 0.35
                xx = x0 - 0.1 * CELL + st
                yy0 = cy + off - 0.16 * CELL * math.cos(math.pi * q) + 1.5 * math.sin(q * 5 + j_)   # 위에서 아래로 살짝 내려 그은 긁힘
                th = (4.6 if age < 3 else 3.4 if age < 7 else 2.0) * taper
                if age > 9 and (st // 3 + k + f) % 3 == 0: continue
                xi = int(round(xx))
                if not 0 <= xi < W: continue
                for yy in range(int(yy0 - th - 1), int(yy0 + th) + 2):
                    d = abs(yy - yy0) / max(th, 0.5)
                    if not 0 <= yy < Hh: continue
                    if d > 1:
                        if age < 4 and d < 1.6 and a[yy, xi, 3] == 0: a[yy, xi] = (232, 60, 52, 90)
                        continue
                    ci = (0 if d < 0.4 else 1) if age < 2 else (2 if d < 0.5 else 3) if age < 6 else (3 if d < 0.5 else 4) if age < 10 else 5
                    a[yy, xi] = (*CLW[ci], 255)
        can = Image.new("RGBA", (W, Hh))
        can.alpha_composite(Image.fromarray(a.clip(0, 255).astype(np.uint8)))
        if f <= T + 4:                                               # 앞발: 직선이 아니라 위에서 아래로 휘두르며 돌진(발톱 끝이 호를 그림)
            fade = max(0, (f - T - 1) / 4)
            def paw_at(ff, alpha, tint=None):
                pa = PAWA.copy(); ph, pw_ = pa.shape[:2]
                k_ = np.clip((REARX - np.arange(pw_)) / 55, 0, 1)
                r_ = np.random.default_rng(ff * 7 + 1).random((ph, pw_))
                drop = r_ < k_[None, :] ** 0.9
                if fade: drop |= r_ < fade
                if alpha < 1: drop |= r_ < (1 - alpha)
                pa[drop, 3] = 0
                if tint is not None: pa[pa[..., 3] > 0, :3] = tint
                tx, ty, ang = arc(ff)
                im_ = Image.fromarray(pa); w0, h0 = im_.size
                vx, vy = PTIP - w0 / 2, PCY - h0 / 2
                ca_, sa_ = math.cos(math.radians(ang)), math.sin(math.radians(ang))
                rim = im_.rotate(-ang, resample=Image.NEAREST, expand=True)
                nx_, ny_ = rim.width / 2 + vx * ca_ - vy * sa_, rim.height / 2 + vx * sa_ + vy * ca_
                return rim, tx - nx_, ty - ny_
            for back, al, tn in ((2, 0.35, (226, 120, 30)), (1, 0.6, (255, 186, 64))):   # 잔상
                if f - back >= 0 and f <= T:
                    im_, x_, y_ = paw_at(f - back, al, tn); can.alpha_composite(im_, (int(round(x_)), int(round(y_))))
            im_, px, py_ = paw_at(f, 1.0); can.alpha_composite(im_, (int(round(px)), int(round(py_))))
            for _ in range(10 if f <= T else 22):
                embers.append([arc(f)[0] - rng.uniform(0, 120), arc(f)[1] + rng.uniform(-40, 40), rng.uniform(-1.2, 0.2), rng.uniform(-1.6, -0.2), int(rng.integers(4, 9))])
        can = embers_step(can, embers)
        out.append(can)
    return out


def wave_claw():
    # 대시 파동 범위 안 몹 위에 뜨는 붉은 발톱(pixelfx claw-swipe 그대로), 128x128 판 가운데 = 몹 가운데
    fr, cx_, cy_ = claw_frames(1.0)
    while fr and fr[-1].getbbox() is None: fr.pop()
    out = []
    for im in fr:
        can = Image.new("RGBA", (128, 128)); can.alpha_composite(im, (int(round(64 - cx_)), int(round(64 - cy_)))); out.append(can)
    return out


if __name__ == "__main__":
    print("spin", SL.save(spin(), "hq_spin_g4"), "cross", SL.save(cross(), "hq_spin_cross_g4"), "dash", [SL.save(dash(n), f"hq_dash_g4_{n}") for n in (1, 2, 3, 4)],
          "wave", [SL.save(dash_wave(n), f"hq_dash_wave_g4_{n}") for n in (1, 2, 3, 4)])
