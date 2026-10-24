"""설치 아이템·아이템 아이콘 리드로우(10-24).
덫:      Effects/obj_trap/0~3(32x32, 4fps 반복, 0.8칸) + Sprites/Props/Trap(예비) + UI/Icons/Item_Trap(24x24)
         - 활짝 벌린 곰덫: 톱니 쇠 턱 두 개, 가운데 발판, 땅에 박은 말뚝 사슬, 톱니를 따라 반짝임이 흐름
허수아비: Effects/obj_decoy/0~3(48x48, 4fps 반복, 1.1칸) + Sprites/Props/Decoy(32x32 예비) + UI/Icons/Item_Decoy(24x24)
         - 십자 나무 기둥, 삼베 자루 머리(바늘땀 눈·입), 뾰족한 밀짚모자, 기운 빨간 옷, 삐져나온 지푸라기, 바람에 살짝 흔들림
아이콘(24x24, 아이템 칸 48px = 2배): Item_{Barrier,Bomb,Cleanse,Flash,Reflect,Smoke,Torch,Weakness} + Bag
python draw_items.py <출력 루트(Resources 구조로 저장)>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, glow, line, part, poly, thick_line

STEEL = [(70, 74, 92), (126, 132, 152), (176, 182, 202), (226, 232, 246)]
DARK = [(30, 30, 38), (50, 50, 62), (74, 74, 90), (104, 104, 122)]
RUST = [(90, 52, 34), (130, 78, 46), (168, 108, 64), (200, 140, 90)]
WOOD = [(70, 44, 26), (108, 72, 42), (144, 102, 62), (176, 132, 86)]


def bear_trap(size, glint=None):
    """size = 32(바닥 그림) / 24(아이콘). 위에서 본 활짝 벌린 곰덫 - 위·아래 반원 턱(안쪽 흰 톱니), 양옆 용수철, 가운데 놋쇠 발판, 말뚝 사슬."""
    k = size / 32
    cv = Canvas(size, size)
    cx, cy = (size - 1) / 2, size * 0.5
    rx, ry = 12.5 * k, 9.5 * k
    if size == 32:
        for p in ellipse(cx, cy + 2, 14.5, 10.5):
            cv.set(p[0], p[1], (10, 6, 16, 90))
    # 말뚝 사슬(오른쪽 아래 모서리로)
    if size == 32:
        for i in range(3):
            part(cv, ellipse(cx + 11 + i * 2, cy + 8 + i * 2, 1.8, 1.3), STEEL, 'sphere')
    # 턱: 바깥 타원 - 안쪽 타원 = 고리, 양옆(경첩)은 끊어 위·아래 두 쪽
    ring = ellipse(cx, cy, rx, ry) - ellipse(cx, cy, rx - 3 * k - 0.5, ry - 3 * k - 0.5)
    for half in (-1, 1):
        jaw = {p for p in ring if (p[1] - cy) * half > 0.6}
        part(cv, jaw, STEEL, 'sphere', light=(-0.3, -0.6 if half < 0 else 0.3))
    # 톱니 - 턱 안쪽 가장자리를 따라 안쪽으로 뾰족하게
    n = 7 if size == 32 else 5
    for half in (-1, 1):
        for i in range(n):
            t = -0.75 + 1.5 * i / (n - 1)
            bx = cx + t * (rx - 3.5 * k)
            by = cy + half * (ry - 3 * k) * math.sqrt(max(0, 1 - t * t))
            tip = (bx, by - half * 2.6 * k)
            tooth = poly([(bx - 1.3 * k - 0.3, by + half * 0.5), (bx + 1.3 * k + 0.3, by + half * 0.5), tip])
            for p in tooth:
                cv.set(p[0], p[1], (238, 240, 248) if p[1] != int(by) else (200, 204, 220))
    # 양옆 용수철(경첩)
    for side in (-1, 1):
        hx = cx + side * (rx - 1.5 * k)
        for j in range(-2, 3):
            cv.set(int(hx), int(cy + j * k), DARK[2] if j % 2 else STEEL[2])
            cv.set(int(hx) + side, int(cy + j * k), OUT)
    # 가운데 놋쇠 발판
    plate = ellipse(cx, cy, 3.8 * k + 0.6, 2.6 * k + 0.6)
    part(cv, plate, [(120, 80, 30), (176, 128, 54), (216, 170, 84), (246, 214, 130)], 'sphere')
    if glint is not None:  # 위 턱을 따라 흐르는 반짝임
        t = -0.8 + glint * 0.5
        x, y = cx + t * rx, cy - ry * math.sqrt(max(0, 1 - t * t)) + 1
        for (dx, dy, c) in ((0, 0, (255, 255, 255)), (1, 0, (220, 230, 255)), (-1, 0, (220, 230, 255)), (0, -1, (220, 230, 255)), (0, 1, (220, 230, 255))):
            cv.set(int(x + dx), int(y + dy), c)
    return cv


STRAW = [(140, 104, 40), (196, 156, 64), (232, 200, 100), (252, 232, 160)]
SACK = [(130, 100, 70), (176, 142, 102), (210, 180, 136), (232, 208, 168)]
SHIRT = [(100, 26, 30), (150, 40, 42), (190, 64, 58), (220, 106, 92)]
PATCH = [(60, 70, 100), (84, 98, 136), (110, 126, 168), (140, 156, 196)]


def scarecrow(size, f=0):
    """size 48(설치 그림) / 32(예비) / 24(아이콘). f = 흔들림 프레임."""
    k = size / 48
    cv = Canvas(size, size)
    sway = [0, 1, 0, -1][f % 4] * k
    cx = size / 2
    if size >= 32:
        for p in ellipse(cx, 45 * k, 9 * k, 2 * k):
            cv.set(p[0], p[1], (10, 6, 16, 100))
        part(cv, ellipse(cx, 44.5 * k, 5 * k, 1.8 * k), [(70, 50, 34), (100, 74, 50), (130, 98, 66), (150, 116, 80)], 'sphere')  # 흙 둔덕
    part(cv, thick_line(cx, 45 * k, cx + sway * 0.3, 14 * k, max(2, int(3 * k))), WOOD, 'span')  # 세로 기둥
    arm_y = 21 * k
    part(cv, thick_line(cx - 17 * k + sway, arm_y, cx + 17 * k + sway, arm_y + sway * 0.5, max(2, int(3 * k))), WOOD, 'span')  # 가로대
    for side in (-1, 1):  # 소매 끝 지푸라기
        x0 = cx + side * 17 * k + sway
        for j in range(3):
            for t in range(int(3 * k) + 1):
                cv.set(int(x0 + side * t), int(arm_y - 1 + j * k + (t if j == 2 else 0) * 0.5), STRAW[2] if j % 2 else STRAW[1])
    shirt = poly([(cx - 11 * k + sway, arm_y - 2 * k), (cx + 11 * k + sway, arm_y - 2 * k), (cx + 13 * k + sway, arm_y + 3 * k),
                  (cx + 8 * k + sway * 0.7, arm_y + 4 * k), (cx + 9 * k + sway * 0.6, 37 * k), (cx - 9 * k + sway * 0.6, 37 * k),
                  (cx - 8 * k + sway * 0.7, arm_y + 4 * k), (cx - 13 * k + sway, arm_y + 3 * k)])
    part(cv, shirt, SHIRT, 'span', light=(-0.3, -0.4))
    if size >= 32:
        patch = poly([(cx + 2 * k, 26 * k), (cx + 6 * k, 26 * k), (cx + 6 * k, 30 * k), (cx + 2 * k, 30 * k)])
        part(cv, patch, PATCH, 'flat')
        for y in range(int(26 * k), int(30 * k) + 1, 2):  # 바늘땀
            cv.set(int(cx + 1 * k), y, STRAW[3])
        for x in range(int(cx - 9 * k + sway * 0.6), int(cx + 9 * k + sway * 0.6) + 1, 2):  # 옷자락 아래 지푸라기
            for t in range(int(3 * k)):
                cv.set(x, int(37 * k) + 1 + t, STRAW[2] if t < 2 else STRAW[1])
        for x in range(int(cx - 3 * k), int(cx + 3 * k) + 1):  # 허리 끈
            cv.set(x + int(sway * 0.6), int(31 * k), STRAW[0])
    hx = cx + sway * 1.2
    head = ellipse(hx, 15 * k, 6 * k + 0.5, 5.5 * k + 0.5)
    part(cv, head, SACK, 'sphere', light=(-0.3, -0.4))
    if size >= 32:
        for x in range(int(hx - 4 * k), int(hx + 4 * k) + 1):  # 목 끈 + 지푸라기
            cv.set(x, int(20 * k), STRAW[1])
        for dx in (-2, 0, 2):
            cv.set(int(hx + dx * k), int(21 * k), STRAW[2])
    ex = (int(hx - 3 * k), int(hx + 2 * k))  # 바늘땀 X 눈 + 꿰맨 입
    for x0 in ex:
        if size >= 32:
            for d in (-1, 0, 1):
                cv.set(x0 + d, int(14 * k) + d, OUT)
                cv.set(x0 + d, int(14 * k) - d, OUT)
        else:
            cv.set(x0, int(14 * k), OUT)
            cv.set(x0 + 1, int(14 * k), OUT)
    for x in range(int(hx - 3 * k), int(hx + 3 * k) + 1):
        cv.set(x, int(18 * k), OUT if x % 2 else SACK[0])
    hat = poly([(hx - 9 * k, 11 * k), (hx + 9 * k, 11 * k + sway * 0.3), (hx + 6 * k, 9 * k), (hx + 1 * k, 1 * k + abs(sway)), (hx - 5 * k, 9 * k)])
    part(cv, hat, STRAW, 'span', light=(-0.3, -0.5))
    for x in range(int(hx - 6 * k), int(hx + 6 * k) + 1):  # 모자 띠
        y = int(9.5 * k)
        if (x, y) in hat:
            cv.set(x, y, SHIRT[1])
    if size == 48:  # 날리는 지푸라기 한 가닥(프레임마다 자리 바꿈)
        fx, fy = [(39, 30), (41, 27), (43, 29), (41, 32)][f % 4]
        cv.set(fx, fy, STRAW[3])
        cv.set(fx + 1, fy - 1, STRAW[2])
    return cv


def scarecrow_icon():
    """허수아비 아이콘 24x24 - 작은 칸에서도 읽히게 따로 그림: 큰 머리·모자, 가로대, 빨간 옷."""
    cv = Canvas(24, 24)
    part(cv, thick_line(11.5, 23, 11.5, 14, 3), WOOD, 'span')
    part(cv, thick_line(1, 13, 22, 13, 3), WOOD, 'span')
    for x in (0, 1, 22, 23):  # 소매 끝 지푸라기
        for y in (12, 14):
            cv.set(x, y, STRAW[2])
    shirt = poly([(5, 11), (18, 11), (19, 15), (16, 15), (16, 21), (7, 21), (7, 15), (4, 15)])
    part(cv, shirt, SHIRT, 'span', light=(-0.3, -0.4))
    part(cv, poly([(12, 16), (15, 16), (15, 19), (12, 19)]), PATCH, 'flat')
    for x in range(7, 17, 2):
        cv.set(x, 22, STRAW[2])
    head = ellipse(11.5, 8.5, 5.8, 4.3)
    part(cv, head, SACK, 'sphere', light=(-0.3, -0.4), outline=(96, 70, 44))  # 자루 머리 - 테두리는 진한 삼베색(검은 테가 머리카락처럼 보이지 않게)
    for x in (9, 14):  # 눈(바늘땀 점)
        cv.set(x, 8, OUT)
        cv.set(x, 9, (96, 70, 44))
    for x in range(9, 15):  # 꿰맨 입
        cv.set(x, 11, OUT if x % 2 else SACK[0])
    hat = poly([(4.5, 6), (18.5, 6), (18.5, 4.5), (15, 4), (12, 0), (9, 4), (4.5, 4.5)])
    part(cv, hat, STRAW, 'span', light=(-0.3, -0.5), outline=(120, 84, 30))
    for x in range(9, 15):
        cv.set(x, 4, SHIRT[1])
    return cv


BLUE = [(30, 60, 130), (60, 116, 200), (120, 180, 246), (210, 240, 255)]
GLASS = [(120, 140, 170), (170, 190, 214), (214, 228, 242), (250, 252, 255)]
GREEN = [(30, 100, 50), (60, 160, 80), (110, 214, 120), (190, 250, 190)]
RED = [(110, 22, 30), (176, 38, 46), (226, 72, 70), (255, 150, 130)]
GOLD = [(140, 90, 20), (206, 150, 40), (246, 204, 80), (255, 240, 160)]
SMOKE = [(70, 72, 84), (110, 112, 126), (150, 152, 166), (196, 198, 210)]
PAPER = [(170, 150, 112), (214, 196, 156), (238, 226, 194), (255, 250, 230)]
LEATHER = [(80, 48, 26), (128, 80, 40), (176, 118, 60), (214, 162, 98)]
FIRE = [(200, 70, 30), (250, 150, 50), (255, 220, 110), (255, 250, 210)]
BOMB = [(20, 20, 28), (40, 40, 52), (66, 66, 82), (120, 120, 140)]
CAP = [(60, 60, 70), (90, 90, 100), (120, 120, 132), (160, 160, 170)]
WRAP = [(60, 40, 30), (90, 64, 44), (120, 90, 64), (150, 116, 86)]
FLASH_ORB = [(200, 190, 140), (240, 232, 190), (255, 250, 220), (255, 255, 255)]
SMOKE_BALL = [(46, 50, 60), (76, 82, 96), (110, 118, 134), (160, 168, 184)]
CREAM = (240, 236, 228)


def icon(name):
    """아이템 아이콘 24x24 - 예전 16px 그림의 상징 그대로(보호막 = 파란 방패, 반사 부적 = 붉은 글씨 부적 …)."""
    cv = Canvas(24, 24)
    if name == 'Barrier':  # 파란 수정 방패
        sh = poly([(3, 3), (21, 3), (21, 12), (12, 22), (3, 12)])
        part(cv, sh, BLUE, 'sphere', light=(-0.35, -0.45))
        for p in line(6, 6, 11, 6) + line(6, 6, 6, 12):
            cv.set(p[0], p[1], BLUE[3])
        for p in line(15, 9, 12, 17):
            if p in sh:
                cv.set(p[0], p[1], BLUE[1])
    elif name == 'Bomb':  # 검은 폭탄 + 불꽃 심지
        part(cv, ellipse(11, 14, 8.5, 8.5), BOMB, 'sphere', light=(-0.4, -0.4))
        part(cv, poly([(14, 4), (19, 4), (19, 7), (14, 7)]), CAP, 'span')
        for p in line(17, 3, 20, 1):
            cv.set(p[0], p[1], (170, 140, 90))
        for (x, y, c) in ((21, 0, FIRE[3]), (22, 1, FIRE[1]), (20, 0, FIRE[1]), (21, 1, FIRE[2]), (23, 0, FIRE[0])):
            cv.set(x, y, c)
        for (x, y) in ((7, 10), (8, 9), (7, 11)):
            cv.set(x, y, (170, 170, 190))
    elif name == 'Cleanse':  # 초록 물약 병 + 거품
        flask = ellipse(12, 15, 7.5, 7) | poly([(9, 3), (15, 3), (15, 9), (9, 9)])
        part(cv, flask, GLASS, 'sphere')
        liquid = {p for p in ellipse(12, 15, 6.2, 5.8) if p[1] >= 12}
        part(cv, liquid, GREEN, 'span', outline=None)
        part(cv, poly([(9, 1), (15, 1), (15, 4), (9, 4)]), LEATHER, 'span')
        for (x, y) in ((10, 15), (14, 17), (12, 13)):
            cv.set(x, y, GREEN[3])
        for (x, y) in ((19, 6), (21, 3), (4, 5)):
            cv.set(x, y, GREEN[2])
            cv.set(x + 1, y, GREEN[3])
        for (x, y) in ((8, 11), (8, 12)):
            cv.set(x, y, (255, 255, 255))
    elif name == 'Flash':  # 빛이 터지는 유리 구슬
        glow(cv, 12, 12, 11, (255, 250, 200), 0.35)
        for k in range(8):
            a = k / 8 * math.tau
            r = 11 if k % 2 == 0 else 7
            for t in range(5, r + 1):
                cv.set(int(12 + math.cos(a) * t), int(12 + math.sin(a) * t), (255, 236, 140) if t < r else (255, 200, 80))
        part(cv, ellipse(12, 12, 5, 5), FLASH_ORB, 'sphere')
    elif name == 'Reflect':  # 붉은 글씨 부적
        part(cv, poly([(6, 1), (18, 1), (18, 23), (6, 23)]), PAPER, 'vspan', bands=(0.1, 0.5, 0.9))
        for y in range(3, 22):
            cv.set(7, y, RED[1])
            cv.set(17, y, RED[1])
        glyph = [(12, 4), (12, 5), (10, 6), (11, 6), (12, 6), (13, 6), (14, 6), (12, 7), (11, 8), (13, 8), (10, 9), (14, 9),
                 (12, 11), (12, 12), (10, 13), (11, 13), (12, 13), (13, 13), (14, 13), (11, 15), (12, 15), (13, 15), (12, 16),
                 (10, 18), (11, 19), (12, 19), (13, 19), (14, 18), (12, 20)]
        for p in glyph:
            cv.set(p[0], p[1], RED[2])
    elif name == 'Smoke':  # 연기 뿜는 회색 구슬
        for (cx, cy, r) in ((15, 7, 4.5), (19, 11, 3.5), (9, 5, 3.5), (13, 3, 3)):
            part(cv, ellipse(cx, cy, r, r * 0.9), SMOKE, 'sphere')
        ball = ellipse(10, 15, 7, 7)
        part(cv, ball, SMOKE_BALL, 'sphere')
        for x in range(4, 17):
            if (x, 15) in ball and (x, 15) not in boundary(ball):
                cv.set(x, 15, (200, 180, 90))
    elif name == 'Torch':  # 불 붙은 횃불
        part(cv, thick_line(8, 23, 13, 11, 4), WOOD, 'span')
        part(cv, thick_line(11, 14, 16, 11, 4), WRAP, 'span')
        flame = poly([(18, 12), (9, 12), (7, 6), (11, 0), (13, 4), (16, 0), (20, 6)])
        part(cv, flame, FIRE, 'sphere', light=(-0.2, 0.4), outline=(140, 40, 20))
        for (x, y) in ((14, 7), (14, 8), (15, 8)):
            cv.set(x, y, FIRE[3])
    elif name == 'Weakness':  # 붉은 과녁 + 조준선
        for r, c in ((11, RED[1]), (8.5, CREAM), (6, RED[2]), (3.5, CREAM), (1.6, RED[1])):
            for p in ellipse(12, 12, r, r):
                cv.set(p[0], p[1], c)
        for p in boundary(ellipse(12, 12, 11, 11)):
            cv.set(p[0], p[1], OUT)
        for k in range(-11, 12):
            if abs(k) > 7:
                cv.set(12 + k, 12, OUT)
                cv.set(12, 12 + k, OUT)
    elif name == 'Bag':  # 가죽 배낭
        part(cv, poly([(4, 8), (20, 8), (21, 22), (3, 22)]) | ellipse(12, 8, 8, 3), LEATHER, 'sphere', light=(-0.3, -0.4))
        part(cv, poly([(4, 8), (20, 8), (18, 14), (6, 14)]), LEATHER, 'span')
        part(cv, poly([(10, 12), (14, 12), (14, 15), (10, 15)]), GOLD, 'sphere')
        for p in line(8, 2, 8, 7) + line(16, 2, 16, 7) + line(8, 2, 16, 2):
            cv.set(p[0], p[1], LEATHER[0])
    return cv


def skill_icon(name):
    """스킬 아이콘 24x24(스킬 칸 48px = 2배) - 대시 = 잔상을 끄는 파란 화살표, 회전 베기 = 칼을 휘감는 금빛 소용돌이."""
    cv = Canvas(24, 24)
    if name == 'Dash':
        for k, a in ((2, 0.35), (1, 0.6)):  # 잔상 화살표 두 개(옅게)
            ox = -k * 5
            for p in poly([(6 + ox, 9), (13 + ox, 9), (13 + ox, 5), (21 + ox, 12), (13 + ox, 19), (13 + ox, 15), (6 + ox, 15)]):
                cv.blend(p[0], p[1], BLUE[2], a)
        part(cv, poly([(7, 9), (14, 9), (14, 4), (23, 12), (14, 20), (14, 15), (7, 15)]), BLUE, 'span', light=(-0.2, -0.5))
        for y in (6, 18):  # 속도선
            for x in range(1, 9):
                cv.blend(x, y, BLUE[3], 0.3 + x * 0.08)
        for (x, y) in ((16, 9), (17, 10), (18, 11)):
            cv.set(x, y, (240, 250, 255))
    elif name == 'Spin':
        c = 11.5
        ring = {p for p in ellipse(c, c, 11, 11) - ellipse(c, c, 7.2, 7.2)
                if not (math.atan2(p[1] + 0.5 - c, p[0] + 0.5 - c) > -0.2 and math.atan2(p[1] + 0.5 - c, p[0] + 0.5 - c) < 1.2)}  # 오른쪽 아래가 빈 3/4 고리
        part(cv, ring, [(170, 80, 20), (232, 140, 40), (255, 200, 80), (255, 240, 170)], 'sphere', light=(-0.35, -0.45))
        part(cv, poly([(15, 18), (23, 15), (20, 23)]), [(170, 80, 20), (232, 140, 40), (255, 200, 80), (255, 240, 170)], 'sphere')  # 고리 끝 화살촉
        part(cv, thick_line(7, 16, 16, 7, 3), [(90, 96, 118), (150, 158, 180), (204, 210, 228), (246, 248, 255)], 'span')  # 가운데 칼
        part(cv, thick_line(6, 13, 10, 17, 2), GOLD, 'span')
        cv.set(15, 8, (255, 255, 255))
    return cv


if __name__ == '__main__':
    root = sys.argv[1]
    os.makedirs(os.path.join(root, 'Effects', 'obj_trap'), exist_ok=True)
    os.makedirs(os.path.join(root, 'Sprites', 'Props'), exist_ok=True)
    os.makedirs(os.path.join(root, 'UI', 'Icons'), exist_ok=True)
    for f in range(4):
        bear_trap(32, glint=f).save(os.path.join(root, 'Effects', 'obj_trap', f'{f}.png'))
    bear_trap(32).save(os.path.join(root, 'Sprites', 'Props', 'Trap.png'))
    bear_trap(24).save(os.path.join(root, 'UI', 'Icons', 'Item_Trap.png'))
    os.makedirs(os.path.join(root, 'Effects', 'obj_decoy'), exist_ok=True)
    for f in range(4):
        scarecrow(48, f).save(os.path.join(root, 'Effects', 'obj_decoy', f'{f}.png'))
    scarecrow_icon().save(os.path.join(root, 'UI', 'Icons', 'Item_Decoy.png'))  # 예비 그림 Props/Decoy는 예전 그대로(설치 그림이 있으면 안 쓰임)
    # 보호막·폭탄·연막탄·약점 표식은 예전 16px 그림 그대로(10-24 사용자 선택) - icon()에 그림은 남겨 둠
    for n in ('Cleanse', 'Flash', 'Reflect', 'Torch'):
        icon(n).save(os.path.join(root, 'UI', 'Icons', f'Item_{n}.png'))
    icon('Bag').save(os.path.join(root, 'UI', 'Icons', 'Bag.png'))
    for n in ('Dash', 'Spin'):
        skill_icon(n).save(os.path.join(root, 'UI', 'Icons', f'Skill_{n}.png'))
