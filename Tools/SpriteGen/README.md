# SpriteGen

5층부터 나오는 새 몹 4종(고블린 주술사·오크 돌격병·분열 젤리·그림자 도적)의 도트 그림 생성 스크립트. Python 3만 있으면 된다(PIL 불필요).

```
python Tools/SpriteGen/draw_mobs.py Assets/Resources/Sprites preview.png
```

- `Assets/Resources/Sprites/{Shaman,OrcCharger,Jelly,Rogue}/`에 48x48 PNG를 덮어쓴다(idle 8 / walk 8 / attack 8 프레임 × south·east·west·north, west는 east 좌우 반전 - 2026-10-13 4/4/3 → 8, 걷기 8장 = 왼발 4장 + 오른발 4장).
- `.meta`는 건드리지 않는다 - 기존 파일을 덮어쓰면 GUID가 그대로라 Unity 참조가 안 깨진다. 프레임을 새로 추가하면 Unity가 새 `.meta`를 기본 텍스처로 만드므로, 기존 `.meta`를 복사해 GUID만 바꿔서 Sprite 설정을 맞출 것.
- `preview.png`는 확인용 미리보기(몹마다 한 줄, 4배 확대) - 저장소에 넣지 않는다.
- 동작은 파일 위쪽 8프레임 동작표(`IDLE_BOB`·`WALK_LIFT`·`SHAMAN_RAISE` 등)로 조절한다.
- 몹 모양은 `draw_shaman` / `draw_orc` / `draw_jelly` / `draw_rogue`, 색은 각 함수 맨 위 `hexc(...)` 값.

## 9층 리치(2026-10-23)

```
python Tools/SpriteGen/draw_boss9.py Assets/Resources/Sprites/Boss9
python Tools/SpriteGen/draw_tel9.py Assets/Resources/Effects
```

- `pix.py`: 공용 도구 - 부위(칸 집합)마다 외곽선 `(18,12,20)` + 왼쪽 위 빛 명암 띠(`part`), 반투명 바닥 그림자, 빛 무리(`glow`), 미리보기 시트(`sheet`).
- `draw_boss9.py`: 64x64, idle/walk/attack 8프레임 × south·east·north(west는 east 반전). 폴더 없이 실행하면 `boss9_preview.png`만 만든다. 공격 자세는 `frames_*()`의 `A` 표(지팡이 끝 `top`·쥔 손 `grip`·폭발 `burst`)로 조절.
- `draw_tel9.py`: 예고 칸 `tel_checker`(체스판 마법)·`tel_orb`(영혼 구체) 32x32 4프레임, 흰 그림(실행 중 예고 색을 곱함). 새 폴더의 `.meta`는 `tel_axe`의 것을 복사해 GUID만 바꿨다.

## 기믹 게이지 UI(2026-10-25)

```
python Tools/SpriteGen/draw_gauge.py Assets/Resources [미리보기.png]
```

- 로스트아크 아이덴티티 게이지 느낌: `UI/Common/GaugeFrame`(20x14 양 끝 뾰족한 금장 테, 9-slice 좌우 6·위아래 4) · `GaugeFill`(8x6 흰 채움, 실행 중 색을 곱함) · `GaugeIconSlot`(18x18 둥근 금장 받침) · `GaugeShine`(흐르는 사선 광택) · `GaugeEdge`(채움 맨 앞 빛) · `GaugeGlow`(16x16 둘레 발광, 9-slice 7) - 모두 2배.
- `UI/Icons/Gimmick_{Hole,Might,Pillar,Crystal,Slime,SlimeWave,Spore,Candle,Target,Clone,Beat,Wall,Doom,Mirror,Light,Hourglass}`(16x16) - `RoomController.HudTimers`의 아이콘 이름.
- 새 `.meta`는 `BarFrame`·`Item_Bomb`의 것을 복사해 GUID·spriteID·spriteBorder만 바꿨다.
