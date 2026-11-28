# 신화(붉은 용) 스킬 이펙트 - 베기(mythic_slash)와 같은 용 그림을 길만 바꿔서
#  대시: 붉은 속도선 위로 용이 길을 따라 날아가 끝에서 불꽃이 터짐
#  회전: 붉은 고리 위로 용이 용사를 한 바퀴 휘감고 불씨로 흩어짐
#  십자: 고리 + 상하좌우 속도선, 용이 네 잎(십자) 길을 따라 휘돌며 지나감
import math, numpy as np
from PIL import Image
import skill_low as SL
CELL = SL.CELL; G = 5
rng = np.random.default_rng(55)

OL = np.array([46, 6, 10.]); DK = np.array([120, 16, 22.]); MD = np.array([196, 34, 30.]); LT = np.array([232, 52, 40.]); HL = np.array([255, 118, 84.])
BEL = np.array([240, 170, 64.]); BELD = np.array([186, 104, 36.]); GOLD = np.array([255, 196, 70.]); HORN = np.array([250, 226, 160.])
EYE = np.array([255, 240, 90.]); ORANGE = np.array([255, 130, 40.])


def cross4():
    # 11-07 사용자 "십자 파동도 4방향 동시에" - 용 네 마리가 가운데에서 동시에 상하좌우로 한 잎씩(나갔다 2칸 끝을 돌아 용사에게 돌아옴).
    # 지나간 자리 불꽃 길, 머리가 십자 12칸을 지나면 그 칸에 불, 다 돌아오면 가운데에서 십자로 불길이 뿜어짐(cross와 같은 마무리)
    C = 288; HC = 144; Rm = 2 * CELL - 4; T = 14
    Ps = []
    for a in (-90, 0, 90, 180):
        ar = math.radians(a); pts = []
        for u in np.linspace(0, 1, 200):
            th = ar - math.pi / 4 + u * math.pi / 2; r = Rm * math.cos(2 * (th - ar))
            pts.append((HC + r * math.cos(th), HC + r * math.sin(th)))
        Ps.append(Path(pts))
    NPT = 40; trails = [[P.at(P.tot * i / (NPT - 1))[0] for i in range(NPT)] for P in Ps]; born = {}
    CELLS = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if dx or dy] + [(0, -2), (2, 0), (0, 2), (-2, 0)]
    lit = {}; embers = []; out = []; BODY = 88
    for f in range(T + 18):
        img = np.zeros((C, C, 4)); L = np.zeros((C, C, 4)); U = np.zeros((C, C, 4))
        t = min(1, (f + 1) / T)
        heads = [P.at(P.tot * t)[0] for P in Ps]
        for d, tr in enumerate(trails):
            for i, (x, y) in enumerate(tr):
                if i / (NPT - 1) <= t and (d, i) not in born: born[(d, i)] = f
                if (d, i) in born:
                    flame(U, x, y + 4, int((f - born[(d, i)]) * 1.3), i + d * 50, f, 0.8)
                    if f - born[(d, i)] < 6 and (i + f) % 9 == 0:
                        embers.append([x, y - 10, rng.uniform(-.3, .3), rng.uniform(-1.4, -.6), int(rng.integers(3, 6)), [GOLD, ORANGE][rng.integers(2)]])
        for dx, dy in CELLS:
            cx_, cy_ = HC + dx * CELL, HC + dy * CELL
            if (dx, dy) not in lit and f < T + 1 and min(math.hypot(h[0] - cx_, h[1] - cy_) for h in heads) < 0.6 * CELL: lit[(dx, dy)] = f
            if (dx, dy) in lit: ignite(U, cx_, cy_ + 4, f - lit[(dx, dy)], embers)
        k = f - T
        if 0 <= k < 8:
            reach = min(1, (k + 1) / 3) * (2 * CELL + 8)
            for ang in (0, 90, 180, 270):
                dv = np.array([math.cos(math.radians(ang)), math.sin(math.radians(ang))]); tv = np.array([-dv[1], dv[0]])
                for dd in np.arange(14, reach, 3):
                    q = dd / (2 * CELL + 8); wdt = (5.5 - 3 * q) * (1 - max(0, k - 3) / 5)
                    if wdt <= 0.3: continue
                    c0 = np.array([HC, HC]) + dv * dd
                    ci = 0 if (k < 2 and q < 0.5) else 1 if k < 3 else 2 if k < 5 else 3
                    for o in np.arange(-wdt, wdt + .01, 0.5):
                        col = FLC[ci] if abs(o) < wdt * 0.45 else FLC[min(5, ci + 2)]
                        jit = math.sin(dd * 0.35 + f * 2 + ang) * 1.5
                        _px(U, c0[0] + tv[0] * (o + jit), c0[1] + tv[1] * (o + jit), col)
            if k == 0:
                for _ in range(20):
                    a = rng.uniform(0, 2 * math.pi); v = rng.uniform(1.5, 3.5)
                    embers.append([HC, HC, math.cos(a) * v, math.sin(a) * v - .5, int(rng.integers(4, 8)), [GOLD, ORANGE, FLC[0]][rng.integers(3)]])
        mu = U[..., 3] > 0; img[mu] = U[mu]
        fade = max(0, (f - T) / 6); pearls = []; glints = []
        if fade < 1:
            for P in Ps:
                pe, gl = dragon(L, P, P.tot * t, f, fade, embers, BODY, mouth_fire=0.05 < t < 0.9, S=1.0)
                if t < 0.97 and pe is not None: pearls.append(pe)
                glints += gl
        for pe in pearls[1:]: finish(L, f, pe, [], [])
        finish(L, f, pearls[0] if pearls else None, glints, embers)
        m = L[..., 3] > 0; img[m] = L[m]
        out.append(Image.fromarray(img.clip(0, 255).astype(np.uint8)))
    return out


class Path:
    """점 목록 -> 길이 s에서 (위치, 진행 방향, 등 쪽 방향). cens가 있으면 등 = 가운데 반대쪽, 없으면 진행 방향 왼쪽(화면 위)."""
    def __init__(self, pts, cens=None):
        self.p = np.array(pts, float); self.c = None if cens is None else np.array(cens, float)
        self.seg = np.r_[0, np.cumsum(np.hypot(*np.diff(self.p, axis=0).T))]; self.tot = self.seg[-1]

    def at(self, s):
        s = min(max(s, 0), self.tot); i = max(1, min(np.searchsorted(self.seg, s), len(self.seg) - 1))
        f = (s - self.seg[i - 1]) / max(1e-6, self.seg[i] - self.seg[i - 1]); p = self.p[i - 1] * (1 - f) + self.p[i] * f
        t = self.p[i] - self.p[i - 1]; t = t / (np.linalg.norm(t) + 1e-9)
        if self.c is not None:
            o = p - self.c[i]; o = o / (np.linalg.norm(o) + 1e-9)
        else:
            o = np.array([t[1], -t[0]])
        return p, t, o


def _px(L, x, y, col):
    x, y = int(x), int(y)
    if 0 <= x < L.shape[1] and 0 <= y < L.shape[0]: L[y, x, :3] = col; L[y, x, 3] = 255


def disc(L, x, y, r, out):
    H, W = L.shape[:2]
    for yy in range(max(0, int(y - r - 1)), min(H, int(y + r + 2))):
        for xx in range(max(0, int(x - r - 1)), min(W, int(x + r + 2))):
            dx, dy = (xx + .5 - x) / r, (yy + .5 - y) / r
            if dx * dx + dy * dy > 1: continue
            side = dx * out[0] + dy * out[1]; lit = -(dx * 0.45 + dy * 0.9)
            if side < -0.6: col = BEL if lit > -0.2 else BELD
            elif lit > 0.55: col = HL
            elif lit > 0.1: col = LT
            elif lit > -0.45: col = MD if (xx + yy * 2) % 5 else DK
            else: col = DK
            L[yy, xx, :3] = col; L[yy, xx, 3] = 255


def line(L, p, q, col):
    n = int(max(abs(q[0] - p[0]), abs(q[1] - p[1]))) + 1
    for k in range(n + 1):
        _px(L, p[0] + (q[0] - p[0]) * k / n, p[1] + (q[1] - p[1]) * k / n, col)


def tri(L, a, b, c_, col):
    xs = [a[0], b[0], c_[0]]; ys = [a[1], b[1], c_[1]]
    sg = lambda p1, p2, p3: (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])
    for yy in range(int(min(ys)), int(max(ys)) + 1):
        for xx in range(int(min(xs)), int(max(xs)) + 1):
            p = (xx + .5, yy + .5); d1, d2, d3 = sg(p, a, b), sg(p, b, c_), sg(p, c_, a)
            if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)): _px(L, xx, yy, col)


def dragon(L, P, head_s, f, fade, embers, BODY=78.0, mouth_fire=False, S=1.0):
    """L(HxWx4 float)에 용을 그림. P = Path, head_s = 머리 위치(길이), fade 0~1 = 꼬리부터 불씨로 흩어짐. S = 용 크기 배율(픽셀은 그대로 1px)."""
    n = int(52 * max(1, S)); segs = []
    for j in range(n):
        k = j / (n - 1); s_ = head_s - BODY * (1 - k)
        if s_ < 0: continue
        p, tg, out = P.at(s_)
        q = p + out * 2.6 * S * math.sin(s_ / (9.0 * S) - f * 1.4) * (1 - k * 0.7)
        r = (1.5 + 4.8 * k ** 0.5) * (0.82 if k > 0.9 else 1) * S
        if fade and k < fade * 1.3:
            if rng.random() < 0.5:
                embers.append([q[0], q[1], rng.uniform(-.6, .6), rng.uniform(-1.6, -.5), int(rng.integers(4, 9)), [GOLD, LT, HL][rng.integers(3)]])
            continue
        segs.append((j, k, q, tg, out, r))
    step = max(1, int(round(2 * S)))

    def leg(q, tg, out, r, ph, far):
        sw = math.sin(f * 1.3 + ph); col, dk = (DK, OL) if far else (MD, DK)
        sh = q - out * (r * 0.5) + tg * (0.8 if far else -0.8) * S; el = sh + (-out * 4.5 + tg * (-2.5 + 2.5 * sw)) * S; ft = el + (-out * 1.5 + tg * (3.0 + 1.5 * sw)) * S
        for a, b in ((sh, el), (el, ft)):
            for w in range(max(1, int(round(S)))):
                line(L, a + tg * w * 0.9, b + tg * w * 0.9, col)
            line(L, a + tg * 0.9 * max(1, round(S)), b + tg * 0.9 * max(1, round(S)), dk)
        for cl in (-1, 0, 1): line(L, ft, ft + (tg * 2.2 - out * (1.0 * cl + 0.6)) * S, HORN)
    LEGS = {int(n * 0.80), int(n * 0.45)}
    for (j, k, q, tg, out, r) in segs:
        if j in LEGS: leg(q, tg, out, r, j * 0.7 + 1.6, True)
    if segs and segs[0][1] < 0.2:
        j, k, q, tg, out, r = segs[0]
        for u in range(3):
            fl = (4 + 2 * ((f + u) % 3)) * S; bb = q - tg * 1.0 * S
            tri(L, bb + out * (u - 1) * 1.6 * S, bb + out * ((u - 1) * 1.6 + 1.4) * S, bb - tg * fl + out * (u - 1) * 2.4 * S, np.array([255, 90 + 50 * u, 40.]))
    for (j, k, q, tg, out, r) in segs:
        disc(L, q[0], q[1], r, out)
    for (j, k, q, tg, out, r) in segs:
        if (j // step) % 2 == 0 and j % step == 0 and k < 0.93:
            fl = (2.4 + 2.6 * (0.5 + 0.5 * math.sin(j / step * 1.7 + f * 2.1))) * (0.5 + k * 0.7) * S
            tri(L, q + out * (r - .6) - tg * 1.4 * S, q + out * (r - .6) + tg * 1.4 * S, q + out * (r + fl) - tg * 2.0 * S, np.array([240, 60, 30.]))
            tri(L, q + out * (r - .3) - tg * .7 * S, q + out * (r - .3) + tg * .7 * S, q + out * (r + fl * .55) - tg * 1.2 * S, GOLD)
    for (j, k, q, tg, out, r) in segs:
        if j in LEGS: leg(q, tg, out, r, j * 0.7, False)
    pearl = None
    if not (fade and fade * 1.3 > 1.0) and head_s > 0:
        hp, tg, up = P.at(head_s)
        for sgn, ln in ((1, 13), (0.4, 10)):
            a = hp + (-tg * 1.0 + up * 4.2) * S; b = a + (-tg * ln + up * (3.5 * sgn + 2)) * S
            for w in range(max(1, int(round(S)))):
                line(L, a + up * w * 0.9, b + up * w * 0.9, HORN)
            line(L, a + up * 0.9 * max(1, round(S)), b + up * 0.9 * max(1, round(S)), np.array([210, 170, 110.]))
            for fr_ in (0.45, 0.75):
                m_ = a + (b - a) * fr_; line(L, m_, m_ + (up * (3.5 - fr_ * 2) + tg * 0.8) * S, HORN)
        disc(L, hp[0], hp[1], 6.3 * S, up)
        for u in np.arange(1, 11, 1 / S):
            c_ = hp + (tg * u + up * 1.3) * S; disc(L, c_[0], c_[1], (4.3 - u * 0.22) * S, up)
        jaw = 1.6 + 1.4 * max(0, math.sin(f * 1.1))
        for u in np.arange(2, 9, 1 / S):
            c_ = hp + (tg * u - up * (2.2 + jaw * u / 9)) * S; disc(L, c_[0], c_[1], 1.7 * S, up)
        for u in np.linspace(3, 10, int(12 * S)):
            q = hp + (tg * u - up * (0.6 + jaw * u / 10 * 0.6)) * S
            for w in range(int((1 + jaw * u / 10) * S)): _px(L, q[0] - up[0] * w, q[1] - up[1] * w, np.array([70, 8, 14.]))
        for u in (5, 7, 9):
            q = hp + (tg * u - up * 0.5) * S
            for w in range(max(1, int(round(S)))): _px(L, q[0] + tg[0] * w, q[1] + tg[1] * w, np.array([255, 250, 235.]))
        e = hp + (tg * 2.0 + up * 2.2) * S; es = max(2, int(round(2 * S)))
        for oy in range(es):
            for ox in range(es):
                q = e + tg * ox + up * (-oy); _px(L, q[0], q[1], EYE)
        for w in range(max(1, int(round(S)))):
            q = e + tg * (es / 2) - up * w; _px(L, q[0], q[1], np.array([20, 0, 0.]))
        for sgn in (1, -1):
            prev = hp + (tg * 9 - up * 0.5 * sgn) * S
            for u in range(1, 18):
                q = hp + (tg * (9 - u * 1.4) - up * (sgn * 2.5 + 3.0 * math.sin(u * 0.5 - f * 1.5) * (u / 17)) - up * 2.5) * S
                line(L, prev, q, GOLD if u < 10 else ORANGE)
                if S >= 1.5: line(L, prev + up, q + up, GOLD if u < 10 else ORANGE)
                prev = q
        for u in range(4):
            b = hp + (-tg * (2 + u * 2.2) + up * 2.5) * S
            tri(L, b - tg * 1.2 * S, b + tg * 1.2 * S, b + (up * (3.5 + (u + f) % 2 * 1.5) - tg * 2.2) * S, np.array([255, 120 + 30 * (u % 2), 40.]))
        pearl = hp + (tg * 16 + up * 1.0) * S
        if mouth_fire:
            m = hp + tg * 8 * S
            for _ in range(int(4 * S)):
                v = tg * rng.uniform(1.5, 3.0) + up * rng.uniform(-.8, .8)
                embers.append([m[0], m[1], v[0], v[1], int(rng.integers(3, 6)), [EYE, GOLD, ORANGE][rng.integers(3)]])
    if (L[..., 3] > 0).any() and not fade:
        ys_, xs_ = np.nonzero(L[..., 3] > 0)
        for _ in range(3):
            i_ = rng.integers(len(xs_))
            embers.append([float(xs_[i_]), float(ys_[i_]), rng.uniform(-.4, .4), rng.uniform(-1.2, -.4), int(rng.integers(3, 7)), [GOLD, LT][rng.integers(2)]])
    glints = [q for (j, k, q, tg, out, r) in segs if j % 9 == (f % 9)]
    a_ = L[..., 3] > 0; ring = np.zeros_like(a_)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)): ring |= np.roll(np.roll(a_, dy, 0), dx, 1)
    ring &= ~a_; L[ring, :3] = OL; L[ring, 3] = 255
    return pearl, glints


def finish(L, f, pearl, glints, embers):
    if pearl is not None:
        pxx, pyy = pearl
        for yy in range(int(pyy) - 8, int(pyy) + 9):
            for xx in range(int(pxx) - 8, int(pxx) + 9):
                d_ = math.hypot(xx + .5 - pxx, yy + .5 - pyy)
                if 4.2 < d_ < 7.5 and (xx + yy + f) % 2 == 0 and 0 <= yy < L.shape[0] and 0 <= xx < L.shape[1] and L[yy, xx, 3] == 0:
                    _px(L, xx, yy, np.array([255, 150, 50.]) if d_ < 5.8 else np.array([200, 60, 30.]))
        for u in range(6):
            ang = u / 6 * 2 * math.pi + f * 0.5; dv = np.array([math.cos(ang), math.sin(ang)]); tv = np.array([-dv[1], dv[0]])
            c0 = np.array(pearl) + dv * 3.0
            tri(L, c0 - tv * 1.3, c0 + tv * 1.3, c0 + dv * (3 + (u + f) % 2 * 2) + tv * 1.2, np.array([255, 120 + 40 * (u % 2), 40.]))
        for yy in range(int(pyy) - 4, int(pyy) + 5):
            for xx in range(int(pxx) - 4, int(pxx) + 5):
                d_ = math.hypot(xx + .5 - pxx, yy + .5 - pyy)
                if d_ <= 3.6: _px(L, xx, yy, np.array([255, 255, 240.]) if d_ < 1.3 else np.array([255, 226, 110.]) if d_ < 2.5 else np.array([255, 140, 50.]))
        _px(L, pxx - 1, pyy - 2, np.array([255, 255, 255.]))
    for q in glints:
        x0, y0 = q[0] + rng.uniform(-3, 3), q[1] + rng.uniform(-3, 3)
        for d_ in (1, 2):
            for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)): _px(L, x0 + ddx * d_, y0 + ddy * d_, GOLD if d_ == 2 else np.array([255, 245, 200.]))
        _px(L, x0, y0, np.array([255, 255, 255.]))
    alive = []
    for e in embers:
        e[0] += e[2]; e[1] += e[3]; e[3] -= 0.05; e[4] -= 1
        if e[4] > 0:
            alive.append(e); _px(L, e[0], e[1], e[5] if e[4] > 2 else np.array([150, 40, 30.]))
    embers[:] = alive


def burst(L, x, y, k, embers):
    """불꽃 터짐: k장째(0부터) - 퍼지는 불꽃 혀 고리 + 불씨."""
    if k < 0 or k > 7: return
    R = 4 + k * 4.2
    for u in range(12):
        ang = u / 12 * 2 * math.pi + k * 0.2; dv = np.array([math.cos(ang), math.sin(ang) * 0.82]); tv = np.array([-dv[1], dv[0]])
        c0 = np.array([x, y]) + dv * R; ln = (6 - k * 0.6) * (1.2 if u % 2 else 0.8)
        if ln <= 0.5: continue
        tri(L, c0 - tv * 2.2, c0 + tv * 2.2, c0 + dv * ln, np.array([240, 60, 30.]) if k > 3 else np.array([255, 130, 40.]))
        tri(L, c0 - tv * 1.0, c0 + tv * 1.0, c0 + dv * ln * 0.55, GOLD if k < 4 else np.array([255, 130, 40.]))
    if k < 3:
        for yy in range(int(y - 6 + k), int(y + 7 - k)):
            for xx in range(int(x - 6 + k), int(x + 7 - k)):
                if math.hypot(xx - x, yy - y) < 6 - k * 1.5: _px(L, xx, yy, np.array([255, 250, 220.]) if k == 0 else GOLD)
    if k == 0:
        for _ in range(16):
            a = rng.uniform(0, 2 * math.pi); v = rng.uniform(1.2, 3.2)
            embers.append([x, y, math.cos(a) * v, math.sin(a) * v * 0.8 - 0.6, int(rng.integers(4, 9)), [GOLD, ORANGE, LT][rng.integers(3)]])


def compose(base, size, off, P, T, BODY, extra=None, total=None, mouth=None, S=1.0, under=None, ease=1.8):
    """base 그림(pixelfx 붉은 바탕)을 off에 깔고 용을 P 길 따라 T장 동안 머리가 끝까지, 그 뒤 흩어짐."""
    W, H = size; embers = []; out = []
    total = total or max(len(base), T + 12)
    for f in range(total):
        img = np.zeros((H, W, 4))
        if f < len(base):
            b = Image.new("RGBA", (W, H)); b.alpha_composite(base[f], off); img = np.array(b).astype(float)
        L = np.zeros((H, W, 4))
        t = min(1, (f + 1) / T); head_s = P.tot * (1 - (1 - t) ** ease)
        if under:                                            # 용 밑에 깔리는 층(불꽃 고리 등)
            U = np.zeros((H, W, 4)); under(U, f, t, head_s, embers)
            mu = U[..., 3] > 0; img[mu] = U[mu]
        fade = max(0, (f - T) / 7)
        pearl, glints = (None, [])
        if fade < 1:
            pearl, glints = dragon(L, P, head_s, f, fade, embers, BODY, mouth_fire=bool(mouth and mouth(t)), S=S)
            if t >= 0.97: pearl = None
        if extra: extra(L, f, embers)
        finish(L, f, pearl, glints, embers)
        m = L[..., 3] > 0; img[m] = L[m]
        out.append(Image.fromarray(img.clip(0, 255).astype(np.uint8)))
    return out


def dash(n):
    base = SL.dash(G, n); W, Hh = base[0].size; Hb = 112; off = (0, (Hb - Hh) // 2); pad = int(0.3 * CELL); cy = Hb / 2
    x0, x1 = pad - 10, pad + n * CELL - 0.35 * CELL
    pts = [(x0 + (x1 - x0) * u, cy - 4 + 7 * math.sin(u * math.pi * (1 + n * 0.5))) for u in np.linspace(0, 1, 120)]
    P = Path(pts); T = 4 + n
    def extra(L, f, embers): burst(L, x1 + 6, cy - 2, f - T + 1, embers)
    return compose(base, (W, Hb), off, P, T, BODY=min(78, 40 + n * 14), extra=extra, mouth=lambda t: 0.5 < t < 1)


def spin():
    base = SL.ring(G); C = 320; HC = 160; R = 62; sy = 0.82                  # 판을 키움(충격파가 잘리지 않게), 가운데 = 용사 몸 가운데
    from skill_legend import A0
    pts, cens = [], []
    for u in np.linspace(0, 1.12, 220):
        th = A0 - u * 2 * math.pi; pts.append((HC + math.cos(th) * R, HC + math.sin(th) * R * sy)); cens.append((HC, HC))
    P = Path(pts, cens)
    T = 9
    # 1) 용이 지나간 자리에 불꽃 고리가 타오름(위로 일렁이는 불꽃 혀, 노랑 -> 주황 -> 빨강 -> 검붉게 식으며 꺼짐)
    # 2) 용 머리가 주변 8칸을 지날 때마다 그 칸에 불이 붙어 터짐  3) 다 감으면 열기 충격파가 바깥으로 퍼짐
    NPT = 64; ring_pts = [(A0 - u * 1.12 * 2 * math.pi) for u in np.linspace(0, 1, NPT)]
    born = {}
    CELLS = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if dx or dy]
    lit = {}
    FL = [np.array(c, float) for c in ((255, 250, 210), (255, 214, 90), (255, 140, 40), (232, 60, 30), (150, 24, 20), (80, 10, 14))]
    def under(U, f, t, head_s, embers):
        uh = head_s / P.tot
        for i, th in enumerate(ring_pts):
            if i / (NPT - 1) <= uh and i not in born: born[i] = f
            if i not in born: continue
            age = f - born[i]
            if age > 18: continue
            x = HC + math.cos(th) * R; y = HC + math.sin(th) * R * sy
            h = (20 + 8 * math.sin(i * 1.9 + f * 1.7)) * (1 - age / 21) * (1.2 if age < 3 else 1)
            ci = 0 if age < 1 else 1 if age < 4 else 2 if age < 8 else 3 if age < 12 else 4 if age < 15 else 5
            w = 6.4 - age * 0.2
            tri(U, (x - w, y + 1), (x + w, y + 1), (x + 1.5 * math.sin(f + i), y - h), FL[ci])
            if age < 12: tri(U, (x - w * 0.5, y), (x + w * 0.5, y), (x + math.sin(f + i), y - h * 0.55), FL[max(0, ci - 2)])
            if age < 14:                                       # 바닥 불씨 띠(고리 모양이 계속 보이게)
                for o in (-2, -1, 0, 1, 2): _px(U, x + o, y + 2, FL[min(5, ci + 1)]); _px(U, x + o, y + 3, FL[min(5, ci + 2)])
            if age < 10 and (i + f) % 5 == 0:
                embers.append([x, y - h, rng.uniform(-.3, .3), rng.uniform(-1.4, -.6), int(rng.integers(3, 6)), [GOLD, ORANGE][rng.integers(2)]])
        for dx, dy in CELLS:                                    # 머리가 그 칸 방향을 지나면 불이 붙음
            ca = math.atan2(dy * sy, dx) % (2 * math.pi)
            hp = P.at(head_s)[0]; ha = math.atan2(hp[1] - HC, hp[0] - HC) % (2 * math.pi)
            if (dx, dy) not in lit and f < T and abs((ha - ca + math.pi) % (2 * math.pi) - math.pi) < 0.45: lit[(dx, dy)] = f
            if (dx, dy) in lit:
                k = f - lit[(dx, dy)]; cx_, cy_ = HC + dx * CELL * 0.92, HC + dy * CELL * 0.92 * sy + 4
                if 0 <= k < 8:
                    rr = 4 + k * 3.4
                    for u in range(10):
                        a = u / 10 * 2 * math.pi + k * 0.3; dv = np.array([math.cos(a), math.sin(a) * 0.8]); tv = np.array([-dv[1], dv[0]])
                        c0 = np.array([cx_, cy_]) + dv * rr; ln = (8 - k * 0.9) * (1.2 if u % 2 else 0.8)
                        if ln > 0.5: tri(U, c0 - tv * 2.2, c0 + tv * 2.2, c0 + dv * ln, FL[min(5, 1 + k // 2)])
                    if k < 3:
                        for yy in range(int(cy_ - 6), int(cy_ + 7)):
                            for xx in range(int(cx_ - 6), int(cx_ + 7)):
                                d_ = math.hypot(xx - cx_, yy - cy_)
                                if d_ < 6 - k * 1.6: _px(U, xx, yy, FL[0] if d_ < 3 - k else FL[1 + k])
                    if k == 0:
                        for _ in range(6):
                            a = rng.uniform(0, 2 * math.pi); v = rng.uniform(.8, 2.2)
                            embers.append([cx_, cy_, math.cos(a) * v, math.sin(a) * v - .8, int(rng.integers(4, 8)), [GOLD, ORANGE, LT][rng.integers(3)]])
        k = f - T                                               # 열기 충격파: 굵은 불꽃 띠(가운데 하양·노랑, 바깥 주황·빨강)가 바깥으로 퍼지며 식음
        if 0 <= k < 9:
            rr = R + 4 + k * 8.5
            th_ = max(1.0, 4.2 - k * 0.4)                        # 띠 굵기(px)
            core = FL[0] if k < 2 else FL[1] if k < 4 else FL[2] if k < 6 else FL[3]
            edge = FL[2] if k < 3 else FL[3] if k < 6 else FL[4]
            for a in np.linspace(0, 2 * math.pi, int(rr * 7)):
                if k >= 5 and (int(a * 30) + k) % 4 == 0: continue   # 식으면서 끊어짐
                ca, sa = math.cos(a), math.sin(a)
                for o in np.arange(-th_, th_ + 0.01, 0.5):
                    col = core if abs(o) < th_ * 0.45 else edge
                    _px(U, HC + ca * (rr + o), HC + sa * (rr + o) * sy, col)
            if k < 4:                                            # 안쪽 열기 잔광(성긴 점)
                for a in np.linspace(0, 2 * math.pi, int(rr * 2)):
                    if (int(a * 50) + k) % 2: continue
                    _px(U, HC + math.cos(a) * (rr - th_ - 3), HC + math.sin(a) * (rr - th_ - 3) * sy, FL[3])
            if k == 0:
                for _ in range(24):
                    a = rng.uniform(0, 2 * math.pi); v = rng.uniform(2.5, 4.5)
                    embers.append([HC + math.cos(a) * rr, HC + math.sin(a) * rr * sy, math.cos(a) * v, math.sin(a) * v * sy - .4, int(rng.integers(4, 8)), [GOLD, ORANGE, np.array([255, 250, 210.])][rng.integers(3)]])
    return compose(base, (C, C), (HC - 112, HC - 112), P, T, BODY=190, mouth=lambda t: 0.3 < t < 0.95, S=1.4, under=under, total=max(len(base), T + 20))


FLC = [np.array(c, float) for c in ((255, 250, 210), (255, 214, 90), (255, 140, 40), (232, 60, 30), (150, 24, 20), (80, 10, 14))]


def flame(U, x, y, age, i, f, big=1.0):
    """불꽃 혀 하나(age장째): 노랑 -> 주황 -> 빨강 -> 검붉게 식으며 작아짐. 18장이면 꺼짐."""
    if age > 18: return
    h = (16 + 7 * math.sin(i * 1.9 + f * 1.7)) * (1 - age / 21) * (1.2 if age < 3 else 1) * big
    ci = 0 if age < 1 else 1 if age < 4 else 2 if age < 8 else 3 if age < 12 else 4 if age < 15 else 5
    w = (5.4 - age * 0.18) * big
    tri(U, (x - w, y + 1), (x + w, y + 1), (x + 1.5 * math.sin(f + i), y - h), FLC[ci])
    if age < 12: tri(U, (x - w * 0.5, y), (x + w * 0.5, y), (x + math.sin(f + i), y - h * 0.55), FLC[max(0, ci - 2)])
    if age < 14:
        for o in (-2, -1, 0, 1, 2): _px(U, x + o, y + 2, FLC[min(5, ci + 1)])


def ignite(U, cx_, cy_, k, embers):
    """몹 칸에 불이 붙어 터짐(k장째)."""
    if not 0 <= k < 8: return
    rr = 4 + k * 3.4
    for u in range(10):
        a = u / 10 * 2 * math.pi + k * 0.3; dv = np.array([math.cos(a), math.sin(a) * 0.8]); tv = np.array([-dv[1], dv[0]])
        c0 = np.array([cx_, cy_]) + dv * rr; ln = (8 - k * 0.9) * (1.2 if u % 2 else 0.8)
        if ln > 0.5: tri(U, c0 - tv * 2.2, c0 + tv * 2.2, c0 + dv * ln, FLC[min(5, 1 + k // 2)])
    if k < 3:
        for yy in range(int(cy_ - 6), int(cy_ + 7)):
            for xx in range(int(cx_ - 6), int(cx_ + 7)):
                d_ = math.hypot(xx - cx_, yy - cy_)
                if d_ < 6 - k * 1.6: _px(U, xx, yy, FLC[0] if d_ < 3 - k else FLC[1 + k])
    if k == 0:
        for _ in range(6):
            a = rng.uniform(0, 2 * math.pi); v = rng.uniform(.8, 2.2)
            embers.append([cx_, cy_, math.cos(a) * v, math.sin(a) * v - .8, int(rng.integers(4, 8)), [GOLD, ORANGE, LT][rng.integers(3)]])


def cross():
    # 11-07 사용자 "용 말고 있는 이펙트도 바꿔 보자" - pixelfx 고리·흰 속도선 대신: 용이 네 잎(십자) 길을 돌며 지나간 자리에 불꽃 길이 타오르고,
    # 머리가 십자 12칸(주변 8칸 + 2칸 끝 4칸)을 지날 때마다 그 칸에 불이 붙어 터짐. 다 돌면 가운데에서 십자로 불길이 한 번 뿜어져 나감
    C = 288; HC = 144; Rm = 2 * CELL - 4; T = 23
    pts = []
    for u in np.linspace(0, 1, 600):
        th = -u * 2 * math.pi; r = Rm * math.cos(2 * th)
        pts.append((HC + r * math.cos(th), HC + r * math.sin(th)))
    ex, ey = pts[-1]                                          # 11-07 사용자 - 다 돌면 용이 다시 용사 쪽으로 돌아옴(살짝 휘며 가운데로)
    for u in np.linspace(0, 1, 80)[1:]:
        pts.append((ex + (HC + 6 - ex) * u, ey + (HC - ey) * u - 16 * math.sin(math.pi * u)))
    P = Path(pts)
    NPT = 110; trail = [P.at(P.tot * i / (NPT - 1))[0] for i in range(NPT)]; born = {}
    CELLS = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if dx or dy] + [(0, -2), (2, 0), (0, 2), (-2, 0)]
    lit = {}
    def under(U, f, t, head_s, embers):
        uh = head_s / P.tot
        for i, (x, y) in enumerate(trail):
            if i / (NPT - 1) <= uh and i not in born: born[i] = f
            if i in born:
                flame(U, x, y + 4, f - born[i], i, f, 0.8)
                if f - born[i] < 9 and (i + f) % 7 == 0:
                    embers.append([x, y - 10, rng.uniform(-.3, .3), rng.uniform(-1.4, -.6), int(rng.integers(3, 6)), [GOLD, ORANGE][rng.integers(2)]])
        hp = P.at(head_s)[0]
        for dx, dy in CELLS:
            cx_, cy_ = HC + dx * CELL, HC + dy * CELL
            if (dx, dy) not in lit and f < T + 1 and math.hypot(hp[0] - cx_, hp[1] - cy_) < 0.55 * CELL: lit[(dx, dy)] = f
            if (dx, dy) in lit: ignite(U, cx_, cy_ + 4, f - lit[(dx, dy)], embers)
        k = f - T                                            # 마무리: 가운데에서 상하좌우로 불길이 뿜어져 2칸 끝까지
        if 0 <= k < 8:
            reach = min(1, (k + 1) / 3) * (2 * CELL + 8)
            for ang in (0, 90, 180, 270):
                dv = np.array([math.cos(math.radians(ang)), math.sin(math.radians(ang))]); tv = np.array([-dv[1], dv[0]])
                for d in np.arange(14, reach, 3):
                    q = d / (2 * CELL + 8); wdt = (5.5 - 3 * q) * (1 - max(0, k - 3) / 5)
                    if wdt <= 0.3: continue
                    c0 = np.array([HC, HC]) + dv * d
                    ci = 0 if (k < 2 and q < 0.5) else 1 if k < 3 else 2 if k < 5 else 3
                    for o in np.arange(-wdt, wdt + .01, 0.5):
                        col = FLC[ci] if abs(o) < wdt * 0.45 else FLC[min(5, ci + 2)]
                        jit = math.sin(d * 0.35 + f * 2 + ang) * 1.5
                        _px(U, c0[0] + tv[0] * (o + jit), c0[1] + tv[1] * (o + jit), col)
            if k == 0:
                for _ in range(20):
                    a = rng.uniform(0, 2 * math.pi); v = rng.uniform(1.5, 3.5)
                    embers.append([HC, HC, math.cos(a) * v, math.sin(a) * v - .5, int(rng.integers(4, 8)), [GOLD, ORANGE, FLC[0]][rng.integers(3)]])
    return compose([], (C, C), (0, 0), P, T, BODY=130, mouth=lambda t: 0.05 < t < 0.95, ease=1.0, under=under, total=T + 18)


FLOOR_MODE = 'tile'   # 11-07 사용자 "바닥에만 문양" - 칸 문양·불꽃은 hq_wave_tile_g5로 따로(게임이 벽 아닌 칸에만 띄움). 'sigil1' = 예전 한 장짜리


def dash_wave(n):
    # 대시 파동(폭 3줄): 큰 용(1.4배)이 세 줄을 넘나들며 길을 따라 날아가고(속도 = 처음 그대로 T = n+4), 지나가면
    # 11-07 사용자 "불길이 가운데에서 시작, 화려하게, 타일 전체를 메우는 느낌, 충격파는 아님" - 범위 한가운데에서 불꽃이 터지고
    # 거기서부터 바깥으로 3×n 칸 바닥 전체가 불바다(달아오른 불씨 바닥 + 칸마다 여러 줄 불꽃 혀)가 됐다가 식음
    pad = int(0.3 * CELL); E = 70; W = int(n * CELL) + 2 * pad + 2 * E; Hh = int(3 * CELL) + 2 * pad + 24; cy = Hh / 2 + 6
    x0 = E + pad; x1 = E + pad + n * CELL - 0.35 * CELL; T = n + 4
    m = 0.5 + 0.35 * n
    pts = [(x0 - 30 + (x1 - x0 + 30) * u, cy - 0.85 * CELL * math.sin(2 * math.pi * m * u + 0.6)) for u in np.linspace(0, 1, 200)]
    P = Path(pts)
    trail_n = int(P.tot / 6); trail = [P.at(P.tot * i / (trail_n - 1))[0] for i in range(trail_n)]; born = {}
    gx0, gx1 = E + pad, E + pad + n * CELL                     # 공격 범위(3×n) 가장자리
    gy0, gy1 = cy - 1.5 * CELL, cy + 1.5 * CELL
    cxc = (gx0 + gx1) / 2; ax_max = (gx1 - gx0) / 2 + 6; ay_max = 1.5 * CELL + 4
    SPREAD = 4.0                                             # 가운데 -> 끝까지 번지는 장 수
    rn = lambda x, y: min(1.0, math.hypot((x - cxc) / ax_max, (y - cy) / ay_max))
    START = T + 1
    # 바닥 불씨(픽셀마다 불붙는 때·무늬)
    yy, xx = np.mgrid[0:Hh, 0:W]
    inside = (xx >= gx0) & (xx < gx1) & (yy >= gy0 + 2) & (yy < gy1)
    R = np.minimum(1.0, np.hypot((xx - cxc) / ax_max, (yy - cy) / ay_max))
    noise = np.random.default_rng(7).random((Hh, W))
    FLOORC = [np.array(c, float) for c in ((255, 214, 90), (255, 140, 40), (232, 60, 30), (170, 34, 22), (110, 18, 16))]
    rows = [k * CELL / 3 + CELL / 6 + 6 for k in range(9)]   # 칸마다 세 줄(9줄) 불꽃 혀 밑동, 위에서 아래로 그려 아래가 앞
    rows = [gy0 + r for r in rows]
    FMASK = np.zeros((Hh, W), np.int8)
    if FLOOR_MODE == "crack":                                   # 용암 균열: 칸마다 점 몇 개로 보로노이 갈라짐
        rg = np.random.default_rng(11 + n)
        seeds = np.array([(rg.uniform(gx0, gx1), rg.uniform(gy0, gy1)) for _ in range(int(n * 3 * 2.2))])
        d = np.sqrt((xx[..., None] - seeds[:, 0]) ** 2 + (yy[..., None] - seeds[:, 1]) ** 2); d.sort(-1)
        gap = d[..., 1] - d[..., 0]
        FMASK[inside & (gap < 1.3)] = 1; FMASK[inside & (gap >= 1.3) & (gap < 3.2)] = 2
    elif FLOOR_MODE == "sigil":                                 # 칸마다 불 문양(고리 + 안쪽 마름모 불꽃)
        for i in range(n):
            for jj in range(3):
                ccx, ccy = gx0 + (i + 0.5) * CELL, gy0 + (jj + 0.5) * CELL
                rr_ = np.hypot(xx - ccx, (yy - ccy) * 1.15)
                FMASK[(np.abs(rr_ - 19) < 1.0)] = 1; FMASK[(np.abs(rr_ - 19) >= 1.0) & (np.abs(rr_ - 19) < 2.2)] = 2
                dm = np.abs(xx - ccx) + np.abs(yy - ccy) * 1.15
                FMASK[(np.abs(dm - 10) < 0.8)] = 1
                FMASK[(np.abs(xx - ccx) < 0.6) & (np.abs(yy - ccy) < 6)] = 1
    elif FLOOR_MODE == "sigil1":                                # 칸마다 불 문장: 고리 + 여덟 갈래 불꽃 별 + 고리 밖 네 점(칸 수만큼 반복)
        for i in range(n):
            for jj in range(3):
                ccx, ccy = gx0 + (i + 0.5) * CELL, gy0 + (jj + 0.5) * CELL
                dx_, dy_ = xx - ccx, (yy - ccy) * 1.12
                rr_ = np.hypot(dx_, dy_); th = np.arctan2(dy_, dx_)
                ring = np.abs(rr_ - 20)
                FMASK[ring < 1.0] = 1; FMASK[(ring >= 1.0) & (ring < 2.2) & (FMASK == 0)] = 2
                star = 6 + 9 * np.abs(np.cos(4 * th)) ** 3            # 여덟 갈래(긴 4 + 짧은 4)
                star = np.where(np.cos(4 * th) > 0, 6 + 10 * np.cos(4 * th) ** 4, 6 + 5 * np.abs(np.cos(4 * th)))
                FMASK[np.abs(rr_ - star) < 0.8] = 1
                FMASK[rr_ < 2.2] = 1
                for a_ in (0.25, 0.75, 1.25, 1.75):
                    px_, py_ = ccx + 24.5 * math.cos(a_ * math.pi), ccy + 24.5 * math.sin(a_ * math.pi) / 1.12
                    FMASK[np.hypot(xx - px_, yy - py_) < 1.6] = 1
    elif FLOOR_MODE == "sigil2":                                # 범위 전체 하나의 불 문장: 이중 테두리 + 칸 가운데 작은 고리 + 그 사이를 잇는 마름모 격자 + 가운데 큰 고리
        cxs = [gx0 + (i + 0.5) * CELL for i in range(n)]; cys = [gy0 + (jj + 0.5) * CELL for jj in range(3)]
        bx = np.minimum(xx - gx0, gx1 - xx); by = np.minimum(yy - gy0, gy1 - yy); bd = np.minimum(bx, by)
        FMASK[inside & (np.abs(bd - 3) < 1.0)] = 1; FMASK[inside & (np.abs(bd - 7) < 0.7)] = 1
        FMASK[inside & (np.abs(bd - 5) < 1.0) & (FMASK == 0)] = 2
        def seg(ax_, ay_, bx_, by_, w=0.75):
            vx, vy = bx_ - ax_, by_ - ay_; L2 = vx * vx + vy * vy
            t_ = np.clip(((xx - ax_) * vx + (yy - ay_) * vy) / L2, 0, 1)
            return np.hypot(xx - ax_ - t_ * vx, yy - ay_ - t_ * vy) < w
        for i in range(n):
            for jj in range(3):
                x_, y_ = cxs[i], cys[jj]
                FMASK[np.abs(np.hypot(xx - x_, yy - y_) - 9) < 0.9] = 1
                FMASK[np.hypot(xx - x_, yy - y_) < 2.0] = 1
                for di, dj in ((1, 1), (1, -1)):
                    if 0 <= i + di < n and 0 <= jj + dj < 3:
                        FMASK[seg(x_, y_, cxs[i + di], cys[jj + dj]) & (np.hypot(xx - x_, yy - y_) > 10) & (np.hypot(xx - cxs[i + di], yy - cys[jj + dj]) > 10)] = 1
        big = np.abs(np.hypot((xx - cxc) / (ax_max - 14), (yy - cy) / (ay_max - 14)) - 1)
        FMASK[inside & (big < 0.018)] = 1; FMASK[inside & (big >= 0.018) & (big < 0.04) & (FMASK == 0)] = 2
    elif FLOOR_MODE == "grid":                                  # 칸 테두리가 불타는 선
        lx = (xx - gx0) % CELL; ly_ = (yy - gy0) % CELL
        dd = np.minimum(np.minimum(lx, CELL - lx), np.minimum(ly_, CELL - ly_))
        FMASK[inside & (dd < 1.6)] = 1; FMASK[inside & (dd >= 1.6) & (dd < 3.2)] = 2
    SIG = FLOOR_MODE in ("sigil1", "sigil2")
    sy_, sx_ = np.nonzero(FMASK == 1); SPTS = list(zip(sx_[::23], sy_[::23])) if SIG else []
    def under(U, f, t, head_s, embers):
        uh = head_s / P.tot
        for i, (x, y) in enumerate(trail):                      # 용이 지나간 길의 불꽃
            if i / (trail_n - 1) <= uh and i not in born: born[i] = f
            if i in born: flame(U, x, y + 6, f - born[i] + 4, i, f, 0.6)
        age = f - (START + SPREAD * R)
        if FLOOR_MODE == "ember":                                # 달아오른 바닥: 붉은 불씨가 성기게
            edge = np.minimum.reduce([xx - gx0, gx1 - xx, yy - gy0, gy1 - yy]) / 6.0
            flick = np.random.default_rng(f * 13 + 1).random((Hh, W))
            dens = 0.42 - np.clip((age - 6) / 12, 0, 0.42)
            on = inside & (age >= 0) & (age < 18) & ((noise * 0.6 + flick * 0.4) < dens * np.clip(edge, 0.3, 1))
            ci = np.clip((age + flick * 3) / 4.0 + 1, 1, 4).astype(int)
            for c in range(1, 5):
                sel = on & (ci == c); U[sel, :3] = FLOORC[c]; U[sel, 3] = 255
        elif FLOOR_MODE in ("crack", "sigil", "grid", "sigil1", "sigil2"):           # 미리 만든 무늬(FMASK: 1 = 밝은 심, 2 = 테두리 빛)를 불붙는 때부터 달궜다 식힘
            on = (FMASK > 0) & (age >= 0) & (age < 20)
            ci = np.clip(age / 4.0, 0, 4).astype(int) + (FMASK == 2)
            ci = np.clip(ci, 0, 4)
            flick = np.random.default_rng(f * 13 + 1).random((Hh, W))
            on &= ~((age > 10) & (flick < (age - 10) / 10))       # 식으며 끊어짐
            for c in range(5):
                sel = on & (ci == c); U[sel, :3] = FLOORC[c]; U[sel, 3] = 255
        if FLOOR_MODE == 'tile': return
        if SIG:                                                  # 문장 모드: 문장 선 위에서 작은 불꽃이 솟음(문장이 가려지지 않게 바닥 불꽃은 성기게)
            for i, (sx, sy) in enumerate(SPTS):
                a_ = f - (START + SPREAD * rn(sx, sy)) - (i % 3) * 0.5
                if a_ >= 0: flame(U, sx, sy, int(a_ * 1.2), i, f, 0.42)
        for j, ry in enumerate(rows):                           # 칸 바닥을 메우는 불꽃 혀(줄마다 엇갈림)
            if SIG and j % 3 != 2: continue
            for i, x in enumerate(np.arange(gx0 + 3 + (j % 2) * 4, gx1 - 2, 8 if not SIG else 11)):
                a_ = f - (START + SPREAD * rn(x, ry)) - ((i * 7 + j * 3) % 3) * 0.5
                if a_ >= 0:
                    tall = (1.15 if (i + j) % 4 == 0 else 0.8) * (0.75 if SIG else 1)
                    flame(U, x + math.sin(i * 2.3 + j) * 2, ry, int(a_), i + j * 31, f, tall)
                    if a_ < 8 and (i * 3 + j + f) % 11 == 0:
                        embers.append([x, ry - 10, rng.uniform(-.4, .4), rng.uniform(-2.0, -0.8), int(rng.integers(4, 8)), [GOLD, ORANGE, FLC[0]][rng.integers(3)]])
    def extra(L, f, embers):
        k = f - START
        burst(L, cxc, cy, k, embers)                            # 가운데에서 불이 터지며 시작
        if k == 0:
            for _ in range(30):
                a = rng.uniform(0, 2 * math.pi); v = rng.uniform(1.5, 3.5)
                embers.append([cxc, cy, math.cos(a) * v * 1.4, math.sin(a) * v * 0.7 - .6, int(rng.integers(5, 10)), [GOLD, ORANGE, FLC[0]][rng.integers(3)]])
        if 3 <= k < 16 and FLOOR_MODE != 'tile':                 # 금빛 반짝임
            for _ in range(2):
                x = rng.uniform(gx0, gx1); y = rng.uniform(gy0, gy1 - 10)
                for d_ in (1, 2):
                    for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)): _px(L, x + ddx * d_, y + ddy * d_, GOLD if d_ == 2 else np.array([255, 245, 200.]))
                _px(L, x, y, np.array([255, 255, 255.]))
    return compose([], (W, Hh), (0, 0), P, T, BODY=150, mouth=lambda t: 0.3 < t < 1, S=1.4, under=under, extra=extra, total=T + 26)


def wave_tile():
    # 대시 파동 바닥 한 칸(128판 가운데 = 칸 가운데, 게임이 벽 아닌 칸마다 가운데부터 차례로 늦게 띄움):
    # 불 문장(고리 + 여덟 갈래 별 + 바깥 네 점)이 달궈졌다 식으며 끊어지고, 문장 선 위 작은 불꽃 + 칸 아래쪽 불꽃 한 줄 + 불티·반짝임
    C = 128; cc = 64; yy, xx = np.mgrid[0:C, 0:C]
    FM = np.zeros((C, C), np.int8)
    dx_, dy_ = xx - cc, (yy - cc) * 1.12
    rr_ = np.hypot(dx_, dy_); th = np.arctan2(dy_, dx_)
    ring = np.abs(rr_ - 20)
    FM[ring < 1.0] = 1; FM[(ring >= 1.0) & (ring < 2.2) & (FM == 0)] = 2
    star = np.where(np.cos(4 * th) > 0, 6 + 10 * np.cos(4 * th) ** 4, 6 + 5 * np.abs(np.cos(4 * th)))
    FM[np.abs(rr_ - star) < 0.8] = 1; FM[rr_ < 2.2] = 1
    for a_ in (0.25, 0.75, 1.25, 1.75):
        px_, py_ = cc + 24.5 * math.cos(a_ * math.pi), cc + 24.5 * math.sin(a_ * math.pi) / 1.12
        FM[np.hypot(xx - px_, yy - py_) < 1.6] = 1
    R = np.minimum(1.0, rr_ / 26)
    FLOORC = [np.array(c, float) for c in ((255, 214, 90), (255, 140, 40), (232, 60, 30), (170, 34, 22), (110, 18, 16))]
    sy_, sx_ = np.nonzero(FM == 1); SP = list(zip(sx_[::23], sy_[::23]))
    ry = cc + CELL / 3 + 6 - CELL / 6                       # 한 장짜리 그림의 칸 아래쪽 불꽃 줄과 같은 높이
    out = []; embers = []; lr = np.random.default_rng(5)
    for f in range(24):
        U = np.zeros((C, C, 4))
        age = f - 1.2 * R
        on = (FM > 0) & (age >= 0) & (age < 20)
        ci = np.clip(np.clip(age / 4.0, 0, 4).astype(int) + (FM == 2), 0, 4)
        flick = np.random.default_rng(f * 13 + 1).random((C, C))
        on &= ~((age > 10) & (flick < (age - 10) / 10))
        for c in range(5):
            sel = on & (ci == c); U[sel, :3] = FLOORC[c]; U[sel, 3] = 255
        for i, (sx, sy) in enumerate(SP):
            a_ = f - 1.2 * min(1, math.hypot(sx - cc, (sy - cc) * 1.12) / 26) - (i % 3) * 0.5
            if a_ >= 0: flame(U, sx, sy, int(a_ * 1.2), i, f, 0.42)
        for i, x in enumerate(np.arange(cc - CELL / 2 + 3, cc + CELL / 2 - 2, 11)):
            a_ = f - ((i * 7) % 3) * 0.5
            if a_ >= 0:
                flame(U, x + math.sin(i * 2.3) * 2, ry, int(a_), i, f, (1.15 if i % 4 == 0 else 0.8) * 0.75)
                if a_ < 8 and (i * 3 + f) % 5 == 0:
                    embers.append([x, ry - 10, lr.uniform(-.4, .4), lr.uniform(-2.0, -0.8), int(lr.integers(4, 8)), [GOLD, ORANGE, FLC[0]][lr.integers(3)]])
        L = np.zeros((C, C, 4))
        if 3 <= f < 16 and f % 4 == 1:                         # 금빛 반짝임 하나
            x, y = cc + lr.uniform(-22, 22), cc + lr.uniform(-22, 18)
            for d_ in (1, 2):
                for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)): _px(L, x + ddx * d_, y + ddy * d_, GOLD if d_ == 2 else np.array([255, 245, 200.]))
            _px(L, x, y, np.array([255, 255, 255.]))
        finish(L, f, None, [], embers)
        m = L[..., 3] > 0; U[m] = L[m]
        out.append(Image.fromarray(U.clip(0, 255).astype(np.uint8)))
    while out and not np.array(out[-1])[..., 3].any(): out.pop()
    return out


def wave_fire():
    # 대시 파동 범위 안 몹 위에 붙는 불(게임 OnDashWaveClaw, 전설 발톱 자리): 128판 가운데 = 몹 가운데. 터짐 + 몸을 감싸는 불꽃
    out = []; embers = []
    for f in range(22):
        U = np.zeros((128, 128, 4))
        ignite(U, 64, 68, f, embers)
        for i, dx in enumerate((-14, -6, 2, 10, 16)):
            if f >= 1 + i % 3: flame(U, 64 + dx, 84 - abs(dx) * 0.3, f - 1 - i % 3, i, f, 0.9)
        finish(U, f, None, [], embers)
        out.append(Image.fromarray(U.clip(0, 255).astype(np.uint8)))
    return out


def diag(n):
    # 분신 대시(대각선) 전용: skill_diag와 같은 판(오른쪽 위 /), 붉은 속도선을 45도로 + 용이 대각선 길을 따라 날아가 끝에서 불꽃
    import skill_diag as SD
    base, C = SD.base_rot(G, n)
    R2 = math.sqrt(2); L_ = n * R2 * CELL; c0 = C / 2
    pt = lambda u: (c0 + u / R2, c0 - u / R2)
    a, b_ = -L_ / 2 - 8, L_ / 2 - 0.35 * CELL * R2
    pts = []
    for u in np.linspace(0, 1, 160):
        d = a + (b_ - a) * u; w = 6 * math.sin(u * math.pi * (1 + n * 0.5))
        x, y = pt(d); pts.append((x + w / R2, y + w / R2))
    P = Path(pts); T = 4 + n; ex, ey = pt(b_ + 6)
    def extra(L, f, embers): burst(L, ex, ey, f - T + 1, embers)
    return compose(base, (C, C), (0, 0), P, T, BODY=min(78, 40 + n * 14), extra=extra, mouth=lambda t: 0.5 < t < 1)


if __name__ == "__main__":
    print("spin", SL.save(spin(), "hq_spin_g5"), "cross", SL.save(cross(), "hq_spin_cross_g5"),
          "dash", [SL.save(dash(n), f"hq_dash_g5_{n}") for n in (1, 2, 3, 4)])
