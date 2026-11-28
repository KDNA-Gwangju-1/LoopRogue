# 유물 추가 피해 이펙트 7종 - pixelfx(MIT) 렌더를 게임 크기(1칸 = 32px)로 줄이고 합쳐 Resources/Effects/hq_relic_* (고퀄 그림체)에 덮어쓴다
# 그림 가운데 = Fx.Play 위치. 판 가로 = PlayerActor RelicFx 의 칸 수 x 32
# 렌더: node tools/render.mjs nature/earth-spike melee/thrust impacts/crit-hit melee/double-slash explosions/small-explosion
#        arcane/mana-burst impacts/ground-slam melee/dash-streak --sheet --no-actor --bg none --out <PFX>
# 사용: python Tools/SpriteGen/pfx_relic_fx.py <PFX 렌더 폴더> Assets/Resources/Effects
import os, re, sys, json, glob, uuid, numpy as np
from PIL import Image
PFX, OUT = sys.argv[1], sys.argv[2]
CELL = 32
LEVELS = np.array([0, 90, 170, 255])
WIND = [(40, 90, 100), (120, 210, 200), (210, 250, 240), (255, 255, 255)]   # 돌풍: 찌르기와 안 겹치게 옅은 민트

_cache = {}
def src(fid):
    if fid not in _cache:
        j = glob.glob(os.path.join(PFX, fid, "*.json"))[0]
        d = json.load(open(j)); sh = Image.open(j[:-5] + ".png").convert("RGBA")
        fr = d["frames"]; fr = fr if isinstance(fr, list) else list(fr.values())
        _cache[fid] = [np.array(sh.crop((f["frame"]["x"], f["frame"]["y"], f["frame"]["x"] + f["frame"]["w"], f["frame"]["y"] + f["frame"]["h"]))) for f in fr]
    return _cache[fid]

def pick(fid, a, b, n):
    s = src(fid); b = min(b, len(s) - 1)
    return [s[round(a + i * (b - a) / (n - 1))] for i in range(n)]

def bbox(frs):
    al = np.max([f[..., 3] for f in frs], axis=0) > 8
    ys, xs = np.where(al); return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1

def anchor_flash(frs):
    """첫 장(터지는 순간)의 밝은 곳 무게중심 = 효과가 시작되는 점"""
    for f in frs:
        a = f[..., 3].astype(np.float32)
        if a.sum() > 0:
            w = a * f[..., :3].mean(-1); ys, xs = np.indices(a.shape)
            return (xs * w).sum() / w.sum(), (ys * w).sum() / w.sum()

def anchor_base(frs):
    """첫 장 아래쪽 가운데 = 땅에서 솟는 것의 뿌리"""
    x0, y0, x1, y1 = bbox(frs[:1]); return (x0 + x1) / 2, y1 - 1

def scaled(f, s):
    a = f[..., 3:4].astype(np.float32) / 255.0
    pm = np.concatenate([f[..., :3] * a, f[..., 3:4]], -1).clip(0, 255).astype(np.uint8)
    w, h = max(1, round(f.shape[1] * s)), max(1, round(f.shape[0] * s))
    return np.array(Image.fromarray(pm, "RGBA").resize((w, h), Image.BOX)).astype(np.float32)

class Canvas:
    def __init__(self, w, h, n): self.w, self.h = w, h; self.f = [np.zeros((h, w, 4), np.float32) for _ in range(n)]
    def put(self, frs, anchor, s, at, delay=0):
        """frs 의 anchor 점이 판의 at 에 오도록 s 배로 줄여 delay 장 뒤부터 겹쳐 그림"""
        while len(self.f) < delay + len(frs): self.f.append(np.zeros((self.h, self.w, 4), np.float32))
        for i, f in enumerate(frs):
            sm = scaled(f, s); ox, oy = round(at[0] - anchor[0] * s), round(at[1] - anchor[1] * s)
            x0, y0 = max(0, ox), max(0, oy); x1, y1 = min(self.w, ox + sm.shape[1]), min(self.h, oy + sm.shape[0])
            if x1 <= x0 or y1 <= y0: continue
            part = sm[y0 - oy:y1 - oy, x0 - ox:x1 - ox]; dst = self.f[delay + i][y0:y1, x0:x1]
            dst[:] = part + dst * (1 - part[..., 3:4] / 255.0)
    def out(self, recolor=None):
        res = []
        for pm in self.f:
            al = pm[..., 3]; rgb = np.where(al[..., None] > 0, pm[..., :3] * 255.0 / np.maximum(al[..., None], 1), 0)
            if recolor:
                g = np.array(recolor, np.float32); t = (rgb.mean(-1) / 255.0).clip(0, 1) * (len(g) - 1)
                i0 = np.floor(t).astype(int).clip(0, len(g) - 2); fr = (t - i0)[..., None]; rgb = g[i0] * (1 - fr) + g[i0 + 1] * fr
            q = LEVELS[np.abs(al[..., None] - LEVELS[None, None, :]).argmin(-1)]; q = np.where(al < 40, 0, q)
            res.append(np.dstack([rgb.clip(0, 255), q]).astype(np.uint8))
        while len(res) > 3 and res[-1][..., 3].sum() < 255 * 2: res.pop()
        return res

def single(fid, a, b, n, W, anchor="flash", recolor=None, maxH=None):
    """한 효과를 anchor 점이 판 정가운데에 오게(가로 W, 세로는 위아래 대칭으로 필요한 만큼)"""
    frs = pick(fid, a, b, n); x0, y0, x1, y1 = bbox(frs)
    ax, ay = anchor_flash(frs) if anchor == "flash" else ((x0 + x1) / 2, (y0 + y1) / 2)
    s = (W / 2 - 1) / max(ax - x0, x1 - ax)
    H = 2 * int(np.ceil(max(ay - y0, y1 - ay) * s)) + 2
    if maxH: H = min(H, maxH)
    c = Canvas(W, H, n); c.put(frs, (ax, ay), s, (W / 2, H / 2)); return c.out(recolor)

FX = {}
# 11-09 사용자 "사이즈 좀 더 키워보자": 칸 위치(십자·8칸·뒤 칸)는 그대로, 효과 하나하나를 약 1.4배 + 판을 넓힘
# 판 가로(칸) = PlayerActor RelicFx 칸 수: 망치4 창날3 메아리2.25 뇌관4 파동6 쇠군화4 돌풍3
# 대지의 망치: 맞은 칸 상하좌우 4칸에서 바위 가시가 솟음(십자). 가운데 = 맞은 칸
sp = pick("nature/earth-spike", 12, 75, 9); ab = anchor_base(sp)
# 좌우로 튀는 흙·먼지는 지움(11-09 사용자): 첫 장(가시만 있는 장) 폭 안쪽 세로 띠만 남긴다
fx0, _, fx1, _ = bbox(sp[:1]); half = (fx1 - fx0) / 2 + 3
cols = np.abs(np.arange(sp[0].shape[1]) - ab[0]) <= half
sp = [np.where(cols[None, :, None], f, 0).astype(np.uint8) for f in sp]
x0, y0, x1, y1 = bbox(sp); s = 58 / (y1 - y0)
c = Canvas(128, 160, 9); C = (64, 80)
for dx, dy, d in ((0, -1, 0), (-1, 0, 1), (1, 0, 1), (0, 1, 0)):         # 위부터 그려 아래 가시가 앞에 오게
    c.put(sp, ab, s, (C[0] + dx * CELL, C[1] + dy * CELL + 10), d)
FX["relic_hammer"] = c.out()
# 관통의 창날: 판 가운데 = 대상과 뒤 몹 사이(대상 x32, 뒤 몹 x64). 찌르기가 뒤 칸 너머까지 뚫고 뒤 칸 가운데서 치명타 불꽃
th = pick("melee/thrust", 0, 18, 6); x0, y0, x1, y1 = bbox(th); s = 92 / (x1 - x0)
c = Canvas(96, 56, 6); c.put(th, ((x0 + x1) / 2, (y0 + y1) / 2), s, (48, 28))
ck = pick("impacts/crit-hit", 0, 18, 7); x0, y0, x1, y1 = bbox(ck); ca = anchor_flash(ck)
c.put(ck, ca, 56 / (x1 - x0), (64, 28), 1)
FX["relic_pierce"] = c.out()
# 메아리 방울: 대상 칸 가운데
FX["relic_echo"] = single("melee/double-slash", 2, 20, 8, 72, anchor="box")
# 연쇄 뇌관: 죽은 칸 가운데 큰 폭발 + 주변 8칸 작은 폭발(상하좌우 1장, 대각 2장 늦게)
ex = pick("explosions/small-explosion", 0, 40, 8); fire = pick("explosions/small-explosion", 0, 20, 5)
x0, y0, x1, y1 = bbox(fire); ea = anchor_flash(ex); w = x1 - x0
c = Canvas(128, 128, 8)
for dx in (-1, 0, 1):
    for dy in (-1, 0, 1):
        if dx or dy: c.put(ex, ea, 36 / w, (64 + dx * CELL, 64 + dy * CELL), 1 if dx == 0 or dy == 0 else 2)
c.put(ex, ea, 62 / w, (64, 64), 0)
FX["relic_chain"] = c.out()
# 파동의 핵: 용사 중심(5x5 범위보다 살짝 넘치게 6칸)
FX["relic_pulse"] = single("arcane/mana-burst", 3, 32, 9, 192, maxH=192)
# 폭군의 쇠군화: 내려선 칸 중심에서 충격파
FX["relic_stomp"] = single("impacts/ground-slam", 0, 36, 9, 128, maxH=160)
# 돌풍 부채: 밀린 방향 줄기
FX["relic_gust"] = single("melee/dash-streak", 2, 20, 8, 96, anchor="box", recolor=WIND)

# 폭탄(11-09 사용자 "3x3, 5x5 둘 다 맞게") -> hq_bomb_3 / hq_bomb_5. HitFeedback.OnExplosion 이 판 가로 = 칸 수 + 1 로 그림
# 가운데 큰 폭발(버섯구름은 위로 넘치게 판을 세로로 늘림) + 범위 칸마다 작은 불꽃(바깥 줄일수록 늦게)
big = pick("explosions/big-explosion", 0, 110, 10); bx0, _, bx1, _ = bbox(big[:4]); ba = anchor_flash(big)
sm = pick("explosions/small-explosion", 0, 24, 6); sx0, _, sx1, _ = bbox(sm); sa = anchor_flash(sm)
for span, centerW, smallW in ((3, 84, 26), (5, 124, 28)):
    W = (span + 1) * CELL; H = W + 64; C = (W / 2, H / 2); r = span // 2
    c = Canvas(W, H, 10)
    ring = sorted(((dx, dy) for dx in range(-r, r + 1) for dy in range(-r, r + 1) if dx or dy), key=lambda p: p[1])  # 위 칸(그림 y 작은 쪽)부터
    for dx, dy in ring:
        dist = max(abs(dx), abs(dy)); delay = dist + (1 if abs(dx) == abs(dy) else 0)
        c.put(sm, sa, smallW / (sx1 - sx0), (C[0] + dx * CELL, C[1] + dy * CELL), delay)
    c.put(big, ba, centerW / (bx1 - bx0), C, 0)
    FX[f"bomb_{span}"] = c.out()

# 그림자 망토(11-09) -> hq_shadow_split(붙을 때) / hq_shadow_slash(다시 벨 때). PlayerActor 가 고퀄이면 이걸 씀(색 덧칠 없음)
# 붙을 때(11-09 사용자 1번안): 용사 자리에서 그림자 덩어리가 날아가(hq_shadow_wisp, Fx.Projectile 반복 그림, 오른쪽 보기)
# 몹 자리에 닿으면 그림자 사람 형체가 나타남(hq_shadow_split). 촉수안은 "붙잡힘" 느낌이라 버림
SHADOW = [(40, 10, 70), (120, 50, 200), (200, 150, 255), (255, 240, 255)]
FX["shadow_wisp"] = single("projectiles/plasma-shot", 0, 21, 8, 64, anchor="box", recolor=SHADOW)
FX["shadow_split"] = single("arcane/blink-in", 4, 34, 9, 48, anchor="box", recolor=SHADOW)
# 다시 벨 때: 베기 호를 보라 그림자색으로(어두운 보라 -> 연보라 -> 흰빛)
FX["shadow_slash"] = single("melee/sword-slash", 2, 18, 8, 64, anchor="box", recolor=SHADOW)

TPL = open(glob.glob(os.path.join(OUT, "*", "0.png.meta"))[0], encoding="utf-8").read()
for name, outs in FX.items():
    d = os.path.join(OUT, "hq_" + name); os.makedirs(d, exist_ok=True)       # 고퀄 그림체용(클래식 relic_* 은 draw_relic_fx.py 손그림)
    for fn in os.listdir(d):
        if fn.endswith(".png"): os.remove(os.path.join(d, fn))
    for i, o in enumerate(outs):
        Image.fromarray(o, "RGBA").save(os.path.join(d, f"{i}.png"))
        m = os.path.join(d, f"{i}.png.meta")
        if not os.path.exists(m):
            open(m, "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, TPL, count=1))
    for fn in os.listdir(d):
        if fn.endswith(".png.meta") and not os.path.exists(os.path.join(d, fn[:-5])): os.remove(os.path.join(d, fn))
    print(name, len(outs), outs[0].shape[1], "x", outs[0].shape[0])
