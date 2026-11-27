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


ELITE_AURA = False


def _elite(anim, a, i, oldpng):
    """11-13 엘몹 보라 기운: 옛 엘몹은 몸 둘레에 반투명 보라 테(110,50,150 안쪽 / 58,14,88 바깥)가 있음 -> 옛 조각으로 붙은 테는 지우고
    새 몸 둘레 3칸에 다시 그림. 세기는 옛 장의 테 평균 불투명도(장마다 일렁임 그대로)."""
    import cv2
    def aura(x): return (x[..., 3] > 0) & (x[..., 3] < 255) & ~((x[..., 0] == 10) & (x[..., 1] == 6) & (x[..., 2] == 16))
    o = np.asarray(Image.open(oldpng).convert("RGBA"))
    om = aura(o); k = float(o[om][:, 3].mean()) / 255 if om.any() else .5
    a = a.copy(); a[aura(a)] = 0
    if not ELITE_AURA: return a                                           # 11-18 사용자 "없어도 될 듯": 새 엘몹은 디자인부터 달라서 보라 기운 안 그림(옛 테 조각만 지움)
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

def _rogue(anim, a, i, oldpng):
    """11-18 사용자 "이펙트 fx 기반으로 새로 그려": 옛 효과를 베끼지 않고 새 몸·새 단검 끝에 맞춰 직접 그림.
    0 준비 / 1 날 끝 반짝 / 2 돌진 잔상+속도선 / 3 잔상(옅게)+큰 십자 별빛 / 4 별빛이 고리로 퍼짐 / 5·6 보라 가루가 떠오르며 사라짐 / 7 끝.
    옛 그림에서 몸에 붙지 않아 '조각'으로 붙은 옛 효과는 먼저 지움(옛 장에만 있고 옛 대기엔 없는 색 = 효과)."""
    if not anim.startswith("attack"): return a
    a = a.copy()
    o = np.asarray(Image.open(oldpng).convert("RGBA")).astype(int)
    pal = set()
    for f in glob.glob(oldpng.replace("attack_", "idle_")[:-6] + "_*.png"):
        d = np.asarray(Image.open(f).convert("RGBA")); pal |= {tuple(x) for x in d[d[..., 3] > 0][:, :3].tolist()}
    H_, W_ = a.shape[:2]
    oc = o[..., :3]
    oldfx = (o[..., 3] > 0) & ~np.array([[tuple(oc[y, x].tolist()) in pal for x in range(W_)] for y in range(H_)])
    oldfx &= ~((oc[..., 0] == 10) & (oc[..., 1] == 6) & (oc[..., 2] == 16))
    same = (a.astype(int) == o).all(-1) & oldfx                            # 새 몸이 아닌, 옛 조각으로 붙은 칸만
    a[same] = 0
    body = a[..., 3] == 255
    if not body.any(): return a
    ys, xs = np.nonzero(body); top, bot, lft, rgt = ys.min(), ys.max(), xs.min(), xs.max()
    side = "east" in anim or "west" in anim
    c = a[..., :3].astype(int)
    blade = body & (c.min(-1) >= 150) & (c.max(-1) - c.min(-1) < 60)
    if side:
        if blade.any() and np.nonzero(blade)[1].max() > (lft + rgt) / 2:
            by, bx = np.nonzero(blade); k = np.argmax(bx); ty, tx = by[k], bx[k] + 2
        else:
            mid = body[(top + bot) // 2]; ty, tx = (top + bot) // 2, (np.nonzero(mid)[0].max() if mid.any() else rgt) + 2
        back = -1
    elif "north" in anim:
        ty, tx = top + 4, int(xs.mean()); back = 0
    else:
        ty, tx = int(top + (bot - top) * .62), int(xs.mean()); back = 0
    out = a.astype(float)
    def put(y, x, col, al, over=True):
        if not (0 <= y < H_ and 0 <= x < W_): return
        if not over and out[y, x, 3] > 0: return
        k = al / 255.0
        if out[y, x, 3] == 0: out[y, x, :3] = col; out[y, x, 3] = al
        else: out[y, x, :3] = out[y, x, :3] * (1 - k) + np.array(col) * k; out[y, x, 3] = max(out[y, x, 3], al)
    def star(L, D, core=255):
        put(ty, tx, (255, 252, 255), core)
        if L >= 4:
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)): put(ty + dy, tx + dx, (255, 252, 255), core)
            for dy, dx in ((1, 1), (1, -1), (-1, 1), (-1, -1)): put(ty + dy, tx + dx, (235, 215, 255), 200)
        for s in range(1, L + 1):
            f = 1 - s / (L + 1); col = (250, 240, 255) if s <= L // 2 else (205, 160, 255)
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)): put(ty + dy * s, tx + dx * s, col, int(90 + 165 * f))
        for s in range(1, D + 1):
            for dy, dx in ((1, 1), (1, -1), (-1, 1), (-1, -1)): put(ty + dy * s, tx + dx * s, (190, 130, 255), int(70 + 120 * (1 - s / (D + 1))))
    rng = np.random.default_rng(11 + i * 31 + len(anim))
    if i in (2, 3):                                                       # 돌진 잔상: 몸을 뒤(옆)나 위(앞·뒤)로 밀어 반투명 보라
        for n, (sh, al) in enumerate(((3, 110), (6, 55)) if i == 2 else ((4, 60),)):
            dy, dx = (0, sh * back) if side else (-sh // 2 - 1, 0)
            m = np.roll(body, (dy, dx), (0, 1))
            for y, x in zip(*np.nonzero(m & ~body)): put(y, x, (96, 52, 190), al, over=False)
        if side:                                                          # 속도선
            for row in rng.choice(np.arange(top + 3, bot - 2), 4, replace=False):
                x0 = lft - 1 - int(rng.integers(0, 3)); ln = int(rng.integers(4, 9)) - (i == 3) * 2
                for s in range(ln): put(int(row), x0 - s, (170, 120, 240), int(150 * (1 - s / ln)), over=False)
    if i == 1: star(1, 0, 230)
    if i == 3: star(8, 4)
    if i == 4:
        star(3, 1, 230)
        for t in range(16):
            ang = t / 16 * 2 * np.pi + .2; r = 7 + (t % 2)
            put(int(round(ty + np.sin(ang) * r)), int(round(tx + np.cos(ang) * r)), (225, 195, 255) if t % 2 else (180, 120, 255), 230)
    if i in (5, 6):
        for t in range(16 if i == 5 else 10):
            ang = rng.random() * 2 * np.pi; r = rng.uniform(4, 9 if i == 5 else 12)
            y = int(round(ty + np.sin(ang) * r - (i - 4) * 2)); x = int(round(tx + np.cos(ang) * r))
            put(y, x, (200, 150, 255) if t % 3 else (245, 235, 255), 200 if i == 5 else 120)
    return np.clip(out.round(), 0, 255).astype(np.uint8)


POST = {"SkeletonArcher": _archer, "OrcCharger": _orc, "Sentinel": _sentinel, "Shaman": _shaman, "Bomber": _bomber, "Spider": _spider,
        "EliteSkeletonArcher": lambda *x: _elite(x[0], _archer(*x), *x[2:]), "EliteSpider": lambda *x: _elite(x[0], _spider(*x), *x[2:])}
POST["EliteSentinel"] = lambda *x: _elite(x[0], _sentinel(*x), *x[2:])
POST["EliteShaman"] = lambda *x: _elite(x[0], _shaman(*x), *x[2:])
POST["EliteOrcCharger"] = lambda *x: _elite(x[0], _orc(*x), *x[2:])
def _ninja_back(anim, a, *_):
    """11-27 사용자 "그림자 보스 attack_north 이상해": 뒷모습 목·두건 아래 초록 천이 얼굴처럼 보여 앞을 보는 것 같음
    (GPT에 빼라고 해도 계속 그림) -> 북쪽 장에서 몸 위쪽 45% 안 초록을 같은 밝기 순서의 보라로."""
    if not anim.endswith("north"): return a
    a = a.copy(); al = a[..., 3] > 0
    if not al.any(): return a
    rgb = a[..., :3].astype(int); r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    ys = np.nonzero(al.any(1))[0]; top, h = ys[0], ys[-1] - ys[0] + 1
    xs = np.nonzero(al.any(0))[0]; cx = (xs[0] + xs[-1]) / 2
    yy, xx = np.mgrid[:a.shape[0], :a.shape[1]]
    green = al & (g > r + 12) & (g > b + 4) & (yy < top + .45 * h) & (abs(xx - cx) < .22 * (xs[-1] - xs[0] + 1))
    purple = al & (r > g + 15) & (b > g + 15)
    if not green.any() or not purple.any(): return a
    lum = rgb @ np.array([.299, .587, .114])
    pc = rgb[purple]; pl = lum[purple]; order = np.argsort(pl); pc, pl = pc[order], pl[order]
    gl = lum[green]; q = (gl - gl.min()) / max(1e-6, gl.max() - gl.min())
    idx = np.clip((q * .6 * (len(pc) - 1)).astype(int), 0, len(pc) - 1)   # 어두운 쪽 60% 보라(두건 그늘처럼)
    a[..., :3][green] = pc[idx]
    return a


for _s in ("EliteShieldSoldier", "EliteJelly"):
    POST[_s] = _elite   # 장마다 덧손질(동쪽 고친 뒤 서쪽은 뒤집기)
# 11-27 사용자 "엘리트몹중에 공격 이펙트 없는것들 체크해서 만들어줘": 옛 엘몹은 공격 효과가 몸에 붙어 있어 새 그림에 안 옮겨짐
# (방패병·반격병·주술사·오크돌격병·젤리·거미·궁수). 그림자도적·주문술사는 이미 보정 있음. 새 몸에 맞춰 직접 그림(보라·자홍 엘몹 색).
_EFX_COL = {"core": (255, 246, 255), "hi": (245, 170, 255), "mid": (220, 110, 250), "deep": (150, 70, 220), "dust": (120, 100, 130)}


class _Fx:
    def __init__(self, a, anim, i, pal=None):
        self.C = {**_EFX_COL, **(pal or {})}
        self.o = a.astype(float); self.H, self.W = a.shape[:2]; self.i = i
        body = a[..., 3] == 255; self.body = body
        ys, xs = np.nonzero(body) if body.any() else (np.array([0]), np.array([0]))
        self.top, self.bot, self.lft, self.rgt = ys.min(), ys.max(), xs.min(), xs.max()
        self.cx = int(round(xs.mean())); self.side = anim.endswith(("east", "west")); self.north = anim.endswith("north")
        self.rng = np.random.default_rng(97 + i * 13 + len(anim))
        self.S = max(1.0, (self.bot - self.top) / 30)   # 보스(64칸)는 크게

    def row(self, f): return int(self.top + (self.bot - self.top) * f)

    def front(self, f, pad=1):
        """옆모습: f 높이 줄의 몸 오른쪽 끝(+pad)."""
        y = self.row(f); r = np.nonzero(self.body[y])[0]
        return y, (r.max() if len(r) else self.rgt) + pad

    def put(self, y, x, col, al, over=True):
        y, x = int(round(y)), int(round(x))
        if not (0 <= y < self.H and 0 <= x < self.W): return
        if not over and self.o[y, x, 3] > 0: return
        k = al / 255.0
        if self.o[y, x, 3] == 0: self.o[y, x, :3] = col; self.o[y, x, 3] = al
        else: self.o[y, x, :3] = self.o[y, x, :3] * (1 - k) + np.array(col) * k; self.o[y, x, 3] = max(self.o[y, x, 3], al)

    def star(self, y, x, L, D=0, core=255):
        C = self.C; self.put(y, x, C["core"], core)
        for s in range(1, L + 1):
            f = 1 - s / (L + 1); col = C["core"] if s <= L // 2 else C["hi"]
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)): self.put(y + dy * s, x + dx * s, col, int(100 + 155 * f))
        for s in range(1, D + 1):
            for dy, dx in ((1, 1), (1, -1), (-1, 1), (-1, -1)): self.put(y + dy * s, x + dx * s, C["mid"], int(80 + 120 * (1 - s / (D + 1))))

    def ring(self, y, x, r, al=220, dotted=False, sy=1.0, arc=None):
        n = max(8, int(2 * np.pi * r * 1.3))
        for t in range(n):
            a = t / n * 2 * np.pi
            if arc and not (arc[0] <= a <= arc[1] or arc[0] <= a - 2 * np.pi <= arc[1]): continue
            if dotted and t % 2: continue
            self.put(y + np.sin(a) * r * sy, x + np.cos(a) * r, self.C["hi"] if t % 3 else self.C["core"], al)

    def glow(self, y, x, r, al):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                d = (dy * dy + dx * dx) ** .5
                if d <= r: self.put(y + dy, x + dx, self.C["mid"], int(al * (1 - d / (r + 1))), over=False)

    def sparks(self, y, x, n, r0, r1, al=210, up=0.0):
        for t in range(n):
            a = self.rng.random() * 2 * np.pi; r = self.rng.uniform(r0, r1)
            self.put(y + np.sin(a) * r - up, x + np.cos(a) * r, self.C["core"] if t % 3 == 0 else self.C["hi"], al)

    def dust(self, y, x, n, spread, al=150):
        for t in range(n):
            dx = self.rng.uniform(-spread, spread); dy = -abs(self.rng.normal(0, 1.2))
            for ox, oy in ((0, 0), (1, 0), (0, -1)): self.put(y + dy + oy, x + dx + ox, self.C["dust"], al, over=False)

    def streak(self, y, x0, x1, al=230, w=1):
        n = max(1, abs(x1 - x0))
        for s in range(n + 1):
            x = x0 + (x1 - x0) * s / n; f = s / n
            self.put(y, x, self.C["core"] if f > .7 else self.C["hi"] if f > .35 else self.C["mid"], int(70 + 185 * f))
            for o in range(1, w):
                for sg in (1, -1): self.put(y + sg * o, x, self.C["mid"], int((40 + 120 * f) / o), over=False)

    def out(self): return np.clip(self.o.round(), 0, 255).astype(np.uint8)


def _efx(kind, pal=None, t0=None):
    def f(anim, a, i, oldpng):
        if not anim.startswith("attack"): return a
        F = _Fx(a, anim, i, pal); S = F.S * (1.5 if t0 is not None else 1)   # 보스 효과는 1.5배(처음 시안이 몸에 비해 작았음)
        T = i - (t0 if t0 is not None else 0)   # 보스: 때리는 장 기준
        if not F.body.any(): return a
        if kind == "shield":                    # 방패 밀치기: 4~6장 충격파 + 발밑 먼지
            if F.side:
                y, x = F.front(.55, 2)
                if i == 4: F.star(y, x, 3, 2); F.ring(y, x, 4, 230, arc=(-1.2, 1.2))
                if i == 5: F.ring(y, x + 1, 6, 200, arc=(-1.1, 1.1)); F.ring(y, x + 1, 3, 150, dotted=True, arc=(-1.2, 1.2))
                if i == 6: F.ring(y, x + 3, 8, 110, dotted=True, arc=(-1.0, 1.0))
            elif not F.north:
                y, x = F.row(.6), F.cx - 2
                if i == 4: F.star(y, x, 4, 2)
                if i == 5: F.ring(y, x, 5, 210); F.star(y, x, 1)
                if i == 6: F.ring(y, x, 8, 110, dotted=True)
            else:
                if i == 5: F.ring(F.top + 3, F.cx, 6, 160, dotted=True, sy=.5)
            if i in (5, 6): F.dust(F.bot, F.cx, 7 if i == 5 else 4, (F.rgt - F.lft) * .6, 150 if i == 5 else 90)
        elif kind == "spear":                   # 창 찌르기: 2~5장 창끝 섬광
            if F.side:
                rows = np.nonzero(F.body[:, F.rgt])[0]; y = int(rows.mean()) if len(rows) else F.row(.5); x = F.rgt + 1
                if i == 2: F.star(y, x, 1)
                if i == 3: F.star(y, x + 1, 4, 2); F.streak(y, x - 8, x - 2, 160)
                if i == 4: F.ring(y, x + 1, 4, 200, dotted=True); F.star(y, x + 1, 2)
                if i == 5: F.sparks(y, x + 2, 6, 2, 6, 130)
            elif not F.north:
                y, x = F.bot - 3, F.cx + (F.rgt - F.cx) // 2
                if i == 2: F.star(y, x, 1)
                if i == 3: F.star(y, x, 4, 2)
                if i == 4: F.ring(y, x, 4, 190, dotted=True); F.star(y, x, 2)
                if i == 5: F.sparks(y, x, 6, 2, 6, 130)
            else:
                if i == 3: F.star(F.top - 2, F.cx, 3, 1)
                if i == 4: F.sparks(F.top - 2, F.cx, 6, 1, 5, 140)
        elif kind == "orb":                     # 주술: 1~5장 구슬이 차올라 터짐 + 발밑 문양 고리
            c = a[..., :3].astype(int); up = np.zeros_like(F.body); up[:F.row(.5)] = True
            m = F.body & up & (c[..., 0] >= 180) & (c[..., 2] >= 180) & (c[..., 1] <= 150)
            if m.any():                         # 가장 밝은 자홍 칸 둘레(머리털·옷 보라에 끌려가지 않게)
                ys_, xs_ = np.nonzero(m); k_ = np.argmax(c[ys_, xs_, 0] + c[ys_, xs_, 2] - (ys_ - F.top) * 6)
                near = (abs(ys_ - ys_[k_]) <= 3) & (abs(xs_ - xs_[k_]) <= 3)
                oy, ox = int(round(ys_[near].mean())), int(round(xs_[near].mean()))
            else: oy, ox = F.top + 2, F.rgt - 2
            if 1 <= i <= 5: F.glow(oy, ox, 2 + min(i, 3), [150, 190, 225, 245, 170][i - 1])
            if 2 <= i <= 4:                     # 구슬 자체도 하얗게 달아오름(옛 장처럼)
                for dy in range(-2, 3):
                    for dx in range(-2, 3):
                        if dy * dy + dx * dx <= 4 + (i == 4) * 2: F.put(oy + dy, ox + dx, F.C["core"], [0, 0, 150, 200, 230][i])
            if i in (2, 3): F.sparks(oy, ox, 4 + i * 2, 3, 6, 200)
            if i == 4: F.star(oy, ox, 4, 2); F.sparks(oy, ox, 10, 4, 8, 190)
            if i == 5: F.sparks(oy, ox, 8, 5, 10, 120, up=2)
            if not F.north and 2 <= i <= 5:
                F.ring(F.bot - 1, F.cx, 7 + (i > 3), [0, 0, 140, 200, 200, 110][i], dotted=True, sy=.3)
        elif kind == "charge":                  # 돌진: 1~5장 발밑 먼지, 3~4장 머리 앞 충격
            if F.side:
                if 1 <= i <= 5: F.dust(F.bot, F.lft - 1, 4, 3, 140 - i * 15)
                y, x = F.front(.3, 2)
                if i == 3: F.star(y, x, 4, 2); F.ring(y, x, 4, 220, arc=(-1.3, 1.3))
                if i == 4: F.ring(y, x + 1, 6, 150, dotted=True, arc=(-1.2, 1.2)); F.sparks(y, x + 2, 5, 3, 7, 140)
            elif not F.north:
                y, x = F.row(.4), F.cx
                if i == 3: F.star(y, x, 4, 2)
                if i == 4: F.ring(y, x, 6, 160, dotted=True)
                if 1 <= i <= 5: F.dust(F.bot, F.cx, 5, (F.rgt - F.lft) * .5, 130 - i * 12)
            else:
                if 1 <= i <= 5: F.dust(F.bot, F.cx, 4, (F.rgt - F.lft) * .5, 110)
        elif kind == "jelly":                   # 젤리 침 뱉기: 3~5장 보라 방울
            if F.side:
                y, x = F.front(.55, 1)
                x = min(x, F.W - 8)                     # 판(48칸) 밖으로 나가면 안 보임 -> 몸 앞 좁은 자리 안에서
                for k, d in enumerate({3: (0, 2), 4: (1, 3, 5), 5: (3, 6)}.get(i, ())):
                    F.glow(y - k % 2, x + d, 1, 240); F.put(y - k % 2, x + d, F.C["core"] if k == 0 else F.C["hi"], 255)
                if i == 4: F.sparks(y, x + 3, 5, 2, 4, 200)
            elif not F.north:
                y, x = F.row(.75), F.cx
                if i == 3: F.star(y, x, 2); F.sparks(y, x, 6, 2, 5, 220)
                if i == 4: F.ring(y, x, 5, 210, dotted=True); F.sparks(y, x, 8, 3, 7, 190)
                if i == 5: F.sparks(y, x, 6, 5, 9, 120)
        elif kind == "web":                     # 거미줄 쏘기: 3~6장 흰 거미줄 덩이가 날아가 퍼짐
            W_ = (235, 230, 245)
            def web(y, x, r, al):
                for k in range(6):
                    t = k / 6 * 2 * np.pi
                    for s in range(1, r + 1): F.put(y + np.sin(t) * s, x + np.cos(t) * s, W_, al)
                F.ring(y, x, max(1, r - 1), al - 40, dotted=True); F.put(y, x, (255, 255, 255), 255)
            if F.side:
                y, x = F.front(.7, 1)
                if i == 3: F.streak(y, x, x + 3, 200)
                if i == 4: F.streak(y, x, x + 6, 150); web(y, x + 8, 1, 230)
                if i == 5: web(y, x + 11, 3, 220)
                if i == 6: web(y, x + 12, 4, 130)
            elif not F.north:
                y, x = F.bot - 1, F.cx
                if i == 4: web(y, x, 2, 230)
                if i == 5: web(y, x, 4, 210)
                if i == 6: web(y, x, 5, 120)
        elif kind == "arrow":                   # 화살: 4~6장 보라 빛 화살이 날아감
            if F.side:
                y, x = F.front(.45, 0)
                if i == 4: F.star(y, x, 2, 1)
                if i == 5: F.streak(y, x - 2, x + 9, 230); F.star(y, x + 9, 1)
                if i == 6: F.streak(y, x + 8, x + 16, 160)
            elif not F.north:
                y, x = F.row(.45), F.cx
                if i == 4: F.star(y, x, 3, 1)
                if i == 5: F.ring(y, x, 3, 180, dotted=True)
        elif kind == "slam":                    # 내려찍기: T0 섬광, T1 바닥 충격 고리+먼지, T2 퍼지는 고리
            if F.side: y, x = F.bot - 1, F.front(.85, int(3 * S))[1]
            elif not F.north: y, x = F.bot, F.cx
            else: y, x = F.bot, F.cx
            if T == 0: F.star(y - 2 * S, x, int(4 * S), int(2 * S))
            if T == 1: F.ring(y, x, 6 * S, 230, sy=.35); F.star(y - S, x, int(2 * S)); F.dust(y, x, int(10 * S), 6 * S, 170)
            if T == 2: F.ring(y, x, 10 * S, 130, dotted=True, sy=.35); F.dust(y, x, int(6 * S), 9 * S, 110)
        elif kind == "charge2":                 # 돌진(멧돼지): T-2~T2 발밑 먼지, T0 머리 앞 충격
            if F.side:
                if -2 <= T <= 2: F.dust(F.bot, F.lft - 1, int(5 * S), 3 * S, 150 - abs(T) * 20)
                y, x = F.front(.45, int(2 * S))
            else:
                if -2 <= T <= 2: F.dust(F.bot, F.cx, int(5 * S), (F.rgt - F.lft) * .5, 140)
                y, x = F.row(.4), F.cx
            if not F.north:
                if T == 0: F.star(y, x, int(4 * S), int(2 * S)); F.ring(y, x, 4 * S, 220, arc=(-1.3, 1.3) if F.side else None)
                if T == 1: F.ring(y, x + (S if F.side else 0), 7 * S, 140, dotted=True, arc=(-1.2, 1.2) if F.side else None); F.sparks(y, x, int(6 * S), 3 * S, 8 * S, 150)
        elif kind == "xslash":                  # X자 베기: T0 첫 사선, T1 X 섬광, T2 흐려짐
            if F.side: y, x = F.front(.45, int(4 * S))
            elif not F.north: y, x = F.row(.5), F.cx
            else: y, x = F.row(.3), F.cx
            L = int(8 * S)
            def diag(sgn, al, dotted=False):
                for s_ in range(-L, L + 1):
                    if dotted and s_ % 2: continue
                    f_ = 1 - abs(s_) / (L + 1)
                    F.put(y + s_, x + sgn * s_, F.C["core"] if f_ > .55 else F.C["hi"], int(al * (.4 + .6 * f_)))
                    for o_ in (1, -1):              # 굵게(3칸): 가운데 흰빛, 양옆 보라
                        F.put(y + s_, x + sgn * s_ + o_, F.C["hi"] if f_ > .4 else F.C["mid"], int(al * .8 * (.3 + .7 * f_)))
            if T == 0: diag(1, 240)
            if T == 1: diag(1, 230); diag(-1, 250); F.star(y, x, int(3 * S), int(S)); F.glow(y, x, int(2 * S), 200)
            if T == 2: diag(1, 120, True); diag(-1, 130, True); F.sparks(y, x, int(8 * S), 2 * S, 7 * S, 130)
        elif kind == "arc":                     # 검 궤적: T0 진한 반달, T1 옅은 반달
            r = (F.bot - F.top) * .5; cy = F.row(.45)
            if F.side: cx, arc = F.cx, (-1.35, 1.35)
            elif not F.north: cx, arc = F.cx, (np.pi + .25, 2 * np.pi - .25)
            else: cx, arc = F.cx, (np.pi + .25, 2 * np.pi - .25)
            if T == 0: F.ring(cy, cx, r, 240, arc=arc); F.ring(cy, cx, r - 1, 200, arc=arc); F.ring(cy, cx, r - 2, 120, arc=arc)
            if T == 1: F.ring(cy, cx, r + 1, 150, dotted=True, arc=arc); F.ring(cy, cx, r, 110, arc=arc)
        elif kind == "spell":                   # 지팡이 주문: T-2~T1 구슬 빛이 차오름, T1 터짐, T2 가루
            c = a[..., :3].astype(int); up = np.zeros_like(F.body); up[:F.row(.55)] = True
            key = np.array(F.C["key"]); d_ = np.abs(c - key).sum(-1)
            m = F.body & up & (d_ < 120)
            if m.any():                         # 지팡이 구슬 = 몸 가운데서 가장 먼 쪽(버섯 갓·옷 색에 끌려가지 않게)
                ys_, xs_ = np.nonzero(m); k_ = np.argmax(np.abs(xs_ - F.cx) * 3 - d_[ys_, xs_] * .05)
                near = (abs(ys_ - ys_[k_]) <= 3) & (abs(xs_ - xs_[k_]) <= 3)
                oy, ox = int(round(ys_[near].mean())), int(round(xs_[near].mean()))
            else: oy, ox = F.top + 2, F.rgt - 2
            if -2 <= T <= 1: F.glow(oy, ox, int((3 + T) * S), [100, 150, 190, 220][T + 2])
            if T in (-1, 0): F.sparks(oy, ox, int(6 * S), 3 * S, 7 * S, 200)
            if T == 1: F.star(oy, ox, int(5 * S), int(2 * S)); F.sparks(oy, ox, int(12 * S), 4 * S, 10 * S, 190)
            if T == 2: F.sparks(oy, ox, int(9 * S), 5 * S, 12 * S, 120, up=2 * S)
            if not F.north and -1 <= T <= 2: F.ring(F.bot - 1, F.cx, 8 * S, [150, 210, 200, 110][T + 1], dotted=True, sy=.3)
        elif kind == "arrow2":                  # 보스 화살: T0 시위 섬광, T1~T2 빛 화살이 날아감
            if F.side:
                y, x = F.front(.42, 0)
                if T == 0: F.star(y, x, int(3 * S), int(S))
                if T == 1: F.streak(y, x - int(3 * S), x + int(12 * S), 240, 2); F.star(y, x + int(12 * S), int(2 * S)); F.glow(y, x + int(12 * S), int(S), 200)
                if T == 2: F.streak(y, x + int(10 * S), x + int(22 * S), 160, 2)
            elif not F.north:
                y, x = F.row(.5), F.cx
                if T == 0: F.star(y, x, int(4 * S), int(S))
                if T == 1: F.ring(y, x, 4 * S, 190, dotted=True); F.star(y, x, int(2 * S))
        return F.out()
    return f


for _s, _k in (("EliteShieldSoldier", "shield"), ("EliteSentinel", "spear"), ("EliteShaman", "orb"), ("EliteOrcCharger", "charge"),
               ("EliteJelly", "jelly"), ("EliteSpider", "web"), ("EliteSkeletonArcher", "arrow")):
    POST[_s] = (lambda prev, fx: (lambda *x: fx(x[0], prev(*x), *x[2:])))(POST.get(_s, _elite), _efx(_k))


def _reaper_string(rgb):
    """11-27 사용자 "사신궁수 활줄 색 튀는 것(흰색이었다가 검은색)": 원본 시위가 1~2px 하늘색이라 줄이면 흰 바탕과 섞여 흰색·검은 테두리로 바뀜,
    끊긴 시위 틈으로 활 안쪽 흰 바탕이 이어져 흰 삼각형이 남음 -> 줄이기 전에 하늘색 시위를 한 칸 굵기(원본 ~9px)로 두껍게."""
    import cv2
    c = rgb.astype(int)
    cyan = (c[..., 2] >= 190) & (c[..., 1] >= 170) & (c[..., 0] <= 140)
    big = cv2.dilate(cyan.astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
    white = c.min(-1) >= 215
    put = big & (white | cyan)                     # 흰 바탕(시위 옆)만 칠함 - 몸·활대는 그대로
    out = rgb.copy(); out[put] = (60, 225, 235)
    return out


def _reaper_white(anim, a, *_):
    """활 안쪽 흰 바탕이 남은 덩어리(아주 밝은 무채색 3칸+)를 투명하게."""
    import cv2
    c = a[..., :3].astype(int); w = (a[..., 3] > 0) & (c.min(-1) >= 228) & (c.max(-1) - c.min(-1) < 30)
    n, lab, st, _ = cv2.connectedComponentsWithStats(w.astype(np.uint8), connectivity=4)
    a = a.copy()
    for k in range(1, n):
        if st[k, cv2.CC_STAT_AREA] >= 3: a[lab == k] = 0
    return a


PRE = {"Boss6": _reaper_string}
POST["Boss6"] = _reaper_white


POST["Boss7"] = _ninja_back
POST["Chanter"] = _chanter
POST["Rogue"] = _rogue
POST["EliteRogue"] = lambda *x: _rogue(x[0], _elite(*x), *x[2:])
POST["EliteChanter"] = lambda *x: _elite(x[0], _chanter(*x), *x[2:])
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
            rgb = H.load_rgb(p)
            if spr in PRE: rgb = PRE[spr](rgb)
            bg = H.background(rgb); iw = inner_white(rgb) & ~bg
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
        rs = [(b[1] - b[0] + 1) / (o[1] - o[0] + 1) for _, _, b, o, _ in fl]
        sheet = os.path.exists(os.path.join(os.path.dirname(groups[anim][0]), "sheet_1.png"))
        for j, (rgb, cls, b, o, shp) in enumerate(fl):
            r = rs[j]; c = base[d]
            if sheet:   # 11-27 4장 묶음: 묶음마다 GPT가 그린 크기·해상도가 달라(거울기사 뒤 공격 7~8장 작아짐, 리치왕 낱장+묶음 섞임) -> 묶음 가운데값 기준
                c = float(np.median(rs[j // 4 * 4: j // 4 * 4 + 4]))
            t = 0 if sheet else .12   # 묶음은 GPT가 한 그림에 같은 크기로 그림 -> 묶음 하나에 한 배율(옛 장 키는 깃털·조각 빠짐 등으로 들쭉날쭉해 장마다 나누면 거울기사 7장만 작아짐)
            pt = float(np.clip(r, c * (1 - t), c * (1 + t)))
            ox, oy = best_phase(rgb, cls, pt)
            col, fb, fm = H.cells(rgb, cls, pt, ox, oy, 2)
            op = fb >= .5
            # 11-12 틈 메우기 뺌: 다리·갈비뼈·활 사이 빈 틈이 검은 테두리 색으로 메워져 해골궁수가 시커메짐(사용자)
            H.add_outline(col, op)
            out.append((col, op, o, shp))
        frames[anim] = out
    pal = H.make_palette(np.concatenate([c[op] for fl in frames.values() for c, op, _, _ in fl]), NCOL)
    # 11-19 사용자 "고퀄은 고퀄만, 일반은 일반만" - 게임 폴더에 넣을 때 새 그림은 Sprites/<Spr>HQ (클래식은 옛 그림 그대로, SpriteAnimator.Folder)
    dst = os.path.join(OUT, spr + ("" if os.environ.get("HQ_RES") else "HQ")); n = 0
    os.makedirs(dst, exist_ok=True)
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


# 11-27 사용자 "보스공격들 이펙트들 추가": 보스 공격 동작에 직접 그린 효과(때리는 장 t0 기준). 킹슬라임은 옛 그림 유지라 뺌.
_BOSS_FX = {
    "Boss1": ("charge2", {"core": (255, 245, 225), "hi": (255, 200, 120), "mid": (235, 140, 60), "dust": (130, 105, 80)}, 4),
    "Boss2": ("slam", {"core": (255, 250, 220), "hi": (255, 215, 110), "mid": (230, 150, 50), "dust": (120, 100, 75)}, 4),
    "Boss3": ("slam", {"core": (235, 250, 255), "hi": (140, 215, 255), "mid": (70, 150, 230), "dust": (125, 120, 120)}, 4),
    "Boss5": ("spell", {"core": (250, 255, 220), "hi": (200, 255, 120), "mid": (200, 90, 230), "key": (220, 90, 230)}, 3),
    "Boss6": ("arrow2", {"core": (235, 255, 255), "hi": (120, 240, 245), "mid": (40, 180, 200)}, 3),
    "Boss7": ("xslash", {"core": (255, 245, 255), "hi": (225, 170, 255), "mid": (150, 90, 230)}, 3),
    "Boss8": ("slam", {"core": (255, 250, 225), "hi": (255, 190, 90), "mid": (230, 110, 40), "dust": (125, 95, 75)}, 4),
    "Boss9": ("spell", {"core": (240, 255, 235), "hi": (140, 255, 150), "mid": (60, 200, 110), "key": (60, 200, 90)}, 4),
    "Boss10": ("arc", {"core": (245, 252, 255), "hi": (170, 225, 255), "mid": (110, 170, 240)}, 3),
}
for _s, (_k, _p, _t) in _BOSS_FX.items():
    POST[_s] = (lambda prev, fx: (lambda *x: fx(x[0], prev(*x), *x[2:]) if prev else fx(*x)))(POST.get(_s), _efx(_k, _p, _t))


if __name__ == "__main__":
    SKIP = {"몹_01_슬라임"}   # 11-13 사용자 "슬라임은 고퀄 저퀄 같은거(저퀄)" - 게임 폴더엔 안 넣음(이름 직접 주면 넣음)
    for s in sys.argv[1:] or sorted(os.path.basename(p) for p in glob.glob(os.path.join(H.AUTO, "*몹_*")) + glob.glob(os.path.join(H.AUTO, "보스_*")) if os.path.basename(p) not in SKIP):
        run(s)
