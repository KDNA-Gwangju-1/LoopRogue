"""9층 리치 예고 칸(10-23) - 흰 그림(실행 중 예고 색을 곱함), 32x32 4프레임.
tel_checker: 체스판 마법 - 칸 테두리 룬 눈금 + 가운데 돌아가는 마름모 룬
tel_orb: 영혼 구체 - 조여 드는 고리 두 겹 + 가운데 해골
python draw_tel9.py <Effects 폴더>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import Canvas

W = (255, 255, 255)
BASE, MID, HI = 50, 150, 255


def base(cv):
    for y in range(1, 31):
        for x in range(1, 31):
            cv.set(x, y, W + (BASE,))


def put(cv, x, y, a):
    x, y = int(round(x)), int(round(y))
    old = cv.get(x, y)
    if old is None or old[3] < a:
        cv.set(x, y, W + (a,))


def checker(f):
    cv = Canvas(32, 32)
    base(cv)
    # 테두리 - 이중선, 룬 눈금이 프레임마다 한 칸씩 흐른다
    for i in range(1, 31):
        for p in ((i, 1), (i, 30), (1, i), (30, i)):
            put(cv, *p, HI)
        for p in ((i, 3), (i, 28), (3, i), (28, i)):
            if 3 <= i <= 28:
                put(cv, *p, MID)
        if (i + f * 2) % 6 < 2 and 4 <= i <= 27:
            for p in ((i, 2), (i, 29), (2, i), (29, i)):
                put(cv, *p, HI)
    # 네 모서리 - 작은 꽉 찬 사각 + 대각 획
    for cx, cy in ((5, 5), (26, 5), (5, 26), (26, 26)):
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                put(cv, cx + dx, cy + dy, HI if dx == 0 or dy == 0 else MID)
    # 가운데 - 돌아가는 마름모 룬(두 겹) + 안쪽 눈
    ang = f * math.pi / 8
    for r, a in ((11, HI), (7, MID)):
        pts = [(15.5 + math.cos(ang + k * math.pi / 2) * r, 15.5 + math.sin(ang + k * math.pi / 2) * r) for k in range(4)]
        for k in range(4):
            (x0, y0), (x1, y1) = pts[k], pts[(k + 1) % 4]
            n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
            for s in range(n + 1):
                put(cv, x0 + (x1 - x0) * s / n, y0 + (y1 - y0) * s / n, a)
        if r == 11:
            for x, y in pts:
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        if abs(dx) + abs(dy) <= 1:
                            put(cv, x + dx, y + dy, HI)
    pulse = [0, 1, 1, 0][f]
    for y in range(13 - pulse, 19 + pulse):
        for x in range(13, 19):
            if abs(x - 15.5) / 3 + abs(y - 15.5) / (2.5 + pulse) <= 1:
                put(cv, x, y, HI if abs(x - 15.5) + abs(y - 15.5) > 1.5 else MID)
    return cv


def orb(f):
    cv = Canvas(32, 32)
    base(cv)
    # 조여 드는 고리 두 겹(프레임마다 안쪽으로)
    for r in (14 - f * 2, 14 - ((f + 2) % 4) * 2):
        for k in range(int(r * 7)):
            a = k / int(r * 7) * math.tau
            x, y = 15.5 + math.cos(a) * r, 15.5 + math.sin(a) * r
            if 0.5 <= x <= 30.5 and 0.5 <= y <= 30.5:
                put(cv, x, y, HI if r > 6 else MID)
    # 바깥 테두리 점선(돌아감)
    for i in range(1, 31):
        for p in ((i, 1), (i, 30), (1, i), (30, i)):
            put(cv, *p, HI if (i + f) % 4 < 2 else MID)
    # 가운데 해골
    skull = ["  #####  ", " ####### ", "#########", "##..#..##", "##..#..##", "#########", " ###.### ", "  #.#.#  ", "  #####  "]
    for j, row in enumerate(skull):
        for i, ch in enumerate(row):
            if ch == '#':
                put(cv, 12 + i, 11 + j, HI)
            elif ch == '.':
                cv.set(12 + i, 11 + j, W + (BASE,))
    return cv


if __name__ == '__main__':
    out = sys.argv[1]
    for name, fn in (('tel_checker', checker), ('tel_orb', orb)):
        os.makedirs(os.path.join(out, name), exist_ok=True)
        for f in range(4):
            fn(f).save(os.path.join(out, name, f'{f}.png'))
