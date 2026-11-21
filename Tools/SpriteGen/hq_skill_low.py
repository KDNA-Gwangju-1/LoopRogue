# 고퀄 스킬 이펙트 일반~영웅(11-06): pixelfx 등급색만. 회전 = spin-ring(위에서 본 고리), 대시 = dash-streak(찌르기 속도선을 길게),
# 십자 = 고리 + 상하좌우 2칸까지 뻗는 속도선 4개. 그림 1px = 용사 그림 1px(한 칸 51.2px), 30fps
import os, json, subprocess, numpy as np
from PIL import Image
S = os.path.dirname(os.path.abspath(__file__))
PFX = r"C:\Users\dignw\pixel-tools\pixelfx"; NODE = r"C:\Users\dignw\pixel-tools\node\node.exe"
CELL = 51.2
RAMPS = [("3a4258", "7e8aa8", "c4d0e8", "ffffff"), ("5a606e", "a0a6b6", "dce2ec", "ffffff"), ("2846a0", "3c6edc", "78aaff", "f0f6ff"),
         ("462878", "8250c8", "c8a0ff", "f5ebff"), ("965a14", "e6a028", "ffdc6e", "fffadc"), ("460a14", "a01e28", "f04646", "ffd8d0"),
         ("146464", "28aaa0", "6ee6d6", "dcfffa"), ("281e6e", "3c2896", "b496ff", "ffe6fa"), ("be963c", "f0d278", "fff5c8", "ffffff"), ("b0288c", "ff3cc8", "6ef0e2", "ffffff")]
H = lambda s: np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], float)
hx = lambda c: "#%02x%02x%02x" % tuple(int(round(v)) for v in np.clip(c, 0, 255))


def pal(g):
    d, m, l, c = map(H, RAMPS[g])
    return [hx(d * .6), hx(d), hx(m), hx((m + l) / 2), hx(l), hx(c), "#ffffff"], ["#ffffff", hx(c), hx(l), hx(m)]


def render(fx, opts, name):
    out = os.path.join(S, "skill", "pfx", name)
    r = subprocess.run([NODE, "tools/render.mjs", fx, "--sheet", "--no-actor", "--bg", "dark", "--out", out, "--opts", json.dumps(opts)], cwd=PFX, capture_output=True, text=True)
    if r.returncode: raise SystemExit(r.stderr[-600:])
    nm = fx.split("/")[-1]; base = os.path.join(out, *fx.split("/"))
    j = json.load(open(os.path.join(base, nm + ".json"))); sh = Image.open(os.path.join(base, nm + ".png")).convert("RGBA")
    return [sh.crop((f["frame"]["x"], f["frame"]["y"], f["frame"]["x"] + f["frame"]["w"], f["frame"]["y"] + f["frame"]["h"])) for f in j["frames"]], j["meta"]["pixelfx"]["pivot"]


def save(frames, name):
    while len(frames) > 3 and frames[-1].getbbox() is None: frames.pop()
    out = os.path.join(S, "skill", "out", name); os.makedirs(out, exist_ok=True)
    for fn in os.listdir(out): os.remove(os.path.join(out, fn))
    for i, fr in enumerate(frames):
        if fr.getbbox() is None: fr.putpixel((0, 0), (0, 0, 0, 1))
        fr.save(os.path.join(out, f"{i}.png"))
    return len(frames)


def ring(g):
    p, sp = pal(g)
    frs, pv = render("melee/spin-ring", {"palette": p, "sparks": sp, "radius": 60, "sy": 0.82, "width": 18, "seed": 3 + g}, f"ring{g}")
    return frs                                              # 224판, 가운데(112,112) = 용사 몸 가운데


def streak(g, reach, tag):
    p, sp = pal(g)
    frs, pv = render("melee/dash-streak", {"palette": p, "sparks": sp, "reach": reach, "seed": 4 + g}, f"{tag}{g}")
    return frs, pv                                          # 시작점 = 원점 + (9, -20), 끝 = 시작 + reach


def dash(g, n=3):
    # 대시 거리별 판(1~4칸, 11-06 사용자 "각각 그리는 게 맞을 듯"): 가로 = n칸 길 + 양옆 0.3칸, 가운데 = 길 가운데. 게임은 늘이지 않고 그대로, 길 방향으로 돌린다
    R = int(n * CELL); frs, pv = streak(g, R, f"dash{n}_")
    sx, sy = pv[0] + 9, pv[1] - 20; pad = int(0.3 * CELL); W = R + 2 * pad; Hh = 64
    return [f.crop((sx - pad, sy - Hh // 2, sx - pad + W, sy + Hh // 2)) for f in frs]


def cross(g):
    C = 288; HC = C // 2
    rf = ring(g); af, pv = streak(g, int(1.85 * CELL), "arm")
    out = []
    for i in range(max(len(rf), len(af) + 4)):
        can = Image.new("RGBA", (C, C))
        if i < len(rf): can.alpha_composite(rf[i], (HC - 112, HC - 112))
        k = i - 4                                           # 고리가 닫힐 즈음 네 방향으로 뻗음
        if 0 <= k < len(af):
            a = af[k]; ox, oy = pv[0] + 9 - 12, pv[1] - 20    # 가운데에서 조금 앞(12px 뒤에서 시작)
            for rot in (0, 90, 180, 270):
                big = Image.new("RGBA", (C, C)); big.alpha_composite(a, (HC - ox, HC - oy))
                can.alpha_composite(big.rotate(rot, resample=Image.NEAREST, center=(HC, HC)))
        out.append(can)
    return out


if __name__ == "__main__":
    import sys
    gs = [int(x) for x in (sys.argv[1:] or ["0", "1", "2", "3"])]
    for g in gs:
        print(g, "spin", save(ring(g), f"hq_spin_g{g}"), "cross", save(cross(g), f"hq_spin_cross_g{g}"),
              "dash", [save(dash(g, n), f"hq_dash_g{g}_{n}") for n in (1, 2, 3, 4)])
