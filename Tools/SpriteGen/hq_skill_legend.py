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


def paw_img(ang, sc, fade=0.0, f=0):
    # 금빛 호랑이 앞발(전설 베기와 같은 그림) - 80판 가운데, 발톱이 ang 방향
    C = 80; M = np.zeros((C, C), bool); COL = np.zeros((C, C, 3)); cx = cy = C / 2
    ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    def p(u, v, col):
        x = int(round(cx + (u * ca - v * sa) * sc)); y = int(round(cy + (u * sa + v * ca) * sc))
        if 0 <= x < C and 0 <= y < C: M[y, x] = True; COL[y, x] = col
    def ell(u0, v0, ru, rv, fn):
        for u in np.arange(u0 - ru, u0 + ru + .01, 0.5):
            for v in np.arange(v0 - rv, v0 + rv + .01, 0.5):
                if ((u - u0) / ru) ** 2 + ((v - v0) / rv) ** 2 <= 1: p(u, v, fn(u, v))
    ell(-12, 0, 11, 8.5, lambda u, v: ST if ((u - v * 0.5) % 6) < 2 and abs(v) < 6.5 and u < -5 else (GLD if v < -3 else OR if v < 4 else ORD))
    ell(0, 0, 9, 11, lambda u, v: LGT if v < -6 else GLD if v < 0 else OR)
    ell(1, 1, 4.5, 6, lambda u, v: PADL if v < -2 else PAD)
    toes = ((8, -9), (11, -3.2), (11, 3.2), (8, 9))
    for tu, tv in toes:
        ell(tu, tv, 3.6, 3.1, lambda u, v: GLD if u < tu - 1.5 else OR)
        ell(tu + 0.5, tv, 1.8, 1.6, lambda u, v: PAD)
    for tu, tv in toes:
        for s in np.arange(0, 1.001, 0.04):
            u = tu + 3 + 11 * s; v = tv + 5 * s * s * (1 if tv >= 0 else 0.6); th = 1.7 * (1 - s) + 0.4
            for o in np.arange(-th, th + .01, 0.5): p(u - o * 0.3, v + o, WHITE if abs(o) < th * 0.55 else LGT)
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


if __name__ == "__main__":
    print("spin", SL.save(spin(), "hq_spin_g4"), "cross", SL.save(cross(), "hq_spin_cross_g4"), "dash", [SL.save(dash(n), f"hq_dash_g4_{n}") for n in (1, 2, 3, 4)])
