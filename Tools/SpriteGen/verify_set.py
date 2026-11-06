# 사용: python verify_set.py HeroHQ4 몸_05_전설 key1 key2 ...  -> 원본|도트 대조표 + 잘림 검사
import sys, os, numpy as np
from PIL import Image, ImageDraw, ImageFont
FONT = ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 14)
RES = r"C:\Users\dignw\OneDrive\Documents\GitHub\LoopRogue\Assets\Resources\Sprites"
DESK = r"C:\Users\dignw\OneDrive\Desktop\고퀄 스프라이트"
s, folder, keys = sys.argv[1], sys.argv[2], sys.argv[3:]
K = {"idle": "대기", "walk": "걷기", "attack": "공격", "dash": "대쉬"}
D = {"south": "앞", "west": "옆", "north": "뒤"}
anch = {}
for l in open(os.path.join(RES, s, "weapon_anchor.txt"), encoding="utf-8"):
    p = l.split()
    if len(p) >= 5: anch[p[0]] = (float(p[1]), float(p[2]), int(p[3]), p[4] == "1")
# 잘림: 테두리 1칸에 불투명 도트
clip = []
for f in os.listdir(os.path.join(RES, s)):
    if f.endswith(".png"):
        a = np.array(Image.open(os.path.join(RES, s, f)).convert("RGBA"))[:, :, 3]
        if a[0].any() or a[-1].any() or a[:, 0].any() or a[:, -1].any(): clip.append(f)
print("잘림:", len(clip), clip[:20])
def comp(key):
    im = Image.new("RGBA", (96, 96), (205, 205, 210, 255))
    body = Image.open(os.path.join(RES, s, key + ".png")).convert("RGBA")
    x, y, k, behind = anch[key]
    a = Image.open(os.path.join(RES, "HeroHQSword0", f"a_{k}.png")).convert("RGBA")
    g = Image.open(os.path.join(RES, "HeroHQSword0", f"g_{k}.png")).convert("RGBA")
    pos = (int(round(x + 16 - a.width / 2)), int(round(y + 16 - a.height / 2)))
    L = lambda src, p: (lambda c: (c.paste(src, p, src), c)[1])(Image.new("RGBA", (96, 96), (0, 0, 0, 0)))
    bl, al, gl = L(body, (16, 16)), L(a, pos), L(g, pos)
    for l in [gl] + ([al, bl] if behind else [bl, al]): im.alpha_composite(l)
    return im.convert("RGB").resize((288, 288), Image.NEAREST)
rows = []
for key in keys:
    kind, d, i = key.split("_")
    src = os.path.join(DESK, folder, "원본", "회전베기" if kind == "spin" else f"{K[kind]}_{D[d]}", f"{int(i)+1:02}.png")
    o = Image.open(src).convert("RGB"); o.thumbnail((288, 288))
    row = Image.new("RGB", (600, 310), "white"); row.paste(o, (0, 20)); row.paste(comp(key), (305, 20))
    ImageDraw.Draw(row).text((5, 3), key + "   (왼: 원본 / 오른: 게임 도트)", fill="black", font=FONT)
    rows.append(row)
if rows:
    sheet = Image.new("RGB", (600 * 2, 310 * ((len(rows) + 1) // 2)), "white")
    for n, r in enumerate(rows): sheet.paste(r, (600 * (n % 2), 310 * (n // 2)))
    out = os.path.join(DESK, f"바로잡은장_확인_{folder}.png"); sheet.save(out); print(out)
