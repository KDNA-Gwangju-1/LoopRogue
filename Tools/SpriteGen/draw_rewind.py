"""사망 연출 "시간 되감기"(10-25) - 화면 가운데 거꾸로 도는 시계.
UI/Common/RewindClock(32x32) : 금장 테 + 어두운 판 + 12 눈금(바늘은 실행 중 Image 두 개로 돌린다)
UI/Common/RewindRing(32x32)  : 흰 고리(색을 곱함) - 바깥에서 시계 쪽으로 빨려 들어간다
python draw_rewind.py <Resources 폴더> [미리보기.png]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix import OUT, Canvas, boundary, ellipse, sheet

GOLD = [(96, 62, 18), (160, 112, 36), (214, 168, 70), (252, 226, 150)]


def clock():
    cv = Canvas(32, 32)
    c = 16
    outer = ellipse(c, c, 15.5, 15.5)
    inner = ellipse(c, c, 12.2, 12.2)
    ob = boundary(outer)
    for x, y in outer - inner:
        if (x, y) in ob:
            cv.set(x, y, OUT)
            continue
        t = (y + 0.5 - c) / 16 - (x + 0.5 - c) / 32
        cv.set(x, y, GOLD[3] if t < -0.45 else GOLD[2] if t < 0.05 else GOLD[1] if t < 0.5 else GOLD[0])
    for x, y in inner:
        d = math.hypot(x + 0.5 - c, y + 0.5 - c) / 12.2
        v = int(30 + 26 * (1 - d))
        cv.set(x, y, (v + 6, v, v - 6, 235))
    for x, y in boundary(inner):
        cv.set(x, y, (24, 16, 10))
    for i in range(12):  # 눈금 - 3·6·9·12시는 길게
        a = math.radians(i * 30)
        long = i % 3 == 0
        for r in ([10, 9, 8] if long else [10, 9]):
            x, y = c + r * math.sin(a), c - r * math.cos(a)
            cv.set(int(math.floor(x)), int(math.floor(y)), GOLD[3] if long else GOLD[2])
    cv.set(15, 15, GOLD[3])
    cv.set(16, 15, GOLD[2])
    cv.set(15, 16, GOLD[2])
    cv.set(16, 16, GOLD[1])
    return cv


def ring():
    cv = Canvas(32, 32)
    o = ellipse(16, 16, 15.5, 15.5)
    i = ellipse(16, 16, 13.5, 13.5)
    for x, y in o - i:
        cv.set(x, y, (255, 255, 255, 230))
    for x, y in boundary(o):
        cv.set(x, y, (255, 255, 255, 120))
    return cv


def main():
    root = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] else None
    preview = sys.argv[2] if len(sys.argv) > 2 else 'rewind_preview.png'
    art = {'RewindClock': clock(), 'RewindRing': ring()}
    if root:
        for k, cv in art.items():
            cv.save(os.path.join(root, 'UI', 'Common', k + '.png'))
    sheet(list(art.values()), preview, scale=8, cols=2)


if __name__ == '__main__':
    main()
