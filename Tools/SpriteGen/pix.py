"""작은 도트 그리기 도구 - PIL 없이 zlib PNG. 부위(mask)마다 외곽선 + 명암 띠를 칠한다."""
import math
import struct
import zlib

OUT = (18, 12, 20)
SHADOW = (10, 6, 16, 100)


class Canvas:
    def __init__(self, w=64, h=64):
        self.w, self.h = w, h
        self.px = {}

    def get(self, x, y):
        return self.px.get((x, y))

    def set(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            if len(c) == 3:
                c = c + (255,)
            self.px[(x, y)] = c

    def blend(self, x, y, c, a):
        """c(RGB)를 a(0~1) 만큼 덮는다 - 빈 칸이면 반투명 픽셀."""
        if not (0 <= x < self.w and 0 <= y < self.h) or a <= 0:
            return
        old = self.px.get((x, y))
        if old is None or old[3] == 0:
            self.px[(x, y)] = (c[0], c[1], c[2], int(255 * min(1, a)))
            return
        oa = old[3] / 255
        na = a + oa * (1 - a)
        mix = tuple(int((c[i] * a + old[i] * oa * (1 - a)) / na) for i in range(3))
        self.px[(x, y)] = mix + (int(255 * na),)

    def opaque(self, x, y):
        p = self.px.get((x, y))
        return p is not None and p[3] == 255

    def flip(self):
        c = Canvas(self.w, self.h)
        for (x, y), v in self.px.items():
            c.px[(self.w - 1 - x, y)] = v
        return c

    def save(self, path):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)
            for x in range(self.w):
                raw.extend(self.px.get((x, y), (0, 0, 0, 0)))
        ch = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
        with open(path, 'wb') as f:
            f.write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', self.w, self.h, 8, 6, 0, 0, 0))
                    + ch(b'IDAT', zlib.compress(bytes(raw), 9)) + ch(b'IEND', b''))


def read_png(path):
    """8비트 RGBA·RGB·팔레트 PNG를 (w, h, [[(r, g, b, a)]]) 로 읽는다 - 예전 그림 위에 덧그릴 때."""
    d = open(path, 'rb').read()
    i, idat, pal, trns = 8, b'', None, None
    while i < len(d):
        n = struct.unpack('>I', d[i:i + 4])[0]
        t, c = d[i + 4:i + 8], d[i + 8:i + 8 + n]
        i += 12 + n
        if t == b'IHDR':
            w, h, bd, ct = struct.unpack('>IIBB', c[:10])
        elif t == b'PLTE':
            pal = [tuple(c[j:j + 3]) for j in range(0, n, 3)]
        elif t == b'tRNS':
            trns = list(c)
        elif t == b'IDAT':
            idat += c
    raw = zlib.decompress(idat)
    bpp = {6: 4, 2: 3, 3: 1, 4: 2, 0: 1}[ct]
    st, out, prev, k = w * bpp, [], bytearray(w * bpp), 0
    for y in range(h):
        f = raw[k]
        ln = bytearray(raw[k + 1:k + 1 + st])
        k += 1 + st
        for x in range(st):
            a = ln[x - bpp] if x >= bpp else 0
            b = prev[x]
            cc = prev[x - bpp] if x >= bpp else 0
            if f == 1:
                ln[x] = (ln[x] + a) & 255
            elif f == 2:
                ln[x] = (ln[x] + b) & 255
            elif f == 3:
                ln[x] = (ln[x] + (a + b) // 2) & 255
            elif f == 4:
                pp = a + b - cc
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - cc)
                ln[x] = (ln[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else cc)) & 255
        prev = ln
        row = []
        for x in range(w):
            if ct == 6:
                row.append(tuple(ln[x * 4:x * 4 + 4]))
            elif ct == 2:
                row.append(tuple(ln[x * 3:x * 3 + 3]) + (255,))
            elif ct == 3:
                ix = ln[x]
                row.append(pal[ix] + ((trns[ix] if trns and ix < len(trns) else 255),))
            elif ct == 4:
                row.append((ln[x * 2],) * 3 + (ln[x * 2 + 1],))
            else:
                row.append((ln[x],) * 3 + (255,))
        out.append(row)
    return w, h, out


# ---- 모양(mask = 칸 집합) ----
def poly(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    m = set()
    n = len(pts)
    for y in range(int(math.floor(min(ys))), int(math.ceil(max(ys))) + 1):
        for x in range(int(math.floor(min(xs))), int(math.ceil(max(xs))) + 1):
            px, py = x + 0.5, y + 0.5
            inside = False
            j = n - 1
            for i in range(n):
                xi, yi = pts[i]
                xj, yj = pts[j]
                if (yi > py) != (yj > py) and px < (xj - xi) * (py - yi) / (yj - yi) + xi:
                    inside = not inside
                j = i
            if inside:
                m.add((x, y))
    return m


def ellipse(cx, cy, rx, ry):
    m = set()
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            if ((x + 0.5 - cx) / max(rx, 0.01)) ** 2 + ((y + 0.5 - cy) / max(ry, 0.01)) ** 2 <= 1:
                m.add((x, y))
    return m


def line(x0, y0, x1, y1):
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    pts = []
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx + dy
    while True:
        pts.append((x0, y0))
        if x0 == x1 and y0 == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy
    return pts


def thick_line(x0, y0, x1, y1, w=2):
    """두께 w의 막대(양 끝 포함) - 가로/세로 어느 쪽이든 w칸."""
    m = set()
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1
    nx, ny = -dy / L, dx / L
    half = (w - 1) / 2
    steps = int(L * 2) + 1
    for i in range(steps + 1):
        t = i / steps
        cx, cy = x0 + dx * t, y0 + dy * t
        for k in range(w * 2 + 1):
            o = -half + k * 0.5
            if o > half + 1e-6:
                break
            m.add((int(math.floor(cx + nx * o + 0.5)), int(math.floor(cy + ny * o + 0.5))))
    return m


def shift(m, dx, dy):
    return {(x + dx, y + dy) for x, y in m}


# ---- 칠하기 ----
def boundary(m):
    return {p for p in m if any((p[0] + dx, p[1] + dy) not in m for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}


def fill(cv, m, c):
    for x, y in m:
        cv.set(x, y, c)


def part(cv, m, ramp, mode='sphere', outline=OUT, light=(-0.4, -0.45), bands=(0.32, 0.62, 0.86), extra=None):
    """ramp = [어두움, 중간, 밝음, 하이라이트]. 경계 칸은 외곽선, 안쪽은 명암 띠.
    mode: sphere(왼쪽 위 빛 동그라미) / span(가로 원기둥) / vspan(세로 그러데이션) / flat."""
    if not m:
        return
    b = boundary(m) if outline is not None else set()
    xs = [p[0] for p in m]
    ys = [p[1] for p in m]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w, h = max(1, x1 - x0), max(1, y1 - y0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    lx, ly = cx + light[0] * w, cy + light[1] * h
    maxd = math.hypot(max(abs(lx - x0), abs(lx - x1)), max(abs(ly - y0), abs(ly - y1))) or 1
    rows = {}
    for x, y in m:
        r = rows.setdefault(y, [x, x])
        r[0] = min(r[0], x)
        r[1] = max(r[1], x)
    for x, y in m:
        if (x, y) in b:
            cv.set(x, y, outline)
            continue
        if mode == 'sphere':
            t = math.hypot((x - lx) / 1.0, (y - ly) / 1.0) / maxd
        elif mode == 'span':
            l, r = rows[y]
            t = (x - l) / max(1, r - l)
            t = abs(t - (0.5 + light[0] * 0.6)) * 1.6
        elif mode == 'vspan':
            t = (y - y0) / h
        else:
            t = 0.5
        if extra:
            t += extra(x, y)
        lvl = 3 if t < bands[0] else 2 if t < bands[1] else 1 if t < bands[2] else 0
        if mode == 'flat':
            lvl = 1
        cv.set(x, y, ramp[min(lvl, len(ramp) - 1)] if lvl < len(ramp) else ramp[-1])


def outline_outside(cv, color=OUT):
    """불투명 칸 바깥 테두리(아직 안 칠한 칸)를 외곽선으로 - 막대처럼 가는 부위용."""
    add = []
    for (x, y), c in cv.px.items():
        if c[3] != 255:
            continue
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            q = (x + dx, y + dy)
            o = cv.px.get(q)
            if o is None or o[3] < 255:
                add.append(q)
    for q in add:
        cv.set(q[0], q[1], color)


def shadow(cv, cx, cy, rx, ry):
    for p in ellipse(cx, cy, rx, ry):
        cv.set(p[0], p[1], SHADOW)


def glow(cv, cx, cy, r, c, a=0.6):
    """부드러운 빛 무리(반투명) - 가운데일수록 진하게."""
    for y in range(int(cy - r) - 1, int(cy + r) + 2):
        for x in range(int(cx - r) - 1, int(cx + r) + 2):
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy) / r
            if d < 1:
                q = round((1 - d) * 3) / 3  # 도트 느낌 - 3단계로 끊는다
                if q > 0:
                    cv.blend(x, y, c, a * q)


def sheet(frames, path, scale=3, cols=8, bg=(60, 60, 70)):
    w, h = frames[0].w, frames[0].h
    rows = (len(frames) + cols - 1) // cols
    big = Canvas(cols * w * scale, rows * h * scale)
    for i in range(big.w):
        for j in range(big.h):
            pass
    out = [[bg + (255,)] * big.w for _ in range(big.h)]
    for n, f in enumerate(frames):
        ox, oy = (n % cols) * w * scale, (n // cols) * h * scale
        for (x, y), c in f.px.items():
            a = c[3] / 255
            col = tuple(int(c[i] * a + bg[i] * (1 - a)) for i in range(3)) + (255,)
            for dy in range(scale):
                row = out[oy + y * scale + dy]
                for dx in range(scale):
                    row[ox + x * scale + dx] = col
    raw = b''.join(b'\0' + bytes(sum(r, ())) for r in out)
    ch = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', big.w, big.h, 8, 6, 0, 0, 0))
                + ch(b'IDAT', zlib.compress(raw, 6)) + ch(b'IEND', b''))
