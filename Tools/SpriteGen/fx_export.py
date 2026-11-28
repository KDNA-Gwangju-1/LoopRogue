r"""11-28 이펙트 고퀄: fx_batch.py 시트(자동\이펙트\<이름>.png) -> 프레임별 도트(옛 크기 x 1.625 = 용사와 같은 도트 크기), 배경색 빼기.
사용: python fx_export.py [게임] [이름,...]   미리보기 _work/preview/fx/<이름>/<i>.png, 비교 바탕화면 고퀄 스프라이트/이펙트/<묶음>.png
게임 = Assets/Resources/EffectsHQ/<이름>/<i>.png (PPU 도 1.625배 -> 세상 크기는 옛 그림과 같음)"""
import os, re, sys, uuid, numpy as np
from PIL import Image, ImageDraw
import fx_batch as FB

K = 52 / 32; NCOL = 32
PREV = os.path.join(os.path.dirname(FB.OUT), "_work", "preview", "fx")
GAME = os.path.join(os.path.dirname(FB.FX), "EffectsHQ")
CMP = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트\이펙트"
ALV = np.array([0, 110, 190, 255.])
from PIL import ImageFont
FONT = ImageFont.truetype(r"C:\Windows\Fonts\malgun.ttf", 15)


def key(rgb, bg):
    """배경색 빼기: 거리 60 안 = 투명, 60~150 = 반투명(배경색 빛 번짐도 걷어냄)."""
    bg = np.array(bg, float); d = np.sqrt(((rgb - bg) ** 2).sum(-1))
    al = np.clip((d - 60) / 90, 0, 1)
    # 반투명 칸의 배경색 섞임 걷어내기: c = al*fg + (1-al)*bg -> fg
    fg = np.where(al[..., None] > 0.05, (rgb - (1 - al[..., None]) * bg) / np.maximum(al[..., None], 0.05), rgb)
    return np.clip(fg, 0, 255), al * 255


def despill(fg, al, bg, old):
    """배경색 번짐 걷기: 옛 이펙트에 그 색(초록/자홍)이 거의 없으면 그 색 쪽으로 치우친 채널을 눌러 줌."""
    a = np.concatenate([np.asarray(f).reshape(-1, 4) for f in old]).astype(int); a = a[a[:, 3] > 60]
    if tuple(bg) == FB.GREEN:
        fr = ((a[:, 1] > a[:, 0] + 30) & (a[:, 1] > a[:, 2] + 30)).mean() if len(a) else 0
        if fr < 0.02: fg[..., 1] = np.minimum(fg[..., 1], np.maximum(fg[..., 0], fg[..., 2]))
    else:
        fr = ((np.minimum(a[:, 0], a[:, 2]) > a[:, 1] + 40)).mean() if len(a) else 0
        if fr < 0.02:
            m = np.clip(np.minimum(fg[..., 0], fg[..., 2]) - fg[..., 1], 0, None)
            fg[..., 0] -= m * 0.85; fg[..., 2] -= m * 0.85
    return fg


def frames(n):
    old = FB.frames(n)
    p = os.path.join(FB.OUT, n + ".png")
    if not old or not os.path.exists(p): return None
    w, h = old[0].size; c, r = FB.layout(len(old), w, h); bg = FB.bg_color(old)
    rgb = np.asarray(Image.open(p).convert("RGB")).astype(float)
    H, W = rgb.shape[:2]; TW, TH = round(w * K), round(h * K); out = []
    for i in range(len(old)):
        x0, x1 = int(W * (i % c) / c), int(W * (i % c + 1) / c); y0, y1 = int(H * (i // c) / r), int(H * (i // c + 1) / r)
        fg, al = key(rgb[y0:y1, x0:x1], bg); fg = despill(fg, al, bg, old)
        im = Image.fromarray(np.dstack([fg, al]).astype(np.uint8), "RGBA").resize((TW, TH), Image.BOX)
        if n.startswith("tel"):            # 예고 칸: 옛 그림의 반투명 칸 바탕(GPT는 반투명을 못 그림)을 밑에 깔기
            o = np.asarray(old[i]).copy(); o[o[..., 3] >= 200] = 0
            under = Image.fromarray(o, "RGBA").resize((TW, TH), Image.NEAREST); under.alpha_composite(im); im = under
        out.append(im)
    # 한 이펙트 전체를 한 팔레트로(프레임마다 색이 튀지 않게), 알파는 4단
    big = Image.new("RGB", (TW * len(out), TH))
    for i, f in enumerate(out): big.paste(f.convert("RGB"), (i * TW, 0))
    pal = big.quantize(NCOL, method=Image.Quantize.MAXCOVERAGE, dither=Image.Dither.NONE)
    res = []
    for f in out:
        a = np.asarray(f).astype(float)
        q = np.asarray(f.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB"))
        al = ALV[np.abs(a[..., 3:4] - ALV[None, None]).argmin(-1)]
        res.append(Image.fromarray(np.dstack([q, al]).astype(np.uint8), "RGBA"))
    return res


def compare(items, path):
    rows = []
    for n, new in items:
        old = FB.frames(n); w, h = new[0].size; k = max(1, 120 // max(w, h)); cw, ch = w * k, h * k
        row = Image.new("RGBA", (180 + len(new) * (cw + 4), 2 * ch + 6), (40, 40, 52, 255)); d = ImageDraw.Draw(row)
        d.text((4, 4), n, fill="white", font=FONT); d.text((4, 24), "위 = 지금 / 아래 = 새", fill=(170, 170, 190), font=FONT)
        for i, f in enumerate(new):
            row.alpha_composite(old[i].resize((cw, ch), Image.NEAREST), (180 + i * (cw + 4), 0))
            row.alpha_composite(f.resize((cw, ch), Image.NEAREST), (180 + i * (cw + 4), ch + 6))
        rows.append(row)
    W = max(r.width for r in rows); o = Image.new("RGBA", (W, sum(r.height + 8 for r in rows)), (28, 28, 36, 255)); y = 0
    for r in rows: o.alpha_composite(r, (0, y)); y += r.height + 8
    o.save(path); return path


def install(n, fs):
    dst = os.path.join(GAME, n); os.makedirs(dst, exist_ok=True)
    if not os.path.exists(GAME + ".meta"):
        fm = open(os.path.join(os.path.dirname(FB.FX), "Effects.meta"), encoding="utf-8").read()
        open(GAME + ".meta", "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, fm, 1))
    if not os.path.exists(dst + ".meta"):
        dm = open(os.path.join(FB.FX, n + ".meta"), encoding="utf-8").read()
        open(dst + ".meta", "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, dm, 1))
    for i, f in enumerate(fs):
        f.save(os.path.join(dst, f"{i}.png"))
        m = open(os.path.join(FB.FX, n, f"{i}.png.meta"), encoding="utf-8").read()
        ppu = float(re.search(r"spritePixelsToUnits: ([\d.]+)", m).group(1))
        m = re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, m, 1)
        m = re.sub(r"spritePixelsToUnits: [\d.]+", f"spritePixelsToUnits: {ppu * K:g}", m)
        m = re.sub(r"spritePivot: \{x: ([\d.]+), y: ([\d.]+)\}", lambda mm: mm.group(0), m)
        open(os.path.join(dst, f"{i}.png.meta"), "w", encoding="utf-8", newline="\n").write(m)


if __name__ == "__main__":
    args = sys.argv[1:]; game = "게임" in args; args = [a for a in args if a != "게임"]
    sel = args[0].split(",") if args else FB.names()
    done = []
    for n in sel:
        fs = frames(n)
        if not fs: continue
        d = os.path.join(PREV, n); os.makedirs(d, exist_ok=True)
        for i, f in enumerate(fs): f.save(os.path.join(d, f"{i}.png"))
        if game: install(n, fs)
        done.append((n, fs))
    os.makedirs(CMP, exist_ok=True)
    for k in range(0, len(done), 14):
        print(compare(done[k:k + 14], os.path.join(CMP, f"이펙트_비교_{k // 14 + 1:02d}.png")))
    print(len(done), "종", "(게임 폴더에도)" if game else "")
