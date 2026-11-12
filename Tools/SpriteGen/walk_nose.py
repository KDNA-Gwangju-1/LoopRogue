"""11-12 사용자 "옆 걷기 중간에 코 삐져나오는 거 다 지워": 새 옆 걷기(walk_sheet.py) 장마다
머리 자리를 대기 옆 0장에 맞춘 뒤(walk_head.find_offset), 눈 줄 ~ 턱 줄에서 대기 얼굴 앞선보다 앞(왼쪽)으로 튀어나온 칸을 지우고
새 앞끝 칸이 테두리가 아니면 테두리 색으로. walk_west 고치고 walk_east는 뒤집기.
사용: python walk_nose.py [HeroHQ ...] (기본 10세트)  HQ_RES = 폴더(기본 게임 폴더), 대기 그림은 게임 폴더 것"""
import os, sys
import numpy as np
from PIL import Image
import walk_head as WH

GAME = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Sprites")
RES = os.environ.get("HQ_RES") or GAME


def eye_of(img):
    """walk_head.eye_of보다 조금 넓게(일반 세트 눈 32,60,148)."""
    a = img.astype(int)
    m = (a[..., 3] > 0) & (a[..., 2] > 130) & (a[..., 0] < 110) & (a[..., 2] - a[..., 0] > 80)
    ys, xs = np.nonzero(m[15:40]); ys = ys + 15
    if not len(ys): return None
    i = int(np.argmin((ys - 28) ** 2 + (xs - 28) ** 2)); return int(ys[i]), int(xs[i])


def lum(c): return c[..., :3].astype(int) @ np.array([.299, .587, .114])


def face_edge(a, y):
    """y줄 얼굴 앞끝 = 맨 왼쪽 피부 칸(없으면 None)."""
    r, g, b = (a[y, :, k].astype(int) for k in range(3))
    sk = (a[y, :, 3] > 0) & (r > 180) & (r - b > 40) & (g > 120) & (g < 225)
    xs = np.nonzero(sk)[0]
    return int(xs.min()) if len(xs) else None


def tidy_face(a, ey):
    """11-12 사용자 "검은 도트 한 칸 삐져나온 것 지우고, 각진 턱 한 칸을 검은색으로".
    1) 눈 위 2줄 ~ 눈 아래 9줄: 맨 앞 칸이 검정이고 위·아래 줄 앞끝보다 한 칸 이상 앞이면 지움(바로 뒤도 검정일 때만)
    2) 눈 아래 피부가 있는 마지막 줄(턱) 맨 앞 피부 칸 -> 테두리 색(턱 모서리 둥글게)"""
    n = 0
    def left(y):
        xs = np.nonzero(a[y, :, 3])[0]; return int(xs.min()) if len(xs) else None
    for y in range(max(1, ey - 2), min(63, ey + 10)):
        L, U, D = left(y), left(y - 1), left(y + 1)
        if L is None or U is None or D is None: continue
        if L < min(U, D) and lum(a[y, L]) < 60 and a[y, L + 1, 3] and lum(a[y, L + 1]) < 60:
            a[y, L] = 0; n += 1
    chin = None
    for y in range(ey + 1, min(64, ey + 9)):
        if face_edge(a, y) is not None: chin = y
    if chin is not None:
        x = face_edge(a, chin)
        if x is not None:
            a[chin, x, :3] = (12, 17, 43); n += 1
    return n


def run(folder):
    idle = np.asarray(Image.open(os.path.join(GAME, folder, "idle_west_0.png")).convert("RGBA"))
    e0 = eye_of(idle)
    if e0 is None: print(folder, "눈 못 찾음"); return
    hm, top, chin = WH.head_of(idle)
    log = []
    for i in range(8):
        p = os.path.join(RES, folder, f"walk_west_{i}.png")
        a = np.asarray(Image.open(p).convert("RGBA")).copy()
        dx, dy = WH.find_offset(a, idle, hm)
        ey = e0[0] + dy
        # 눈 줄(과 바로 위) 얼굴 앞끝 = 기준. 그 아래 눈+1 ~ 턱 줄에서 기준보다 앞으로 나온 피부·테두리 = 코 -> 지움
        refs = [x for x in (face_edge(a, ey), face_edge(a, ey - 1)) if x is not None]
        if not refs: log.append("?"); continue
        lim = min(refs)
        n = 0
        for ty in range(ey + 1, min(64, chin + dy + 1)):
            fe = face_edge(a, ty)
            if fe is None or fe >= lim: continue
            cut = lim - 1                                            # 11-12 사용자 "한 칸 들어갔어, 앞으로": 테두리 칸(기준 바로 앞)은 남김
            xs = np.nonzero(a[ty, :cut, 3])[0]
            if not len(xs): continue
            seg = a[ty, xs].astype(int)
            hair = (seg[:, 0] > 150) & (seg[:, 1] > 90) & (seg[:, 2] < 120) & (seg[:, 0] - seg[:, 2] > 80) & (seg[:, 1] > 150)
            if hair.any(): continue                                  # 앞머리는 그대로
            a[ty, :cut] = 0; n += len(xs)
            a[ty, cut, :3] = (12, 17, 43); a[ty, cut, 3] = 255         # 새 앞끝 = 눈 줄과 같은 자리에 테두리
        t = tidy_face(a, ey)
        Image.fromarray(a).save(p)
        Image.fromarray(a[:, ::-1].copy()).save(os.path.join(RES, folder, f"walk_east_{i}.png"))
        log.append(f"{n}+{t}")
    print(folder, "지운 칸(장마다)", log)


if __name__ == "__main__":
    for f in sys.argv[1:] or ["HeroHQ"] + [f"HeroHQ{g}" for g in range(1, 10)]:
        run(f)
