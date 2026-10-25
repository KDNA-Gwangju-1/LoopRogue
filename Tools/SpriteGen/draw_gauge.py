"""기믹 게이지 UI(10-25) - 화면 위 층별 남은 턴 막대.
UI/Common/GaugeFrame(14x12, 9-slice 테 4·4·4·4) : 쇠 테 + 양 끝 놋쇠 리벳, 가운데 투명(채움이 비친다)
UI/Common/GaugeFill(8x8, 9-slice 좌우 1)       : 흰 채움(실행 중 색을 곱함) - 위 반짝임 줄, 아래 그늘
UI/Common/GaugeIconSlot(16x16, 9-slice 4)      : 막대 왼쪽 아이콘 받침
UI/Icons/Gimmick_*(16x16)                      : 층별 기믹 아이콘
python draw_gauge.py <Resources 폴더> [미리보기.png]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, glow, line, part, poly, sheet, thick_line

IRON = [(34, 34, 44), (58, 60, 74), (92, 96, 114), (136, 142, 162)]
BRASS = [(110, 72, 28), (170, 122, 50), (214, 168, 80), (248, 216, 136)]
STONE = [(62, 60, 70), (98, 96, 108), (136, 134, 146), (176, 174, 186)]
EARTH = [(58, 38, 26), (96, 64, 40), (134, 94, 58), (170, 128, 82)]
CYAN = [(30, 92, 120), (60, 160, 196), (120, 216, 240), (210, 250, 255)]
PINK = [(110, 30, 60), (186, 60, 104), (232, 110, 150), (255, 186, 210)]
GREEN = [(30, 76, 44), (56, 130, 70), (100, 186, 100), (176, 236, 160)]
PURPLE = [(48, 26, 76), (88, 50, 132), (136, 90, 190), (196, 160, 240)]
RED = [(110, 20, 24), (176, 40, 40), (226, 80, 70), (255, 160, 140)]
WAX = [(150, 130, 100), (206, 188, 150), (236, 224, 192), (255, 250, 232)]
SILVER = [(80, 90, 110), (140, 152, 176), (196, 208, 228), (244, 248, 255)]
GOLD = [(140, 96, 20), (210, 160, 40), (250, 210, 80), (255, 246, 190)]


def rect(x0, y0, x1, y1):
    return {(x, y) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)}


# ------------------------------------------------------------------ 막대(로스트아크 아이덴티티 게이지 느낌 - 10-25 사용자)
FW, FH = 20, 14          # 테: 양 끝 뾰족한 금장 마개(9-slice 좌우 6, 위아래 4), 가운데 6줄이 채움 자리
GOLDF = [(96, 62, 18), (160, 112, 36), (214, 168, 70), (252, 226, 150)]


def gauge_frame():
    cv = Canvas(FW, FH)
    shape = poly([(0, 7), (5.5, 0), (FW - 5.5, 0), (FW, 7), (FW - 5.5, FH), (5.5, FH)])
    hole = rect(6, 4, FW - 7, 9)
    body = shape - hole
    edge = boundary(shape)
    for x, y in body:
        if (x, y) in edge:
            cv.set(x, y, OUT)
        elif y in (1, 2) and 4 < x < FW - 5:
            cv.set(x, y, GOLDF[3] if y == 1 else GOLDF[2])
        elif y == 3 and 5 < x < FW - 6:
            cv.set(x, y, (24, 16, 10))          # 채움 위 그늘 홈
        elif y == FH - 4 and 5 < x < FW - 6:
            cv.set(x, y, GOLDF[0])
        elif y >= FH - 3 and 4 < x < FW - 5:
            cv.set(x, y, GOLDF[1] if y == FH - 3 else GOLDF[0])
        else:                                     # 양 끝 마개 - 위 밝고 아래 어둡게
            cv.set(x, y, GOLDF[3] if y < 4 else GOLDF[2] if y < 7 else GOLDF[1] if y < 10 else GOLDF[0])
    for x, y in boundary(hole):                   # 채움 칸 둘레 1px 어두운 테두리 안쪽은 비움(채움이 비친다)
        pass
    for x0, sgn in ((2, 1), (FW - 3, -1)):        # 마개에 박힌 작은 보석
        cv.set(x0, 6, (150, 230, 255))
        cv.set(x0, 7, (60, 150, 210))
        cv.set(x0 + sgn, 6, (40, 110, 170))
        cv.set(x0 + sgn, 7, (30, 70, 120))
    for x in (5, FW - 6):                         # 마개와 몸통 이음새
        for y in range(3, FH - 3):
            cv.set(x, y, OUT)
    return cv


def gauge_fill():
    """흰 액체(색을 곱함) - 위 1줄 반짝, 가운데 밝은 심, 아래로 어두워짐."""
    cv = Canvas(8, 6)
    shades = [255, 236, 218, 196, 166, 132]
    for y in range(6):
        for x in range(8):
            v = shades[y]
            cv.set(x, y, (v, v, v))
    return cv


def gauge_shine():
    """채움 위를 왼→오 흐르는 사선 광택(흰색 반투명)."""
    cv = Canvas(16, 6)
    for y in range(6):
        for x in range(16):
            d = abs((x - 8) + (y - 2.5) * 0.9)
            a = max(0.0, 1 - d / 4.5)
            if a > 0:
                q = round(a * 3) / 3
                if q > 0:
                    cv.set(x, y, (255, 255, 255, int(200 * q)))
    return cv


def gauge_edge():
    """채움 맨 앞 빛나는 끝(흰 심 + 양옆 번짐)."""
    cv = Canvas(6, 6)
    alpha = [40, 120, 255, 255, 120, 40]
    for y in range(6):
        for x in range(6):
            cv.set(x, y, (255, 255, 255, alpha[x]))
    return cv


def gauge_glow():
    """막대 둘레 발광(9-slice 7) - 가득 찼을 때 색을 입혀 맥박."""
    cv = Canvas(16, 16)
    for y in range(16):
        for x in range(16):
            dx = max(0, 7 - x, x - 8)
            dy = max(0, 7 - y, y - 8)
            d = math.hypot(dx, dy)
            if d <= 0:
                continue
            a = max(0.0, 1 - d / 7.5) ** 1.6
            q = round(a * 4) / 4
            if q > 0:
                cv.set(x, y, (255, 255, 255, int(230 * q)))
    return cv


def icon_slot():
    """둥근 금장 보석 받침(18x18) - 아이콘이 가운데."""
    cv = Canvas(18, 18)
    outer = ellipse(9, 9, 9, 9)
    inner = ellipse(9, 9, 6.4, 6.4)
    ob = boundary(outer)
    for x, y in outer - inner:
        if (x, y) in ob:
            cv.set(x, y, OUT)
            continue
        t = (y + 0.5 - 9) / 9 - (x + 0.5 - 9) / 18   # 왼쪽 위가 밝게
        cv.set(x, y, GOLDF[3] if t < -0.45 else GOLDF[2] if t < 0.05 else GOLDF[1] if t < 0.5 else GOLDF[0])
    for x, y in inner:
        d = math.hypot(x + 0.5 - 9, y + 0.5 - 9) / 6.4
        v = int(14 + 22 * (1 - d))
        cv.set(x, y, (v, v - 2, v + 8, 245))
    for x, y in boundary(inner):
        cv.set(x, y, (24, 16, 10))
    return cv


# ------------------------------------------------------------------ 아이콘 16x16
def i_hole():
    cv = Canvas(16, 16)
    rim = ellipse(8, 9, 7.2, 5.2)
    part(cv, rim, EARTH, 'sphere')
    pit = ellipse(8, 9.6, 4.8, 3.2)
    for x, y in pit:
        cv.set(x, y, (8, 4, 10) if y > 8 else (24, 14, 18))
    for p in line(2, 7, 4, 5) + line(13, 7, 15, 4) + line(11, 13, 13, 15):  # 갈라진 금
        cv.set(p[0], p[1], OUT)
    return cv


def i_might():
    cv = Canvas(16, 16)
    rock = poly([(1, 15), (2, 10), (6, 8), (11, 9), (15, 12), (15, 15)])
    part(cv, rock, EARTH, 'sphere')
    arrow = poly([(8, 0.5), (13.5, 6), (10.5, 6), (10.5, 11), (5.5, 11), (5.5, 6), (2.5, 6)])
    part(cv, arrow, GREEN, 'sphere')
    return cv


def i_pillar():
    cv = Canvas(16, 16)
    part(cv, rect(3, 1, 12, 3), STONE, 'span')
    part(cv, rect(5, 3, 10, 12), STONE, 'span')
    part(cv, rect(3, 12, 12, 14), STONE, 'span')
    for y in range(5, 12):
        cv.set(7, y, STONE[0])
    for p in line(9, 5, 8, 9):  # 금
        cv.set(p[0], p[1], OUT)
    return cv


def i_crystal():
    cv = Canvas(16, 16)
    gem = poly([(8, 0.5), (13.5, 7), (8, 15.5), (2.5, 7)])
    part(cv, gem, CYAN, 'sphere')
    for p in line(8, 2, 8, 13):
        if (p[0], p[1]) in gem and p not in boundary(gem):
            cv.set(p[0], p[1], CYAN[2] if p[1] < 7 else CYAN[1])
    cv.set(6, 4, CYAN[3])
    cv.set(5, 6, CYAN[3])
    return cv


def i_slime():
    cv = Canvas(16, 16)
    blob = ellipse(8, 11, 7.3, 4.2) | ellipse(8, 7.5, 4.6, 5) | ellipse(3, 13, 2, 1.6) | ellipse(13.5, 12.5, 2, 1.6)
    part(cv, blob, PINK, 'sphere')
    cv.set(5, 5, PINK[3])
    cv.set(6, 4, PINK[3])
    cv.set(6, 9, OUT)
    cv.set(10, 9, OUT)
    return cv


def i_wave():
    cv = Canvas(16, 16)
    for r, c in ((11.5, PINK[1]), (8.5, PINK[2]), (5.5, PINK[3])):
        for a in range(-50, 51, 4):
            t = math.radians(a)
            x, y = 1 + r * math.cos(t), 8 + r * math.sin(t)
            cv.set(int(round(x)), int(round(y)), c)
            cv.set(int(round(x)) - 1, int(round(y)), OUT)
    part(cv, ellipse(2, 8, 2.4, 3.2), PINK, 'sphere')
    return cv


def i_spore():
    cv = Canvas(16, 16)
    part(cv, rect(6, 9, 9, 14), WAX, 'span')
    cap = ellipse(8, 8, 7.4, 5.2) - rect(0, 9, 15, 15)
    part(cv, cap, GREEN, 'sphere')
    for x, y in ((5, 5), (9, 4), (11, 7), (4, 7)):
        cv.set(x, y, GREEN[3])
    for x, y in ((2, 1), (13, 2), (14, 11), (1, 12)):  # 날리는 포자
        cv.set(x, y, (190, 240, 150))
    return cv


def i_candle():
    cv = Canvas(16, 16)
    part(cv, rect(5, 8, 10, 15), WAX, 'span')
    cv.set(9, 9, WAX[0])
    cv.set(9, 10, WAX[0])
    flame = poly([(8, -0.5), (11.5, 4.5), (10.5, 7.5), (5.5, 7.5), (4.5, 4.5)])
    part(cv, flame, [(220, 100, 20), (250, 160, 40), (255, 220, 100), (255, 250, 210)], 'sphere', light=(0, 0.4))
    cv.set(8, 7, OUT)
    cv.set(8, 6, (255, 250, 210))
    return cv


def i_target():
    cv = Canvas(16, 16)
    for r, c in ((7.5, RED), (5.5, [(200, 196, 210)] * 2 + [(240, 236, 246)] * 2), (3.5, RED), (1.6, [(240, 236, 246)] * 4)):
        part(cv, ellipse(8, 8, r, r), c, 'flat', outline=OUT if r > 7 else None)
    return cv


def i_clone():
    cv = Canvas(16, 16)
    for cx, cy, ramp in ((10.5, 0, [(36, 22, 54), (52, 34, 76), (70, 48, 100), (90, 64, 124)]), (6, 1, PURPLE)):
        body = ellipse(cx, cy + 4.5, 3, 3) | poly([(cx - 4.5, cy + 15), (cx - 3, cy + 7.5), (cx + 3, cy + 7.5), (cx + 4.5, cy + 15)])
        part(cv, body, ramp, 'sphere')
        cv.set(int(cx) - 1, cy + 4, (255, 90, 90))
        cv.set(int(cx) + 1, cy + 4, (255, 90, 90))
    return cv


def i_beat():
    cv = Canvas(16, 16)
    for r, a in ((7.5, 0.5), (5, 0.8)):
        for p in boundary(ellipse(8, 8, r, r)):
            cv.blend(p[0], p[1], PURPLE[3], a)
    part(cv, ellipse(8, 8, 3, 3), PURPLE, 'sphere')
    return cv


def i_wall():
    cv = Canvas(16, 16)
    for y in range(0, 16, 4):
        off = 0 if (y // 4) % 2 == 0 else 2
        for x0 in range(-off, 16, 5):
            m = rect(max(0, x0), y, min(15, x0 + 4), y + 3)
            part(cv, m, EARTH, 'vspan')
    for side in (-1, 1):  # 양쪽 벽에서 가운데로 조여 오는 화살표(왼쪽 → / 오른쪽 ←)
        base = 8 + side * 3.5
        head = poly([(base, 3.5), (base, 12.5), (8 + side * 0.5, 8)])
        shaft = rect(1, 7, 4, 9) if side < 0 else rect(11, 7, 14, 9)
        part(cv, head | shaft, RED, 'sphere')
    return cv


def i_doom():
    cv = Canvas(16, 16)
    for p in boundary(ellipse(8, 8, 7, 7)):
        cv.set(p[0], p[1], PURPLE[2])
    skull = ellipse(8, 7, 4.2, 4) | rect(6, 10, 10, 12)
    part(cv, skull, [(150, 150, 170), (200, 200, 214), (232, 232, 240), (255, 255, 255)], 'sphere')
    for x, y in ((6, 7), (10, 7), (6, 8), (10, 8)):
        cv.set(x, y, (200, 30, 60))
    cv.set(8, 9, OUT)
    for x in (7, 9):
        cv.set(x, 12, OUT)
    return cv


def i_mirror():
    cv = Canvas(16, 16)
    part(cv, rect(7, 11, 8, 15), BRASS, 'span')
    part(cv, ellipse(7.5, 6, 6, 6), BRASS, 'sphere')
    glass = ellipse(7.5, 6, 4.2, 4.2)
    part(cv, glass, SILVER, 'sphere', outline=None)
    for p in line(5, 7, 8, 4):
        cv.set(p[0], p[1], SILVER[3])
    return cv


def i_light():
    cv = Canvas(16, 16)
    glow(cv, 8, 8, 7, (255, 230, 120), 0.25)
    for a in range(0, 360, 45):
        t = math.radians(a)
        for p in line(8 + 4 * math.cos(t), 8 + 4 * math.sin(t), 8 + 7 * math.cos(t), 8 + 7 * math.sin(t)):
            cv.set(p[0], p[1], GOLD[2])
    part(cv, ellipse(8, 8, 3.6, 3.6), GOLD, 'sphere')
    return cv


def i_hourglass():
    cv = Canvas(16, 16)
    part(cv, rect(2, 0, 13, 1), [(70, 44, 26), (108, 72, 42), (144, 102, 62), (176, 132, 86)], 'span')
    part(cv, rect(2, 14, 13, 15), [(70, 44, 26), (108, 72, 42), (144, 102, 62), (176, 132, 86)], 'span')
    glass = poly([(3, 2), (13, 2), (8.6, 8), (13, 14), (3, 14), (7.4, 8)])
    part(cv, glass, [(120, 150, 170), (170, 200, 220), (210, 230, 244), (240, 250, 255)], 'flat')
    for x, y in glass:  # 모래 - 아래쪽에 쌓이고 위에 조금 남음
        if (y >= 11 and abs(x - 8) <= (y - 8)) or (4 <= y <= 5 and abs(x - 8) <= 7 - y) or (x in (7, 8) and 8 <= y <= 10):
            if (x, y) not in boundary(glass):
                cv.set(x, y, (236, 190, 90) if y > 12 else (250, 214, 120))
    return cv


ICONS = {
    'Hole': i_hole, 'Might': i_might, 'Pillar': i_pillar, 'Crystal': i_crystal,
    'Slime': i_slime, 'SlimeWave': i_wave, 'Spore': i_spore, 'Candle': i_candle,
    'Target': i_target, 'Clone': i_clone, 'Beat': i_beat, 'Wall': i_wall,
    'Doom': i_doom, 'Mirror': i_mirror, 'Light': i_light, 'Hourglass': i_hourglass,
}


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else None
    preview = sys.argv[2] if len(sys.argv) > 2 else 'gauge_preview.png'
    common = {'GaugeFrame': gauge_frame(), 'GaugeFill': gauge_fill(), 'GaugeIconSlot': icon_slot(),
              'GaugeShine': gauge_shine(), 'GaugeEdge': gauge_edge(), 'GaugeGlow': gauge_glow()}
    icons = {k: f() for k, f in ICONS.items()}
    if root:
        for k, cv in common.items():
            cv.save(os.path.join(root, 'UI', 'Common', k + '.png'))
        for k, cv in icons.items():
            cv.save(os.path.join(root, 'UI', 'Icons', 'Gimmick_' + k + '.png'))
    frames = []
    for cv in list(common.values()) + list(icons.values()):
        f = Canvas(20, 18)
        f.px = dict(cv.px)
        frames.append(f)
    sheet(frames, preview, scale=8, cols=10)


if __name__ == '__main__':
    main()
