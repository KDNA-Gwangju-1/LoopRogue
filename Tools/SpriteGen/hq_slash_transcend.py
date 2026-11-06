# 초월 베기(차원 베기) v2 - 11-06 사용자 "더 크고, 차원을 찢는 느낌 세게"
# pixelfx 보라 베기(바로 시작) + 몹 자리를 순식간에 세 번 더 벰(X자 + 가로) - 벨 때마다 공간이 들쭉날쭉 찢어진 틈이 크게 벌어지고
# 틈 둘레로 금(번개 같은 균열)이 뻗고, 찢긴 공간 조각이 바깥으로 젖혀지고, 주변 빛가루가 틈으로 빨려 들어간다.
# 마지막에 세 틈이 한꺼번에 깨지며 큰 별빛 + 유리 조각 + 고리. 판 256px(가운데 = 용사 몸 가운데)
import os, math, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
C = 256; HC = C // 2; CELL = 51.2
# 방향별 판(11-06 사용자 "세 번 베기 방향을 위아래·좌우 똑같이"): 첫 베기만 방향대로 돌리고, X자 + 가로 베기는 늘 같은 방향
DIR = os.environ.get("DIR", "side")
EX, EY = {"side": (HC + CELL - 2, HC), "up": (HC, HC - CELL + 2), "down": (HC, HC + CELL - 2)}[DIR]   # 몹 가운데
ROT = {"side": 0, "up": 90, "down": -90}[DIR]
SRC = os.path.join(S, "pfxframes", "hq_slash_g7_src")
OUT = os.path.join(S, "pfxframes", "hq_slash_g7" + ("" if DIR == "side" else "_" + DIR))
slash = [Image.open(os.path.join(SRC, f"{i}.png")).convert("RGBA") for i in range(len(os.listdir(SRC)))][2:]
slash = [np.array(im.rotate(ROT, resample=Image.NEAREST) if ROT else im) for im in slash]

VOID = np.array([14, 6, 34.]); VOID2 = np.array([46, 16, 92.]); NEB = np.array([110, 40, 170.]); EDGE = np.array([210, 180, 255.]); EDGE2 = np.array([130, 70, 230.])
WHITE = np.array([255, 250, 255.]); PINK = np.array([255, 190, 245.]); GOLDV = np.array([255, 236, 170.]); OLV = np.array([30, 10, 60.])
# (시작 장, 각도, 길이)
CUTS = [(1, -36, 82), (3, 36, 82), (5, 0, 90)]
SHATTER = 13
NF = 26
rng = np.random.default_rng(7)
STARS = [(rng.uniform(-1, 1), rng.uniform(-1, 1), int(rng.integers(0, 4))) for _ in range(36)]
Y_, X_ = np.mgrid[0:C, 0:C].astype(float)


def put(L, x, y, col):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < C and 0 <= y < C:
        L[y, x, :3] = col; L[y, x, 3] = 255


def crack(seed, x, y, ang, ln):
    # 번개처럼 꺾이며 뻗는 금(점 목록) + 가지
    r = np.random.default_rng(seed); pts = [(x, y)]; out = []
    for i in range(int(ln)):
        ang += r.uniform(-0.6, 0.6); x += math.cos(ang); y += math.sin(ang); pts.append((x, y))
        if i > 4 and r.random() < 0.05:
            out += crack(seed * 7 + i, x, y, ang + r.choice([-1, 1]) * r.uniform(0.6, 1.1), ln * 0.45)
    return [pts] + out


# 틈마다 미리: 들쭉날쭉 모양, 금, 젖혀지는 조각 자리
CUT = []
for ci, (st, deg, ln) in enumerate(CUTS):
    a = math.radians(deg); ux, uy = math.cos(a), math.sin(a); nx, ny = -uy, ux
    r = np.random.default_rng(100 + ci); half = ln / 2
    cr = []
    for k in range(5):              # 틈 가장자리·끝에서 뻗는 금
        su = r.uniform(-0.9, 0.9) * half if k > 1 else (half if k == 0 else -half)
        sgn = r.choice([-1, 1])
        bx, by = EX + su * ux, EY + su * uy
        ang = math.atan2(ny * sgn, nx * sgn) + r.uniform(-0.7, 0.7) if k > 1 else math.atan2(uy * (1 if k == 0 else -1), ux * (1 if k == 0 else -1))
        cr += crack(1000 + ci * 50 + k, bx, by, ang, r.uniform(7, 12) if k > 1 else r.uniform(12, 18))
    flaps = [(r.uniform(-0.75, 0.75), r.choice([-1, 1]), r.uniform(4, 7)) for _ in range(6)]   # 젖혀지는 공간 조각
    CUT.append(dict(st=st, ux=ux, uy=uy, nx=nx, ny=ny, half=half, cracks=cr, flaps=flaps, ph=r.uniform(0, 6)))
shards = []; motes = []
frames = []
for f in range(NF):
    L = np.zeros((C, C, 4))
    if f < len(slash):
        L[HC - 96:HC + 96, HC - 96:HC + 96] = slash[f]
    for ci, c in enumerate(CUT):
        age = f - c["st"]
        if age < 0 or f >= SHATTER:
            continue
        ux, uy, nx, ny, half = c["ux"], c["uy"], c["nx"], c["ny"], c["half"]
        dx, dy = X_ - EX, Y_ - EY
        u = dx * ux + dy * uy; v = dx * nx + dy * ny
        if age == 0:                # 벤 순간: 굵은 흰 선 + 끝까지 뻗는 빛줄기
            core = (np.abs(u) <= half) & (np.abs(v) < 1.6)
            L[core, :3] = WHITE; L[core, 3] = 255
            glow = (np.abs(u) <= half + 16) & (np.abs(v) < 3.5) & ~core & (((X_ + Y_) % 2) == 0)
            L[glow, :3] = PINK; L[glow, 3] = 255
            continue
        # 들쭉날쭉 찢긴 틈: 가운데 선이 지그재그로 휘고, 너비도 울퉁불퉁
        zig = 2.2 * np.sin(u * 0.16 + c["ph"]) + 0.9 * np.abs(np.sin(u * 0.33 + c["ph"] * 2)) * np.sign(np.sin(u * 0.11 + c["ph"]))
        jag = 1 + 0.4 * np.abs(np.sin(u * 0.21 + c["ph"])) - 0.2 * (np.sin(u * 0.47 + c["ph"]) > 0.6)
        wmax = 9.0 * min(1, age / 2) * (1 + 0.1 * math.sin(f * 1.7 + ci))
        w = wmax * np.sqrt(np.clip(1 - (u / half) ** 2, 0, 1)) * jag
        vv = v - zig
        inside = np.abs(u) <= half
        rift = inside & (np.abs(vv) <= w)
        edge = inside & (np.abs(vv) <= w + 1.3) & ~rift
        edge2 = inside & (np.abs(vv) <= w + 2.4) & ~rift & ~edge
        dark = inside & (np.abs(vv) <= w + 3.4) & ~rift & ~edge & ~edge2
        L[dark, :3] = OLV; L[dark, 3] = 255
        L[edge2, :3] = EDGE2; L[edge2, 3] = 255
        L[edge, :3] = WHITE; L[edge, 3] = 255
        # 틈 안: 검보라 우주 + 소용돌이 성운 + 별
        rel = np.abs(vv[rift]) / np.maximum(w[rift], .1)
        swirl = np.sin(u[rift] * 0.12 - f * 0.6 + vv[rift] * 0.35)
        col = np.where((rel > 0.7)[:, None], VOID2, np.where((swirl > 0.55)[:, None], NEB, VOID))
        L[rift, :3] = col; L[rift, 3] = 255
        for (su, sv, ph) in STARS:
            uu = su * half; wl = wmax * math.sqrt(max(0, 1 - su * su))
            if abs(sv) * wl < wl - 1 and (f + ph) % 4 != 0:
                z = 2.2 * math.sin(uu * 0.16 + c["ph"]) + 0.9 * abs(math.sin(uu * 0.33 + c["ph"] * 2)) * np.sign(math.sin(uu * 0.11 + c["ph"]))
                put(L, EX + uu * ux + (sv * wl * 0.8 + z) * nx, EY + uu * uy + (sv * wl * 0.8 + z) * ny, [WHITE, PINK, GOLDV, EDGE][ph])
        # 번개 같은 금: 벌어질수록 길어짐
        grow = min(1, age / 3)
        for pts in c["cracks"]:
            n = int(len(pts) * grow)
            for i, (px_, py_) in enumerate(pts[:n]):
                put(L, px_, py_, WHITE if i < n * 0.3 else EDGE2)
        # 찢겨 바깥으로 젖혀지는 공간 조각(어두운 삼각 + 라벤더 테두리)
        for (su, sgn, sz) in c["flaps"]:
            uu = su * half; wl = wmax * math.sqrt(max(0, 1 - su * su)) + 1.5
            bx, by = EX + uu * ux + sgn * wl * nx, EY + uu * uy + sgn * wl * ny
            lift = sz * min(1, age / 3)
            tip = (bx + sgn * nx * lift + ux * lift * 0.4, by + sgn * ny * lift + uy * lift * 0.4)
            a1 = (bx - ux * 3, by - uy * 3); a2 = (bx + ux * 3, by + uy * 3)
            xs = [a1[0], a2[0], tip[0]]; ys = [a1[1], a2[1], tip[1]]
            for yy in range(int(min(ys)), int(max(ys)) + 1):
                for xx in range(int(min(xs)), int(max(xs)) + 1):
                    def sg(p1, p2, p3): return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])
                    p = (xx + .5, yy + .5); d1, d2, d3 = sg(p, a1, a2), sg(p, a2, tip), sg(p, tip, a1)
                    if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)):
                        put(L, xx, yy, VOID2 if (xx + yy) % 3 else NEB)
            for t_ in np.linspace(0, 1, 8):
                put(L, a1[0] + (tip[0] - a1[0]) * t_, a1[1] + (tip[1] - a1[1]) * t_, EDGE)
                put(L, a2[0] + (tip[0] - a2[0]) * t_, a2[1] + (tip[1] - a2[1]) * t_, EDGE)
        # 빨려 드는 빛가루
        for _ in range(2):
            su = rng.uniform(-0.8, 0.8); sgn = rng.choice([-1, 1]); d0 = rng.uniform(18, 34)
            motes.append([EX + su * half * ux + sgn * d0 * nx, EY + su * half * uy + sgn * d0 * ny, -sgn * nx, -sgn * ny, int(d0 / 4)])
        # 틈 끝 별빛
        for sgn in (-1, 1):
            tx, ty = EX + sgn * (half + 2) * ux, EY + sgn * (half + 2) * uy
            for d_ in (1, 2, 3):
                for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    put(L, tx + ddx * d_, ty + ddy * d_, PINK if d_ == 3 else WHITE)
    alive = []
    for m in motes:                 # 빛가루가 틈 쪽으로 빨려 감
        m[0] += m[2] * 4; m[1] += m[3] * 4; m[4] -= 1
        if m[4] > 0 and f < SHATTER:
            alive.append(m); put(L, m[0], m[1], EDGE if m[4] > 2 else WHITE); put(L, m[0] - m[2], m[1] - m[3], EDGE2)
    motes = alive
    if f == SHATTER:                # 다 같이 깨짐: 큰 별빛 + 조각
        for c in CUT:
            for k in range(30):
                su = rng.uniform(-0.5, 0.5) * c["half"] * 2
                x0, y0 = EX + su * c["ux"], EY + su * c["uy"]
                sp = rng.uniform(2.5, 6.5); sgn = 1 if rng.random() < .5 else -1
                shards.append([x0, y0, c["nx"] * sp * sgn + c["ux"] * rng.uniform(-1.5, 1.5), c["ny"] * sp * sgn + c["uy"] * rng.uniform(-1.5, 1.5),
                               int(rng.integers(2, 6)), [WHITE, EDGE, PINK, VOID2, NEB][int(rng.integers(5))], int(rng.integers(6, 11))])
        for rr_ in range(0, 40):
            for ang in np.linspace(0, 2 * math.pi, 96):
                if rr_ < 8 or (abs(math.sin(2 * ang)) < 0.06) or (rr_ < 22 and abs(math.sin(2 * ang + math.pi / 4)) < 0.05):
                    put(L, EX + math.cos(ang) * rr_, EY + math.sin(ang) * rr_, WHITE if rr_ < 10 else PINK)
    if SHATTER + 1 <= f <= SHATTER + 4:
        for k_, rr0 in enumerate((18, 10)):
            rr = rr0 + (f - SHATTER - 1) * 12
            for ang in np.linspace(0, 2 * math.pi, 240):
                if (f - SHATTER) < 3 or int(ang * 12) % 2:
                    put(L, EX + math.cos(ang) * rr, EY + math.sin(ang) * rr, (EDGE if int(ang * 10) % 2 else WHITE) if k_ == 0 else PINK)
    alive = []
    for sh in shards:
        sh[0] += sh[2]; sh[1] += sh[3]; sh[2] *= 0.88; sh[3] *= 0.88; sh[6] -= 1
        if sh[6] > 0:
            alive.append(sh); sz = sh[4]
            for yy in range(sz):
                for xx in range(sz - yy):
                    put(L, sh[0] + xx, sh[1] + yy, sh[5] if (f + xx) % 3 else WHITE)
            put(L, sh[0] - 1, sh[1] - 1, OLV)
    shards = alive
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
