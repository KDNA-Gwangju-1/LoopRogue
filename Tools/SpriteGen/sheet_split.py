"""11-12 4장 묶음 시트(codex_batch.py body_sheet_jobs: <동작>_<방향>/sheet_1.png = 1~4장, sheet_2.png = 5~8장, 회전베기도 같음)를 01~08.png로.
- 덩어리 나누기·분홍 정리는 walk_sheet.py 것 그대로(덩어리가 4개 안 되면 4등분)
- 크기: 머리 폭(위 22%) 가운데값을 디자인 그림(_디자인/<방향>.png) 머리 폭에 맞춤 - 웅크리는 동작(대쉬)도 안 커지게
- 세로: 시트 안 바닥줄(4장 중 가장 아래 발)을 디자인 발 줄에 - 점프·웅크림 높이 차이는 그대로
- 가로: 머리 가운데를 디자인 머리 가운데에
사용: python sheet_split.py [몸_10_Unknown ...] (기본: SHEET_SETS)"""
import os, sys
import numpy as np
from PIL import Image
from walk_sheet import AUTO, ink, blobs, head_cx, NO_LIGHT
from codex_batch import SHEET_SETS


def head_w(m, y0, h):
    xs = np.nonzero(m[y0:y0 + max(2, int(h * 0.22))].any(0))[0]
    return xs[-1] - xs[0] + 1


def ref_of(set_dir, d):
    a = np.asarray(Image.open(os.path.join(set_dir, "_디자인", f"{d}.png")).convert("RGB")); m = ink(a)
    rows = np.nonzero(m.any(1))[0]; h = rows[-1] - rows[0] + 1
    return head_w(m, rows[0], h), a.shape[0], rows[-1], head_cx(m, rows[0], h)


def split(set_dir, sub, d):
    folder = os.path.join(set_dir, sub)
    sheets = [os.path.join(folder, f"sheet_{i}.png") for i in (1, 2)]
    if not all(os.path.exists(p) for p in sheets): return False
    rw, size, rfoot, rhx = ref_of(set_dir, d)
    crops = []
    for p in sheets:
        a, bs = blobs(Image.open(p), light_ok=os.path.basename(set_dir) not in NO_LIGHT)
        if len(bs) != 4:
            print(f"  {sub}/{os.path.basename(p)}: 덩어리 {len(bs)}개 - 4등분")
            m = ink(a); W = a.shape[1]; bs = []
            for i in range(4):
                x0, x1 = W * i // 4, W * (i + 1) // 4
                r = np.nonzero(m[:, x0:x1].any(1))[0]; c = np.nonzero(m[:, x0:x1].any(0))[0]
                bs.append((x0 + c[0], r[0], x0 + c[-1] + 1, r[-1] + 1))
        ground = max(b[3] for b in bs)
        crops += [(a, b, ground) for b in bs]
    ws = [head_w(ink(a[y0:y1, x0:x1]), 0, y1 - y0) for a, (x0, y0, x1, y1), _ in crops]
    k = rw / np.median(ws)
    for i, (a, (x0, y0, x1, y1), ground) in enumerate(crops):
        c = Image.fromarray(a[y0:y1, x0:x1]); c = c.resize((max(1, round(c.width * k)), max(1, round(c.height * k))), Image.NEAREST)
        can = Image.new("RGB", (size, size), (255, 255, 255))
        hx = head_cx(ink(np.asarray(c)), 0, c.height)
        lift = round((ground - y1) * k)
        can.paste(c, (int(round(rhx - hx)), rfoot + 1 - c.height - lift))
        can.save(os.path.join(folder, f"{i + 1:02d}.png"))
    print(f"  {sub} 배율 {k:.3f}")
    return True


def run(set_dir):
    print(os.path.basename(set_dir))
    for sub in sorted(os.listdir(set_dir)):
        if sub.startswith("_") or sub.endswith("_v2"): continue
        d = "앞" if sub == "회전베기" else sub.rsplit("_", 1)[-1]
        if d in ("앞", "옆", "뒤"): split(set_dir, sub, d)


if __name__ == "__main__":
    for s in sys.argv[1:] or sorted(SHEET_SETS):
        run(os.path.join(AUTO, s))
