using UnityEngine;

namespace LoopRogue
{
    /// <summary>스테이지 번호 → 배율(지수적, "n승만큼 강해지는 우상향" 요청). 적 스탯과 보상(골드/경험치)의
    /// 곡선을 따로 둔다 - 처음엔 하나였는데, 자동 플레이 봇 5판 평균에서 "뒤로 가도 안 어려워진다"(스테이지 10
    /// 사망이 스테이지 1보다 적음)가 나와서, 적만 더 가파르게 키우고 보상은 예전 곡선을 유지하도록 분리했다
    /// (둘을 같이 올리면 보상도 커져서 플레이어도 그만큼 빨리 강해진다).</summary>
    public static class StageScaling
    {
        private const float EnemyGrowthRate = 1.41f;  // 몹/보스 HP·ATK (GameBootstrap) - 1.45에서 보스 계단 보정 도입과 함께 낮춤
        private const float RewardGrowthRate = 1.35f; // 골드(RoomController)/경험치(PlayerActor)

        public static float EnemyMultiplier(int stage) => Mathf.Pow(EnemyGrowthRate, stage - 1);

        public static float RewardMultiplier(int stage) => Mathf.Pow(RewardGrowthRate, stage - 1);
    }
}
