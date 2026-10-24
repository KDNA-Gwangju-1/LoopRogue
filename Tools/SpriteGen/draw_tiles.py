"""던전 바닥·벽 타일 리드로우(10-24) - 32x32 = 한 칸, DungeonTiles가 칸마다 무작위로 섞어 깐다.
칸 오른쪽·아래 가장자리는 늘 줄눈(어두운 선) 한 줄이라 어떤 조합으로 붙어도 이어진다.
Floor_0~5      판석 바닥 - 크기가 다른 돌, 돌마다 위·왼쪽 밝은 모서리 / 아래·오른쪽 그림자, 잔 흠
FloorDecor_0~5 장식 바닥(10%) - 이끼 / 금 / 뼈 / 물웅덩이 / 돌 부스러기 / 희미한 룬
WallTop_0~2    벽 윗면 - 두꺼운 덮개돌을 위에서 본 모습
WallFace_0~5   벽 앞면 - 엇갈려 쌓은 벽돌, 위 턱, 바닥 쪽 그림자 (기본 3 / 금 / 이끼 / 사슬 - 무늬는 각 1/6)
WallFace_Torch 횃불 벽 - 주변 벽돌이 따뜻하게 물듦
WallCracked    비밀 벽(금 간 벽) - 갈라진 틈이 또렷이
python draw_tiles.py <Resources/Tiles 폴더>
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import Canvas

MORTAR = (17, 16, 24)
STONE = [(30, 29, 39), (38, 37, 49), (45, 44, 57), (53, 52, 66), (63, 61, 77)]   # 그림자 → 밝은 모서리
TOPS = [(46, 43, 58), (58, 55, 72), (68, 65, 84), (80, 77, 97), (95, 92, 113)]
BRICK = [(38, 33, 46), (50, 44, 60), (60, 54, 72), (72, 66, 86), (88, 82, 104)]
MOSS = [(34, 52, 36), (48, 72, 44), (66, 94, 54)]
BONE = [(150, 144, 128), (196, 190, 170), (226, 222, 204)]
WATER = [(30, 40, 62), (42, 58, 86), (70, 96, 130), (130, 160, 196)]
FIRE = [(200, 70, 30), (250, 150, 50), (255, 230, 140)]


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def stones(cv, rects, ramp, rng, mortar=MORTAR, pits=3):
    """돌 직사각형들(x0, y0, x1, y1 - 끝 포함)을 칠한다. 바깥 줄눈 1칸, 위·왼쪽 밝게, 아래·오른쪽 어둡게, 모서리 깎임, 잔 흠."""
    for x in range(32):
        for y in range(32):
            cv.set(x, y, mortar)
    for (x0, y0, x1, y1) in rects:
        tone = rng.choice((1, 2, 2, 2, 3))
        base = ramp[tone]
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                # 모서리 깎기
                if (x, y) in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
                    continue
                c = base
                if y == y0 or x == x0:
                    c = ramp[min(4, tone + 1)]
                if y == y1 or x == x1:
                    c = ramp[max(0, tone - 1)]
                n = rng.random()
                if n < 0.06:
                    c = lerp(c, ramp[max(0, tone - 1)], 0.6)
                elif n < 0.09:
                    c = lerp(c, ramp[min(4, tone + 1)], 0.5)
                cv.set(x, y, c)
        # 잔 흠(어두운 점 + 아래 밝은 점)
        w, h = x1 - x0, y1 - y0
        for _ in range(rng.randint(0, pits) if w > 4 and h > 4 else 0):
            px, py = rng.randint(x0 + 2, x1 - 2), rng.randint(y0 + 2, y1 - 2)
            cv.set(px, py, ramp[max(0, tone - 2)])
            cv.set(px + 1, py + 1, ramp[min(4, tone + 1)])


FLOOR_LAYOUTS = [  # 줄눈은 칸 오른쪽·아래(31) 한 줄 + 안쪽 나눔 - 붙이면 늘 1칸 줄눈
    [(0, 0, 14, 14), (16, 0, 30, 9), (16, 11, 30, 30), (0, 16, 14, 30)],
    [(0, 0, 19, 12), (21, 0, 30, 18), (0, 14, 10, 30), (12, 14, 19, 30), (21, 20, 30, 30)],
    [(0, 0, 30, 10), (0, 12, 15, 30), (17, 12, 30, 22), (17, 24, 30, 30)],
    [(0, 0, 9, 20), (11, 0, 30, 13), (11, 15, 22, 30), (24, 15, 30, 30), (0, 22, 9, 30)],
    [(0, 0, 14, 6), (0, 8, 14, 30), (16, 0, 30, 22), (16, 24, 30, 30)],
    [(0, 0, 30, 14), (0, 16, 21, 30), (23, 16, 30, 30)],
]


def floor(i):
    rng = random.Random(100 + i)
    cv = Canvas(32, 32)
    stones(cv, FLOOR_LAYOUTS[i], STONE, rng)
    return cv, rng


def decor(i):
    cv, rng = floor((i + 2) % 6)
    if i == 0:  # 이끼 - 돌 위에 번진 덩어리 두세 개(가장자리는 성기게)
        for cx, cy, r in ((9, 21, 7), (21, 9, 4.5), (24, 24, 3)):
            for y in range(32):
                for x in range(32):
                    d = math.hypot(x - cx, (y - cy) * 1.2) / r
                    if d < 1 and rng.random() < (1.0 if d < 0.6 else 0.45):
                        cv.set(x, y, MOSS[2] if d < 0.3 and rng.random() < 0.5 else MOSS[1] if d < 0.75 else MOSS[0])
    elif i == 1:  # 금
        x, y = 8, 4
        for k in range(26):
            cv.set(x, y, MORTAR)
            cv.set(x + 1, y, STONE[0])
            y += 1
            x += rng.choice((-1, 0, 0, 1))
            if k == 12:
                bx, by = x, y
        for k in range(8):
            cv.set(bx + k, by + k // 2, MORTAR)
    elif i == 2:  # 뼈 - 해골 + 뼈 두 개
        for (x, y) in ((19, 18), (20, 18), (21, 18), (18, 19), (19, 19), (20, 19), (21, 19), (22, 19), (18, 20), (20, 20), (22, 20),
                       (19, 21), (20, 21), (21, 21)):
            cv.set(x, y, BONE[1])
        for (x, y) in ((19, 20), (21, 20)):
            cv.set(x, y, MORTAR)
        for (x, y) in ((19, 18), (18, 19)):
            cv.set(x, y, BONE[2])
        for k in range(7):
            cv.set(6 + k, 9 + k // 3, BONE[1] if k % 6 else BONE[2])
        for k in range(6):
            cv.set(10 + k, 25 - k // 2, BONE[0] if k % 5 else BONE[2])
        for (x, y) in ((19, 22), (20, 22), (21, 22)):
            cv.set(x, y, STONE[0])
    elif i == 3:  # 물웅덩이 + 반사광
        for y in range(10, 24):
            for x in range(5, 27):
                d = ((x - 15.5) / 10.5) ** 2 + ((y - 16.5) / 6.2) ** 2
                if d <= 1:
                    cv.set(x, y, WATER[0] if d > 0.8 else WATER[1] if d > 0.35 else WATER[2] if rng.random() < 0.1 else WATER[1])
        for x in range(10, 15):
            cv.set(x, 13, WATER[3])
        cv.set(18, 19, WATER[3])
        cv.set(19, 19, WATER[2])
    elif i == 4:  # 돌 부스러기
        for _ in range(7):
            x, y = rng.randint(3, 27), rng.randint(3, 27)
            r = rng.choice((1, 1, 2))
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if abs(dx) + abs(dy) <= r:
                        cv.set(x + dx, y + dy, STONE[3] if dy < 0 or dx < 0 else STONE[1])
            cv.set(x + r + 1, y + 1, MORTAR)
    else:  # 희미한 룬(새김)
        for a in range(0, 360, 8):
            x = 16 + math.cos(math.radians(a)) * 9
            y = 16 + math.sin(math.radians(a)) * 9
            cv.set(int(x), int(y), STONE[0])
        for p in ((16, 9), (16, 23), (9, 16), (23, 16)):
            pass
        for k in range(-5, 6):
            cv.set(16 + k, 16, STONE[0])
            cv.set(16, 16 + k, STONE[0])
        for k in range(-3, 4):
            cv.set(16 + k, 16 + k, STONE[1])
    return cv


def wall_top(i):
    rng = random.Random(300 + i)
    cv = Canvas(32, 32)
    layouts = [
        [(0, 0, 30, 14), (0, 16, 14, 30), (16, 16, 30, 30)],
        [(0, 0, 14, 30), (16, 0, 30, 14), (16, 16, 30, 30)],
        [(0, 0, 20, 14), (22, 0, 30, 14), (0, 16, 30, 30)],
    ]
    stones(cv, layouts[i], TOPS, rng, mortar=(28, 26, 36), pits=2)
    return cv


def wall_face(i, torch=False, cracked=False):
    """앞면: 0~3 위 턱(덮개돌 가장자리), 4~29 벽돌 4줄(6칸 높이, 엇갈림), 30~31 바닥 쪽 짙은 그림자."""
    rng = random.Random(500 + i)
    cv = Canvas(32, 32)
    for x in range(32):
        for y in range(32):
            cv.set(x, y, (22, 20, 30))
    # 위 턱
    for x in range(32):
        cv.set(x, 0, TOPS[4])
        cv.set(x, 1, TOPS[3])
        cv.set(x, 2, TOPS[2])
        cv.set(x, 3, (20, 18, 28))
    rows = [(4, 9), (11, 16), (18, 23), (25, 29)]
    for r, (y0, y1) in enumerate(rows):
        off = 0 if r % 2 == 0 else 8
        xs = [x for x in range(-16 + off, 33, 16)]
        for x0 in xs:
            a, b = max(0, x0 + 1), min(31, x0 + 15)
            if a > b:
                continue
            tone = rng.choice((1, 2, 2, 3))
            for y in range(y0, y1 + 1):
                for x in range(a, b + 1):
                    c = BRICK[tone]
                    if y == y0:
                        c = BRICK[min(4, tone + 1)]
                    if y == y1 or x == b:
                        c = BRICK[max(0, tone - 1)]
                    if rng.random() < 0.06:
                        c = lerp(c, BRICK[0], 0.5)
                    cv.set(x, y, c)
    # 아래 그림자(바닥과 만나는 곳) - 마지막 벽돌줄 아래를 어둡게
    for x in range(32):
        cv.set(x, 30, (14, 13, 20))
        cv.set(x, 31, (10, 9, 15))
        if rng.random() < 0.5:
            cv.set(x, 29, lerp(cv.get(x, 29)[:3], (10, 9, 15), 0.5))
    if i == 3 or cracked:  # 금
        x, y = (17 if cracked else 9), 4
        w = 2 if cracked else 1
        for k in range(26):
            for dx in range(w):
                cv.set(x + dx, y, (8, 7, 12))
            cv.set(x + w, y, BRICK[0])
            y += 1
            x += rng.choice((-1, 0, 1)) if cracked else rng.choice((-1, 0, 0, 1))
        if cracked:  # 갈라진 틈 사이로 새는 빛·떨어진 부스러기
            for k in range(5):
                cv.set(15 + k * 2, 28 + k % 2, BRICK[3])
            for y2 in range(10, 22, 3):
                cv.set(rng.randint(12, 22), y2, (8, 7, 12))
    if i == 4:  # 이끼가 위 턱에서 흘러내림
        for x in range(32):
            if rng.random() < 0.6:
                ln = rng.randint(1, 7) if rng.random() < 0.5 else 1
                for y in range(3, 3 + ln):
                    cv.set(x, y, MOSS[0] if y > 5 else MOSS[1 + (x % 2)])
    if i == 5:  # 벽에 박힌 쇠고리 + 늘어진 사슬
        for (x, y) in ((15, 8), (16, 8), (14, 9), (17, 9), (15, 10), (16, 10)):
            cv.set(x, y, (120, 120, 132))
        cv.set(15, 8, (170, 170, 184))
        for k in range(8):
            y = 11 + k * 2
            x = 16 + (1 if k % 2 else 0)
            cv.set(x, y, (100, 100, 112))
            cv.set(x, y + 1, (60, 60, 70))
            cv.set(x + 1, y, (150, 150, 162))
    if torch:  # 쇠 받침 + 불꽃 + 주변 따뜻한 빛
        for y in range(4, 30):
            for x in range(32):
                c = cv.get(x, y)[:3]
                d = math.hypot(x - 16, (y - 12) * 1.2) / 15
                if d < 1:
                    cv.set(x, y, lerp(c, (150, 90, 50), 0.38 * (1 - d) ** 1.5))
        for (x, y) in ((14, 17), (15, 17), (16, 17), (17, 17), (18, 17), (15, 18), (16, 18), (17, 18), (16, 19), (16, 20), (16, 21)):
            cv.set(x, y, (70, 62, 66))
        for x in range(14, 19):
            cv.set(x, 16, (110, 100, 100))
        for (x, y, c) in ((15, 15, 0), (16, 15, 1), (17, 15, 0), (15, 14, 1), (16, 14, 2), (17, 14, 1), (16, 13, 2), (16, 12, 1), (15, 12, 0),
                          (17, 11, 0), (16, 11, 1), (16, 10, 0)):
            cv.set(x, y, FIRE[c])
    return cv


if __name__ == '__main__':
    out = sys.argv[1]
    for i in range(6):
        floor(i)[0].save(os.path.join(out, f'Floor_{i}.png'))
        decor(i).save(os.path.join(out, f'FloorDecor_{i}.png'))
    for i in range(3):
        wall_top(i).save(os.path.join(out, f'WallTop_{i}.png'))
    for i in range(6):
        wall_face(i).save(os.path.join(out, f'WallFace_{i}.png'))
    wall_face(0, torch=True).save(os.path.join(out, 'WallFace_Torch.png'))
    wall_face(7, cracked=True).save(os.path.join(out, 'WallCracked.png'))
