"""Codex CLI로 고퀄 용사 그림을 밤새 한 장씩 뽑는다 (11-04 art-style-option)
사용법: python codex_batch.py [pilot|weapons|base|armor|all]

- 몸: 일반 시트(바탕화면\\gpt 그림\\일반)의 프레임을 '자세'로, 고퀄 대기 그림을 '캐릭터 디자인'으로 주고 한 장씩 다시 그리게 한다.
  칼은 분홍(#FF00FF) 단색 자리표시로 그리게 해서 나중에 손 위치·각도·앞뒤를 자동으로 뽑고, 그 자리에 등급별 칼을 겹친다.
- 칼: 등급 10개(일반~Unknown) 한 자루씩, 세워서.
- 방어구 등급: 일반 몸이 끝나고 Codex 주간 한도가 남으면 등급 순서대로(대표 3장 먼저 -> 나머지).
- 이미 있는 파일은 건너뛴다(중간에 끊겨도 다시 돌리면 이어서). 결과는 바탕화면\\gpt 그림\\고퀄\\자동
- 매 장마다 ~/.codex/sessions 로그의 rate_limits를 읽어 5시간 한도가 차면 풀릴 때까지 기다리고, 주간 한도가 차면 멈춘다."""
import os, sys, glob, json, time, shutil, subprocess, datetime
from PIL import Image

HOME = os.path.expanduser("~")
GPT = os.path.join(HOME, "OneDrive", "Desktop", "gpt 그림")
SRC = os.path.join(GPT, "일반")
HQ = os.path.join(GPT, "고퀄")
OUT = os.path.join(HQ, "자동")
WORK = os.path.join(OUT, "_work")
LOG = os.path.join(OUT, "log.txt")
GEN = os.path.join(HOME, ".codex", "generated_images")
SESS = os.path.join(HOME, ".codex", "sessions")
CODEX = shutil.which("codex") or os.path.join(HOME, ".local", "bin", "codex.cmd")
PRIMARY_MAX, WEEKLY_MAX = 97.0, 97.0

KINDS = [("대기", "idle"), ("걷기", "walk"), ("공격", "attack"), ("대쉬", "dash")]
DIRS = [("앞", "front view, facing the viewer"), ("옆", "side view, facing LEFT"), ("뒤", "back view, facing away from the viewer")]
FLIP = {("대쉬", "옆"): {4}}  # 원본 시트에서 반대로 그려진 장(gpt_hero.py TABLE과 같음)

GRADES = [
    ("01_일반", "Common", "a plain iron shortsword: grey steel blade, simple brown leather grip, small iron crossguard"),
    ("02_고급", "Uncommon", "a fine steel sword with green accents: polished blade, green-wrapped grip, bronze crossguard with a small green gem"),
    ("03_희귀", "Rare", "a knight's blue steel sword: bright blade with a blue fuller line, silver crossguard, blue sapphire pommel"),
    ("04_영웅", "Epic", "an ornate purple sword: violet-tinted blade with engraved runes, curled silver-purple crossguard, amethyst pommel"),
    ("05_전설", "Legendary", "a legendary golden sword: blade edged with gold, ornate winged gold crossguard, glowing orange gem, faint orange glow"),
    ("06_신화", "Mythic", "a mythic crimson dragon sword: red-black blade with flame pattern, dragon-wing crossguard, ruby pommel, faint red glow"),
    ("07_고대", "Ancient", "an ancient teal rune sword: stone-and-jade blade with glowing cyan runes, weathered bronze guard"),
    ("08_초월", "Transcendent", "a transcendent cosmic sword: deep indigo blade filled with tiny stars like a night sky, silver-violet crossguard, glowing indigo edge"),
    ("09_영원", "Eternal", "an eternal prismatic sword: white crystal blade shimmering with rainbow colors along the edge, pearl-white and gold guard"),
    ("10_Unknown", "Unknown", "an unknown deep-space sword (11-12 user: outer-space concept): the blade is a window into open space - jet-black void filled with a swirling galaxy nebula in deep blue, cyan and warm orange-gold, scattered tiny white stars, a thin glowing white-gold edge like an event horizon; a small ringed planet set in the crossguard, black and gold hilt. NO magenta or pink anywhere"),
]
ARMOR = [
    ("02_고급", "green-trimmed leather and chainmail armor, green cloth tunic, simple leather cap"),
    ("03_희귀", "polished blue steel plate armor, blue cloak, open-faced steel helmet"),
    ("04_영웅", "ornate purple and silver plate armor with engravings, short purple cape, winged circlet"),
    ("05_전설", "shining golden plate armor with orange accents, red-orange cape, golden open helmet with a crest"),
    ("06_신화", "crimson dragon-scale armor with black trim, horned red helmet that leaves the face visible"),
    ("07_고대", "ancient teal-and-bronze armor with glowing cyan runes, weathered jade circlet"),
    ("08_초월", "cosmic indigo armor with tiny stars like a night sky, glowing violet trim, starry crown-like helm"),
    ("09_영원", "pearl-white crystal armor shimmering with rainbow highlights, white-gold trim, radiant halo-like circlet"),
    ("10_Unknown", "unknown deep-space armor (outer-space concept, different from a plain starry night): jet-black void plates with swirling galaxy nebula patterns in deep blue, cyan and warm orange-gold, tiny white stars, glowing white-gold trim like an event horizon, a small orbiting planetary ring floating around the head like a halo, a short black cape whose inside shows a galaxy; the face must stay fully visible, no hood, no mask; NO magenta or pink anywhere"),
]


def log(msg):
    line = f"[{datetime.datetime.now():%m-%d %H:%M:%S}] {msg}"
    print(line, flush=True)
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def rate_limits():
    files = sorted(glob.glob(os.path.join(SESS, "**", "*.jsonl"), recursive=True), key=os.path.getmtime)
    for f in reversed(files[-5:]):
        rl = None
        for ln in open(f, encoding="utf-8", errors="ignore"):
            if '"rate_limits"' in ln:
                try:
                    j = json.loads(ln)
                    rl = (j.get("payload") or {}).get("rate_limits") or rl
                except Exception:
                    pass
        if rl:
            return rl
    return None


def wait_quota():
    """True = 계속, False = 주간 한도 다 씀."""
    while True:
        rl = rate_limits()
        if not rl:
            return True
        p, s = rl.get("primary") or {}, rl.get("secondary") or {}
        if s.get("used_percent", 0) >= WEEKLY_MAX and s.get("resets_at", time.time() + 1) > time.time():  # 풀린 시각이 지났으면 옛 기록이라 무시
            log(f"주간 한도 {s.get('used_percent')}% - 멈춤")
            return False
        if p.get("used_percent", 0) >= PRIMARY_MAX:
            until = p.get("resets_at", time.time() + 1800)
            if until < time.time():
                return True  # 로그는 마지막 호출 때 값이라 풀린 뒤에도 97%로 남아 있다 - 풀린 시각이 지났으면 그냥 간다
            log(f"5시간 한도 {p.get('used_percent')}% - {datetime.datetime.fromtimestamp(until):%H:%M}까지 대기")
            time.sleep(max(60, until - time.time() + 90))
            continue
        return True


def image_limit_until():
    """그림 생성 한도(usage_limit_reached - 5시간·주간 %와 따로 걸린다, 11-05 10:35)에 걸렸으면 풀리는 시각."""
    files = sorted(glob.glob(os.path.join(SESS, "**", "*.jsonl"), recursive=True), key=os.path.getmtime)
    if not files:
        return None
    txt = open(files[-1], encoding="utf-8", errors="ignore").read()
    i = txt.rfind("usage_limit_reached")
    if i < 0:
        return None
    import re
    m = re.search(r'resets_at\\*"?:\s*(\d+)', txt[i:i + 600])
    until = int(m.group(1)) if m else time.time() + 3600
    return until if until > time.time() else None


def quota_text():
    rl = rate_limits() or {}
    return f"5h {(rl.get('primary') or {}).get('used_percent')}% / 주간 {(rl.get('secondary') or {}).get('used_percent')}%"


def frame(sheet, i, n=8, flip=False):
    """시트에서 i번째 장(같은 폭으로 n등분)을 흰 정사각 캔버스에."""
    s = Image.open(sheet).convert("RGB")
    W, H = s.size
    fw = W / n
    f = s.crop((int(i * fw), 0, int((i + 1) * fw), H))
    if flip:
        f = f.transpose(Image.FLIP_LEFT_RIGHT)
    c = Image.new("RGB", (H, H), (255, 255, 255))
    c.paste(f, ((H - f.width) // 2, 0))
    return c


def run(out, prompt, images):
    if os.path.exists(out):
        return True
    if not wait_quota():
        return None
    os.makedirs(os.path.dirname(out), exist_ok=True)
    attempt = 0
    while attempt < 2:
        until = image_limit_until()
        if until:
            log(f"그림 생성 한도 - {datetime.datetime.fromtimestamp(until):%m-%d %H:%M}까지 대기")
            time.sleep(max(60, until - time.time() + 120))
        start = time.time()
        args = [CODEX, "exec", "--skip-git-repo-check", "-C", WORK, "-s", "workspace-write"]
        for im in images:
            args += ["-i", im]
        args.append("-")  # 프롬프트는 stdin으로(.cmd라 괄호·따옴표가 명령줄에서 깨진다)
        try:
            r = subprocess.run(args, input=prompt.encode("utf-8"), stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, timeout=1200)
            if r.returncode:
                log(f"codex 종료코드 {r.returncode}: {r.stderr.decode('utf-8', 'ignore')[-300:]}")
        except subprocess.TimeoutExpired:
            log(f"시간 초과: {out}")
        new = [p for p in glob.glob(os.path.join(GEN, "**", "*.png"), recursive=True) if os.path.getmtime(p) >= start - 1]
        if new:
            shutil.copy(max(new, key=os.path.getmtime), out)
            log(f"OK {os.path.relpath(out, OUT)} ({int(time.time() - start)}초, {quota_text()})")
            return True
        if image_limit_until():
            continue  # 한도 때문이면 횟수 안 세고 풀린 뒤 같은 장 다시
        attempt += 1
        log(f"그림 없음, 재시도 {attempt}: {out}")
        if not wait_quota():
            return None
    return False


DESIGN = {d: os.path.join(HQ, f"대기_{d}", "01.png") for d, _ in DIRS}
SHEET_SETS = {"몸_10_Unknown"}   # 11-12 우주 컨셉 다시 그리기 - 4장씩 묶어서(옆 걷기 v2처럼 머리·갑옷 안 흔들리게)
PLACEHOLDER = ("The sword must be drawn as a FLAT PLACEHOLDER: a simple straight sword silhouette filled with one solid pure magenta color "
               "(#FF00FF), no shading, no outline, no other colors on it, with the same length, position and angle as the sword in the pose "
               "reference, held in the same hand (draw the hand gripping it normally).")
COMMON = ("Keep the design reference's exact character design, palette, outline thickness and pixel size, same scale and framing "
          "(square canvas, character centered, feet near the bottom). Plain solid white background. "
          "No effects, no motion trails, no shadow, no text, no numbers, only one character. Use your image generation tool, do not write code.")


def strip(sheet, half, name, flips=()):
    """원본 시트 8장 중 half번째 4장을 가로 한 줄(흰 바탕)로 - 4장 묶음 그리기의 자세 참고."""
    pose = os.path.join(WORK, "pose", f"{name}_strip_{half + 1}.png")
    if not os.path.exists(pose):
        os.makedirs(os.path.dirname(pose), exist_ok=True)
        fr = [frame(sheet, half * 4 + i, flip=half * 4 + i in flips) for i in range(4)]
        h = fr[0].height; im = Image.new("RGB", (h * 4, h), (255, 255, 255))
        for i, f in enumerate(fr): im.paste(f, (i * h, 0))
        im.save(pose)
    return pose


def sheet_prompt(intro, what, half, last_img):
    p = (f"{intro} Draw a SPRITE SHEET: ONE wide landscape image with exactly 4 frames side by side in a single horizontal row "
         f"(frames {half * 4 + 1}-{half * 4 + 4} of an 8-frame {what}), each frame in the pose of the matching frame of the pose strip. "
         "Equal-width cells, the same character size in every frame, all feet on the same ground line, clear white space between frames "
         "(frames must not touch), no borders, no grid lines, no frame numbers. VERY IMPORTANT: the head, face, hair, headpiece, armor design, "
         "colors and character size are IDENTICAL in all 4 frames and identical to the design reference - only the pose changes. "
         f"{PLACEHOLDER} " + COMMON.replace("only one character", "exactly 4 copies of the character, one per frame")
         .replace("(square canvas, character centered, feet near the bottom)", "(wide canvas)"))
    if half:
        p += f" Image {last_img} = frames 1-4 of this same animation (sheet 1); its head, armor, colors and character size must match exactly."
    return p


def body_sheet_jobs(set_dir, design, look=""):
    """11-12 사용자 "이번에도 4장씩 묶어서": 동작마다 4장 시트 2장(<동작>_<방향>/sheet_1.png = 1~4장, sheet_2.png = 5~8장).
    자르기는 sheet_split.py (-> 같은 폴더 01~08.png), 그다음 hq_export.py."""
    jobs = []
    for kr, en in KINDS:
        for d, view in DIRS:
            sheet = os.path.join(SRC, kr, f"{kr}_{d}.png"); first = os.path.join(set_dir, f"{kr}_{d}", "sheet_1.png")
            for half in (0, 1):
                refs = [design[d], strip(sheet, half, f"{en}_{d}", FLIP.get((kr, d), set()))] + ([first] if half else [])
                intro = (f"Image 1 = CHARACTER DESIGN reference{look}, {view}. Image 2 = POSE strip: 4 frames of a different character - ignore "
                         "its colors, hair, face, clothes and cape; copy only body pose, limb positions, sword angle and facing. "
                         f"Every frame shows the design character {view}.")
                jobs.append((os.path.join(set_dir, f"{kr}_{d}", f"sheet_{half + 1}.png"), sheet_prompt(intro, f"{en} animation", half, 3), refs))
    sheet = os.path.join(SRC, "회전", "회전베기.png"); first = os.path.join(set_dir, "회전베기", "sheet_1.png")
    for half in (0, 1):
        refs = [design["앞"], design["옆"], design["뒤"], strip(sheet, half, "spin")] + ([first] if half else [])
        intro = (f"Images 1-3 = CHARACTER DESIGN references{look} (front, side, back). Image 4 = POSE strip: 4 frames of a different character - "
                 "copy only the body pose, facing direction and sword angle.")
        jobs.append((os.path.join(set_dir, "회전베기", f"sheet_{half + 1}.png"), sheet_prompt(intro, "spinning sword slash", half, 5), refs))
    return jobs


def body_jobs(set_dir, design, look=""):
    """(out, prompt, images) 목록 - design = {방향: 디자인 그림}."""
    jobs = []
    for kr, en in KINDS:
        for d, view in DIRS:
            sheet = os.path.join(SRC, kr, f"{kr}_{d}.png")
            for i in range(8):
                pose = os.path.join(WORK, "pose", f"{en}_{d}_{i + 1:02d}.png")
                if not os.path.exists(pose):
                    os.makedirs(os.path.dirname(pose), exist_ok=True)
                    frame(sheet, i, flip=i in FLIP.get((kr, d), set())).save(pose)
                prompt = (f"Image 1 = CHARACTER DESIGN reference{look}. Image 2 = POSE reference only (a different character - ignore its colors, "
                          f"hair, face, clothes and cape; copy only body pose, limb positions, sword angle and facing). "
                          f"Draw ONE frame: the design character in exactly the pose of image 2, {view}. This is frame {i + 1} of 8 of a "
                          f"{en} animation. {PLACEHOLDER} {COMMON}")
                jobs.append((os.path.join(set_dir, f"{kr}_{d}", f"{i + 1:02d}.png"), prompt, [design[d], pose]))
    sheet = os.path.join(SRC, "회전", "회전베기.png")
    for i in range(8):
        pose = os.path.join(WORK, "pose", f"spin_{i + 1:02d}.png")
        if not os.path.exists(pose):
            os.makedirs(os.path.dirname(pose), exist_ok=True)
            frame(sheet, i).save(pose)
        prompt = (f"Images 1-3 = CHARACTER DESIGN references{look} (front, side, back). Image 4 = POSE reference only (a different character - "
                  f"copy only the body pose, facing direction and sword angle). Draw ONE frame: the design character in exactly the pose of "
                  f"image 4. This is frame {i + 1} of 8 of a spinning sword slash. {PLACEHOLDER} {COMMON}")
        jobs.append((os.path.join(set_dir, "회전베기", f"{i + 1:02d}.png"), prompt, [design["앞"], design["옆"], design["뒤"], pose]))
    return jobs


def walkside_jobs():
    """11-11 사용자 "옆 걷기 갑옷이 꿀렁거림·얼굴 뭉개짐" -> "8장을 한 번에(4장씩 2번) 뽑고 그 위에 도트화".
    모든 몸 세트의 옆 걷기를 가로 한 줄 4칸 시트 2장으로(걷기_옆_v2/sheet_1.png = 1~4장, sheet_2.png = 5~8장).
    그림 1 = 이 세트 대기 옆 01(실제 갑옷), 그림 2 = 자세 4장 줄, 시트 2는 그림 3 = 시트 1(머리·갑옷 똑같이). 자르기·도트화는 walk_sheet.py."""
    jobs = []
    sheet = os.path.join(SRC, "걷기", "걷기_옆.png")
    for half in (0, 1):
        pose = os.path.join(WORK, "pose", f"walk_옆_strip_{half + 1}.png")
        if not os.path.exists(pose):
            os.makedirs(os.path.dirname(pose), exist_ok=True)
            fr = [frame(sheet, half * 4 + i) for i in range(4)]
            h = fr[0].height; strip = Image.new("RGB", (h * 4, h), (255, 255, 255))
            for i, f in enumerate(fr): strip.paste(f, (i * h, 0))
            strip.save(pose)
    for set_dir in sorted(glob.glob(os.path.join(OUT, "몸_*"))):
        idle = os.path.join(set_dir, "대기_옆", "01.png")
        if not os.path.exists(idle) or os.path.basename(set_dir) in SHEET_SETS:
            continue
        first = os.path.join(set_dir, "걷기_옆_v2", "sheet_1.png")
        for half in (0, 1):
            prompt = ("Image 1 = the EXACT character to draw (side view, facing LEFT, standing). Image 2 = POSE reference only: a strip of 4 walk "
                      "frames of a different character - copy only the leg and arm positions. Draw a SPRITE SHEET: ONE wide landscape image with "
                      f"exactly 4 frames side by side in a single horizontal row (frames {half * 4 + 1}-{half * 4 + 4} of an 8-frame walk cycle), "
                      "each frame the character of image 1 walking, side view facing LEFT, in the pose of the matching frame of image 2. "
                      "Equal-width cells, the same character size in every frame, all feet on the same ground line, clear white space between "
                      "frames, no borders, no grid lines, no frame numbers. VERY IMPORTANT: the head, face, hair, helmet/headpiece, torso armor and "
                      "shoulder plates are IDENTICAL in all 4 frames and identical to image 1 (same shapes, same size, same pixels, same height) - "
                      "only the legs, the arms and the cape move. Keep the face clean and simple like image 1: one eye, no nose bump. "
                      f"{PLACEHOLDER} {COMMON.replace('only one character', 'exactly 4 copies of the character, one per frame').replace('(square canvas, character centered, feet near the bottom)', '(wide canvas)')}")
            refs = [idle, os.path.join(WORK, "pose", f"walk_옆_strip_{half + 1}.png")]
            if half:
                prompt += " Image 3 = frames 1-4 of this same walk (sheet 1); its head, armor, colors and character size must match exactly."
                refs.append(first)
            jobs.append((os.path.join(set_dir, "걷기_옆_v2", f"sheet_{half + 1}.png"), prompt, refs))
    return jobs

def weapon_jobs():
    jobs = []
    for name, grade, desc in GRADES:
        prompt = ("Image 1 = style and scale reference (pixel-art knight holding a sword). Draw ONLY a single sword, no character, no hand: "
                  f"{desc}. ({grade} grade item.) Pixel art in exactly the same style, outline thickness and pixel size as the reference, "
                  "about the same length as the reference's sword. The sword stands perfectly vertical, blade pointing UP, centered on a square "
                  "canvas. Plain solid white background, no text, no shadow. Use your image generation tool, do not write code.")
        jobs.append((os.path.join(OUT, "무기", f"{name}.png"), prompt, [DESIGN["앞"]]))
    return jobs


def armor_jobs():
    jobs = []
    for name, desc in ARMOR:
        set_dir = os.path.join(OUT, f"몸_{name}")
        look = {}
        for d, view in DIRS:  # 대표 3장 먼저: 기본 디자인에 갑옷만 바꿔서
            out = os.path.join(set_dir, "_디자인", f"{d}.png")
            look[d] = out
            prompt = (f"Image 1 = the base character. Redraw the SAME character in the SAME pose, {view}, but wearing {desc}. Keep the same face, "
                      f"blond hair (a helmet or headpiece may cover part of it but must not hide the face), body proportions, sword and pixel "
                      f"size. {COMMON}")
            refs = [DESIGN[d]] + ([look["앞"]] if d != "앞" else [])
            if d != "앞":
                prompt += " Image 2 = the same armored character from the front; match its armor exactly."
            jobs.append((out, prompt, refs))
        jobs.append(("__armor_set__", set_dir, look))
    return jobs


# 11-11 사용자 "방어구 완성하면 몬스터도, 일반몹부터": 나오는 층 순서. (스프라이트 폴더, 결과 폴더 이름, 생김새)
MOBS = [
    ("Slime", "01_슬라임", "a green slime blob monster"),
    ("SkeletonArcher", "02_해골궁수", "a skeleton archer with a bow"),
    ("ShieldSoldier", "03_해골방패병", "a skeleton soldier with a large shield"),
    ("Spider", "04_거미", "a big purple dungeon spider"),
    ("Bomber", "05_고블린폭탄병", "a goblin bomber carrying a round black bomb with a fuse"),
    ("Sentinel", "06_반격병", "a steel-armored sentinel knight with a sun-emblem shield"),
    ("Shaman", "07_고블린주술사", "a goblin shaman healer with a staff"),
    ("OrcCharger", "08_오크돌격병", "a horned orc charger in red armor"),
    ("Chanter", "09_주문술사", "a purple-hooded robed spellcaster with a floating spellbook"),
    ("Jelly", "10_분열젤리", "a blue splitting jelly monster"),
    ("Rogue", "11_그림자도적", "a shadow rogue assassin with daggers"),
]
SPR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "Sprites")
MOB_DIRS = [("south", "앞", "front view, facing the viewer"), ("east", "옆", "side view, facing RIGHT"), ("north", "뒤", "back view, facing away from the viewer")]
MOB_COMMON = ("Keep the same scale and framing as the pose reference (the monster takes up the same part of the square canvas, feet/bottom at the "
              "same height). Plain solid white background. No effects, no motion trails, no shadow, no text, no numbers, only one monster. "
              "Use your image generation tool, do not write code.")


def mob_ref(set_name, key):
    """게임 스프라이트(48px) 한 장을 8배로 키워 흰 정사각 판에(자세·생김새 참고용)."""
    p = os.path.join(WORK, "mobpose", set_name, key + ".png")
    if not os.path.exists(p):
        os.makedirs(os.path.dirname(p), exist_ok=True)
        s = Image.open(os.path.join(SPR, set_name, key + ".png")).convert("RGBA")
        s = s.resize((s.width * 8, s.height * 8), Image.NEAREST)
        c = Image.new("RGB", (s.width + 64, s.height + 64), (255, 255, 255))
        c.paste(s, (32, 32), s)
        c.save(p)
    return p


def mob_jobs():
    """몹마다: 디자인 3장(앞·옆·뒤, 예전 그림을 고퀄 용사 화풍으로) -> 그 디자인으로 게임 스프라이트의 모든 장(동작 x 3방향, 서쪽 = 동쪽 뒤집기)."""
    jobs = []
    for set_name, name, desc in MOBS:
        set_dir = os.path.join(OUT, f"몹_{name}")
        look = {}
        for en, d, view in MOB_DIRS:
            out = os.path.join(set_dir, "_디자인", f"{d}.png")
            look[d] = out
            prompt = (f"Image 1 = STYLE reference (a high-quality pixel-art hero - copy only its art style: pixel size, outline thickness, shading, "
                      f"color richness). Image 2 = the MONSTER to redraw ({desc}) from an old low-resolution game sprite. Redraw this exact monster "
                      f"as high-quality pixel art in the style of image 1: same identity, silhouette, colors and pose as image 2, {view}, but with "
                      f"the detail and quality of image 1. {MOB_COMMON}")
            refs = [DESIGN["앞"], mob_ref(set_name, f"idle_{en}_0")] + ([look["앞"]] if d != "앞" else [])
            if d != "앞":
                prompt += " Image 3 = the same redrawn monster from the front; match its design exactly."
            jobs.append((out, prompt, refs))
        jobs.append(("__mob_set__", set_name, set_dir, look, desc))
    return jobs


def mob_set_jobs(set_name, set_dir, look, desc):
    jobs = []
    keys = sorted({f.rsplit("_", 1)[0] for f in os.listdir(os.path.join(SPR, set_name)) if f.endswith(".png") and not f.rsplit("_", 1)[0].endswith("west")})
    for anim in keys:
        kind, en = anim.rsplit("_", 1)
        d, view = next((d, v) for e, d, v in MOB_DIRS if e == en)
        n = len([f for f in os.listdir(os.path.join(SPR, set_name)) if f.startswith(anim + "_") and f.endswith(".png")])
        for i in range(n):
            pose = mob_ref(set_name, f"{anim}_{i}")
            prompt = (f"Image 1 = MONSTER DESIGN reference ({desc}, {view}) - keep its exact design, palette, outline and pixel size. Image 2 = POSE "
                      f"reference only (an old low-resolution sprite of the same monster - copy only the body pose, limb positions and facing). "
                      f"Draw ONE frame: the design monster in exactly the pose of image 2. This is frame {i + 1} of {n} of its '{kind}' animation. "
                      f"{MOB_COMMON}")
            jobs.append((os.path.join(set_dir, f"{kind}_{d}", f"{i + 1:02d}.png"), prompt, [look[d], pose]))
    return jobs


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "all"
    os.makedirs(WORK, exist_ok=True)
    log(f"시작 mode={mode} ({quota_text()})")
    base = os.path.join(OUT, "몸_01_일반")
    if mode == "pilot":
        jobs = [j for j in body_jobs(base, DESIGN) if j[0].endswith(os.path.join("공격_앞", "05.png"))] + weapon_jobs()[:1]
    elif mode == "weapons":
        jobs = weapon_jobs()
    elif mode == "base":
        jobs = body_jobs(base, DESIGN)
    elif mode == "armor":
        jobs = armor_jobs()
    elif mode == "mobs":
        jobs = mob_jobs()
    elif mode == "walkside":
        jobs = walkside_jobs()
    else:
        jobs = weapon_jobs() + body_jobs(base, DESIGN) + armor_jobs() + walkside_jobs() + mob_jobs()  # 방어구 -> 옆 걷기 다시(11-11) -> 일반 몹(층 순서)
    fails = 0
    for job in jobs:
        if job[0] == "__mob_set__":
            _, set_name, set_dir, look, desc = job
            if not all(os.path.exists(p) for p in look.values()):
                continue
            for out, prompt, images in mob_set_jobs(set_name, set_dir, look, desc):
                r = run(out, prompt, images)
                if r is None:
                    log("끝(한도)"); return
                fails += r is False
            continue
        if job[0] == "__armor_set__":
            _, set_dir, look = job
            if not all(os.path.exists(p) for p in look.values()):
                continue
            sub = (body_sheet_jobs if os.path.basename(set_dir) in SHEET_SETS else body_jobs)(set_dir, look, " (armored variant - keep this armor exactly)")
            for out, prompt, images in sub:
                r = run(out, prompt, images)
                if r is None:
                    log("끝(한도)"); return
                fails += r is False
            continue
        r = run(*job)
        if r is None:
            log("끝(한도)"); return
        fails += r is False
    log(f"끝 - 실패 {fails}장 ({quota_text()})")


if __name__ == "__main__":
    main()
