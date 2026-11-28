r"""11-28 UI 아이콘 고퀄(카드·아이콘·유물·가챠 장비·로비·카지노 기호). 틀 그림(창·버튼·게이지·9-slice)은 옛 것 유지.
시트 = 아이콘 16개(4x4), 단색 배경(자홍/초록) -> ui_export.py 가 자르고 옛 아이콘 자리·크기 x2 로.
사용: python ui_batch.py [all|시트,...]   결과: 바탕화면 gpt 그림\고퀄\자동\UI\<시트>.png"""
import os, sys, math, numpy as np
from PIL import Image
import codex_batch as cb, fx_batch as FB

UI = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "UI")
# 11-28 사용자 "고퀄모드는 좀 더 던전에 어울리게 새로그리게": UI_STYLE=dungeon 이면 어두운 던전 판타지 화풍으로 UI_던전 폴더에
DUNGEON = os.environ.get("UI_STYLE") == "dungeon"
OUT = os.path.join(cb.OUT, "UI_던전" if DUNGEON else "UI")
FLOOR = os.path.join(cb.OUT, "타일", "floor.png")
DSTYLE = (" STYLE CHANGE: the old icons look too bright, cute and generic. Redraw every icon so it clearly belongs in a DARK FANTASY "
          "DUNGEON game like image 3 (the game's dungeon floor): weathered, worn and aged materials - tarnished iron and bronze, "
          "chipped stone, old bone, cracked leather, yellowed parchment, smoky glass, dull gems; deeper and more muted, slightly "
          "desaturated colours with darker shadows, scratches and grime, lit from the top-left like a torch. No glossy cartoon look, "
          "no pastel, no clean modern UI style. Keep the main colour of each icon (red stays red, gold stays gold) but darker and "
          "grittier, and keep each icon instantly readable at a small size with a strong silhouette and a dark outline.")
REF = os.path.join(cb.WORK, "ui_ref")
HERO = os.path.join(cb.HQ, "대기_앞", "01.png")
GROUPS = {"Cards": "skill/upgrade CARD icons", "Icons": "game item, skill, trait and gimmick ICONS", "Relics": "magic RELIC item icons",
          "Gacha": "equipment / treasure chest icons", "Lobby": "lobby menu icons", "Casino": "slot machine symbols"}
SKIP = {"Gacha/Rays", "Gacha/Sparkle", "Casino/SlotFrame", "Casino/SlotFrame_Win"}


def all_icons():
    out = []
    for g in GROUPS:
        for r, _, fs in os.walk(os.path.join(UI, g)):
            for f in sorted(fs):
                if f.endswith(".png"):
                    rel = os.path.relpath(os.path.join(r, f), UI)[:-4].replace("\\", "/")
                    if rel not in SKIP: out.append(rel)
    return out


def sheets():
    s = {}
    for g in GROUPS:
        items = [i for i in all_icons() if i.startswith(g + "/")]
        for k in range(0, len(items), 16):
            s[f"{g}_{k // 16 + 1}"] = items[k:k + 16]
    return s


def load(rel):
    return Image.open(os.path.join(UI, rel + ".png")).convert("RGBA")


def ref_sheet(name, items, bg):
    path = os.path.join(REF, name + ".png")
    if not os.path.exists(path):
        os.makedirs(REF, exist_ok=True); S = 256
        im = Image.new("RGBA", (4 * S, 4 * S), (40, 40, 52, 255))
        for k, rel in enumerate(items):
            t = load(rel); z = (S - 40) / max(t.size); t = t.resize((max(1, round(t.width * z)), max(1, round(t.height * z))), Image.NEAREST)
            im.alpha_composite(t, ((k % 4) * S + (S - t.width) // 2, (k // 4) * S + (S - t.height) // 2))
        im.convert("RGB").save(path)
    return path


def jobs(sel):
    sh = sheets()
    for n in sel:
        items = sh[n]; bg = FB.bg_color([load(i) for i in items])
        bgname = "pure magenta (#FF00FF)" if bg == FB.MAGENTA else "pure green (#00FF00)"
        g = n.rsplit("_", 1)[0]
        prompt = (f"Image 1 shows {len(items)} old low-resolution pixel-art {GROUPS[g]} from a dungeon RPG, in a 4x4 grid of equal square "
                  f"cells on a dark grey background (the grey is not part of the icons), read left to right, top to bottom"
                  f"{'; the remaining cells are empty' if len(items) < 16 else ''}. Redraw ALL of them as ONE image with exactly the same "
                  f"4x4 grid and order, as high-quality detailed pixel-art icons in the style of image 2 (crisp pixels, clean dark "
                  f"outline, rich shading, readable at a small size). Keep each icon's subject, silhouette, main colours and meaning "
                  f"exactly the same (they must still be recognisable as the same icon), centred in its cell with some margin, just much "
                  f"more detailed and polished. Empty cells stay empty. The background of the whole image must be flat solid {bgname} "
                  f"everywhere outside the icons (no gradient, no texture, no grid lines, no borders, no text, no numbers), and the "
                  f"icons must not use that colour. Use your image generation tool, do not write code.")
        imgs = [ref_sheet(n, items, bg), HERO]
        if DUNGEON:
            prompt += DSTYLE; imgs.append(FLOOR)
        yield os.path.join(OUT, n + ".png"), prompt, imgs


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    sel = list(sheets()) if mode == "all" else mode.split(",")
    cb.log(f"UI 시작 {len(sel)}장 ({cb.quota_text()})")
    for job in jobs(sel):
        if cb.run(*job) is None:
            break
    cb.log("UI 끝")
