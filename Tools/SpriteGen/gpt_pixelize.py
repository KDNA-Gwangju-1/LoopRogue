"""GPT 도트풍 이미지 -> 진짜 도트 변환
사용법: python pixelize.py 입력.png 출력폴더 [색수=32] [도트크기=auto]
"""
import sys, os
from collections import deque
import numpy as np
from PIL import Image

src, outdir = sys.argv[1], sys.argv[2]
ncolors = int(sys.argv[3]) if len(sys.argv) > 3 else 32
forced = float(sys.argv[4]) if len(sys.argv) > 4 else None
os.makedirs(outdir, exist_ok=True)

img = Image.open(src).convert("RGB")
a = np.asarray(img).astype(np.float32)
H, W, _ = a.shape
lum = a.mean(axis=2)


def edge_profile(axis):
    d = np.abs(np.diff(lum, axis=axis))
    return d.sum(axis=1 - axis) if axis == 1 else d.sum(axis=1)


def best_grid(sig):
    """sig[i] = edge energy between pixel i and i+1. 반환: (scale, offset, score)"""
    n = len(sig)
    base = sig.mean() + 1e-6
    results = []
    for s in np.arange(3.0, 16.01, 0.05):
        best = (0, 0)
        for off in np.arange(0, s, 0.25):
            idx = np.round(np.arange(off, n - 1, s)).astype(int)
            idx = idx[idx < n]
            sc = sig[idx].mean() / base
            if sc > best[0]:
                best = (sc, off)
        results.append((s, best[1], best[0]))
    top = max(r[2] for r in results)
    # 배수 주기도 점수가 높으므로, 최고점의 90% 이상인 것 중 가장 작은 scale
    for r in results:
        if r[2] >= top * 0.9:
            return r
    return max(results, key=lambda r: r[2])


sx = best_grid(edge_profile(1))
sy = best_grid(edge_profile(0))
print(f"x grid: scale={sx[0]:.2f} off={sx[1]:.2f} score={sx[2]:.2f}")
print(f"y grid: scale={sy[0]:.2f} off={sy[1]:.2f} score={sy[2]:.2f}")
scale = forced or (sx[0] + sy[0]) / 2
offx = sx[1] % scale
offy = sy[1] % scale
print(f"사용 도트크기 = {scale:.2f}px")

# 1) 격자 칸마다 가운데 영역의 중앙값 색 (원본 색 그대로)
xs = np.arange(offx - scale, W, scale)
ys = np.arange(offy - scale, H, scale)
xs = xs[xs + scale > 0]
ys = ys[ys + scale > 0]
gw, gh = len(xs), len(ys)
small = np.zeros((gh, gw, 3), dtype=np.uint8)
bg0 = np.median(np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]]), axis=0)
bright_bg = bg0.mean() > 128
m = 0.25  # 칸 가장자리 25%는 무시 (번진 경계)
for j, y0 in enumerate(ys):
    ya, yb = int(max(0, round(y0 + scale * m))), int(min(H, round(y0 + scale * (1 - m))))
    if yb <= ya:
        ya, yb = int(max(0, y0)), int(min(H, y0 + scale))
    for i, x0 in enumerate(xs):
        xa, xb = int(max(0, round(x0 + scale * m))), int(min(W, round(x0 + scale * (1 - m))))
        if xb <= xa:
            xa, xb = int(max(0, x0)), int(min(W, x0 + scale))
        block = a[ya:yb, xa:xb].reshape(-1, 3)
        if block.size == 0:
            continue
        if bright_bg:
            # 흰 배경: 배경에 걸친 칸이 섞인 밝은 색(흰 테두리 점)이 되지 않게
            # 배경 픽셀이 절반 이상이면 배경, 아니면 배경 픽셀을 빼고 캐릭터 색만으로
            isbg = np.abs(block - bg0).max(axis=1) <= 18
            if isbg.mean() >= 0.5:
                small[j, i] = bg0
                continue
            block = block[~isbg]
        small[j, i] = np.median(block, axis=0)

# 2) 축소된 이미지에서 색 줄이기 (kmeans로 다듬어 작은 색도 살림)
q = Image.fromarray(small).quantize(colors=ncolors, method=Image.Quantize.MAXCOVERAGE,
                                    kmeans=3, dither=Image.Dither.NONE)
pal = np.array(q.getpalette()[: ncolors * 3]).reshape(-1, 3)
grid = np.asarray(q).astype(np.int32)
rgb = pal[grid].astype(np.uint8)

# 3) 배경 투명화: 가장자리에서 이어진 '아주 어두운' 칸만 제거
# 배경색 = 테두리 칸들의 중앙값(검은색이든 흰색이든). 양자화 전 색으로 판정 (외곽선이 배경에 먹히지 않게)
border = np.concatenate([small[0], small[-1], small[:, 0], small[:, -1]]).astype(int)
bg = np.median(border, axis=0)
if bright_bg:  # 격자 가장자리 칸이 이미지 밖이라 비면(검정) 테두리 중앙값이 틀어진다 - 원본에서 잰 배경색을 쓴다
    bg = bg0
    small[(small == 0).all(axis=2)] = bg0.astype(np.uint8)
bright = np.abs(small.astype(int) - bg).max(axis=2)  # 배경색과의 차이
bg_thresh = 12
print(f"배경색 = {bg.astype(int).tolist()}")
# 어두운 칸끼리 이어진 덩어리 중 가장자리에 닿거나 크기가 큰 것 = 배경 (테두리로 막힌 패널 안쪽도 지움)
alpha = np.full((gh, gw), 255, np.uint8)
dark = bright <= bg_thresh
seen = np.zeros((gh, gw), bool)
big = 150
for sj in range(gh):
    for si in range(gw):
        if seen[sj, si] or not dark[sj, si]:
            continue
        comp, edge = [], False
        dq = deque([(sj, si)])
        seen[sj, si] = True
        while dq:
            j, i = dq.popleft()
            comp.append((j, i))
            if j in (0, gh - 1) or i in (0, gw - 1):
                edge = True
            for dj, di in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nj, ni = j + dj, i + di
                if 0 <= nj < gh and 0 <= ni < gw and dark[nj, ni] and not seen[nj, ni]:
                    seen[nj, ni] = True
                    dq.append((nj, ni))
        if edge or len(comp) >= big:
            for j, i in comp:
                alpha[j, i] = 0

if bright_bg:
    # 흰 배경: 안쪽에 갇힌 '배경 판정' 칸 덩어리 - 둘레가 어두우면(팔·다리·칼 사이 틈) 투명,
    # 둘레가 밝으면(흰 머리카락 하이라이트) 그대로 둔다
    isbgcell = (small == bg0.astype(np.uint8)).all(axis=2) & (alpha > 0)
    seen2 = np.zeros_like(isbgcell)
    lum_small = small.astype(int).mean(axis=2)
    for sj in range(gh):
        for si in range(gw):
            if not isbgcell[sj, si] or seen2[sj, si]:
                continue
            comp, ring = [], []
            dq = deque([(sj, si)]); seen2[sj, si] = True
            while dq:
                j, i = dq.popleft(); comp.append((j, i))
                for dj, di in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nj, ni = j + dj, i + di
                    if not (0 <= nj < gh and 0 <= ni < gw):
                        continue
                    if isbgcell[nj, ni]:
                        if not seen2[nj, ni]:
                            seen2[nj, ni] = True; dq.append((nj, ni))
                    elif alpha[nj, ni] > 0:
                        ring.append(lum_small[nj, ni])
            # 5칸 이하 작은 흰 덩어리는 틈이 아니라 눈 흰자·하이라이트 (지우면 눈이 뚫려 무섭게 보임)
            # 둘레에 밝은 칸(흰 머리카락 등)이 25% 이상이면 틈이 아니라 하이라이트 (11-04 뒷모습 머리에 구멍 남)
            light_share = np.mean([v > 170 for v in ring]) if ring else 1.0
            if ring and np.mean(ring) < 150 and len(comp) >= 6 and light_share < 0.25:
                for j, i in comp:
                    alpha[j, i] = 0

# 4) 외톨이 1도트 노이즈 제거 (상하좌우가 전부 투명이면 삭제)
pad = np.pad(alpha, 1)
lonely = (pad[:-2, 1:-1] == 0) & (pad[2:, 1:-1] == 0) & (pad[1:-1, :-2] == 0) & (pad[1:-1, 2:] == 0)
alpha[lonely] = 0

out = Image.fromarray(np.dstack([rgb, alpha]), "RGBA")
name = os.path.splitext(os.path.basename(src))[0]
out.save(os.path.join(outdir, f"{name}_도트_원본크기.png"))
big = out.resize((gw * 6, gh * 6), Image.NEAREST)
big.save(os.path.join(outdir, f"{name}_도트_6배.png"))
# 미리보기용 (검은 배경 합성)
prev = Image.new("RGBA", big.size, (30, 30, 40, 255))
prev.alpha_composite(big)
prev.convert("RGB").save(os.path.join(outdir, f"{name}_미리보기.png"))
print(f"결과 해상도: {gw}x{gh}, 사용 색 수: {len(np.unique(grid[alpha > 0]))}")
