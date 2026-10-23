"""10층 거울 가면 기사 도트 리드로우(10-23) - 반짝이는 판금 기사: 얼굴 대신 타원 거울 가면·보라 깃털 장식·
겹친 견갑·누더기 보라 망토·거울 유리 장검, 몸 둘레를 도는 거울 조각.
python draw_boss10.py [Sprites/Boss10 폴더] - 폴더를 안 주면 boss10_preview.png만 만든다
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, fill, glow, line, part, poly, shadow, sheet, thick_line

STEEL = [(66, 70, 92), (114, 120, 146), (164, 172, 196), (222, 228, 242)]
STEEL_D = [(42, 44, 62), (72, 76, 100), (104, 110, 136), (144, 150, 176)]
CAPE = [(28, 16, 42), (48, 28, 74), (72, 44, 108), (96, 66, 140)]
CAPE_IN = [(18, 10, 28), (30, 18, 46), (44, 28, 66), (58, 38, 86)]
PLUME = [(64, 22, 80), (104, 40, 128), (150, 70, 180), (196, 120, 226)]
GLASS = [(96, 140, 196), (150, 196, 236), (204, 232, 252), (246, 252, 255)]
GOLD = [(116, 76, 28), (172, 124, 48), (220, 176, 80), (255, 228, 140)]
VIOLET = (170, 110, 255)

TEETH = [2, 1, 3, 1, 2, 3, 1, 2]


def hem(x, base, ph, amp=1.0):
    i = int(math.floor((x + ph) / 3)) % len(TEETH)
    frac = ((x + ph) % 3) / 3
    return base + int(round(TEETH[i] * (1 - abs(frac - 0.5) * 1.2) * amp))


def tattered(pts, base, ph, amp=1.0):
    return {(x, y) for x, y in poly(pts) if y <= hem(x, base, ph, amp)}


def mirror_face(cv, cx, cy, rx, ry, f, flare=0.0, crack=False):
    """타원 거울 가면 - 은 테두리, 위에서 아래로 밝아지는 유리, 비스듬한 반사광 두 줄(프레임마다 흐름)."""
    frame = ellipse(cx, cy, rx + 1, ry + 1)
    part(cv, frame, STEEL, 'sphere')
    glass = ellipse(cx, cy, rx, ry)
    part(cv, glass, GLASS, 'vspan', outline=STEEL[0], bands=(0.25, 0.5, 0.8))
    inner = glass - boundary(glass)
    off = [0, 1, 2, 3, 3, 2, 1, 0][f % 8]
    for k in (0, 3):
        for t in range(-8, 9):
            x, y = int(cx - 3 + off + k + t * 0.5), int(cy + t)
            if (x, y) in inner:
                cv.set(x, y, GLASS[3])
    # 거울 속 보랏빛 두 점(눈 대신)
    for ex in (-2, 2):
        cv.blend(int(cx + ex), int(cy), VIOLET, 0.55 + 0.45 * flare)
    if flare > 0:
        glow(cv, cx, cy, rx + 3 + flare * 3, GLASS[2], 0.35 * flare)
    if crack:
        for p in line(cx + 1, cy - ry + 1, cx - 1, cy) + line(cx - 1, cy, cx + 2, cy + 3):
            if p in inner:
                cv.set(p[0], p[1], STEEL[0])


def plume(cv, x, y, f, side=0, h=13):
    """투구 꼭대기 말총 장식(프레임마다 살랑). side=0(앞·뒤): 가운데 볏이 위로 솟음,
    side=-1(옆모습): 뒤로 길게 휘어 넘김."""
    sway = [0, 1, 1, 0, -1, -1, 0, 1][f % 8] * 0.7
    m = set()
    if side == 0:
        for k in range(9):  # 가운데 볏 - 투구 정수리에서 위로, 끝이 살랑
            t = k / 8
            m |= ellipse(x + sway * t * t * 1.5, y - k * 0.85, 2.2 - t * 0.9, 1.3)
    else:
        for spread, hk in ((0, 1.0), (-1.5, 0.85), (1.5, 0.7)):
            hh = h * hk
            for k in range(int(hh)):
                t = k / hh
                cx = x - t * 12 + sway * t * t * 2
                cy = y - math.sin(t * 2.2) * 5 + t * t * 9 + spread
                m |= ellipse(cx, cy, 2.2 * (1 - t) ** 0.7 + 0.7, 1.4)
    part(cv, m, PLUME, 'span')


def sword(cv, grip, tip, guard=True, gleam=0.0):
    """거울 유리 장검 - grip(손)에서 tip까지 날, 손 반대로 손잡이·폼멜."""
    gx, gy = grip
    dx, dy = tip[0] - gx, tip[1] - gy
    L = math.hypot(dx, dy) or 1
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    base = (gx + ux * 3, gy + uy * 3)
    blade = poly([(base[0] + nx * 3.2, base[1] + ny * 3.2), (tip[0] - ux * 5 + nx * 2.6, tip[1] - uy * 5 + ny * 2.6),
                  (tip[0] + ux * 0.5, tip[1] + uy * 0.5), (tip[0] - ux * 5 - nx * 2.6, tip[1] - uy * 5 - ny * 2.6),
                  (base[0] - nx * 3.2, base[1] - ny * 3.2)])
    part(cv, blade, GLASS, 'span', light=(-0.2, -0.4))
    for p in line(base[0] + ux, base[1] + uy, tip[0] - ux * 3, tip[1] - uy * 3):  # 가운데 홈
        if p in blade and p not in boundary(blade):
            cv.set(p[0], p[1], GLASS[3])
    if guard:
        g = thick_line(gx + ux * 2 + nx * 6, gy + uy * 2 + ny * 6, gx + ux * 2 - nx * 6, gy + uy * 2 - ny * 6, 3)
        part(cv, g, STEEL, 'sphere')
        cv.set(int(round(gx + ux * 2)), int(round(gy + uy * 2)), VIOLET)
    hilt = thick_line(gx, gy, gx - ux * 4, gy - uy * 4, 3)
    part(cv, hilt, CAPE, 'span')
    part(cv, ellipse(gx - ux * 5, gy - uy * 5, 1.8, 1.8), STEEL, 'sphere')
    if gleam > 0:
        tx, ty = tip[0] - ux * 4, tip[1] - uy * 4
        for k in range(-3, 4):
            cv.blend(int(tx + k), int(ty), GLASS[3], gleam * (1 - abs(k) / 4))
            cv.blend(int(tx), int(ty + k), GLASS[3], gleam * (1 - abs(k) / 4))


def gauntlet(cv, x, y):
    part(cv, ellipse(x, y, 2.6, 2.4), STEEL, 'sphere')


def shards(cv, cx, cy, f, front, n=4, rad=22, a=0.9):
    """몸 둘레를 도는 거울 조각(작은 마름모) - front=True면 앞(아래 반)만."""
    for i in range(n):
        t = (f / 8 + i / n) * math.tau
        x, y = cx + math.cos(t) * rad, cy + math.sin(t) * rad * 0.35
        if (math.sin(t) > 0) != front:
            continue
        x, y = int(x), int(y)
        cv.blend(x, y - 1, GLASS[3], a)
        cv.blend(x, y, GLASS[2], a)
        cv.blend(x - 1, y, GLASS[1], a * 0.8)
        cv.blend(x + 1, y, GLASS[3], a * 0.8)
        cv.blend(x, y + 1, GLASS[0], a * 0.8)


def slash_arc(cv, cx, cy, r, a0, a1, a=1.0, width=3):
    """베기 궤적 - 초승달 빛(바깥 밝게)."""
    steps = int(abs(a1 - a0) * r * 1.5) + 2
    for i in range(steps + 1):
        t = a0 + (a1 - a0) * i / steps
        fade = 0.35 + 0.65 * i / steps
        for k in range(width):
            rr = r - k
            x, y = cx + math.cos(t) * rr, cy + math.sin(t) * rr
            cv.blend(int(round(x)), int(round(y)), GLASS[3] if k == 0 else GLASS[2] if k == 1 else VIOLET, a * fade * (1 - k * 0.25))


def legs(cv, b, step, view='south'):
    """다리 두 개 - step: -1/0/1 (어느 발이 들렸나)."""
    if view == 'south':
        for i, x in enumerate((25, 35)):
            lift = 2 if (step == 1 and i == 0) or (step == -1 and i == 1) else 0
            leg = poly([(x, 44 + b), (x + 5, 44 + b), (x + 5, 56 - lift), (x, 56 - lift)])
            part(cv, leg, STEEL_D, 'span')
            knee = ellipse(x + 2.5, 49 + b - lift * 0.5, 3, 2)
            part(cv, knee, STEEL, 'sphere')
            boot = poly([(x - 1, 55 - lift), (x + 6, 55 - lift), (x + 6.5, 59 - lift), (x - 1.5, 59 - lift)])
            part(cv, boot, STEEL, 'span')
    else:  # 옆모습 - 앞다리/뒷다리
        for i, ox in enumerate((-3, 3)):
            sw = step * (1 if i == 0 else -1) * 3
            x = 30 + ox * 0.4 + sw
            lift = 2 if sw > 0 else 0
            leg = poly([(29 + ox * 0.3, 44 + b), (34 + ox * 0.3, 44 + b), (x + 3, 56 - lift), (x - 2, 56 - lift)])
            part(cv, leg, STEEL_D if i == 0 else STEEL, 'span', light=(0.3, -0.4))
            boot = poly([(x - 2.5, 55 - lift), (x + 4, 55 - lift), (x + 5.5, 59 - lift), (x - 2.5, 59 - lift)])
            part(cv, boot, STEEL, 'span', light=(0.3, -0.4))


def knight_south(f, b=0, step=0, grip=(47, 38), tip=(52, 7), hand2=(16, 40), flare=0.0, arc=None, ph=0.0,
                 two_hand=False, gleam=0.0, raise_arm=False):
    cv = Canvas()
    shadow(cv, 32, 59, 13, 2.6)
    shards(cv, 32, 36 + b, f, False)
    cape = tattered([(21, 23 + b), (43, 23 + b), (49, 46 + b), (51, 62), (13, 62), (15, 46 + b)], 55, ph, 1.0)
    part(cv, cape, CAPE_IN, 'span')
    legs(cv, b, step)
    # 허리 판갑(치마)
    skirt = poly([(22, 37 + b), (42, 37 + b), (44, 47 + b), (20, 47 + b)])
    part(cv, skirt, STEEL_D, 'span')
    for y in (40 + b, 43 + b):
        for x in range(21, 44):
            if (x, y) in skirt and (x, y) not in boundary(skirt):
                cv.set(x, y, STEEL_D[0])
    for p in line(32, 37 + b, 32, 46 + b):
        cv.set(p[0], p[1], STEEL_D[0])
    # 흉갑
    torso = poly([(22, 21 + b), (42, 21 + b), (41, 31 + b), (38, 38 + b), (26, 38 + b), (23, 31 + b)])
    part(cv, torso, STEEL, 'span', extra=lambda x, y: -0.1 if y < 27 + b else 0.05)
    for p in line(32, 23 + b, 32, 36 + b):
        cv.set(p[0], p[1], STEEL[3] if p[1] < 30 + b else STEEL[2])
        cv.set(p[0] + 1, p[1], STEEL[1])
    for y, w in ((31 + b, 7), (34 + b, 5)):
        for x in range(32 - w, 33 + w):
            if (x, y) in torso and (x, y) not in boundary(torso):
                cv.set(x, y, STEEL[0])
    # 허리띠 + 거울 버클
    for x in range(25, 40):
        cv.set(x, 37 + b, OUT)
        cv.set(x, 38 + b, CAPE[1])
    tab = tattered([(28.5, 39 + b), (35.5, 39 + b), (36.5, 50 + b), (37, 56), (27, 56), (27.5, 50 + b)], 51 + b, 0, 0.7)
    part(cv, tab, CAPE, 'span')
    for p in boundary(tab):
        if p[1] < 50 + b and p[1] > 39 + b:
            cv.set(p[0], p[1], GOLD[1])
    part(cv, ellipse(32, 44 + b, 1.6, 2.2), GLASS, 'sphere', outline=GOLD[0])
    part(cv, ellipse(32, 38 + b, 2.2, 2), GLASS, 'sphere', outline=GOLD[0])
    # 망토 어깨 부분(앞으로 넘어온 자락)
    for side in (-1, 1):
        drape = poly([(32 + side * 9, 21 + b), (32 + side * 13, 22 + b), (32 + side * 14, 34 + b), (32 + side * 11, 30 + b)])
        part(cv, drape, CAPE, 'span')
    # 왼팔(화면 왼쪽)
    if not two_hand:
        arm = thick_line(19, 27 + b, hand2[0] + 1, hand2[1] - 2, 5)
        part(cv, arm, STEEL_D, 'span')
        gauntlet(cv, *hand2)
    # 검 + 오른팔
    if raise_arm:
        sword(cv, grip, tip, gleam=gleam)
    arm = thick_line(45, 27 + b, grip[0] - 1, grip[1] - 1, 5)
    part(cv, arm, STEEL_D, 'span')
    if two_hand:
        arm2 = thick_line(19, 27 + b, grip[0] - 2, grip[1] + 1, 5)
        part(cv, arm2, STEEL_D, 'span')
    if not raise_arm:
        sword(cv, grip, tip, gleam=gleam)
    gauntlet(cv, *grip)
    # 견갑(두 장 겹침)
    for side, cx in ((-1, 19), (1, 45)):
        lower = ellipse(cx + side * 1, 26 + b, 5, 3.5)
        part(cv, lower, STEEL_D, 'sphere')
        upper = ellipse(cx, 23 + b, 6, 4)
        part(cv, upper, STEEL, 'sphere')
        cv.set(cx - side * 2, 22 + b, STEEL[3])
    # 목가리개 + 투구 + 거울 가면 + 깃털
    part(cv, poly([(27, 19 + b), (37, 19 + b), (38, 23 + b), (26, 23 + b)]), STEEL_D, 'span')
    helm = ellipse(32, 13 + b, 7.5, 8.5)
    part(cv, helm, STEEL, 'sphere', light=(-0.35, -0.5))
    plume(cv, 32, 7 + b, f, side=0, h=7)
    mirror_face(cv, 32, 14 + b, 5, 6, f, flare)
    if arc:
        slash_arc(cv, *arc)
    shards(cv, 32, 36 + b, f, True)
    return cv


def knight_east(f, b=0, step=0, grip=(43, 36), tip=(48, 6), flare=0.0, arc=None, ph=0.0, gleam=0.0, trail=0):
    cv = Canvas()
    shadow(cv, 31, 59, 11, 2.6)
    shards(cv, 31, 36 + b, f, False)
    cape = tattered([(24, 22 + b), (36, 22 + b), (36, 44 + b), (37, 62), (12 - trail, 62), (17 - trail, 44 + b)], 55, ph, 1.0)
    part(cv, cape, CAPE, 'span', light=(0.3, -0.4))
    legs(cv, b, step, view='east')
    skirt = poly([(25, 37 + b), (37, 37 + b), (39, 47 + b), (24, 47 + b)])
    part(cv, skirt, STEEL_D, 'span', light=(0.3, -0.4))
    for y in (40 + b, 43 + b):
        for x in range(24, 40):
            if (x, y) in skirt and (x, y) not in boundary(skirt):
                cv.set(x, y, STEEL_D[0])
    torso = poly([(25, 21 + b), (37, 21 + b), (39, 28 + b), (38, 38 + b), (26, 38 + b), (24, 30 + b)])
    part(cv, torso, STEEL, 'span', light=(0.35, -0.4))
    for x in range(26, 39):
        cv.set(x, 37 + b, OUT)
        cv.set(x, 38 + b, CAPE[1])
    part(cv, ellipse(37, 38 + b, 1.8, 1.8), GLASS, 'sphere', outline=GOLD[0])
    # 투구(옆) - 거울 가면은 앞쪽(오른쪽)으로 옆에서 본 얇은 타원
    part(cv, poly([(28, 19 + b), (36, 19 + b), (36, 23 + b), (27, 23 + b)]), STEEL_D, 'span')
    plume(cv, 30, 7 + b, f, side=-1, h=13)
    helm = ellipse(31, 13 + b, 6.5, 8)
    part(cv, helm, STEEL, 'sphere', light=(0.2, -0.5))
    glass = ellipse(36.5, 14 + b, 2, 5.5)
    part(cv, glass, GLASS, 'vspan', outline=STEEL[0])
    cv.blend(37, int(14 + b), VIOLET, 0.6 + 0.4 * flare)
    if flare > 0:
        glow(cv, 37, 14 + b, 4 + flare * 3, GLASS[2], 0.35 * flare)
    for p in line(26, 9 + b, 26, 17 + b):
        cv.set(p[0], p[1], STEEL[1])
    # 견갑 + 팔 + 검
    upper = ellipse(30, 24 + b, 5.5, 4)
    sword(cv, grip, tip, gleam=gleam)
    arm = thick_line(30, 27 + b, grip[0] - 1, grip[1] - 1, 5)
    part(cv, arm, STEEL_D, 'span', light=(0.3, -0.4))
    gauntlet(cv, *grip)
    part(cv, ellipse(31, 26.5 + b, 4.5, 3), STEEL_D, 'sphere')
    part(cv, upper, STEEL, 'sphere')
    if arc:
        slash_arc(cv, *arc)
    shards(cv, 31, 36 + b, f, True)
    return cv


def knight_north(f, b=0, step=0, grip=(17, 38), tip=(12, 7), hand2=(48, 40), arc=None, ph=0.0, gleam=0.0, two_hand=False):
    cv = Canvas()
    shadow(cv, 32, 59, 13, 2.6)
    shards(cv, 32, 36 + b, f, False)
    legs(cv, b, -step)
    sword(cv, grip, tip, gleam=gleam)
    arm = thick_line(19, 27 + b, grip[0] + 1, grip[1] - 1, 5)
    part(cv, arm, STEEL_D, 'span')
    gauntlet(cv, *grip)
    if not two_hand:
        arm2 = thick_line(45, 27 + b, hand2[0] - 1, hand2[1] - 2, 5)
        part(cv, arm2, STEEL_D, 'span')
        gauntlet(cv, *hand2)
    cape = tattered([(21, 22 + b), (43, 22 + b), (47, 44 + b), (49, 62), (15, 62), (17, 44 + b)], 56, ph, 1.0)
    part(cv, cape, CAPE, 'span', light=(0.2, -0.4))
    for k in (-1, 1):
        for p in line(32 + k * 4, 26 + b, 32 + k * 9, 54):
            if cv.opaque(*p) and p in cape and p not in boundary(cape):
                cv.set(p[0], p[1], CAPE[0])
    # 망토 등 문양 - 금실 타원 거울
    for t in range(0, 360, 12):
        x = 32 + math.cos(math.radians(t)) * 4
        y = 38 + b + math.sin(math.radians(t)) * 5
        cv.set(int(round(x)), int(round(y)), GOLD[1])
    for side, cx in ((-1, 19), (1, 45)):
        part(cv, ellipse(cx + side, 26 + b, 5, 3.5), STEEL_D, 'sphere')
        part(cv, ellipse(cx, 23 + b, 6, 4), STEEL, 'sphere')
    helm = ellipse(32, 13 + b, 7.5, 8.5)
    part(cv, helm, STEEL, 'sphere', light=(-0.35, -0.5))
    for p in line(32, 9 + b, 32, 20 + b):
        cv.set(p[0], p[1], STEEL[1])
    plume(cv, 32, 7 + b, f, side=0, h=7)
    if arc:
        slash_arc(cv, *arc)
    shards(cv, 32, 36 + b, f, True)
    return cv


def bob(f, walk=False):
    if walk:
        return [0, -1, -1, 0, 0, -1, -1, 0][f]
    return [0, 0, -1, -1, -1, 0, 0, 0][f]


STEP = [1, 1, 0, 0, -1, -1, 0, 0]


def frames_south():
    idle = [knight_south(f, b=bob(f), grip=(47, 38 + bob(f)), tip=(52, 7 + bob(f)), hand2=(16, 40 + bob(f)), ph=f * 0.5) for f in range(8)]
    walk = [knight_south(f, b=bob(f, 1), step=STEP[f], grip=(47, 38 + bob(f, 1)), tip=(52, 7 + bob(f, 1)), hand2=(16, 40 + bob(f, 1)),
                         ph=f * 1.2) for f in range(8)]
    A = [  # 0~2 두 손으로 검을 머리 위로, 3~4 내려 베기(빛 궤적), 5~7 되돌아옴
        dict(b=0, grip=(44, 30), tip=(54, 4), flare=0.2, two_hand=True),
        dict(b=-1, grip=(38, 18), tip=(52, 0), flare=0.5, two_hand=True, raise_arm=True, gleam=0.6),
        dict(b=-2, grip=(34, 14), tip=(48, 0), flare=1.0, two_hand=True, raise_arm=True, gleam=1.0),
        dict(b=1, grip=(33, 36), tip=(30, 62), flare=1.0, two_hand=True, arc=(32, 30, 27, -2.3, 1.5, 1.0, 3)),
        dict(b=1, grip=(33, 37), tip=(30, 62), flare=0.6, two_hand=True, arc=(32, 30, 28, -1.4, 1.6, 0.6, 2)),
        dict(b=0, grip=(40, 38), tip=(48, 60), flare=0.3, two_hand=True),
        dict(b=0, grip=(46, 38), tip=(53, 12), flare=0.1),
        dict(b=0, grip=(47, 38), tip=(52, 7)),
    ]
    atk = [knight_south(f, ph=f * 0.6, **kw) for f, kw in enumerate(A)]
    return idle, walk, atk


def frames_east():
    idle = [knight_east(f, b=bob(f), grip=(43, 36 + bob(f)), tip=(48, 6 + bob(f)), ph=f * 0.5) for f in range(8)]
    walk = [knight_east(f, b=bob(f, 1), step=STEP[f], grip=(43, 36 + bob(f, 1)), tip=(48, 6 + bob(f, 1)), ph=f * 1.2,
                        trail=2) for f in range(8)]
    A = [
        dict(b=0, grip=(38, 30), tip=(30, 4), flare=0.2),
        dict(b=-1, grip=(34, 22), tip=(18, 6), flare=0.5, gleam=0.6),
        dict(b=-1, grip=(32, 20), tip=(14, 10), flare=1.0, gleam=1.0),
        dict(b=1, grip=(46, 34), tip=(62, 46), flare=1.0, arc=(36, 30, 26, -1.9, 1.0, 1.0, 3), trail=3),
        dict(b=1, grip=(46, 35), tip=(62, 48), flare=0.6, arc=(36, 30, 27, -1.0, 1.1, 0.6, 2), trail=3),
        dict(b=0, grip=(45, 36), tip=(60, 40), flare=0.3, trail=2),
        dict(b=0, grip=(43, 36), tip=(50, 9), flare=0.1),
        dict(b=0, grip=(43, 36), tip=(48, 6)),
    ]
    atk = [knight_east(f, ph=f * 0.6, **kw) for f, kw in enumerate(A)]
    return idle, walk, atk


def frames_north():
    idle = [knight_north(f, b=bob(f), grip=(17, 38 + bob(f)), tip=(12, 7 + bob(f)), hand2=(48, 40 + bob(f)), ph=f * 0.5) for f in range(8)]
    walk = [knight_north(f, b=bob(f, 1), step=STEP[f], grip=(17, 38 + bob(f, 1)), tip=(12, 7 + bob(f, 1)), hand2=(48, 40 + bob(f, 1)),
                         ph=f * 1.2) for f in range(8)]
    A = [
        dict(b=0, grip=(20, 30), tip=(10, 4), two_hand=True),
        dict(b=-1, grip=(26, 20), tip=(14, 2), two_hand=True, gleam=0.6),
        dict(b=-2, grip=(30, 18), tip=(18, 0), two_hand=True, gleam=1.0),
        dict(b=-1, grip=(31, 20), tip=(34, 0), two_hand=True, arc=(32, 26, 24, -3.6, -0.4, 1.0, 3)),
        dict(b=-1, grip=(31, 21), tip=(36, 0), two_hand=True, arc=(32, 26, 25, -3.0, -0.2, 0.6, 2)),
        dict(b=0, grip=(24, 30), tip=(22, 2), two_hand=True),
        dict(b=0, grip=(18, 37), tip=(13, 10)),
        dict(b=0, grip=(17, 38), tip=(12, 7)),
    ]
    atk = [knight_north(f, ph=f * 0.6, **kw) for f, kw in enumerate(A)]
    return idle, walk, atk


def write_all(out):
    sets = {'south': frames_south(), 'east': frames_east(), 'north': frames_north()}
    sets['west'] = tuple([c.flip() for c in fs] for fs in sets['east'])
    for d, (idle, walk, atk) in sets.items():
        for name, fs in (('idle', idle), ('walk', walk), ('attack', atk)):
            for i, c in enumerate(fs):
                c.save(os.path.join(out, f'{name}_{d}_{i}.png'))


if __name__ == '__main__':
    if len(sys.argv) > 1:
        write_all(sys.argv[1])
    else:
        allf = []
        for fn in (frames_south, frames_east, frames_north):
            i, w, a = fn()
            allf += i + w + a
        sheet(allf, 'boss10_preview.png', scale=2)  # 확인용 - 저장소에 넣지 않는다
