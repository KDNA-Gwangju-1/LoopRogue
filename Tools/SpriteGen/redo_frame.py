# 잘못 그려진 몸 그림 한 장 다시 뽑기: python redo_frame.py 몸_06_신화 공격_앞 04
# (예전 그림은 같은 폴더 _잘못뽑힘\ 으로 옮겨 둠)
import os, sys, shutil
import codex_batch as cb
setn, grp, num = sys.argv[1], sys.argv[2], sys.argv[3]
set_dir = os.path.join(cb.OUT, setn)
look = {d: os.path.join(set_dir, "_디자인", f"{d}.png") for d, _ in cb.DIRS}
target = os.path.join(set_dir, grp, f"{num}.png")
job = next(j for j in cb.body_jobs(set_dir, look, " (armored variant - keep this armor exactly)") if os.path.normcase(j[0]) == os.path.normcase(target))
if os.path.exists(target):
    bad = os.path.join(set_dir, "_잘못뽑힘"); os.makedirs(bad, exist_ok=True)
    shutil.move(target, os.path.join(bad, f"{grp}_{num}.png"))
print("결과:", cb.run(*job), target)
