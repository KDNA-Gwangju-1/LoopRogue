r"""11-29 사용자 "벽화 배경 보스패턴 이펙트 ui틀 학자 npc 부터 작업이어서하자": 남은 고퀄 그림을 GPT(Codex)로.
- murals : 벽화 보기 창 Murals/Mural1~10 (200x112) + 방 벽 소품 Props/Mural (96x32)  -> 한 장씩
- bgs    : Backgrounds/* (로비 4장·타이틀 320x180, 던전 바탕 128 반복 타일)       -> 한 장씩
- frames : UI 틀(Common 패널·버튼·카드틀·게이지틀, Room 지도 문, Title 버튼·로고)   -> 시트 3장
- tel    : 보스 패턴 경고 tel_*/telegraph (흰색+알파, 코드에서 색칠)               -> 검정 바탕에 흰/회색으로 -> 밝기 = 알파
사용: python scene_batch.py [murals,bgs,frames,tel | all]   결과: 바탕화면 gpt 그림\고퀄\자동\<벽화|배경|UI틀|경고>\
자르기·도트화·설치는 scene_export.py"""
import os, sys
from PIL import Image
import codex_batch as cb, fx_batch as FB

RES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources")
REF = os.path.join(cb.WORK, "scene_ref")
HERO = os.path.join(cb.HQ, "대기_앞", "01.png")
FLOOR = os.path.join(cb.OUT, "타일", "floor.png")
WALL = os.path.join(cb.OUT, "타일", "wallface.png")
OUT = {"murals": os.path.join(cb.OUT, "벽화"), "bgs": os.path.join(cb.OUT, "배경"), "frames": os.path.join(cb.OUT, "UI틀"),
       "tel": os.path.join(cb.OUT, "경고")}
DARK = ("It must fit a DARK FANTASY DUNGEON game: weathered, worn, aged materials, muted slightly desaturated colours, deep shadows, "
        "warm torch light. High-quality detailed pixel art (crisp square pixels, no blur, no anti-aliasing to the background), in the "
        "style of the high-quality pixel-art hero and dungeon tiles in the style references. No text, no letters, no numbers, no "
        "watermark, no border around the whole image. Use your image generation tool, do not write code.")

MURALS = [f"Murals/Mural{i}" for i in range(1, 11)] + ["Sprites/Props/Mural"]
BGS = {"Backgrounds/Lobby_Hub": "the lobby camp at night outside the dungeon: a tent, a campfire, a signpost, a weapon rack and crates in "
                                "front of a huge stone dungeon wall with a red glowing arched entrance, starry sky",
       "Backgrounds/Lobby_Gacha": "a dark dungeon treasure hall: brick wall with weapon racks and red/blue heraldic banners, wall "
                                  "torches, treasure chests and gold coins on the floor",
       "Backgrounds/Lobby_Potion": "an alchemist's potion shop: a wall of vertical BROWN WOODEN PLANKS (warm dark brown wood, NOT red, no "
                                   "paint stains), shelves full of colourful potion bottles on both sides, a bubbling green cauldron, "
                                   "candles and a small table, lit by warm candle light",
       "Backgrounds/Lobby_Casino": "an old underground gambling den: dark red patterned wallpaper with framed dice pictures and "
                                   "hanging lamps, wooden wainscot, a green felt card table edge at the bottom with chips and cards",
       "Backgrounds/TitleDungeon": "the dungeon entrance for the title screen: a stone brick wall with two pillars, hanging chains, two "
                                   "wall torches and a big dark arched doorway with stairs going down into darkness, a few bones on "
                                   "the floor",
       "Backgrounds/Dungeon": "a SEAMLESSLY TILEABLE square dark dungeon brick wall background (it repeats in all directions: the left "
                              "edge must continue the right edge and the top edge the bottom edge), with a vertical stone pillar, one "
                              "or two small wall torches and a hanging chain, very dark so it stays in the background"}
FRAMES = {
    "common": ["UI/Common/Panel", "UI/Common/Button", "UI/Common/Button_Hover", "UI/Common/Slot",
               "UI/Common/Card_C", "UI/Common/Card_B", "UI/Common/Card_A", "UI/Common/Card_S",
               "UI/Common/Band", "UI/Common/BarFrame", "UI/Common/GaugeFrame", "UI/Common/GaugeIconSlot", "UI/Common/RewindClock"],
    "room": ["UI/Room/Room", "UI/Room/Room_Selected", "UI/Room/RoomBoss", "UI/Room/RoomBoss_Selected", "UI/Room/RoomMarker"],
    "title": ["UI/Title/Logo", "UI/Title/Start", "UI/Title/Start_Hover", "UI/Title/Reset", "UI/Title/Reset_Hover",
              "UI/Title/Quit", "UI/Title/Quit_Hover"],
}
FRAME_GRID = {"common": (4, 4), "room": (3, 2), "title": (2, 4)}
FRAME_NOTE = {
    "common": ("These are 9-SLICE UI frames: the game stretches the flat middle part, so every frame needs a decorated BORDER all "
               "around and a FLAT, plain, evenly coloured middle (no pattern or picture in the middle). Same border thickness ratio as "
               "the old ones. In order: 1 window panel, 2 button, 3 button highlighted (warmer/brighter), 4 item slot, 5-8 card frames "
               "of rising rarity (iron grey, blue steel, purple, gold), 9 a horizontal band (dark strip with metal edges at top and "
               "bottom), 10 a small bar frame, 11 a gold gauge frame with blue gems on both sides, 12 a round gold icon slot, 13 an old "
               "brass clock face. Cells 14-16 are empty. Use iron, bronze, gold and dark stone; dark fantasy, not sci-fi."),
    "room": ("Dungeon map icons: 1 a wooden door in a stone arch, 2 the same door SELECTED (glowing warm light inside the arch), 3 a "
             "boss door (red, a skull in the middle), 4 the boss door SELECTED (bright glowing red), 5 a small gold arrow marker "
             "pointing down. Cell 6 is empty."),
    "title": ("Title screen: 1 the game logo text 'LOOP ROGUE' (LOOP in gold, ROGUE in silver, carved metal letters - this is the ONLY "
              "text allowed, spell it exactly L-O-O-P R-O-G-U-E), 2-7 wide flat menu buttons WITHOUT any text (the game writes the "
              "text on top): dark iron/stone bar with small torches or rivets at both ends, and the _Hover version of each (cells 3, 5, "
              "7) lit with warmer brighter colours. Cell 8 is empty."),
}
TEL = sorted(d for d in os.listdir(os.path.join(RES, "Effects")) if d.startswith("tel") and os.path.isdir(os.path.join(RES, "Effects", d)))


def up(path, k=None, bg=(40, 40, 52)):
    im = Image.open(os.path.join(RES, path + ".png")).convert("RGBA")
    k = k or max(1, 1024 // max(im.size))
    big = Image.new("RGBA", (im.width * k, im.height * k), (*bg, 255)); big.alpha_composite(im.resize(big.size, Image.NEAREST))
    return big


def ref(name, im):
    os.makedirs(REF, exist_ok=True); p = os.path.join(REF, name + ".png")
    if not os.path.exists(p): im.convert("RGB").save(p)
    return p


def grid_ref(name, items, c, r, bg=(40, 40, 52)):
    S = 256 if c * r > 6 else 340
    if name == "title": cw, ch = 512, 160
    else: cw = ch = S
    im = Image.new("RGBA", (c * cw, r * ch), (*bg, 255))
    for k, rel in enumerate(items):
        t = Image.open(os.path.join(RES, rel + ".png")).convert("RGBA")
        z = min((cw - 30) / t.width, (ch - 30) / t.height); t = t.resize((max(1, round(t.width * z)), max(1, round(t.height * z))), Image.NEAREST)
        im.alpha_composite(t, ((k % c) * cw + (cw - t.width) // 2, (k // c) * ch + (ch - t.height) // 2))
    return ref(name, im), cw, ch


def jobs(kinds):
    styles = [HERO, FLOOR, WALL]
    for kind in kinds:
        out = OUT[kind]
        if kind == "murals":
            for rel in MURALS:
                n = os.path.basename(rel) + ("_prop" if "Props" in rel else "")
                im = up(rel); w, h = im.size
                prompt = (f"Image 1 is an old low-resolution pixel-art STORY MURAL from a dungeon game ({w}x{h}, wide). Redraw it as ONE "
                          f"wide image with exactly the same aspect ratio, the same composition and the same figures, objects, poses and "
                          f"positions (a knight in blue armour with a red cape, etc. - keep everything that is in it, add nothing new), as "
                          f"a high-quality detailed pixel-art mural PAINTED ON an old cracked sandstone dungeon wall, faded paint, inside "
                          f"the same thin gold frame along the image edges. Images 2-4 are style references. " + DARK)
                yield os.path.join(out, n + ".png"), prompt, [ref("mural_" + n, im)] + styles
        elif kind == "bgs":
            for rel, what in BGS.items():
                n = os.path.basename(rel); im = up(rel); w, h = im.size
                prompt = (f"Image 1 is an old low-resolution pixel-art BACKGROUND ({'square' if w == h else '16:9 wide'}) from a dungeon game: "
                          f"{what}. Redraw it as ONE image with exactly the same aspect ratio and the same layout (every object in the "
                          f"same place, same size, same lighting), as a high-quality detailed pixel-art background. It is a background "
                          f"behind UI and characters, so keep it calm and fairly dark. Images 2-4 are style references. " + DARK)
                yield os.path.join(out, n + ".png"), prompt, [ref("bg_" + n, im)] + styles
        elif kind == "frames":
            for n, items in FRAMES.items():
                c, r = FRAME_GRID[n]; p, cw, ch = grid_ref("frame_" + n, items, c, r)
                prompt = (f"Image 1 shows old low-resolution pixel-art UI pieces from a dungeon game in a {c}x{r} grid of equal cells "
                          f"(each cell {cw}:{ch}) on a dark grey background (the grey is not part of them), read left to right, top to "
                          f"bottom. Redraw ALL of them as ONE image with exactly the same {c}x{r} grid, order, shapes and proportions "
                          f"(each piece centred in its cell with some margin, same width:height ratio as the old piece), as high-quality "
                          f"detailed pixel art. {FRAME_NOTE[n]} The background of the whole image must be flat solid pure magenta "
                          f"(#FF00FF) everywhere outside the pieces (no gradient, no grid lines), and the pieces must not use that colour. "
                          f"Images 2-4 are style references. " + DARK)
                yield os.path.join(out, n + ".png"), prompt, [p] + styles
        elif kind == "tel":
            for n in TEL:
                fs = FB.frames(n); w, h = fs[0].size; c, r = FB.layout(len(fs), w, h); k = 256 // max(w, h)
                im = Image.new("RGBA", (c * w * k, r * h * k), (0, 0, 0, 255))
                for i, f in enumerate(fs):
                    im.alpha_composite(f.resize((w * k, h * k), Image.NEAREST), ((i % c) * w * k, (i // c) * h * k))
                prompt = (f"Image 1 is an old low-resolution pixel-art boss ATTACK WARNING marker ('{n}', {len(fs)} animation frames in a "
                          f"{c}x{r} grid of equal square cells, read left to right, top to bottom). Each cell is ONE floor tile that will "
                          f"light up before a boss attack; the game colours it red/orange later, so it is drawn ONLY in white and greys "
                          f"on pure black. Redraw it as ONE image with exactly the same {c}x{r} grid and frame order, same symbol, same "
                          f"shapes and positions in every frame, same animation, as high-quality detailed pixel art: crisp glowing white "
                          f"magic lines and runes, carved/engraved look like a magic circle on a dungeon floor tile, dim grey fill where "
                          f"the old one is dim grey, bright white where it is white. STRICT: ONLY pure black (#000000), greys and white - "
                          f"no colour at all; black means transparent. No grid lines between cells, no text, no numbers. Use your image "
                          f"generation tool, do not write code.")
                yield os.path.join(out, n + ".png"), prompt, [ref("tel_" + n, im)]


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    kinds = list(OUT) if mode == "all" else mode.split(",")
    cb.log(f"장면 시작 {kinds} ({cb.quota_text()})")
    for job in jobs(kinds):
        if cb.run(*job) is None:
            break
    cb.log("장면 끝")
