"""Codex가 뽑은 고퀄 용사(codex_batch.py 결과) -> 게임 그림 + 바탕화면 정리 폴더 (11-05~06 art-style-option)
사용법: python hq_export.py [몸 세트 폴더 이름...] (기본: 자동\\ 아래 104장 다 된 몸_* 전부)

- 몸: 바탕화면\\gpt 그림\\고퀄\\자동\\몸_{번호}_{등급}\\{동작}_{방향}\\01~08.png, 회전베기\\01~08.png
  배경(흰/검정) 지우고, 분홍(#FF00FF) 칼 자리표시는 떼어 내고 그 자리(몸 안쪽)는 주변 색으로 메운다.
  크기: 대기·걷기는 키 48칸, 공격·대쉬·회전은 머리카락 면적으로(투구 등급은 틀릴 수 있어 세트 대기 크기 ±12%로 묶음).
  발 밑 58줄, 머리 가운데 맞춤, 세트마다 공통 32색.
  -> Assets/Resources/Sprites/HeroHQ(일반) / HeroHQ{등급 번호}(고급=1 ...) + weapon_anchor.txt
- 칼 위치 표 weapon_anchor.txt: "그림이름 x y 각도번호 뒤?" - 분홍 칼의 칼막이 자리(64칸 도트 좌표), 칼끝 방향(위에서 시계 방향 5.625도 x 번호),
  칼막이~칼끝이 몸에 가려져 있으면 뒤=1. 게임(HeroLook.UpdateHqSword)이 이 표를 보고 칼 그림을 붙인다.
- 칼 그림: 자동\\무기\\01~10 -> HeroHQSword{0~9}/a_0~63.png (64칸, 가운데 = 칼막이, 칼막이~칼끝 = SWORD_LEN칸, 손잡이는 주먹에 가려지니 뺌)
- 바탕화면\\{날짜} 용사 고퀄\\{세트}\\: 원본(Codex 그림), 인게임(png), GIF\\도트(게임처럼 칼 붙임)·GIF\\원본"""
import os, sys, glob, uuid, shutil, datetime, math
import numpy as np
import cv2
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TEMPLATE = os.path.join(ROOT, "Assets", "Resources", "Sprites", "HeroBody0", "idle_east_0.png.meta")
RES = os.environ.get("HQ_RES") or os.path.join(ROOT, "Assets", "Resources", "Sprites")  # 시험할 땐 다른 폴더로
HOME = os.path.expanduser("~")
DESK = os.path.join(HOME, "OneDrive", "Desktop")
AUTO = os.path.join(DESK, "gpt 그림", "고퀄", "자동")
WEAP = os.path.join(AUTO, "무기")
DATE_DIR = os.environ.get("HQ_DATE_DIR") or os.path.join(DESK, "고퀄 스프라이트")  # 11-06 사용자: 날짜 폴더 대신 하나로
C, CW, FEET, NCOL = 64, 96, 58, 32
PAD = (CW - C) // 2
ANGLES = 64  # 11-06 32(11.25도)는 돌아가 보여서 5.6도 간격으로
SWORD_LEN = 20  # 칼막이~칼끝 도트 수(모든 등급 공통) - 11-06 26은 손을 가려 커 보인다(사용자)

REPLACE = {"idle_west_3": "idle_west_2"}  # 망친 장 -> 대신 쓸 장(모든 세트)
# 세트별 망친 장 -> 대신 쓸 장. 11-07 초월 대쉬_앞 05는 몸이 반만 하게 그려짐(Codex 한도 걸려 다시 뽑기 전까지 옆 장으로)
SET_REPLACE = {"몸_08_초월": {"dash_south_4": "dash_south_3"}}
# 세트별 손으로 고친 칸: (장 이름들, (x, y), 색). 11-07 사용자 - 초월 대기 앞 0~2장 (28,30) 검정 -> 피부(3~7장과 같게)
# 색 None = 지움. 11-11 사용자 - 영원 대쉬 옆 4번째 장 손 밑 외곽선: (28~30,47) 검정, 그 밑 (28~30,48) 지움
SET_PIXEL_FIX = {"몸_08_초월": [([f"idle_south_{i}" for i in range(3)], (28, 30), (253, 201, 159))],
                 "몸_09_영원": [(["dash_west_3"], (28, 47), (5, 8, 23)), (["dash_west_3"], (28, 48), None), (["dash_west_3"], (29, 47), (5, 8, 23)), (["dash_west_3"], (30, 47), (5, 8, 23)),
                               (["dash_west_3"], (29, 48), None), (["dash_west_3"], (30, 48), None)]}
KINDS = {"대기": "idle", "걷기": "walk", "공격": "attack", "대쉬": "dash"}
DIRS = {"앞": "south", "옆": "west", "뒤": "north"}


# ---------------- 그림 한 장 정리 ----------------
def load_rgb(p):
    im = Image.open(p)
    if im.mode == "RGBA":
        a = np.asarray(im).astype(np.int32)
        rgb = a[..., :3].copy()
        rgb[a[..., 3] < 128] = 255
        return rgb
    return np.asarray(im.convert("RGB")).astype(np.int32)


def background(rgb):
    """테두리에 이어진 배경색(흰/검정) 영역."""
    H, W = rgb.shape[:2]
    corners = np.concatenate([rgb[:16, :16].reshape(-1, 3), rgb[:16, -16:].reshape(-1, 3), rgb[-16:, :16].reshape(-1, 3), rgb[-16:, -16:].reshape(-1, 3)])
    bg = np.median(corners, axis=0)
    tol = 30 if bg.mean() < 60 else 60
    cand = (np.abs(rgb - bg).sum(-1) < tol * 1.7).astype(np.uint8) * 255
    im = Image.fromarray(cand, "L").copy()  # fromarray 그대로면 floodfill이 안 먹는다
    for x, y in [(0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1), (W // 2, 0), (W // 2, H - 1), (0, H // 2), (W - 1, H // 2)]:
        if im.getpixel((x, y)) == 255:
            ImageDraw.floodfill(im, (x, y), 128, thresh=0)
    return np.asarray(im) == 128


# 11-07 초월 갑옷 테두리가 (210,70,249) 자홍이라 예전 기준이면 칼 자리표시(250,3,251)로 잡혔다(audit 49장 어긋남).
# 이런 세트는 더 빡빡하게: 초록 40 미만, 빨강·파랑 차이 30 미만. 예전 세트 결과가 바뀌지 않게 세트 이름으로만 켬
STRICT_MAGENTA = {"몸_08_초월"}
_strict = False


def magenta(rgb, strict=None):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    if _strict if strict is None else strict:
        return (r > 200) & (b > 200) & (g < 40) & (np.abs(r - b) < 30)
    # 자리표시는 순수 #FF00FF - 보라 갑옷(영웅)이 섞이지 않게 빡빡하게(빨강·파랑 둘 다 높고 비슷, 초록 낮음)
    return (r > 190) & (b > 190) & (g < 90) & (np.abs(r - b) < 60)


def cells(rgb, cls, p, ox, oy, extra=0):
    """칸마다 (색, 몸 비율, 분홍 비율). cls: 0 배경 1 몸 2 분홍. extra = 바깥 여유 칸."""
    H, W = rgb.shape[:2]
    gh, gw = int((H - oy) // p) + 2 * extra, int((W - ox) // p) + 2 * extra
    col = np.zeros((gh, gw, 3)); fb = np.zeros((gh, gw)); fm = np.zeros((gh, gw))
    for j in range(gh):
        y0 = oy + (j - extra) * p
        yA, yB = max(0, int(round(y0))), min(H, int(round(y0 + p)))
        if yB <= yA:
            continue
        ya, yb = int(round(y0 + p * .2)), int(round(y0 + p * .8))
        for i in range(gw):
            x0 = ox + (i - extra) * p
            xA, xB = max(0, int(round(x0))), min(W, int(round(x0 + p)))
            if xB <= xA:
                continue
            c = cls[yA:yB, xA:xB]
            fb[j, i] = (c == 1).mean(); fm[j, i] = (c == 2).mean()
            if fb[j, i] >= .5:
                xa, xb = int(round(x0 + p * .2)), int(round(x0 + p * .8))
                blk = rgb[max(0, ya):yb, max(0, xa):xb][cls[max(0, ya):yb, max(0, xa):xb] == 1]
                if len(blk) == 0:
                    blk = rgb[yA:yB, xA:xB][c == 1]
                col[j, i] = np.median(blk, axis=0)
    return col, fb, fm


def dilate(m, r=1):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out |= np.roll(np.roll(m, dy, 0), dx, 1)
    return out


def erode(m, r=1):
    return ~dilate(~m, r)


def fill_holes(col, op, hole):
    """hole 칸을 이웃 불투명 칸 색으로 채워 나간다."""
    col = col.copy(); op = op.copy(); hole = hole & ~op
    for _ in range(20):
        if not hole.any():
            break
        acc = np.zeros(col.shape); n = np.zeros(op.shape)
        for dy, dx in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            o = np.roll(np.roll(op, dy, 0), dx, 1); c = np.roll(np.roll(col, dy, 0), dx, 1)
            acc += c * o[..., None]; n += o
        take = hole & (n > 0)
        col[take] = acc[take] / n[take][:, None]; op |= take; hole &= ~take
    return col, op


def biggest_blob(mask, cell=8):
    """제일 큰 덩어리(칼날+칼막이)만 - 보라 보석·무늬처럼 멀리 떨어진 분홍 점이 축을 비틀지 않게(영웅 서클릿 보석)."""
    H, W = mask.shape
    g = mask[:H // cell * cell, :W // cell * cell].reshape(H // cell, cell, W // cell, cell).any((1, 3))
    lab = np.zeros(g.shape, int); best, best_n, cur = 0, 0, 0
    for sy, sx in zip(*np.nonzero(g)):
        if lab[sy, sx]:
            continue
        cur += 1; stack = [(sy, sx)]; lab[sy, sx] = cur; n = 0
        while stack:
            y, x = stack.pop(); n += 1
            for yy in range(y - 1, y + 2):
                for xx in range(x - 1, x + 2):
                    if 0 <= yy < g.shape[0] and 0 <= xx < g.shape[1] and g[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = cur; stack.append((yy, xx))
        if n > best_n:
            best, best_n = cur, n
    keep = np.kron(lab == best, np.ones((cell, cell), bool)) if best else np.zeros_like(g)
    out = np.zeros_like(mask); out[:keep.shape[0], :keep.shape[1]] = keep
    return mask & out


LAST_SURE = False
EYE_LOG = []  # (동작, 장, 찍은 눈 수) - 검사용


def sword_geometry(mask, body, prefer=None):
    """분홍 칼: (칼막이 점, 칼끝 방향 단위벡터, 칼막이~칼끝 길이) - 없으면 None.
    방향: 몸(주먹)에 닿아 있는 끝 = 손잡이, 떨어진 끝 = 칼끝(11-06 - 칼막이 넓이로 정하면 주먹이 칼막이를 가릴 때 뒤집혔다).
    prefer(칼끝이 가야 할 대략의 방향)를 주면 그쪽으로(앞뒤 장과 정반대로 뒤집힌 장 바로잡기).
    칼막이: 손잡이 쪽 절반에서 제일 넓은 곳."""
    mask = biggest_blob(mask)
    ys, xs = np.nonzero(mask)
    if len(xs) < 300:
        return None
    pts = np.stack([xs, ys], 1).astype(float)
    m = pts.mean(0); u = np.linalg.svd(pts - m, full_matrices=False)[2][0]
    t = (pts - m) @ u; v = (pts - m) @ np.array([-u[1], u[0]])
    lo, hi = t.min(), t.max()
    near = dilate(body[::4, ::4], 4)  # 몸 가까이(약 16px)
    def touch(end_sel):
        p = (pts[end_sel] / 4).astype(int)
        p[:, 0] = p[:, 0].clip(0, near.shape[1] - 1); p[:, 1] = p[:, 1].clip(0, near.shape[0] - 1)
        return near[p[:, 1], p[:, 0]].mean() if len(p) else 0
    span = hi - lo
    th, tl = touch(t > hi - span * .15), touch(t < lo + span * .15)
    if abs(th - tl) < .15:  # 양쪽 다 몸에 붙음/떨어짐(칼이 몸 앞을 가로지름) -> 예전처럼 칼막이(넓은 곳)가 가까운 쪽이 손잡이
        allb = np.linspace(lo, hi, 41)
        wa = np.array([np.ptp(v[(t >= allb[k]) & (t < allb[k + 1])]) if ((t >= allb[k]) & (t < allb[k + 1])).sum() > 3 else 0 for k in range(40)])
        c0 = (allb[int(np.argmax(wa))] + allb[int(np.argmax(wa)) + 1]) / 2
        th, tl = (1, 0) if c0 - lo > hi - c0 else (0, 1)
    # 11-06 팔을 뻗은 장(회전·뒤 공격)에서 칼끝도 망토·몸 근처라 '닿은 쪽' 판정이 뒤집혔다(사용자: 중간에 칼이 돌아가 있음)
    # -> 몸 중심에서 확실히 더 먼 끝(칼 길이의 35% 넘게 차이)이 칼끝. 애매할 때만 위 판정.
    global LAST_SURE
    LAST_SURE = False
    by, bx = np.nonzero(body)
    if len(bx):
        bc = np.array([bx.mean(), by.mean()])
        dh, dl = np.linalg.norm(m + u * hi - bc), np.linalg.norm(m + u * lo - bc)
        if abs(dh - dl) > span * .35:
            th, tl = (0, 1) if dh > dl else (1, 0)
            LAST_SURE = True  # 이웃 장 바로잡기가 덮어쓰지 않게
    if prefer is not None:
        th, tl = (1, 0) if u @ prefer < 0 else (0, 1)
    if th > tl:  # hi 끝이 손잡이 -> 칼끝은 lo 쪽
        u = -u; t = -t; v = -v; lo, hi = -hi, -lo
    # 이제 lo = 손잡이 쪽, hi = 칼끝
    bins = np.linspace(lo, lo + span * .5, 21)
    w = np.array([np.ptp(v[(t >= bins[k]) & (t < bins[k + 1])]) if ((t >= bins[k]) & (t < bins[k + 1])).sum() > 3 else 0 for k in range(20)])
    k = int(np.argmax(w)); cg = (bins[k] + bins[k + 1]) / 2
    # 11-06 각도는 칼날만으로 다시(칼막이가 섞이면 축이 비틀린다)
    blade = t > cg + (hi - cg) * .3
    ub = u
    if blade.sum() > 50:
        P = pts[blade]; ub = np.linalg.svd(P - P.mean(0), full_matrices=False)[2][0]
        if ub @ u < 0:
            ub = -ub
    return m + u * cg, ub, hi - cg


def find_eyes(rgb, cls, pt):
    """11-06 원본에서 눈(피부로 둘러싸인 진한 파란 덩어리, 머리카락 범위 안) 찾기 -> [(가운데 x, y, 눈동자 색, 속눈썹 색)].
    줄일 때 칸 경계가 원본 도트와 어긋나 눈이 1칸/2칸으로 들쭉날쭉 깨지던 것(사용자) - 찾은 자리에 같은 모양으로 다시 찍는다."""
    import cv2
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    body = cls == 1
    eye = body & (b - r > 70) & (b > 100) & (r < 90) & (g < 120)
    skin = body & (r > 200) & (g > 140) & (g < 215) & (b > 100) & (b < 195) & (r - b > 40) & (r - g > 15)
    hair = body & (r > 180) & (g > 120) & (b < 110) & (r - b > 90)
    if hair.sum() < 100 or skin.sum() < 100:
        return []
    hy, hx = np.nonzero(hair)
    n, lab, st, cen = cv2.connectedComponentsWithStats(eye.astype(np.uint8), 8)
    rad = max(2, int(pt * .6))
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * rad + 1, 2 * rad + 1))
    cand = []
    for i in range(1, n):
        x, y, w, h, area = st[i]
        if not (.3 * pt * pt <= area <= 4 * pt * pt) or w > 2.5 * pt or h > 3 * pt:
            continue
        cx, cy = cen[i]
        if not (hy.min() <= cy <= hy.max() and hx.min() <= cx <= hx.max()):
            continue
        y0, y1, x0, x1 = max(0, y - rad), y + h + rad, max(0, x - rad), x + w + rad
        m = (lab[y0:y1, x0:x1] == i).astype(np.uint8)
        ring = (cv2.dilate(m, k) > 0) & (m == 0)
        sk = skin[y0:y1, x0:x1][ring].mean() if ring.any() else 0
        if sk < .2:
            continue
        iris = np.median(rgb[lab == i], axis=0)
        above = rgb[max(0, y - int(pt * .7)):y, x:x + w].reshape(-1, 3)
        dark = above[above.sum(1) < 200]
        lash = np.median(dark, axis=0) if len(dark) > 5 else np.array([25, 25, 50])
        cand.append((sk, cx, cy, iris, lash, area, (x, y, w, h)))
    # 한 눈이 위아래 두 덩어리(눈동자+반사광)로 잡힌 것 - 가로로 2.5칸 안이면 큰 쪽만(옆모습 눈이 두 번 찍히던 것). 앞모습 두 눈은 5칸 넘게 떨어져 있다
    cand.sort(key=lambda c: -c[5])
    keep = []
    for c in cand:
        if all(abs(c[1] - k[1]) > 2.5 * pt for k in keep):
            keep.append(c)
    keep.sort(key=lambda c: -c[0])
    return [c[1:5] + (c[6],) for c in keep[:2]]


def resample_eyes(col, op, eyes, rgb, cls, pt, ox, oy, extra):
    """11-06 눈 둘레만 칸 격자를 원본 눈 도트 경계(눈동자 왼쪽 끝·위 끝)에 맞춰 다시 자르고, 칸마다 제일 많은 색으로.
    눈 모양(속눈썹·흰자·눈동자)은 원본 그대로 두고 칸 경계가 어긋나 깨지는 것만 막는다(사용자: 눈은 안 바뀌어도 된다, 안 깨지게만)."""
    n = 0
    for cx, cy, iris, lash, (x, y, w, h) in eyes:
        done = []
        for ky in range(-2, int(np.ceil(h / pt)) + 1):
            for kx in range(-2, int(np.ceil(w / pt)) + 2):
                x0, y0 = x + kx * pt, y + ky * pt
                xa, xb, ya, yb = int(round(x0)), int(round(x0 + pt)), int(round(y0)), int(round(y0 + pt))
                if xa < 0 or ya < 0 or xb > rgb.shape[1] or yb > rgb.shape[0]:
                    continue
                m = cls[ya:yb, xa:xb] == 1
                if m.mean() < .5:
                    continue
                px = rgb[ya:yb, xa:xb][m]
                q = px // 24
                keys, inv, cnt = np.unique(q[:, 0] * 10000 + q[:, 1] * 100 + q[:, 2], return_inverse=True, return_counts=True)
                c = px[inv.ravel() == np.argmax(cnt)].mean(0)
                bi = int((x0 + pt / 2 - ox) / pt) + extra
                bj = int((y0 + pt / 2 - oy) / pt) + extra
                if 0 <= bj < col.shape[0] and 0 <= bi < col.shape[1] and op[bj, bi]:
                    col[bj, bi] = c
                    n += 1
                    done.append((bj, bi))
        # 11-06 사용자(대기_앞 28,28): 눈동자(파란 칸) 맨 아래 줄보다 아래로 삐져나온 흰자 칸은 피부색으로
        ci = int((cx - ox) / pt) + extra; cj = int((cy - oy) / pt) + extra
        blue = [(jj, ii) for jj in range(max(0, cj - 2), min(col.shape[0], cj + 3)) for ii in range(max(0, ci - 1), min(col.shape[1], ci + 2))
                if op[jj, ii] and col[jj, ii][2] - col[jj, ii][0] > 60]
        if blue:
            ib = max(b[0] for b in blue)
            for bj in range(ib + 1, min(col.shape[0], ib + 3)):
                for bi in range(max(0, ci - 2), min(col.shape[1], ci + 3)):
                    if op[bj, bi] and col[bj, bi].min() > 225:
                        nb = [col[jj, ii] for jj, ii in ((bj + 1, bi), (bj, bi - 1), (bj, bi + 1), (bj - 1, bi))
                              if 0 <= jj < col.shape[0] and 0 <= ii < col.shape[1] and op[jj, ii]
                              and col[jj, ii][0] > 200 and col[jj, ii][0] - col[jj, ii][2] > 40]
                        if nb:
                            col[bj, bi] = np.mean(nb, 0)
    return n


def iris_cells(col, op, eyes, pt, ox, oy, extra):
    """찾은 눈마다 몸 격자에서 눈동자(파란) 칸 하나(맨 위·가운데 가까운 것). 왼쪽부터."""
    res = []
    for e in eyes:
        cx, cy = e[0], e[1]
        ci = int((cx - ox) / pt) + extra; cj = int((cy - oy) / pt) + extra
        cand = [(abs(jj - cj) + abs(ii - ci), jj, ii) for jj in range(cj - 1, cj + 2) for ii in range(ci - 1, ci + 2)
                if 0 <= jj < col.shape[0] and 0 <= ii < col.shape[1] and op[jj, ii] and col[jj, ii][2] - col[jj, ii][0] > 60]
        if cand:
            _, jj, ii = min(cand)
            while jj - 1 >= 0 and op[jj - 1, ii] and col[jj - 1, ii][2] - col[jj - 1, ii][0] > 60:
                jj -= 1
            res.append((jj, ii))
    return sorted(res, key=lambda t: t[1])


def _eyeish(c):
    r, g, b = c
    return (b - r > 60) or (min(c) > 170 and b >= r - 15)


EYE_COPY_LOG = []


def copy_ref_eyes(frames):
    """11-06 앞·옆: 대기 첫 장(눈 둘/하나 제대로 찾은 장)의 눈 칸(눈동자·흰자·바로 위 속눈썹)을 기준으로,
    같은 방향 모든 장에서 눈동자 칸 위치에 맞춰 그대로 옮긴다. 원래 있던 파란·흰 칸은 피부색으로 지우고 붙임."""
    R = 2
    for d, need in (("south", 2), ("west", 1)):
        ref = next((f for f in frames.get(f"idle_{d}", []) if len(f["eye_cells"]) == need), None)
        if ref is None:
            continue
        patches = []
        for aj, ai in ref["eye_cells"]:
            cells = []
            for dj in range(-R, R + 1):
                for di in range(-R, R + 1):
                    jj, ii = aj + dj, ai + di
                    if not (0 <= jj < ref["col"].shape[0] and 0 <= ii < ref["col"].shape[1]) or not ref["op"][jj, ii]:
                        continue
                    c = ref["col"][jj, ii]
                    lash = dj == -1 and abs(di) <= 1 and c @ np.array([.299, .587, .114]) < 50
                    if _eyeish(c) or lash:
                        cells.append((dj, di, c.copy()))
            patches.append(cells)
        for g, fl in frames.items():
            if g.split("_")[1] != d:
                continue
            for idx, f in enumerate(fl):
                if f is ref or len(f["eye_cells"]) != need:
                    continue
                col, op = f["col"], f["op"]
                for (aj, ai), cells in zip(f["eye_cells"], patches):
                    box = [(aj + dj, ai + di) for dj in range(-R, R + 1) for di in range(-R, R + 1)
                           if 0 <= aj + dj < col.shape[0] and 0 <= ai + di < col.shape[1] and op[aj + dj, ai + di]]
                    skin = [col[j, i] for j, i in box if col[j, i][0] > 200 and col[j, i][0] - col[j, i][2] > 40 and not _eyeish(col[j, i])]
                    if not skin:
                        continue
                    sk = np.median(skin, 0)
                    for j, i in box:
                        if _eyeish(col[j, i]):
                            col[j, i] = sk
                    for dj, di, c in cells:
                        j, i = aj + dj, ai + di
                        if 0 <= j < col.shape[0] and 0 <= i < col.shape[1] and op[j, i]:
                            col[j, i] = c
                EYE_COPY_LOG.append(f"{g}_{idx}")


def head_template(rgb, cls, pt):
    """대기 첫 장 원본의 머리(몸 맨 위부터 18칸) 회색 본 - 배경·분홍 칼은 흰색. 1/4로 줄여 씀."""
    g = cv2.cvtColor(rgb.astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    g[cls != 1] = 255
    ys, xs = np.nonzero(cls == 1); top = ys.min(); bot = int(top + 18 * pt)
    band = cls[top:bot] == 1; cx = np.nonzero(band.any(0))[0]
    t = g[top:bot, cx.min():cx.max() + 1]
    return cv2.resize(t, (max(8, t.shape[1] // 4), max(8, t.shape[0] // 4)), interpolation=cv2.INTER_AREA)


def head_scale(tmpl, rgb, cls):
    """그 장 원본에서 대기 머리가 몇 배로 그려졌는지(0.7~1.6배 맞대보기) -> (배율, 맞은 정도)."""
    g = cv2.cvtColor(rgb.astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    g[cls != 1] = 255
    g = cv2.resize(g, (g.shape[1] // 4, g.shape[0] // 4), interpolation=cv2.INTER_AREA)
    best = (0.0, 1.0)
    for sc in np.linspace(.7, 1.6, 46):
        t = cv2.resize(tmpl, (max(4, int(tmpl.shape[1] * sc)), max(4, int(tmpl.shape[0] * sc))), interpolation=cv2.INTER_AREA)
        if t.shape[0] >= g.shape[0] or t.shape[1] >= g.shape[1]:
            continue
        r = cv2.matchTemplate(g, t, cv2.TM_CCOEFF_NORMED).max()
        if r > best[0]:
            best = (float(r), float(sc))
    return best[1], best[0]


OUTLINE_LOG = []
MANUAL_FIX = {
    # 대기_옆 첫 장(대기 옆 8장이 다 이 장에서 나옴): 코처럼 튀어나온 피부 칸 -> 테두리로 마감, 바깥 테두리 칸은 지움
    "HeroHQ": {"idle_west_0": [(27, 27, "ol"), (26, 27, "clear"), (26, 28, "clear"), (28, 29, "ol"), (29, 29, "ol"), (30, 29, "ol")]},
    # 11-06 사용자: 고급·희귀·영웅도 옆모습 같은 문제(일반과 같은 방식: 코 칸 -> 테두리, 그 바깥·아래 테두리 지움, 턱 맨 아래 피부 줄 -> 테두리)
    "HeroHQ1": {"idle_west_0": [(25, 28, "ol"), (26, 30, "ol"), (26, 31, "clear"), (24, 28, "clear"), (24, 29, "clear"), (27, 30, "ol"), (28, 30, "ol")]},
    "HeroHQ2": {"idle_west_0": [(26, 29, "ol"), (25, 29, "clear"), (25, 30, "clear"), (27, 31, "ol"), (28, 31, "ol"), (29, 31, "ol")]},
    "HeroHQ3": {"idle_west_0": [(26, 29, "ol"), (25, 29, "clear"), (26, 31, "ol"), (28, 31, "ol"), (29, 31, "ol"), (30, 31, "ol")]},
}
IDLE_N = 16                       # 대기 장 수(게임 0.15초씩 -> 한 바퀴 2.4초, HeroLook IdleSlow 4)
IDLE_HEAD = tuple(range(3, 12))   # 머리 1칸 내려간 장(먼저 내려가고 늦게 올라옴)
IDLE_BODY = tuple(range(4, 11))   # 몸(목~무릎) 1칸 내려간 장
IDLE_BLINK = 14                   # 눈 감은 장


def blink(img, eyes):
    """눈동자 칸(맨 위) 둘레 파란·흰 칸과 바로 위 속눈썹을 피부로 덮고, 눈동자 줄에 눈 감은 선(테두리색)."""
    R = 2
    a = img[..., 3] > 0
    rgb = img[..., :3].astype(int)
    dark = a & (rgb.sum(-1) < 90)
    olc = np.median(rgb[dark], 0) if dark.any() else np.array([9, 13, 33])
    for aj, ai in eyes:
        box = [(aj + dj, ai + di) for dj in range(-R, R + 1) for di in range(-R, R + 1)
               if 0 <= aj + dj < img.shape[0] and 0 <= ai + di < img.shape[1] and a[aj + dj, ai + di]]
        skin = [rgb[j, i] for j, i in box if rgb[j, i][0] > 200 and rgb[j, i][0] - rgb[j, i][2] > 40 and not _eyeish(rgb[j, i])]
        if not skin:
            continue
        sk = np.median(skin, 0)
        cols = sorted({i for j, i in box if _eyeish(rgb[j, i])})
        for j, i in box:
            if _eyeish(rgb[j, i]) or (j == aj - 1 and i in cols and dark[j, i]):
                img[j, i, :3] = sk
        for i in cols:
            img[aj, i, :3] = olc


def add_outline(col, op):
    """11-06 바깥(투명)과 맞닿은 칸인데 어둡지 않은 칸 = 검은 테두리가 빠진 곳 -> 그 장 테두리 색(남색)으로.
    크기는 그대로(바깥으로 늘리지 않음), 원본이 테두리 있던 자리는 손대지 않음."""
    pad = np.pad(op, 1)
    edge = op & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    lum = col @ np.array([.299, .587, .114])
    dark = edge & (lum < 50)
    ol = np.median(col[dark], 0) if dark.sum() >= 5 else np.array([12., 17., 43.])
    miss = edge & (lum >= 50)
    col[miss] = ol
    return int(miss.sum())


def hair_size(rgb, body):
    """금발 면적의 제곱근(그림마다 캐릭터 크기가 달라서 이걸로 맞춘다 - 자세가 바뀌어도 머리 크기는 그대로)."""
    ys = np.nonzero(body)[0]
    top, h = ys.min(), np.ptp(ys)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    hair = body & (r > 170) & (g > 120) & (b < 120) & (r - b > 80)
    hair[int(top + h * .5):] = False
    return math.sqrt(max(1, hair.sum())), h


# ---------------- 팔레트·저장 ----------------
def make_palette(pixels, n):
    pixels = pixels.astype(np.uint8)
    side = int(np.ceil(np.sqrt(len(pixels))))
    buf = np.zeros((side * side, 3), np.uint8); buf[:len(pixels)] = pixels
    buf[len(pixels):] = pixels[0]
    q = Image.fromarray(buf.reshape(side, side, 3)).quantize(colors=n, method=Image.Quantize.MAXCOVERAGE, kmeans=3, dither=Image.Dither.NONE)
    return np.array(q.getpalette()[:n * 3]).reshape(-1, 3)


def snap(col, pal):
    return pal[((col[:, None, :] - pal[None]) ** 2).sum(-1).argmin(1)]


def save_png(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(np.ascontiguousarray(img), "RGBA").save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):
        m = open(TEMPLATE, encoding="utf-8").read()
        m = m.replace("de8bfc5f9c7a49c88a10e008588f84bb", uuid.uuid4().hex).replace("66019ece891942b5942d266e51bcdae9", uuid.uuid4().hex)
        open(meta, "w", encoding="utf-8", newline="\n").write(m)


# ---------------- 칼 그림(각도별) ----------------
def make_swords():
    """HeroHQSword{w}/a_{k}.png - 가운데(32,32 칸 모서리) = 칼막이, 위쪽에서 시계 방향 k*360/ANGLES도로 칼끝."""
    out = {}
    for w, p in enumerate(sorted(glob.glob(os.path.join(WEAP, "*.png")))):
        rgb = load_rgb(p); alpha = ~background(rgb)
        ys, xs = np.nonzero(alpha)
        y0, y1 = ys.min(), ys.max()
        width = np.array([alpha[y].sum() for y in range(y0, y1 + 1)])
        a, b = int(len(width) * .3), int(len(width) * .9)
        cg = y0 + a + int(np.argmax(width[a:b]))  # 칼막이 줄(제일 넓은 줄)
        band = width[cg - y0] * .6
        cg_bottom = cg
        while cg_bottom + 1 <= y1 and width[cg_bottom + 1 - y0] >= band:
            cg_bottom += 1
        blade_m = alpha.copy(); blade_m[cg_bottom + 1:] = False  # 칼날+칼막이(몸 앞)
        grip_m = alpha.copy(); grip_m[:cg_bottom + 1] = False    # 11-06 손잡이·폼멜은 따로(몸 뒤에 그려서 주먹 사이로만 보이게)
        row = alpha[cg]; cx = (np.nonzero(row)[0].min() + np.nonzero(row)[0].max()) / 2
        s = SWORD_LEN / (cg - y0)  # 원본 1px = s 도트
        pal = make_palette(rgb[alpha], 20)
        SS = 4
        g = (np.arange(C * SS) + .5) / SS - C / 2  # 칸 중심 기준 부분 표본
        gy, gx = np.meshgrid(g, g, indexing="ij")

        def render(sx, sy, ok, m):
            hit = np.zeros(gx.shape, bool); hit[ok] = m[sy[ok], sx[ok]]
            colr = np.zeros(gx.shape + (3,)); colr[ok] = rgb[sy[ok], sx[ok]]
            hit4 = hit.reshape(C, SS, C, SS); cov = hit4.mean((1, 3))
            csum = (colr.reshape(C, SS, C, SS, 3) * hit4[..., None]).sum((1, 3))
            op = cov >= .35
            img = np.zeros((C, C, 4), np.uint8)
            cc = csum[op] / np.maximum(1, hit4.sum((1, 3))[op])[:, None]
            img[op, :3] = snap(cc, pal); img[op, 3] = 255
            return img

        imgs = []
        for k in range(ANGLES):
            ang = math.radians(k * 360 / ANGLES)
            u = np.array([math.sin(ang), -math.cos(ang)]); n = np.array([math.cos(ang), math.sin(ang)])
            sx = np.round(cx + (gx * n[0] + gy * n[1]) / s).astype(int)
            sy = np.round(cg - (gx * u[0] + gy * u[1]) / s).astype(int)
            ok = (sx >= 0) & (sy >= 0) & (sx < alpha.shape[1]) & (sy < alpha.shape[0])
            blade, grip = render(sx, sy, ok, blade_m), render(sx, sy, ok, grip_m)
            grip[blade[..., 3] > 0] = 0  # 겹치는 칸은 칼날 쪽으로
            save_png(blade, os.path.join(RES, f"HeroHQSword{w}", f"a_{k}.png"))
            save_png(grip, os.path.join(RES, f"HeroHQSword{w}", f"g_{k}.png"))
            imgs.append((blade, grip))
        out[w] = (os.path.splitext(os.path.basename(p))[0], imgs)
    return out


# ---------------- 몸 세트 하나 ----------------
def set_name(folder):
    n = int(os.path.basename(folder).split("_")[1])  # 몸_01_일반 -> 1
    return "HeroHQ" if n == 1 else f"HeroHQ{n - 1}"


def export_set(folder, swords):
    global _strict
    name = set_name(folder)
    label = os.path.basename(folder)
    _strict = label in STRICT_MAGENTA
    groups = {}
    for kr, en in KINDS.items():
        for dk, de in DIRS.items():
            sub = f"{kr}_{dk}"
            if sub == "걷기_옆" and os.environ.get("HQ_WALKSIDE_V2") and glob.glob(os.path.join(folder, "걷기_옆_v2", "0*.png")):
                sub = "걷기_옆_v2"  # 11-11 시트로 한 번에 뽑아 자른 옆 걷기(walk_sheet.py)
            fs = sorted(glob.glob(os.path.join(folder, sub, "0*.png")))
            if len(fs) == 8:
                groups[f"{en}_{de}"] = fs
    fs = sorted(glob.glob(os.path.join(folder, "회전베기", "*.png")))
    if len(fs) == 8:
        groups["spin_south"] = fs
    if len(groups) < 13:
        print(f"{label}: 아직 {len(groups)}/13 동작 - 건너뜀")
        return None
    print(f"{label} -> {name}")

    pre = {}
    for g, fl in groups.items():
        pre[g] = []
        for p in fl:
            rgb = load_rgb(p)
            bg = background(rgb); mg = magenta(rgb) & ~bg
            cls = np.where(bg, 0, np.where(mg, 2, 1)).astype(np.int8)
            hs, h = hair_size(rgb, cls == 1)
            pre[g].append((p, rgb, bg, mg, cls, hs, h))
    ratio, base_pt = {}, {}
    for d in ("south", "west", "north"):
        src = pre[f"idle_{d}"]
        ratio[d] = float(np.median([hs / ((h + 1) / 48) for (_, _, _, _, _, hs, h) in src]))
        base_pt[d] = float(np.median([(h + 1) / 48 for (_, _, _, _, _, hs, h) in src]))

    head_ref = {}
    for d in ("south", "west", "north"):
        _, rgb0, _, _, cls0, _, h0 = pre[f"idle_{d}"][0]
        head_ref[d] = (head_template(rgb0, cls0, (h0 + 1) / 48), (h0 + 1) / 48)
    frames = {}
    for g, items in pre.items():
        kind, d = g.split("_")
        # 11-06 사용자: 공격할 때 캐릭터가 커졌다 작아졌다 함 + 동작끼리 크기가 안 맞음
        # -> 코덱스가 장마다 다른 크기로 그렸으니, 대기 첫 장 원본 머리를 본으로 떠서 장마다 몇 배로 그려졌는지 찾아 그만큼 축척을 맞춘다
        #    (모든 장·모든 동작의 머리가 대기와 같은 크기). 잘 안 맞는 장(고개 돌림 등)은 같은 동작 가운데값. 64칸 넘는 장이 있으면 동작 전체를 같이 줄임.
        dref = d if d in head_ref else "south"
        if kind == "idle":
            pts = [(h + 1) / 48 for _, _, _, _, _, hs, h in items]
        else:
            tm, pref = head_ref[dref]
            got = [head_scale(tm, rgb, cls) for _, rgb, _, _, cls, _, _ in items]
            pts = [pref * sc if sco >= .5 else None for sc, sco in got]
            okp = [x for x in pts if x is not None]
            med = float(np.median(okp)) if okp else pref
            pts = [x if x is not None else med for x in pts]
            print(f"  {g}: 머리 배율 " + " ".join(f"{sc:.2f}" + ("" if sco >= .5 else "?") for sc, sco in got))
        factor = 1.0
        for _attempt in range(3):
          out = []; need = []
          for p, rgb, bg, mg, cls, hs, h in items:
            pt = pts[len(out)] * factor
            ys = np.nonzero(cls == 1)[0]
            geo = sword_geometry(dilate(mg, 1) & ~bg, cls == 1)
            extra = 4
            for _ in range(4):  # 11-06 64칸에 안 들어가면(위로 FEET+1줄 넘게, 옆으로 64칸 넘게) 그 장만 살짝 줄여 다시
                oy, ox = (ys.max() + 1) % pt, 0.0
                col, fb, fm = cells(rgb, cls, pt, ox, oy, extra)
                op = fb >= .5
                inside = erode(dilate(op, 3), 3)
                col, op = fill_holes(col, op, (fm >= .25) & ~op & inside)
                cy_, cx_ = np.nonzero(op)
                hh, ww = np.ptp(cy_) + 1, np.ptp(cx_) + 1
                over = max(hh / (FEET + 1), ww / (C - 2))
                if over <= 1:
                    break
                pt *= over * 1.02
            if d != "north":  # 뒷모습은 눈 없음
                eyes = find_eyes(rgb, cls, pt); n_eyes = len(eyes)
                resample_eyes(col, op, eyes, rgb, cls, pt, ox, oy, extra)
                eye_cells = iris_cells(col, op, eyes, pt, ox, oy, extra)
            else:
                eye_cells = []
                EYE_LOG.append((g, len(out), n_eyes))
            OUTLINE_LOG.append(add_outline(col, op))  # 11-06 사용자: 검은 테두리 빠진 곳부터 채우기
            behind = False
            if geo is not None:
                C0, u, L = geo
                ts = np.arange(L * .1, L * .95, 2)
                q = (C0[None] + ts[:, None] * u[None]).round().astype(int)
                okq = (q[:, 0] >= 0) & (q[:, 1] >= 0) & (q[:, 0] < rgb.shape[1]) & (q[:, 1] < rgb.shape[0])
                c = cls[q[okq, 1], q[okq, 0]]
                nb, nm = (c == 1).sum(), (c == 2).sum()
                behind = nb > .35 * max(1, nb + nm)
            out.append(dict(eye_cells=eye_cells, rgb=rgb, pt=pt, ox=ox, oy=oy, extra=extra, col=col, op=op, geo=geo, sure=LAST_SURE, behind=behind, mg=mg, bg=bg, cls=cls))
            need.append(pt)
          grow = max(n_ / (p_ * factor) for n_, p_ in zip(need, pts))
          if kind == "idle" or grow <= 1.0001:
              break
          factor *= grow  # 제일 많이 줄여야 했던 장에 맞춰 동작 전체를 같이 줄여 다시
          print(f"  {g}: 64칸 넘는 장이 있어 동작 전체 {1 / factor:.2f}배로 다시")
        # 11-06 앞뒤 장 둘 다와 정반대(120도 넘게)로 칼끝이 간 장 = 손잡이·칼끝을 거꾸로 읽은 것 -> 앞뒤 장 방향으로 다시
        for i, f in enumerate(out):
            if f["geo"] is None or f["sure"]:
                continue
            nb = [out[j]["geo"][1] for j in ((i - 1) % 8, (i + 1) % 8) if out[j]["geo"] is not None]
            if len(nb) == 2 and all(f["geo"][1] @ v < -.5 for v in nb):
                ref = nb[0] + nb[1]
                if np.linalg.norm(ref) > .1:
                    f["geo"] = sword_geometry(dilate(f["mg"], 1) & ~f["bg"], f["cls"] == 1, prefer=ref)
                    print(f"  칼 방향 바로잡음: {g}_{i}")
        for f in out:
            del f["mg"], f["bg"], f["cls"]
        frames[g] = out

    copy_ref_eyes(frames)  # 11-06 사용자: 눈 주변은 장마다 바뀔 이유가 없다 -> 대기 첫 장 눈을 같은 방향 모든 장에
    pal = make_palette(np.concatenate([f["col"][f["op"]] for fl in frames.values() for f in fl]), NCOL)
    bodies, anchors, eyes_at = {}, {}, {}
    for g, fl in frames.items():
        for idx, f in enumerate(fl):
            op, col = f["op"], f["col"]
            cy, cx = np.nonzero(op)
            top = cy.min(); hx = int(round(cx[cy < top + 8].mean()))
            dy, dx = FEET - cy.max(), C // 2 - hx
            dx = int(np.clip(dx, 1 - cx.min(), C - 2 - cx.max()))  # 옆으로 넘치면(대쉬 망토) 머리 가운데를 포기하고 안쪽으로 민다
            can = np.zeros((C, C, 4), np.uint8)
            for (y, x), c in zip(zip(cy, cx), snap(col[op], pal)):
                ty, tx = y + dy, x + dx
                if 0 <= ty < C and 0 <= tx < C:
                    can[ty, tx, :3] = c; can[ty, tx, 3] = 255
            key = f"{g}_{idx}"
            bodies[key] = can
            eyes_at[key] = [(j + dy, i + dx) for j, i in f.get("eye_cells", [])]
            if f["geo"] is not None:
                C0, u, L = f["geo"]
                ax = (C0[0] - f["ox"]) / f["pt"] + f["extra"] + dx
                ay = (C0[1] - f["oy"]) / f["pt"] + f["extra"] + dy
                k = int(round(math.degrees(math.atan2(u[0], -u[1])) / (360 / ANGLES))) % ANGLES
                anchors[key] = (ax, ay, k, f["behind"])
            else:
                print(f"  칼 못 찾음: {key}")

    # 11-06 자세 참고 그림이 한 발 내딛는 모양이라 Codex가 걷는 자세·다른 손 칼로 그린 장 -> 옆 장으로 대신(대기는 숨쉬기라 티 안 남)
    for bad_key, use_key in list(REPLACE.items()) + list(SET_REPLACE.get(label, {}).items()):
        if use_key in bodies:
            bodies[bad_key] = bodies[use_key].copy()
            if use_key in anchors:
                anchors[bad_key] = anchors[use_key]
    for keys_, (fx_, fy_), col_ in SET_PIXEL_FIX.get(label, []):
        for k_ in keys_:
            if k_ in bodies and col_ is None: bodies[k_][fy_, fx_] = 0
            elif k_ in bodies: bodies[k_][fy_, fx_, :3] = col_; bodies[k_][fy_, fx_, 3] = 255
    # 11-06 사용자: 옆모습 대기 칼이 손이 아니라 손 앞에 떠 있고, 칼끝이 땅에 닿아 한두 칸 삐져나옴
    # -> 칼막이를 주먹(칼 자리 둘레 피부 칸 가운데)에서 칼날 쪽으로 1.5칸에 두고, 칼끝이 발바닥 2칸 위보다 내려가면 칼을 앞으로 들어 올림
    if "idle_west_0" in anchors:
        ax0, ay0, k0, b0 = anchors["idle_west_0"]
        img = bodies["idle_west_0"]; rgb = img[..., :3].astype(int)
        skin = (img[..., 3] > 0) & (rgb[..., 0] > 200) & (rgb[..., 0] - rgb[..., 2] > 40) & (rgb[..., 1] > 140) & (rgb[..., 1] < 215)
        yy, xx = np.nonzero(skin)
        near = (np.hypot(xx - ax0, yy - ay0) <= 7) & (yy > FEET - 25)
        k = k0
        if near.sum() >= 2:  # 영웅은 건틀릿이라 피부 칸이 2칸뿐
            fx, fy = xx[near].mean() + .5, yy[near].mean() + .5
            def tip_y(kk, gx, gy):
                a = math.radians(kk * 360 / ANGLES); return gy - math.cos(a) * SWORD_LEN
            def guard(kk):
                a = math.radians(kk * 360 / ANGLES); return fx + math.sin(a) * 1.5, fy - math.cos(a) * 1.5
            gx, gy = guard(k)
            while tip_y(k, gx, gy) > FEET - 2 and k % ANGLES != ANGLES * 3 // 4:  # 서쪽 = 270도까지
                k = (k + (1 if math.sin(math.radians(k * 360 / ANGLES)) < 0 else -1)) % ANGLES
                gx, gy = guard(k)
            anchors["idle_west_0"] = (gx, gy, k, b0)
            print(f"  대기 옆 칼: ({ax0:.1f},{ay0:.1f}) 각도{k0} -> 주먹({fx:.1f},{fy:.1f}) 칼막이({gx:.1f},{gy:.1f}) 각도{k}")
    # 11-06 사용자가 좌표로 짚은 칸 손보기(게임 64칸 좌표). "ol" = 테두리색, "clear" = 투명
    for key, fixes in MANUAL_FIX.get(name, {}).items():
        if key in bodies:
            img = bodies[key]
            ol = img[(img[..., 3] > 0) & (img[..., :3].astype(int).sum(-1) < 90)][:, :3]
            olc = np.median(ol, 0).astype(np.uint8) if len(ol) else np.array([9, 13, 33], np.uint8)
            for x, y, what in fixes:
                if what == "clear":
                    img[y, x] = 0
                else:
                    img[y, x, :3] = olc; img[y, x, 3] = 255
    # 11-06 사용자: 대기 모션이 너무 정신없다(코덱스가 8장을 따로 그려 장마다 조금씩 다름) -> 첫 장 하나로 숨쉬기만.
    # + "좀 더 움직이는 느낌": 16장(게임에서 한 바퀴 2.4초), 머리가 한 박자 먼저 1칸 내려가고 몸(무릎 위·손·칼)이 따라감, 한 바퀴에 한 번 깜빡임.
    neck_ref = None
    for d in ("south", "west", "north"):
        base = f"idle_{d}_0"
        if base not in bodies:
            continue
        b0 = bodies[base]; a0 = anchors.get(base)
        rows = np.nonzero(b0[..., 3].any(1))[0]
        split = FEET - 7 if a0 is None else int(min(FEET - 5, max(FEET - 9, a0[1] + 3)))
        eyes = eyes_at.get(base, [])
        if eyes:
            neck = max(e[0] for e in eyes) + 3
            neck_ref = neck if d == "south" else neck_ref
        else:
            neck = neck_ref if neck_ref is not None else rows.min() + 15
        for i in range(IDLE_N):
            h, t = int(i in IDLE_HEAD), int(i in IDLE_BODY)
            img = b0.copy()
            if t:
                img[neck + 2:split + 2] = b0[neck + 1:split + 1]
            if h:
                img[1:neck + 2] = b0[0:neck + 1]; img[0] = 0
            if i == IDLE_BLINK and eyes:
                blink(img, [(e[0] + h, e[1]) for e in eyes])
            bodies[f"idle_{d}_{i}"] = img
            if a0 is not None:
                anchors[f"idle_{d}_{i}"] = (a0[0], a0[1] + t, a0[2], a0[3])
    # 게임에 쓰기(서쪽 = 동쪽 반전도)
    lines = []
    for key, img in bodies.items():
        save_png(img, os.path.join(RES, name, key + ".png"))
        if "_west_" in key:
            save_png(img[:, ::-1].copy(), os.path.join(RES, name, key.replace("_west_", "_east_") + ".png"))
    for key, (ax, ay, k, b) in anchors.items():
        lines.append(f"{key} {ax:.2f} {ay:.2f} {k} {int(b)}")
        if "_west_" in key:
            lines.append(f"{key.replace('_west_', '_east_')} {C - ax:.2f} {ay:.2f} {(ANGLES - k) % ANGLES} {int(b)}")
    open(os.path.join(RES, name, "weapon_anchor.txt"), "w", encoding="utf-8", newline="\n").write("\n".join(sorted(lines)) + "\n")
    print(f"  몸 {len(bodies)}장(+동쪽), 칼 자리 {len(anchors)}장, 몸 뒤 {sum(a[3] for a in anchors.values())}장")
    print(f"  테두리 채운 칸: 장당 평균 {np.mean(OUTLINE_LOG):.1f}, 최대 {max(OUTLINE_LOG)}"); OUTLINE_LOG.clear()
    print(f"  기준 눈 옮겨 붙인 장: {len(EYE_COPY_LOG)}"); EYE_COPY_LOG.clear()
    from collections import Counter
    print(f"  눈 찍은 수(앞·옆 장): {dict(Counter((g.split('_')[1], n) for g, _, n in EYE_LOG))}")
    miss = [f"{g}_{i}" for g, i, n in EYE_LOG if n == 0 or (g.endswith('south') and n == 1)]
    if miss:
        print(f"  눈 못 찾은/하나만: {miss}")
    EYE_LOG.clear()

    # 바탕화면 정리
    dst = os.path.join(DATE_DIR, label)
    shutil.copytree(folder, os.path.join(dst, "원본"), dirs_exist_ok=True)
    os.makedirs(os.path.join(dst, "인게임"), exist_ok=True)
    for p in glob.glob(os.path.join(RES, name, "*.png")) + [os.path.join(RES, name, "weapon_anchor.txt")]:
        shutil.copy(p, os.path.join(dst, "인게임"))

    def composite(key, w, scale=6):
        im = Image.new("RGBA", (CW, CW), (205, 205, 210, 255))
        b = np.zeros((CW, CW, 4), np.uint8); b[PAD:PAD + C, PAD:PAD + C] = bodies[key]
        layers = [Image.fromarray(b, "RGBA")]
        if key in anchors and w in swords:
            ax, ay, k, behind = anchors[key]
            pos = (int(round(ax - C / 2 + PAD)), int(round(ay - C / 2 + PAD)))  # 칼 그림 가운데 = 칼막이
            sw = Image.fromarray(swords[w][1][k][0], "RGBA"); gr = Image.fromarray(swords[w][1][k][1], "RGBA")
            sl = Image.new("RGBA", (CW, CW), (0, 0, 0, 0)); sl.paste(sw, pos, sw)
            gl = Image.new("RGBA", (CW, CW), (0, 0, 0, 0)); gl.paste(gr, pos, gr)
            layers = [gl] + ([sl] + layers if behind else layers + [sl])  # 손잡이는 항상 몸 뒤(게임과 같게)
        for l in layers:
            im.alpha_composite(l)
        return im.convert("RGB").resize((CW * scale, CW * scale), Image.NEAREST)

    speed = {"idle": 250, "walk": 120, "attack": 90, "dash": 70, "spin": 60}
    for g, fl in frames.items():
        ms = speed[g.split("_")[0]] * 2
        for w in sorted(swords):
            if w != 0 and g != "attack_south":
                continue  # 등급별 칼은 공격 앞모습으로만
            seq = [composite(f"{g}_{i}", w) for i in range(8)]
            os.makedirs(os.path.join(dst, "GIF", "도트"), exist_ok=True)
            seq[0].save(os.path.join(dst, "GIF", "도트", f"{g}_{swords[w][0]}.gif"), save_all=True, append_images=seq[1:], duration=ms, loop=0)
        seq = [Image.fromarray(f["rgb"].astype(np.uint8)).resize((418, 418), Image.LANCZOS) for f in fl]
        os.makedirs(os.path.join(dst, "GIF", "원본"), exist_ok=True)
        seq[0].save(os.path.join(dst, "GIF", "원본", f"{g}.gif"), save_all=True, append_images=seq[1:], duration=ms, loop=0)
    return name


if __name__ == "__main__":
    swords = make_swords()
    print(f"칼 {len(swords)}등급 x {ANGLES}각도")
    sel = sys.argv[1:] or sorted(os.path.basename(p) for p in glob.glob(os.path.join(AUTO, "몸_*")))
    for s in sel:
        export_set(os.path.join(AUTO, s), swords)
    sheet = Image.new("RGB", (len(swords) * 200, 200), (255, 255, 255))
    for w, p in enumerate(sorted(glob.glob(os.path.join(WEAP, "*.png")))):
        sheet.paste(Image.open(p).convert("RGB").resize((200, 200), Image.LANCZOS), (w * 200, 0))
    os.makedirs(DATE_DIR, exist_ok=True)
    sheet.save(os.path.join(DATE_DIR, "등급별_칼.png"))
    print("정리 폴더:", DATE_DIR)
