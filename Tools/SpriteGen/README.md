# SpriteGen

5층부터 나오는 새 몹 4종(고블린 주술사·오크 돌격병·분열 젤리·그림자 도적)의 도트 그림 생성 스크립트. Python 3만 있으면 된다(PIL 불필요).

```
python Tools/SpriteGen/draw_mobs.py Assets/Resources/Sprites preview.png
```

- `Assets/Resources/Sprites/{Shaman,OrcCharger,Jelly,Rogue}/`에 48x48 PNG를 덮어쓴다(idle 4 / walk 4 / attack 3 프레임 × south·east·west·north, west는 east 좌우 반전).
- `.meta`는 건드리지 않는다 - 기존 파일을 덮어쓰면 GUID가 그대로라 Unity 참조가 안 깨진다. 프레임을 새로 추가하면 Unity가 새 `.meta`를 기본 텍스처로 만드므로, 기존 `.meta`를 복사해 GUID만 바꿔서 Sprite 설정을 맞출 것.
- `preview.png`는 확인용 미리보기(몹마다 한 줄, 4배 확대) - 저장소에 넣지 않는다.
- 몹 모양은 `draw_shaman` / `draw_orc` / `draw_jelly` / `draw_rogue`, 색은 각 함수 맨 위 `hexc(...)` 값.
