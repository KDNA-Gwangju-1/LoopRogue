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
from codex_batch import MOBS

SPR = os.path.join(H.ROOT, "Assets", "Resources", "Sprites")
OUT = os.environ.get("HQ_RES") or SPR
DIRS = {"앞": "south", "옆": "east", "뒤": "north"}
NCOL = 24
import mob_bowstring
def _archer(anim, a):
    """11-12 사용자 "다른 동작 활줄도 옆 공격과 맞춰": 옆(동쪽) 동작은 시위 정리, 나머지는 활 옆 흰 시위만 같은 색."""
    return mob_bowstring.fix(a)[0] if anim.endswith("_east") else mob_bowstring.fix_any(a)[0]


POST = {"SkeletonArcher": _archer}   # 장마다 덧손질(동쪽 고친 뒤 서쪽은 뒤집기)
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
    spr = next(s for s, n, _ in MOBS if f"몹_{n}" == label)
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
                if inbox[m_].mean() <= .5: keep |= m_
            put = keep & (can[..., 3] == 0)                              # 옛 그림자·효과 조각(몸에 안 가리는 곳만)
            can[put] = extra[put]
            if spr in POST: can = POST[spr](anim, can)
            H.save_png(can, os.path.join(dst, f"{anim}_{i}.png")); n += 1
            if anim.endswith("_east"):
                H.save_png(can[:, ::-1].copy(), os.path.join(dst, f"{anim[:-5]}_west_{i}.png")); n += 1
    print(label, "->", spr, f"{n}장", "칸 크기", {d: round(v, 1) for d, v in base.items()})


if __name__ == "__main__":
    for s in sys.argv[1:] or sorted(os.path.basename(p) for p in glob.glob(os.path.join(H.AUTO, "몹_*"))):
        run(s)
