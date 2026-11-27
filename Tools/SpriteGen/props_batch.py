r"""11-28 사용자 "나머지 고퀄작업없는것도 싹다 진행해": 맵 소품(Resources/Sprites/Props, 32칸 = 한 칸) 고퀄을 GPT 묶음 시트로.
사용: python props_batch.py [all|sheetA,sheetB]   결과: 바탕화면 gpt 그림\고퀄\자동\소품\<시트>.png -> props_export.py 가 칸별로 자름.
벽화(Mural*)·색 벽화·빛줄기는 그림 내용이 퍼즐 정답/코드 연출이라 옛 그림 유지."""
import os, sys
from PIL import Image
import codex_batch as cb

PROPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Sprites", "Props")
OUT = os.path.join(cb.OUT, "소품")
REF = os.path.join(cb.WORK, "props_ref")
HERO = os.path.join(cb.HQ, "대기_앞", "01.png")
TILE = os.path.join(cb.OUT, "타일", "floor.png")

OBJ = ("Each cell holds ONE separate object, centred, with clear plain white space around it - objects never touch each other or "
       "the cell edges. Plain solid white background everywhere outside the objects. No floor under the objects, no cast shadows "
       "on the background, no text, no numbers, no grid lines, no borders.")
FULL = ("Each cell is a full square floor tile that fills its whole cell edge to edge (no white margin inside the cell), cells side "
        "by side with no gaps. No text, no numbers, no grid lines drawn.")
STYLE = ("Style: high-quality detailed pixel art matching the hero in the style reference (same pixel size relative to the cell, "
         "outline darkness and shading), top-down 3/4 view like classic 2D RPG dungeons, light from the top. Keep every item's "
         "colours, shape and meaning the same as the old sprite in the layout reference, just much more detailed. "
         "Use your image generation tool, do not write code.")

# 시트: (이름들, (열, 행), 종류 obj|full)
SHEETS = {
    "objA": (["Altar_Lit", "Altar_Unlit", "Bomb", "Decoy", "DroppedCoins_0", "DroppedCoins_1", "Event_BlessingAltar", "Event_CursedChest",
              "Event_HealingSpring", "Event_TreasureChest", "Glow_Mushroom", "Golem_Crystal_0", "Golem_Crystal_1", "Golem_Rubble", "Key", "Lich_Jar"], (4, 4), "obj"),
    "objB": (["Light_Helper", "Light_Mirror", "Light_Mirror_Face", "Mole_Bomb", "Mole_Gold", "Mole_Hit", "Mole_Hole", "Mole_Normal",
              "Ogre_Hole", "Slime_Puddle", "Soul_Target", "Spirit_Candle", "Torch", "Trap", "Floor_Crack_0", "Floor_Crack_1"], (4, 4), "obj"),
    "objC": (["Lava_Crack", "Puzzle_Gem", "Puzzle_Gem_Emerald", "Puzzle_Gem_Ruby", "Puzzle_Gem_Sapphire", "Puzzle_Gem_Topaz", "Puzzle_GoalPad",
              "Puzzle_Lever_Off", "Puzzle_Lever_On", "Puzzle_Spike_1", "Puzzle_Spike_2", "Puzzle_Spike_3", "Boar_Pillar"], (4, 4), "obj"),
    "fullA": (["Puzzle_Block", "Puzzle_Block_0", "Puzzle_Block_1", "Puzzle_Block_2", "Puzzle_Block_3", "Puzzle_Block_4",
               "Puzzle_Slab", "Puzzle_Slab_0", "Puzzle_Slab_1", "Puzzle_Slab_2", "Puzzle_Slab_3", "Puzzle_Slab_4",
               "Puzzle_Ice", "Puzzle_Mud", "Puzzle_SweepCover", "Puzzle_SweepDug"], (4, 4), "full"),
    "fullB": (["Puzzle_Rune_0", "Puzzle_Rune_1", "Puzzle_Rune_2", "Puzzle_Rune_3", "Puzzle_Rune_4", "Puzzle_Rune_5", "Puzzle_Rune_6",
               "Puzzle_ExitTile", "Puzzle_ColorPlate", "Puzzle_TrapPlate"], (4, 3), "full"),
}
# 11-28 사용자 "미니게임 사용하는 룬하고 상자 기호같은건 새로 그리라고": 옛 그림 따라 그리지 말고 새 디자인. 게임이 색을 입히므로(renderer.color) 밝은 흰 돌.
SHEETS["blocks"] = (["Puzzle_Block", "Puzzle_Block_0", "Puzzle_Block_1", "Puzzle_Block_2", "Puzzle_Block_3", "Puzzle_Block_4",
                     "Puzzle_Slab", "Puzzle_Slab_0", "Puzzle_Slab_1", "Puzzle_Slab_2", "Puzzle_Slab_3", "Puzzle_Slab_4"], (4, 3), "full")
SHEETS["runes"] = (["Puzzle_Rune_0", "Puzzle_Rune_1", "Puzzle_Rune_2", "Puzzle_Rune_3", "Puzzle_Rune_4", "Puzzle_Rune_5", "Puzzle_Rune_6"], (4, 2), "full")
# 11-28 사용자 "좀더 게임에 어울리게": 하얀 UI 아이콘 같던 1차(_1차) -> 던전 바닥(그림 3)과 같은 회보라 돌 + 기호만 밝게 새긴 문양.
# 게임이 색을 곱하므로 돌(중간 밝기)은 은은하게, 밝은 기호는 선명한 색으로 빛나 보임.
DUNGEON = ("It must look like it belongs in the dark dungeon of image 3: the SAME grey-purple stone colour, texture, chips and worn edges "
           "as those floor tiles (not white marble, not clean UI icons), but a bit lighter than the floor so it stands out. The carved "
           "symbol is filled with a bright, almost white, softly glowing inlay (like magic light inside the carving) - that is the only "
           "bright part. No other colours (the game tints the whole tile with one colour, so the glowing symbol becomes that colour).")
NEW = {
    "blocks": ("NEW DESIGN - do not copy the old art, only the symbol of each cell. Cells 1-6 are heavy PUSHABLE STONE BLOCKS (a chunky, "
               "worn, chipped dungeon stone cube seen from 3/4 top-down: top face plus a darker front face at the bottom, dark outline, "
               "drawn on plain white so it can be cut out) and cells 7-12 are flat PRESSURE PLATES set into the dungeon floor (a slightly "
               "raised square stone slab filling the cell, with a worn carved border). Each carries one carved ancient symbol in its "
               "centre: cell 1 = three horizontal lines, 2 = circle, 3 = triangle, 4 = square, 5 = diamond, 6 = cross; cell 7 = a small "
               "round dot, 8 = circle, 9 = triangle, 10 = square, 11 = diamond, 12 = cross. Block and plate symbols of the same shape must "
               "clearly match. " + DUNGEON),
    "runes": ("NEW DESIGN - do not copy the old letters. 7 square RUNE FLOOR TILES (a worn dungeon floor slab filling the cell), each with "
              "one large, bold, clearly different fantasy RUNE glyph carved into it (ancient magic runes made of straight and curved "
              "strokes, NOT latin letters, NOT numbers), easy to tell apart at a small size. The 8th cell is EMPTY (plain white). " + DUNGEON),
}
NOTE = {
    "objC": " The last 3 cells are EMPTY (plain white). Cells 10-12 are floor spikes at 3 growth stages (small, medium, fully raised); "
            "cell 13 is a tall stone pillar.",
    "objB": " Cells 7 and 9 are holes in the ground, cells 15-16 are dark cracks in the floor (draw them on white, no floor slab).",
    "fullB": " The last 2 cells are EMPTY (plain white). Runes 1-7 keep the exact same rune symbol as the old tile.",
}


def ref_strip(names, cols, rows, full):
    path = os.path.join(REF, f"{names[0]}_{cols}x{rows}.png")
    if os.path.exists(path):
        return path
    os.makedirs(REF, exist_ok=True)
    S = 256; im = Image.new("RGB", (cols * S, rows * S), (255, 255, 255))
    for k, n in enumerate(names):
        t = Image.open(os.path.join(PROPS, n + ".png")).convert("RGBA")
        k2 = (S if full else S - 48) / max(t.size); t = t.resize((max(1, int(t.width * k2)), max(1, int(t.height * k2))), Image.NEAREST)
        im.paste(t, ((k % cols) * S + (S - t.width) // 2, (k // cols) * S + (S - t.height) // 2), t)
    im.save(path)
    return path


def jobs(keys):
    for k in keys:
        names, (c, r), kind = SHEETS[k]
        prompt = (f"Draw ONE image: a {c}x{r} grid of equal square cells (exactly {c} cells wide and {r} cells tall) holding {len(names)} "
                  f"pixel-art dungeon props, in the same order as the layout reference (image 1, old low-resolution versions). "
                  f"{OBJ if kind == 'obj' else FULL}{NOTE.get(k, '')} Image 2 is the style reference.\n{STYLE}")
        if k in NEW:
            prompt = prompt.replace("Keep every item's colours, shape and meaning the same as the old sprite in the layout reference, just much more detailed. ", "") + " " + NEW[k]
        imgs = [ref_strip(names, c, r, kind == "full"), HERO]
        if kind == "full" and os.path.exists(TILE) :
            imgs.append(TILE); prompt += " Image 3 shows the new dungeon floor tiles; the stone tiles should sit nicely next to them."
        yield os.path.join(OUT, f"{k}.png"), prompt, imgs


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    ks = list(SHEETS) if mode == "all" else mode.split(",")
    cb.log(f"소품 시작 {ks} ({cb.quota_text()})")
    for job in jobs(ks):
        if cb.run(*job) is None:
            break
    cb.log("소품 끝")
