r"""11-28 소품 고퀄: props_batch.py 시트(자동\소품\<시트>.png) -> 칸별 52칸 도트(옛 32칸 소품과 같은 자리·크기 비율).
사용: python props_export.py [게임]   (게임 = Assets/Resources/Sprites/PropsHQ 에 PPU 52 로, 없으면 미리보기 폴더만)"""
import os, sys, re, uuid, shutil, numpy as np
from PIL import Image, ImageDraw
from props_batch import SHEETS, OUT as SRC, PROPS
import hq_export as H

TS = 52; K = TS / 32; NCOL = 32
PREV = os.path.join(os.path.dirname(SRC), "_work", "preview", "props")
GAME = os.path.join(os.path.dirname(PROPS), "PropsHQ")


def autocrop(rgb):
    ink = rgb.min(-1) < 235
    ys, xs = np.nonzero(ink)
    return rgb[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def cells(sheet):
    names, (c, r), kind = SHEETS[sheet]
    p = os.path.join(SRC, sheet + ".png")
    if not os.path.exists(p): return {}
    rgb = np.asarray(Image.open(p).convert("RGB")).astype(float)
    if kind == "full": rgb = autocrop(rgb)
    h, w = rgb.shape[:2]; out = {}
    for k, n in enumerate(names):
        x0, y0 = int(w * (k % c) / c), int(h * (k // c) / r); x1, y1 = int(w * (k % c + 1) / c), int(h * (k // c + 1) / r)
        out[n] = rgb[y0:y1, x0:x1]
    return out


def obj_sprite(name, rgb):
    """흰 바탕 지우고, 옛 소품 그림의 자리·크기(32칸 기준)를 52칸으로 늘린 자리에 맞춰 넣음."""
    bg = H.background(rgb)
    a = np.dstack([rgb, np.where(bg, 0, 255)]).astype(np.uint8)
    ys, xs = np.nonzero(a[..., 3]); a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    old = Image.open(os.path.join(PROPS, name + ".png")).convert("RGBA")
    ow, oh = old.size; bx0, by0, bx1, by1 = old.getbbox()
    tw, th = (bx1 - bx0) * K, (by1 - by0) * K
    s = min(tw / a.shape[1], th / a.shape[0])
    im = Image.fromarray(a).resize((max(1, round(a.shape[1] * s)), max(1, round(a.shape[0] * s))), Image.BOX)
    W, Hh = round(ow * K), round(oh * K); can = Image.new("RGBA", (W, Hh))
    cx, cy = (bx0 + bx1) / 2 * K, (by0 + by1) / 2 * K
    can.alpha_composite(im, (int(round(cx - im.width / 2)), int(round(cy - im.height / 2))))
    al = np.asarray(can)[..., 3]; arr = np.asarray(can).copy(); arr[..., 3] = np.where(al > 110, 255, 0); return Image.fromarray(arr)


def block_cut(rgb):
    """밀 수 있는 돌덩이: 진한 테두리의 바깥 상자 밖(둘레 바닥)을 지우고, 둥근 모서리 바깥 밝은 점도 지움."""
    import cv2
    if rgb[:6, :6].mean() > 235 and rgb[-6:, -6:].mean() > 235:   # 흰 바탕에 그린 돌덩이(2차~)
        a = np.dstack([rgb, np.where(H.background(rgb), 0, 255)]).astype(np.uint8)
        im = Image.fromarray(a).resize((TS, TS), Image.BOX); arr = np.asarray(im).copy(); arr[..., 3] = np.where(arr[..., 3] > 128, 255, 0)
        return Image.fromarray(arr)
    lum = rgb.mean(-1); H_, W_ = lum.shape
    dark = (lum < 80).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(dark, connectivity=8)
    ar = st[1:, cv2.CC_STAT_AREA]; ids = 1 + np.nonzero(ar >= ar.max() * 0.08)[0]
    x0, y0 = st[ids, 0].min(), st[ids, 1].min(); x1, y1 = (st[ids, 0] + st[ids, 2]).max(), (st[ids, 1] + st[ids, 3]).max()
    x, y, w, h = x0, y0, x1 - x0, y1 - y0
    keep = np.zeros_like(dark, bool); keep[y:y + h, x:x + w] = True
    c = max(3, min(w, h) // 7)                              # 모서리 창 안에서 가장자리와 이어진 밝은 곳 = 바닥
    for (cx, cy) in ((x, y), (x + w - c, y), (x, y + h - c), (x + w - c, y + h - c)):
        win = (lum[cy:cy + c, cx:cx + c] > 110).astype(np.uint8)
        m, l2 = cv2.connectedComponents(win, connectivity=4)
        corner = l2[0 if cy == y else -1, 0 if cx == x else -1]
        if corner: keep[cy:cy + c, cx:cx + c][l2 == corner] = False
    a = np.dstack([rgb, np.where(keep, 255, 0)]).astype(np.uint8)
    im = Image.fromarray(a).resize((TS, TS), Image.BOX); arr = np.asarray(im).copy(); arr[..., 3] = np.where(arr[..., 3] > 128, 255, 0)
    return Image.fromarray(arr)


def quant(ims):
    if not ims: return {}
    big = Image.new("RGB", (TS * len(ims), max(i.height for i in ims.values())))
    for j, im in enumerate(ims.values()): big.paste(im.convert("RGB"), (j * TS, 0), im)
    pal = big.quantize(NCOL, method=Image.Quantize.MAXCOVERAGE, dither=Image.Dither.NONE)
    out = {}
    for n, im in ims.items():
        q = np.asarray(im.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB"))
        out[n] = Image.fromarray(np.dstack([q, np.asarray(im)[..., 3]]).astype(np.uint8))
    return out


def export():
    res = {}
    for sh, (names, _, kind) in SHEETS.items():
        cs = cells(sh); ims = {}
        if sh in ("blocks", "runes"):   # 색이 곱해지는 퍼즐 돌: 바닥보다 한 톤 밝게(흰 바탕은 그대로)
            cs = {n: np.where(rgb.min(-1, keepdims=True) > 240, rgb, np.clip(rgb * 1.3 + 8, 0, 255)) for n, rgb in cs.items()}
        for n, rgb in cs.items():
            ims[n] = obj_sprite(n, rgb) if kind == "obj" else block_cut(rgb) if sh == "blocks" and n.startswith("Puzzle_Block")                 else Image.fromarray(rgb.astype(np.uint8)).resize((TS, TS), Image.BOX).convert("RGBA")
        res.update(quant(ims))
    os.makedirs(PREV, exist_ok=True)
    for n, im in res.items(): im.save(os.path.join(PREV, n + ".png"))
    return res


def compare(res, path):
    ns = list(res); cols = 8; S = 160
    o = Image.new("RGBA", (cols * S * 2 // 1, ((len(ns) + cols - 1) // cols) * (S + 18)), (40, 42, 54, 255)); d = ImageDraw.Draw(o)
    for i, n in enumerate(ns):
        x, y = (i % cols) * S * 2, (i // cols) * (S + 18)
        d.text((x + 4, y + 2), n, fill="white")
        for j, im in enumerate((Image.open(os.path.join(PROPS, n + ".png")).convert("RGBA"), res[n])):
            k = (S - 8) // max(im.size); t = im.resize((im.width * k, im.height * k), Image.NEAREST)
            bgc = Image.new("RGBA", t.size, (70, 66, 86, 255)); bgc.alpha_composite(t)
            o.alpha_composite(bgc, (x + j * S + 4, y + 16))
    o.save(path); return path


def install(res):
    os.makedirs(GAME, exist_ok=True)
    for n, im in res.items():
        im.save(os.path.join(GAME, n + ".png"))
        m = open(os.path.join(PROPS, n + ".png.meta"), encoding="utf-8").read()
        m = re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, m, 1).replace("spritePixelsToUnits: 32", "spritePixelsToUnits: 52")
        open(os.path.join(GAME, n + ".png.meta"), "w", encoding="utf-8", newline="\n").write(m)
    if not os.path.exists(GAME + ".meta"):
        fm = open(os.path.join(os.path.dirname(PROPS), "Props.meta"), encoding="utf-8").read()
        open(GAME + ".meta", "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, fm, 1))


if __name__ == "__main__":
    r = export()
    print(len(r), compare(r, os.path.join(r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트", "소품_비교.png")) if r else "시트 없음")
    if "게임" in sys.argv[1:]: install(r); print("게임 폴더", GAME)
