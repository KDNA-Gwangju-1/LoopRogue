r"""11-28 UI 아이콘 고퀄: ui_batch.py 시트 -> 아이콘별(옛 크기 x2, 옛 그림의 자리·크기 비율 그대로), 배경색 빼기, 시트마다 32색.
사용: python ui_export.py [게임] [시트,...]   미리보기 _work/preview/ui, 비교 바탕화면 고퀄 스프라이트/UI_비교_<시트>.png
게임 = Assets/Resources/UIHQ/<같은 하위 경로>.png (PPU 200 = 화면 크기는 옛 그림과 같음, PixelUi.Get 이 고퀄일 때 먼저 찾음)"""
import os, re, sys, uuid, numpy as np
from PIL import Image, ImageDraw, ImageFont
import ui_batch as UB, fx_batch as FB, fx_export as FE

Z = 2; NCOL = 32
PREV = os.path.join(os.path.dirname(UB.OUT), "_work", "preview", "ui")
GAME = os.path.join(os.path.dirname(UB.UI), "UIHQ")
CMP = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트\UI"
FONT = ImageFont.truetype(r"C:\Windows\Fonts\malgun.ttf", 13)


def icon(rel, rgb, bg, old):
    fg, al = FE.key(rgb, bg); fg = FE.despill(fg, al, bg, [old])
    a = np.dstack([fg, np.where(al > 128, 255, 0)]).astype(np.uint8)
    ys, xs = np.nonzero(a[..., 3])
    if not len(ys): return None
    a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    ow, oh = old.size; bx0, by0, bx1, by1 = old.getbbox()
    tw, th = (bx1 - bx0) * Z, (by1 - by0) * Z; s = min(tw / a.shape[1], th / a.shape[0])
    im = Image.fromarray(a).resize((max(1, round(a.shape[1] * s)), max(1, round(a.shape[0] * s))), Image.BOX)
    can = Image.new("RGBA", (ow * Z, oh * Z)); cx, cy = (bx0 + bx1) / 2 * Z, (by0 + by1) / 2 * Z
    can.alpha_composite(im, (int(round(cx - im.width / 2)), int(round(cy - im.height / 2))))
    arr = np.asarray(can).copy(); arr[..., 3] = np.where(arr[..., 3] > 110, 255, 0); return Image.fromarray(arr)


def sheet(name, items):
    p = os.path.join(UB.OUT, name + ".png")
    if not os.path.exists(p): return {}
    olds = [UB.load(i) for i in items]; bg = FB.bg_color(olds)
    rgb = np.asarray(Image.open(p).convert("RGB")).astype(float); H, W = rgb.shape[:2]; res = {}
    for k, rel in enumerate(items):
        x0, x1 = int(W * (k % 4) / 4), int(W * (k % 4 + 1) / 4); y0, y1 = int(H * (k // 4) / 4), int(H * (k // 4 + 1) / 4)
        im = icon(rel, rgb[y0:y1, x0:x1], bg, olds[k])
        if im is not None: res[rel] = im
    if not res: return res
    big = Image.new("RGB", (sum(i.width for i in res.values()), max(i.height for i in res.values()))); x = 0
    for i in res.values(): big.paste(i.convert("RGB"), (x, 0), i); x += i.width
    pal = big.quantize(NCOL, method=Image.Quantize.MAXCOVERAGE, dither=Image.Dither.NONE)
    for rel, im in res.items():
        q = np.asarray(im.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB"))
        res[rel] = Image.fromarray(np.dstack([q, np.asarray(im)[..., 3]]).astype(np.uint8), "RGBA")
    return res


def compare(name, res, path):
    S = 120; o = Image.new("RGBA", (4 * S * 2, ((len(res) + 3) // 4) * (S + 18)), (40, 40, 52, 255)); d = ImageDraw.Draw(o)
    for k, (rel, im) in enumerate(res.items()):
        x, y = (k % 4) * S * 2, (k // 4) * (S + 18); d.text((x + 4, y + 1), rel.split("/")[-1], fill="white", font=FONT)
        old = UB.load(rel); z = (S - 10) // max(old.size)          # 같은 화면 크기로(새 그림은 Z배라 z/Z배)
        for j, t in enumerate((old, im)):
            w, h = old.width * z, old.height * z
            o.alpha_composite(t.resize((w, h), Image.NEAREST if j == 0 or z % Z == 0 else Image.BOX), (x + j * S + 4, y + 18))
    o.save(path); return path


def install(rel, im):
    dst = os.path.join(GAME, rel + ".png"); os.makedirs(os.path.dirname(dst), exist_ok=True); im.save(dst)
    m = open(os.path.join(UB.UI, rel + ".png.meta"), encoding="utf-8").read()
    m = re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, m, 1).replace("spritePixelsToUnits: 100", "spritePixelsToUnits: 200")
    open(dst + ".meta", "w", encoding="utf-8", newline="\n").write(m)
    # 폴더 meta
    d = os.path.dirname(dst)
    while os.path.normpath(d) != os.path.normpath(os.path.dirname(GAME)):
        if not os.path.exists(d + ".meta"):
            fm = open(os.path.join(os.path.dirname(UB.UI), "UI.meta"), encoding="utf-8").read()
            open(d + ".meta", "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, fm, 1))
        d = os.path.dirname(d)


if __name__ == "__main__":
    args = sys.argv[1:]; game = "게임" in args; args = [a for a in args if a != "게임"]
    sh = UB.sheets(); sel = args[0].split(",") if args else list(sh)
    os.makedirs(CMP, exist_ok=True); n = 0
    for name in sel:
        res = sheet(name, sh[name])
        if not res: continue
        for rel, im in res.items():
            p = os.path.join(PREV, rel + ".png"); os.makedirs(os.path.dirname(p), exist_ok=True); im.save(p)
            if game: install(rel, im)
        print(compare(name, res, os.path.join(CMP, f"UI_비교_{name}.png"))); n += len(res)
    print(n, "장", "(게임 폴더에도)" if game else "")
