"""11-28 고퀄 타일: tile_batch.py 가 그린 격자 시트를 칸별로 잘라 도트화 + 미리보기 방.
사용: python tile_export.py [출력폴더]   (기본 = 자동/_work/preview/tiles, 게임 폴더엔 안 씀)
- 칸 크기 TS=52: 용사(64칸 x 1.25배)와 도트 한 칸 크기가 같아지는 값(64/1.25 = 51.2). 옛 타일은 32라 도트가 1.6배 컸음.
- 모든 종류가 같은 팔레트(색 64개)를 쓰게 한꺼번에 줄임.
- room.png: 고퀄 용사·몹을 실제 게임 배율로 올린 방 미리보기(옛 타일 방과 나란히)."""
import os, sys, random
import numpy as np
from PIL import Image
from tile_batch import SETS, OUT as SRC, TILES

TS = 52
NCOL = 32   # 11-28 사용자 "문이 너무 고퀄인데 도트맞아?": 64색은 그라데이션이 부드러워 그림처럼 보임 -> 용사(29색)와 비슷하게
NCOL_KIND = {"door": 20}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Sprites")
NAMES = {  # 격자 칸 순서 -> 게임 이름
    "floor": [f"Floor_{i}" for i in range(16)],
    "wallface": ["WallFace_0", "WallFace_1", "WallFace_3", "WallFace_4", "WallFace_5", "WallFace_Torch", "WallCracked", "WallFace_2"],
    "walltop": [f"WallTop_{i}" for i in range(4)],
    "decor": [f"FloorDecor_{i}" for i in range(12)],
    "door": ["ExitDoor", "Stairs"],
}


def cells(kind):
    _, (c, r), _ = SETS[kind]
    im = Image.open(os.path.join(SRC, kind + ".png")).convert("RGB")
    a = np.asarray(im).astype(int); ink = a.min(-1) < 235            # GPT가 위아래에 흰 여백을 둘 때가 있음 -> 잘라냄
    ys, xs = np.nonzero(ink); im = im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    W, H = im.size; out = []
    for j in range(r):
        for i in range(c):
            box = (round(i * W / c), round(j * H / r), round((i + 1) * W / c), round((j + 1) * H / r))
            out.append(im.crop(box).resize((TS, TS), Image.BOX))
    return out


def export(dst):
    os.makedirs(dst, exist_ok=True)
    got = {k: cells(k) for k in NAMES if os.path.exists(os.path.join(SRC, k + ".png"))}   # NAMES 순서상 floor 가 decor 보다 먼저
    raw_decor = list(got.get("decor", []))
    if "decor" in got and "floor" in got:      # 장식 칸 바탕색을 일반 바닥에 맞춤(GPT가 갈색·보라로 그림): 돌판 얼굴색(중앙값)끼리 옮김
        f = np.concatenate([np.asarray(t, float).reshape(-1, 3) for t in got["floor"]])
        fm = np.median(f, 0)
        got["decor"] = [Image.fromarray(np.clip(np.asarray(t, float) - np.median(np.asarray(t, float).reshape(-1, 3), 0) + fm, 0, 255).astype(np.uint8))
                        for t in got["decor"]]
    # 11-28 사용자 "어색한그림들 한번더 찾아보고 수정": 벽 앞면·문·계단이 청록빛이라 바닥·벽 윗면(회보라)과 따로 놂 -> 밝기는 두고 색조만 벽 윗면 쪽으로
    if "walltop" in got and "floor" in got:      # 밝기 그대로, 색은 바닥·벽 윗면 돌색 띠(밝기별 평균색)에서 가져옴
        ref = np.concatenate([np.asarray(t, float).reshape(-1, 3) for t in got["walltop"] + got["floor"]])
        L = ref.mean(-1); ramp = np.zeros((256, 3))
        for v in range(256):
            m = np.abs(L - v) < 6
            ramp[v] = ref[m].mean(0) * (v / max(1e-6, ref[m].mean())) if m.sum() > 20 else np.nan
        ok = ~np.isnan(ramp[:, 0]); xs = np.arange(256)
        for c in range(3): ramp[:, c] = np.interp(xs, xs[ok], ramp[ok, c])
        def tint(t):
            a = np.asarray(t, float)
            keep = ((a[..., 0] - a[..., 2] > 18) & (a.max(-1) - a.min(-1) > 28)) | (a.max(-1) > 200)   # 불꽃·문빛·녹슨 사슬은 그대로
            g = ramp[np.clip(a.mean(-1), 0, 255).astype(int)]
            return Image.fromarray(np.clip(np.where(keep[..., None], a, g), 0, 255).astype(np.uint8))
        for k in ("wallface", "door"):
            if k in got: got[k] = [tint(t) for t in got[k]]
    if "wallface" in got:
        wf = list(got["wallface"])
        mi = NAMES["wallface"].index("WallFace_4")           # 바닥 이끼를 뺐으니 벽 이끼도 뺌 -> 민 벽돌(1번 좌우 뒤집기)
        wf[mi] = wf[NAMES["wallface"].index("WallFace_1")].transpose(Image.FLIP_LEFT_RIGHT)
        got["wallface"] = wf
    res = {}
    for k, ts in got.items():                  # 종류마다 팔레트(횃불 주황처럼 작은 색이 공용 팔레트에서 사라지지 않게)
        if k == "decor" and "Floor_0" in res:     # 바탕 = 바닥 팔레트, 장식(바닥색과 먼 칸) = 옛 색 그대로 16색
            fpal = np.array(sorted({tuple(c[:3]) for n in NAMES["floor"] if n in res for c in np.asarray(res[n]).reshape(-1, 4)}), float)
            for name, t, r0 in zip(NAMES[k], ts, raw_decor):
                a = np.asarray(t, float); d = np.sqrt(((a[:, :, None, :] - fpal[None, None]) ** 2).sum(-1)); near = d.argmin(-1)
                ra = np.asarray(r0, float)
                deco = (d.min(-1) > 30) | (ra.max(-1) - ra.min(-1) > 45)   # 바닥과 먼 색이거나 채도가 높은 칸(피·버섯·물·화살)
                o = fpal[near]
                if deco.any():                  # 장식 칸만 모아 옛 색 그대로 줄임
                    px = ra[deco].astype(np.uint8)[None]
                    dq = np.asarray(Image.fromarray(px).quantize(min(24, len(px[0])), method=Image.Quantize.MEDIANCUT,
                                                                dither=Image.Dither.NONE).convert("RGB"), float)[0]
                    o[deco] = dq
                q = Image.fromarray(o.astype(np.uint8)).convert("RGBA")
                q.save(os.path.join(dst, name + ".png")); res[name] = q
            continue
        src = ts
        big = Image.new("RGB", (TS * len(src), TS))
        for i, t in enumerate(src): big.paste(t, (i * TS, 0))
        pal = big.quantize(NCOL_KIND.get(k, NCOL), method=Image.Quantize.MAXCOVERAGE, dither=Image.Dither.NONE)
        for name, t in zip(NAMES[k], ts):
            q = t.quantize(palette=pal, dither=Image.Dither.NONE).convert("RGBA")
            q.save(os.path.join(dst, name + ".png")); res[name] = q
    return res


def shadows(dst, res):
    """그림자 4종: 옛 반투명 띠(32칸)를 TS로 부드럽게 늘려 알파만 다시 3단(도트 느낌 유지)."""
    for n in ("Shadow_Top", "Shadow_Left", "Shadow_TopLeft", "Shadow_Corner"):
        a = np.asarray(Image.open(os.path.join(TILES, n + ".png")).convert("RGBA").resize((TS, TS), Image.BILINEAR)).copy()
        al = a[..., 3].astype(float); a[..., 3] = (np.round(al / 50) * 50).clip(0, 150).astype(np.uint8); a[..., :3] = (8, 6, 14)
        im = Image.fromarray(a); im.save(os.path.join(dst, n + ".png")); res[n] = im


def glitch(dst, res, seed=11):
    """오류방(10-19) 깨진 타일: 새 바닥·벽에 줄무늬 어둡게 + 색 어긋남 + 자홍·청록·초록 조각(옛 Glitch* 와 같은 느낌)."""
    rng = np.random.default_rng(seed)
    cols = [(200, 60, 200), (60, 200, 210), (80, 200, 90), (150, 90, 230)]
    def make(base, name):
        a = np.asarray(base.convert("RGB"), float).copy()
        a[::2] *= .78                                   # 주사선
        sh = rng.integers(1, 3); a[:, :, 0] = np.roll(a[:, :, 0], sh, 1); a[:, :, 2] = np.roll(a[:, :, 2], -sh, 1)
        for _ in range(rng.integers(3, 6)):              # 색 막대
            y, x = rng.integers(0, TS - 2), rng.integers(0, TS - 8); w = rng.integers(4, 12)
            a[y:y + rng.integers(1, 3), x:x + w] = cols[rng.integers(len(cols))]
        for _ in range(rng.integers(3, 7)):              # 흰·검은 점
            a[rng.integers(TS), rng.integers(TS)] = (240, 240, 250) if rng.random() < .6 else (10, 8, 16)
        im = Image.fromarray(a.clip(0, 255).astype(np.uint8)).convert("RGBA"); im.save(os.path.join(dst, name + ".png")); res[name] = im
    fl = [res[f"Floor_{i}"] for i in range(10) if f"Floor_{i}" in res]
    for i in range(10): make(fl[i % len(fl)], f"GlitchFloor_{i}")
    for i in range(4):
        make(res[f"WallTop_{i % 4}"], f"GlitchWallTop_{i}"); make(res[f"WallFace_{i}"], f"GlitchWallFace_{i}")


def door_alpha(dst, res):
    """출구 문은 바닥 칸 위에 놓이는 그림이라 둘레 벽돌은 투명해야 함(옛 그림처럼): 아치 바깥 = 가장자리에서 이어진 어두운 벽돌 칸을 지움."""
    if "ExitDoor" not in res: return
    a = np.asarray(res["ExitDoor"].convert("RGBA")).copy(); H, W = a.shape[:2]
    lum = a[..., :3].astype(float).mean(-1)
    # 아치 돌틀 = 문 가운데 금빛에서 바깥으로 가며 처음 만나는 밝은 돌 고리. 단순히: 가운데 열 기준 타원 바깥을 지움
    warm = (a[..., 0].astype(int) - a[..., 2] > 40)
    ys, xs = np.nonzero(warm); cy, cx = ys.mean(), xs.mean(); rx = (xs.max() - xs.min()) / 2 + 6; top = ys.min() - 7
    yy, xx = np.mgrid[:H, :W]
    inside = (np.abs(xx - cx) <= rx) & (yy >= top + rx) | (((xx - cx) / rx) ** 2 + ((yy - (top + rx)) / rx) ** 2 <= 1)
    a[~inside, 3] = 0
    edge = inside & ~(np.roll(inside, 1, 0) & np.roll(inside, -1, 0) & np.roll(inside, 1, 1) & np.roll(inside, -1, 1))
    a[edge, :3] = (18, 12, 20)                  # 바깥 테두리 한 줄(옛 문·용사처럼)
    im = Image.fromarray(a); im.save(os.path.join(dst, "ExitDoor.png")); res["ExitDoor"] = im


def load_old():
    return {os.path.splitext(f)[0]: Image.open(os.path.join(TILES, f)).convert("RGBA") for f in os.listdir(TILES) if f.endswith(".png")}


def room(tiles, ts, seed=3, w=11, h=8):
    """벽 두께 1, 위쪽은 벽 앞면이 보이게. 칸 = ts 픽셀. 캐릭터는 게임 배율(칸 1.5유닛, 용사 64px x 1.25 / 몹 48px@64PPU x 1.3)."""
    rng = random.Random(seed)
    def pick(pre):
        ks = sorted(k for k in tiles if k.startswith(pre) and k[len(pre):].isdigit())
        return tiles[rng.choice(ks)]
    im = Image.new("RGBA", (w * ts, h * ts), (0, 0, 0, 255))
    for y in range(h):
        for x in range(w):
            wall = x in (0, w - 1) or y in (0, h - 1)
            if wall:
                face = y == 0 and 0 < x < w - 1      # 아래가 바닥인 벽만 앞면
                t = (tiles.get("WallFace_Torch") if face and rng.random() < .15 else pick("WallFace_")) if face else pick("WallTop_")
            else:
                t = pick("FloorDecor_") if rng.random() < .12 else pick("Floor_")
            im.alpha_composite(t.resize((ts, ts), Image.NEAREST), (x * ts, y * ts))
    unit = ts  # 한 칸 = ts 픽셀 = 1.5 유닛
    def put(path, cx, cy, scale_units):
        s = Image.open(path).convert("RGBA"); px = round(scale_units / 1.5 * unit)
        s = s.resize((round(s.width / 64 * px), round(s.height / 64 * px)), Image.NEAREST)
        im.alpha_composite(s, (int((cx + .5) * unit - s.width / 2), int((cy + .5) * unit - s.height / 2)))
    put(os.path.join(ROOT, "HeroHQ", "idle_south_0.png"), 5, 4, 1.5 * 1.25)
    put(os.path.join(ROOT, "HeroHQSword0", "a_0.png"), 5, 4, 1.5 * 1.25)
    for spr, cx, cy, sc in (("SkeletonArcherHQ", 2, 2, 1.3), ("SpiderHQ", 8, 3, 1.3), ("ShieldSoldierHQ", 3, 5, 1.35), ("ShamanHQ", 8, 6, 1.3)):
        put(os.path.join(ROOT, spr, "idle_south_0.png"), cx, cy, 1.5 * sc)
    return im


if __name__ == "__main__":
    dst = sys.argv[1] if len(sys.argv) > 1 else os.path.join(SRC, "..", "_work", "preview", "tiles")
    new = export(dst)
    shadows(dst, new); glitch(dst, new); door_alpha(dst, new)
    old = load_old()
    for k in old:
        new.setdefault(k, old[k])   # 아직 없는 종류는 옛 타일로
    S = 1
    a, b = room(old, 104), room(new, 104)   # 새 타일 52 -> 정확히 2배
    gap = 24; cmp = Image.new("RGBA", (a.width * 2 + gap, a.height), (40, 40, 50, 255))
    cmp.alpha_composite(a, (0, 0)); cmp.alpha_composite(b, (a.width + gap, 0))
    cmp = cmp.resize((cmp.width * S // 1, cmp.height * S // 1), Image.NEAREST)
    out = os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "고퀄 스프라이트", "타일")
    os.makedirs(out, exist_ok=True)
    cmp.save(os.path.join(out, "방_비교_19차.png"))
    # 낱장 모아보기
    ks = [n for v in NAMES.values() for n in v if n in new]
    sheet = Image.new("RGBA", (8 * (TS * 3 + 6), ((len(ks) + 7) // 8) * (TS * 3 + 6)), (40, 40, 50, 255))
    for i, n in enumerate(ks):
        sheet.alpha_composite(new[n].resize((TS * 3, TS * 3), Image.NEAREST), ((i % 8) * (TS * 3 + 6), (i // 8) * (TS * 3 + 6)))
    sheet.save(os.path.join(out, "타일_낱장.png"))
    print("ok", len(ks), out)
