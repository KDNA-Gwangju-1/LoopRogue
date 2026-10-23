"""9층 리치 도트 리드로우(10-23) - 떠다니는 해골 마법사: 금관 두건·뼈 견갑·갈비뼈 보이는 앞섶·누더기 망토 끝·영혼불 지팡이.
python draw_boss9.py [Sprites/Boss9 폴더] - 폴더를 안 주면 boss9_preview.png만 만든다
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import (OUT, Canvas, ellipse, fill, glow, line, outline_outside, part, poly, shadow, sheet, shift,
                 thick_line)

ROBE = [(30, 18, 44), (50, 30, 74), (74, 46, 106), (100, 70, 138)]
CAPE = [(22, 13, 32), (36, 22, 54), (54, 34, 80), (70, 46, 100)]
VOID = (12, 8, 18)
BONE = [(118, 108, 92), (168, 160, 138), (212, 204, 182), (240, 236, 218)]
GOLD = [(116, 76, 28), (172, 124, 48), (220, 176, 80), (255, 228, 140)]
BONE_D = [(92, 84, 72), (136, 128, 110), (180, 172, 150), (214, 208, 188)]
WOOD = [(44, 28, 22), (72, 48, 36), (104, 74, 54), (136, 102, 74)]
SOUL = [(30, 120, 70), (60, 200, 110), (130, 255, 170), (215, 255, 228)]

TEETH = [3, 1, 4, 2, 5, 1, 3, 2, 4, 1, 2, 5]  # 망토 끝 누더기 길이(3칸마다)


def hem(x, base, ph, amp=1.0):
    i = int(math.floor((x + ph) / 3)) % len(TEETH)
    frac = ((x + ph) % 3) / 3
    t = TEETH[i] * (1 - abs(frac - 0.5) * 1.2)
    return base + int(round(t * amp))


def tattered(pts, base, ph, amp=1.0):
    """다각형 몸통에서 누더기 끝 아래를 깎는다."""
    m = poly(pts)
    return {(x, y) for x, y in m if y <= hem(x, base, ph, amp)}


def soul_fire(cv, cx, cy, r, f, bright=1.0):
    """지팡이 끝 영혼불 - 구슬 + 위로 일렁이는 불꽃."""
    glow(cv, cx, cy, r + 4 + bright * 2, SOUL[1], 0.35 * bright)
    orb = ellipse(cx, cy, r, r)
    part(cv, orb, [SOUL[0], SOUL[1], SOUL[2], SOUL[3]], 'sphere', outline=(14, 46, 30))
    # 불꽃 혀(프레임마다 흔들림)
    sway = [0, 1, 1, 0, -1, -1, 0, 1][f % 8]
    h = int(r + 3 + bright * 2)
    for k in range(h):
        y = int(cy - r - k)
        wdt = max(0, int((h - k) / h * r) - (1 if k > h * 0.6 else 0))
        ox = int(round(sway * k / h * 1.5))
        for x in range(int(cx) - wdt + ox, int(cx) + wdt + ox + 1):
            c = SOUL[3] if abs(x - cx - ox) < wdt * 0.4 and k < h * 0.5 else SOUL[2] if k < h * 0.75 else SOUL[1]
            cv.blend(x, y, c, 0.9 if k < h * 0.7 else 0.6)


def wisps(cv, cx, cy, f, n=3, rad=20, ry=0.45, bright=1.0, front=None):
    """몸 둘레를 도는 영혼 불티. front=True면 앞쪽(아래 반)만, False면 뒤쪽만."""
    for i in range(n):
        a = (f / 8 + i / n) * math.tau
        x, y = cx + math.cos(a) * rad, cy + math.sin(a) * rad * ry
        if front is not None and (math.sin(a) > 0) != front:
            continue
        cv.blend(int(x), int(y), SOUL[3], 0.95 * bright)
        cv.blend(int(x) + 1, int(y), SOUL[2], 0.6 * bright)
        cv.blend(int(x) - 1, int(y), SOUL[2], 0.6 * bright)
        cv.blend(int(x), int(y) - 1, SOUL[2], 0.6 * bright)
        cv.blend(int(x), int(y) + 1, SOUL[1], 0.4 * bright)
        tx = int(x - math.cos(a + math.pi / 2) * 2)
        cv.blend(tx, int(y + 1), SOUL[1], 0.35 * bright)


def staff(cv, top, grip, length=46):
    """지팡이 - top(구슬 받침 위치)에서 grip을 지나 length까지. 맨 위는 뼈 갈퀴 받침."""
    dx, dy = grip[0] - top[0], grip[1] - top[1]
    L = math.hypot(dx, dy) or 1
    ux, uy = dx / L, dy / L
    bot = (top[0] + ux * length, top[1] + uy * length)
    shaft = thick_line(top[0], top[1], bot[0], bot[1], 4)
    part(cv, shaft, WOOD, 'span')
    nx, ny = -uy, ux
    # 뼈 마디(감긴 띠) 두 곳
    for t in (12, 30):
        bx, by = top[0] + ux * t, top[1] + uy * t
        part(cv, thick_line(bx - nx * 2.5, by - ny * 2.5, bx + nx * 2.5, by + ny * 2.5, 3), BONE, 'sphere')
    # 갈퀴 받침 - 구슬을 감싸는 뼈 손가락
    claw = set()
    for s in (-1, 1):
        bx, by = top[0] + nx * s * 1.5, top[1] + ny * s * 1.5
        mid = (bx - ux * 3 + nx * s * 3.5, by - uy * 3 + ny * s * 3.5)
        tip = (bx - ux * 8 + nx * s * 2, by - uy * 8 + ny * s * 2)
        claw |= thick_line(bx, by, mid[0], mid[1], 3) | thick_line(mid[0], mid[1], tip[0], tip[1], 2)
    claw |= ellipse(top[0], top[1], 2.5, 2.5)
    part(cv, claw, BONE, 'sphere')
    return (top[0] - ux * 4, top[1] - uy * 4)  # 구슬 중심


def skull_front(cv, cx, cy, f, flare=0.0, look=0):
    """두건 속 해골 얼굴(정면)."""
    head = ellipse(cx, cy, 5, 4.6)
    jaw = poly([(cx - 3.5, cy + 2), (cx + 3.5, cy + 2), (cx + 2.5, cy + 6), (cx - 2.5, cy + 6)])
    part(cv, head | jaw, BONE, 'sphere', light=(-0.3, -0.5))
    # 눈구멍 + 초록 눈빛
    for ex in (cx - 3, cx + 1):
        ex = int(ex) + look
        for p in ((ex, cy), (ex + 1, cy), (ex, cy + 1), (ex + 1, cy + 1), (ex, cy - 1) if ex < cx else (ex + 1, cy - 1)):
            cv.set(int(p[0]), int(p[1]), VOID)
        cv.set(ex + (1 if ex < cx else 0), int(cy), SOUL[3])
        cv.set(ex + (0 if ex < cx else 1), int(cy) + 1, SOUL[2])
        glow(cv, ex + 1, cy + 1, 2 + flare * 2, SOUL[2], 0.2 + 0.2 * flare)
    # 코 구멍, 이빨
    cv.set(int(cx), int(cy) + 2, VOID)
    cv.set(int(cx) - 1, int(cy) + 3, BONE[0])
    for x in range(int(cx) - 2, int(cx) + 3):
        cv.set(x, int(cy) + 4, VOID if x % 2 else BONE[3])
    cv.set(int(cx) - 3, int(cy) + 3, BONE[0])
    cv.set(int(cx) + 3, int(cy) + 3, BONE[0])


def crown(cv, cx, top, back=False):
    """금관 - 다섯 뿔, 가운데 초록 보석."""
    band = poly([(cx - 7, top + 5), (cx + 7, top + 5), (cx + 6, top + 8), (cx - 6, top + 8)])
    spikes = set()
    for sx, h in ((-6, 3), (-3, 5), (0, 7), (3, 5), (6, 3)):
        spikes |= poly([(cx + sx - 1.5, top + 6), (cx + sx + 1.5, top + 6), (cx + sx, top + 6 - h)])
    part(cv, band | spikes, GOLD, 'sphere', light=(-0.4, -0.6))
    if not back:
        for p in ((cx, top + 6), (cx - 1, top + 6), (cx, top + 5), (cx - 1, top + 5)):
            cv.set(int(p[0]), int(p[1]), SOUL[2])
        cv.set(int(cx) - 1, int(top + 5), SOUL[3])
    for sx in (-4, 4):
        cv.set(int(cx + sx), int(top + 6), (150, 30, 50) if not back else GOLD[0])


def pauldron(cv, cx, cy, side):
    """뼈 견갑 - 겹친 뼈판 두 장 + 바깥으로 휘어 솟은 뿔, 바깥 판엔 해골 눈구멍."""
    horn = thick_line(cx + side * 2, cy - 2, cx + side * 5, cy - 6, 3) | thick_line(cx + side * 5, cy - 6, cx + side * 5, cy - 9, 2)
    part(cv, horn, BONE, 'sphere')
    m = ellipse(cx, cy, 5.5, 4) | ellipse(cx + side * 1.5, cy + 2.5, 4.5, 3)
    part(cv, m, BONE_D, 'sphere', light=(-0.3, -0.5))
    for x in range(int(cx - 4), int(cx + 5)):
        if (x, int(cy + 1)) in m and (x, int(cy + 2)) in m:
            cv.set(x, int(cy + 1), BONE_D[0])
    ex = int(cx + side * 1.5)
    cv.set(ex - 1, int(cy) + 3, VOID)
    cv.set(ex + 1, int(cy) + 3, VOID)


def hand(cv, x, y, side, open_=False):
    """해골 손 - 손바닥 + 손가락 마디(외곽선 포함)."""
    m = ellipse(x, y, 2.2, 2)
    if open_:
        for k in range(4):
            m |= set(line(x - 2 + k * 1.4, y - 1, x - 2.5 + k * 1.7, y - 4 - (k % 2)))
    else:
        for k in range(3):
            m |= set(line(x - 1 + k, y + 1, x - 1 + k + side, y + 3))
    for p in m:
        cv.set(p[0], p[1], BONE[2] if (p[0] + p[1]) % 3 else BONE[1])
    outline_mask(cv, m)


def outline_mask(cv, m):
    for x, y in m:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            if q not in m and not cv.opaque(*q):
                cv.set(q[0], q[1], OUT)


def ribcage(cv, cx, top, bot):
    """앞섶 속 - 어둠 + 갈비뼈(척추에서 아래로 휘어 나감) + 아래로 새는 영혼빛."""
    glow(cv, cx, bot - 6, 7, SOUL[1], 0.5)
    for y in range(top + 1, top + 13):
        cv.set(cx, y, BONE[2] if y % 2 else BONE[1])
    for i, y in enumerate(range(top + 3, top + 12, 2)):
        w = [3, 4, 4, 3, 2][i]
        for d in range(1, w + 1):
            yy = y + (1 if d >= 3 else 0)
            for sgn in (-1, 1):
                cv.set(cx + sgn * d, yy, BONE[2] if d < 3 else BONE[1])


def lich_south(f, bob=0, top=(52, 9), grip=(52, 35), orb=2.5, fire=1.0, flare=0.0, burst=None, sway=0.0,
               hand2=(15, 41), lean=0, wisp=1.0, hem_amp=1.0):
    cv = Canvas()
    b = bob
    shadow(cv, 32, 59, 12 - abs(b) * 0.5, 2.6)
    wisps(cv, 32, 40 + b, f, 3, 22, 0.4, wisp, front=False)
    ph = f * 0.75 + sway
    # 뒤 망토(어둡고 더 길게 끌림)
    cape = tattered([(22, 24 + b), (42, 24 + b), (51, 44 + b), (53, 62 + b), (11, 62 + b), (13, 44 + b)], 53 + b, ph + 1, hem_amp)
    part(cv, cape, CAPE, 'span')
    # 몸통 - 허리에서 잘록, 아래로 퍼지는 치마
    body = tattered([(24, 25 + b), (40, 25 + b), (39, 37 + b), (47, 60 + b), (17, 60 + b), (25, 37 + b)], 49 + b, ph, hem_amp * 0.8)
    part(cv, body, ROBE, 'span', extra=lambda x, y: -0.15 if y < 31 + b else 0)
    for fx, y0 in ((24, 42), (40, 42), (28, 46), (36, 46)):
        for y in range(y0 + b, 54 + b):
            x = fx + int(round((fx - 32) * (y - y0 - b) / 24))
            if (x, y) in body and (x, y + 1) in body and (x - 1, y) in body:
                cv.set(x, y, ROBE[0])
    # 앞섶 - 금 테두리 사이 어둠 + 갈비뼈
    opening = poly([(30, 25 + b), (34, 25 + b), (37, 54 + b), (27, 54 + b)])
    opening = {p for p in opening if p in body and p not in boundary_of(body)}
    fill(cv, opening, VOID)
    ribcage(cv, 32, 25 + b, 50 + b)
    for side in (-1, 1):
        for p in line(32 + side * 2.5, 25 + b, 32 + side * 5.5, 53 + b):
            if (p[0], p[1]) in body:
                cv.set(p[0], p[1], GOLD[2] if side < 0 else GOLD[1])
                if (p[0] + side, p[1]) in body:
                    cv.set(p[0] + side, p[1], GOLD[0])
    # 허리 뼈 사슬 + 매달린 성물
    for x in range(25, 40):
        y = 38 + b + int(round(((x - 32) / 7) ** 2 * 1.5))
        if (x, y) in body:
            cv.set(x, y, BONE[2] if x % 3 else BONE[0])
    gem = poly([(32, 39 + b), (34.5, 42 + b), (32, 45 + b), (29.5, 42 + b)])
    part(cv, gem, SOUL, 'sphere', outline=GOLD[0])
    glow(cv, 32, 42 + b, 4, SOUL[2], 0.4 * fire)
    # 오른소매(화면 오른쪽) - 지팡이 쥔 팔, 종 모양 소매
    sr = poly([(40, 25 + b), (46, 26 + b), (grip[0] + 3, grip[1] - 1), (grip[0] - 1, grip[1] + 3), (grip[0] - 6, grip[1] + 1), (40, 32 + b)])
    part(cv, sr, ROBE, 'span')
    oc = staff(cv, top, grip)
    hand(cv, grip[0], grip[1], -1)
    # 왼소매 - 늘어진 종 소매 + 영혼빛을 쥔 손
    sl = poly([(24, 25 + b), (18, 26 + b), (hand2[0] - 5, hand2[1]), (hand2[0] + 1, hand2[1] + 2), (hand2[0] + 6, hand2[1] - 1), (25, 32 + b)])
    part(cv, sl, ROBE, 'span')
    for p in line(hand2[0] - 5, hand2[1], hand2[0] + 1, hand2[1] + 2) + line(hand2[0] + 1, hand2[1] + 2, hand2[0] + 6, hand2[1] - 1):
        if p in sl:
            cv.set(p[0], p[1], GOLD[1])
    hand(cv, hand2[0] + 1, hand2[1] + 3, 1, open_=False)
    # 견갑
    pauldron(cv, 19, 25 + b, -1)
    pauldron(cv, 45, 25 + b, 1)
    # 두건(뾰족한 끝) + 얼굴 + 금관
    hood = ellipse(32, 16 + b, 7.5, 8.5) | poly([(25, 15 + b), (39, 15 + b), (42, 23 + b), (43, 28 + b), (21, 28 + b), (22, 23 + b)])
    part(cv, hood, ROBE, 'sphere', light=(-0.35, -0.5))
    for p in line(26, 26 + b, 32, 28 + b) + line(32, 28 + b, 38, 26 + b):
        cv.set(p[0], p[1], ROBE[0])
    face = ellipse(32, 19.5 + b, 6, 7)
    fill(cv, face, VOID)
    skull_front(cv, 32, 18 + b, f, flare)
    crown(cv, 32, 4 + b)
    soul_fire(cv, oc[0], oc[1], orb, f, fire)
    if burst:
        ring(cv, *burst)
    wisps(cv, 32, 40 + b, f, 3, 22, 0.4, wisp, front=True)
    return cv


def boundary_of(m):
    from pix import boundary
    return boundary(m)


def ring(cv, cx, cy, r):
    """영혼 폭발 고리 + 안쪽 섬광."""
    glow(cv, cx, cy, r, SOUL[2], 0.55)
    for a in range(0, 360, 6):
        x = cx + math.cos(math.radians(a)) * r
        y = cy + math.sin(math.radians(a)) * r * 0.75
        cv.blend(int(x), int(y), SOUL[3], 0.9)
    for a in range(0, 360, 45):
        x = cx + math.cos(math.radians(a + r * 7)) * (r + 2)
        y = cy + math.sin(math.radians(a + r * 7)) * (r + 2) * 0.75
        cv.blend(int(x), int(y), SOUL[2], 0.8)


def frames_south():
    idle, walk, atk = [], [], []
    for f in range(8):
        b = int(round(math.sin(f / 8 * math.tau) * 1.5)) - 1
        idle.append(lich_south(f, bob=b, top=(52, 9 + b), grip=(52, 35 + b), hand2=(15, 40 + b), fire=0.8 + 0.2 * math.sin(f)))
    for f in range(8):
        b = int(round(math.sin(f / 4 * math.tau) * 1.2)) - 1
        walk.append(lich_south(f, bob=b, top=(52, 9 + b), grip=(52, 35 + b), hand2=(15, 40 + b), sway=f * 1.5, hem_amp=1.3))
    # 공격: 0~2 지팡이를 머리 위로 들며 힘 모음, 3~4 앞으로 내리찍어 영혼 폭발, 5~7 돌아옴
    A = [
        dict(bob=-1, top=(50, 7), grip=(50, 33), orb=3, fire=1.2, hand2=(18, 36), flare=0.3),
        dict(bob=-3, top=(46, 6), grip=(48, 28), orb=3.5, fire=1.6, hand2=(20, 28), flare=0.6),
        dict(bob=-4, top=(44, 6), grip=(47, 27), orb=4, fire=2.0, hand2=(22, 26), flare=1.0),
        dict(bob=0, top=(36, 47), grip=(46, 30), orb=3, fire=2.0, hand2=(18, 40), flare=1.0, burst=(33, 51, 6)),
        dict(bob=1, top=(37, 47), grip=(47, 31), orb=2.5, fire=1.5, hand2=(17, 41), flare=0.7, burst=(33, 51, 10)),
        dict(bob=0, top=(44, 38), grip=(49, 32), orb=2.5, fire=1.2, hand2=(16, 41), flare=0.4, burst=(33, 51, 13)),
        dict(bob=-1, top=(51, 14), grip=(52, 35), orb=2.5, fire=1.0, hand2=(15, 40), flare=0.2),
        dict(bob=-1, top=(52, 9), grip=(52, 35), orb=2.5, fire=0.9, hand2=(15, 40)),
    ]
    for f, kw in enumerate(A):
        atk.append(lich_south(f, **kw))
    return idle, walk, atk


def lich_east(f, bob=0, top=(45, 9), grip=(45, 35), orb=2.5, fire=1.0, flare=0.0, burst=None, sway=0.0,
              wisp=1.0, hem_amp=1.0, trail=0):
    """오른쪽을 보는 옆모습. trail = 망토 끝이 뒤(왼쪽)로 끌리는 정도."""
    cv = Canvas()
    b = bob
    shadow(cv, 31, 59, 11 - abs(b) * 0.5, 2.6)
    wisps(cv, 31, 40 + b, f, 3, 20, 0.4, wisp, front=False)
    ph = f * 0.75 + sway
    cape = tattered([(24, 23 + b), (36, 23 + b), (39, 44 + b), (41, 62 + b), (12 - trail, 62 + b), (17 - trail, 44 + b)],
                    53 + b, ph + 1, hem_amp)
    part(cv, cape, CAPE, 'span', light=(0.3, -0.4))
    body = tattered([(26, 25 + b), (37, 25 + b), (38, 37 + b), (43, 60 + b), (19 - trail // 2, 60 + b), (25, 37 + b)],
                    49 + b, ph, hem_amp * 0.8)
    part(cv, body, ROBE, 'span', light=(0.3, -0.4), extra=lambda x, y: -0.15 if y < 31 + b else 0)
    for fx, y0 in ((27, 42), (33, 44)):
        for y in range(y0 + b, 54 + b):
            x = fx - int(round((y - y0 - b) / 4)) * (1 if fx < 30 else 0)
            if (x, y) in body and (x, y + 1) in body and (x - 1, y) in body:
                cv.set(x, y, ROBE[0])
    # 앞자락 금 테두리
    for p in line(37, 26 + b, 42, 52 + b):
        if p in body:
            cv.set(p[0], p[1], GOLD[2])
            if (p[0] - 1, p[1]) in body:
                cv.set(p[0] - 1, p[1], GOLD[0])
    # 허리 뼈 사슬 + 성물(옆에서 보면 앞으로 매달림)
    for x in range(26, 39):
        y = 37 + b + (1 if x > 34 else 0)
        if (x, y) in body:
            cv.set(x, y, BONE[2] if x % 3 else BONE[0])
    gem = poly([(39, 38 + b), (41, 40.5 + b), (39, 43 + b), (37, 40.5 + b)])
    part(cv, gem, SOUL, 'sphere', outline=GOLD[0])
    glow(cv, 39, 40.5 + b, 3.5, SOUL[2], 0.35 * fire)
    # 두건 + 옆얼굴 + 금관
    hood = ellipse(31.5, 16 + b, 8, 8.5) | poly([(24, 15 + b), (39, 15 + b), (41, 24 + b), (39, 28 + b), (23, 28 + b), (23, 22 + b)])
    part(cv, hood, ROBE, 'sphere', light=(0.1, -0.5))
    face = {p for p in ellipse(37, 19.5 + b, 3.4, 6) if p in hood and p not in boundary_of(hood)}
    fill(cv, face, VOID)
    skull = ellipse(37, 17.5 + b, 2.8, 3) | poly([(35, 19 + b), (39.6, 19 + b), (39.6, 22.6 + b), (36, 22.6 + b)])
    skull = {p for p in skull if p in face}
    part(cv, skull, BONE, 'sphere', light=(0.2, -0.5))
    cv.set(38, int(17 + b), VOID)
    cv.set(39, int(17 + b), SOUL[3])
    cv.set(38, int(18 + b), SOUL[2])
    glow(cv, 38.5, 17.5 + b, 2 + flare * 2, SOUL[2], 0.2 + 0.2 * flare)
    cv.set(39, int(20 + b), BONE[0])
    for x in (37, 39):
        cv.set(x, int(21 + b), VOID)
    crown_side(cv, 31, 4 + b)
    pauldron(cv, 29, 25 + b, -1)
    # 지팡이 쥔 팔(앞쪽) - 소매가 어깨에서 앞으로
    sl = poly([(28, 26 + b), (33, 25 + b), (grip[0] + 2, grip[1] - 2), (grip[0] - 1, grip[1] + 3), (grip[0] - 7, grip[1] + 2), (27, 32 + b)])
    oc = staff(cv, top, grip)
    part(cv, sl, ROBE, 'span', light=(0.3, -0.4))
    for p in line(grip[0] - 1, grip[1] + 3, grip[0] - 7, grip[1] + 2):
        if p in sl:
            cv.set(p[0], p[1], GOLD[1])
    hand(cv, grip[0], grip[1], 1)
    soul_fire(cv, oc[0], oc[1], orb, f, fire)
    if burst:
        ring(cv, *burst)
    wisps(cv, 31, 40 + b, f, 3, 20, 0.4, wisp, front=True)
    return cv


def crown_side(cv, cx, top):
    band = poly([(cx - 6, top + 5), (cx + 6, top + 6), (cx + 5, top + 9), (cx - 6, top + 8)])
    spikes = set()
    for sx, h in ((-5, 3), (-1, 5), (3, 6), (6, 3)):
        spikes |= poly([(cx + sx - 1.5, top + 6), (cx + sx + 1.5, top + 6), (cx + sx + 0.5, top + 6 - h)])
    part(cv, band | spikes, GOLD, 'sphere', light=(0.1, -0.6))
    for p in ((cx + 3, top + 7), (cx + 4, top + 7)):
        cv.set(int(p[0]), int(p[1]), SOUL[2])
    cv.set(int(cx - 2), int(top + 7), (150, 30, 50))


def lich_north(f, bob=0, top=(12, 9), grip=(12, 35), orb=2.5, fire=1.0, burst=None, sway=0.0, hand2=(49, 40),
               wisp=1.0, hem_amp=1.0):
    """뒷모습 - 지팡이는 화면 왼쪽, 망토 등에 금실 눈 문양."""
    cv = Canvas()
    b = bob
    shadow(cv, 32, 59, 12 - abs(b) * 0.5, 2.6)
    wisps(cv, 32, 40 + b, f, 3, 22, 0.4, wisp, front=False)
    ph = f * 0.75 + sway
    cape = tattered([(22, 24 + b), (42, 24 + b), (51, 44 + b), (53, 62 + b), (11, 62 + b), (13, 44 + b)], 54 + b, ph + 1, hem_amp)
    part(cv, cape, CAPE, 'span', light=(0.2, -0.4))
    sr = poly([(40, 25 + b), (46, 26 + b), (hand2[0] + 5, hand2[1]), (hand2[0] - 1, hand2[1] + 2), (hand2[0] - 6, hand2[1] - 1), (39, 32 + b)])
    part(cv, sr, CAPE, 'span')
    sl = poly([(24, 25 + b), (18, 26 + b), (grip[0] - 3, grip[1] - 1), (grip[0] + 1, grip[1] + 3), (grip[0] + 6, grip[1] + 1), (24, 32 + b)])
    oc = staff(cv, top, grip)
    part(cv, sl, CAPE, 'span')
    hand(cv, grip[0], grip[1], 1)
    hand(cv, hand2[0] - 1, hand2[1] + 3, -1)
    # 등 문양 - 금실 원 + 눈
    for a in range(0, 360, 10):
        x = 32 + math.cos(math.radians(a)) * 6
        y = 41 + b + math.sin(math.radians(a)) * 5
        if cv.opaque(int(x), int(y)):
            cv.set(int(x), int(y), GOLD[1])
    fill(cv, ellipse(32, 41 + b, 3.5, 1.8), GOLD[2])
    for p in ((31, 41 + b), (32, 41 + b)):
        cv.set(p[0], int(p[1]), SOUL[2])
    for k in (-1, 1):
        for p in line(32 + k * 8, 34 + b, 32 + k * 11, 50 + b):
            if cv.opaque(*p):
                cv.set(p[0], p[1], CAPE[0])
    pauldron(cv, 19, 25 + b, -1)
    pauldron(cv, 45, 25 + b, 1)
    hood = ellipse(32, 16 + b, 7.5, 8.5) | poly([(25, 15 + b), (39, 15 + b), (42, 23 + b), (43, 28 + b), (21, 28 + b), (22, 23 + b)])
    part(cv, hood, ROBE, 'sphere', light=(-0.35, -0.5))
    for p in line(32, 10 + b, 32, 27 + b):
        cv.set(p[0], p[1], ROBE[0])
    crown(cv, 32, 4 + b, back=True)
    soul_fire(cv, oc[0], oc[1], orb, f, fire)
    if burst:
        ring(cv, *burst)
    wisps(cv, 32, 40 + b, f, 3, 22, 0.4, wisp, front=True)
    return cv


def frames_east():
    idle, walk, atk = [], [], []
    for f in range(8):
        b = int(round(math.sin(f / 8 * math.tau) * 1.5)) - 1
        idle.append(lich_east(f, bob=b, top=(45, 9 + b), grip=(45, 35 + b), fire=0.8 + 0.2 * math.sin(f)))
    for f in range(8):
        b = int(round(math.sin(f / 4 * math.tau) * 1.2)) - 1
        walk.append(lich_east(f, bob=b, top=(46, 10 + b), grip=(45, 35 + b), sway=f * 1.5, hem_amp=1.3, trail=2 + f % 2))
    A = [
        dict(bob=-1, top=(43, 7), grip=(44, 33), orb=3, fire=1.2, flare=0.3),
        dict(bob=-3, top=(34, 5), grip=(40, 27), orb=3.5, fire=1.6, flare=0.6),
        dict(bob=-4, top=(30, 5), grip=(38, 26), orb=4, fire=2.0, flare=1.0),
        dict(bob=0, top=(57, 38), grip=(43, 31), orb=3, fire=2.0, flare=1.0, burst=(57, 42, 5), trail=3),
        dict(bob=1, top=(57, 39), grip=(43, 32), orb=2.5, fire=1.5, flare=0.7, burst=(56, 44, 8), trail=3),
        dict(bob=0, top=(52, 24), grip=(44, 33), orb=2.5, fire=1.2, flare=0.4, burst=(55, 46, 11), trail=2),
        dict(bob=-1, top=(47, 12), grip=(45, 35), orb=2.5, fire=1.0, flare=0.2),
        dict(bob=-1, top=(45, 9), grip=(45, 35), orb=2.5, fire=0.9),
    ]
    for f, kw in enumerate(A):
        atk.append(lich_east(f, **kw))
    return idle, walk, atk


def frames_north():
    idle, walk, atk = [], [], []
    for f in range(8):
        b = int(round(math.sin(f / 8 * math.tau) * 1.5)) - 1
        idle.append(lich_north(f, bob=b, top=(12, 9 + b), grip=(12, 35 + b), hand2=(49, 40 + b), fire=0.8 + 0.2 * math.sin(f)))
    for f in range(8):
        b = int(round(math.sin(f / 4 * math.tau) * 1.2)) - 1
        walk.append(lich_north(f, bob=b, top=(12, 9 + b), grip=(12, 35 + b), hand2=(49, 40 + b), sway=f * 1.5, hem_amp=1.3))
    A = [
        dict(bob=-1, top=(13, 7), grip=(13, 33), orb=3, fire=1.2),
        dict(bob=-3, top=(18, 5), grip=(16, 27), orb=3.5, fire=1.6, hand2=(47, 32)),
        dict(bob=-4, top=(22, 5), grip=(18, 26), orb=4, fire=2.0, hand2=(46, 30)),
        dict(bob=-2, top=(28, 4), grip=(20, 28), orb=3, fire=2.0, burst=(31, 6, 5)),
        dict(bob=-1, top=(28, 5), grip=(20, 29), orb=2.5, fire=1.5, burst=(31, 6, 8)),
        dict(bob=-1, top=(20, 7), grip=(16, 32), orb=2.5, fire=1.2, burst=(31, 6, 11)),
        dict(bob=-1, top=(14, 9), grip=(13, 35), orb=2.5, fire=1.0),
        dict(bob=-1, top=(12, 9), grip=(12, 35), orb=2.5, fire=0.9),
    ]
    for f, kw in enumerate(A):
        atk.append(lich_north(f, **kw))
    return idle, walk, atk


def write_all(out):
    sets = {'south': frames_south(), 'east': frames_east(), 'north': frames_north()}
    sets['west'] = tuple([c.flip() for c in fs] for fs in sets['east'])
    for d, (idle, walk, atk) in sets.items():
        for name, fs in (('idle', idle), ('walk', walk), ('attack', atk)):
            for i, c in enumerate(fs):
                c.save(os.path.join(out, f'{name}_{d}_{i}.png'))
    return sets


if __name__ == '__main__':
    if len(sys.argv) > 1:
        write_all(sys.argv[1])
    else:
        allf = []
        for fn in (frames_south, frames_east, frames_north):
            i, w, a = fn()
            allf += i + w + a
        sheet(allf, 'boss9_preview.png', scale=2)  # 확인용 - 저장소에 넣지 않는다
