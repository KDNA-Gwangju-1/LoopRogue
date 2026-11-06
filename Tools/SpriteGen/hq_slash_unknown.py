# Unknown 베기 꾸미기: pixelfx sword-slash를 청록/분홍 두 번 뽑아 어긋나게 겹침(색 갈라짐 글리치)
# + 맞는 순간 칼끝에 기존 Unknown 마름모 무늬가 퍼지고, 가로 줄 밀림·네모 조각·십자 반짝이
import os, json, subprocess, shutil, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
PFX = r"C:\Users\dignw\pixel-tools\pixelfx"; NODE = r"C:\Users\dignw\pixel-tools\node\node.exe"
C = 192; LEAD = 2
CY = [(10, 60, 70), (20, 110, 120), (40, 190, 190), (90, 240, 225), (160, 255, 240), (220, 255, 250), (255, 255, 255)]
MG = [(60, 10, 50), (120, 20, 100), (200, 40, 160), (255, 70, 200), (255, 140, 225), (255, 210, 240), (255, 255, 255)]
hx = lambda c: "#%02x%02x%02x" % c
def render(name, pal, sparks, seed):
    out = os.path.join(S, "pfxu", name)
    subprocess.run([NODE, "tools/render.mjs", "melee/sword-slash", "--sheet", "--no-actor", "--bg", "dark", "--out", out,
                    "--opts", json.dumps({"palette": [hx(c) for c in pal], "sparks": sparks, "seed": seed})], cwd=PFX, check=True, capture_output=True)
    base = os.path.join(out, "melee", "sword-slash")
    j = json.load(open(os.path.join(base, "sword-slash.json"))); sh = Image.open(os.path.join(base, "sword-slash.png")).convert("RGBA")
    px, py = j["meta"]["pixelfx"]["pivot"]; frs = []
    for f in j["frames"]:
        r = f["frame"]; im = sh.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))
        can = Image.new("RGBA", (C, C)); can.paste(im, (C // 2 - px, C // 2 - (py - 26))); frs.append(np.array(can).astype(float))
    return frs
cy = render("cyan", CY, ["#ffffff", "#dcfffa", "#6ef0e2", "#ff3cc8"], 2)
mg = render("mag", MG, ["#ffffff", "#ffd2f0", "#ff3cc8", "#6ef0e2"], 7)
n = max(i for i, f in enumerate(cy) if f[..., 3].sum() > 255 * 3) + 1
rng = np.random.default_rng(9)
TIP = (132, 106)  # 칼끝(pixelfx 호 끝점 a1=0.95)
WHITE = np.array([255, 255, 255.]); CYC = np.array([110, 245, 230.]); MGC = np.array([255, 60, 200.])
def shift(a, dx, dy):
    o = np.zeros_like(a); H, W = a.shape[:2]
    o[max(0, dy):H + min(0, dy), max(0, dx):W + min(0, dx)] = a[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)]
    return o
def put(img, x, y, col, a=255):
    if 0 <= x < C and 0 <= y < C: img[y, x, :3] = col; img[y, x, 3] = a
def diamond(img, cx, cy_, r, col, gap=0):
    for k in range(-r, r + 1):
        dy = r - abs(k)
        if gap and (k + r) % gap == 0: continue
        put(img, cx + k, cy_ - dy, col); put(img, cx + k, cy_ + dy, col)
def cross(img, x, y, col, s=2):
    for k in range(-s, s + 1): put(img, x + k, y, col); put(img, x, y + k, col)
    put(img, x, y, WHITE)
# 우주 성운 노이즈(고정 시드) + 팔레트 + 별 자리
def _noise(seed, cells):
    r = np.random.default_rng(seed).random((cells, cells))
    a = cv2.resize(r, (C, C), interpolation=cv2.INTER_CUBIC)
    r2 = np.random.default_rng(seed + 50).random((cells * 2, cells * 2))
    a = a * 0.7 + cv2.resize(r2, (C, C), interpolation=cv2.INTER_CUBIC) * 0.3
    return (a - a.min()) / (a.max() - a.min())
import cv2
NEB1, NEB2 = _noise(11, 10), _noise(23, 9)
G_ = np.array([40, 220, 140.]); PK = np.array([255, 70, 200.])
SPACE = np.array([[8, 6, 20], [22, 14, 44], [48, 20, 72], [16, 70, 60], [34, 150, 105], [60, 225, 150], [120, 26, 100], [210, 50, 170], [255, 120, 220], [235, 245, 255]], float)
STARS = [(int(a), int(b), int(c_)) for a, b, c_ in zip(np.random.default_rng(4).integers(-24, 25, 40), np.random.default_rng(5).integers(-24, 25, 40), np.random.default_rng(6).integers(0, 6, 40))]
out = []
for i in range(n):
    t = i / max(1, n - 1)
    d = 2 if i < 3 else (3 if i < 6 else 1)
    m = shift(mg[i], -d, 1 if i % 2 else 0); c = shift(cy[i], d, 0)
    space = np.zeros((C, C), bool)
    # 알파 제대로 겹치기(분홍 위에 청록), 둘 다 진한 칸만 밝게
    am, ac = m[..., 3:] / 255, c[..., 3:] / 255
    ao = ac + am * (1 - ac)
    rgb = (c[..., :3] * ac + m[..., :3] * am * (1 - ac)) / np.maximum(ao, 1e-6)
    img = np.dstack([rgb, ao[..., 0] * 255])
    both = (m[..., 3] > 160) & (c[..., 3] > 160)
    img[both, :3] = np.minimum(255, (m[both, :3] + c[both, :3]) * 0.7)
    # 번짐(11-06 사용자 "검은 번짐 느낌 좋은데 은색으로"): 두 겹의 옅은 빛이 겹친 자리를 꽉 찬 덩어리로 -> 아래에서 은색으로 칠함
    glow = (m[..., 3] > 0) & (c[..., 3] > 0) & ~both
    # 덩어리 조금 작게(11-06 사용자) - 바깥 테두리를 7칸 깎음(모양은 그대로)
    import cv2
    blob = glow | both
    core = cv2.erode(blob.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))).astype(bool)
    glow = glow & core   # 깎인 바깥 칸은 원래(옅은 빛) 그대로 둠
    # 번짐 모양을 원 -> 마름모(11-06 사용자): 같은 넓이의 마름모를 번짐 가운데에, 가운데 밝고 바깥 진한 은색 띠
    if core.any():
        yy_, xx_ = np.nonzero(core); mx, my = xx_.mean(), yy_.mean()
        rr = np.sqrt(core.sum() / 2)
        Y_, X_ = np.mgrid[0:C, 0:C]
        dd = (np.abs(X_ - mx) + np.abs(Y_ - my)) / rr
        dia = (dd <= 1) & ~((m[..., 3] > 160) | (c[..., 3] > 160))  # 베기 선(진한 칸)은 마름모 위로
        tt = np.clip(1 - dd[dia], 0, 1) ** 0.7
        col = np.array([118, 126, 146.]) + tt[:, None] * np.array([130, 126, 106.])
        img[dia, :3] = np.round(col / 24) * 24; img[dia, 3] = 255
        # 안쪽 = 우주(11-06 사용자 "검은색 녹색 분홍색 우주 느낌"): 검은 바탕 + 녹색·분홍 성운(천천히 흐름) + 별, 바깥 은색 띠는 테두리로 남김
        inn = (dd <= 0.88) & dia   # 우주 비율 크게, 은색은 얇은 테두리(11-06 사용자)
        space = inn
        oy, ox = int(my) - 96, int(mx) - 96
        n1 = np.roll(NEB1, (-oy + i, -ox + i), (0, 1)); n2 = np.roll(NEB2, (-oy - i, -ox + 2 * i), (0, 1))
        # 녹색은 왼아래, 분홍은 오른위 쪽으로 흐르는 성운 띠 + 노이즈로 구름 모양, 가운데 대각선은 검은 우주 틈
        uu = ((X_ - mx) - (Y_ - my)) / rr; vv = ((X_ - mx) + (Y_ - my)) / rr
        wav = 0.25 * np.sin(vv * 3.0 + i * 0.35)
        g = np.clip((-(uu - wav) - 0.08) * 2.2 + (n1 - 0.5) * 1.6, 0, 1)
        pk = np.clip(((uu - wav) - 0.08) * 2.2 + (n2 - 0.5) * 1.6, 0, 1)
        sp = np.array([8, 6, 20.]) * (1 - np.maximum(g, pk))[..., None] + G_[None, None] * g[..., None] + PK[None, None] * pk[..., None]
        sp = np.where((g > .55)[..., None] & (pk > .45)[..., None], np.array([235, 245, 255.]), sp)  # 겹친 곳은 빛나는 핵
        # 팔레트로 끊어 도트 느낌
        q = sp[inn]; dists = ((q[:, None, :] - SPACE[None]) ** 2).sum(-1); img[inn, :3] = SPACE[dists.argmin(1)]
        # 별: 고정 자리, 장마다 깜빡
        for (sy_, sx_, ph) in STARS:
            x_, y_ = int(mx) + sx_, int(my) + sy_
            if 0 <= x_ < C and 0 <= y_ < C and inn[y_, x_] and (i + ph) % 3 != 0:
                img[y_, x_, :3] = [255, 255, 255] if ph % 2 else [180, 255, 230]
                if (i + ph) % 4 == 0:
                    for ax, ay in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        if inn[min(C - 1, y_ + ay), min(C - 1, x_ + ax)]: img[y_ + ay, x_ + ax, :3] = [255, 140, 220]
        # 마름모 밖으로 남은 번짐 칸은 원래 옅은 빛으로
        out_ = glow & ~dia
        img[out_, :3] = rgb[out_]; img[out_, 3] = ao[out_, 0] * 255
    # 가로 줄 밀림(글리치) - 맞은 직후 몇 장만
    if 2 <= i <= 7:
        for _ in range(3):
            y0 = rng.integers(70, 130); hgt = rng.integers(1, 4); dx = int(rng.choice([-6, -4, 4, 6, 8]))
            img[y0:y0 + hgt] = np.roll(img[y0:y0 + hgt], dx, axis=1)
    # 칼끝 마름모: 맞는 순간(2장째)부터 퍼지며 끊어짐
    if 2 <= i <= 9:
        k = i - 2
        diamond(img, *TIP, 4 + k * 3, CYC if k % 2 == 0 else MGC, gap=0 if k < 3 else 2 + (k - 3))
        if k < 5: diamond(img, TIP[0], TIP[1], max(1, 2 + k * 2 - 2), WHITE if k < 2 else MGC, gap=0 if k < 2 else 3)
        if k < 2:
            for y in range(TIP[1] - 1, TIP[1] + 2):
                for x in range(TIP[0] - 1, TIP[0] + 2): put(img, x, y, WHITE)
    # 네모 조각(2x2) + 십자 반짝이 - 호 둘레에서 깜빡
    if 1 <= i <= n - 2:
        alive = np.argwhere(img[..., 3] > 0)
        if len(alive):
            for _ in range(int(10 * (1 - t)) + 3):
                yy, xx = alive[rng.integers(len(alive))]
                xx += rng.integers(-10, 11); yy += rng.integers(-8, 9)
                col = [CYC, MGC, WHITE][rng.integers(3)]
                for oy in range(2):
                    for ox in range(2): put(img, xx + ox, yy + oy, col)
            for _ in range(2 if t < .6 else 1):
                yy, xx = alive[rng.integers(len(alive))]
                cross(img, xx + rng.integers(-6, 7), yy + rng.integers(-6, 7), [CYC, MGC][rng.integers(2)], 2 if t < .5 else 1)
    # 어두운 칸(번짐·꼬리 점·안쪽 그림자) -> 밝은 은색(어두울수록 살짝 진한 은색)
    lum = img[..., :3] @ [0.299, 0.587, 0.114]
    dk = (img[..., 3] > 0) & (lum < 120) & ~space
    tt = np.clip(lum[dk] / 70, 0, 1) ** 0.8   # 번짐 바깥(어두움) -> 진한 은색, 가운데(밝음) -> 거의 흰 은색
    sil = np.array([118, 126, 146.]) + tt[:, None] * np.array([130, 126, 106.])
    # 3단계로 끊어 도트 느낌(금속 띠)
    sil = np.round(sil / 24) * 24
    img[dk, :3] = sil
    out.append(Image.fromarray(img.clip(0, 255).astype(np.uint8), "RGBA"))
dst = os.path.join(S, "pfxframes", "hq_slash_g9")
bak = os.path.join(S, "pfxframes", "hq_slash_g9_old")
if not os.path.exists(bak): shutil.copytree(dst, bak)
for f in os.listdir(dst): os.remove(os.path.join(dst, f))
blank = Image.new("RGBA", (C, C)); blank.putpixel((0, 0), (0, 0, 0, 1))
for i, f in enumerate([blank] * LEAD + out): f.save(os.path.join(dst, f"{i}.png"))
print("장 수", LEAD + len(out))
