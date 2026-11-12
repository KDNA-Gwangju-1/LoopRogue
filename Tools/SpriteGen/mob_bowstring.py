"""11-12 사용자 "해골궁수 옆공격 활줄만 색 건들면", "2·3·5번 검은 활줄 색 변경": mob_export.py가 SkeletonArcher attack_east(+west 뒤집기)에 씀.
몸(망토 초록이 3칸+ 쌓인 세로줄) 오른쪽 ~ 활 테두리 앞에서, 4칸+ 이어진 검정/흰 세로 토막 = 시위.
줄마다 시위 자리(흰 칸 많은 세로줄)에 가장 가까운 한 칸만 연한 갈색, 나머지 검은 칸은 지움. 팔이 지나가는 줄(뼈색 2칸+)은 그대로.
가는 흰 점(좌우 흰색 아님)도 시위 색으로."""
import numpy as np
STR = (196, 168, 122)


def kinds(a):
    rgb = a[..., :3].astype(int); op = a[..., 3] > 0
    K = op & (rgb.max(-1) < 40); W = op & (rgb.min(-1) > 190)
    o = op & (rgb[..., 0] > rgb[..., 1] + 40) & ~W
    g = op & (rgb[..., 1] > rgb[..., 0]) & ~K & ~W
    return op, K, W, o, g


def fix(a, minrun=4):
    a = a.copy(); op, K, W, o, g = kinds(a); H, Wd = K.shape
    gc = np.nonzero(g.sum(0) >= 3)[0]
    body_r = int(gc.max()) + 2 if len(gc) else 24
    KW = K | W
    bowline = np.zeros_like(K); bowline[:, :-1] |= o[:, 1:]; bowline[:, 1:] |= o[:, :-1]
    cand = np.zeros_like(K)
    for x in range(body_r, Wd):
        y = 0
        while y < H:
            if KW[y, x] and not bowline[y, x]:
                y0 = y
                while y < H and KW[y, x] and not bowline[y, x]: y += 1
                if y - y0 >= minrun: cand[y0:y, x] = True
            else: y += 1
    n = 0
    if cand.any():
        wcol = (cand & W).sum(0)
        sx = int(wcol.argmax()) if wcol.max() > 0 else int(np.nonzero(cand.any(0))[0].min())
        for y in range(H):
            xs = np.nonzero(cand[y])[0]
            if not len(xs): continue
            band = a[y, body_r:, :3].astype(int)
            if ((band.min(-1) > 120) & (band.min(-1) <= 190)).sum() >= 2: continue   # 팔이 지나가는 줄
            keep = xs[np.argmin(np.abs(xs - sx))]
            for x in xs:
                if x == keep: a[y, x, :3] = STR; a[y, x, 3] = 255; n += 1
                elif K[y, x]: a[y, x] = 0
    op, K, W, o, g = kinds(a)
    for y in range(H):                                                  # 남은 가는 흰 점
        band = a[y, body_r:, :3].astype(int)
        if ((band.min(-1) > 120) & (band.min(-1) <= 190)).sum() >= 2 or W[y, body_r:].sum() >= 3: continue
        for x in range(body_r, Wd - 1):
            if W[y, x] and not W[y, x - 1] and not W[y, x + 1]: a[y, x, :3] = STR; n += 1
    return a, n


def fix_any(a, near=3, minrun=4):
    """앞·뒤·넘어짐 등: 활(주황 나무) 3칸 안에 있는 1칸 폭 흰 세로 토막(4칸+) = 시위 -> 같은 갈색(옆 공격과 맞춤)."""
    a = a.copy(); op, K, W, o, g = kinds(a); H, Wd = K.shape
    nearbow = o.copy()
    for _ in range(near):
        m = nearbow.copy(); m[:, 1:] |= nearbow[:, :-1]; m[:, :-1] |= nearbow[:, 1:]; m[1:] |= nearbow[:-1]; m[:-1] |= nearbow[1:]; nearbow = m
    thin = W.copy(); thin[:, 1:] &= ~W[:, :-1]; thin[:, :-1] &= ~W[:, 1:]
    n = 0
    for x in range(Wd):
        y = 0
        while y < H:
            if thin[y, x]:
                y0 = y
                while y < H and thin[y, x]: y += 1
                if y - y0 >= minrun and nearbow[y0:y, x].mean() > .5:
                    a[y0:y, x, :3] = STR; n += y - y0
            else: y += 1
    return a, n
