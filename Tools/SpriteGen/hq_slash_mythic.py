# 신화 베기: pixelfx 붉은 베기(hq_slash_g5 바탕) 위로 붉은 용이 칼 궤적을 타고 날아가 몹을 한 바퀴 휘감고 불씨로 흩어진다
import os, math, shutil, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
C = 192; OX, OY = 96, 122          # pixelfx 원점(발) -> 판 좌표
BASE = os.path.join(S, "pfxframes", "hq_slash_g5")
BAK = os.path.join(S, "pfxframes", "hq_slash_g5_old")
if not os.path.exists(BAK):
    shutil.copytree(BASE, BAK)
base = [np.array(Image.open(os.path.join(BAK, f"{i}.png")).convert("RGBA")).astype(float) for i in range(len(os.listdir(BAK)))]
NF = 26
rng = np.random.default_rng(5)

# ---- 길: pixelfx 호(a0 -> a1) + 몹 둘레를 도는 나선
cx, cy, R, sy, rot = 6, -26, 42, 0.6, -0.38
cr, sr = math.cos(rot), math.sin(rot)


def arc(a):
    lx, ly = math.cos(a) * R, math.sin(a) * R * sy
    return OX + cx + lx * cr - ly * sr, OY + cy + lx * sr + ly * cr


ACEN = (OX + cx, OY + cy)
pts, cens = [], []
for a in np.linspace(-2.55, 0.95, 140):
    pts.append(arc(a)); cens.append(ACEN)
EC = (OX + 60, OY - 24)             # 몹 가운데
tx, ty = pts[-1]
a_s = math.atan2(ty - EC[1], tx - EC[0]); r_s = math.hypot(tx - EC[0], ty - EC[1])
for k in np.linspace(0, 1, 160)[1:]:
    ang = a_s - k * 2 * math.pi * 0.95          # 아래 -> 오른쪽 -> 위로 감음
    rr = r_s * (1 - 0.3 * k)
    pts.append((EC[0] + math.cos(ang) * rr, EC[1] + math.sin(ang) * rr)); cens.append(EC)
pts = np.array(pts); cens = np.array(cens)
seg = np.r_[0, np.cumsum(np.hypot(*np.diff(pts, axis=0).T))]; TOT = seg[-1]


def at(s):
    s = min(max(s, 0), TOT); i = min(np.searchsorted(seg, s), len(seg) - 1); i = max(i, 1)
    f = (s - seg[i - 1]) / max(1e-6, seg[i] - seg[i - 1]); p = pts[i - 1] * (1 - f) + pts[i] * f
    t = pts[i] - pts[i - 1]; t = t / (np.linalg.norm(t) + 1e-9)
    out = p - cens[i]; out = out / (np.linalg.norm(out) + 1e-9)
    return p, t, out


# ---- 색
OL = np.array([46, 6, 10.]); DK = np.array([120, 16, 22.]); MD = np.array([196, 34, 30.]); LT = np.array([232, 52, 40.]); HL = np.array([255, 118, 84.])
BEL = np.array([240, 170, 64.]); BELD = np.array([186, 104, 36.]); GOLD = np.array([255, 196, 70.]); HORN = np.array([250, 226, 160.])
EYE = np.array([255, 240, 90.]); ORANGE = np.array([255, 130, 40.])


def disc(L, x, y, r, out):
    x0, x1 = int(x - r - 1), int(x + r + 2); y0, y1 = int(y - r - 1), int(y + r + 2)
    for yy in range(max(0, y0), min(C, y1)):
        for xx in range(max(0, x0), min(C, x1)):
            dx, dy = (xx + .5 - x) / r, (yy + .5 - y) / r
            if dx * dx + dy * dy > 1:
                continue
            side = dx * out[0] + dy * out[1]            # +바깥(등) / -안쪽(배)
            lit = -(dx * 0.45 + dy * 0.9)                # 왼위에서 빛
            if side < -0.6:
                col = BEL if lit > -0.2 else BELD
            elif lit > 0.55:
                col = HL
            elif lit > 0.1:
                col = LT
            elif lit > -0.45:
                col = MD if (xx + yy * 2) % 5 else DK    # 비늘 점
            else:
                col = DK
            L[yy, xx, :3] = col; L[yy, xx, 3] = 255


def line(L, p, q, col):
    n = int(max(abs(q[0] - p[0]), abs(q[1] - p[1]))) + 1
    for k in range(n + 1):
        x = p[0] + (q[0] - p[0]) * k / n; y = p[1] + (q[1] - p[1]) * k / n
        xi, yi = int(x), int(y)
        if 0 <= xi < C and 0 <= yi < C:
            L[yi, xi, :3] = col; L[yi, xi, 3] = 255


def tri(L, a, b, c_, col):
    xs = [a[0], b[0], c_[0]]; ys = [a[1], b[1], c_[1]]

    def sg(p1, p2, p3):
        return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])
    for yy in range(int(min(ys)), int(max(ys)) + 1):
        for xx in range(int(min(xs)), int(max(xs)) + 1):
            p = (xx + .5, yy + .5)
            d1, d2, d3 = sg(p, a, b), sg(p, b, c_), sg(p, c_, a)
            if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)) and 0 <= xx < C and 0 <= yy < C:
                L[yy, xx, :3] = col; L[yy, xx, 3] = 255


BODY = 78.0
embers = []   # [x, y, vx, vy, life, col]
frames = []
for f in range(NF):
    bi = f + 2                             # 11-06 사용자 "공격하면 바로 용이 나가게": 앞 빈 2장 없이 바로 시작
    img = base[bi].copy() if bi < len(base) else np.zeros((C, C, 4))
    L = np.zeros((C, C, 4)); pearl = None; segs_glint = []
    t = (f + 1) / 12                       # 첫 장부터 용이 칼끝에서 튀어나와 11장째 다 감음
    head_s = TOT * (1 - (1 - min(max(t, 0), 1)) ** 2.2)
    fade = max(0, (f - 12) / 9)            # 12장부터 꼬리부터 불씨로 흩어짐
    if head_s > 0:
        n = 52
        segs = []
        for j in range(n):                 # 꼬리 -> 머리
            k = j / (n - 1); s_ = head_s - BODY * (1 - k)
            if s_ < 0:
                continue
            p, tg, out = at(s_)
            wav = 2.6 * math.sin(s_ / 9.0 - f * 1.4) * (1 - k * 0.7)   # 몸통을 타고 흐르는 꿈틀
            q = p + out * wav
            r = 1.5 + 4.8 * k ** 0.5
            if k > 0.9:
                r *= 0.82                  # 목
            if fade and k < fade * 1.3:    # 흩어짐: 꼬리부터 비고 불씨
                if rng.random() < 0.5:
                    embers.append([q[0], q[1], rng.uniform(-.6, .6), rng.uniform(-1.6, -.5), int(rng.integers(4, 9)), [GOLD, LT, HL][rng.integers(3)]])
                continue
            segs.append((j, k, q, tg, out, r))
        def leg(q, tg, out, r, ph, far):
            # 용 다리(이무기와 다른 점): 어깨 -> 팔꿈치 -> 발, 금빛 발톱 셋. 걷듯이 앞뒤로 휘젓는다
            sw = math.sin(f * 1.3 + ph)
            col, dk = (DK, OL) if far else (MD, DK)
            sh = q - out * (r * 0.5) + tg * (0.8 if far else -0.8)
            el = sh - out * 4.5 + tg * (-2.5 + 2.5 * sw)
            ft = el - out * 1.5 + tg * (3.0 + 1.5 * sw)
            for a, b in ((sh, el), (el, ft)):
                line(L, a, b, col); line(L, a + tg * 0.9, b + tg * 0.9, dk)
            for cl in (-1, 0, 1):
                line(L, ft, ft + tg * 2.2 - out * (1.0 * cl + 0.6), HORN)
        LEGS = {int(n * 0.80), int(n * 0.45)}
        for (j, k, q, tg, out, r) in segs:                 # 먼 쪽 다리(몸 뒤)
            if j in LEGS: leg(q, tg, out, r, j * 0.7 + 1.6, True)
        if segs and segs[0][1] < 0.2:                      # 꼬리 끝 불꽃 술
            j, k, q, tg, out, r = segs[0]
            for u in range(3):
                fl = 4 + 2 * ((f + u) % 3)
                bb = q - tg * 1.0
                tri(L, bb + out * (u - 1) * 1.6, bb + out * ((u - 1) * 1.6 + 1.4), bb - tg * fl + out * (u - 1) * 2.4, np.array([255, 90 + 50 * u, 40.]))
        for (j, k, q, tg, out, r) in segs:
            disc(L, q[0], q[1], r, out)
            if j % 2 == 0 and k < 0.93:    # 등 불꽃 갈기: 일렁이는 불꽃 혀(바깥 빨강 -> 안 노랑)
                fl = (2.4 + 2.6 * (0.5 + 0.5 * math.sin(j * 1.7 + f * 2.1))) * (0.5 + k * 0.7)
                b1 = q + out * (r - .6) - tg * 1.4; b2 = q + out * (r - .6) + tg * 1.4; tp = q + out * (r + fl) - tg * 2.0
                tri(L, b1, b2, tp, np.array([240, 60, 30.]))
                tri(L, q + out * (r - .3) - tg * .7, q + out * (r - .3) + tg * .7, q + out * (r + fl * .55) - tg * 1.2, GOLD)
        for (j, k, q, tg, out, r) in segs:                 # 가까운 쪽 다리(몸 앞)
            if j in LEGS: leg(q, tg, out, r, j * 0.7, False)
        # 머리
        if not (fade and fade * 1.3 > 1.0):
            p, tg, up = at(head_s)
            hp = p
            for sgn, ln in ((1, 13), (0.4, 10)):   # 사슴뿔처럼 갈라진 뿔 두 개(2칸 굵기) - 머리보다 먼저(뒤에)
                a = hp - tg * 1.0 + up * 4.2; b = a - tg * ln + up * (3.5 * sgn + 2)
                line(L, a, b, HORN); line(L, a + up * 0.9, b + up * 0.9, np.array([210, 170, 110.]))
                for fr_ in (0.45, 0.75):       # 가지
                    m_ = a + (b - a) * fr_
                    line(L, m_, m_ + up * (3.5 - fr_ * 2) + tg * 0.8, HORN)
            disc(L, hp[0], hp[1], 6.3, up)    # 머리통
            for u in range(1, 11):             # 위턱(주둥이)
                c_ = hp + tg * u + up * 1.3; disc(L, c_[0], c_[1], 4.3 - u * 0.22, up)
            jaw = 1.6 + 1.4 * max(0, math.sin(f * 1.1))
            for u in range(2, 9):              # 아래턱
                c_ = hp + tg * u - up * (2.2 + jaw * u / 9); disc(L, c_[0], c_[1], 1.7, up)
            for u in np.linspace(3, 10, 12):   # 벌린 입(검붉은 틈)
                q = hp + tg * u - up * (0.6 + jaw * u / 10 * 0.6)
                for w in range(int(1 + jaw * u / 10)):
                    xx, yy = int(q[0] - up[0] * w), int(q[1] - up[1] * w)
                    if 0 <= xx < C and 0 <= yy < C: L[yy, xx, :3] = (70, 8, 14); L[yy, xx, 3] = 255
            for u in (5, 7, 9):                # 이빨
                q = hp + tg * u - up * 0.5; xx, yy = int(q[0]), int(q[1])
                if 0 <= xx < C and 0 <= yy < C: L[yy, xx, :3] = (255, 250, 235); L[yy, xx, 3] = 255
            e = hp + tg * 2.0 + up * 2.2       # 눈 2x2 + 동공
            for oy in range(2):
                for ox in range(2):
                    q = e + tg * ox + up * (-oy); xx, yy = int(q[0]), int(q[1])
                    if 0 <= xx < C and 0 <= yy < C: L[yy, xx, :3] = EYE; L[yy, xx, 3] = 255
            q = e + tg * 1; xx, yy = int(q[0]), int(q[1])
            if 0 <= xx < C and 0 <= yy < C: L[yy, xx, :3] = (20, 0, 0)
            for sgn in (1, -1):                # 수염 한 가닥씩(주둥이 끝에서 뒤로 길게 흩날림)
                prev = hp + tg * 9 - up * 0.5 * sgn
                for u in range(1, 18):
                    q = hp + tg * (9 - u * 1.4) - up * (sgn * 2.5 + 3.0 * math.sin(u * 0.5 - f * 1.5) * (u / 17)) - up * 2.5
                    line(L, prev, q, GOLD if u < 10 else ORANGE); prev = q
            for u in range(4):             # 갈기(머리 뒤 불꽃 털)
                b = hp - tg * (2 + u * 2.2) + up * 2.5
                tri(L, b - tg * 1.2, b + tg * 1.2, b + up * (3.5 + (u + f) % 2 * 1.5) - tg * 2.2, np.array([255, 120 + 30 * (u % 2), 40.]))
            pearl = (hp + tg * 16 + up * 1.0) if t < 0.97 else None
            if 0.42 < t < 0.9:             # 입김 불꽃: 칼끝 지나 몹을 감기 시작할 때
                m = hp + tg * 8
                for _ in range(4):
                    v = tg * rng.uniform(1.5, 3.0) + up * rng.uniform(-.8, .8)
                    embers.append([m[0], m[1], v[0], v[1], int(rng.integers(3, 6)), [EYE, GOLD, ORANGE][rng.integers(3)]])
        if 0 < f < 13 and (L[..., 3] > 0).any():   # 몸에서 불씨
            ys_, xs_ = np.nonzero(L[..., 3] > 0)
            for _ in range(3):
                i_ = rng.integers(len(xs_))
                embers.append([float(xs_[i_]), float(ys_[i_]), rng.uniform(-.4, .4), rng.uniform(-1.2, -.4), int(rng.integers(3, 7)), [GOLD, LT][rng.integers(2)]])
        segs_glint = [q for (j, k, q, tg, out, r) in segs if j % 9 == (f % 9)]
        a_ = L[..., 3] > 0                 # 외곽선 1칸
        ring = np.zeros_like(a_)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ring |= np.roll(np.roll(a_, dy, 0), dx, 1)
        ring &= ~a_
        L[ring, :3] = OL; L[ring, 3] = 255
    def px(x, y, col):
        x, y = int(x), int(y)
        if 0 <= x < C and 0 <= y < C: L[y, x, :3] = col; L[y, x, 3] = 255
    if pearl is not None:                  # 여의주: 불꽃 두른 빛나는 구슬
        pxx, pyy = pearl
        for yy in range(int(pyy) - 8, int(pyy) + 9):     # 성긴 빛무리(체크무늬)
            for xx in range(int(pxx) - 8, int(pxx) + 9):
                d_ = math.hypot(xx + .5 - pxx, yy + .5 - pyy)
                if 4.2 < d_ < 7.5 and (xx + yy + f) % 2 == 0 and L[min(C - 1, max(0, yy)), min(C - 1, max(0, xx)), 3] == 0:
                    px(xx, yy, np.array([255, 150, 50.]) if d_ < 5.8 else np.array([200, 60, 30.]))
        for u in range(6):                 # 둘레 불꽃
            ang = u / 6 * 2 * math.pi + f * 0.5; dv = np.array([math.cos(ang), math.sin(ang)]); tv = np.array([-dv[1], dv[0]])
            c0 = np.array(pearl) + dv * 3.0
            tri(L, c0 - tv * 1.3, c0 + tv * 1.3, c0 + dv * (3 + (u + f) % 2 * 2) + tv * 1.2, np.array([255, 120 + 40 * (u % 2), 40.]))
        for yy in range(int(pyy) - 4, int(pyy) + 5):
            for xx in range(int(pxx) - 4, int(pxx) + 5):
                d_ = math.hypot(xx + .5 - pxx, yy + .5 - pyy)
                if d_ <= 3.6:
                    col = np.array([255, 255, 240.]) if d_ < 1.3 else np.array([255, 226, 110.]) if d_ < 2.5 else np.array([255, 140, 50.])
                    px(xx, yy, col)
        px(pxx - 1, pyy - 2, np.array([255, 255, 255.]))
    for q in segs_glint:                   # 금빛 반짝(4방향 별)
        x0, y0 = q[0] + rng.uniform(-3, 3), q[1] + rng.uniform(-3, 3)
        for d_ in (1, 2):
            for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                px(x0 + ddx * d_, y0 + ddy * d_, GOLD if d_ == 2 else np.array([255, 245, 200.]))
        px(x0, y0, np.array([255, 255, 255.]))
    alive = []                             # 불씨 움직이기·그리기
    for e in embers:
        e[0] += e[2]; e[1] += e[3]; e[3] -= 0.05; e[4] -= 1
        if e[4] > 0:
            alive.append(e); x, y = int(e[0]), int(e[1])
            if 0 <= x < C and 0 <= y < C:
                L[y, x, :3] = e[5] if e[4] > 2 else np.array([150, 40, 30.]); L[y, x, 3] = 255
    embers = alive
    m = L[..., 3] > 0
    img[m] = L[m]
    frames.append(Image.fromarray(img.clip(0, 255).astype(np.uint8), "RGBA"))
while len(frames) > 3 and np.array(frames[-1])[..., 3].sum() < 255 * 3:
    frames.pop()
for fn in os.listdir(BASE):
    os.remove(os.path.join(BASE, fn))
for i, fr in enumerate(frames):
    if np.array(fr)[..., 3].sum() == 0:
        fr.putpixel((0, 0), (0, 0, 0, 1))
    fr.save(os.path.join(BASE, f"{i}.png"))
print("frames", len(frames))
