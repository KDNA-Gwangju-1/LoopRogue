using UnityEngine;

namespace LoopRogue
{
    /// <summary>지금 장착 중인 장비(슬롯 3개, 각 슬롯 등급 하나) - GoldWallet과 같은 이유로 PlayerPrefs에
    /// 저장한다. "-1"은 그 슬롯이 아직 비어있다는 뜻(미장착).</summary>
    public static class EquipmentWallet
    {
        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
            EquipmentEnhance.Reload(); // 강화도 같이
        }
        private static readonly int[] EquippedGrade = new int[3]; // ItemSlot 순서와 동일, -1=미장착

        private static string PrefKey(ItemSlot slot) => $"LoopRogue_Equip_{slot}";

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                EquippedGrade[(int)slot] = PlayerPrefs.GetInt(PrefKey(slot), -1);

            _loaded = true;
        }

        /// <summary>그 슬롯에 뭔가 장착돼있으면 등급을, 없으면 null을 반환한다.</summary>
        public static ItemGrade? GetEquipped(ItemSlot slot)
        {
            EnsureLoaded();
            var value = EquippedGrade[(int)slot];
            return value < 0 ? (ItemGrade?)null : (ItemGrade)value;
        }

        /// <summary>새 등급이 지금 장착 중인 것보다 높을 때만 실제로 교체한다("가챠로 더 안 좋은 걸
        /// 뽑으면 그냥 보관도 안 되고 버려진다"는 셈 - 인벤토리가 따로 없는 이 프로토타입에서는
        /// 가장 단순한 규칙). 실제로 교체됐으면 true.</summary>
        public static bool TryEquipIfBetter(ItemSlot slot, ItemGrade grade)
        {
            EnsureLoaded();
            var current = EquippedGrade[(int)slot];
            if (grade <= (ItemGrade)current && current >= 0)
                return false;

            EquippedGrade[(int)slot] = (int)grade;
            PlayerPrefs.SetInt(PrefKey(slot), (int)grade);
            PlayerPrefs.Save();
            return true;
        }

        public static float TotalAttackBonus()
        {
            EnsureLoaded();
            var total = 0f;
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
            {
                var grade = GetEquipped(slot);
                if (grade.HasValue)
                    total += EquipmentData.AttackBonus(slot, grade.Value) * EquipmentEnhance.Multiplier(slot);
            }
            return total;
        }

        public static float TotalHealthBonus()
        {
            EnsureLoaded();
            var total = 0f;
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
            {
                var grade = GetEquipped(slot);
                if (grade.HasValue)
                    total += EquipmentData.HealthBonus(slot, grade.Value) * EquipmentEnhance.Multiplier(slot);
            }
            return total;
        }
    }

    /// <summary>장비 강화(10-14 사용자 "중복으로 뽑으면 버려진다") - 장착되지 않은 뽑기 결과는 그 슬롯의 강화 경험치가 된다(등급이 높을수록 많이).
    /// 강화는 장비가 아니라 슬롯에 붙어서 더 좋은 장비로 바꿔도 이어진다. 단계당 장비 스탯 +StatPerLevel, 최대 MaxLevel.
    /// 필요 누적 경험치 = ExpPerLevelSquared × 단계² - 한 슬롯을 뽑다 보면 영원이 나올 즈음(평균 약 490회) +5, 1,300회 남짓에 +10.</summary>
    public static class EquipmentEnhance
    {
        public const int MaxLevel = 10;
        public const float StatPerLevel = 0.05f;
        private const int ExpPerLevelSquared = 100;
        /// <summary>등급별 강화 경험치(ItemGrade 순서: 일반 고급 희귀 영웅 전설 신화 고대 초월 영원 Unknown).</summary>
        private static readonly int[] ExpByGrade = { 1, 2, 3, 5, 8, 12, 18, 25, 35, 35 };

        private static readonly int[] Exp = new int[3]; // ItemSlot 순서
        private static bool _loaded;

        private static string ExpKey(ItemSlot slot) => $"LoopRogue_EquipEnhanceExp_{slot}";

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                Exp[(int)slot] = PlayerPrefs.GetInt(ExpKey(slot), 0);
            _loaded = true;
        }

        public static int ExpFor(ItemGrade grade) => ExpByGrade[Mathf.Clamp((int)grade, 0, ExpByGrade.Length - 1)];

        /// <summary>그 단계가 되는 데 필요한 누적 경험치.</summary>
        public static int ExpToReach(int level) => ExpPerLevelSquared * level * level;

        public static int GetExp(ItemSlot slot)
        {
            EnsureLoaded();
            return Exp[(int)slot];
        }

        public static int GetLevel(ItemSlot slot)
        {
            var exp = GetExp(slot);
            var level = 0;
            while (level < MaxLevel && exp >= ExpToReach(level + 1))
                level++;
            return level;
        }

        /// <summary>장비 스탯 배율(EquipmentWallet 합계·표시에 곱한다).</summary>
        public static float Multiplier(ItemSlot slot) => 1f + StatPerLevel * GetLevel(slot);

        /// <summary>버려질 뽑기 결과 하나를 경험치로 - 오른 단계 수를 돌려준다. 최대 단계면 경험치는 더 쌓이지 않는다(0).</summary>
        public static int AddExp(ItemSlot slot, ItemGrade grade, out int gained)
        {
            EnsureLoaded();
            gained = 0;
            var before = GetLevel(slot);
            if (before >= MaxLevel)
                return 0;
            gained = ExpFor(grade);
            Exp[(int)slot] = Mathf.Min(Exp[(int)slot] + gained, ExpToReach(MaxLevel));
            PlayerPrefs.SetInt(ExpKey(slot), Exp[(int)slot]);
            PlayerPrefs.Save();
            return GetLevel(slot) - before;
        }

        /// <summary>"+3" 같은 이름 꼬리표(0단계면 빈 문자열).</summary>
        public static string Suffix(ItemSlot slot)
        {
            var level = GetLevel(slot);
            return level > 0 ? $" +{level}" : "";
        }

        /// <summary>다음 단계까지 "120/400"(최대면 "MAX").</summary>
        public static string ProgressText(ItemSlot slot)
        {
            var level = GetLevel(slot);
            if (level >= MaxLevel)
                return "MAX";
            return $"{GetExp(slot) - ExpToReach(level)}/{ExpToReach(level + 1) - ExpToReach(level)}";
        }
    }
}
