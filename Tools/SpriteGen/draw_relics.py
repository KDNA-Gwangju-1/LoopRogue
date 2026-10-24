"""유물 아이콘 리드로우(10-24) - 24x24. 유물 창 48px(2배)·도감 72px(3배)에 딱 맞는 정수 배율.
상징은 예전 16px 그림 그대로(광전사 = 붉은 귀신 얼굴, 피의 계약 = 두루마리 …), 외곽선·명암·하이라이트를 더했다.
python draw_relics.py <Resources/UI/Relics 폴더> [미리보기.png]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, fill, glow, line, part, poly, thick_line

S = 24
STEEL = [(70, 74, 94), (118, 124, 148), (168, 176, 198), (226, 232, 244)]
DARKSTEEL = [(40, 42, 56), (62, 66, 84), (90, 96, 118), (130, 136, 160)]
GOLD = [(120, 72, 22), (184, 124, 40), (236, 182, 70), (255, 236, 150)]
WOOD = [(70, 40, 24), (110, 68, 38), (148, 98, 56), (186, 134, 84)]
RED = [(110, 20, 30), (176, 38, 46), (226, 72, 70), (255, 150, 130)]
BONE = [(150, 134, 108), (206, 192, 160), (236, 226, 198), (255, 250, 232)]
PAPER = [(170, 150, 112), (214, 196, 156), (238, 226, 194), (255, 250, 230)]
GREEN = [(30, 80, 40), (52, 130, 54), (96, 186, 80), (170, 232, 130)]
BLUE = [(26, 50, 110), (44, 96, 180), (90, 160, 236), (190, 228, 255)]
CYAN = [(20, 80, 100), (40, 150, 170), (110, 220, 230), (220, 255, 255)]
PURPLE = [(56, 30, 90), (96, 56, 150), (150, 100, 210), (210, 170, 255)]
LEATHER = [(80, 48, 26), (128, 80, 40), (176, 118, 60), (214, 162, 98)]
STONE = [(56, 54, 66), (86, 84, 98), (120, 118, 134), (160, 158, 174)]
WHITE = (255, 255, 255)


def hl(cv, pts, c=WHITE):
    for p in pts:
        cv.set(p[0], p[1], c)


def berserker():
    cv = Canvas(S, S)
    for side in (-1, 1):  # 뿔 - 머리 옆에서 굵게 솟아 바깥으로 휨
        horn = set()
        for k in range(8):
            t = k / 7
            horn |= ellipse(12 + side * (6 + t * 4.5), 7 - t * 6, 2.4 * (1 - t) + 0.7, 2.4 * (1 - t) + 0.7)
        part(cv, horn, BONE, 'sphere')
    face = ellipse(12, 13, 8, 8.5) | poly([(6, 18), (18, 18), (16, 23), (8, 23)])
    part(cv, face, RED, 'sphere', light=(-0.3, -0.4))
    for x0 in (7, 14):  # 화난 눈썹 + 노란 눈
        hl(cv, [(x0, 10), (x0 + 1, 10), (x0 + 2, 10)], OUT)
        hl(cv, [(x0 + (0 if x0 < 12 else 2), 9)], OUT)
        hl(cv, [(x0, 11), (x0 + 1, 11), (x0 + 2, 11)], (255, 220, 60))
        cv.set(x0 + 1, 11, OUT)
    for x in range(8, 17):  # 이빨 드러낸 입
        cv.set(x, 17, OUT)
        cv.set(x, 19, OUT)
        cv.set(x, 18, BONE[3] if x % 2 else BONE[1])
    hl(cv, [(8, 16), (16, 16)], BONE[3])
    hl(cv, [(9, 6), (10, 6), (8, 7)], RED[3])
    return cv


def blood_pact():
    cv = Canvas(S, S)
    paper = poly([(3, 3), (18, 3), (18, 21), (3, 21)])
    part(cv, paper, PAPER, 'vspan', bands=(0.1, 0.5, 0.9))
    for y in (3, 21):  # 말린 위아래
        part(cv, thick_line(2, y, 19, y, 3), WOOD, 'span')
    for y in (8, 11, 14):
        for x in range(6, 16 - (3 if y == 14 else 0)):
            cv.set(x, y, PAPER[0] if x % 3 else (120, 100, 70))
    drop = ellipse(18, 18, 4, 4) | poly([(15, 17), (21, 17), (18, 10)])
    part(cv, drop, RED, 'sphere')
    hl(cv, [(17, 15), (16, 17)], RED[3])
    return cv


def chain_blast():
    cv = Canvas(S, S)
    for k, (cx, cy) in enumerate(((5, 18), (10, 13), (15, 8))):
        ring = ellipse(cx, cy, 4.8, 3.8) - ellipse(cx, cy, 1.6, 0.9)
        part(cv, ring, STEEL, 'sphere', light=(-0.3, -0.4))
    glow(cv, 19, 5, 6, (255, 200, 80), 0.7)
    for a in range(0, 360, 45):
        r = 5 if a % 90 == 0 else 3
        for t in range(1, r + 1):
            cv.set(int(19 + math.cos(math.radians(a)) * t), int(5 + math.sin(math.radians(a)) * t), (255, 236, 150) if t < r else (255, 160, 60))
    cv.set(19, 5, WHITE)
    return cv


def charge_horn():
    cv = Canvas(S, S)
    m = set()
    for k in range(20):
        t = k / 19
        x = 3 + t * 15 + math.sin(t * math.pi) * 2
        y = 20 - math.sin(t * math.pi * 0.9) * 14 - t * 2
        m |= ellipse(x, y, 3.2 * (1 - t) + 0.8, 3.2 * (1 - t) + 0.8)
    part(cv, m, BONE, 'span')
    for k in range(4):  # 고리 무늬
        t = 0.15 + k * 0.12
        x = 3 + t * 15 + math.sin(t * math.pi) * 2
        y = 20 - math.sin(t * math.pi * 0.9) * 14 - t * 2
        for d in (-2, -1, 0, 1, 2):
            if (int(x + d * 0.6), int(y + d)) in m:
                cv.set(int(x + d * 0.6), int(y + d), BONE[0])
    for y in (6, 10, 14):  # 돌진 줄
        for x in range(19, 23 - (y == 10)):
            cv.set(x, y, (255, 230, 160))
    return cv


def cross_crest():
    cv = Canvas(S, S)
    sh = poly([(3, 2), (21, 2), (21, 12), (12, 22), (3, 12)])
    part(cv, sh, BLUE, 'sphere')
    inner = {p for p in sh if p not in boundary(sh)}
    for p in boundary(inner):
        cv.set(p[0], p[1], GOLD[2])
    for y in range(4, 19):
        for x in (11, 12):
            if (x, y) in inner:
                cv.set(x, y, RED[2] if x == 11 else RED[1])
    for x in range(5, 19):
        for y in (8, 9):
            if (x, y) in inner:
                cv.set(x, y, RED[2] if y == 8 else RED[1])
    hl(cv, [(5, 4), (6, 4), (5, 5)], BLUE[3])
    return cv


def crown_of_end():
    cv = Canvas(S, S)
    m = poly([(1, 20), (23, 20), (23, 8), (17, 13), (12, 3), (7, 13), (1, 8)]) | poly([(1, 18), (23, 18), (23, 23), (1, 23)])
    part(cv, m, [(170, 100, 20), (226, 160, 40), (255, 210, 80), (255, 244, 170)], 'vspan', bands=(0.2, 0.5, 0.85))
    for x, y in ((1, 7), (12, 2), (23, 7)):
        part(cv, ellipse(x, y, 1.8, 1.8), GOLD, 'sphere')
    part(cv, ellipse(12, 17, 3, 3), PURPLE, 'sphere', outline=GOLD[0])
    for x in (5, 19):
        part(cv, ellipse(x, 20, 1.6, 1.4), RED, 'sphere', outline=GOLD[0])
    hl(cv, [(11, 15)], PURPLE[3])
    return cv


def earth_hammer():
    cv = Canvas(S, S)
    part(cv, thick_line(12, 9, 12, 23, 4), WOOD, 'span')
    head = poly([(1, 1), (23, 1), (23, 11), (1, 11)])
    part(cv, head, STEEL, 'vspan', bands=(0.15, 0.4, 0.8))
    for x in (7, 17):
        for y in range(2, 11):
            cv.set(x, y, STEEL[0])
    for x in range(2, 23):
        cv.set(x, 2, STEEL[3])
    part(cv, thick_line(10, 12, 14, 12, 2), GOLD, 'span')
    return cv


def executioner():
    """사형 집행자 - 사신의 대낫(10-24 사용자): 길고 곧은 자루 끝에서 초승달 날이 왼쪽으로 길게 휘어 내려온다."""
    cv = Canvas(S, S)
    pole = [(58, 36, 26), (92, 60, 40), (126, 88, 58), (160, 118, 80)]
    part(cv, thick_line(18, 23, 16.5, 3, 4), pole, 'span')  # 이음쇠(16.5, 3.5) 바로 아래에서 곧게(10-24 - 어긋나 꺾여 보였다)
    for y in range(14, 21):  # 가죽끈을 감은 손잡이(10-24 사용자 - 옆으로 튀어나온 돌기 대신)
        for x in range(14, 21):
            c = cv.get(x, y)
            if c and c[:3] != OUT and c[3] == 255:
                cv.set(x, y, (54, 34, 30) if (y + x // 3) % 3 == 0 else (88, 56, 44))
    outer = ellipse(10.5, 10.5, 11, 8.5)
    inner = ellipse(8, 14.5, 10, 8)
    blade = {p for p in outer - inner if p[1] <= 13 and p[0] <= 17}
    part(cv, blade, [(60, 62, 80), (110, 114, 136), (166, 172, 192), (226, 232, 246)], 'sphere', light=(0.2, -0.7))
    for p in blade:  # 날 안쪽(벤 쪽) 번뜩임
        if (p[0], p[1] + 1) not in blade and (p[0], p[1] + 1) in inner and p[1] > 3:
            cv.set(p[0], p[1], (240, 244, 255))
    part(cv, ellipse(16.5, 3.5, 2.4, 2.4), [(40, 42, 56), (62, 66, 84), (90, 96, 118), (130, 136, 160)], 'sphere')  # 날 이음쇠
    return cv


def first_strike():
    cv = Canvas(S, S)
    bolt = poly([(14, 0), (3, 13), (11, 13), (7, 24), (21, 9), (13, 9), (19, 0)])
    part(cv, bolt, [(196, 120, 20), (250, 196, 50), (255, 228, 110), (255, 250, 200)], 'span', light=(-0.4, -0.4))
    hl(cv, [(14, 2), (13, 4), (12, 6), (11, 8)], (255, 255, 230))
    return cv


def gale():
    """질풍 - 토네이도(10-24 사용자): 바람 띠가 층층이 쌓여 위는 넓고 아래로 좁아지며 좌우로 흔들린다 + 날리는 부스러기."""
    cv = Canvas(S, S)
    wind = [(50, 120, 140), (96, 180, 198), (160, 228, 238), (226, 252, 255)]
    bands = 8
    for i in reversed(range(bands)):  # 아래 띠부터 그려 위 띠가 덮게
        t = i / (bands - 1)
        y = 3.5 + t * 17.5
        cx = 12 + math.sin(i * 1.1) * 2.6 * (0.4 + t)
        rx = 10.8 * (1 - t) ** 0.85 + 1.6
        band = ellipse(cx, y, rx, 2.1)
        part(cv, band, wind, 'span', light=(-0.25, -0.5))
        for k in range(int(rx * 3)):  # 띠 위쪽 바람 결
            x = int(cx - rx * 0.7 + k * 0.5)
            if (x, int(y - 1)) in band and k % 3:
                cv.set(x, int(y - 1), wind[3])
    for (x, y) in ((2, 8), (21, 12), (5, 17), (19, 4), (17, 19)):  # 날리는 부스러기
        cv.set(x, y, (126, 100, 72))
        cv.set(x + 1, y + 1, (88, 68, 50))
    return cv


def greed():
    cv = Canvas(S, S)
    bag = ellipse(11, 15, 8.5, 7.5) | poly([(7, 7), (15, 7), (14, 10), (8, 10)])
    part(cv, bag, LEATHER, 'sphere')
    part(cv, thick_line(6, 7, 16, 7, 3), GOLD, 'span')
    for (x, y) in ((9, 4), (12, 3), (14, 5)):
        cv.set(x, y, LEATHER[2])
    sign = [(10, 12), (11, 12), (12, 12), (9, 13), (10, 14), (11, 14), (12, 15), (13, 16), (9, 17), (10, 17), (11, 17), (12, 17), (11, 11), (11, 18)]
    hl(cv, sign, GOLD[3])
    for (cx, cy) in ((19, 20), (21, 17)):
        part(cv, ellipse(cx, cy, 2.6, 1.8), GOLD, 'sphere')
    return cv


def hunter_eye():
    cv = Canvas(S, S)
    tube = poly([(3, 19), (16, 6), (20, 10), (7, 23)])
    part(cv, tube, LEATHER, 'span')
    for k in (0, 1):  # 몸통을 감는 금속 띠(몸통 안에서만)
        for p in thick_line(6 + k * 6, 17 - k * 6, 10 + k * 6, 21 - k * 6, 2):
            if p in tube and p not in boundary(tube):
                cv.set(p[0], p[1], GOLD[2] if (p[0] + p[1]) % 2 else GOLD[1])
    part(cv, poly([(14, 4), (19, 1), (23, 5), (19, 10)]), GOLD, 'sphere')
    lens = ellipse(20, 5, 2, 2)
    fill(cv, lens, BLUE[2])
    cv.set(19, 4, WHITE)
    return cv


def iron_skin():
    cv = Canvas(S, S)
    disc = ellipse(12, 12, 10, 10)
    part(cv, disc, STEEL, 'sphere', light=(-0.35, -0.4))
    inner = ellipse(12, 12, 6, 6)
    part(cv, inner, DARKSTEEL, 'sphere', light=(0.3, 0.3))
    for a in range(0, 360, 45):
        x, y = 12 + math.cos(math.radians(a)) * 8, 12 + math.sin(math.radians(a)) * 8
        cv.set(int(round(x)), int(round(y)), STEEL[3])
        cv.set(int(round(x)) + 1, int(round(y)) + 1, STEEL[0])
    hl(cv, [(10, 9), (9, 10)], DARKSTEEL[3])
    return cv


def knight_memory():
    cv = Canvas(S, S)
    glow(cv, 12, 12, 12, CYAN[2], 0.3)
    helm = ellipse(12, 11, 8, 9) | poly([(4, 12), (20, 12), (19, 22), (5, 22)])
    part(cv, helm, DARKSTEEL, 'sphere', light=(-0.3, -0.5))
    for x in range(6, 19):  # 눈 틈
        cv.set(x, 12, OUT)
    for x in (8, 15):
        cv.set(x, 12, CYAN[3])
        cv.set(x + 1, 12, CYAN[2])
    for y in range(4, 21):
        cv.set(12, y, DARKSTEEL[3] if y < 11 else DARKSTEEL[2])
    for y in range(15, 21):
        for x in (9, 15):
            cv.set(x, y, OUT if y % 2 else DARKSTEEL[0])
    hl(cv, [(8, 5), (7, 6)], DARKSTEEL[3])
    return cv


def phantom_banner():
    cv = Canvas(S, S)
    part(cv, thick_line(3, 1, 3, 23, 2), WOOD, 'span')
    flag = poly([(4, 2), (23, 2), (23, 15), (20, 17), (17, 14), (14, 17), (11, 14), (8, 17), (4, 14)])
    part(cv, flag, PURPLE, 'span', light=(-0.4, -0.4))
    skull = ellipse(13.5, 7.5, 4, 3.6) | poly([(11, 9), (16, 9), (15.5, 12.5), (11.5, 12.5)])
    part(cv, skull, BONE, 'sphere')
    for x in (11, 15):  # 눈구멍
        hl(cv, [(x, 7), (x + 1, 7), (x, 8)], OUT)
    cv.set(13, 10, OUT)
    for x in (12, 14):
        cv.set(x, 12, OUT)
    cv.set(3, 0, GOLD[3])
    return cv


def piercing_seal():
    cv = Canvas(S, S)
    seal = ellipse(11, 13, 8, 8)
    part(cv, seal, RED, 'sphere')
    ring = ellipse(11, 13, 5, 5) - ellipse(11, 13, 4, 4)
    for p in ring:
        cv.set(p[0], p[1], RED[0])
    hl(cv, [(10, 12), (11, 12), (12, 12), (11, 11), (11, 13), (11, 14)], RED[3])
    # 관통하는 화살 - 깃(왼쪽 아래) · 대 · 촉(오른쪽 위)
    # 관통하는 화살(10-24 사용자 - 창처럼 보이지 않게): 가는 대, 미늘 달린 넓은 촉, 꼬리에 큰 V자 깃털. 칸 안에 다 들어오게.
    arrow = set(line(5, 18, 17, 6))                       # 가는 대(1칸)
    head = poly([(15, 4.5), (21.5, 2.5), (19.5, 9), (17.5, 7.5), (16.5, 6.5)])  # 촉 + 뒤로 젖힌 미늘
    fl = set()
    for k in range(4):                                     # 깃털 두 장(대 양옆으로 V자)
        fl |= set(line(5 + k, 18 - k, 2 + k, 16 - k)) | set(line(5 + k, 18 - k, 7 + k, 21 - k))
    for p in arrow | head | fl:                            # 화살 전체 외곽선 먼저
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (p[0] + dx, p[1] + dy)
            if q not in arrow and q not in head and q not in fl and 0 <= q[0] < S and 0 <= q[1] < S:
                cv.set(q[0], q[1], OUT)
    for p in arrow:
        cv.set(p[0], p[1], WOOD[2])
    for p in fl:
        cv.set(p[0], p[1], RED[2] if (p[0] + p[1]) % 2 else (240, 236, 228))
    for p in head:
        cv.set(p[0], p[1], STEEL[2])
    for p in line(16, 6, 20, 3):
        cv.set(p[0], p[1], STEEL[3])
    return cv


def pulse_core():
    cv = Canvas(S, S)
    for k in range(80):
        t = k / 80 * math.tau
        cv.blend(int(12 + math.cos(t) * 11), int(12 + math.sin(t) * 11), BLUE[2], 0.75)
    core = ellipse(12, 12, 8, 8)
    part(cv, core, BLUE, 'sphere', light=(-0.35, -0.35))
    inner = ellipse(12, 12, 4, 4) - ellipse(12, 12, 3, 3)
    for p in inner:
        cv.set(p[0], p[1], BLUE[3])
    hl(cv, [(8, 7), (7, 8), (8, 8), (9, 7)], WHITE)
    return cv


def regen_moss():
    cv = Canvas(S, S)
    rock = ellipse(12, 17, 10, 5.5)
    part(cv, rock, STONE, 'sphere')
    moss = {p for p in ellipse(12, 14, 9, 4) if p[1] <= 16}
    part(cv, moss, GREEN, 'sphere')
    for x in (7, 12, 17):  # 새싹
        for y in range(6, 11):
            cv.set(x, y, GREEN[2])
        hl(cv, [(x - 1, 7), (x + 1, 6), (x - 2, 6), (x + 2, 5)], GREEN[3])
    return cv


def second_wind():
    cv = Canvas(S, S)
    m = set()
    for k in range(18):
        t = k / 17
        x = 5 + t * 14
        y = 20 - t * 16 + math.sin(t * math.pi) * -2
        m |= ellipse(x, y, 4.5 * math.sin(t * math.pi) + 0.8, 4.5 * math.sin(t * math.pi) + 0.8)
    part(cv, m, [(170, 186, 200), (210, 222, 232), (236, 244, 250), WHITE], 'sphere')
    for p in line(4, 21, 19, 4):  # 깃대
        cv.set(p[0], p[1], (150, 160, 176))
    for k in range(4):
        for p in line(8 + k * 3, 17 - k * 3, 6 + k * 3, 13 - k * 3):
            if p in m:
                cv.set(p[0], p[1], (190, 200, 214))
    for (x, y) in ((20, 14), (21, 13), (19, 18), (21, 17)):
        cv.set(x, y, GREEN[2])
    return cv


def sniper_eye():
    cv = Canvas(S, S)
    eye = poly([(1, 12), (6, 6), (12, 4), (18, 6), (23, 12), (18, 18), (12, 20), (6, 18)])
    part(cv, eye, [(200, 196, 190), (232, 228, 222), (248, 246, 240), WHITE], 'sphere')
    part(cv, ellipse(12, 12, 5, 5), RED, 'sphere')
    fill(cv, ellipse(12, 12, 2.2, 2.2), OUT)
    cv.set(11, 10, WHITE)
    for k in range(0, 24):
        if not 7 <= k <= 16:
            cv.set(k, 12, RED[2])
            cv.set(12, k, RED[2])
    return cv


def spinning_blade():
    cv = Canvas(S, S)
    for k in range(4):
        a = math.radians(k * 90 + 20)
        tip = (12 + math.cos(a) * 12, 12 + math.sin(a) * 12)
        side = (12 + math.cos(a + 1.2) * 8, 12 + math.sin(a + 1.2) * 8)
        back = (12 + math.cos(a - 0.6) * 4, 12 + math.sin(a - 0.6) * 4)
        part(cv, poly([back, side, tip]), STEEL, 'sphere')
    part(cv, ellipse(12, 12, 4.5, 4.5), DARKSTEEL, 'sphere')
    fill(cv, ellipse(12, 12, 1.6, 1.6), OUT)
    return cv


def treasure_hunter():
    """오래된 보물지도(TreasureHunter, 10-24 사용자) - 누렇게 바랜 지도: 찢기고 그을린 가장자리, 섬, 점선 길, 빨간 X, 나침반."""
    import random
    rng = random.Random(7)
    cv = Canvas(S, S)
    paper = set()
    for y in range(2, 22):
        for x in range(1, 23):
            ragged = (x in (1, 22) or y in (2, 21)) and rng.random() < 0.35
            if not ragged:
                paper.add((x, y))
    for (x, y) in ((1, 2), (2, 2), (1, 3), (22, 21), (21, 21), (22, 20), (22, 2)):  # 찢긴 모서리
        paper.discard((x, y))
    old = [(138, 104, 58), (186, 150, 92), (214, 184, 124), (236, 214, 160)]
    part(cv, paper, old, 'sphere', light=(-0.2, -0.3), outline=(70, 46, 24))
    inner = paper - boundary(paper)
    for p in inner:  # 그을린 가장자리 + 얼룩
        x, y = p
        edge = min(x - 1, 22 - x, y - 2, 21 - y)
        if edge <= 1 and rng.random() < 0.55:
            cv.set(x, y, old[0])
        elif rng.random() < 0.04:
            cv.set(x, y, old[1])
    island = {p for p in ellipse(9, 11, 5.5, 4) | ellipse(13, 15, 4, 3) if p in inner}
    for p in island:
        cv.set(p[0], p[1], (120, 140, 80) if p not in boundary(island) else (96, 90, 60))
    for k, (x, y) in enumerate(((4, 18), (6, 17), (8, 16), (10, 15), (12, 14), (14, 12))):  # 점선 길
        cv.set(x, y, (120, 50, 40) if k % 2 == 0 else (150, 70, 50))
    for k in range(-2, 3):  # 빨간 X
        cv.set(16 + k, 9 + k, RED[1])
        cv.set(16 + k, 9 - k, RED[1])
    cv.set(16, 9, RED[2])
    for (x, y) in ((20, 4), (20, 6), (19, 5), (21, 5)):  # 나침반 별
        cv.set(x, y, (90, 60, 34))
    cv.set(20, 5, RED[2])
    return cv


def tyrant_plate():
    cv = Canvas(S, S)
    for side in (-1, 1):  # 어깨 가시 - 굵게
        part(cv, poly([(12 + side * 4, 8), (12 + side * 10, 8), (12 + side * 11.5, 0)]), STEEL, 'sphere')
    plate = poly([(4, 6), (20, 6), (21, 12), (17, 22), (7, 22), (3, 12)])
    part(cv, plate, DARKSTEEL, 'sphere', light=(-0.3, -0.4))
    for y in range(8, 20):
        cv.set(12, y, DARKSTEEL[3])
    for x in range(10, 15):
        cv.set(x, 13, RED[2])
    for y in range(11, 16):
        cv.set(12, y, RED[2])
    for (x, y) in ((6, 9), (18, 9), (8, 18), (16, 18)):
        cv.set(x, y, STEEL[3])
    return cv


RELICS = {
    'Berserker': berserker, 'BloodPact': blood_pact, 'ChainBlast': chain_blast, 'ChargeHorn': charge_horn,
    'CrossCrest': cross_crest, 'CrownOfEnd': crown_of_end, 'EarthHammer': earth_hammer, 'Executioner': executioner,
    'FirstStrike': first_strike, 'Gale': gale, 'Greed': greed, 'HunterEye': hunter_eye,
    'KnightMemory': knight_memory, 'PhantomBanner': phantom_banner, 'PiercingSeal': piercing_seal, 'PulseCore': pulse_core,
    'RegenMoss': regen_moss, 'SecondWind': second_wind, 'SniperEye': sniper_eye, 'SpinningBlade': spinning_blade,
    'TreasureHunter': treasure_hunter,  # 이름은 '오래된 보물지도'(10-24)
}
# 철의 피부·폭군의 갑주는 예전 16px 그림을 그대로 쓴다(10-24 사용자 "이전 게 좋다") - iron_skin()·tyrant_plate()는 참고용으로만 남김

if __name__ == '__main__':
    out = sys.argv[1]
    for name, fn in RELICS.items():
        fn().save(os.path.join(out, f'Relic_{name}.png'))
