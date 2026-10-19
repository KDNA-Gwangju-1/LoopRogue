using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>씬에 딱 하나 있는 진입점 - 에디터로 씬에 오브젝트를 직접 배치할 수 없는 환경이라,
    /// 플레이어/방/HUD/진행 매니저를 전부 여기서 코드로 만들어 연결한다. 지금 도전 중인 스테이지
    /// (StageProgress.CurrentStage)만큼만 방 시퀀스를 만든다 - 보스를 잡아야 다음 스테이지로
    /// 넘어가므로, 한 번에 10스테이지를 전부 만들어둘 필요가 없다.</summary>
    public class GameBootstrap : MonoBehaviour
    {
        public const int RoomsPerStage = 10;
        /// <summary>자동 플레이 봇의 "보스 배율 찾기" 모드 전용 - 보스 HP/ATK에 추가로 곱한다(평소엔 항상 1).</summary>
        public static float BossScaleForBotTest = 1f;

        private const float BossStatMultiplier = 1.92f; // 전 스테이지 보스 HP/ATK에 곱함 - 1.6("보스가 약하다") → ×1.2(봇 5판 결과, 트라이 횟수에 따른 골짜기 보정)

        /// <summary>스테이지별 보스 추가 보정 - 한 번 오르면 절대 안 내려가는 계단(2 ×1.1, 3 ×1.2, 4 이후 ×1.3 유지).
        /// 예전엔 특정 스테이지에만 보정을 넣었더니, 보정이 사라지는 바로 다음 스테이지는 보스 상승폭이 확 줄고
        /// (예: 4 ×1.3 → 5 ×1.0이면 4→5가 1.45/1.3 ≈ 1.12배) 앞에서 많이 죽으며 쌓인 골드까지 겹쳐 봇 테스트에서
        /// 매번 그 다음 스테이지가 "쉬운 골짜기"가 됐다. 계단식으로 바꾸고 대신 적 성장률을 1.45 → 1.41로 낮춰
        /// 스테이지 10 보스는 이전과 거의 같게 맞췄다.</summary>
        /// <summary>보스 패턴 난이도 보정 - 보스 패턴이 생긴 뒤로는 스탯보다 패턴이 난이도를 정해서(봇 30판: 돌진 0.2회,
        /// 저격 14회, 십자+저격 21회 사망), 쉬운 패턴 보스는 스탯을 올리고 어려운 패턴 보스는 내린다.
        /// 계단 보정(ExtraBossMultiplier)과는 별개로 곱한다.</summary>
        private static float BossPatternBalance(int stage) => stage switch
        {
            // "보스 배율 찾기" 봇 모드(층마다 그 층 입장 상태로 보스 세기를 바꿔가며 측정)로 목표 곡선에 맞춘 값.
            // 목표 리듬(사용자 선택): 5층 중간 보스·10층 최종 보스가 벽, 6층은 숨 돌리기 -
            // 층별 실제 평균 사망 2:3 3:4 4:5 5:15 6:5 7:8 8:10 9:12 10:20(배율 찾기 1회차 + 일반 모드 실측 곡선으로 직접 보정 2~17회차, 4회차부터 장비/영약 고정 이후, 8회차부터 스킬·몹 1.5배·흡혈 상한 이후, 12회차부터 일반 방 몹 강화 이후 - 보스 목표는 일반 방 사망과 별개로 이 값). 보스 세기 10% 차이가 사망 수를 약 40% 바꿀 만큼
            // 민감하니 손으로 크게 건드리지 말 것. 패턴이 어려운 층(6 저격, 7 X자, 8 돌진+강타)은 값이 낮다.
            // 23회차(10-16, 리치 패턴 개편 뒤 봇 bot_20261016_124602·133055: 9층 보스방 7.7·6.4, 목표 12) - 9층만 +15%(한 번 상한)
            // 22회차(10-15, 봇 100판 bot_20261015_141742: 완주 99/100, 판당 사망 102.5. 보스방 4.0/3.5/6.3/6.0/8.1/3.6/6.8/9.7/9.8/16.0) - 평소 공식, 7층은 뒤집혀서 중간값, 8층 그대로
            // 21회차(10-15, 봇 100판 bot_20261015_135646: 완주 98/100, 판당 사망 98.8. 보스방 5.2/2.0/10.1/5.9/3.2/4.5/9.3/9.5/5.9/18.2) - 평소 공식, 4층은 뒤집혀서 중간값, 6·8·10층 그대로
            // 20회차(10-15, 봇 100판 bot_20261015_132216: 오늘 기믹·패턴 변경 뒤 완주 99/100, 판당 사망 103.5. 보스방 6.3/1.0/15.7/1.9/0.0/10.2/8.0/6.2/2.9/21.6) - 평소 공식, 7·10층 그대로, 5층은 일반 공격 되살림으로 대신
            // 19회차(봇 100판 bot_20261012_160333: 완주 81/100, 판당 사망 133.9, 퍼즐 15.0개. 보스방 2.8/2.9/4.0/5.7/15.9/6.7/11.0/9.4/11.1/22.9) - 6·7층만 평소 공식으로 내림
            // 18회차(봇 100판 bot_20261012_152934: 완주 80/100, 판당 사망 141.4, 퍼즐 15.1개 - 목표 근처. 보스방 3.5/3.5/4.5/5.7/18.7/4.9/8.2/11.3/16.7/22.2) - 평소 공식, 목표와 10% 안쪽(6·7층)은 그대로
            // 17회차(봇 100판 bot_20261012_145819: 완주 68/100, 판당 사망 159.0, 퍼즐 14.6개.
            // 보스방 4.0/4.6/3.4/8.2/18.9/8.2/13.8/15.8/18.3/12.5) - 평소 공식(목표/실측)^(1/4)·±15%로 내림, 10층은 도달 53판뿐(살아남은 강한 판)이라 그대로
            // 16회차(봇 100판 bot_20261012_140110: 퍼즐을 방마다 한 번만 굴린 뒤 퍼즐 판당 13.8개, 완주 50/100·판당 사망 177.6 - 이제 너무 어려움.
            // 보스방 4.0/4.6/3.4/8.2/18.9/8.2/13.8/15.8/18.3/12.5) - 평소 공식(목표/실측)^(1/4)·±15%로 내림, 10층은 도달 53판뿐(살아남은 강한 판)이라 그대로
            // 14회차(봇 100판 bot_20261012_133114: 보스방 3.2/2.7/2.0/4.0/11.0/4.5/6.5/6.0/1.2/8.8) - 퍼즐을 방마다 한 번만 굴리게 고친 효과부터 보려고
            // 9층만 +30%(십자 띠로 고친 뒤에도 1.2 - 목표 12와 너무 동떨어짐), 나머지 그대로
            // 13회차(봇 100판 bot_20261012_124405: 판당 사망 47.6, 보스방 2.7/2.7/0.2/3.8/6.1/2.0/3.7/3.6/1.7/5.2) - 십자 베기를 몸 폭 띠로 고침(3·9층은 세기 그대로 두고 그 효과부터 봄), 나머지는 목표 비율대로(1·2층 +6%, 4층 +16%, 5~8층 +30%, 10층 +15%)
            // 12회차(2026-10-12 봇 100판 bot_20261012_121405: +15%로 판당 사망 29 -> 36뿐 - 죽을수록 퍼즐(카드·영약)이 늘어 반응이 약함) - 전 층 +30%(사용자 선택). 보스방 사망 1.7/1.4/0.1/2.5/5.2/2.2/2.8/3.2/1.6/5.5
            // 11회차(2026-10-12 봇 100판: 퍼즐 보상·3x3 보스·집 작업 합친 뒤 판당 사망 118 -> 29, 전 층 목표 미달) - 전 층 +15%(한 번 상한). 보스방 사망 1.2/1.2/0.0/1.6/3.9/1.7/2.1/2.4/1.3/5.5
            1 => 1.42f, // 1.52 // 1.75 // 2.06 // 2.14 // 2.28 // 2.45 // 2.31 // 1.78 // 1.55
            2 => 2.46f, // 2.56 // 2.31 // 2.01 // 2.09 // 2.19 // 2.44 // 2.3 // 1.77 // 1.54
            3 => 1.28f, // 1.43 // 1.68 // 1.98 // 2.04 // 2.25 // 2.16 // 1.66 // 1.44
            4 => 2.43f, // 2.54 // 2.71 // 2.36 // 2.44 // 2.48 // 2.81 // 2.42 // 1.86 // 1.62
            5 => 4.13f, // 3.59 // 3.12 // 3.3 // 3.5 // 3.71 // 2.85 // 2.19 // 1.9
            // 6~10층: 몹 16마리·방 22칸으로 성장(경험치·골드)이 늘어 보스방 사망이 줄어서(봇 100판 2026-10-11, 어제 대비 보스방
            // 사망 6층 4.0→3.1, 7층 5.9→4.8, 8층 7.3→6.3, 9층 8.3→6.3, 10층 21.4→19.6) 어제 수준으로 되돌리게 올림.
            6 => 2.25f, // 2.07 // 2.44 // 2.62 // 2.79 // 3.16 // 2.43 // 1.87 // 1.63 // 1.51
            7 => 2.86f, // 2.49(10-15 분신 동안 X자만 넣고 봇 bot_20261015_152132 7층 보스방 6.6→2.4, 목표 8 - +15%) // 2.44 // 2.53 // 2.74 // 2.89 // 3.31 // 2.55 // 1.96 // 1.7 // 1.6
            8 => 3.48f, // 3.08 // 3.18 // 3.47 // 3.89 // 2.99 // 2.3 // 2.0 // 1.93
            9 => 3.77f, // 3.28 // 3.12 // 2.71 // 2.36 // 2.56 // 2.83 // 3.15 // 2.42 // 1.86 // 1.62 // 1.5
            10 => 4.70f, // 4.45 // 4.57 // 4.39 // 3.82 // 2.94 // 2.56 // 2.5 // 1.64 -> 2.5(2026-10-10 봇 100판: 10층 사망 7.3/목표 20, 보스방 42% - 거울 깨기·보스방 벽 제거·이벤트 버프 뒤 너무 쉬워짐. 민감도 추정 x1.7 중 보수적으로 x1.52)
            _ => 1f,
        };

        private static float ExtraBossMultiplier(int stage) => stage switch
        {
            <= 1 => 1f,
            2 => 1.1f,
            3 => 1.2f,
            _ => 1.3f,
        };

        private static GameBootstrap _instance;
        private readonly HashSet<GameObject> _sceneRoots = new HashSet<GameObject>();

        private void Awake()
        {
            _instance = this;
            foreach (var go in gameObject.scene.GetRootGameObjects())
                _sceneRoots.Add(go); // 씬 파일에 원래 있던 것(카메라 등) - 다시 지을 때 남긴다
            Build();
        }

        /// <summary>자동 플레이 봇 전용 - Main 씬을 다시 로드하는 대신, 런타임에 만든 루트 오브젝트를 전부 지우고 다음 프레임에
        /// Awake와 같은 순서로 다시 짓는다. 에디터에서 씬 로드 한 번이 약 0.28초라 봇 시간의 40%가 씬 로드였다.
        /// 저장(RunProgress 등)은 전부 정적/PlayerPrefs라 씬 로드와 결과가 같다.</summary>
        public static void RebuildInPlace()
        {
            if (_instance == null)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
                return;
            }
            foreach (var go in _instance.gameObject.scene.GetRootGameObjects())
                if (!_instance._sceneRoots.Contains(go))
                    Destroy(go);
            _instance.StartCoroutine(_instance.BuildNextFrame());
        }

        private System.Collections.IEnumerator BuildNextFrame()
        {
            yield return null; // Destroy가 끝난 뒤에 짓는다(옛 오브젝트와 한 프레임 겹치지 않게)
            Build();
        }

        private void Build()
        {
            StageProgress.EnsureLoaded();
            var stage = StageProgress.CurrentStage;

            var roomGo = new GameObject("Room");
            var room = roomGo.AddComponent<RoomController>();

            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<PlayerActor>();
            player.Initialize(room);

            var hudGo = new GameObject("GameHUD");
            var hud = hudGo.AddComponent<GameHUD>();
            hud.Initialize(player);

            var rooms = BossRush.Active ? BuildBossRushRooms()
                : TutorialStage.TryBegin() ? TutorialStage.BuildRooms() : BuildStageRooms(stage); // 처음 온 사람·타이틀 [튜토리얼]
            if (TestMap.Active)
                rooms = TestMap.BuildRooms(rooms); // 에디터 메뉴 "테스트 맵" - [이벤트 전부 깐 방, 이 층 보스방]
            var loopManager = new LoopManager();
            loopManager.Initialize(room, player, hud, rooms, stage);
            TestMap.Attach(room, player);
        }

        /// <summary>스테이지 하나 = 일반 방 10개 + 보스방 1개("방도 10개 유지해야 해, 스펙업 수단을
        /// 마구 추가할 거라 좀 커져도 상관없다" 요청 - 스테이지 분할 초기엔 3개로 줄였다가 다시
        /// 늘림). 스테이지 안에서(방 1→10) 커지는 몫은 기존처럼 완만한 가산이고, 그 "방 1~10짜리
        /// 세트 전체"가 StageScaling.EnemyMultiplier(지수적, 보상 곡선보다 가파름)만큼 통째로
        /// 곱해져서 스테이지마다 세진다 - "벽" 컨셉은 그대로라 어떤 스테이지든 그 스테이지 보스는
        /// 루프(사망 리셋)가 돌아도 안 세지고, 오직 스테이지를 올려야만(=격파해야만) 다음 단계의 더
        /// 강한 벽을 만난다.</summary>
        /// <summary>플레이어 스킬(대시/회전 베기)을 넣으면서 사용자 요청으로 일반 방 몹 수를 1.5배로("스킬이 사기니 그만큼 몹 수를
        /// 늘리자"). 방 크기는 그대로라 더 빽빽해진다 - 회전 베기로 여러 마리를 한 번에 치는 상황이 자주 나오게.</summary>
        private const float EnemyCountMultiplier = 1.5f;
        // 12 -> 16, 방 크기 최대 18 -> 22(사용자 요청) - 6층 중반부터 모든 방이 12마리·18칸으로 똑같아지던 것을 10층까지 계속 커지게.
        private const int MaxEnemiesPerRoom = 16;
        private const int MaxRoomSize = 22;

        /// <summary>일반 방 몹 세기 보정(보스방 제외) - 봇 측정에서 2층부터 첫 시도에도 방을 체력 거의 100%로 끝냈다(상점·뽑기·영약
        /// 성장이 방 몹 성장보다 빠름). 목표(사용자 선택 "중간"): 일반 방 사망 층마다 1~2회, 단 8~10층은 연달아 빡센 구간으로 6~10회.
        /// 봇 요약의 "일반 방" 표로 맞춘다. 실측 결과 흡혈·처치 회복이 커서 체력은 거의 항상 가득 차 있다가 한꺼번에 무너지는 식이라
        /// "남은 체력"보다 "그 층에 간 판당 일반 방 사망"(목표 1~2회)을 기준으로 맞춘다.
        /// 1차(1층 1.3, 2층+ 공격력 2.5/체력 1.5): 그 층에 간 판당 일반 방 사망 1.1/0.9/0.4/1.3/1.7/0.7/7.0/25.0/27.3/38.8 - 7층부터 급증. 2차(7층 1.8, 8~9층 1.9, 10층 1.8, 7층+ 체력 1.2~1.3): 1.3/0.9/1.4/1.1/0.9/0.9/0.6/7.2/7.6/28.3 - 10층만 방7~9에 몰려 죽음(남은 몹 궁수 2.9·방패병 1.9·거미 1.2).</summary>
        private static float RoomEnemyAttackScale(int stage) => stage switch
        {
            // 19회차(6·10층만 평소 공식). 일반 방 사망 1.2/1.6/1.3/1.6/1.7/2.5/1.2/8.2/7.5/15.4
            // 18회차(평소 공식, 1~7층 중 1~2회 범위 안은 그대로). 일반 방 사망 1.5/1.6/1.6/2.1/1.4/1.1/2.6/5.7/10.2/15.7
            // 17회차(평소 공식). 일반 방 사망 1.4/2.2/1.7/1.4/2.1/1.3/1.0/11.2/19.9/23.3
            // 16회차(평소 공식으로 내림). 일반 방 사망 1.7/3.7/3.0/4.4/5.1/2.4/3.8/17.6/30.5/15.1
            // 13회차(1층 +13%, 2~10층 +30%). 일반 방 사망 1.2/0.5/0.3/0.8/0.6/0.3/0.5/2.4/3.9/5.1
            // 12회차(+30%, 사용자 선택). 일반 방 사망 0.8/0.6/0.2/0.4/0.4/0.2/0.4/1.6/2.2/3.1
            // 11회차(2026-10-12 봇 100판: 퍼즐 보상·3x3 보스·집 작업 합친 뒤 판당 사망 118 -> 29, 전 층 목표 미달) - 전 층 +15%(한 번 상한). 일반 방 사망 0.7/0.3/0.2/0.3/0.2/0.2/0.3/1.1/1.7/2.6
            1 => 2.17f, // 2.13 // 2.2 // 1.95 // 1.5 // 1.3
            2 => 5.56f, // 6.12 // 7.2 // 5.54 // 4.26 // 3.7
            3 => 6.36f, // 6.56 // 7.72 // 5.94 // 4.57 // 3.97
            4 => 5.86f, // 6.37 // 6.26 // 7.37 // 5.67 // 4.36 // 3.79
            5 => 5.55f, // 6.04 // 7.11 // 5.47 // 4.21 // 3.66
            6 => 5.98f, // 6.8 // 6.56 // 7.38 // 5.68 // 4.37 // 3.8
            7 => 5.33f, // 6.11 // 5.52 // 6.5 // 5 // 3.85 // 3.35
            8 => 6.38f, // 6.06 // 6.82 // 8.02 // 6.17 // 4.75 // 4.13
            9 => 5.85f, // 6.04 // 7.11 // 8.37 // 6.44 // 4.95 // 4.3
            _ => 4.95f, // 5.38 // 5.88 // 6.92 // 7.49 // 5.76 // 4.43 // 3.85 // 8~10층은 갈수록 빡센 마지막 고비(사용자: "후반으로 갈수록 더 어려워져야" - 일반 방 사망 목표 8층 약 7, 9층 약 9, 10층 약 11)
        };

        private static float RoomEnemyHealthScale(int stage) => stage switch
        {
            1 => 1f,
            <= 6 => 1.5f,
            7 => 1.2f,
            _ => 1.3f,
        };

        /// <summary>그 층 보스의 최대 체력·공격력 - 보스방과 히든 몹 기억하는 기사(보스 체력, 보스 공격력의 75%)가 같이 쓴다.</summary>
        public static (float Health, float Attack) BossStats(int stage)
        {
            var stageMultiplier = StageScaling.EnemyMultiplier(stage);
            var extraBoss = ExtraBossMultiplier(stage) * BossPatternBalance(stage) * BossScaleForBotTest;
            return (150f * stageMultiplier * BossStatMultiplier * extraBoss, 12f * stageMultiplier * BossStatMultiplier * extraBoss);
        }

        private static List<RoomDefinition> BuildStageRooms(int stage)
        {
            var stagePower = stage - 1; // 0-based - 방 크기/몹 수 가산에 그대로 씀.
            var rooms = new List<RoomDefinition>();

            for (var i = 1; i <= RoomsPerStage; i++)
            {
                // 크기/몹 수는 배율을 안 곱한다 - 격자가 지수적으로 커지면 방이 순식간에 감당 안
                // 될 만큼 거대해진다. 이 둘은 예전처럼 스테이지당 고정폭으로만 커진다.
                // 방 크기 +4(사용자: "맵이 작아서 불합리한 느낌" - 카메라를 당기고 시야를 줄이면서 같이 키움), 최대 14 -> 18.
                var size = Mathf.Min(10 + stagePower + (i - 1) / 2, MaxRoomSize);
                var (hp, atk, enemyCount) = RoomEnemyStats(stage, i);

                var room = MakeRoom($"Stage {stage}-{i}", size, size, enemyCount, enemyHp: hp, enemyAtk: atk);
                room.EnemyKinds = BuildEnemyKinds(stage, i, enemyCount);
                rooms.Add(room);
            }

            var bossSize = Mathf.Min(12 + stagePower / 2, 18);
            var (bossHp, bossAtk) = BossStats(stage);

            var bossRoom = MakeRoom($"Stage {stage} Boss", bossSize, bossSize, 1,
                enemyHp: bossHp, enemyAtk: bossAtk, isBossRoom: true);
            bossRoom.BossPatterns = BossBrain.StagePatterns(stage);
            rooms.Add(bossRoom);

            return rooms;
        }

        /// <summary>일반 방 몹 기준 체력·공격력(종류 배율 곱하기 전)과 몹 수 - 방 번호 1~RoomsPerStage.</summary>
        private static (float Hp, float Atk, int Count) RoomEnemyStats(int stage, int room)
        {
            var stagePower = stage - 1;
            var stageMultiplier = StageScaling.EnemyMultiplier(stage);
            var count = Mathf.Min(Mathf.RoundToInt((2 + stagePower + (room - 1) / 2) * EnemyCountMultiplier), MaxEnemiesPerRoom);
            var hp = (8f + (room - 1) * 2f) * stageMultiplier * RoomEnemyHealthScale(stage);
            var atk = (2f + (room - 1) * 0.4f) * stageMultiplier * RoomEnemyAttackScale(stage);
            return (hp, atk, count);
        }

        /// <summary>그 층 마지막 일반 방 기본 몹(슬라임)의 체력·공격력 - 10층 기사의 소환 졸개(10-19 사용자 "10층 마지막 방 애들급").</summary>
        public static (float Hp, float Atk) LastRoomEnemySpec(int stage)
        {
            var (hp, atk, _) = RoomEnemyStats(stage, RoomsPerStage);
            return (hp, atk);
        }

        /// <summary>보스 러시(BossRush) - 시작 층부터 10층까지 보스방을 차례로(크기·체력·공격력·패턴은 그 층 보스방 그대로).</summary>
        private static List<RoomDefinition> BuildBossRushRooms()
        {
            var rooms = new List<RoomDefinition>();
            for (var s = BossRush.FirstStage; s <= BossRush.LastStage; s++)
            {
                var size = Mathf.Min(12 + (s - 1) / 2, 18);
                var (hp, atk) = BossStats(s);
                var room = MakeRoom($"Boss Rush {s}", size, size, 1, enemyHp: hp, enemyAtk: atk, isBossRoom: true);
                room.BossPatterns = BossBrain.StagePatterns(s);
                room.BossStage = s;
                rooms.Add(room);
            }
            return rooms;
        }

        /// <summary>궁수 배치 - 스테이지 1은 방 6부터 딱 1마리(첫 스테이지가 제일 어려운 벽이 되지 않게 - 봇 30판에서
        /// 방 3부터 궁수를 넣었더니 스테이지 1 사망이 6.2회로 스테이지 7 수준이었다). 스테이지 2는 방마다 몹의
        /// 1/3(최소 1), 3층부터는 1/4(최소 1)이 궁수. 몹 위치는 플레이어에게서 가까운 순으로 놓이므로 목록 끝쪽(먼 쪽)을 궁수로 둔다(뒤에서 쏘게).</summary>
        private const int FirstRangedRoomInStage1 = 6;
        private const int MaxRangedInStage1 = 1;

        /// <summary>방패병/거미 등장 - 새 몹은 한 층 늦게 하나씩 소개한다. 방패병: 2층 방 4부터 1마리, 3층부터 몹의 1/4(최소 1).
        /// 거미: 3층 방 4부터 1마리, 4층부터 몹의 1/5(최소 1). 가까운 순 목록에서 방패병은 맨 앞(앞줄에서 막게),
        /// 그다음 근접, 거미, 궁수가 맨 뒤.</summary>
        private const int NewEnemyFirstRoom = 4;

        private static List<EnemyKind> BuildEnemyKinds(int stage, int roomNumber, int enemyCount)
        {
            // 새 몹은 한 층에 하나씩 소개한다(그 층 방 4부터 1마리 → 다음 층부터 비율): 방패병 2층, 거미 3층, 폭발병 4층,
            // 치유사 5층, 돌진병 6층, 분열체 7층, 도적 8층. 몹 수가 최대 12마리에서 막히는 6층부터는 새 몹 자리를 만들려고
            // 예전 몹 비율을 줄인다(사용자: "몬스터 종류가 너무 적다" - 5~10층이 같은 진형을 체력만 올려 반복하던 문제).
            int Intro(int firstStage) => stage < firstStage ? 0 : stage == firstStage ? (roomNumber >= NewEnemyFirstRoom ? 1 : 0) : -1;
            int Share(int firstStage, int divisor) { var intro = Intro(firstStage); return intro >= 0 ? intro : Mathf.Max(1, enemyCount / divisor); }

            int rangedCount;
            if (stage == 1)
                rangedCount = roomNumber < FirstRangedRoomInStage1 ? 0 : MaxRangedInStage1;
            else if (stage == 2)
                rangedCount = Mathf.Max(1, enemyCount / 3);
            else
                // 방패병/거미가 같이 나오는 3층부터는 1/4 - 1/3이던 때 9·10층 일반 방 사망이 급증했다(봇 100판: 10층 방
                // 판당 10.5회, 죽을 때 남은 몹 평균 궁수 3.6). 방패병 뒤에 숨은 궁수 그림은 유지하고 뒤쪽 화력만 줄인다.
                rangedCount = Mathf.Max(1, enemyCount / (stage >= 6 ? 5 : 4));

            var shieldCount = Share(2, stage >= 6 ? 5 : 4);
            var spiderCount = Share(3, stage >= 7 ? 8 : 5);
            // 폭발병: 근접 바로 뒤 - 앞줄이 붙어 있을 때 파고들어 터지게.
            var bomberCount = Share(4, stage >= 7 ? 8 : 6);
            var healerCount = Mathf.Min(1, Share(5, 99)); // 치유사는 방에 1마리까지(둘이면 서로 고쳐서 안 죽는다)
            var chargerCount = Share(6, 6);
            var splitterCount = Share(7, 8);
            var assassinCount = Share(8, 8);

            // 근접이 최소 1마리는 남게 줄인다(궁수는 안 줄임). 2마리 이상인 종류 중 제일 많은 것부터 하나씩 - 한 종류를 통째로
            // 지우면 그 층 몹 구성이 단조로워진다(16마리 방에서 도적이 0마리가 됐다). 전부 1마리 이하면 새 몹부터
            // (도적 → 분열체 → 돌진병 → 치유사 → 폭발병 → 거미 → 방패병) 뺀다. 같은 수면 돌진병·거미·폭발병·분열체·도적·방패병 순으로 먼저 줄인다.
            var trimmable = new[] { chargerCount, spiderCount, bomberCount, splitterCount, assassinCount, shieldCount, healerCount };
            int Special() { var sum = rangedCount; foreach (var c in trimmable) sum += c; return sum; }
            while (Special() > enemyCount - 1)
            {
                var most = 0;
                for (var k = 1; k < trimmable.Length; k++)
                    if (trimmable[k] > trimmable[most])
                        most = k;
                if (trimmable[most] > 1)
                {
                    trimmable[most]--;
                    continue;
                }
                var dropOrder = new[] { 4, 3, 0, 6, 2, 1, 5 }; // 도적, 분열체, 돌진병, 치유사, 폭발병, 거미, 방패병
                var dropped = false;
                foreach (var k in dropOrder)
                {
                    if (trimmable[k] <= 0)
                        continue;
                    trimmable[k]--;
                    dropped = true;
                    break;
                }
                if (!dropped)
                    break;
            }
            chargerCount = trimmable[0];
            spiderCount = trimmable[1];
            bomberCount = trimmable[2];
            splitterCount = trimmable[3];
            assassinCount = trimmable[4];
            shieldCount = trimmable[5];
            healerCount = trimmable[6];
            var meleeCount = Mathf.Max(0, enemyCount - Special());

            // 몹 위치는 플레이어에게서 가까운 순 - 방패병이 맨 앞, 그다음 근접·분열체·돌진병·도적·폭발병, 거미, 치유사, 궁수가 맨 뒤.
            var kinds = new List<EnemyKind>(enemyCount);
            for (var i = 0; i < shieldCount; i++) kinds.Add(EnemyKind.Shield);
            for (var i = 0; i < meleeCount; i++) kinds.Add(EnemyKind.Melee);
            for (var i = 0; i < splitterCount; i++) kinds.Add(EnemyKind.Splitter);
            for (var i = 0; i < chargerCount; i++) kinds.Add(EnemyKind.Charger);
            for (var i = 0; i < assassinCount; i++) kinds.Add(EnemyKind.Assassin);
            for (var i = 0; i < bomberCount; i++) kinds.Add(EnemyKind.Bomber);
            for (var i = 0; i < spiderCount; i++) kinds.Add(EnemyKind.Spider);
            for (var i = 0; i < healerCount; i++) kinds.Add(EnemyKind.Healer);
            while (kinds.Count < enemyCount) kinds.Add(EnemyKind.Ranged);
            return kinds;
        }

        /// <summary>방 청사진 - 실제 배치(크기 ±1, 벽, 몹 위치, 이벤트)는 방을 깔 때마다 RoomLayoutGenerator가 새로 뽑는다.</summary>
        private static RoomDefinition MakeRoom(string name, int width, int height,
            int enemyCount, float enemyHp, float enemyAtk, bool isBossRoom = false)
        {
            return new RoomDefinition
            {
                RoomName = name,
                Width = width,
                Height = height,
                EnemyCount = enemyCount,
                EnemyMaxHealth = enemyHp,
                EnemyAttackPower = enemyAtk,
                IsBossRoom = isBossRoom,
            };
        }
    }
}
