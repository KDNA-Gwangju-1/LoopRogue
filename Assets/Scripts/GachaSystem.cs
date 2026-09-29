using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    public struct GachaResult
    {
        public ItemSlot Slot;
        public ItemGrade Grade;
        public bool Equipped; // 기존 장비보다 안 좋아서 버려졌으면 false.
        public bool ShopLeveledUp; // 이번 뽑기로 그 슬롯 상점 레벨이 올랐으면 true.
    }

    /// <summary>골드를 써서 장비를 뽑는다 - 슬롯을 직접 골라서 뽑고(검 뽑기면 검만 나옴), 등급은 그
    /// 슬롯 "상점 레벨"의 확률표로 뽑는다. 상점 레벨은 슬롯마다 따로 있고, 그 슬롯을 뽑은 누적 횟수로
    /// 오른다(PlayerPrefs 영구 저장 - GoldWallet과 같은 패턴).</summary>
    public static class GachaSystem
    {
        /// <summary>상점 레벨별 1회 뽑기 가격(인덱스 0 = Lv1) - 좋은 확률표일수록 비싸진다.</summary>
        private static readonly int[] PullCostByLevel = { 10, 12, 14, 17, 19, 21, 23, 26, 28, 30 };

        /// <summary>10연차 = 10회를 9회 가격에(1회분 할인).</summary>
        public const int MultiPullCount = 10;
        public const int MultiPullPaidCount = 9;

        /// <summary>상점 레벨 N이 되기 위한 누적 뽑기 횟수(인덱스 0 = Lv1). 마지막 값이 최대 레벨.</summary>
        private static readonly int[] PullsForLevel = { 0, 10, 30, 60, 100, 150, 220, 300, 400, 520 };

        public static int MaxShopLevel => PullsForLevel.Length;

        /// <summary>상점 레벨별 등급 확률(%) - 열 순서는 ItemGrade 순서(일반 고급 희귀 영웅 전설 신화
        /// 고대 초월 영원), 행마다 합계 100. 레벨이 오를수록 낮은 등급 비중이 줄고 윗 등급이 하나씩 열린다.
        /// Lv8~10의 고대/초월/영원은 봇 테스트에서 후반 운 편차가 너무 커서 낮추고 그만큼 영웅/전설로 옮겼다.</summary>
        private static readonly float[][] GradeWeightsByLevel =
        {
            new[] { 55f, 30f, 12f, 3f, 0f, 0f, 0f, 0f, 0f },         // Lv1
            new[] { 45f, 32f, 16f, 6f, 1f, 0f, 0f, 0f, 0f },         // Lv2
            new[] { 35f, 32f, 20f, 10f, 3f, 0f, 0f, 0f, 0f },        // Lv3
            new[] { 25f, 30f, 24f, 14f, 6f, 1f, 0f, 0f, 0f },        // Lv4
            new[] { 18f, 26f, 26f, 18f, 9f, 3f, 0f, 0f, 0f },        // Lv5
            new[] { 12f, 22f, 26f, 21f, 12f, 6f, 1f, 0f, 0f },       // Lv6
            new[] { 8f, 17f, 24f, 23f, 15f, 9f, 3.5f, 0.5f, 0f },    // Lv7
            new[] { 0f, 17f, 20f, 25.8f, 19f, 12f, 5f, 1f, 0.2f },  // Lv8
            new[] { 0f, 11f, 16f, 24.4f, 24f, 15f, 7f, 2f, 0.6f },  // Lv9
            new[] { 0f, 7f, 12f, 23.8f, 26f, 18f, 9f, 3f, 1.2f },   // Lv10
        };

        private static readonly System.Random Rng = new System.Random();
        private static readonly int[] PullCounts = new int[3]; // ItemSlot 순서와 동일
        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static string PrefKey(ItemSlot slot) => $"LoopRogue_GachaPulls_{slot}";

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
                PullCounts[(int)slot] = PlayerPrefs.GetInt(PrefKey(slot), 0);

            _loaded = true;
        }

        public static int GetPullCount(ItemSlot slot)
        {
            EnsureLoaded();
            return PullCounts[(int)slot];
        }

        public static int GetShopLevel(ItemSlot slot)
        {
            var pulls = GetPullCount(slot);
            var level = 1;
            for (var i = 1; i < PullsForLevel.Length; i++)
            {
                if (pulls >= PullsForLevel[i])
                    level = i + 1;
            }
            return level;
        }

        /// <summary>다음 상점 레벨까지 필요한 누적 뽑기 횟수. 최대 레벨이면 null.</summary>
        public static int? GetPullsForNextLevel(ItemSlot slot)
        {
            var level = GetShopLevel(slot);
            return level >= MaxShopLevel ? (int?)null : PullsForLevel[level];
        }

        /// <summary>그 슬롯 현재 상점 레벨의 등급별 확률(%) - UI 확률표 표시용.</summary>
        public static float[] GetGradeChances(ItemSlot slot) => GradeWeightsByLevel[GetShopLevel(slot) - 1];

        /// <summary>스테이지가 하나 오를 때마다 뽑기 가격 +25%(스테이지 10 = 3.25배) - 골드 수입은 스테이지마다 1.35배씩
        /// 늘어나는데 가격만 고정이라 상점 레벨이 일찍 끝나고 중반 이후엔 골드 쓸 곳이 없던 문제 보정.</summary>
        private const float PricePerStageRate = 0.25f;

        public static float StagePriceMultiplier => 1f + PricePerStageRate * (StageProgress.CurrentStage - 1);

        public static int GetPullCost(ItemSlot slot) =>
            Mathf.RoundToInt(PullCostByLevel[GetShopLevel(slot) - 1] * StagePriceMultiplier);

        /// <summary>10연차 가격 - 누르는 시점의 상점 레벨 가격 기준(도중에 레벨이 올라도 추가 요금 없음).</summary>
        public static int GetMultiPullCost(ItemSlot slot) => GetPullCost(slot) * MultiPullPaidCount;

        /// <summary>골드가 모자라면 null. 충분하면 골드를 소모하고 결과를 반환한다.</summary>
        public static GachaResult? Pull(ItemSlot slot)
        {
            if (!GoldWallet.TrySpend(GetPullCost(slot)))
                return null;

            return PullOnce(slot);
        }

        /// <summary>10연차 - 골드가 모자라면 null. 한 번씩 순서대로 굴려서 도중에 상점 레벨이 오르면 남은
        /// 뽑기는 오른 레벨의 확률표로 굴린다.</summary>
        public static List<GachaResult> PullMulti(ItemSlot slot)
        {
            if (!GoldWallet.TrySpend(GetMultiPullCost(slot)))
                return null;

            var results = new List<GachaResult>(MultiPullCount);
            for (var i = 0; i < MultiPullCount; i++)
                results.Add(PullOnce(slot));
            return results;
        }

        private static GachaResult PullOnce(ItemSlot slot)
        {
            var levelBefore = GetShopLevel(slot);
            var grade = RollGrade(GradeWeightsByLevel[levelBefore - 1]);

            PullCounts[(int)slot]++;
            PlayerPrefs.SetInt(PrefKey(slot), PullCounts[(int)slot]);
            PlayerPrefs.Save();

            var equipped = EquipmentWallet.TryEquipIfBetter(slot, grade);

            return new GachaResult
            {
                Slot = slot,
                Grade = grade,
                Equipped = equipped,
                ShopLeveledUp = GetShopLevel(slot) > levelBefore,
            };
        }

        private static ItemGrade RollGrade(float[] weights)
        {
            var totalWeight = 0f;
            foreach (var w in weights)
                totalWeight += w;

            var roll = (float)Rng.NextDouble() * totalWeight;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f)
                    continue;

                roll -= weights[i];
                if (roll <= 0f)
                    return (ItemGrade)i;
            }

            return ItemGrade.Common; // 부동소수점 오차로 못 걸러졌을 때 안전망.
        }
    }
}
