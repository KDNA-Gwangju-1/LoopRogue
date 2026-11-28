"""유물 추가 피해 전용 이펙트 7종 (2026-11-09).

python Tools/SpriteGen/draw_relic_fx.py [Assets/Resources/Effects] [미리보기.png]

- 1칸 = 32px(배경 도트 크기, 고퀄 이펙트 chunky 후처리와 같은 굵기), 알파는 3단(255/170/90)만.
- 오른쪽을 향해 그린다(Fx.Play angle로 돌림). 새 폴더·프레임은 boss_ring의 .meta를 복사해 GUID만 바꾼다(이미 있으면 그대로 둠).

  relic_hammer 96x96  대지의 망치   - 대상 칸 충격 + 상하좌우 칸에서 바위 가시가 솟았다 부서짐
  relic_pierce 64x32  관통의 창날   - 대상 칸에서 뒤 칸까지 창끝이 꿰뚫고 지나감 (가운데 = 두 칸 경계)
  relic_echo   48x48  메아리 방울   - 금빛 방울 소리 물결 + 반투명 메아리 베기
  relic_chain  96x96  연쇄 뇌관     - 죽은 몹 자리에서 3x3 불꽃 폭발
  relic_pulse 160x160 파동의 핵     - 5x5로 퍼지는 푸른 파동 고리 + 룬 점
  relic_stomp  96x96  폭군의 쇠군화 - 착지 충격, 8방향 달아오른 균열 + 먼지 고리
  relic_gust   64x32  돌풍 부채     - 밀려나는 몹 뒤로 바람 줄기
"""
import math
import os
import random
import shutil
import sys
import uuid

from PIL import Image

A1, A2, A3 = 255, 170, 90


class C:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.px = self.im.load()

    def set(self, x, y, c, a=A1):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[x, y] = (c[0], c[1], c[2], a)

    def under(self, x, y, c, a=A1):
        """빈 칸에만 칠한다."""
        x, y = int(round(x)), int(round(y))
        if 0 <= x < self.w and 0 <= y < self.h and self.px[x, y][3] == 0:
            self.px[x, y] = (c[0], c[1], c[2], a)

    def rect(self, x0, y0, x1, y1, c, a=A1):
        for y in range(int(y0), int(y1) + 1):
            for x in range(int(x0), int(x1) + 1):
                self.set(x, y, c, a)

    def disc(self, cx, cy, r, c, a=A1):
        for y in range(int(cy - r - 1), int(cy + r + 2)):
            for x in range(int(cx - r - 1), int(cx + r + 2)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    self.set(x, y, c, a)

    def ring(self, cx, cy, r, t, c, a=A1):
        for y in range(int(cy - r - t - 1), int(cy + r + t + 2)):
            for x in range(int(cx - r - t - 1), int(cx + r + t + 2)):
                d = math.hypot(x - cx, y - cy)
                if r - t / 2 <= d <= r + t / 2:
                    self.set(x, y, c, a)

    def line(self, x0, y0, x1, y1, c, a=A1, w=1):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n + 1):
            t = i / max(1, n)
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for dy in range(-(w // 2), w - w // 2):
                for dx in range(-(w // 2), w - w // 2):
                    self.set(x + dx, y + dy, c, a)

    def tri(self, pts, c, a=A1):
        (x0, y0), (x1, y1), (x2, y2) = pts
        minx, maxx = int(min(x0, x1, x2)), int(max(x0, x1, x2)) + 1
        miny, maxy = int(min(y0, y1, y2)), int(max(y0, y1, y2)) + 1
        def s(ax, ay, bx, by, px, py): return (px - bx) * (ay - by) - (ax - bx) * (py - by)
        for y in range(miny, maxy + 1):
            for x in range(minx, maxx + 1):
                d1, d2, d3 = s(x, y, x0, y0, x1, y1), s(x, y, x1, y1, x2, y2), s(x, y, x2, y2, x0, y0)
                if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)):
                    self.set(x, y, c, a)

    def outline(self, c=(18, 12, 20), a=A1):
        """불투명 그림 바깥 1칸 외곽선."""
        src = self.im.copy().load()
        for y in range(self.h):
            for x in range(self.w):
                if src[x, y][3] != 0:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < self.w and 0 <= ny < self.h and src[nx, ny][3] == A1:
                        self.px[x, y] = (c[0], c[1], c[2], a)
                        break


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def fade(t):
    """0~1 진행도 -> 알파 단계."""
    return A1 if t < 0.5 else A2 if t < 0.8 else A3


# ---------------------------------------------------------------- 대지의 망치
def hammer():
    W = 96
    ROCK = [hexc(c) for c in ("#2e2018", "#5c3f28", "#8f6438", "#c8955a", "#ecc58a")]
    DUST = hexc("#b89c78")
    FLASH = hexc("#fff4c8")
    frames = []
    rng = random.Random(3)
    spikes = [(16, 48, (-1, 0)), (80, 48, (1, 0)), (48, 16, (0, -1)), (48, 80, (0, 1))]
    for f in range(7):
        c = C(W, W)
        # 가운데 충격
        if f <= 1:
            c.disc(48, 48, 6 + f * 4, FLASH, A1 if f == 0 else A2)
        # 균열 - 가운데에서 네 방향
        reach = min(1.0, (f + 1) / 3)
        if f <= 5:
            a = fade(max(0, f - 2) / 4)
            for _, _, (dx, dy) in spikes:
                L = 30 * reach
                px, py = 48, 48
                for i in range(int(L)):
                    jx = (1 if (i // 4) % 2 else -1) * (abs(dy))
                    jy = (1 if (i // 4) % 2 else -1) * (abs(dx))
                    c.set(48 + dx * i + jx * (i % 4 == 2), 48 + dy * i + jy * (i % 4 == 2), ROCK[0], a)
        # 바위 가시 - 솟았다(1~3) 부서짐(4~6)
        for sx, sy, (dx, dy) in spikes:
            if 1 <= f <= 3:
                h = [10, 18, 22][f - 1]
                base = sy + 9
                for k, off in enumerate((-8, 0, 7)):
                    hh = h - abs(off) // 2 - (k == 2) * 2
                    pts = [(sx + off - 6, base), (sx + off + 6, base), (sx + off + (1 if off < 0 else -1), base - hh)]
                    c.tri(pts, ROCK[2])
                    c.line(sx + off - 3, base - 1, sx + off, base - hh + 1, ROCK[3])
                    c.set(sx + off, base - hh + 1, ROCK[4])
                    c.line(sx + off + 2, base - 1, sx + off + 3, base - 3, ROCK[1])
            elif f >= 4:
                t = (f - 4) / 2
                for i in range(7):
                    ang = rng.uniform(0, math.pi * 2)
                    r = 4 + t * 10 + rng.uniform(0, 3)
                    x, y = sx + math.cos(ang) * r, sy + 2 + math.sin(ang) * r * 0.6 + t * 3
                    c.rect(x, y, x + 1, y + 1, ROCK[rng.randint(1, 3)], fade(t + 0.3))
                c.ring(sx, sy + 5, 4 + (f - 4) * 3, 2, DUST, A3)
        c.outline(a=A1)
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 관통의 창날
def pierce():
    W, H = 64, 32
    STEEL = [hexc(c) for c in ("#3a4250", "#7c8798", "#c5ced9", "#ffffff")]
    TRAIL = hexc("#d8f0ff")
    frames = []
    tips = [24, 40, 54, 62, 62, 62]
    for f, tip in enumerate(tips):
        c = C(W, H)
        y = 16
        if f <= 3:
            # 잔상 줄
            c.line(max(0, tip - 30), y, tip - 8, y, TRAIL, A3, w=7)
            c.line(max(0, tip - 22), y, tip - 8, y, TRAIL, A2, w=3)
            # 자루
            c.line(max(0, tip - 26), y, tip - 12, y, hexc("#6b4a2e"), A1, w=3)
            # 창끝(마름모)
            c.tri([(tip - 14, y - 6), (tip - 14, y + 6), (tip, y)], STEEL[1])
            c.tri([(tip - 14, y - 6), (tip - 14, y), (tip, y)], STEEL[2])
            c.line(tip - 12, y - 2, tip - 2, y, STEEL[3])
            c.rect(tip - 16, y - 5, tip - 15, y + 5, STEEL[0])
        if f >= 2:
            # 뒤 칸 꿰뚫린 자리 - 별 섬광
            t = (f - 2) / 3
            r = 5 + f * 3
            a = fade(t)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                c.line(48, y, 48 + dx * r, y + dy * r, STEEL[3], a)
            for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                c.line(48, y, 48 + dx * r * 0.5, y + dy * r * 0.5, TRAIL, a)
        c.outline(a=A2)
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 메아리 방울
def echo():
    W = 48
    GOLD = [hexc(c) for c in ("#6b4a12", "#b8862a", "#ffd966", "#fff6c8")]
    GHOST = hexc("#cfe6ff")
    frames = []
    for f in range(7):
        c = C(W, W)
        cx, cy = 24, 24
        # 방울(위쪽, 흔들림)
        if f <= 4:
            sw = [0, 2, -2, 1, 0][f]
            bx, by = 24 + sw, 9
            c.tri([(bx - 5, by + 6), (bx + 5, by + 6), (bx, by - 3)], GOLD[1])
            c.rect(bx - 5, by + 5, bx + 5, by + 6, GOLD[1])
            c.line(bx - 2, by + 4, bx - 1, by - 1, GOLD[2])
            c.set(bx - 2, by + 1, GOLD[3])
            c.rect(bx - 1, by + 7, bx + 1, by + 8, GOLD[0])
        # 소리 물결
        for k in range(2):
            r = 5 + (f - k * 2) * 4
            if 4 <= r <= 23:
                c.ring(cx, cy, r, 1.2, GOLD[2] if k == 0 else GOLD[3], fade((f - k * 2) / 6))
        # 메아리 베기(반투명 대각선 호)
        if 1 <= f <= 4:
            a = A2 if f <= 2 else A3
            for i in range(-12, 13):
                x = cx + i
                y = cy - i * 0.8 + (i * i) / 30
                c.set(x, y, GHOST, a)
                c.set(x, y + 1, GHOST, a)
                if abs(i) < 8:
                    c.set(x, y - 1, (255, 255, 255), a)
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 연쇄 뇌관
def chain():
    W = 96
    FIRE = [hexc(c) for c in ("#5a2a2a", "#c23a1e", "#ff8a1e", "#ffcf4a", "#fff3b0")]
    SMOKE = hexc("#3a2e34")
    rng = random.Random(7)
    sparks = [(rng.uniform(0, math.pi * 2), rng.uniform(0.7, 1.2)) for _ in range(14)]
    frames = []
    for f in range(8):
        c = C(W, W)
        cx = cy = 48
        R = [6, 16, 26, 34, 40, 43, 45, 46][f]
        if f <= 5:
            # 바깥부터 연기 -> 붉은 -> 주황 -> 노랑 -> 흰 심지
            layers = [(R, SMOKE if f >= 3 else FIRE[1]), (R * 0.82, FIRE[1]), (R * 0.64, FIRE[2]), (R * 0.42, FIRE[3]), (R * 0.2, FIRE[4])]
            shrink = max(0, f - 3) * 0.25
            for i, (r, col) in enumerate(layers):
                rr = r * (1 - shrink * (i / 4))
                if rr > 1:
                    # 울퉁불퉁한 불꽃 가장자리
                    for y in range(int(cy - rr - 3), int(cy + rr + 4)):
                        for x in range(int(cx - rr - 3), int(cx + rr + 4)):
                            ang = math.atan2(y - cy, x - cx)
                            wob = 1 + 0.12 * math.sin(ang * 7 + f + i)
                            if math.hypot(x - cx, y - cy) <= rr * wob:
                                c.set(x, y, col, fade(f / 8) if i == 0 else A1)
        else:
            # 꺼져 가는 연기 고리
            c.ring(cx, cy, R - 4, 6, SMOKE, A3)
            c.ring(cx, cy, R - 8, 2, FIRE[1], A3)
        # 튀는 불똥
        if 1 <= f <= 6:
            for ang, sp in sparks:
                r = 8 + f * 7 * sp
                x, y = cx + math.cos(ang) * r, cy + math.sin(ang) * r
                c.rect(x, y, x + 1, y + 1, FIRE[3] if f < 4 else FIRE[2], fade(f / 7))
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 파동의 핵
def pulse():
    W = 160
    BLUE = [hexc(c) for c in ("#1e4a8a", "#3aa0e0", "#8fe0ff", "#e8fbff")]
    frames = []
    for f in range(8):
        c = C(W, W)
        cx = cy = 80
        R = [10, 24, 38, 50, 60, 68, 74, 78][f]
        t = f / 7
        # 핵(가운데) 섬광
        if f <= 2:
            c.disc(cx, cy, 9 - f * 2, BLUE[3])
            c.ring(cx, cy, 11 - f * 2, 2, BLUE[2], A2)
        # 메인 파동 고리(두께 줄어듦) + 안쪽 잔물결
        th = max(2, 7 - f)
        a = fade(t)
        c.ring(cx, cy, R, th + 2, BLUE[1], A3 if f > 4 else A2)
        c.ring(cx, cy, R, th, BLUE[2], a)
        c.ring(cx, cy, R + th / 2 - 0.5, 1, BLUE[3], a)
        if R - 14 > 4:
            c.ring(cx, cy, R - 14, 1.5, BLUE[1], A3)
        # 룬 점(고리 위 8개, 돌아감)
        for k in range(8):
            ang = k * math.pi / 4 + f * 0.12
            x, y = cx + math.cos(ang) * R, cy + math.sin(ang) * R
            c.rect(x - 2, y - 2, x + 2, y + 2, BLUE[0], a)
            c.rect(x - 1, y - 1, x + 1, y + 1, BLUE[3], a)
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 폭군의 쇠군화
def stomp():
    W = 96
    CRACK = hexc("#22140f")
    HOT = [hexc(c) for c in ("#8a2a12", "#e0561e", "#ffa040", "#ffe2a0")]
    DUST = [hexc("#7a6a5e"), hexc("#a8968a")]
    rng = random.Random(11)
    debris = [(rng.uniform(0, math.pi * 2), rng.uniform(0.6, 1.1)) for _ in range(12)]
    dirs = [(math.cos(k * math.pi / 4 + 0.2 * ((k % 2) - 0.5)), math.sin(k * math.pi / 4 + 0.2 * ((k % 2) - 0.5))) for k in range(8)]
    frames = []
    for f in range(7):
        c = C(W, W)
        cx = cy = 48
        # 충격 섬광(쇠 빛 -> 붉은 열)
        if f <= 1:
            c.disc(cx, cy, 10 + f * 6, HOT[3] if f == 0 else HOT[2], A1 if f == 0 else A2)
        # 8방향 균열, 안쪽은 달아오름
        L = min(44, 14 + f * 12)
        for dx, dy in dirs:
            pts = []
            x, y = cx, cy
            for i in range(int(L)):
                wig = math.sin(i * 0.7 + dx * 3) * 1.2
                x, y = cx + dx * i - dy * wig, cy + dy * i + dx * wig
                pts.append((x, y))
            for i, (x, y) in enumerate(pts):
                if f <= 5:
                    c.set(x, y, CRACK, A1 if f <= 3 else A2)
                    c.set(x + 1, y, CRACK, A2 if f <= 3 else A3)
                    if i < L * 0.6 and f <= 4:
                        c.set(x, y, HOT[1] if i > L * 0.3 else HOT[2])
        # 먼지 고리
        if f >= 1:
            r = 12 + f * 5
            c.ring(cx, cy + 2, r, 5, DUST[0], A3)
            c.ring(cx, cy + 2, r - 1, 2, DUST[1], A2 if f < 5 else A3)
        # 튀는 파편
        if 1 <= f <= 5:
            for ang, sp in debris:
                r = 8 + f * 8 * sp
                x, y = cx + math.cos(ang) * r, cy + math.sin(ang) * r * 0.8 - (6 - abs(3 - f) * 2)
                c.rect(x, y, x + 1, y + 1, HOT[0] if f < 3 else DUST[0])
        frames.append(c.im)
    return frames


# ---------------------------------------------------------------- 돌풍 부채
def gust():
    W, H = 64, 32
    WIND = [hexc(c) for c in ("#5fa88a", "#9fe0c0", "#dff7e8", "#ffffff")]
    frames = []
    streams = [(8, 0.0, 34), (16, 0.6, 44), (24, 1.2, 30)]
    for f in range(6):
        c = C(W, H)
        for y0, ph, ln in streams:
            head = 8 + f * 10 + ph * 6
            for i in range(ln):
                x = head - i
                if not (0 <= x < W):
                    continue
                y = y0 + math.sin((x + f * 4) * 0.25 + ph) * 2
                a = A1 if i < ln * 0.35 else A2 if i < ln * 0.7 else A3
                col = WIND[3] if i < 3 else WIND[2] if i < ln * 0.4 else WIND[1]
                c.set(x, y, col, a if f < 4 else A3)
                c.set(x, y + 1, WIND[1] if i > 2 else col, A2 if f < 4 else A3)
            # 끝 말림
            if f <= 3:
                hx = head
                for k in range(6):
                    ang = k * 0.9
                    c.set(hx + math.cos(ang) * 3, y0 - 3 + math.sin(ang) * 3, WIND[2], A2)
        frames.append(c.im)
    return frames


EFFECTS = {
    "relic_hammer": hammer, "relic_pierce": pierce, "relic_echo": echo, "relic_chain": chain,
    "relic_pulse": pulse, "relic_stomp": stomp, "relic_gust": gust,
}


def write_meta(src, dst):
    if os.path.exists(dst):
        return
    text = open(src, encoding="utf-8").read()
    old = [l for l in text.splitlines() if l.startswith("guid:")][0]
    open(dst, "w", encoding="utf-8", newline="\n").write(text.replace(old, "guid: " + uuid.uuid4().hex, 1))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else None
    preview = sys.argv[2] if len(sys.argv) > 2 else "relic_fx_preview.png"
    rows = []
    for name, fn in EFFECTS.items():
        frames = fn()
        rows.append((name, frames))
        if out:
            d = os.path.join(out, name)
            os.makedirs(d, exist_ok=True)
            ref = os.path.join(out, "boss_ring")
            write_meta(ref + ".meta", d + ".meta")
            for i, im in enumerate(frames):
                p = os.path.join(d, f"{i}.png")
                im.save(p)
                write_meta(os.path.join(ref, "0.png.meta"), p + ".meta")
    # 미리보기: 효과마다 한 줄, 어두운 바닥 위 3배
    S = 3
    cell = 330
    Wd = 40 + cell * 8
    Ht = sum(max(f[0].height for f in [fr]) * S // 2 + 20 for _, fr in rows)
    sheet = Image.new("RGBA", (Wd, sum(fr[0].height * 2 + 20 for _, fr in rows) + 20), (38, 36, 48, 255))
    y = 10
    for name, fr in rows:
        for i, im in enumerate(fr[:8]):
            big = im.resize((im.width * 2, im.height * 2), Image.NEAREST)
            sheet.alpha_composite(big, (20 + i * cell, y))
        y += fr[0].height * 2 + 20
    sheet.save(preview)
    print("ok", preview)


if __name__ == "__main__":
    main()
