using UnityEngine;

namespace LoopRogue
{
    public enum PotionType
    {
        Attack,
        Health,
        Critical,
    }

    /// <summary>골드로 직접 사는 영구 스탯 영약 - 가챠(EquipmentWallet)와 달리 랜덤이 아니라 확정
    /// 구매다. "방어력은 밸런스 부담이 크니 만들지 말고, 대신 골드로 스탯을 직접 사는 영약을 팔자"
    /// 요청으로 추가. 살 때마다 다음 구매 가격이 올라가서 무한정 싸게 스택하는 걸 막는다.
    ///
    /// 치명타 영약은 "처음부터 팔지 말고 일정 스테이지 이상부터"라는 요청으로 CriticalUnlockStage
    /// 미만이면 구매 자체를 막는다(영약이 올리는 건 확률 - 100%를 넘긴
    /// 분은 CharacterStats.CriticalDamageMultiplier에서 치명타 피해로 전환된다).</summary>
    public static class StatPotionWallet
    {
        private const float AttackPerPotion = 1f;
        private const float HealthPerPotion = 5f;
        private const float CriticalChancePerPotion = 0.02f; // +2%p

        private const int BaseCost = 20;
        private const int CostIncreasePerPurchase = 5; // 구매할 때마다 5골드씩 오름(2 → 5: 봇 테스트에서 판당 영약 370개 이상이라 올림)

        public const int CriticalUnlockStage = 3;

        private const string AttackCountKey = "LoopRogue_Potion_AttackCount";
        private const string HealthCountKey = "LoopRogue_Potion_HealthCount";
        private const string CriticalCountKey = "LoopRogue_Potion_CriticalCount";

        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
        private static int _attackCount;
        private static int _healthCount;
        private static int _criticalCount;

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _attackCount = PlayerPrefs.GetInt(AttackCountKey, 0);
            _healthCount = PlayerPrefs.GetInt(HealthCountKey, 0);
            _criticalCount = PlayerPrefs.GetInt(CriticalCountKey, 0);
            _loaded = true;
        }

        /// <summary>치명타 영약은 StageProgress.CurrentStage가 CriticalUnlockStage 이상이어야
        /// 구매 가능 - 스테이지는 한 번 오르면 안 내려가므로(StageProgress.AdvanceStage 참고) "지금까지
        /// 도달한 최고 스테이지"로 그대로 써도 안전하다. 나머지 영약은 처음부터 항상 열려있다.</summary>
        public static bool IsUnlocked(PotionType type) =>
            type != PotionType.Critical || StageProgress.CurrentStage >= CriticalUnlockStage;

        public static int GetCount(PotionType type)
        {
            EnsureLoaded();
            return type switch
            {
                PotionType.Attack => _attackCount,
                PotionType.Health => _healthCount,
                _ => _criticalCount,
            };
        }

        public static int GetNextCost(PotionType type) => BaseCost + GetCount(type) * CostIncreasePerPurchase;

        public static float TotalAttackBonus()
        {
            EnsureLoaded();
            return _attackCount * AttackPerPotion;
        }

        public static float TotalHealthBonus()
        {
            EnsureLoaded();
            return _healthCount * HealthPerPotion;
        }

        public static float TotalCriticalChanceBonus()
        {
            EnsureLoaded();
            return _criticalCount * CriticalChancePerPotion;
        }

        /// <summary>잠겨있거나 골드가 모자라면 false. 성공하면 골드를 소모하고 카운트를 저장한다
        /// (다음 구매 가격도 이 카운트로 다시 계산되니 자동으로 오른다).</summary>
        public static bool TryBuy(PotionType type)
        {
            EnsureLoaded();
            if (!IsUnlocked(type))
                return false;

            var cost = GetNextCost(type);
            if (!GoldWallet.TrySpend(cost))
                return false;

            switch (type)
            {
                case PotionType.Attack:
                    _attackCount++;
                    PlayerPrefs.SetInt(AttackCountKey, _attackCount);
                    break;
                case PotionType.Health:
                    _healthCount++;
                    PlayerPrefs.SetInt(HealthCountKey, _healthCount);
                    break;
                default:
                    _criticalCount++;
                    PlayerPrefs.SetInt(CriticalCountKey, _criticalCount);
                    break;
            }

            PlayerPrefs.Save();
            return true;
        }
    }
}
