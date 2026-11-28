r"""11-28 사용자 "몬스터 이벤트 이펙트랑 ui그림부터 쭉": 몹·보스·이벤트·아이템·상태 이펙트(Resources/Effects, 용사 무기 이펙트·hq_ 제외) 고퀄.
이펙트 하나 = 시트 한 장(모든 프레임을 격자로). 배경은 단색(자홍, 이펙트에 분홍이 많으면 초록) -> fx_export.py 가 색을 빼고 자름.
사용: python fx_batch.py [all|이름1,이름2]   결과: 바탕화면 gpt 그림\고퀄\자동\이펙트\<이름>.png"""
import os, re, sys, math, numpy as np
from PIL import Image
import codex_batch as cb

FX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Effects")
OUT = os.path.join(cb.OUT, "이펙트")
REF = os.path.join(cb.WORK, "fx_ref")
HERO = os.path.join(cb.HQ, "대기_앞", "01.png")
SKIP = re.compile(r"^(slash|slash_crit|dash|spin|spin_cross)(_g\d)?$|^relic_|^shadow_(slash|split)$|^hq_|^tel|^status_dashmark")
MAGENTA, GREEN = (255, 0, 255), (0, 255, 0)


def names():
    return [d for d in sorted(os.listdir(FX)) if os.path.isdir(os.path.join(FX, d)) and not SKIP.match(d)]


def frames(n):
    out = []
    for i in range(99):
        p = os.path.join(FX, n, f"{i}.png")
        if not os.path.exists(p): break
        out.append(Image.open(p).convert("RGBA"))
    return out


def bg_color(fs):
    a = np.concatenate([np.asarray(f).reshape(-1, 4) for f in fs]).astype(int)
    a = a[a[:, 3] > 60]
    if not len(a): return MAGENTA
    pink = ((a[:, 0] > 150) & (a[:, 2] > 120) & (a[:, 1] < 120)).mean()
    return GREEN if pink > 0.04 else MAGENTA


def layout(n, w, h):
    best = None
    for c in range(1, n + 1):
        r = math.ceil(n / c); asp = c * w / (r * h)
        sc = abs(math.log(asp / 1.4)) + (r * c - n) * 0.2
        if best is None or sc < best[0]: best = (sc, c, r)
    return best[1], best[2]


def ref_sheet(n, fs, c, r, bg):
    path = os.path.join(REF, f"{n}.png")
    w, h = fs[0].size; k = max(2, 256 // max(w, h)); W, H = w * k, h * k
    if not os.path.exists(path):
        os.makedirs(REF, exist_ok=True)
        im = Image.new("RGBA", (c * W, r * H), (40, 40, 52, 255))   # 반투명 이펙트가 배경색에 물들지 않게 참고는 어두운 회색 바탕
        for i, f in enumerate(fs):
            im.alpha_composite(f.resize((W, H), Image.NEAREST), ((i % c) * W, (i // c) * H))
        im.convert("RGB").save(path)
    return path


def jobs(sel):
    for n in sel:
        fs = frames(n)
        if not fs: continue
        w, h = fs[0].size; c, r = layout(len(fs), w, h); bg = bg_color(fs)
        bgname = "pure magenta (#FF00FF)" if bg == MAGENTA else "pure green (#00FF00)"
        what = f"{len(fs)} frames" if len(fs) > 1 else "1 frame"
        prompt = (f"Image 1 is an old low-resolution pixel-art game EFFECT animation ('{n}', {what}) laid out as a {c}x{r} grid of equal "
                  f"cells (each cell {w}:{h}) on a dark grey background (the grey is NOT part of the effect), read left to right, top to bottom. Redraw it as ONE image with exactly the same {c}x{r} grid "
                  f"layout, cell proportions and frame order, as high-quality detailed pixel art in the style of image 2 (a high-quality "
                  f"pixel-art hero: crisp pixels, clean dark outlines where the effect has solid parts, rich shading). Keep the same "
                  f"shapes, colours, size, position inside each cell and motion of every frame - same timing, same effect - just much "
                  f"more detailed and polished, like a modern pixel-art game VFX. Semi-transparent glows may be drawn as solid lighter pixels or dithering. Empty cells stay empty. The background of the whole "
                  f"image must be flat solid {bgname} everywhere outside the effect (no gradient, no texture, no grid lines, no borders, "
                  f"no text, no numbers), and the effect itself must not use that colour. Use your image generation tool, do not write code.")
        yield os.path.join(OUT, f"{n}.png"), prompt, [ref_sheet(n, fs, c, r, bg), HERO]


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    sel = names() if mode == "all" else mode.split(",")
    cb.log(f"이펙트 시작 {len(sel)}종 ({cb.quota_text()})")
    for job in jobs(sel):
        if cb.run(*job) is None:
            break
    cb.log("이펙트 끝")
