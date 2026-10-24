"""두더지 잡기 미니게임 그림 리드로우(10-24) - Sprites/Props/Mole_{Hole,Normal,Gold,Bomb,Hit}.png, 32x32.
구멍(뒤 테두리·깊이) → 두더지 → 앞 흙 테두리 순으로 그려 두더지가 구멍에서 솟아오른 것처럼 보인다.
python draw_moles.py <Sprites/Props 폴더>
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, fill, part, poly

S = 32
DIRT = [(62, 40, 26), (96, 64, 40), (128, 90, 58), (156, 116, 78)]
FUR = [(70, 44, 34), (104, 70, 52), (136, 96, 70), (168, 126, 94)]
FUR_GOLD = [(170, 110, 20), (226, 168, 40), (255, 214, 80), (255, 246, 170)]
FUR_BOMB = [(58, 34, 32), (88, 54, 48), (116, 74, 64), (146, 100, 86)]
MUZZLE = [(176, 132, 104), (214, 176, 144), (236, 206, 178), (250, 232, 210)]
PINK = [(170, 70, 90), (220, 110, 130), (246, 160, 170), (255, 210, 216)]
CX, HY = 16, 12  # 머리 가운데


def hole_back(cv, rng):
    inner = ellipse(CX, 25, 12.5, 4.2)
    rim = ellipse(CX, 25, 15.5, 6.2) - inner
    part(cv, rim, DIRT, 'sphere', light=(-0.2, -0.6))
    for (x, y) in inner:  # 깊이 - 가운데로 갈수록 어둡게
        d = math.hypot((x + 0.5 - CX) / 12.5, (y + 0.5 - 25) / 4.2)
        cv.set(x, y, (16, 10, 8) if d < 0.65 else (34, 22, 16) if d < 0.9 else (50, 32, 22))
    return inner, rim


def hole_front(cv, rim, rng):
    """앞쪽(아래 반) 흙 테두리 - 두더지 몸 아래를 덮는다 + 자갈·풀."""
    front = {p for p in rim if p[1] >= 24}
    part(cv, front, DIRT, 'span', light=(-0.3, -0.5))
    for _ in range(5):
        x, y = rng.choice(sorted(front))
        if (x, y) not in boundary(front):
            cv.set(x, y, (150, 140, 130))
            cv.set(x + 1, y, (100, 94, 90))
    for gx in (4, 27):  # 풀
        for k, h in enumerate((3, 4, 2)):
            for t in range(h):
                cv.set(gx + k, 24 - t, (70, 130, 60) if t < h - 1 else (120, 180, 90))


def mole(cv, fur, squash=0, face='normal'):
    hy = HY + squash
    head = ellipse(CX, hy, 8.5, 8.5 - squash * 0.6) | poly([(CX - 8.5, hy), (CX + 8.5, hy), (CX + 9, 26), (CX - 9, 26)])
    part(cv, head, fur, 'sphere', light=(-0.35, -0.5))
    rng = random.Random(3)
    for _ in range(14):  # 털 결
        x, y = rng.randint(CX - 7, CX + 7), rng.randint(hy - 7, 24)
        if (x, y) in head and (x, y) not in boundary(head) and (x, y + 1) in head:
            cv.set(x, y, fur[0] if rng.random() < 0.6 else fur[3])
    muzzle = ellipse(CX, hy + 5, 5.5, 3.6)
    part(cv, muzzle, MUZZLE, 'sphere', outline=fur[0])
    nose = ellipse(CX, hy + 3, 2.2, 1.6)
    part(cv, nose, PINK, 'sphere', outline=(110, 40, 60))
    cv.set(CX - 1, hy + 2, (255, 240, 244))
    for x in (CX - 1, CX):  # 앞니
        cv.set(x, hy + 7, (255, 255, 250))
        cv.set(x, hy + 8, (220, 220, 214))
    for x in range(CX - 2, CX + 2):
        cv.set(x, hy + 6, MUZZLE[0])
    ex = (CX - 4, CX + 3)
    if face == 'hit':  # 소용돌이 눈
        for x0 in ex:
            for (dx, dy) in ((0, 0), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)):
                if (dx, dy) in ((0, 0), (1, 0), (1, 1), (-1, -1), (0, -1)):
                    cv.set(x0 + dx, hy - 1 + dy, OUT)
    else:
        for x0 in ex:
            for (dx, dy) in ((0, 0), (1, 0), (0, 1), (1, 1)):
                cv.set(x0 + dx, hy - 1 + dy, OUT)
            cv.set(x0, hy - 1, (255, 255, 255))
        if face == 'angry':
            for i, x0 in enumerate(ex):
                for k in range(3):
                    cv.set(x0 - 1 + k, hy - 3 + (k if i == 0 else 2 - k) // 1 - 1, OUT)
    for x0 in (CX - 6, CX + 5):  # 볼 홍조
        cv.set(x0, hy + 2, PINK[2])
        cv.set(x0 + 1, hy + 2, PINK[1])
    for side in (-1, 1):  # 앞발 - 구멍 테두리를 짚음
        paw = ellipse(CX + side * 7, 24, 3, 2)
        part(cv, paw, PINK, 'sphere', outline=(110, 40, 60))
        for k in (-1, 0, 1):
            cv.set(CX + side * 7 + k, 26, (255, 250, 240))


def sparkle(cv, x, y, c=(255, 250, 200)):
    for (dx, dy) in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2)):
        cv.set(x + dx, y + dy, c if abs(dx) + abs(dy) <= 1 else (255, 220, 120))


def make(kind):
    rng = random.Random(11)
    cv = Canvas(S, S)
    inner, rim = hole_back(cv, rng)
    if kind == 'Hole':
        hole_front(cv, rim, rng)
        return cv
    if kind == 'Normal':
        mole(cv, FUR)
    elif kind == 'Gold':
        mole(cv, FUR_GOLD)
        crown = poly([(CX - 5, 6), (CX + 5, 6), (CX + 5, 1.5), (CX + 2.5, 4), (CX, 0.5), (CX - 2.5, 4), (CX - 5, 1.5)])
        crown = {p for p in crown if p[1] >= 0}
        part(cv, crown, FUR_GOLD, 'vspan', outline=(120, 70, 10))
        cv.set(CX, 4, (220, 40, 60))
        sparkle(cv, 4, 6)
        sparkle(cv, 27, 9)
    elif kind == 'Bomb':
        mole(cv, FUR_BOMB, face='angry')
        bomb = ellipse(CX, 3.5, 5.5, 4)
        part(cv, bomb, [(20, 20, 26), (40, 40, 50), (64, 64, 78), (110, 110, 130)], 'sphere')
        for x in range(CX - 5, CX + 6):
            if (x, 6) in bomb and (x, 6) not in boundary(bomb):
                cv.set(x, 6, (200, 40, 50))
        for k in range(4):  # 심지 + 불꽃
            cv.set(CX + 4 + k // 2, 0 - (k % 2) + 0, (150, 120, 80))
        for (x, y, c) in ((CX + 7, 0, (255, 240, 120)), (CX + 8, 1, (255, 160, 40)), (CX + 6, 1, (255, 160, 40))):
            cv.set(x, y, c)
    elif kind == 'Hit':
        mole(cv, FUR, squash=3, face='hit')
        bump = ellipse(CX + 3, 6.5, 2.6, 2.2)  # 머리 위 혹(붉게 부어오름)
        part(cv, bump, [(150, 50, 60), (210, 90, 100), (240, 140, 150), (255, 200, 205)], 'sphere', outline=FUR[0])
        for (x, y) in ((6, 4), (16, 1), (26, 4)):  # 머리 위를 도는 별
            sparkle(cv, x, y, (255, 240, 140))
    hole_front(cv, rim, rng)
    return cv


def gold_on_old_hole(old_hole_path):
    """새 황금 두더지를 예전 구멍(Mole_Hole) 위에 얹는다(10-24 사용자 - 구멍은 예전 것 유지, 황금 두더지만 교체).
    예전 구멍을 깔고 → 두더지 → 예전 구멍의 앞쪽 테두리(아래 반)를 다시 덮고 → 앞발만 다시 그린다."""
    from pix import read_png
    w, h, old = read_png(old_hole_path)
    cv = Canvas(S, S)
    for y in range(h):
        for x in range(w):
            if old[y][x][3]:
                cv.set(x, y, old[y][x])
    rim_y = min(y for y in range(h) for x in range(w) if old[y][x][3])  # 구멍 맨 윗줄
    mid = rim_y + (max(y for y in range(h) for x in range(w) if old[y][x][3]) - rim_y) // 2
    mole(cv, FUR_GOLD)
    for y in range(mid + 1, h):  # 예전 구멍 앞 테두리
        for x in range(w):
            c = old[y][x]
            if c[3] and (c[0] + c[1] + c[2]) > 90:  # 어두운 구멍 속은 빼고 흙 테두리만
                cv.set(x, y, c)
    for side in (-1, 1):  # 앞발을 테두리 위로
        paw = ellipse(CX + side * 7, mid, 3, 2)
        part(cv, paw, PINK, 'sphere', outline=(110, 40, 60))
        for k in (-1, 0, 1):
            cv.set(CX + side * 7 + k, mid + 2, (255, 250, 240))
    crown = poly([(CX - 5, 6), (CX + 5, 6), (CX + 5, 1.5), (CX + 2.5, 4), (CX, 0.5), (CX - 2.5, 4), (CX - 5, 1.5)])
    part(cv, {p for p in crown if p[1] >= 0}, FUR_GOLD, 'vspan', outline=(120, 70, 10))
    cv.set(CX, 4, (220, 40, 60))
    sparkle(cv, 4, 6)
    sparkle(cv, 27, 9)
    return cv


if __name__ == '__main__':
    out = sys.argv[1]
    for k in ('Hole', 'Normal', 'Gold', 'Bomb', 'Hit'):
        make(k).save(os.path.join(out, f'Mole_{k}.png'))
