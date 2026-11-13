"""11-12 몬스터 고퀄(codex_batch.py mob_jobs: 몹_XX_이름/{동작}_{앞|옆|뒤}/NN.png) -> 게임 스프라이트(48칸, 용사와 같은 도트 크기).
- 몸 칸 고르기·테두리 메우기·팔레트는 hq_export.py 것 그대로
- 크기: 방향마다 대기 장들의 (원본 키 / 옛 스프라이트 키) 가운데값 = 한 칸 크기. 장마다 원본/옛 키 비가 그 ±12% 안이면 그 장 값(찌그러짐 살림)
- 자리: 옛 스프라이트 같은 장의 몸(가장 큰 덩어리) 발 줄·가로 가운데에 맞춤(점프·앞으로 나가는 움직임 그대로)
- 옛 장의 나머지 조각(그림자·별·튀는 방울)은 그대로 덧붙임(코덱스엔 효과 없이 그리게 함)
- 테두리에 안 이어진 큰 흰 바탕(활 안쪽)도 지움
- 옆 = east(오른쪽 봄), west = east 좌우 뒤집기
- 동작에 장 수가 모자라면(아직 그리는 중) 그 동작은 건너뜀
사용: python mob_export.py [몹_01_슬라임 ...]  (HQ_RES = 내보낼 폴더, 기본은 게임 폴더 Sprites/<이름>)"""
import os, sys, glob
import numpy as np
from PIL import Image
import hq_export as H
from codex_batch import MOBS, ELITES, BOSSES
LABELS = {**{f"몹_{n}": s for s, n, _ in MOBS}, **{f"엘몹_{n}": s for s, n, _ in ELITES}, **{f"보스_{n}": s for s, n, _ in BOSSES}}

SPR = os.path.join(H.ROOT, "Assets", "Resources", "Sprites")
OUT = os.environ.get("HQ_RES") or SPR
DIRS = {"앞": "south", "옆": "east", "뒤": "north"}
NCOL = 24
import mob_bowstring
def _archer(anim, a, *_):
    """11-12 사용자 "다른 동작 활줄도 옆 공격과 맞춰": 옆(동쪽) 동작은 시위 정리, 나머지는 활 옆 흰 시위만 같은 색."""
    return mob_bowstring.fix(a)[0] if anim.endswith("_east") else mob_bowstring.fix_any(a)[0]


def _bomber(anim, a, i, oldpng):
    """11-13 사용자 확인: 새 그림엔 "불붙음" 경고가 없음 -> 옛 lit 장을 보고 덧칠.
    - 폭탄(머리 위 어두운 무채색 덩어리)을 주황·빨강 불빛으로(밝기 순서대로), 옛 장의 불빛 세기(밝은 주황 비율)를 따름
    - 옛 장에 빨간 테두리가 깜빡이는 장이면 새 몸 바깥 테두리도 빨강(160,30,20)"""
    if not anim.startswith("lit"): return a
    import cv2
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    oa = o[..., 3] > 0
    hot = ((o[..., 0] == 255) & (o[..., 1] == 160)).sum(); warm = ((o[..., 0] >= 160) & (o[..., 1] <= 90) & (o[..., 2] <= 50) & oa).sum()
    flash = ((o[..., 0] == 160) & (o[..., 1] == 30) & (o[..., 2] == 20)).sum() > 25
    t = float(np.clip(hot / max(1, hot + warm) * 1.6, 0, 1))          # 0 = 어두운 빨강 위주, 1 = 밝은 주황 위주
    a = a.copy(); al = a[..., 3] > 0
    ys, xs = np.nonzero(al); top, bot = ys.min(), ys.max()
    rgb = a[..., :3].astype(int); mx = rgb.max(-1); mn = rgb.min(-1); lum = rgb @ np.array([.299, .587, .114])
    cand = al & (mx - mn < 40) & (lum >= 28) & (lum < 170)               # 폭탄 몸통: 무채색 회색(검은 테두리 제외)
    cand[int(top + (bot - top) * .45):] = False                          # 위쪽만(몸 갑옷 회색 빼기)
    n, lab, st, _ = cv2.connectedComponentsWithStats(cand.astype(np.uint8), connectivity=8)
    if n > 1:
        k = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA])); m = lab == k
        PAL = np.array([(120, 24, 18), (168, 32, 26), (232, 80, 42), (255, 160, 80), (255, 214, 150)])
        L = lum[m]; r = (L - L.min()) / max(1e-6, L.max() - L.min())
        idx = np.clip(np.round(r * 3 + t * 1.2), 0, 4).astype(int)
        a[m, :3] = PAL[idx]
    if flash:                                                            # 바깥 테두리(투명에 닿은 칸) 빨강
        er = cv2.erode(al.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        edge = al & ~er & (lum < 60)
        a[edge, :3] = (160, 30, 20)
    return a


def _spider(anim, a, i, oldpng):
    """11-13 거미 공격의 거미줄(침·뭉치)은 옛 장에서 몸에 붙어 있어 '조각'으로 안 잡힘 -> 옛 장의 거미줄 색(푸른빛 흰·회색) 칸을 새 장 위에 그대로 얹음.
    보라 반사광(180,150,220)·송곳니(크림색, 파랑<빨강)·눈빛은 빼려고 파랑>=빨강-5, 초록>=빨강-10 인 밝은 칸만."""
    if not anim.startswith("attack"): return a
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    r, g, b = o[..., 0], o[..., 1], o[..., 2]
    web = (o[..., 3] > 0) & (o[..., :3].min(-1) >= 140) & (b >= r - 5) & (b - r <= 45) & (g >= r - 10)
    a = a.copy(); a[web] = o[web].astype(np.uint8)
    return a


def _elite(anim, a, i, oldpng):
    """11-13 엘몹 보라 기운: 옛 엘몹은 몸 둘레에 반투명 보라 테(110,50,150 안쪽 / 58,14,88 바깥)가 있음 -> 옛 조각으로 붙은 테는 지우고
    새 몸 둘레 3칸에 다시 그림. 세기는 옛 장의 테 평균 불투명도(장마다 일렁임 그대로)."""
    import cv2
    def aura(x): return (x[..., 3] > 0) & (x[..., 3] < 255) & ~((x[..., 0] == 10) & (x[..., 1] == 6) & (x[..., 2] == 16))
    o = np.asarray(Image.open(oldpng).convert("RGBA"))
    om = aura(o); k = float(o[om][:, 3].mean()) / 255 if om.any() else .5
    a = a.copy(); a[aura(a)] = 0
    body = a[..., 3] == 255
    d = cv2.distanceTransform((~body).astype(np.uint8), cv2.DIST_L2, 3)
    up = np.zeros_like(body); up[:-2] = body[2:]                          # 옛 테처럼 위로 피어오르게(몸을 2칸 올린 것과의 거리도)
    d = np.minimum(d, cv2.distanceTransform((~up).astype(np.uint8), cv2.DIST_L2, 3) + .6)
    rng = np.random.default_rng(i * 7 + len(anim))
    d = d + rng.random(d.shape) * .9                                      # 테두리 끝을 연기처럼 들쭉날쭉
    ring = (d > 0) & (d <= 3.2) & ~body
    shadow = (a[..., 3] > 0) & ~body
    al = np.clip(k * 255 * (1.35 - d / 3.2), 35, 215).astype(np.uint8)
    col = np.where((d <= 1.5)[..., None], np.array([110, 50, 150], np.uint8), np.array([58, 14, 88], np.uint8))
    put = ring & ~shadow
    a[put, :3] = col[put]; a[put, 3] = al[put]
    sh = ring & shadow                                                   # 그림자 위는 보라를 섞음
    a[sh, :3] = ((a[sh, :3].astype(int) + col[sh]) // 2).astype(np.uint8); a[sh, 3] = np.maximum(a[sh, 3], al[sh])
    return a


def _sentinel(anim, a, i, oldpng):
    """11-13 반격병 lit(반격 태세): 옛 장은 몸 둘레에 금빛 점이 반짝임 -> 새 몸 바깥 1칸에 금 점선(장마다 위상 바꿔 반짝) + 옛 장 밖으로 튄 금 점."""
    if not anim.startswith("lit"): return a
    import cv2
    a = a.copy(); body = a[..., 3] == 255
    ring = (cv2.dilate(body.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0) & (a[..., 3] == 0)
    yy, xx = np.indices(body.shape)
    dot = ring & (((yy + xx + i) % 3) == 0)
    a[dot] = (246, 214, 138, 255)
    o = np.asarray(Image.open(oldpng).convert("RGBA"))
    gold = (o[..., 3] > 0) & (o[..., 0] >= 240) & (o[..., 1] >= 200) & (o[..., 2] >= 100) & (a[..., 3] == 0)
    far = gold & ~(cv2.dilate(body.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0)
    a[far] = o[far]
    return a


def _shaman(anim, a, i, oldpng):
    """11-13 고블린주술사 attack(치유 시전): 옛 장은 지팡이 구슬이 하얗게 빛나고 둘레·발밑에 초록 점 -> 새 구슬을 밝히고 점 테 + 옛 초록 점을 투명한 곳에."""
    if not anim.startswith("attack"): return a
    import cv2
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    a = a.copy(); c = a[..., :3].astype(int); al = a[..., 3] > 0
    ys = np.nonzero(al.any(1))[0]; top, bot = ys[0], ys[-1]
    glow = ((o[..., 3] > 0) & (o[..., :3].min(-1) >= 215)).sum()
    orb = al & (c[..., 1] > 150) & (c[..., 1] > c[..., 0] + 40) & (c[..., 2] > 110)
    orb[int(top + (bot - top) * .45):] = False
    n, lab, st, _ = cv2.connectedComponentsWithStats(orb.astype(np.uint8), connectivity=8)
    if n > 1 and glow >= 3:
        m = lab == 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
        L = c[..., 1]; hi = m & (L >= np.median(L[m]))
        a[hi, :3] = (235, 255, 240)
        ring = (cv2.dilate(m.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0) & ~al
        yy, xx = np.indices(m.shape); ring &= ((yy + xx + i) % 2) == 0
        a[ring] = (150, 240, 190, 255)
    sp = (o[..., 3] > 0) & (o[..., 1] > 150) & (o[..., 1] > o[..., 0] + 40) & (a[..., 3] == 0)
    a[sp] = o[sp].astype(np.uint8)
    return a


def _orc(anim, a, i, oldpng):
    """11-13 오크돌격병 attack: 옛 장의 흰 충격 표시(+ 모양·원)가 몸 위에 그려져 '조각'으로 안 잡힘 -> 옛 대기 장엔 없는 흰색 칸만 그대로 얹음."""
    if not anim.startswith("attack"): return a
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    idle = np.asarray(Image.open(oldpng.replace("attack_", "idle_")[:-6] + "_0.png").convert("RGBA")).astype(int)
    pal = {tuple(c) for c in idle[idle[..., 3] > 0][:, :3]}
    w = (o[..., 3] > 0) & (o[..., :3].min(-1) >= 200) & (o[..., :3].max(-1) - o[..., :3].min(-1) < 50)
    w &= ~np.array([[tuple(o[y, x, :3]) in pal for x in range(o.shape[1])] for y in range(o.shape[0])])
    a = a.copy(); a[w] = o[w].astype(np.uint8)
    return a


_CH_SHIFT = {}


def _chanter(anim, a, i, oldpng):
    """11-14 사용자 "주문이 보여야해": 옛 장의 분홍 주문 문양(손 위 마름모·공격 고리)이 새 그림에서 사라짐 -> 옛 분홍 칸을 불투명하게 얹음(눈 색 255,90,216 은 뺌).
    새 몸(후드)이 더 커서 문양이 몸에 묻히므로, 동작마다 몸과 덜 겹치는 쪽으로 조금 옮김(첫 장 기준으로 고정해 떨림 없음)."""
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    c = o[..., :3]
    pink = (o[..., 3] > 0) & (c[..., 0] >= 200) & (c[..., 2] >= 180) & (c[..., 1] <= 120)
    # 옆모습에선 문양 왼쪽 반이 후드 가장자리 위에 반투명하게 섞여 그려져 있다(953a8a·c34bb8·a842a2 등) -> 그것도 문양으로(안 그러면 ")" 반쪽만 돎)
    pink |= (o[..., 3] > 0) & (c[..., 0] >= 140) & (c[..., 2] >= 120) & (c[..., 1] <= 110) & (c[..., 0] - c[..., 1] >= 80)
    pink &= ~((c[..., 0] == 255) & (c[..., 1] == 90) & (c[..., 2] == 216))
    hi = (o[..., 3] > 0) & (c[..., 0] == 255) & (c[..., 1] == 220) & (c[..., 2] == 250)
    body = a[..., 3] > 0
    if anim not in _CH_SHIFT:
        m = pink | hi; best = (m & body).sum() * 4, 0, 0
        for dy in range(-6, 3):
            for dx in range(-6, 7):
                sc = (np.roll(m, (dy, dx), (0, 1)) & body).sum() * 4 + abs(dx) + abs(dy)
                if sc < best[0]: best = sc, dy, dx
        _CH_SHIFT[anim] = best[1:]
    dy, dx = _CH_SHIFT[anim]
    pink, hi = np.roll(pink, (dy, dx), (0, 1)), np.roll(hi, (dy, dx), (0, 1))
    a = a.copy()
    a[pink] = (250, 105, 230, 255)
    a[hi] = (255, 225, 252, 255)
    return a

POST = {"SkeletonArcher": _archer, "OrcCharger": _orc, "Sentinel": _sentinel, "Shaman": _shaman, "Bomber": _bomber, "Spider": _spider,
        "EliteSkeletonArcher": lambda *x: _elite(x[0], _archer(*x), *x[2:]), "EliteSpider": lambda *x: _elite(x[0], _spider(*x), *x[2:])}
POST["EliteSentinel"] = lambda *x: _elite(x[0], _sentinel(*x), *x[2:])
POST["EliteShaman"] = lambda *x: _elite(x[0], _shaman(*x), *x[2:])
POST["EliteOrcCharger"] = lambda *x: _elite(x[0], _orc(*x), *x[2:])
POST["Chanter"] = _chanter
POST["EliteChanter"] = lambda *x: _elite(x[0], _chanter(*x), *x[2:])
for _s in ("EliteShieldSoldier", "EliteJelly", "EliteRogue"):
    POST[_s] = _elite   # 장마다 덧손질(동쪽 고친 뒤 서쪽은 뒤집기)
KEEP_EXTRA = {"Spider", "EliteSpider", "Shaman", "EliteShaman", "OrcCharger", "EliteOrcCharger"}   # 11-13 옛 효과 조각(거미줄 덩이)이 새 몸 상자 안에 있어도 버리지 않음(새 몸이 안 가리는 칸에만 붙음)
OLDKEEP = os.path.join(H.AUTO, "_work", "mobold")   # 옛 스프라이트 보관(게임 폴더에 새 그림을 넣은 뒤에도 위치·그림자 기준은 옛 그림)


def old_dir(spr):
    d = os.path.join(OLDKEEP, spr)
    if not os.path.isdir(d):
        import shutil
        shutil.copytree(os.path.join(SPR, spr), d, ignore=shutil.ignore_patterns("*.meta"))
    return d


def split_old(rgba):
    """옛 장: 가장 큰 덩어리 = 몸, 나머지(그림자·별·튀는 방울·공격 표시) = 덧붙일 조각."""
    import cv2
    a = (rgba[..., 3] > 0).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(a, connectivity=8)
    big = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
    return lab == big, (lab > 0) & (lab != big)


def inner_white(rgb, min_area=150):
    """테두리에 안 이어진 흰 바탕(활과 시위 사이, 다리·갈비뼈 사이) = 무채색(파랑 >= 빨강-1.5) 아주 흰(240+) 덩어리.
    11-12 사용자 "머리에 구멍": 노란빛 흰색(파랑이 빨강보다 낮음)이나, 둘레가 밝은(뼈·몸 색) 반사광은 남김 - 진짜 틈은 둘레가 검은 테두리."""
    import cv2
    w = (rgb.min(-1) >= 240).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(w, connectivity=4)
    out = np.zeros(w.shape, bool)
    for k in range(1, n):
        if st[k, cv2.CC_STAT_AREA] < min_area: continue
        m = lab == k
        c = rgb[m].mean(0)
        if c[2] - c[0] < -1.5: continue
        x, y, ww, hh, _ = st[k]                                     # 바로 바깥 둘레(3px)가 어두우면(검은 테두리) 틈, 밝으면(뼈) 반사광
        y0, y1, x0, x1 = max(0, y - 4), y + hh + 4, max(0, x - 4), x + ww + 4
        sub = m[y0:y1, x0:x1].astype(np.uint8)
        ring = (cv2.dilate(sub, np.ones((7, 7), np.uint8)) > 0) & (sub == 0)
        lum = rgb[y0:y1, x0:x1][ring] @ np.array([.299, .587, .114])
        if len(lum) and (lum < 90).mean() >= .5: out |= m
    return out


def best_phase(rgb, cls, pt, step=2):
    """칸 격자를 GPT 그림 도트 격자에 맞춤: 몸 칸 안 색 흩어짐(분산) 합이 가장 작은 (ox, oy). 11-12 사용자 "방패 깨짐"(1칸 테두리가 두 칸에 걸려 끊김)."""
    H_, W_ = cls.shape
    f = rgb.astype(np.float64) * (cls == 1)[..., None]
    I1 = np.zeros((H_ + 1, W_ + 1, 3)); I1[1:, 1:] = f.cumsum(0).cumsum(1)
    I2 = np.zeros((H_ + 1, W_ + 1, 3)); I2[1:, 1:] = (f ** 2).cumsum(0).cumsum(1)
    In = np.zeros((H_ + 1, W_ + 1)); In[1:, 1:] = (cls == 1).cumsum(0).cumsum(1)
    best = None
    for oy in np.arange(0, pt, step):
        ys = np.round(np.arange(oy, H_ + pt, pt)).astype(int).clip(0, H_)
        for ox in np.arange(0, pt, step):
            xs = np.round(np.arange(ox, W_ + pt, pt)).astype(int).clip(0, W_)
            def box(I):
                return I[ys[1:]][:, xs[1:]] - I[ys[:-1]][:, xs[1:]] - I[ys[1:]][:, xs[:-1]] + I[ys[:-1]][:, xs[:-1]]
            n = box(In); s1 = box(I1); s2 = box(I2)
            ok = n > 4
            var = (s2[ok] - s1[ok] ** 2 / n[ok][:, None]).sum()
            if best is None or var < best[0]: best = (var, ox, oy)
    return best[1], best[2]


def bbox(alpha):
    ys, xs = np.nonzero(alpha)
    return ys.min(), ys.max(), xs.min(), xs.max()


def run(label):
    spr = LABELS[label]
    src = os.path.join(H.AUTO, label)
    old = old_dir(spr)
    groups = {}
    for d in sorted(glob.glob(os.path.join(src, "*_*"))):
        kind, dk = os.path.basename(d).rsplit("_", 1)
        if kind.startswith("_") or dk not in DIRS: continue
        anim = f"{kind}_{DIRS[dk]}"
        n = len(glob.glob(os.path.join(old, f"{anim}_*.png")))
        fs = sorted(glob.glob(os.path.join(d, "[0-9][0-9].png")))
        if n and len(fs) == n: groups[anim] = fs
        else: print(f"  {anim}: {len(fs)}/{n}장 - 건너뜀")
    if not groups: return
    pre = {}
    for anim, fs in groups.items():
        pre[anim] = []
        for i, p in enumerate(fs):
            rgb = H.load_rgb(p); bg = H.background(rgb); iw = inner_white(rgb) & ~bg
            cls = np.where(bg, 0, np.where(iw, 2, 1)).astype(np.int8)     # 2 = 안쪽 흰 바탕(메우지 않음)
            orgba = np.asarray(Image.open(os.path.join(old, f"{anim}_{i}.png")).convert("RGBA"))
            ob, ox_ = split_old(orgba)
            extra = orgba.copy(); extra[~ox_] = 0
            pre[anim].append((rgb, cls, bbox(cls == 1), bbox(ob), (orgba.shape, extra)))
    base = {}
    for d in ("south", "east", "north"):
        k = [(b[1] - b[0] + 1) / (o[1] - o[0] + 1) for a, fl in pre.items() if a.endswith(d) and a.startswith("idle") for _, _, b, o, _ in fl]
        if not k: k = [(b[1] - b[0] + 1) / (o[1] - o[0] + 1) for a, fl in pre.items() if a.endswith(d) for _, _, b, o, _ in fl]
        if k: base[d] = float(np.median(k))
    frames = {}
    for anim, fl in pre.items():
        d = anim.rsplit("_", 1)[1]
        out = []
        for rgb, cls, b, o, shp in fl:
            r = (b[1] - b[0] + 1) / (o[1] - o[0] + 1)
            pt = float(np.clip(r, base[d] * .88, base[d] * 1.12))
            ox, oy = best_phase(rgb, cls, pt)
            col, fb, fm = H.cells(rgb, cls, pt, ox, oy, 2)
            op = fb >= .5
            # 11-12 틈 메우기 뺌: 다리·갈비뼈·활 사이 빈 틈이 검은 테두리 색으로 메워져 해골궁수가 시커메짐(사용자)
            H.add_outline(col, op)
            out.append((col, op, o, shp))
        frames[anim] = out
    pal = H.make_palette(np.concatenate([c[op] for fl in frames.values() for c, op, _, _ in fl]), NCOL)
    dst = os.path.join(OUT, spr); n = 0
    for anim, fl in frames.items():
        for i, (col, op, o, shp) in enumerate(fl):
            cy, cx = np.nonzero(op)
            dy = o[1] - cy.max()
            dx = int(round((o[2] + o[3]) / 2 - (cx.min() + cx.max()) / 2))
            shp, extra = shp
            can = np.zeros((shp[0], shp[1], 4), np.uint8)
            for (y, x), c in zip(zip(cy, cx), H.snap(col[op], pal)):
                ty, tx = y + dy, x + dx
                if 0 <= ty < shp[0] and 0 <= tx < shp[1]:
                    can[ty, tx, :3] = c; can[ty, tx, 3] = 255
            import cv2                                                    # 옛 조각 중 새 몸 상자 안에 반 넘게 들어가는 것(옛 활시위 등)은 버림
            ea = (extra[..., 3] > 0).astype(np.uint8)
            nb_, lab_, _, _ = cv2.connectedComponentsWithStats(ea, connectivity=8)
            ys_, xs_ = np.nonzero(can[..., 3]); inbox = np.zeros(ea.shape, bool)
            if len(ys_): inbox[ys_.min():ys_.max() + 1, xs_.min():xs_.max() + 1] = True
            keep = np.zeros(ea.shape, bool)
            for k_ in range(1, nb_):
                m_ = lab_ == k_
                if spr in KEEP_EXTRA or inbox[m_].mean() <= .5: keep |= m_
            put = keep & (can[..., 3] == 0)                              # 옛 그림자·효과 조각(몸에 안 가리는 곳만)
            can[put] = extra[put]
            if spr in POST: can = POST[spr](anim, can, i, os.path.join(old, f"{anim}_{i}.png"))
            H.save_png(can, os.path.join(dst, f"{anim}_{i}.png")); n += 1
            if anim.endswith("_east"):
                H.save_png(can[:, ::-1].copy(), os.path.join(dst, f"{anim[:-5]}_west_{i}.png")); n += 1
    print(label, "->", spr, f"{n}장", "칸 크기", {d: round(v, 1) for d, v in base.items()})


if __name__ == "__main__":
    SKIP = {"몹_01_슬라임"}   # 11-13 사용자 "슬라임은 고퀄 저퀄 같은거(저퀄)" - 게임 폴더엔 안 넣음(이름 직접 주면 넣음)
    for s in sys.argv[1:] or sorted(os.path.basename(p) for p in glob.glob(os.path.join(H.AUTO, "*몹_*")) + glob.glob(os.path.join(H.AUTO, "보스_*")) if os.path.basename(p) not in SKIP):
        run(s)
