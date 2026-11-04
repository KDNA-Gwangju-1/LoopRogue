"""GPT 용사 시트(흰 배경, 가로 8장) -> 도트화 -> Resources/Sprites/HeroGpt 세트 (11-04 test/gpt-hero)
사용법: python gpt_hero.py [시트 폴더(기본: 바탕화면\\gpt 그림\\일반)]

시트 폴더 구성(파일 = 방향):
  idle\\001.png 아래(앞모습) 002.png 위(뒷모습)  003.png 옆(왼쪽 봄)
  이동\\001.png 위(뒷모습)  002.png 아래(앞모습)  003.png 옆(왼쪽 봄)
  공격\\004.png 위          005.png 아래          006.png 옆
  대쉬\\위.png  위          002.png 아래          001.png 옆 (5번째 장만 반대 방향 -> 반전)
- 시트마다 GPT가 그린 크기가 달라서, 1번째 장(서 있기) 키가 65도트가 되도록 도트 크기를 시트마다 정한다
- 시트마다 색감도 달라서 걷기 색에 맞춘다(아래 색감 맞추기)
- 오른쪽 = 옆 좌우반전, 회전 베기 = 4방향 공격 장면을 이어 붙임
- 캔버스 88, PPU 88, 발 밑 76줄(몸 가운데 = 타일 가운데)
도트화는 같은 폴더 gpt_pixelize.py (흰 배경·눈 흰자 처리 포함)."""
import os, sys, subprocess, tempfile, uuid
from collections import deque
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Sprites", "HeroGpt")
TEMPLATE = os.path.join(ROOT, "Assets", "Resources", "Sprites", "HeroBody0", "idle_east_0.png.meta")
SHEETS = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.expanduser("~"), "OneDrive", "Desktop", "gpt 그림", "일반")
C, FEET, TARGET = 88, 76, 65

TABLE = [  # (동작, 방향, 파일, 반전할 장(0부터))
    ("idle", "south", r"idle\001.png", set()),  # idle 폴더는 001 = 앞모습, 002 = 뒷모습 (이동 폴더와 반대)
    ("idle", "north", r"idle\002.png", set()),
    ("idle", "west", r"idle\003.png", set()),
    ("walk", "north", r"이동\001.png", set()),
    ("walk", "south", r"이동\002.png", set()),
    ("walk", "west", r"이동\003.png", set()),
    ("attack", "north", r"공격\004.png", set()),
    ("attack", "south", r"공격\005.png", set()),
    ("attack", "west", r"공격\006.png", set()),
    ("dash", "north", r"대쉬\위.png", set()),
    ("dash", "south", r"대쉬\002.png", set()),
    ("dash", "west", r"대쉬\001.png", {4}),
    ("spin", "south", r"회전\001.png", set()),  # GPT 회전 베기(앞→왼→뒤→오른→앞). 없으면 공격 장면 이어 붙임
]
TMP = tempfile.mkdtemp(prefix="gpt_hero_")


def components_to_frames(a):
    H, W = a.shape[:2]
    op = a[..., 3] > 0
    op[:, :2] = False; op[:, -2:] = False; op[:2] = False; op[-2:] = False  # 원본 가장자리 잡티
    bounds = [round(i * W / 8) for i in range(9)]
    owner = -np.ones((H, W), int)
    seen = np.zeros((H, W), bool)
    for y in range(H):
        for x in range(W):
            if op[y, x] and not seen[y, x]:
                q = deque([(y, x)]); seen[y, x] = True; pts = []
                while q:
                    cy, cx = q.popleft(); pts.append((cy, cx))
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            ny, nx = cy + dy, cx + dx
                            if 0 <= ny < H and 0 <= nx < W and op[ny, nx] and not seen[ny, nx]:
                                seen[ny, nx] = True; q.append((ny, nx))
                mx = np.mean([p[1] for p in pts])  # 붙은 이펙트는 무게중심으로 프레임 배정
                k = max(i for i in range(8) if bounds[i] <= mx)
                for p in pts:
                    owner[p] = k
    return op, owner


def _shift_or(m, r):
    out = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            out |= np.roll(np.roll(m, dy, 0), dx, 1)
    return out


def fill_hair_holes(can, r=2):
    """머리(위쪽 40%)에 '닫기'(불투명을 r칸 넓혔다 다시 r칸 줄이기) - 폭 2r 이하 골·틈을 메운다.
    흰 머리 하이라이트가 외곽선 틈으로 바깥 흰 배경과 이어져 같이 지워지는 구멍(11-04 뒷모습 대기) 보정.
    메운 칸 색 = 가장 가까운 기존 불투명 칸 색."""
    can = can.copy()
    al = can[..., 3] > 0
    ys = np.nonzero(al)[0]
    top, bot = ys.min(), ys.max()
    lim = top + int((bot - top) * 0.4)
    pad = np.pad(al, r + 1)
    closed = ~_shift_or(~_shift_or(pad, r), r)
    closed = closed[r + 1:-(r + 1), r + 1:-(r + 1)]
    new = closed & ~al
    new[lim:] = False
    oy, ox = np.nonzero(al)
    for y, x in zip(*np.nonzero(new)):
        k = ((oy - y) ** 2 + (ox - x) ** 2).argmin()
        can[y, x, :3] = can[oy[k], ox[k], :3]
        can[y, x, 3] = 255
    return can


def prep_dark_band(path):
    """GPT가 흰 배경 대신 어두운 띠 위에 번호(1~8)·발밑 그림자까지 그린 시트(11-04 회전) -> 흰 배경으로.
    띠 = 절반 이상 어두운 줄들. 띠 색과 14 이내(배경·옅은 그림자)는 흰색, 띠 위쪽 65줄(번호)도 흰색."""
    o = np.asarray(Image.open(path).convert("RGB")).astype(int)
    rows = np.nonzero((o.mean(axis=2) < 80).mean(axis=1) > 0.5)[0]
    if len(rows) < 50:
        return path
    y0, y1 = rows.min(), rows.max()
    bgc = np.median(o[y0:y1, :8].reshape(-1, 3), axis=0)
    out = o.copy()
    band = out[y0:y1 + 1]
    band[np.abs(band - bgc).max(axis=2) <= 14] = 255
    out[max(0, y0 - 4):y0 + 65] = 255
    out[y1 - 3:y1 + 5] = 255  # 띠 아래 경계선(번진 줄)이 캐릭터 키에 잡히지 않게
    p = os.path.join(TMP, "prep.png")
    Image.fromarray(out.astype(np.uint8)).save(p)
    return p


def head_w(f):
    """머리 폭(도트) - 맨 위에서 8~16줄 사이 가장 넓은 줄. 시트마다 GPT가 그린 머리 크기가 달라서 맞출 때 쓴다."""
    a = f[..., 3] > 0
    top = np.nonzero(a)[0].min()
    return max(np.ptp(np.nonzero(a[y])[0]) + 1 for y in range(top + 8, top + 16) if a[y].any())


def sheet_frames(path, flip, scale=None):
    o = np.asarray(Image.open(path).convert("RGB")).astype(int)
    first = o[:, 5:o.shape[1] // 8]
    ys = np.nonzero((np.abs(first - 254).max(axis=2) > 40).any(axis=1))[0]
    if scale is None:
        scale = (ys.max() - ys.min() + 1) / TARGET
    src = os.path.join(TMP, "sheet.png")
    Image.open(path).convert("RGB").save(src)
    subprocess.run([sys.executable, os.path.join(HERE, "gpt_pixelize.py"), src, TMP, "48", f"{scale:.3f}"], check=True, capture_output=True)
    a = np.asarray(Image.open(os.path.join(TMP, "sheet_도트_원본크기.png")).convert("RGBA"))
    op, owner = components_to_frames(a)
    lum = a[..., :3].astype(int).mean(axis=2)
    frames, clipped = [], []
    for k in range(8):
        m = op & (owner == k)
        ys, xs = np.nonzero(m)
        top = ys.min()
        bot = np.nonzero(m & (lum < 150))[0].max()  # 발 = 이펙트 빼고 어두운 몸 맨 아래
        hx = int(round(xs[ys < top + 12].mean()))  # 머리 가운데
        dx, dy = C // 2 - hx, FEET - bot
        if xs.max() - xs.min() < C:  # 폭이 들어가면 안 잘리게 밀어 넣음
            dx = max(dx, -xs.min()); dx = min(dx, C - 1 - xs.max())
        can = np.zeros((C, C, 4), np.uint8)
        cut = False
        for y, x in zip(ys, xs):
            ty, tx = y + dy, x + dx
            if 0 <= ty < C and 0 <= tx < C:
                can[ty, tx] = a[y, x]
            else:
                cut = True
        can = fill_hair_holes(can)
        if k in flip:
            can = can[:, ::-1].copy()
        if cut:
            clipped.append(k + 1)
        frames.append(can)
    return frames, scale, clipped


tmpl = open(TEMPLATE, encoding="utf-8").read()
os.makedirs(OUT, exist_ok=True)


def save(img, name):
    path = os.path.join(OUT, name + ".png")
    Image.fromarray(np.ascontiguousarray(img), "RGBA").save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):  # guid는 처음 한 번만
        m = tmpl.replace("de8bfc5f9c7a49c88a10e008588f84bb", uuid.uuid4().hex)
        m = m.replace("66019ece891942b5942d266e51bcdae9", uuid.uuid4().hex)
        m = m.replace("spritePixelsToUnits: 64", f"spritePixelsToUnits: {C}")
        open(meta, "w", encoding="utf-8", newline="\n").write(m)


sets = {}
head_target = {}
# 걷기 먼저(1번째 장 키 65도트) -> 나머지는 1번째 장 머리 폭을 같은 방향 걷기 머리 폭에 맞춘다(11-04: 대쉬·대기 머리가 1.3~1.5배 크게 나왔음)
for kind, d, rel, flip in sorted(TABLE, key=lambda t: t[0] != "walk"):
    path = os.path.join(SHEETS, rel)
    if not os.path.exists(path):
        print(f"{kind:6s} {d:5s} {rel:14s} 없음 - 건너뜀")
        continue
    path = prep_dark_band(path)
    frames, scale, clipped = sheet_frames(path, flip)
    if kind == "walk":
        head_target[d] = head_w(frames[0])
    else:
        # 머리 폭만 믿으면 GPT가 머리카락을 더 부스스하게 그린 시트(대기)가 너무 작아져서 키 기준과 반반(제곱근)
        scale *= (head_w(frames[0]) / head_target[d]) ** 0.5
        frames, scale, clipped = sheet_frames(path, flip, scale)
    sets[(kind, d)] = frames
    print(f"{kind:6s} {d:5s} {rel:14s} 도트 {scale:.2f}px" + (f"  (그림 밖으로 잘린 장: {clipped})" if clipped else ""))


def _lab(rgb):
    c = rgb.astype(float) / 255
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    xyz = c @ np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]]).T
    xyz /= np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


# 색감 맞추기(11-04) - GPT가 시트마다 색을 다르게 그려서(대기·대쉬는 빨갛고 밝음, 걷기는 갈색) 걷다 멈추면 색이 바뀐다.
# 걷기 3방향을 기준으로: 다른 시트의 색 분포(Lab 평균·폭)를 걷기에 맞춘 뒤 걷기에 쓰인 색 중 가장 가까운 색으로 바꾼다.
ref = np.concatenate([f[f[..., 3] > 0][:, :3] for d in ("north", "south", "west") for f in sets[("walk", d)]])
ref_pal = np.unique(ref, axis=0)
ref_lab = _lab(ref_pal)
# 망토 색 기준 = 걷기 뒷모습(11-04 사용자 "모든 망토색 walk north 색으로") - 색 분포·채도 상한은 뒷모습에서,
# 바꿔 넣을 색 후보는 걷기 3방향 전체(뒷모습엔 눈 빨강이 없어서)
BASE = ("walk", "north")
ref_all = _lab(np.concatenate([f[f[..., 3] > 0][:, :3] for f in sets[BASE]]))
rmu, rsd = ref_all.mean(0), ref_all.std(0) + 1e-6
ref_chroma = np.percentile(np.hypot(ref_all[:, 1], ref_all[:, 2]), 90)
for key, frames in sets.items():
    if key == BASE:
        continue
    px = np.concatenate([f[f[..., 3] > 0][:, :3] for f in frames])
    lab = _lab(px)
    mu, sd = lab.mean(0), lab.std(0) + 1e-6
    for f in frames:
        m = f[..., 3] > 0
        cols, inv = np.unique(f[m][:, :3], axis=0, return_inverse=True)
        inv = inv.ravel()
        t = (_lab(cols) - mu) / sd * rsd + rmu
        free = ((t[:, None, :] - ref_lab[None]) ** 2).sum(-1).argmin(1)
        # 망토 빨강처럼 진한 색은 걷기의 보통 채도(상위 10%)까지만 - 안 그러면 눈 빨강으로 붙는다.
        # 얼굴(머리 높이·머리 폭 안)은 눈 빨강이 살아야 해서 채도 제한 안 함
        ch = np.hypot(t[:, 1], t[:, 2])
        k = np.minimum(1, ref_chroma / (ch + 1e-6))
        tc = t.copy(); tc[:, 1] *= k; tc[:, 2] *= k
        capped = ((tc[:, None, :] - ref_lab[None]) ** 2).sum(-1).argmin(1)
        yy, xx = np.nonzero(m)
        face = (yy < yy.min() + 26) & (np.abs(xx - C // 2) <= 12)
        f[m, :3] = np.where(face[:, None], ref_pal[free][inv], ref_pal[capped][inv])

for kind in ("idle", "walk", "attack", "dash"):
    sets[(kind, "east")] = [f[:, ::-1].copy() for f in sets[(kind, "west")]]

for (kind, d), frames in sets.items():
    for i, f in enumerate(frames):
        save(f, f"{kind}_{d}_{i}")
# 회전 베기 - 아래 → 오른쪽 → 위 → 왼쪽 → 아래, 각 방향 공격의 휘두르는 장면
if ("spin", "south") not in sets:  # GPT 회전 시트가 없을 때만 (있으면 위에서 spin_south_*로 저장됨)
    A = lambda d, k: sets[("attack", d)][k]
    spin = [A("south", 4), A("east", 4), A("east", 5), A("north", 4), A("north", 5), A("west", 4), A("west", 5), A("south", 5)]
    for i, f in enumerate(spin):
        save(f, f"spin_south_{i}")
print("완료:", OUT)
