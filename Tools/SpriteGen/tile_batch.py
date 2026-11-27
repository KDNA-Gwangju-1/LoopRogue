"""11-28 사용자 "타일부터 하자": 고퀄 던전 타일(64칸 = 한 칸, 캐릭터와 같은 도트 크기)을 GPT(Codex)로 묶음 시트로 그린다.
사용: python tile_batch.py [pilot|all]
- 옛 타일(Resources/Tiles, 32칸)을 종류별로 크게 늘어놓은 '배치 참고'와 고퀄 용사 대기 그림('그림체 참고')을 같이 준다.
- 결과: 바탕화면 gpt 그림\\고퀄\\자동\\타일\\<종류>.png (한 장 = 칸 N개 격자). 자르기·도트화는 tile_export.py."""
import os, sys
from PIL import Image
import codex_batch as cb

TILES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Tiles")
OUT = os.path.join(cb.OUT, "타일")
REF = os.path.join(cb.WORK, "tile_ref")
HERO = os.path.join(cb.HQ, "대기_앞", "01.png")

STYLE = ("Style: high-quality detailed pixel art matching the character in the style reference image (same palette mood, same outline "
         "darkness, same pixel size relative to a 64x64 tile). Dark moody dungeon, light from the top, cool blue-grey stone. "
         "Top-down 3/4 view like classic 2D RPG dungeons. No characters, no text, no numbers, no grid lines drawn between the cells, "
         "no border around the image. Use your image generation tool, do not write code.")

# 종류: (옛 타일 이름들(배치 참고), 격자 열x행, 설명)
SETS = {
    "floor": ([f"Floor_{i}" for i in range(10)], (4, 4),
              "16 different dungeon FLOOR tiles seen from above: worn flagstone slabs of varying sizes with thin dark grout lines, "
              "subtle cracks, small chips and dust. All 16 must share the same stone color and brightness so they can be mixed freely "
              "on one floor, and EACH TILE MUST TILE SEAMLESSLY with its neighbours (stones may not be cut oddly at the edges - put "
              "grout lines along the tile edges). Keep them calm and low-contrast so characters stand out."),
    "wallface": ([f"WallFace_{i}" for i in range(6)] + ["WallFace_Torch", "WallCracked"], (4, 2),
                 "8 dungeon WALL FRONT tiles (the vertical brick face of a wall seen from the front, a thin lighter stone ledge along "
                 "the very top edge): row 1 = four plain brick variants (one with a few cracks, one with hanging moss at the top), "
                 "row 2 = a variant with a hanging rusty chain, a variant with a lit wall torch in an iron bracket (warm orange flame), "
                 "a badly cracked variant, a plain variant. Bricks must line up seamlessly left-right between all 8 tiles. Every decoration "
                 "(chain, torch, crack) must sit fully INSIDE its own cell, centred, never crossing a cell border."),
    "walltop": ([f"WallTop_{i}" for i in range(3)], (4, 1),
                "4 dungeon WALL TOP tiles (the flat top of a thick stone wall seen from above): the SAME grey-purple stone colour as the "
                "floor and the wall bricks, only slightly lighter - NOT blue, NOT icy, no glossy painterly highlights, flat pixel-art "
                "shading like the floor tiles. Each cell is exactly one square tile holding 1 to 4 large capstones with dark grout lines "
                "along all four tile edges, so the tile repeats seamlessly. The 4 cells are equal squares side by side."),
    # 11-28 사용자 "이끼랑 풀 쇠창살은 지우거나 다른걸로 교체": 0 이끼 -> 그을음, 5 풀 -> 부러진 화살, 6 쇠창살 -> 녹슨 쇠사슬
    "decor": ([f"FloorDecor_{i}" for i in range(12)], (4, 3),
              "12 dungeon FLOOR tiles with one small decoration each. The floor under every decoration must be EXACTLY the same "
              "rectangular flagstone slab style, colour and brightness as the plain floor tiles in image 3 (NOT round cobblestones), "
              "so these cells blend in when mixed with plain floor tiles. In this order: 1 a black scorch / burn mark, "
              "2 a thin crack running across the slabs, 3 scattered bones and a skull, 4 a shallow water puddle, 5 a few broken "
              "stone chips, 6 a broken arrow (shaft and arrowhead), 7 a rusty iron chain lying on the floor, 8 a dried blood stain, "
              "9 a cobweb in one corner, 10 broken clay pot shards, 11 small glowing pink mushrooms, 12 a melted candle. "
              "Decorations small and centred. NO moss, NO grass, NO floor grate."),
    # 11-28 남은 타일: 다음 방 문(ExitDoor, 게임에서 실제로 씀) + 옛 계단(대체용)
    "door": (["ExitDoor", "Stairs"], (2, 1),
             "2 tiles: 1 = the exit DOOR to the next room, set into a dungeon brick wall: a stone arched doorway whose wooden door "
             "stands half open with bright warm golden light pouring out (it must read clearly as 'the way forward' from far away); "
             "the area around the arch is transparent-looking plain dark wall. 2 = a stone staircase going down into darkness seen "
             "from above, inside a square stone frame. Both centred and filling most of their cell."),
}
PILOT = ["floor", "wallface"]


def ref_strip(names, cols, rows):
    """옛 타일을 격자로 크게(칸 256) - 배치 참고."""
    path = os.path.join(REF, "_".join(names[:1]) + f"_{cols}x{rows}.png")
    if os.path.exists(path):
        return path
    os.makedirs(REF, exist_ok=True)
    S = 256; im = Image.new("RGB", (cols * S, rows * S), (255, 255, 255))
    for k in range(cols * rows):
        t = Image.open(os.path.join(TILES, names[k % len(names)] + ".png")).convert("RGBA").resize((S, S), Image.NEAREST)
        im.paste(t, ((k % cols) * S, (k // cols) * S), t)
    im.save(path)
    return path


def jobs(kinds):
    for k in kinds:
        names, (c, r), what = SETS[k]
        prompt = (f"Draw ONE image: a {c}x{r} grid of square pixel-art tiles with NO gaps and NO lines between them "
                  f"(the image is exactly {c} cells wide and {r} cells tall, every cell the same square size). {what}\n"
                  f"Image 1 is the layout reference (old low-resolution versions of these tiles, same order) - redraw them at much "
                  f"higher detail, keep what each cell shows. Image 2 is the style reference.\n{STYLE}")
        imgs = [ref_strip(names, c, r), HERO]
        if k == "decor" and os.path.exists(os.path.join(OUT, "floor.png")):
            imgs.append(os.path.join(OUT, "floor.png")); prompt += " Image 3 shows the plain floor tiles to match. Where the old layout reference differs from the numbered list, follow the list."
        yield os.path.join(OUT, f"{k}.png"), prompt, imgs


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "pilot"
    ks = PILOT if mode == "pilot" else list(SETS) if mode == "all" else mode.split(",")
    cb.log(f"타일 시작 {ks} ({cb.quota_text()})")
    for job in jobs(ks):
        if cb.run(*job) is None:
            break
    cb.log("타일 끝")
