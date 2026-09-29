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
                    total += EquipmentData.AttackBonus(slot, grade.Value);
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
                    total += EquipmentData.HealthBonus(slot, grade.Value);
            }
            return total;
        }
    }
}
