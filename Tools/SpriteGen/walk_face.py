"""11-12 사용자 "지금 일반 등급 얼굴형으로 나머지 등급도 다 적용": 손으로 고친 일반(HeroHQ) 옆 걷기 장마다의 얼굴 앞쪽 모양을
같은 번호 장의 다른 등급에 옮김. 눈 위치로 맞추고, 눈 줄 ~ 눈 아래 7줄 x 눈 앞 7칸 ~ 눈 칸 범위에서
대상 칸이 피부·테두리·빈칸일 때만 일반 모양(피부/테두리/빈칸)으로 바꿈(머리카락·투구·옷깃은 그대로). 피부는 대상 세트 피부색 중 가까운 색.
사용: python walk_face.py [HeroHQ1 ...]  편집 폴더 PE_EDIT(기본 _work/walkside_res)"""
import os, sys
import numpy as np
from PIL import Image

WORK = os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "gpt 그림", "고퀄", "자동", "_work")
EDIT = os.environ.get("PE_EDIT") or os.path.join(WORK, "walkside_res")
SRC = "HeroHQ"
OUTLINE = (12, 17, 43)


def kind(p):
    """0 빈칸 1 테두리 2 피부 3 그 밖."""
    if p[3] == 0: return 0
    r, g, b = int(p[0]), int(p[1]), int(p[2])
    if .299 * r + .587 * g + .114 * b < 60: return 1
    if r > 180 and r - b > 40 and 120 < g < 225: return 2
    return 3


GAME = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Sprites")


def blue_eye(x):
    """진한 파랑 눈(초록 낮음) - 고대 이마 보석(청록)·희귀 파란 갑옷과 구분."""
    x = x.astype(int)
    blue = (x[..., 3] > 0) & (x[..., 2] > 120) & (x[..., 1] < 120) & (x[..., 0] < 90) & (x[..., 2] - x[..., 1] > 50)
    r, g, b = x[..., 0], x[..., 1], x[..., 2]
    skin = (x[..., 3] > 0) & (r > 180) & (r - b > 40) & (g > 120) & (g < 225) & (b > 100)   # 금발(파랑 낮음) 제외
    near = skin.copy(); near[1:] |= skin[:-1]; near[:-1] |= skin[1:]; near[:, 1:] |= skin[:, :-1]; near[:, :-1] |= skin[:, 1:]
    return blue & near                                                # 피부 바로 옆 파랑 = 눈(투구 보석 제외)


def eye_of(a, near=None, r=6):
    """옆모습 눈 = 진한 파랑 칸 중 가장 앞(왼쪽)·위. near(게임 대기 눈) ±r칸 안에서만."""
    m = blue_eye(a)
    if near is not None:
        box = np.zeros_like(m); box[max(0, near[0] - r):near[0] + r + 1, max(0, near[1] - r):near[1] + r + 1] = True; m &= box
    else:
        m[:15] = False; m[40:] = False
    ys, xs = np.nonzero(m)
    if not len(ys): return None
    i = int(np.lexsort((ys, xs))[0]); return int(ys[i]), int(xs[i])


def idle_eye(folder):
    a = np.asarray(Image.open(os.path.join(GAME, folder, "idle_west_0.png")).convert("RGBA"))
    return eye_of(a)


def eye_or_head(a, folder):
    """눈을 못 찾으면(안 그렸거나 색이 다름) 머리 색 맞춤 위치(walk_head.find_offset) + 대기 눈 위치."""
    e0 = idle_eye(folder)
    e = eye_of(a, e0)
    if e is not None: return e, ""
    import walk_head as WH
    idle = np.asarray(Image.open(os.path.join(GAME, folder, "idle_west_0.png")).convert("RGBA"))
    hm, _, _ = WH.head_of(idle)
    dx, dy = WH.find_offset(a, idle, hm)
    return (e0[0] + dy, e0[1] + dx), "*"


def run(folder):
    log = []
    for i in range(8):
        s = np.asarray(Image.open(os.path.join(EDIT, SRC, f"walk_west_{i}.png")).convert("RGBA"))
        p = os.path.join(EDIT, folder, f"walk_west_{i}.png")
        t = np.asarray(Image.open(p).convert("RGBA")).copy()
        (es, m1), (et, m2) = eye_or_head(s, SRC), eye_or_head(t, folder)
        sk = np.array([t[y, x, :3] for y in range(max(0, et[0] - 4), min(64, et[0] + 8)) for x in range(64) if kind(t[y, x]) == 2]).astype(int)
        n = 0
        for dy in range(0, 8):
            for dx in range(-7, 1):
                sy, sx, ty, tx = es[0] + dy, es[1] + dx, et[0] + dy, et[1] + dx
                if not (0 <= sy < 64 and 0 <= sx < 64 and 0 <= ty < 64 and 0 <= tx < 64): continue
                ks, kt = kind(s[sy, sx]), kind(t[ty, tx])
                if ks == 3 or kt == 3 or ks == kt: continue
                if ks == 0: t[ty, tx] = 0
                elif ks == 1: t[ty, tx, :3] = OUTLINE; t[ty, tx, 3] = 255
                else:
                    c = s[sy, sx, :3].astype(int)
                    t[ty, tx, :3] = sk[((sk - c) ** 2).sum(1).argmin()] if len(sk) else c; t[ty, tx, 3] = 255
                n += 1
        Image.fromarray(t).save(p)
        Image.fromarray(t[:, ::-1].copy()).save(os.path.join(EDIT, folder, f"walk_east_{i}.png"))
        log.append(f"{n}{m1}{m2}")
    print(folder, "바꾼 칸(*=머리 위치로 맞춤)", log)


if __name__ == "__main__":
    for f in sys.argv[1:] or [f"HeroHQ{g}" for g in range(1, 10)]:
        run(f)
