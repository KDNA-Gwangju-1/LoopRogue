# 사용: python sword_gifs.py attack_north  -> 몸 세트마다 그 동작 GIF를 칼 10등급으로(게임 그림 그대로 합성)
import sys, os
from PIL import Image
RES = r"C:\Users\dignw\OneDrive\Documents\GitHub\LoopRogue\Assets\Resources\Sprites"
OUT = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트"
NAMES = ["일반", "고급", "희귀", "영웅", "전설", "신화", "고대", "초월", "영원", "Unknown"]
SPEED = {"idle": 250, "walk": 120, "attack": 90, "dash": 70, "spin": 60}
g = sys.argv[1]; ONLY = sys.argv[2:]
for folder in sorted(os.listdir(OUT)):
    if not folder.startswith("몸_") or (ONLY and folder not in ONLY): continue
    n = int(folder[2:4]); s = "HeroHQ" if n == 1 else f"HeroHQ{n-1}"
    if not os.path.isdir(os.path.join(RES, s)): continue
    anch = {}
    for l in open(os.path.join(RES, s, "weapon_anchor.txt"), encoding="utf-8"):
        p = l.split()
        if len(p) >= 5: anch[p[0]] = (float(p[1]), float(p[2]), int(p[3]), p[4] == "1")
    for w, nm in enumerate(NAMES):
        seq = []
        for i in range(8):
            key = f"{g}_{i}"
            im = Image.new("RGBA", (96, 96), (205, 205, 210, 255))
            body = Image.open(os.path.join(RES, s, key + ".png")).convert("RGBA")
            x, y, k, behind = anch[key]
            a = Image.open(os.path.join(RES, f"HeroHQSword{w}", f"a_{k}.png")).convert("RGBA")
            gr = Image.open(os.path.join(RES, f"HeroHQSword{w}", f"g_{k}.png")).convert("RGBA")
            pos = (int(round(x + 16 - a.width / 2)), int(round(y + 16 - a.height / 2)))
            def L(src, p):
                c = Image.new("RGBA", (96, 96), (0, 0, 0, 0)); c.paste(src, p, src); return c
            bl, al, gl = L(body, (16, 16)), L(a, pos), L(gr, pos)
            for l in [gl] + ([al, bl] if behind else [bl, al]): im.alpha_composite(l)
            seq.append(im.convert("RGB").resize((576, 576), Image.NEAREST))
        out = os.path.join(OUT, folder, "GIF", "도트", f"{g}_{w+1:02}_{nm}.gif")
        seq[0].save(out, save_all=True, append_images=seq[1:], duration=SPEED[g.split("_")[0]] * 2, loop=0)
    print(folder, "완료")
