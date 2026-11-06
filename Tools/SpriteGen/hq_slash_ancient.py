# 고대 베기: pixelfx 청록 베기(바로 시작) + 몹 발밑에 pixelfx 룬 마법진(청록·금빛)이 펼쳐지며 돌고
# + pixelfx 둔기 타격으로 돌 파편 + 금빛 고대 문자가 떠올라 사라진다. 판 256px(가운데 = 용사 몸 가운데)
import os, json, subprocess, math, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
PFX = r"C:\Users\dignw\pixel-tools\pixelfx"; NODE = r"C:\Users\dignw\pixel-tools\node\node.exe"
C = 256; HC = C // 2
CELL = 51.2
# 방향별 판: 옆(오른쪽, 왼쪽은 게임에서 뒤집기) / 위 / 아래 - 위·아래는 베기만 돌리고 검·마법진은 똑바로(하늘에서 떨어지니까)
DIR = os.environ.get("DIR", "side")
EX, EY = {"side": (HC + CELL, HC), "up": (HC, HC - CELL), "down": (HC, HC + CELL)}[DIR]   # 몹 가운데
FEET = EY + 26                      # 몹 발밑
OUT = os.path.join(S, "pfxframes", "hq_slash_g6" + ("" if DIR == "side" else "_" + DIR))
ROT = {"side": 0, "up": 90, "down": -90}[DIR]


def render(fx, opts, name):
    out = os.path.join(S, "pfxa", name)
    r = subprocess.run([NODE, "tools/render.mjs", fx, "--sheet", "--no-actor", "--bg", "dark", "--out", out, "--opts", json.dumps(opts)],
                       cwd=PFX, capture_output=True, text=True)
    if r.returncode:
        raise SystemExit(r.stderr[-800:])
    base = os.path.join(out, *fx.split("/"))
    nm = fx.split("/")[-1]
    j = json.load(open(os.path.join(base, nm + ".json"))); sh = Image.open(os.path.join(base, nm + ".png")).convert("RGBA")
    pv = j["meta"]["pixelfx"]["pivot"]
    frs = []
    for f in j["frames"]:
        rr = f["frame"]; frs.append(sh.crop((rr["x"], rr["y"], rr["x"] + rr["w"], rr["y"] + rr["h"])))
    return frs, pv


TEAL = ["#0a2626", "#124a48", "#1d7a72", "#2ab0a2", "#6ee6d6", "#e2c46a", "#fff4c8"]
ring, rpv = render("arcane/rune-circle", {"palette": TEAL, "seed": 8}, "ring")
stone, spv = render("impacts/blunt-hit", {"palette": ["#12171a", "#2a3335", "#475654", "#6c7f79", "#97a9a1", "#c6d5ca", "#ffffff"],
                                          "warm": "#ffe9a0", "dust": "#8fa8a0", "clod": "#4c5c58", "height": 22, "seed": 9}, "stone")
slash = [Image.open(os.path.join(S, "pfxframes", "hq_slash_g6_src", f"{i}.png")).convert("RGBA")
         for i in range(len(os.listdir(os.path.join(S, "pfxframes", "hq_slash_g6_src"))))]
slash = slash[2:]                   # 앞 빈 2장 없이 바로
if ROT:
    slash = [im.rotate(ROT, resample=Image.NEAREST) for im in slash]

# 금빛 고대 문자(룬 느낌 5x7)
GLYPHS = ["..#..|.###.|#.#.#|..#..|..#..|.#.#.|#...#", "#...#|##.##|#.#.#|#...#|#.#.#|##.##|#...#", "###..|#..#.|###..|#.#..|#..#.|#...#|#...#",
          ".###.|#...#|..#..|.###.|..#..|#...#|.###.", "#.#.#|.###.|..#..|..#..|.###.|#.#.#|..#.."]
GOLD = (255, 220, 110); GLOW = (120, 240, 220); GD = (150, 110, 40)


def glyph(L, gx, gy, gi, alpha_on):
    rows = GLYPHS[gi].split("|")
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == "#" and alpha_on(x, y):
                for (dx, dy, col) in ((1, 1, GD), (0, 0, GOLD)):
                    xx, yy = gx + x + dx, gy + y + dy
                    if 0 <= xx < C and 0 <= yy < C:
                        L[yy, xx] = (*col, 255)


IMPACT = 4                          # 검이 꽂히는 장


def big_sword(L, x, tip_y, glow, dissolve, f):
    # 고대 검 환영: 칼날 46px(끝 8px 뾰족), 금빛 코등이에 청록 보석, 칼날에 금빛 룬 홈이 맥박
    BL, BW = 46, 4
    OLC = (8, 46, 46); EDGE = (225, 255, 250); BODY = (84, 212, 196); MIDC = (40, 146, 138); RUNE = (255, 222, 120)
    def put(xx, yy, col):
        if 0 <= xx < C and 0 <= yy < C:
            if dissolve and ((xx * 7 + yy * 13 + f) % 10) < dissolve * 10:
                return
            L[yy, xx] = (*col, 255)
    for yy in range(BL):
        y = tip_y - yy
        w = BW if yy > 8 else max(0, int(BW * yy / 8))
        for dx in range(-w - 1, w + 2):
            ad = abs(dx)
            col = OLC if ad == w + 1 else EDGE if dx == -w else MIDC if ad == 0 else BODY
            if ad == 0 and yy > 12 and yy % 6 in (0, 1) and glow:
                col = RUNE                                  # 칼날 룬 홈
            put(x + dx, y, col)
    gy = tip_y - BL                                         # 코등이
    for yy in range(-1, 3):
        for dx in range(-10, 11):
            col = OLC if yy in (-1, 2) or abs(dx) == 10 else (255, 214, 100) if yy == 0 else (190, 140, 50)
            put(x + dx, gy - yy, col)
    for yy in range(-1, 2):
        for dx in range(-1, 2):
            put(x + dx, gy - 1 + yy, (110, 255, 235) if (dx, yy) != (0, 0) else (240, 255, 255))
    for yy in range(3, 12):                                 # 손잡이
        for dx in range(-2, 3):
            col = OLC if abs(dx) == 2 else (60, 110, 104) if (yy % 2) else (90, 160, 150)
            put(x + dx, gy - yy, col)
    for yy in range(12, 16):                                # 칼자루 끝 금구슬
        for dx in range(-2, 3):
            if dx * dx + (yy - 13.5) ** 2 <= 5:
                put(x + dx, gy - yy, (255, 226, 120) if dx < 1 else (190, 140, 50))


NF = 26
rng = np.random.default_rng(3)
runes = []                          # [x, y, glyph, born, life]
frames = []
for f in range(NF):
    can = Image.new("RGBA", (C, C))
    # 1) 룬 마법진: 2장째(맞는 순간) 작게 -> 6장째 다 펼침, 16장부터 깜빡이며 사라짐
    if f >= IMPACT:
        k = min(1, (f - IMPACT) / 4)
        sc = 0.35 + 0.65 * (1 - (1 - k) ** 3)
        fr = ring[(f * 2) % len(ring)]
        if sc < 0.999:
            fr = fr.resize((max(1, int(fr.width * sc)), max(1, int(fr.height * sc))), Image.NEAREST)
        a = np.array(fr)
        if f >= 17:
            fade = (f - 17) / 7
            keep = (np.add.outer(np.arange(a.shape[0]), np.arange(a.shape[1])) % 4) >= int(fade * 4.99)
            a[..., 3] = np.where(keep, a[..., 3], 0)
        fr = Image.fromarray(a)
        can.alpha_composite(fr, (int(EX - rpv[0] * sc), int(FEET - rpv[1] * sc)))
    # 2) 청록 베기
    if f < len(slash):
        can.alpha_composite(slash[f], (HC - 96, HC - 96))
    # 3) 돌 파편(둔기 타격) - 맞는 순간부터
    if IMPACT <= f < IMPACT + len(stone):
        fr = stone[f - IMPACT]
        can.alpha_composite(fr, (int(EX - spv[0]), int(FEET - spv[1])))
    # 4) 고대 검 환영: 0~3장 하늘에서 떨어져 4장째 꽂힘, 15장부터 아래부터 흩어짐
    L = np.array(can)
    if f <= 20:
        k = min(1, (f + 1) / (IMPACT + 1))
        drop = int((1 - k) ** 2 * 130)
        tip = int(FEET - 4) - drop
        if f < IMPACT:                                      # 떨어지는 잔상 줄
            for u in range(1, 4):
                for yy in range(tip - 60 - u * 14, tip - 50):
                    for dx in (-4 + u, 4 - u):
                        if 0 <= yy < C and (yy + u) % 3: L[yy, int(EX) + dx] = (110, 240, 225, 255)
        dis = max(0, (f - 15) / 6)
        big_sword(L, int(EX), tip, glow=(f >= IMPACT and (f % 4) < 3), dissolve=dis, f=f)
        if dis > 0:                                         # 흩어지는 청록 가루
            for _ in range(10):
                xx = int(EX + rng.integers(-6, 7)); yy = int(FEET - 4 - rng.integers(0, 60) - (f - 15) * 3)
                if 0 <= xx < C and 0 <= yy < C: L[yy, xx] = ((110, 240, 225) if rng.random() < .6 else (255, 222, 120)) + (255,)
    if IMPACT <= f <= IMPACT + 3:                           # 꽂히는 순간 바닥 충격 고리
        rr = 8 + (f - IMPACT) * 9
        for ang in np.linspace(0, 2 * math.pi, 160):
            xx = int(EX + math.cos(ang) * rr); yy = int(FEET + math.sin(ang) * rr * 0.32)
            if 0 <= xx < C and 0 <= yy < C: L[yy, xx] = ((255, 255, 240) if f == IMPACT else (150, 250, 235)) + (255,)
    # 5) 금빛 고대 문자 떠오름
    if IMPACT + 1 <= f <= 16 and f % 2 == 1:
        ang = rng.uniform(0, 2 * math.pi)
        runes.append([EX + math.cos(ang) * rng.uniform(10, 30) - 2, FEET + math.sin(ang) * rng.uniform(3, 9) - 10, int(rng.integers(len(GLYPHS))), f, int(rng.integers(8, 12))])
    for r in runes:
        age = f - r[3]
        if 0 <= age < r[4]:
            y = int(r[1] - age * 1.6)
            vis = age / r[4]
            glyph(L, int(r[0]), y, r[2], lambda x, yy: (vis < 0.65) or ((x + yy + f) % 3 != 0 and vis < 0.85) or ((x + yy + f) % 3 == 0))
            if age < 2:             # 처음 나올 때 청록 번쩍
                for yy in range(y - 1, y + 9):
                    for xx in range(int(r[0]) - 1, int(r[0]) + 7):
                        if 0 <= xx < C and 0 <= yy < C and L[yy, xx, 3] == 0 and (xx + yy) % 2 == 0:
                            L[yy, xx] = (*GLOW, 255)
    frames.append(Image.fromarray(L))
while len(frames) > 3 and np.array(frames[-1])[..., 3].sum() < 255 * 3:
    frames.pop()
os.makedirs(OUT, exist_ok=True)
for fn in os.listdir(OUT):
    os.remove(os.path.join(OUT, fn))
for i, fr in enumerate(frames):
    if np.array(fr)[..., 3].sum() == 0:
        fr.putpixel((0, 0), (0, 0, 0, 1))
    fr.save(os.path.join(OUT, f"{i}.png"))
print("frames", len(frames), "ring", len(ring), rpv, ring[0].size, "stone", len(stone), spv)
