# 분신 대시(대각선) 전용 그림 hq_dash_diag_g{등급}_{1~4칸}(11-07 사용자): 오른쪽 위(/) 방향으로 그린 정사각 판.
# 길 = 대각 n칸(n*1.414칸), 판 가운데 = 길 가운데. 게임은 늘이지 않고 (길 각도 - 45도)로 돌림 = 90도 단위라 픽셀이 안 깨짐.
# 속도선 = 직선 대시 그림(같은 길이)을 45도 돌림(가는 선이라 계단이 깔끔), 전설 발톱·앞발은 돌리지 않은 원래 그림(/ \ 번갈아, 길을 가로지름)
import math, numpy as np
from PIL import Image
import skill_low as SL, skill_legend as LG
CELL = SL.CELL; R2 = math.sqrt(2)


def base_rot(g, n):
    L = n * R2
    frs = SL.dash(g, L)                                       # 가로 = L칸 + 0.6칸, 세로 64
    W, Hh = frs[0].size
    C = int(math.ceil((W + Hh) / R2 + 130)); C += C % 2
    out = []
    for f in frs:
        r = f.rotate(45, resample=Image.NEAREST, expand=True)
        can = Image.new("RGBA", (C, C)); can.alpha_composite(r, ((C - r.width) // 2, (C - r.height) // 2)); out.append(can)
    return out, C


def low(g, n):
    return base_rot(g, n)[0]


def legend(n):
    base, C = base_rot(4, n)
    L = n * R2 * CELL; c0 = C / 2
    pt = lambda u: (c0 + u / R2, c0 - u / R2)                # 길 위 거리 u(가운데 0) -> 화면
    XS = [0] + list(np.linspace(1.35, n - 0.7, n - 1)) if n > 1 else [0]
    CL, CX, CY = LG.claw_frames(1.0)
    CLF = [im.transpose(Image.FLIP_LEFT_RIGHT) for im in CL]; CXF = CL[0].width - CX
    rng = np.random.default_rng(7 + n); embers = []; out = []
    for f in range(max(len(base), 2 + len(CL))):
        can = Image.new("RGBA", (C, C))
        if f < len(base): can.alpha_composite(base[f])
        for i in range(n):
            x, y = pt(-L / 2 + XS[i] * R2 * CELL); k = f - i
            if k < 0: continue
            frs, cx_ = (CL, CX) if i % 2 == 0 else (CLF, CXF)
            if k < len(frs): can.alpha_composite(frs[k], (int(x - cx_), int(y - CY)))
            if k < 8:                                         # 앞발이 발톱 자국 방향(위 -> 아래)으로 휘두름
                sg = -1 if i % 2 == 0 else 1
                dx_, dy_ = sg * math.sin(math.radians(15)), math.cos(math.radians(15))
                e = 1 - (1 - min(1, k / 2.5)) ** 2
                px_, py_ = x + dx_ * (-20 + 38 * e), y + dy_ * (-20 + 38 * e)
                ang = math.degrees(math.atan2(dy_, dx_)) - sg * (30 - 40 * e)
                LG.paste_c(can, LG.paw_img(ang, 0.75, max(0, (k - 3) / 4), f), px_, py_)
                if k == 0:
                    for _ in range(7): embers.append([x + rng.uniform(-9, 9), y + rng.uniform(-8, 8), rng.uniform(-.6, .3), rng.uniform(-1.3, -.2), int(rng.integers(3, 7))])
        can = LG.embers_step(can, embers)
        out.append(can)
    return out


if __name__ == "__main__":
    for n in (1, 2, 3, 4):
        for g in (0, 1, 2, 3): SL.save(low(g, n), f"hq_dash_diag_g{g}_{n}")
        print(n, SL.save(legend(n), f"hq_dash_diag_g4_{n}"))
