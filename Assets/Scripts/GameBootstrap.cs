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
        private const int RoomsPerStage = 10;
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
            // 층별 실제 평균 사망 2:3 3:4 4:5 5:15 6:5 7:8 8:10 9:12 10:20(배율 찾기 1회차 + 일반 모드 실측 곡선으로 직접 보정 2~9회차, 4회차부터 장비/영약 고정 이후, 8회차부터 스킬·몹 1.5배·흡혈 상한 이후). 보스 세기 10% 차이가 사망 수를 약 40% 바꿀 만큼
            // 민감하니 손으로 크게 건드리지 말 것. 패턴이 어려운 층(6 저격, 7 X자, 8 돌진+강타)은 값이 낮다.
            1 => 1.32f,
            2 => 1.24f,
            3 => 0.99f,
            4 => 1.33f,
            5 => 1.27f,
            6 => 0.8f,
            7 => 0.68f,
            8 => 0.87f,
            9 => 0.6f,
            10 => 0.67f,
            _ => 1f,
        };

        private static float ExtraBossMultiplier(int stage) => stage switch
        {
            <= 1 => 1f,
            2 => 1.1f,
            3 => 1.2f,
            _ => 1.3f,
        };

        private void Awake()
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

            var loopManager = new LoopManager();
            loopManager.Initialize(room, player, hud, BuildStageRooms(stage), stage);
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
        private const int MaxEnemiesPerRoom = 12;

        private static List<RoomDefinition> BuildStageRooms(int stage)
        {
            var stagePower = stage - 1; // 0-based - 방 크기/몹 수 가산에 그대로 씀.
            var stageMultiplier = StageScaling.EnemyMultiplier(stage);
            var rooms = new List<RoomDefinition>();

            for (var i = 1; i <= RoomsPerStage; i++)
            {
                // 크기/몹 수는 배율을 안 곱한다 - 격자가 지수적으로 커지면 방이 순식간에 감당 안
                // 될 만큼 거대해진다. 이 둘은 예전처럼 스테이지당 고정폭으로만 커진다.
                var size = Mathf.Min(6 + stagePower + (i - 1) / 2, 14);
                var enemyCount = Mathf.Min(Mathf.RoundToInt((2 + stagePower + (i - 1) / 2) * EnemyCountMultiplier), MaxEnemiesPerRoom);
                var hp = (8f + (i - 1) * 2f) * stageMultiplier;
                var atk = (2f + (i - 1) * 0.4f) * stageMultiplier;

                var room = MakeRoom($"Stage {stage}-{i}", size, size, enemyCount, enemyHp: hp, enemyAtk: atk);
                room.EnemyKinds = BuildEnemyKinds(stage, i, enemyCount);
                rooms.Add(room);
            }

            var bossSize = Mathf.Min(9 + stagePower / 2, 14);
            var extraBoss = ExtraBossMultiplier(stage) * BossPatternBalance(stage) * BossScaleForBotTest;
            var bossHp = 150f * stageMultiplier * BossStatMultiplier * extraBoss;
            var bossAtk = 12f * stageMultiplier * BossStatMultiplier * extraBoss;

            var bossRoom = MakeRoom($"Stage {stage} Boss", bossSize, bossSize, 1,
                enemyHp: bossHp, enemyAtk: bossAtk, isBossRoom: true);
            bossRoom.BossPatterns = BossBrain.StagePatterns(stage);
            rooms.Add(bossRoom);

            return rooms;
        }

        /// <summary>궁수 배치 - 스테이지 1은 방 6부터 딱 1마리(첫 스테이지가 제일 어려운 벽이 되지 않게 - 봇 30판에서
        /// 방 3부터 궁수를 넣었더니 스테이지 1 사망이 6.2회로 스테이지 7 수준이었다). 스테이지 2부터는 방마다 몹의
        /// 1/3(최소 1)이 궁수. 몹 위치는 플레이어에게서 가까운 순으로 놓이므로 목록 끝쪽(먼 쪽)을 궁수로 둔다(뒤에서 쏘게).</summary>
        private const int FirstRangedRoomInStage1 = 6;
        private const int MaxRangedInStage1 = 1;

        private static List<EnemyKind> BuildEnemyKinds(int stage, int roomNumber, int enemyCount)
        {
            var kinds = new List<EnemyKind>(enemyCount);
            int rangedCount;
            if (stage == 1)
                rangedCount = roomNumber < FirstRangedRoomInStage1 ? 0 : MaxRangedInStage1;
            else
                rangedCount = Mathf.Max(1, enemyCount / 3);
            for (var i = 0; i < enemyCount; i++)
                kinds.Add(i >= enemyCount - rangedCount ? EnemyKind.Ranged : EnemyKind.Melee);
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
