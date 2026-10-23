"""낙인 해골·제단 불붙이기 이펙트(10-23).
doom_mark (96x96 10프레임, 1.875칸으로 재생 = 용사 그림과 같은 도트 크기): 발밑에서 영혼 불티가 솟아 커다란 유령 해골이 용사를
  머금듯 나타나고 → 턱을 닫아 감싼 뒤 → 조여 들어 가슴에 작은 해골 낙인으로 박힌다.
altar_ignite (48x48 8프레임, 1.5칸으로 재생 = 제단 그림과 같은 도트 크기, 그릇은 (24, 18)): 푸른 불씨가 그릇에 떨어져
  불꽃이 칸 위까지 확 솟았다가 제단(Altar_Lit) 불꽃 크기로 가라앉는다.
python draw_fx_extra.py <Effects 폴더>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import Canvas, boundary, glow

GREEN = (84, 226, 128)
LIGHT = (184, 255, 204)
WHITE = (240, 255, 245)
PURPLE = (112, 56, 168)
DARK = (52, 24, 82)

# ---------------------------------------------------------------- 낙인 해골
MX, MY = 48, 44  # 해골 가운데(용사 몸통 위쪽 - 용사는 이 캔버스에서 x34~64, y29~74)


def skull_mask(s, jaw, part=1.0):
    """해골 칸 집합들 - (몸, 눈구멍, 코, 이빨 틈). s=크기, jaw=아래턱 벌어짐(px), part=아래부터 보이는 비율."""
    body, eyes, nose = set(), set(), set()
    for y in range(96):
        for x in range(96):
            u, v = (x + 0.5 - MX) / s, (y + 0.5 - MY) / s
            inside = False
            if (u / 25) ** 2 + ((v + 2) / 23) ** 2 <= 1 and v < 14:
                inside = True
            elif 10 <= v < 25 and abs(u) < 19 - (v - 10) * 0.45:
                inside = True
            elif 25 <= v < 28 and abs(u) < 13:   # 윗니
                inside = abs(u) % 3.2 > 0.6 or v < 26
            elif 24 <= v < 30 + jaw and 12 <= abs(u) < 15.5:  # 턱뼈(벌어져도 두개골과 이어짐)
                inside = True
            elif 28 + jaw <= v < 40 + jaw and abs(u) < 15 - max(0, v - jaw - 33) * 0.9:  # 아래턱
                inside = not (v < 31 + jaw and abs(u) < 12.5 and abs(u) % 3.2 <= 0.6)
            if not inside:
                continue
            if part < 1 and (y - (MY - 25 * s)) < (1 - part) * (65 * s):
                continue
            if ((u + 10.5) / 6.5) ** 2 + ((v - 4) / 7.5) ** 2 <= 1 or ((u - 10.5) / 6.5) ** 2 + ((v - 4) / 7.5) ** 2 <= 1:
                eyes.add((x, y))
            elif 12 <= v < 19 and abs(u) < (v - 11) * 0.45:
                nose.add((x, y))
            body.add((x, y))
    return body, eyes, nose


def ghost_skull(cv, s, jaw, a_fill, a_line, part=1.0, flare=0.0):
    body, eyes, nose = skull_mask(s, jaw, part)
    edge = boundary(body)
    for p in body:
        if p in eyes or p in nose:
            cv.blend(p[0], p[1], DARK, a_fill * 2.2)
        elif p in edge:
            cv.blend(p[0], p[1], LIGHT, a_line)
        else:
            cv.blend(p[0], p[1], GREEN, a_fill)
    for p in boundary(eyes) | boundary(nose):
        cv.blend(p[0], p[1], GREEN, a_line * 0.8)
    if flare > 0:  # 눈빛
        for ex in (-10.5, 10.5):
            x, y = MX + ex * s, MY + 4 * s
            glow(cv, x, y, 4 + flare * 4, LIGHT, 0.5 * flare)
            cv.blend(int(x), int(y), WHITE, min(1, flare + 0.2))
            cv.blend(int(x) + 1, int(y), WHITE, min(1, flare))


def spark(cv, x, y, a=1.0):
    cv.blend(int(x), int(y), WHITE, a)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.blend(int(x) + dx, int(y) + dy, GREEN, a * 0.55)


def rising(cv, f):
    """발밑 고리 + 양옆으로 솟는 불티."""
    for k in range(14):
        t = k / 14 * math.tau + f * 0.4
        x, y = 49 + math.cos(t) * 22, 76 + math.sin(t) * 5
        cv.blend(int(x), int(y), GREEN if k % 2 else LIGHT, 0.8)
    for i in range(8):
        side = -1 if i % 2 else 1
        h = (f + 1) * 12 - (i // 2) * 6
        if h > 0:
            x = 49 + side * (24 - h * 0.12) + math.sin(h * 0.3 + i) * 2
            spark(cv, x, 76 - h, 0.9)
            cv.blend(int(x), int(76 - h + 3), PURPLE, 0.5)


def mark(cv, a):
    """가슴의 작은 해골 낙인."""
    rows = ["  ###  ", " ##### ", "##.#.##", "#######", " ##.## ", "  # #  "]
    for j, row in enumerate(rows):
        for i, ch in enumerate(row):
            x, y = 46 + i, 45 + j
            cv.blend(x, y, (WHITE if ch == '#' else DARK) if ch != ' ' else LIGHT, a if ch != ' ' else 0)


def doom_frame(f):
    cv = Canvas(96, 96)
    if f <= 1:
        rising(cv, f)
    elif f == 2:
        rising(cv, f)
        ghost_skull(cv, 1.25, 5, 0.07, 0.4)
    elif f == 3:
        ghost_skull(cv, 1.15, 5, 0.16, 0.8, flare=0.4)
    elif f == 4:
        ghost_skull(cv, 1.05, 6, 0.22, 0.95, flare=1.0)
    elif f == 5:  # 턱을 닫아 감쌈
        glow(cv, MX, MY + 26, 14, LIGHT, 0.35)
        ghost_skull(cv, 1.0, 0, 0.28, 1.0, flare=1.0)
    elif f == 6:
        ghost_skull(cv, 0.85, 0, 0.32, 1.0, flare=0.8)
    elif f == 7:
        glow(cv, MX, MY + 4, 14, GREEN, 0.4)
        ghost_skull(cv, 0.6, 0, 0.45, 1.0, flare=0.5)
    elif f == 8:
        glow(cv, MX, MY + 4, 12, LIGHT, 0.6)
        mark(cv, 1.0)
        for k in range(10):
            t = k / 10 * math.tau
            spark(cv, MX + math.cos(t) * 14, MY + 4 + math.sin(t) * 14, 0.9)
    else:
        glow(cv, MX, MY + 4, 7, GREEN, 0.3)
        mark(cv, 0.6)
        for k in range(10):
            t = k / 10 * math.tau + 0.3
            spark(cv, MX + math.cos(t) * 20, MY + 4 + math.sin(t) * 20 - 3, 0.45)
    return cv


# ---------------------------------------------------------------- 제단 불붙이기
B1, B2, B3, B4 = (24, 40, 110), (60, 110, 230), (120, 200, 255), (230, 250, 255)
BX, BY = 24, 18  # 그릇 가운데(불꽃 밑동)


def flame(cv, h, w, sway=0.0, a=1.0, tongues=True):
    """밑동 (BX, BY)에서 위로 h, 밑동 반폭 w - 끝이 뾰족한 불꽃 + 양옆 작은 불혀. 바깥 진파랑 → 하늘 → 흰 속불."""
    if tongues and h > 6:
        for side in (-1, 1):
            tongue(cv, BX + side * w * 0.55, h * 0.5, w * 0.45, sway - side * 0.5, a * 0.9)
    tongue(cv, BX, h, w, sway, a)


def tongue(cv, bx, h, w, sway, a):
    for k in range(int(h) + 1):
        t = k / max(1, h)
        hw = w * (1 - t) ** 1.15 * (1 + 0.35 * math.sin(t * math.pi))
        cx = bx + sway * t * t * 3
        y = BY - k
        for x in range(int(cx - hw - 1), int(cx + hw + 2)):
            d = abs(x + 0.5 - cx) / max(hw, 0.5)
            if d > 1:
                continue
            c = B4 if d < 0.4 and t < 0.55 else B3 if d < 0.75 and t < 0.85 else B2
            cv.blend(x, y, c, a)
        if hw > 0.8:
            for x in (int(cx - hw - 1), int(cx + hw + 1)):
                cv.blend(x, y, B1, a * 0.45)


def ember(cv, x, y, a=1.0):
    cv.blend(int(x), int(y), B4, a)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        cv.blend(int(x) + dx, int(y) + dy, B3, a * 0.5)


def altar_frame(f):
    cv = Canvas(48, 48)
    if f == 0:  # 불씨가 그릇으로
        ember(cv, BX, BY - 8)
        cv.blend(BX, BY - 10, B3, 0.5)
        cv.blend(BX, BY - 12, B2, 0.3)
    elif f == 1:  # 그릇에 닿아 번쩍
        glow(cv, BX, BY - 1, 8, B3, 0.6)
        for dx in (-5, -3, 3, 5):
            ember(cv, BX + dx, BY - 2 - abs(dx) // 2, 0.8)
        flame(cv, 5, 3, tongues=False)
    elif f == 2:  # 확 솟음
        glow(cv, BX, BY - 8, 15, B3, 0.45)
        flame(cv, 20, 6, sway=0.3)
        for i, (dx, dy) in enumerate(((-10, -6), (10, -8), (-7, -14), (8, -15), (-12, -1), (12, -2))):
            ember(cv, BX + dx, BY + dy, 0.9)
    elif f == 3:
        glow(cv, BX, BY - 9, 14, B3, 0.4)
        flame(cv, 21, 6, sway=-0.5)
        for dx, dy in ((-12, -10), (12, -12), (-9, -18), (9, -20)):
            ember(cv, BX + dx, BY + dy, 0.7)
    elif f == 4:
        glow(cv, BX, BY - 7, 12, B3, 0.3)
        flame(cv, 16, 5, sway=0.4)
        for dx, dy in ((-13, -14), (13, -16), (-8, -23), (7, -25)):
            ember(cv, BX + dx, BY + dy, 0.5)
    elif f == 5:
        glow(cv, BX, BY - 5, 10, B3, 0.25)
        flame(cv, 12, 4, sway=-0.2, a=0.8)
        for dx, dy in ((-12, -19), (12, -21), (-6, -27)):
            ember(cv, BX + dx, BY + dy, 0.35)
    else:  # 제단 불꽃(Altar_Lit) 위로 남는 불티
        glow(cv, BX, BY - 4, 8, B3, 0.15 if f == 6 else 0.07)
        for dx, dy in (((-4, -14), (5, -17)) if f == 6 else ((-3, -19), (4, -22))):
            ember(cv, BX + dx, BY + dy, 0.4 if f == 6 else 0.2)
    return cv


if __name__ == '__main__':
    root = sys.argv[1]
    for name, fn, n in (('doom_mark', doom_frame, 10), ('altar_ignite', altar_frame, 8)):
        out = os.path.join(root, name)
        os.makedirs(out, exist_ok=True)
        for f in range(n):
            fn(f).save(os.path.join(out, f'{f}.png'))
