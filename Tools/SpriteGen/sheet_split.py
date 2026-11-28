"""11-12 4장 묶음 시트(codex_batch.py body_sheet_jobs: <동작>_<방향>/sheet_1.png = 1~4장, sheet_2.png = 5~8장, 회전베기도 같음)를 01~08.png로.
- 덩어리 나누기·분홍 정리는 walk_sheet.py 것 그대로(덩어리가 4개 안 되면 4등분)
- 크기: 방향마다 한 배율 = (디자인 키 x FIT) / 대기 시트 키 가운데값 -> 그 방향 모든 동작에 같게(웅크리는 대쉬도 안 커짐). 회전베기는 앞 배율
  FIT: 디자인은 판의 81%를 채워 칼 든 팔이 위로 잘림 -> 다른 등급 낱장처럼 키 830/1254, 발 줄 1049
- 세로: 시트 안 바닥줄(4장 중 가장 아래 발)을 발 줄에 - 점프·웅크림 높이 차이는 그대로
- 가로: 머리 가운데를 디자인 머리 가운데에
사용: python sheet_split.py [몸_10_Unknown ...] (기본: SHEET_SETS)"""
import os, sys
import numpy as np
from PIL import Image
from walk_sheet import AUTO, ink, blobs, head_cx, NO_LIGHT
from codex_batch import SHEET_SETS


FOOT, TALL = 1049 / 1254, 830 / 1254     # 다른 등급(몸_09_영원 대기_앞 01) 낱장의 발 줄·키 비율


def size_of(a):
    m = ink(a); rows = np.nonzero(m.any(1))[0]
    return rows[0], rows[-1], m


def sheet_crops(set_dir, folder):
    sheets = [os.path.join(folder, f"sheet_{i}.png") for i in (1, 2)]
    if not all(os.path.exists(p) for p in sheets): return None
    crops = []
    for p in sheets:
        a, bs = blobs(Image.open(p), light_ok=os.path.basename(set_dir) not in NO_LIGHT)
        if len(bs) != 4:
            print(f"  {os.path.basename(folder)}/{os.path.basename(p)}: 덩어리 {len(bs)}개 - 4등분")
            m = ink(a); W = a.shape[1]; bs = []
            for i in range(4):
                x0, x1 = W * i // 4, W * (i + 1) // 4
                r = np.nonzero(m[:, x0:x1].any(1))[0]; c = np.nonzero(m[:, x0:x1].any(0))[0]
                bs.append((x0 + c[0], r[0], x0 + c[-1] + 1, r[-1] + 1))
        ground = max(b[3] for b in bs)
        crops += [(a, b, ground) for b in bs]
    return crops


def scale_of(set_dir, d):
    a = np.asarray(Image.open(os.path.join(set_dir, "_디자인", f"{d}.png")).convert("RGB"))
    y0, y1, m = size_of(a); size = a.shape[0]
    fit = TALL * size / (os.path.exists(os.path.join(set_dir, "_디자인", "앞.png")) and
                         (lambda b: b[1] - b[0] + 1)(size_of(np.asarray(Image.open(os.path.join(set_dir, "_디자인", "앞.png")).convert("RGB")))))
    crops = sheet_crops(set_dir, os.path.join(set_dir, f"대기_{d}"))
    k = (y1 - y0 + 1) * fit / np.median([b[3] - b[1] for _, b, _ in crops])
    return k, size, head_cx(m, y0, y1 - y0 + 1)


def split(set_dir, sub, d, k, size, rhx):
    folder = os.path.join(set_dir, sub)
    crops = sheet_crops(set_dir, folder)
    if not crops: return False
    rfoot = int(FOOT * size); clip = 0
    for i, (a, (x0, y0, x1, y1), ground) in enumerate(crops):
        c = Image.fromarray(a[y0:y1, x0:x1]); c = c.resize((max(1, round(c.width * k)), max(1, round(c.height * k))), Image.NEAREST)
        can = Image.new("RGB", (size, size), (255, 255, 255))
        hx = head_cx(ink(np.asarray(c)), 0, c.height)
        top = rfoot + 1 - c.height - round((ground - y1) * k); left = int(round(rhx - hx))
        clip += top < 0 or left < 0 or left + c.width > size
        can.paste(c, (left, top))
        can.save(os.path.join(folder, f"{i + 1:02d}.png"))
    print(f"  {sub} 배율 {k:.3f}" + (f"  판 밖으로 잘린 장 {clip}" if clip else ""))
    return True


def run(set_dir):
    print(os.path.basename(set_dir))
    K = {d: scale_of(set_dir, d) for d in ("앞", "옆", "뒤")}
    for sub in sorted(os.listdir(set_dir)):
        if sub.startswith("_") or sub.endswith("_v2"): continue
        d = "앞" if sub == "회전베기" else sub.rsplit("_", 1)[-1]
        if d in K: split(set_dir, sub, d, *K[d])


if __name__ == "__main__":
    for s in sys.argv[1:] or sorted(SHEET_SETS):
        run(os.path.join(AUTO, s))
