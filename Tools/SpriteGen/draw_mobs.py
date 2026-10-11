"""LoopRogue 새 몹 4종 도트 그림 생성 (PIL 없이 zlib PNG).
Shaman(고블린 주술사) / OrcCharger(오크 돌격병) / Jelly(분열 젤리) / Rogue(그림자 암살자)
48x48, idle 4 / walk 4 / attack 3 프레임, south/east/west/north. west = east 좌우 반전.
usage: python draw_mobs.py <Assets/Resources/Sprites 경로> <미리보기 png 경로>"""
import math
import os
import struct
import sys
import zlib

W = H = 48
GROUND = 41


def hexc(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def shade(c, f):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))), max(0, min(255, int(c[2] * f))), c[3])


class Canvas:
    def __init__(self):
        self.px = [[None] * W for _ in range(H)]

    def set(self, x, y, c):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < W and 0 <= y < H and c is not None:
            self.px[y][x] = c

    def get(self, x, y):
        if 0 <= x < W and 0 <= y < H:
            return self.px[y][x]
        return None

    def ellipse(self, cx, cy, rx, ry, c, lit=True):
        """채운 타원 - 왼쪽 위는 밝게, 오른쪽 아래는 어둡게."""
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                dx = (x + 0.5 - cx) / max(rx, 0.5)
                dy = (y + 0.5 - cy) / max(ry, 0.5)
                if dx * dx + dy * dy <= 1.0:
                    col = c
                    if lit:
                        s = dx * 0.6 + dy * 0.8
                        if s < -0.55:
                            col = shade(c, 1.25)
                        elif s > 0.45:
                            col = shade(c, 0.75)
                    self.set(x, y, col)

    def rect(self, x0, y0, x1, y1, c):
        for y in range(int(y0), int(y1) + 1):
            for x in range(int(x0), int(x1) + 1):
                self.set(x, y, c)

    def trapezoid(self, cx, y0, y1, w0, w1, c, lit=True):
        """y0에서 너비 w0, y1에서 너비 w1인 사다리꼴(가운데 cx) - 왼쪽 가장자리 밝게, 오른쪽 어둡게."""
        for y in range(int(y0), int(y1) + 1):
            t = (y - y0) / max(1, (y1 - y0))
            half = (w0 + (w1 - w0) * t) / 2
            xs = int(round(cx - half))
            xe = int(round(cx + half)) - 1
            for x in range(xs, xe + 1):
                col = c
                if lit:
                    if x <= xs + 1:
                        col = shade(c, 1.2)
                    elif x >= xe - 1:
                        col = shade(c, 0.75)
                self.set(x, y, col)

    def line(self, x0, y0, x1, y1, c):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n + 1):
            t = i / max(1, n)
            self.set(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, c)

    def outline(self, c):
        add = []
        for y in range(H):
            for x in range(W):
                if self.px[y][x] is not None:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    p = self.get(x + dx, y + dy)
                    if p is not None and p[3] > 100:
                        add.append((x, y))
                        break
        for x, y in add:
            self.px[y][x] = c

    def under_shadow(self, cx, rx):
        for y in range(GROUND - 2, GROUND + 2):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                dx = (x + 0.5 - cx) / rx
                dy = (y + 0.5 - GROUND) / 1.8
                if dx * dx + dy * dy <= 1 and self.get(x, y) is None:
                    self.set(x, y, (0, 0, 0, 70))

    def mirrored(self):
        m = Canvas()
        for y in range(H):
            m.px[y] = list(reversed(self.px[y]))
        return m


def write_png(path, canvas):
    raw = b''
    for y in range(H):
        raw += b'\x00'
        for x in range(W):
            c = canvas.px[y][x] or (0, 0, 0, 0)
            raw += bytes(c)

    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 6, 0, 0, 0)) + \
        chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(data)


OUTLINE = hexc('1b1420')

# ---------------------------------------------------------------- 고블린 주술사


def draw_shaman(d, pose, t):
    c = Canvas()
    skin = hexc('6fae4a')
    robe = hexc('6b4a2b')
    trim = hexc('c9a24a')
    staff = hexc('7a5530')
    orb = hexc('7dffa0')
    bob = [0, 0, 1, 1][t] if pose == 'idle' else ([0, 1, 0, 1][t] if pose == 'walk' else 0)
    raise_ = [2, 5, 3][t] if pose == 'attack' else 0
    cx = 24
    y = bob
    # 발
    lift = [0, 2, 0, 0][t] if pose == 'walk' else 0
    lift2 = [0, 0, 0, 2][t] if pose == 'walk' else 0
    boot = hexc('3a2a1c')
    if d == 'east':
        c.rect(cx - 3, 38 - lift, cx, 40 - lift, boot)
        c.rect(cx + 1, 38 - lift2, cx + 4, 40 - lift2, boot)
    else:
        c.rect(cx - 5, 38 - lift, cx - 2, 40 - lift, boot)
        c.rect(cx + 2, 38 - lift2, cx + 5, 40 - lift2, boot)
    # 지팡이(뒤쪽이면 몸보다 먼저)
    sx = {'south': cx + 9, 'north': cx - 9, 'east': cx + 7}[d]

    def draw_staff():
        top = 13 + y - raise_
        c.line(sx, top, sx, 40 + y - raise_ // 2, staff)
        c.line(sx + 1, top + 1, sx + 1, 40 + y - raise_ // 2, shade(staff, 0.7))
        r = 2.6 + (1.2 if pose == 'attack' and t == 1 else 0)
        if pose == 'attack':
            c.ellipse(sx + 0.5, top - 2, r + 2, r + 2, hexc('7dffa0', 90), lit=False)
        c.ellipse(sx + 0.5, top - 2, r, r, orb)
        c.set(sx - 1, top - 4, hexc('e8fff0'))
    if d == 'north':
        draw_staff()
    # 로브
    c.trapezoid(cx, 24 + y, 38 + y, 11, 17, robe)
    c.rect(cx - 7, 37 + y, cx + 7, 38 + y, trim)
    c.rect(cx - 5, 29 + y, cx + 5, 29 + y, trim)
    # 머리
    hx = cx + (1 if d == 'east' else 0)
    hy = 18 + y
    if d == 'east':
        # 귀는 뒤쪽(왼쪽)으로
        c.line(hx - 5, hy, hx - 11, hy - 3, skin)
        c.line(hx - 5, hy + 1, hx - 10, hy - 2, shade(skin, 0.8))
    else:
        c.line(hx - 5, hy, hx - 11, hy - 3, skin)
        c.line(hx - 5, hy + 1, hx - 10, hy - 2, shade(skin, 0.8))
        c.line(hx + 5, hy, hx + 11, hy - 3, skin)
        c.line(hx + 5, hy + 1, hx + 10, hy - 2, shade(skin, 0.8))
    c.ellipse(hx, hy, 6, 5.5, skin)
    # 뼈 머리장식 + 깃털
    c.rect(hx - 4, hy - 6, hx + 4, hy - 5, hexc('e8e0cc'))
    c.line(hx + (2 if d != 'east' else -2), hy - 7, hx + (4 if d != 'east' else -5), hy - 12, hexc('d0402a'))
    c.line(hx + (3 if d != 'east' else -3), hy - 7, hx + (5 if d != 'east' else -6), hy - 11, hexc('ff6a4a'))
    if d == 'south':
        c.set(hx - 2, hy, hexc('ffe14a'))
        c.set(hx + 2, hy, hexc('ffe14a'))
        c.set(hx - 2, hy + 1, hexc('1b1420'))
        c.set(hx + 2, hy + 1, hexc('1b1420'))
        c.rect(hx - 1, hy + 3, hx + 1, hy + 3, shade(skin, 0.6))
    elif d == 'east':
        c.set(hx + 3, hy, hexc('ffe14a'))
        c.set(hx + 3, hy + 1, hexc('1b1420'))
        c.set(hx + 6, hy + 1, skin)  # 코
        c.set(hx + 7, hy + 2, shade(skin, 0.8))
    # 팔(지팡이 잡은 손)
    if d != 'north':
        hand_y = 27 + y - raise_
        c.ellipse(sx - 1, hand_y, 2, 2, skin)
        c.line(cx + (4 if d == 'south' else 2), 26 + y, sx - 1, hand_y, robe)
    if d != 'north':
        draw_staff()
    c.outline(OUTLINE)
    c.under_shadow(cx, 8)
    return c

# ---------------------------------------------------------------- 오크 돌격병


def draw_orc(d, pose, t):
    c = Canvas()
    skin = hexc('6f8f4e')
    armor = hexc('8a3b26')
    metal = hexc('8c8f99')
    pants = hexc('4a3828')
    horn = hexc('e8dfc8')
    bob = [0, 0, 1, 1][t] if pose == 'idle' else ([0, 1, 0, 1][t] if pose == 'walk' else 0)
    lunge = [1, 4, 2][t] if pose == 'attack' else 0
    cx = 24
    y = bob
    fx = lunge if d == 'east' else 0
    fy = lunge if d == 'south' else (-lunge if d == 'north' else 0)
    lift = [0, 2, 0, 0][t] if pose == 'walk' else 0
    lift2 = [0, 0, 0, 2][t] if pose == 'walk' else 0
    boot = hexc('2e241c')
    # 다리
    if d == 'east':
        c.rect(cx - 4, 33 + y, cx - 1, 40 - lift, pants)
        c.rect(cx + 1, 33 + y, cx + 4, 40 - lift2, pants)
        c.rect(cx - 5, 39 - lift, cx - 1, 40 - lift, boot)
        c.rect(cx + 1, 39 - lift2, cx + 6, 40 - lift2, boot)
    else:
        c.rect(cx - 7, 33 + y, cx - 3, 40 - lift, pants)
        c.rect(cx + 3, 33 + y, cx + 7, 40 - lift2, pants)
        c.rect(cx - 8, 39 - lift, cx - 3, 40 - lift, boot)
        c.rect(cx + 3, 39 - lift2, cx + 8, 40 - lift2, boot)
    bx, by = cx + fx, y + fy
    # 몸통
    if d == 'east':
        c.ellipse(bx, 27 + by, 9, 8, armor)
    else:
        c.ellipse(bx, 27 + by, 11, 8, armor)
        c.rect(bx - 1, 21 + by, bx + 1, 34 + by, shade(armor, 0.7))  # 가죽 끈
    c.rect(bx - 8 if d != 'east' else bx - 7, 32 + by, bx + 8 if d != 'east' else bx + 7, 33 + by, hexc('3a2a1c'))
    # 팔
    if d == 'east':
        c.ellipse(bx + 2 + lunge, 28 + by, 3, 4, skin)
        c.ellipse(bx + 4 + lunge, 31 + by, 2.5, 2.5, shade(skin, 0.9))
    else:
        c.ellipse(bx - 12, 28 + by, 3, 5, skin)
        c.ellipse(bx + 12, 28 + by, 3, 5, skin)
        c.ellipse(bx - 12, 33 + by, 2.5, 2.5, shade(skin, 0.9))
        c.ellipse(bx + 12, 33 + by, 2.5, 2.5, shade(skin, 0.9))
    # 어깨 갑옷
    if d == 'east':
        c.ellipse(bx - 1, 21 + by, 5, 3.5, metal)
    else:
        c.ellipse(bx - 10, 21 + by, 4.5, 3.5, metal)
        c.ellipse(bx + 10, 21 + by, 4.5, 3.5, metal)
    # 머리
    hx = bx + (3 if d == 'east' else 0)
    hy = 15 + by
    c.ellipse(hx, hy, 6, 5.5, skin)
    # 투구 + 뿔
    c.ellipse(hx, hy - 3, 6.5, 3.5, metal)
    c.rect(hx - 6, hy - 2, hx + 6, hy - 1, shade(metal, 0.8))
    if d == 'east':
        c.line(hx + 4, hy - 4, hx + 8, hy - 7, horn)
        c.line(hx + 8, hy - 7, hx + 9, hy - 10, horn)
        c.line(hx - 4, hy - 4, hx - 7, hy - 8, shade(horn, 0.8))
    else:
        c.line(hx - 6, hy - 3, hx - 9, hy - 7, horn)
        c.line(hx - 9, hy - 7, hx - 9, hy - 10, horn)
        c.line(hx + 6, hy - 3, hx + 9, hy - 7, horn)
        c.line(hx + 9, hy - 7, hx + 9, hy - 10, horn)
    if d == 'south':
        c.set(hx - 2, hy + 1, hexc('ff3a2a'))
        c.set(hx + 2, hy + 1, hexc('ff3a2a'))
        c.set(hx - 3, hy + 4, horn)  # 엄니
        c.set(hx + 3, hy + 4, horn)
        c.set(hx - 3, hy + 3, horn)
        c.set(hx + 3, hy + 3, horn)
    elif d == 'east':
        c.set(hx + 3, hy + 1, hexc('ff3a2a'))
        c.set(hx + 5, hy + 3, horn)
        c.set(hx + 5, hy + 2, horn)
    c.outline(OUTLINE)
    c.under_shadow(cx, 10)
    return c

# ---------------------------------------------------------------- 분열 젤리


def draw_jelly(d, pose, t):
    c = Canvas()
    body = hexc('4a8cff', 225)
    core = hexc('1f4fb8', 235)
    cx = 24
    if pose == 'idle':
        rx, ry, oy, ox = [(10, 8, 0, 0), (10.5, 7.5, 0, 0), (11, 7, 0, 0), (10.5, 7.5, 0, 0)][t]
    elif pose == 'walk':
        rx, ry, oy, ox = [(11, 7, 0, 0), (9, 9, -3, 0), (9.5, 8.5, -4, 0), (10.5, 7.5, -1, 0)][t]
    else:
        rx, ry, oy, ox = [(11.5, 6.5, 0, 0), (9, 9.5, -2, 4), (10, 8, -1, 2)][t]
    fx = ox if d == 'east' else 0
    fy = (ox // 2 if d == 'south' else (-ox // 2 if d == 'north' else 0))
    bcx = cx + fx
    bcy = GROUND - ry + oy + fy
    if d == 'east' and pose == 'attack':
        rx += 1
    c.ellipse(bcx, bcy, rx, ry, body)
    # 위쪽 하이라이트
    c.ellipse(bcx - rx * 0.4, bcy - ry * 0.45, rx * 0.3, ry * 0.2, hexc('cfe4ff', 230), lit=False)
    # 핵
    kx = bcx + (2 if d == 'east' else 0)
    c.ellipse(kx, bcy + 1, 3, 2.6, core, lit=False)
    c.set(kx - 1, bcy, hexc('6aa8ff'))
    # 눈
    if d == 'south':
        for ex in (bcx - 4, bcx + 4):
            c.rect(ex - 1, bcy - 3, ex, bcy - 2, hexc('10132a'))
            c.set(ex - 1, bcy - 3, hexc('ffffff'))
    elif d == 'east':
        for ex in (bcx + 4, bcx + 8):
            c.rect(ex - 1, bcy - 3, ex, bcy - 2, hexc('10132a'))
            c.set(ex - 1, bcy - 3, hexc('ffffff'))
    c.outline(hexc('132a66'))
    c.under_shadow(cx + fx, rx - 1)
    return c

# ---------------------------------------------------------------- 그림자 암살자


def draw_rogue(d, pose, t):
    c = Canvas()
    cloak = hexc('3b2f5a')
    scarf = hexc('a02a3a')
    blade = hexc('d8dde8')
    eye = hexc('d080ff')
    bob = [0, 0, 1, 1][t] if pose == 'idle' else ([0, 1, 0, 1][t] if pose == 'walk' else 0)
    cx = 24
    y = bob
    thrust = [1, 5, 2][t] if pose == 'attack' else 0
    lift = [0, 2, 0, 0][t] if pose == 'walk' else 0
    lift2 = [0, 0, 0, 2][t] if pose == 'walk' else 0
    boot = hexc('1e1828')
    if d == 'east':
        c.rect(cx - 3, 36 - lift, cx - 1, 40 - lift, boot)
        c.rect(cx + 1, 36 - lift2, cx + 4, 40 - lift2, boot)
    else:
        c.rect(cx - 4, 36 - lift, cx - 2, 40 - lift, boot)
        c.rect(cx + 2, 36 - lift2, cx + 4, 40 - lift2, boot)
    # 단검(남쪽: 오른손, 동쪽: 앞쪽)
    def draw_dagger():
        if d == 'south':
            hx, hy = cx + 8, 29 + y + thrust
            c.line(hx, hy, hx, hy + 5 + thrust // 2, blade)
            c.set(hx + 1, hy + 1, shade(blade, 0.7))
            c.rect(hx - 1, hy - 1, hx + 1, hy - 1, hexc('6a5030'))
        elif d == 'east':
            hx, hy = cx + 6 + thrust, 28 + y
            c.line(hx, hy, hx + 5 + thrust // 2, hy, blade)
            c.set(hx + 1, hy + 1, shade(blade, 0.7))
            c.rect(hx - 1, hy - 1, hx - 1, hy + 1, hexc('6a5030'))
        else:
            hx, hy = cx - 8, 27 + y - thrust
            c.line(hx, hy, hx, hy - 4, blade)
    if d == 'north':
        draw_dagger()
    # 망토
    c.trapezoid(cx + (1 if d == 'east' else 0), 21 + y, 37 + y, 10, 16, cloak)
    # 망토 자락 펄럭임
    flap = [0, 1, 0, -1][t] if pose != 'attack' else -2
    if d == 'east':
        c.trapezoid(cx - 6 + flap, 26 + y, 37 + y, 2, 5, shade(cloak, 0.8), lit=False)
    # 목도리
    c.rect(cx - 5, 22 + y, cx + 5, 23 + y, scarf)
    if d == 'north':
        c.line(cx + 3, 23 + y, cx + 5 + flap, 30 + y, scarf)
    elif d == 'east':
        c.line(cx - 4, 23 + y, cx - 8 + flap, 27 + y, scarf)
    # 두건
    hx = cx + (1 if d == 'east' else 0)
    hy = 16 + y
    c.ellipse(hx, hy, 6.5, 6.5, cloak)
    c.line(hx, hy - 7, hx + (-3 if d == 'east' else 0), hy - 9, cloak)  # 두건 꼭지
    if d == 'south':
        c.ellipse(hx, hy + 1, 4, 3.5, hexc('0c0a12'), lit=False)
        c.set(hx - 2, hy + 1, eye)
        c.set(hx + 2, hy + 1, eye)
    elif d == 'east':
        c.ellipse(hx + 3, hy + 1, 3, 3.5, hexc('0c0a12'), lit=False)
        c.set(hx + 4, hy + 1, eye)
    if d != 'north':
        # 단검 든 손
        hand = (cx + 8, 28 + y + thrust) if d == 'south' else (cx + 5 + thrust, 28 + y)
        c.ellipse(hand[0], hand[1], 1.6, 1.6, hexc('c9a080'))
        draw_dagger()
    c.outline(OUTLINE)
    c.under_shadow(cx, 8)
    return c


MOBS = {
    'Shaman': draw_shaman,
    'OrcCharger': draw_orc,
    'Jelly': draw_jelly,
    'Rogue': draw_rogue,
}
POSES = {'idle': 4, 'walk': 4, 'attack': 3}


def main(out_root, preview_path):
    sheet = []  # (mob, pose, dir, frame, canvas)
    for mob, fn in MOBS.items():
        folder = os.path.join(out_root, mob)
        os.makedirs(folder, exist_ok=True)
        for pose, n in POSES.items():
            for t in range(n):
                frames = {
                    'south': fn('south', pose, t),
                    'north': fn('north', pose, t),
                    'east': fn('east', pose, t),
                }
                frames['west'] = frames['east'].mirrored()
                for d, cv in frames.items():
                    write_png(os.path.join(folder, f'{pose}_{d}_{t}.png'), cv)
                    sheet.append((mob, pose, d, t, cv))
    # 미리보기: 몹마다 한 줄(방향 4 × idle0, walk1, attack1), 4배 확대, 회색 배경
    cols = [(p, d, t) for d in ('south', 'east', 'west', 'north') for (p, t) in (('idle', 0), ('walk', 1), ('attack', 1))]
    S = 4
    PW, PH = len(cols) * W * S, len(MOBS) * H * S
    img = [[(70, 72, 80, 255)] * PW for _ in range(PH)]
    lookup = {(m, p, d, t): cv for m, p, d, t, cv in sheet}
    for r, mob in enumerate(MOBS):
        for ci, (p, d, t) in enumerate(cols):
            cv = lookup[(mob, p, d, t)]
            for y in range(H):
                for x in range(W):
                    px = cv.px[y][x]
                    if px is None:
                        continue
                    a = px[3] / 255
                    for yy in range(S):
                        row = img[r * H * S + y * S + yy]
                        for xx in range(S):
                            bg = row[ci * W * S + x * S + xx]
                            row[ci * W * S + x * S + xx] = tuple(int(px[k] * a + bg[k] * (1 - a)) for k in range(3)) + (255,)
    raw = b''.join(b'\x00' + b''.join(bytes(p) for p in row) for row in img)

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    with open(preview_path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', PW, PH, 8, 6, 0, 0, 0)) +
                chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
