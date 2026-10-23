"""10층 거울 가면 기사 기믹 그림(10-23).
Effects/mirror_crack (96x96 4단계): 변신 중 보스 몸 전체를 감싸는 거울 방어막 - 맞을수록 금이 퍼지고 마지막엔 조각이 떨어져 나감
Effects/tel_mirror (32x32 4프레임, 흰 그림): 거울 파편 예고 칸 - 깨진 유리 금 + 반짝이는 조각
Sprites/Props/Light_Mirror (32x32): 빛 거울 받침대(돌 받침 + 은 고리)
Sprites/Props/Light_Mirror_Face (32x32): 거울 면 "/" 방향 - "\"는 좌우 반전(RoomController.UpdateMirrorFace)
Sprites/Props/Light_Helper (32x32): 의문의 조력자 - 두건 쓴 인물이 금빛 등불을 든다
python draw_gimmick10.py <Assets/Resources 폴더>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, glow, line, part, poly, thick_line

STEEL = [(66, 70, 92), (114, 120, 146), (164, 172, 196), (222, 228, 242)]
GLASS = [(96, 140, 196), (150, 196, 236), (204, 232, 252), (246, 252, 255)]
STONE = [(52, 50, 62), (78, 76, 90), (104, 102, 118), (132, 130, 146)]
ROBE = [(70, 56, 40), (110, 92, 64), (150, 130, 92), (190, 172, 128)]
LIGHT = [(200, 120, 30), (240, 180, 60), (255, 226, 130), (255, 250, 220)]
VIOLET = (170, 110, 255)
W = (255, 255, 255)


# ---------------------------------------------------------------- 거울 방어막(변신 중 몸 전체)
SC, SY, SR = 47.5, 48.5, 45.5  # 가운데·반지름(96x96)


def crack_paths(stage):
    """단계마다 늘어나는 금 - 맞은 자리(가장자리 근처)에서 안쪽으로 갈라져 나가는 꺾은선들."""
    import random
    rng = random.Random(10)
    starts = [(-0.55, -0.35), (0.5, 0.3), (-0.2, 0.6), (0.35, -0.55)]
    paths = []
    for s in range(stage):
        for k in range(3 + s):
            ox, oy = starts[s]
            x, y = SC + ox * SR, SY + oy * SR
            ang = rng.uniform(0, math.tau)
            pts = [(x, y)]
            for _ in range(rng.randint(3, 5)):
                ang += rng.uniform(-0.7, 0.7)
                ln = rng.uniform(5, 10)
                x, y = x + math.cos(ang) * ln, y + math.sin(ang) * ln
                pts.append((x, y))
            paths.append(pts)
    return paths


def mirror_shield(stage):
    """변신한 보스를 감싸는 거울 방어막 - 반투명 유리 구(가장자리일수록 진함) + 면 무늬 + 은빛 테 + 반사광.
    stage 0~3: 맞을수록 금이 퍼지고, 3단계엔 조각이 떨어져 나가 구멍이 뚫린다."""
    cv = Canvas(96, 96)
    holes = set()
    if stage == 3:
        for tri in ([(SC + 20, SY + 8), (SC + 34, SY + 14), (SC + 26, SY + 28)], [(SC - 34, SY - 18), (SC - 22, SY - 26), (SC - 20, SY - 10)]):
            holes |= poly(tri)
    inside = {}
    for y in range(96):
        for x in range(96):
            d = math.hypot((x + 0.5 - SC) / SR, (y + 0.5 - SY) / (SR + 0.5))
            if d <= 1 and (x, y) not in holes:
                inside[(x, y)] = d
    for (x, y), d in inside.items():
        a = 0.06 + 0.26 * d ** 3
        col = GLASS[1] if y < SY + SR * 0.4 else (150, 150, 230)
        cv.blend(x, y, col, a)
    # 면 무늬 - 세 방향 평행선(삼각 격자)
    for ang in (0, 60, 120):
        ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        for (x, y), d in inside.items():
            u = (x - SC) * ca + (y - SY) * sa
            if abs(u % 16) < 0.6 and d < 0.93:
                cv.blend(x, y, GLASS[3], 0.16)
    # 테 + 안쪽 테
    for (x, y), d in inside.items():
        if d > 0.965:
            cv.blend(x, y, GLASS[3], 0.85)
        elif d > 0.93:
            cv.blend(x, y, GLASS[2], 0.4)
    # 반사광(왼쪽 위 굵은 호 + 작은 호)
    for (x, y), d in inside.items():
        t = math.degrees(math.atan2(y - SY, x - SC)) % 360
        if 200 <= t <= 255 and 0.78 <= d <= 0.86:
            cv.blend(x, y, (255, 255, 255), 0.6)
        if 270 <= t <= 290 and 0.62 <= d <= 0.66:
            cv.blend(x, y, (255, 255, 255), 0.5)
    for gx, gy in ((SC + 26, SY - 30), (SC - 30, SY + 24)):
        for dx, dy, a in ((0, 0, 1), (1, 0, .6), (-1, 0, .6), (0, 1, .6), (0, -1, .6)):
            cv.blend(int(gx + dx), int(gy + dy), (255, 255, 255), a)
    cv.blend(int(SC), int(SY + SR - 3), VIOLET, 0.5)
    # 금
    for pts in crack_paths(stage):
        for i in range(len(pts) - 1):
            for p in line(*pts[i], *pts[i + 1]):
                if p in inside:
                    cv.blend(p[0] + 1, p[1] + 1, STEEL[0], 0.45)
                    cv.blend(p[0], p[1], (255, 255, 255), 0.95)
    if stage == 3:  # 구멍 가장자리 반짝 + 떨어지는 조각
        for p in boundary(holes):
            q = None
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if (p[0] + dx, p[1] + dy) in inside:
                    q = (p[0] + dx, p[1] + dy)
            if q:
                cv.blend(q[0], q[1], (255, 255, 255), 0.9)
        for tri in ([(SC + 30, SY + 36), (SC + 36, SY + 38), (SC + 31, SY + 43)], [(SC - 28, SY - 2), (SC - 23, SY), (SC - 27, SY + 5)]):
            for p in poly(tri):
                cv.blend(p[0], p[1], GLASS[2], 0.8)
            for p in boundary(poly(tri)):
                cv.blend(p[0], p[1], (255, 255, 255), 0.9)
    return cv


# ---------------------------------------------------------------- 거울 파편 예고 칸(흰 그림)
def put(cv, x, y, a):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < 32 and 0 <= y < 32:
        old = cv.get(x, y)
        if old is None or old[3] < a:
            cv.set(x, y, W + (a,))


def tel_mirror(f):
    cv = Canvas(32, 32)
    for y in range(1, 31):
        for x in range(1, 31):
            cv.set(x, y, W + (50,))
    for i in range(1, 31):
        for p in ((i, 1), (i, 30), (1, i), (30, i)):
            put(cv, *p, 200)
    # 가운데에서 뻗는 깨진 금
    c = (15.5, 15.5)
    for ang, ln in ((0.3, 15), (1.4, 13), (2.3, 15), (3.3, 14), (4.2, 15), (5.3, 13)):
        mid = (c[0] + math.cos(ang) * ln * 0.5 + math.cos(ang + 1.3) * 2, c[1] + math.sin(ang) * ln * 0.5 + math.sin(ang + 1.3) * 2)
        end = (c[0] + math.cos(ang) * ln, c[1] + math.sin(ang) * ln)
        for p in line(*c, *mid) + line(*mid, *end):
            put(cv, *p, 255)
    for r in (4, 9):  # 금 사이를 잇는 고리 금
        for k in range(int(r * 6)):
            t = k / (r * 6) * math.tau
            if int(t * 5 + r) % 3:
                put(cv, c[0] + math.cos(t) * r, c[1] + math.sin(t) * r, 150)
    # 반짝이는 조각 셋(프레임마다 자리 바꿈)
    spots = [(8, 8), (23, 10), (10, 23), (22, 22)]
    for k in range(2):
        x, y = spots[(f + k * 2) % 4]
        for dx, dy, a in ((0, 0, 255), (1, 0, 200), (-1, 0, 200), (0, 1, 200), (0, -1, 200), (2, 0, 120), (-2, 0, 120), (0, 2, 120), (0, -2, 120)):
            put(cv, x + dx, y + dy, a)
    return cv


# ---------------------------------------------------------------- 빛 거울 받침대·면
def mirror_stand():
    cv = Canvas(32, 32)
    base = poly([(7, 25), (25, 25), (27, 30), (5, 30)])
    part(cv, base, STONE, 'span')
    step = poly([(10, 22), (22, 22), (23, 26), (9, 26)])
    part(cv, step, STONE, 'span')
    post = thick_line(16, 22, 16, 13, 4)
    part(cv, post, STEEL, 'span')
    ring = ellipse(16, 12, 7, 7) - ellipse(16, 12, 4.5, 4.5)
    part(cv, ring, STEEL, 'sphere')
    for p in ((16, 4), (16, 5), (15, 5), (17, 5)):
        cv.set(p[0], p[1], VIOLET if p == (16, 4) else STEEL[2])
    return cv


def mirror_face():
    """"/" 방향 거울 판 - 은테 + 유리 + 반사광, 받침 고리 가운데를 지나간다."""
    cv = Canvas(32, 32)
    a, b = (6, 22), (26, 2)
    panel = thick_line(a[0], a[1], b[0], b[1], 7)
    part(cv, panel, STEEL, 'span')
    glass = thick_line(a[0] + 1, a[1] - 1, b[0] - 1, b[1] + 1, 4)
    glass = {p for p in glass if p in panel and p not in boundary(panel)}
    for p in glass:
        t = (p[0] - a[0]) / (b[0] - a[0])
        cv.set(p[0], p[1], GLASS[2] if (p[0] + p[1]) % 7 else GLASS[3])
    for p in line(a[0] + 4, a[1] - 5, b[0] - 5, b[1] + 3):
        if p in glass:
            cv.set(p[0], p[1], GLASS[3])
    for p in line(a[0] + 3, a[1] - 2, b[0] - 2, b[1] + 3):
        if p in glass:
            cv.set(p[0], p[1], GLASS[1])
    return cv


# ---------------------------------------------------------------- 의문의 조력자
def helper():
    cv = Canvas(32, 32)
    glow(cv, 22, 15, 10, LIGHT[2], 0.4)
    for p in ellipse(16, 30, 9, 1.8):
        cv.set(p[0], p[1], (10, 6, 16, 100))
    robe = poly([(12, 11), (19, 11), (22, 20), (23, 30), (8, 30), (9, 20)])
    part(cv, robe, ROBE, 'span')
    for p in line(15, 14, 13, 29):
        if p in robe and p not in boundary(robe):
            cv.set(p[0], p[1], ROBE[0])
    hood = ellipse(15, 9, 5, 5.5) | poly([(10, 9), (20, 9), (21, 14), (9, 14)])
    part(cv, hood, ROBE, 'sphere', light=(-0.3, -0.5))
    face = ellipse(16.5, 10.5, 2.6, 3)
    for p in face:
        if p in hood and p not in boundary(hood):
            cv.set(p[0], p[1], (20, 14, 12))
    cv.set(17, 10, LIGHT[3])  # 두건 속 눈빛 한 점
    cv.set(15, 10, LIGHT[2])
    arm = thick_line(18, 15, 22, 15, 3)
    part(cv, arm, ROBE, 'span')
    # 등불 - 쇠 고리 + 금빛 유리
    for p in line(23, 11, 23, 13):
        cv.set(p[0], p[1], STEEL[0])
    lamp = poly([(21, 13), (25, 13), (26, 15), (25, 19), (21, 19), (20, 15)])
    part(cv, lamp, LIGHT, 'sphere', outline=(90, 50, 20))
    for p in ((22, 15), (23, 15), (23, 16), (22, 16)):
        cv.set(p[0], p[1], LIGHT[3])
    part(cv, poly([(21, 19), (25, 19), (24, 21), (22, 21)]), STEEL, 'span')
    return cv


if __name__ == '__main__':
    root = sys.argv[1]
    eff = os.path.join(root, 'Effects')
    props = os.path.join(root, 'Sprites', 'Props')
    for i in range(4):
        mirror_shield(i).save(os.path.join(eff, 'mirror_crack', f'{i}.png'))
    os.makedirs(os.path.join(eff, 'tel_mirror'), exist_ok=True)
    for f in range(4):
        tel_mirror(f).save(os.path.join(eff, 'tel_mirror', f'{f}.png'))
    mirror_stand().save(os.path.join(props, 'Light_Mirror.png'))
    mirror_face().save(os.path.join(props, 'Light_Mirror_Face.png'))
    helper().save(os.path.join(props, 'Light_Helper.png'))
