"""방 이벤트 그림 리드로우(10-24) - Effects/event_{AdventurerRemains,ChallengeFlag}/0~3.png, 32x32 4프레임(6fps 반복).
모험가의 유해: 배낭에 기대 앉은 해골 + 비뚤어진 녹슨 투구 + 해진 망토 + 땅에 꽂힌 부러진 검, 배낭에서 반짝임이 돈다
도전의 깃발: 빨간 전투 깃발(금색 교차 검 문장)이 물결치며 펄럭임 + 창날 깃대 + 돌 받침
python draw_events.py <Resources/Effects 폴더>
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, part, poly, thick_line, line

BONE = [(136, 124, 100), (190, 180, 152), (222, 214, 188), (246, 240, 220)]
RUST = [(70, 50, 40), (110, 80, 60), (150, 110, 80), (180, 140, 100)]
CLOTH = [(50, 40, 56), (76, 62, 82), (100, 84, 104), (124, 108, 128)]
LEATHER = [(76, 48, 28), (118, 78, 44), (156, 108, 62), (190, 140, 88)]
STEEL = [(70, 74, 94), (118, 124, 148), (168, 176, 198), (226, 232, 244)]
WOOD = [(70, 44, 26), (108, 72, 42), (144, 102, 62), (176, 132, 86)]
RED = [(110, 22, 30), (170, 36, 44), (214, 64, 64), (244, 120, 110)]
GOLD = [(140, 90, 20), (206, 150, 40), (246, 204, 80), (255, 240, 160)]
STONE = [(60, 58, 70), (90, 88, 102), (120, 118, 134), (152, 150, 166)]


def remains(f):
    cv = Canvas(32, 32)
    for p in ellipse(15, 29, 14, 2.2):  # 바닥 그림자
        cv.set(p[0], p[1], (10, 6, 16, 100))
    cape = poly([(5, 12), (16, 12), (17, 25), (13, 27), (8, 26), (4, 27)])  # 등 뒤 해진 망토
    part(cv, cape, CLOTH, 'span')
    bag = poly([(18, 13), (28, 13), (29, 28), (17, 28)]) | ellipse(23, 13, 5, 2.5)
    part(cv, bag, LEATHER, 'span', light=(0.2, -0.4))
    part(cv, poly([(18, 18), (28, 18), (28, 20), (18, 20)]), LEATHER, 'flat')
    part(cv, poly([(22, 19), (24, 19), (24, 22), (22, 22)]), GOLD, 'sphere')
    torso = poly([(7, 14), (15, 14), (15, 23), (7, 23)])  # 몸통 속 어둠(뼈가 또렷이 보이게)
    for p in torso:
        cv.set(p[0], p[1], (26, 22, 30))
    bones = set()
    for y in range(15, 23, 2):  # 갈비뼈(척추에서 휘어 나감)
        for x in range(8, 15):
            bones.add((x, y + (1 if x in (8, 14) else 0)))
    bones |= {(11, y) for y in range(13, 24)}               # 척추
    bones |= set(thick_line(7, 23, 15, 23, 2))              # 골반
    bones |= set(thick_line(13, 24, 3, 25, 2)) | set(thick_line(10, 26, 1, 27, 2))  # 뻗은 다리뼈
    bones |= set(thick_line(15, 16, 19, 17, 2))             # 배낭에 걸친 팔
    for p in bones:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (p[0] + dx, p[1] + dy)
            if q not in bones and q not in torso and 0 <= q[0] < 32 and 0 <= q[1] < 32:
                cv.set(q[0], q[1], OUT)
    for p in bones:
        cv.set(p[0], p[1], BONE[2] if (p[0] + p[1]) % 4 else BONE[1])
    for (x, y) in ((1, 26), (2, 26), (0, 27)):  # 낡은 장화 끝
        cv.set(x, y, LEATHER[1])
    skull = ellipse(11, 9, 4.6, 4.4) | poly([(8, 10), (14, 10), (13.5, 13.5), (8.5, 13.5)])
    part(cv, skull, BONE, 'sphere', light=(-0.3, -0.5))
    for (x, y) in ((9, 9), (10, 9), (9, 10), (13, 9), (13, 10), (12, 9)):
        cv.set(x, y, OUT)
    cv.set(11, 11, OUT)
    for x in (9, 11, 13):
        cv.set(x, 13, OUT)
    helm = {p for p in ellipse(12.5, 6, 5.5, 3.8) if p[1] <= 7} | set(thick_line(6, 7.5, 18, 6.5, 2))  # 녹슨 쇠 투구(챙)
    part(cv, helm, [(70, 62, 60), (110, 98, 90), (150, 128, 108), (186, 160, 130)], 'sphere')
    for (x, y) in ((10, 4), (14, 5)):
        cv.set(x, y, (140, 80, 40))
    part(cv, thick_line(29.5, 5, 30, 22, 3), STEEL, 'span')  # 땅에 꽂힌 부러진 검
    part(cv, thick_line(26, 21, 31, 21, 2), GOLD, 'span')
    sx, sy = [(19, 11), (22, 10), (26, 11), (24, 12)][f % 4]
    for (dx, dy, c) in ((0, 0, (255, 255, 230)), (1, 0, (255, 230, 140)), (-1, 0, (255, 230, 140)), (0, 1, (255, 230, 140)), (0, -1, (255, 230, 140))):
        cv.set(sx + dx, sy + dy, c)
    return cv


def challenge_flag(f):
    cv = Canvas(32, 32)
    for p in ellipse(9, 30, 7, 1.6):
        cv.set(p[0], p[1], (10, 6, 16, 100))
    part(cv, poly([(3, 26), (15, 26), (16, 30), (2, 30)]), STONE, 'span')  # 돌 받침
    for x in range(4, 15, 3):
        cv.set(x, 28, STONE[0])
    part(cv, thick_line(8.5, 3, 8.5, 27, 4), WOOD, 'span')
    part(cv, poly([(7, 4), (10, 4), (8.5, -0.5)]), STEEL, 'sphere')  # 창날
    # 깃발 천 - 물결(프레임마다 위상)
    ph = f / 4 * math.tau
    cloth = set()
    for x in range(10, 31):
        t = (x - 10) / 20
        wave = math.sin(t * math.pi * 1.6 - ph) * 1.6 * t
        top, bot = 5 + wave, 18 + wave - t * 1.5
        for y in range(int(top), int(bot) + 1):
            if x == 30 and y % 3 == 0:  # 꼬리 끝 해짐
                continue
            cloth.add((x, y))
    part(cv, cloth, [(90, 14, 24), (150, 28, 36), (196, 48, 52), (226, 92, 84)], 'span', light=(-0.3, -0.5), bands=(0.18, 0.55, 0.85),
         extra=lambda x, y: 0.22 * math.sin(((x - 10) / 20) * math.pi * 1.6 - ph + 1.2))
    # 금색 교차 검 문장(천 물결을 따라 움직임)
    cx = 19
    cyw = 11.5 + math.sin(((cx - 10) / 20) * math.pi * 1.6 - ph) * 1.6 * ((cx - 10) / 20)
    for k in range(-4, 5):  # 교차 검 - 2칸 굵기
        for (x, y) in ((cx + k, cyw + k), (cx + k, cyw - k)):
            for d in (0, 1):
                if (int(x + d), int(y)) in cloth:
                    cv.set(int(x + d), int(y), GOLD[2] if d == 0 else GOLD[1])
    for (x, y) in ((cx - 3, cyw + 2), (cx + 3, cyw + 2)):  # 손잡이 가로대
        for d in (-1, 0, 1):
            if (int(x + d), int(y)) in cloth:
                cv.set(int(x + d), int(y), GOLD[1])
    for p in boundary(cloth):
        if p[1] <= 6 + 0 and cv.get(*p):
            pass
    return cv


if __name__ == '__main__':
    out = sys.argv[1]
    for name, fn in (('AdventurerRemains', remains), ('ChallengeFlag', challenge_flag)):
        d = os.path.join(out, f'event_{name}')
        os.makedirs(d, exist_ok=True)
        for f in range(4):
            fn(f).save(os.path.join(d, f'{f}.png'))
