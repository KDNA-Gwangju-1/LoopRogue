"""고퀄 용사(큰 도트풍 이미지, 장마다 1장 파일, 투명 배경) -> 64x64 진짜 도트 -> Resources/Sprites/HeroHQ (11-04 art-style-option)
사용법: python hq_hero.py [원본 폴더(기본: 바탕화면\\gpt 그림\\고퀄)]

원본 폴더 구성: {동작}_{앞|뒤|옆}\\01.png, 02.png ... (옆 = 왼쪽 봄, 오른쪽은 좌우반전)
  동작 = 대기(idle) 걷기(walk) 공격(attack) 대쉬(dash), 회전베기\\01~08.png(spin_south)
- 칸 크기 = 걷기_앞 1번째 장 키 / 48 (모든 동작·방향 공통 -> 크기 통일)
- 동작·방향마다 1번째 장 기준으로 같은 격자·같은 위치를 모든 장에 적용 -> 장끼리 떨리지 않음(그려진 들썩임은 그대로)
- 칸마다 가운데 60% 중앙값, 덮인 비율 50% 이상만 불투명, 전체 공통 32색
- 캔버스 64, PPU 64(예전 HeroBody와 같은 도트 크기), 발 밑 58줄"""
import os, sys, glob, uuid
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Sprites", "HeroHQ")
TEMPLATE = os.path.join(ROOT, "Assets", "Resources", "Sprites", "HeroBody0", "idle_east_0.png.meta")
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "gpt 그림", "고퀄")
HEIGHT, NCOL, C, FEET = 48, 32, 64, 58

KINDS = {"대기": "idle", "걷기": "walk", "공격": "attack", "대쉬": "dash"}
DIRS = {"앞": "south", "뒤": "north", "옆": "west"}


def load(p):
    return np.asarray(Image.open(p).convert("RGBA")).astype(np.int32)


groups = {}
for kr, en in KINDS.items():
    for dk, de in DIRS.items():
        files = sorted(glob.glob(os.path.join(SRC, f"{kr}_{dk}", "*.png")))
        if files:
            groups[f"{en}_{de}"] = files
spin = sorted(glob.glob(os.path.join(SRC, "회전베기", "*.png")))
if spin:
    groups["spin_south"] = spin
if "walk_south" not in groups:
    sys.exit("걷기_앞 폴더가 있어야 크기를 잡는다")

a0 = load(groups["walk_south"][0])
ys = np.nonzero((a0[..., 3] > 128).any(axis=1))[0]
S = (ys.max() - ys.min() + 1) / HEIGHT
print(f"도트 1칸 = 원본 {S:.2f}px")


def to_cells(a, oy, ox):
    H, W = a.shape[:2]
    gh = int((H - oy) // S); gw = int((W - ox) // S)
    out = np.zeros((gh, gw, 4), np.float64)
    for j in range(gh):
        y0 = oy + j * S
        ya, yb = int(round(y0 + S * 0.2)), int(round(y0 + S * 0.8))
        yA, yB = int(round(y0)), int(round(y0 + S))
        for i in range(gw):
            x0 = ox + i * S
            if (a[yA:yB, int(round(x0)):int(round(x0 + S)), 3] > 128).mean() < 0.5:
                continue
            blk = a[ya:yb, int(round(x0 + S * 0.2)):int(round(x0 + S * 0.8))].reshape(-1, 4)
            blk = blk[blk[:, 3] > 128]
            if len(blk):
                out[j, i, :3] = np.median(blk[:, :3], axis=0); out[j, i, 3] = 255
    return out


raw = {}
for g, files in groups.items():
    first = load(files[0])
    ys, xs = np.nonzero(first[..., 3] > 128)
    oy = (ys.max() + 1) % S  # 발 밑이 칸 경계에 오게
    cells = [to_cells(load(p), oy, 0.0) for p in files]
    cy, cx = np.nonzero(cells[0][..., 3] > 0)
    top = cy.min(); hx = int(round(cx[cy < top + 8].mean()))
    raw[g] = (cells, FEET - cy.max(), C // 2 - hx)
    print(f"{g:12s} {len(files)}장")

allpx = np.concatenate([c[c[..., 3] > 0][:, :3] for cells, _, _ in raw.values() for c in cells]).astype(np.uint8)
side = int(np.ceil(np.sqrt(len(allpx))))
buf = np.zeros((side * side, 3), np.uint8); buf[: len(allpx)] = allpx
q = Image.fromarray(buf.reshape(side, side, 3)).quantize(colors=NCOL, method=Image.Quantize.MAXCOVERAGE, kmeans=3, dither=Image.Dither.NONE)
pal = np.array(q.getpalette()[: NCOL * 3]).reshape(-1, 3)

tmpl = open(TEMPLATE, encoding="utf-8").read()
os.makedirs(OUT, exist_ok=True)


def save(img, name):
    path = os.path.join(OUT, name + ".png")
    Image.fromarray(np.ascontiguousarray(img), "RGBA").save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):  # guid는 처음 한 번만. PPU 64는 템플릿 그대로
        m = tmpl.replace("de8bfc5f9c7a49c88a10e008588f84bb", uuid.uuid4().hex)
        m = m.replace("66019ece891942b5942d266e51bcdae9", uuid.uuid4().hex)
        open(meta, "w", encoding="utf-8", newline="\n").write(m)


for g, (cells, dy, dx) in raw.items():
    out = []
    for c in cells:
        op = c[..., 3] > 0
        rgb = c[..., :3].astype(int)
        snapped = np.zeros_like(rgb)
        snapped[op] = pal[((rgb[op][:, None, :] - pal[None]) ** 2).sum(-1).argmin(1)]
        can = np.zeros((C, C, 4), np.uint8)
        for y, x in zip(*np.nonzero(op)):
            ty, tx = y + dy, x + dx
            if 0 <= ty < C and 0 <= tx < C:
                can[ty, tx, :3] = snapped[y, x]; can[ty, tx, 3] = 255
        out.append(can)
    for i, f in enumerate(out):
        save(f, f"{g}_{i}")
    if g.endswith("_west"):  # 오른쪽 = 옆 좌우반전
        for i, f in enumerate(out):
            save(f[:, ::-1].copy(), f"{g[:-5]}_east_{i}")
print("완료:", OUT)
