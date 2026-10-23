"""9층 리치 낙인 구체 등장 이펙트 doom_summon(10-23) - 32x32 8프레임.
초록 영혼 불티가 소용돌이치며 모여(0~3) 해골 핵으로 뭉쳤다가(4) 고리로 퍼지며 사라진다(5~7).
뒤이어 그 자리에 soul_wisp(떠 있는 구체)가 남으므로 끝은 비워 둔다. 색은 soul_wisp와 같은 초록·보라.
python draw_fx9.py <Effects 폴더>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import Canvas, ellipse, glow

GREEN = (84, 226, 128)
LIGHT = (184, 255, 204)
WHITE = (240, 255, 245)
PURPLE = (112, 56, 168)
DARK = (52, 24, 82)
C = 15.5


def spark(cv, x, y, a=1.0, big=False):
    cv.blend(int(x), int(y), WHITE if big else LIGHT, a)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.blend(int(x) + dx, int(y) + dy, GREEN, a * (0.8 if big else 0.45))


def swirl(cv, f, r):
    """불티 6개가 반지름 r에서 돌며 모인다 - 지나온 길에 보라 꼬리."""
    for i in range(6):
        a0 = i / 6 * math.tau + f * 0.55
        for k in range(4):  # 꼬리(뒤로 갈수록 바깥·옅게)
            rr = r + k * 1.6
            aa = a0 - k * 0.22
            x, y = C + math.cos(aa) * rr, C + math.sin(aa) * rr
            if k == 0:
                spark(cv, x, y, 1.0)
            else:
                cv.blend(int(x), int(y), PURPLE if k > 1 else GREEN, 0.7 - k * 0.15)


def core(cv, r, skull=False):
    glow(cv, C, C, r + 5, GREEN, 0.45)
    m = ellipse(C, C, r, r)
    for x, y in m:
        d = math.hypot(x + 0.5 - C + 1, y + 0.5 - C + 1) / r
        cv.set(x, y, WHITE if d < 0.45 else LIGHT if d < 0.8 else GREEN)
    for x, y in m:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if (x + dx, y + dy) not in m:
                cv.set(x + dx, y + dy, DARK)
    if skull:
        rows = ["  ###  ", " ##### ", "##.#.##", "#######", " #.#.# "]
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch == '.':
                    cv.set(12 + i, 13 + j, DARK)


def ring(cv, r, a):
    n = int(r * 8)
    for k in range(n):
        t = k / n * math.tau
        x, y = C + math.cos(t) * r, C + math.sin(t) * r
        cv.blend(int(x), int(y), LIGHT, a)
        x2, y2 = C + math.cos(t) * (r - 1), C + math.sin(t) * (r - 1)
        cv.blend(int(x2), int(y2), GREEN, a * 0.6)


def burst_sparks(cv, r, a, f):
    for i in range(8):
        t = i / 8 * math.tau + 0.2
        x, y = C + math.cos(t) * r, C + math.sin(t) * r
        spark(cv, x, y, a, big=i % 2 == 0)
        cv.blend(int(C + math.cos(t) * (r - 2)), int(C + math.sin(t) * (r - 2)), PURPLE, a * 0.5)


def frame(f):
    cv = Canvas(32, 32)
    if f <= 3:
        r = [13, 10, 7, 4.5][f]
        glow(cv, C, C, 4 + f * 2, GREEN, 0.15 + f * 0.1)
        swirl(cv, f, r)
        if f == 3:
            core(cv, 2)
    elif f == 4:
        core(cv, 4.5, skull=True)
    elif f == 5:
        ring(cv, 7, 0.95)
        core(cv, 3)
        burst_sparks(cv, 9, 1.0, f)
    elif f == 6:
        ring(cv, 11, 0.7)
        glow(cv, C, C, 5, GREEN, 0.4)
        burst_sparks(cv, 12.5, 0.75, f)
    else:
        ring(cv, 14, 0.35)
        burst_sparks(cv, 14.5, 0.4, f)
    return cv


if __name__ == '__main__':
    out = os.path.join(sys.argv[1], 'doom_summon')
    os.makedirs(out, exist_ok=True)
    for f in range(8):
        frame(f).save(os.path.join(out, f'{f}.png'))
