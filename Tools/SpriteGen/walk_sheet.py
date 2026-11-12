"""11-11 옆 걷기 시트(codex_batch.py walkside: 걷기_옆_v2/sheet_1.png = 1~4장, sheet_2.png = 5~8장)를 장마다 잘라 01~08.png로.
- 흰 바탕이 아닌 세로줄 덩어리를 왼쪽부터 4개로 묶음(칼 자리표시 분홍 포함)
- 분홍 칼 가장자리 섞인 색은 순수 분홍으로, 키움은 NEAREST
- 좌우는 머리 가운데를 대기 옆 01 머리 가운데에 맞춤
- 시트 1·2에서 같은 배율: 두 시트 모든 장의 키 가운데값을 다른 장(대기 옆 01) 키에 맞춰 키움 -> 흰 정사각 판, 발 높이 맞춤
- 그다음 HQ_WALKSIDE_V2=1 python hq_export.py 몸_... 로 도트화
사용: python walk_sheet.py [몸_... ...] (기본: 시트 2장 다 있는 세트 전부)"""
import os, sys, glob
import numpy as np
from PIL import Image

AUTO = os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "gpt 그림", "고퀄", "자동")


def ink(rgb):
    return (np.abs(rgb.astype(int) - 255).sum(-1) > 60)


def clean_pink(a):
    """분홍 칼 자리표시 가장자리의 섞인 색(분홍+빨강 갑옷 등)을 순수 #FF00FF로 - 안 그러면 도트화 때 몸 색으로 남음(신화 허리 분홍 점)."""
    a = a.astype(int); r, g, b = a[..., 0], a[..., 1], a[..., 2]
    core = (r > 200) & (b > 200) & (g < 80)
    light = (r > 215) & (b > 190) & (g < 150) & (np.abs(r - b) < 45)   # 칼막이 밝은 분홍 칠(253,103,244) - 분홍 심에 이어진 것만
    def grow(m, allow, n):
        for _ in range(n):
            n2 = m.copy(); n2[1:] |= m[:-1]; n2[:-1] |= m[1:]; n2[:, 1:] |= m[:, :-1]; n2[:, :-1] |= m[:, 1:]
            n2 &= allow
            if (n2 == m).all(): break
            m = n2
        return m
    near3 = grow(core, np.ones_like(core), 8)                    # 분홍 심 8칸 안(칼자루 테두리 건너) = 아무 색
    near = grow(near3, near3 | light, 300)                       # 그 뒤로는 밝은 분홍 따라서만 번짐
    fringe = near & (((b > 110) & (r > 140) & (b - g > 70) & (r - g > 70)) | light)
    a[fringe] = (255, 0, 255)
    return a.astype(np.uint8)


def blobs(im, n=4):
    a = clean_pink(np.asarray(im.convert("RGB"))); m = ink(a)
    col = m.sum(0) > 2
    runs, x = [], 0
    W = len(col)
    while x < W:
        if col[x]:
            x0 = x
            while x < W and col[x]: x += 1
            runs.append([x0, x])
        else: x += 1
    while len(runs) > n:                                         # 가장 가까운 이웃끼리 합침(칼·망토 조각)
        gaps = [runs[i + 1][0] - runs[i][1] for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps)); runs[i] = [runs[i][0], runs[i + 1][1]]; del runs[i + 1]
    out = []
    for x0, x1 in runs:
        rows = np.nonzero(m[:, x0:x1].any(1))[0]
        out.append((x0, rows[0], x1, rows[-1] + 1))
    return a, out


def head_cx(m, y0, h):
    """머리(위 22%) 가로 가운데 = 좌우 기준(망토·칼 폭에 안 흔들리게)."""
    xs = np.nonzero(m[y0:y0 + max(2, int(h * 0.22))].any(0))[0]; return (xs[0] + xs[-1]) / 2


def height_of(p):
    a = np.asarray(Image.open(p).convert("RGB")); m = ink(a); rows = np.nonzero(m.any(1))[0]
    h = rows[-1] - rows[0] + 1
    return h, a.shape[0], rows[-1], head_cx(m, rows[0], h)


def run(set_dir):
    d = os.path.join(set_dir, "걷기_옆_v2")
    sheets = [os.path.join(d, f"sheet_{i}.png") for i in (1, 2)]
    if not all(os.path.exists(p) for p in sheets): return False
    ref_h, size, ref_foot, ref_hx = height_of(os.path.join(set_dir, "대기_옆", "01.png"))
    crops = []
    for p in sheets:
        a, bs = blobs(Image.open(p))
        if len(bs) != 4: print(set_dir, os.path.basename(p), "덩어리", len(bs)); return False
        crops += [(a, b) for b in bs]
    k = ref_h / np.median([b[3] - b[1] for _, b in crops])
    for i, (a, (x0, y0, x1, y1)) in enumerate(crops):
        c = Image.fromarray(a[y0:y1, x0:x1]); c = c.resize((max(1, round(c.width * k)), max(1, round(c.height * k))), Image.NEAREST)   # 섞인 색 안 생기게
        can = Image.new("RGB", (size, size), (255, 255, 255))
        cm = ink(np.asarray(c)); hx = head_cx(cm, 0, c.height)
        can.paste(c, (int(round(ref_hx - hx)), ref_foot + 1 - c.height))
        can.save(os.path.join(d, f"{i + 1:02d}.png"))
    print(os.path.basename(set_dir), "배율", round(k, 3))
    return True


if __name__ == "__main__":
    for s in sys.argv[1:] or sorted(os.path.basename(p) for p in glob.glob(os.path.join(AUTO, "몸_*"))):
        run(os.path.join(AUTO, s))
