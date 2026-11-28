"""11-12 사용자 "초월 신발 중간에 곰팡이 핀 것 같은 게 있음": 새 옆 걷기(walk_sheet.py) 장화에 섞인 연보라·회색 점을
대기 옆 0장 장화 색(무릎 아래 갈색들) 중 가장 가까운 색으로 맞춤. 보라 갑옷(파랑이 빨강보다 25+ 큼)·빨간 망토·검은 테두리는 그대로.
사용: python walk_boots.py [HeroHQ7 ...]  (HQ_RES = 폴더, 기본 게임 폴더) - walk_west 고치고 walk_east는 뒤집기"""
import os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.environ.get("HQ_RES") or os.path.join(HERE, "..", "..", "Assets", "Resources", "Sprites")
SETS = ["HeroHQ7"]
TOP = 48                                         # 이 줄부터 아래 = 장화(64칸 기준, 발바닥 58)


def boot_mask(a, top):
    rgb = a[..., :3].astype(int); m = np.zeros(a.shape[:2], bool); m[top:] = True
    lum = rgb @ np.array([.299, .587, .114])
    red = (rgb[..., 0] > 150) & (rgb[..., 0] - rgb[..., 1] > 70)          # 빨간 망토 끝·금장식은 그대로
    return m & (a[..., 3] > 0) & (rgb[..., 2] - rgb[..., 0] <= 25) & (lum >= 30) & ~red


def run(folder):
    d = os.path.join(RES, folder)
    idle = np.asarray(Image.open(os.path.join(d, "idle_west_0.png")).convert("RGBA"))
    pal = np.unique(idle[boot_mask(idle, TOP + 1)][:, :3], axis=0).astype(int)
    n = 0
    for i in range(8):
        p = os.path.join(d, f"walk_west_{i}.png")
        a = np.asarray(Image.open(p).convert("RGBA")).copy()
        m = boot_mask(a, TOP)
        px = a[m][:, :3].astype(int)
        near = pal[((px[:, None] - pal[None]) ** 2).sum(-1).argmin(1)]
        n += int((near != px).any(1).sum())
        a[m, :3] = near
        Image.fromarray(a).save(p)
        Image.fromarray(a[:, ::-1].copy()).save(os.path.join(d, f"walk_east_{i}.png"))
    print(folder, "장화 색", len(pal), "가지로, 바꾼 칸", n)


if __name__ == "__main__":
    for f in sys.argv[1:] or SETS:
        run(f)
