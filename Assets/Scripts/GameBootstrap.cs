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
        private const float BossStatMultiplier = 1.92f; // 전 스테이지 보스 HP/ATK에 곱함 - 1.6("보스가 약하다") → ×1.2(봇 5판 결과, 트라이 횟수에 따른 골짜기 보정)

        /// <summary>스테이지별 보스 추가 보정 - 한 번 오르면 절대 안 내려가는 계단(2 ×1.1, 3 ×1.2, 4 이후 ×1.3 유지).
        /// 예전엔 특정 스테이지에만 보정을 넣었더니, 보정이 사라지는 바로 다음 스테이지는 보스 상승폭이 확 줄고
        /// (예: 4 ×1.3 → 5 ×1.0이면 4→5가 1.45/1.3 ≈ 1.12배) 앞에서 많이 죽으며 쌓인 골드까지 겹쳐 봇 테스트에서
        /// 매번 그 다음 스테이지가 "쉬운 골짜기"가 됐다. 계단식으로 바꾸고 대신 적 성장률을 1.45 → 1.41로 낮춰
        /// 스테이지 10 보스는 이전과 거의 같게 맞췄다.</summary>
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
                var enemyCount = Mathf.Min(2 + stagePower + (i - 1) / 2, 8);
                var hp = (8f + (i - 1) * 2f) * stageMultiplier;
                var atk = (2f + (i - 1) * 0.4f) * stageMultiplier;

                rooms.Add(MakeRoom($"Stage {stage}-{i}", size, size,
                    GenerateEnemyPositions(size, size, enemyCount), enemyHp: hp, enemyAtk: atk));
            }

            var bossSize = Mathf.Min(9 + stagePower / 2, 14);
            var extraBoss = ExtraBossMultiplier(stage);
            var bossHp = 150f * stageMultiplier * BossStatMultiplier * extraBoss;
            var bossAtk = 12f * stageMultiplier * BossStatMultiplier * extraBoss;

            rooms.Add(MakeRoom($"Stage {stage} Boss", bossSize, bossSize,
                new List<Vector2Int> { new Vector2Int(bossSize - 2, bossSize - 2) },
                enemyHp: bossHp, enemyAtk: bossAtk, isBossRoom: true));

            return rooms;
        }

        /// <summary>플레이어 시작 칸(0,0)을 피해서, 대각선/반대각선을 번갈아 타고 퍼지는 좌표를
        /// count개 만든다. 격자가 작아서 반올림 좌표가 겹치면 그 칸부터 오른쪽으로 한 칸씩 밀어서
        /// 빈 칸을 찾는다(공간이 넉넉한 이 프로젝트 방 크기에서는 몇 칸만 밀면 항상 찾아진다).</summary>
        private static List<Vector2Int> GenerateEnemyPositions(int width, int height, int count)
        {
            var positions = new List<Vector2Int>();

            for (var i = 0; i < count; i++)
            {
                var t = (i + 1f) / (count + 1f);
                var x = Mathf.RoundToInt(t * (width - 1));
                var yMain = Mathf.RoundToInt(t * (height - 1));
                var y = i % 2 == 0 ? yMain : height - 1 - yMain;
                var pos = new Vector2Int(x, y);

                while (pos == Vector2Int.zero || positions.Contains(pos))
                    pos = new Vector2Int((pos.x + 1) % width, pos.y);

                positions.Add(pos);
            }

            return positions;
        }

        private static RoomDefinition MakeRoom(string name, int width, int height,
            List<Vector2Int> enemyPositions, float enemyHp, float enemyAtk, bool isBossRoom = false)
        {
            return new RoomDefinition
            {
                RoomName = name,
                Width = width,
                Height = height,
                PlayerStart = Vector2Int.zero,
                EnemyPositions = enemyPositions,
                EnemyMaxHealth = enemyHp,
                EnemyAttackPower = enemyAtk,
                IsBossRoom = isBossRoom,
            };
        }
    }
}
