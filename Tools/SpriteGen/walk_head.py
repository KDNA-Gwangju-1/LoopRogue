"""11-11 사용자 "옆모습 걷기 얼굴이 너무 뭉개져": 옆 걷기 8장 머리를 대기 옆 0장 머리(깨끗한 얼굴)로 바꿔 붙인다.
- 머리 = 대기 옆 0장 맨 윗줄 ~ 턱(위에서부터 이어진 피부/머리색 줄의 끝, 27~31줄로 묶음)
- 걷기 장마다 머리 자리(dx ±4, dy ±3)를 색 맞춤으로 찾아 옮겨 붙임 = 걸을 때 머리 들썩임은 그대로
- 턱보다 2줄 위까지는 머리밖에 없으니 원래 머리를 다 지우고, 턱 근처(목·어깨)는 남긴 채 새 머리를 덮음
- 동쪽(walk_east)은 서쪽 좌우 뒤집기
사용: python walk_head.py [HeroHQ ...] (기본 = HeroHQ, HeroHQ1~9)  /  HQ_RES로 다른 폴더"""
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.environ.get("HQ_RES") or os.path.join(HERE, "..", "..", "Assets", "Resources", "Sprites")


FACE_FROM = "HeroHQ2"          # 11-11 사용자 "희귀·고급·영웅은 맘에 듦" -> 얼굴 본(희귀 대기 옆 0장)
BODY_SETS = {"HeroHQ8"}         # 11-11 사용자 "영원은 키가 안 맞음" -> 머리 높이를 옛 머리 대신 어깨(턱 아래 몸통) 맞춤으로
FACE_SETS = {"HeroHQ4", "HeroHQ5", "HeroHQ7", "HeroHQ8", "HeroHQ9"}   # 코가 나오거나 뭉개진 세트(전설·신화·초월·영원·Unknown)


def eye_of(img):
    a = img.astype(int)
    m = (a[..., 3] > 0) & (a[..., 2] > 150) & (a[..., 0] < 110) & (a[..., 2] - a[..., 0] > 80)
    ys, xs = np.nonzero(m[15:40]); ys = ys + 15
    if not len(ys): return None
    i = int(np.argmin((ys - 28) ** 2 + (xs - 28) ** 2)); return int(ys[i]), int(xs[i])


def put_face(idle, tpl):
    """눈을 맞춰 본 얼굴(눈 위 3줄 ~ 아래 4줄, 앞끝 ~ 눈 뒤 2칸)을 덮음. 본에서 머리카락 색 칸은 이 세트 머리카락을 남기고, 본에서 빈 칸은 지움(코 없앰)."""
    e1, e0 = eye_of(idle), eye_of(tpl)
    if e1 is None or e0 is None: return idle
    out = idle.copy(); dy, dx = e1[0] - e0[0], e1[1] - e0[1]
    t = tpl.astype(int)
    for y in range(e0[0] - 3, e0[0] + 5):
        for x in range(e0[1] - 9, e0[1] + 3):
            ty, tx = y + dy, x + dx
            if not (0 <= ty < 64 and 0 <= tx < 64): continue
            r, g, b, a = t[y, x]
            hair = a > 0 and r > 150 and g > 90 and b < 120
            if hair: continue
            if a == 0:
                if x < e0[1] - 1: out[ty, tx] = 0          # 얼굴 앞쪽 빈칸 = 코 자리 지움
                continue
            out[ty, tx] = tpl[y, x]
    return out


def head_of(idle):
    al = idle[..., 3] > 0
    rows = np.nonzero(al.any(1))[0]; top = rows[0]
    r, g, b = idle[..., 0].astype(int), idle[..., 1].astype(int), idle[..., 2].astype(int)
    warm = al & (r > 150) & (r - b > 30)                       # 피부·금발(투구는 아래 고정 범위로)
    chin = top
    for y in range(top, top + 26):
        if warm[y].any(): chin = y
        elif y > top + 8: break
    chin = int(np.clip(chin, 27, 31))
    m = np.zeros(al.shape, bool); m[top:chin + 2] = al[top:chin + 2]
    return m, top, chin


def find_offset(walk, idle, hm):
    """색 맞춤으로 이 장의 머리 자리(dx, dy)."""
    best = None
    ys, xs = np.nonzero(hm)
    src = idle[ys, xs].astype(int)
    for dy in range(-3, 4):
        for dx in range(-3, 4):
            ty, tx = ys + dy, xs + dx
            ok = (ty >= 0) & (ty < 64) & (tx >= 0) & (tx < 64)
            dst = walk[ty[ok], tx[ok]].astype(int)
            hit = dst[:, 3] > 0
            d = np.abs(dst[:, :3] - src[ok][:, :3]).sum(1)
            score = (hit & (d < 90)).sum() - 0.5 * (~hit).sum()
            if best is None or score > best[0]: best = (score, dx, dy)
    return best[1], best[2]


def body_offset(walk, idle, chin):
    """턱 아래 몸통(턱+1 ~ 턱+9줄) 색 맞춤 위아래 차이 = 이 장에서 어깨가 오르내린 만큼."""
    best = None
    a0 = idle[chin + 1:chin + 10].astype(int)
    for dy in range(-3, 4):
        if chin + 1 + dy < 0 or chin + 10 + dy > 64: continue
        b = walk[chin + 1 + dy:chin + 10 + dy].astype(int)
        for dx in range(-3, 4):
            bb = np.roll(b, dx, 1)
            hit = (a0[..., 3] > 0) & (bb[..., 3] > 0)
            score = (hit & (np.abs(a0[..., :3] - bb[..., :3]).sum(-1) < 120)).sum() - 0.3 * ((a0[..., 3] > 0) ^ (bb[..., 3] > 0)).sum()
            if best is None or score > best[0]: best = (score, dy)
    return best[1]


def fix_frame(walk, idle, hm, chin, dx, dy):
    ys, xs = np.nonzero(hm)
    out = walk.copy()
    cut = chin + dy - 2                                         # 이 줄 위로는 머리만 있음 -> 다 지움
    out[:max(0, cut)] = 0
    ty, tx = ys + dy, xs + dx; ok = (ty >= 0) & (ty < 64) & (tx >= 0) & (tx < 64)
    sh = np.zeros((64, 64), bool); sh[ty[ok], tx[ok]] = True
    for y in range(max(0, cut), min(64, chin + dy + 3)):      # 턱 근처: 새 얼굴보다 앞(왼쪽)에 남은 옛 코·턱 지움
        xs_new = np.nonzero(sh[y])[0]
        if len(xs_new): out[y, :xs_new.min()] = 0
    out[ty[ok], tx[ok]] = idle[ys[ok], xs[ok]]
    return out


def run(folder):
    d = os.path.join(RES, folder)
    p0 = os.path.join(d, "idle_west_0.png")
    if not os.path.exists(p0): return
    idle = np.asarray(Image.open(p0).convert("RGBA")).copy()
    if folder in FACE_SETS:
        idle = put_face(idle, np.asarray(Image.open(os.path.join(RES, FACE_FROM, "idle_west_0.png")).convert("RGBA")))
    hm, top, chin = head_of(idle)
    walks, offs = {}, {}
    for i in range(8):
        p = os.path.join(d, f"walk_west_{i}.png")
        if os.path.exists(p):
            walks[i] = np.asarray(Image.open(p).convert("RGBA")).copy(); offs[i] = find_offset(walks[i], idle, hm)
    # 11-11 사용자 "키가 커졌다 내려가거나 너무 과하게 흔들림" -> 앞뒤(dx)는 한 세트 한 값, 위아래(dy)는 가운데값 ±1칸까지만
    mx = int(np.median([o[0] for o in offs.values()])); my = int(np.median([o[1] for o in offs.values()]))
    log = []
    for i, walk in walks.items():
        dy = my + int(np.clip(offs[i][1] - my, -1, 1))
        if folder in BODY_SETS: dy = int(np.clip(body_offset(walk, idle, chin), -2, 2))
        out = fix_frame(walk, idle, hm, chin, mx, dy)
        Image.fromarray(out).save(os.path.join(d, f"walk_west_{i}.png"))
        Image.fromarray(out[:, ::-1].copy()).save(os.path.join(d, f"walk_east_{i}.png"))
        log.append(f"{mx:+d},{dy:+d}")
    print(folder, "턱", chin, "옮김", " ".join(log))


if __name__ == "__main__":
    for f in sys.argv[1:] or ["HeroHQ"] + [f"HeroHQ{g}" for g in range(1, 10)]:
        run(f)
