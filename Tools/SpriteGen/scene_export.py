r"""scene_batch.py 결과 -> 도트화 -> 비교 그림 / 게임 설치.
사용: python scene_export.py [murals,bgs,frames,tel] [게임]
- 미리보기: 바탕화면 고퀄 스프라이트\장면\<종류>_비교.png
- 게임: MuralsHQ(2배, PPU x2) + PropsHQ/Mural*(52/32배), BackgroundsHQ(2배), UIHQ/Common|Room|Title(2배, 9-slice 테두리도 2배), EffectsHQ/tel_*(52/32배)
  고퀄 경로는 PixelUi.Get(맨 앞 폴더+HQ) / VisualUtil.PropSprite / Fx.Folder 가 읽고, 없으면 옛 그림."""
import os, re, sys, uuid, numpy as np
from PIL import Image, ImageDraw, ImageFont
import scene_batch as SB, fx_batch as FB, fx_export as FE

RES = SB.RES
CMP = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트\장면"
FONT = ImageFont.truetype(r"C:\Windows\Fonts\malgun.ttf", 15)
K = 52 / 32
TEL_ALV = np.array([0, 50, 120, 200, 255.])


def old(rel):
    return Image.open(os.path.join(RES, rel + ".png")).convert("RGBA")


def quant(im, n):
    a = np.asarray(im.convert("RGBA")); rgb = Image.fromarray(a[..., :3])
    q = np.asarray(rgb.quantize(n, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB"))   # MAXCOVERAGE 는 큰 그림에서 갈색->빨강으로 튐
    return Image.fromarray(np.dstack([q, a[..., 3]]).astype(np.uint8), "RGBA")


def fit_crop(im, w, h):
    """비율 맞춰 가운데 자르고(넘치는 쪽만) w x h 로 줄임."""
    W, H = im.size; t = w / h
    if W / H > t: nw = round(H * t); im = im.crop(((W - nw) // 2, 0, (W - nw) // 2 + nw, H))
    else: nh = round(W / t); im = im.crop((0, (H - nh) // 2, W, (H - nh) // 2 + nh))
    return im.resize((w, h), Image.BOX)


# ---------- 종류별 ----------
def reframe(new, o, k):
    """옛 벽화의 테두리(금색 틀+어두운 가장자리)를 k배로 키워 새 그림 가장자리에 덮는다 - 가운데 자르기로 틀이 잘려서."""
    a = np.asarray(o); h, w = a.shape[:2]
    # 테두리 두께: 가장자리에서 금색(R>G>B, R>150)이 끝나는 곳까지
    gold = (a[..., 0] > 150) & (a[..., 0] > a[..., 2] + 50) & (a[..., 3] > 0)
    t = 1
    for d in range(1, min(h, w) // 4):
        if gold[d, w // 2] or gold[h // 2, d]: t = d + 1
    big = o.resize((new.width, new.height), Image.NEAREST); m = Image.new("L", new.size, 255)
    ImageDraw.Draw(m).rectangle([round(t * k), round(t * k), new.width - 1 - round(t * k), new.height - 1 - round(t * k)], fill=0)
    out = new.copy(); out.paste(big, (0, 0), m); return out


def murals():
    res = {}
    for rel in SB.MURALS:
        n = os.path.basename(rel) + ("_prop" if "Props" in rel else "")
        p = os.path.join(SB.OUT["murals"], n + ".png")
        if not os.path.exists(p): continue
        o = old(rel); src = Image.open(p).convert("RGB")
        if "Props" in rel:
            res[rel] = reframe(quant(fit_crop(src, round(o.width * K), round(o.height * K)).convert("RGBA"), 32), o, K)
        else:
            big = reframe(quant(fit_crop(src, o.width * 2, o.height * 2).convert("RGBA"), 48), o, 2)
            res[rel] = big
            prop = "Sprites/Props/" + os.path.basename(rel)          # 방 벽 소품(96x55)도 같은 그림을 줄여서
            if os.path.exists(os.path.join(RES, prop + ".png")):
                po = old(prop); res[prop] = reframe(quant(fit_crop(src, round(po.width * K), round(po.height * K)).convert("RGBA"), 32), po, K)
    return res


def bgs():
    res = {}
    for rel in SB.BGS:
        p = os.path.join(SB.OUT["bgs"], os.path.basename(rel) + ".png")
        if not os.path.exists(p): continue
        o = old(rel); res[rel] = quant(fit_crop(Image.open(p).convert("RGB"), o.width * 2, o.height * 2).convert("RGBA"), 200)
    return res


def border(rel):
    m = open(os.path.join(RES, rel + ".png.meta"), encoding="utf-8").read()
    b = re.search(r"spriteBorder: \{x: (\d+), y: (\d+), z: (\d+), w: (\d+)\}", m)
    return tuple(map(int, b.groups())) if b else (0, 0, 0, 0)


def frames():
    res = {}
    for n, items in SB.FRAMES.items():
        p = os.path.join(SB.OUT["frames"], n + ".png")
        if not os.path.exists(p): continue
        c, r = SB.FRAME_GRID[n]
        rgb = np.asarray(Image.open(p).convert("RGB")).astype(float); H, W = rgb.shape[:2]
        for k, rel in enumerate(items):
            x0, x1 = W * (k % c) // c, W * (k % c + 1) // c; y0, y1 = H * (k // c) // r, H * (k // c + 1) // r
            fg, al = FE.key(rgb[y0:y1, x0:x1], FB.MAGENTA)
            a = np.dstack([fg, np.where(al > 128, 255, 0)]).astype(np.uint8)
            ys, xs = np.nonzero(a[..., 3])
            if not len(ys): continue
            a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
            o = old(rel); bx0, by0, bx1, by1 = o.getbbox(); tw, th = (bx1 - bx0) * 2, (by1 - by0) * 2
            stretch = any(border(rel)) or rel.startswith("UI/Title/") and rel != "UI/Title/Logo"
            if stretch: nw, nh = tw, th                                 # 9-slice·버튼: 옛 틀과 같은 칸에 꽉
            else:
                s = min(tw / a.shape[1], th / a.shape[0]); nw, nh = max(1, round(a.shape[1] * s)), max(1, round(a.shape[0] * s))
            im = Image.fromarray(a).resize((nw, nh), Image.BOX)
            can = Image.new("RGBA", (o.width * 2, o.height * 2))
            can.alpha_composite(im, (int(round((bx0 + bx1) - nw / 2)), int(round((by0 + by1) - nh / 2))))
            arr = np.asarray(can).copy(); arr[..., 3] = np.where(arr[..., 3] > 110, 255, 0)
            res[rel] = quant(Image.fromarray(arr), 24)
    return res


def tel():
    res = {}
    for n in SB.TEL:
        p = os.path.join(SB.OUT["tel"], n + ".png")
        if not os.path.exists(p): continue
        fs = FB.frames(n); w, h = fs[0].size; c, r = FB.layout(len(fs), w, h)
        lum = np.asarray(Image.open(p).convert("L")).astype(float); H, W = lum.shape; out = []
        for i in range(len(fs)):
            cell = lum[H * (i // c) // r:H * (i // c + 1) // r, W * (i % c) // c:W * (i % c + 1) // c]
            cell = np.asarray(Image.fromarray(cell.astype(np.uint8)).resize((round(w * K), round(h * K)), Image.BOX)).astype(float)
            cell[cell < 22] = 0
            al = TEL_ALV[np.abs(cell[..., None] - TEL_ALV).argmin(-1)].astype(np.uint8)
            a = np.zeros((*al.shape, 4), np.uint8); a[..., :3] = 255; a[..., 3] = al
            out.append(Image.fromarray(a, "RGBA"))
        res["Effects/" + n] = out
    return res


KINDS = {"murals": murals, "bgs": bgs, "frames": frames, "tel": tel}


# ---------- 비교 ----------
def compare(kind, res):
    os.makedirs(CMP, exist_ok=True); rows = []
    for rel, new in res.items():
        if kind == "tel":
            olds = FB.frames(rel.split("/")[1]); news = new; z = 4
        else:
            olds, news = [old(rel)], [new]; z = max(1, min(4, 560 // max(olds[0].size)))
        ow, oh = olds[0].size; bg = (0, 0, 0, 255) if kind == "tel" else (40, 40, 52, 255)
        row = Image.new("RGBA", (240 + 2 * len(olds) * (ow * z + 8), oh * z + 10), bg)
        ImageDraw.Draw(row).text((4, 4), rel.split("/")[-1], fill="white", font=FONT)
        x = 220
        for o in olds:
            row.alpha_composite(o.resize((ow * z, oh * z), Image.NEAREST), (x, 5)); x += ow * z + 8
        x += 16
        for nn in news:
            row.alpha_composite(nn.resize((ow * z, oh * z), Image.BOX if nn.width != ow else Image.NEAREST), (x, 5)); x += ow * z + 8
        rows.append(row)
    if not rows: return None
    W = max(r.width for r in rows); paths = []
    for part in range(0, len(rows), 12):
        rs = rows[part:part + 12]; o = Image.new("RGBA", (W, sum(r.height for r in rs)), (30, 30, 38, 255)); y = 0
        for r in rs: o.alpha_composite(r, (0, y)); y += r.height
        p = os.path.join(CMP, f"{kind}_비교_{part // 12 + 1}.png"); o.save(p); paths.append(p)
    return paths


# ---------- 설치 ----------
def hq_path(rel):
    if rel.startswith("Sprites/Props/"): return "Sprites/PropsHQ/" + rel.split("/", 2)[2], K
    if rel.startswith("Effects/"): return "EffectsHQ/" + rel.split("/", 1)[1], K
    a, b = rel.split("/", 1); return a + "HQ/" + b, 2


def folder_meta(d):
    """새 폴더마다 .meta (Resources 까지 올라가며, 이미 있으면 그만)."""
    root = os.path.normpath(RES)
    while os.path.normpath(d) != root and not os.path.exists(d + ".meta"):
        src = os.path.join(RES, "Effects.meta")
        open(d + ".meta", "w", encoding="utf-8", newline="\n").write(re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, open(src, encoding="utf-8").read(), 1))
        d = os.path.dirname(d)


def write(dst_rel, im, src_meta, mult):
    dst = os.path.join(RES, dst_rel + ".png"); os.makedirs(os.path.dirname(dst), exist_ok=True); folder_meta(os.path.dirname(dst))
    im.save(dst)
    m = open(src_meta, encoding="utf-8").read()
    ppu = float(re.search(r"spritePixelsToUnits: ([\d.]+)", m).group(1))
    m = re.sub(r"guid: \w+", "guid: " + uuid.uuid4().hex, m, 1)
    m = re.sub(r"spritePixelsToUnits: [\d.]+", f"spritePixelsToUnits: {ppu * mult:g}", m)
    m = re.sub(r"spriteBorder: \{x: (\d+), y: (\d+), z: (\d+), w: (\d+)\}",
               lambda b: "spriteBorder: {x: %d, y: %d, z: %d, w: %d}" % tuple(round(int(v) * mult) for v in b.groups()), m)
    open(dst + ".meta", "w", encoding="utf-8", newline="\n").write(m)


def install(res):
    for rel, new in res.items():
        dst, mult = hq_path(rel)
        if isinstance(new, list):
            for i, f in enumerate(new): write(f"{dst}/{i}", f, os.path.join(RES, rel, f"{i}.png.meta"), mult)
        else:
            write(dst, new, os.path.join(RES, rel + ".png.meta"), mult)


if __name__ == "__main__":
    args = sys.argv[1:]; game = "게임" in args; args = [a for a in args if a != "게임"]
    sel = args[0].split(",") if args else list(KINDS)
    skip = set(args[1].split(",")) if len(args) > 1 else set()        # 3번째 인자: 설치에서 뺄 이름들
    for kind in sel:
        res = KINDS[kind]()
        res = {k: v for k, v in res.items() if k.split("/")[-1] not in skip}
        print(kind, len(res), compare(kind, res))
        if game: install(res); print("  설치", len(res))
