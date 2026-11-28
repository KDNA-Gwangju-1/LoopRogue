"""11-27 사용자 "4장씩 겹쳐서 그리라고해 미노타보니까 그래야것다": 몬스터도 용사처럼 4장 묶음 시트로 그리게(장마다 도끼·몸이 달라지던 것 막기).
codex_batch.py mob_set_jobs(sheet=True) -> <동작>_<방향>/sheet_K.png (K번째 4장 묶음) -> split() 이 NN.png 낱장으로 자름.
- 가로로 잉크 있는 열을 덩어리로 나누고, 4개보다 많으면 가장 가까운 것끼리 합침, 모자라면 4등분
- 낱장은 흰 정사각 판에, 시트 바닥줄(가장 아래 발) 기준 높이 차이는 그대로(mob_export 가 옛 장 발 줄에 다시 맞춤)
사용: python mob_sheet.py <시트 png> <첫 장 번호(1부터)> [장 수]"""
import os, sys
import numpy as np
from PIL import Image


def ink(a):
    return a.min(-1) < 235


def strip(paths, out):
    """옛 자세 참고 그림(mob_ref) 여러 장을 가로 한 줄로."""
    if os.path.exists(out):
        return out
    ims = [Image.open(p).convert("RGB") for p in paths]
    h = max(i.height for i in ims)
    s = Image.new("RGB", (sum(i.width for i in ims), h), (255, 255, 255))
    x = 0
    for i in ims:
        s.paste(i, (x, h - i.height)); x += i.width
    os.makedirs(os.path.dirname(out), exist_ok=True)
    s.save(out)
    return out


def split(sheet, first, n=4):
    a = np.asarray(Image.open(sheet).convert("RGB")); m = ink(a); W = m.shape[1]
    col = m.sum(0) > 2
    runs, x = [], 0
    while x < W:
        if col[x]:
            x0 = x
            while x < W and col[x]: x += 1
            runs.append([x0, x])
        else: x += 1
    while len(runs) > n:
        gaps = [runs[i + 1][0] - runs[i][1] for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps)); runs[i] = [runs[i][0], runs[i + 1][1]]; del runs[i + 1]
    if len(runs) < n:
        print(f"  {os.path.basename(sheet)}: 덩어리 {len(runs)}개 - {n}등분")
        runs = [[W * i // n, W * (i + 1) // n] for i in range(n)]
    boxes = []
    for x0, x1 in runs:
        r = np.nonzero(m[:, x0:x1].any(1))[0]; c = np.nonzero(m[:, x0:x1].any(0))[0]
        boxes.append((x0 + c[0], r[0], x0 + c[-1] + 1, r[-1] + 1))
    ground = max(b[3] for b in boxes)
    S = max(max(b[2] - b[0], b[3] - b[1] + ground - b[3]) for b in boxes) + 96
    folder = os.path.dirname(sheet)
    for k, (x0, y0, x1, y1) in enumerate(boxes):
        can = Image.new("RGB", (S, S), (255, 255, 255))
        can.paste(Image.fromarray(a[y0:y1, x0:x1]), ((S - (x1 - x0)) // 2, S - 48 - (ground - y0)))
        can.save(os.path.join(folder, f"{first + k:02d}.png"))
    return True


if __name__ == "__main__":
    split(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 4)
