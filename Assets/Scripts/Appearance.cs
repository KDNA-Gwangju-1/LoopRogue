namespace LoopRogue
{
    /// <summary>장비 외형 고르기(11-03 사용자 - 로비 [9] 재봉사). 방어구 외형·무기 외형·이펙트 등급을 따로 고른다(-1 = 장착한 장비를 따라감).
    /// 고를 수 있는 건 그 부위에서 한 번이라도 얻어 본 등급(뽑기·장착 기록 MarkOwned) + 지금 장착한 등급. 이펙트는 무기 쪽 기록을 쓴다.
    /// 고른 등급을 잃는 일은 없지만, 기록에 없는 값이 저장돼 있으면(테스트·저장 꼬임) 장비를 따라간다.</summary>
    public static class Appearance
    {
        public enum Part { Armor, Weapon, Effect }

        private const string OwnedKeyPrefix = "LoopRogue_LookOwned_"; // + ItemSlot - 얻어 본 등급 비트(1 << 등급)
        private static string ChoiceKey(Part part) => $"LoopRogue_Look{part}";

        public static ItemSlot SlotOf(Part part) => part == Part.Armor ? ItemSlot.Armor : ItemSlot.Weapon;

        /// <summary>그 부위에서 이 등급을 얻었다(뽑기 결과·장착).</summary>
        public static void MarkOwned(ItemSlot slot, ItemGrade grade)
        {
            var key = OwnedKeyPrefix + slot;
            var mask = Prefs.GetInt(key, 0);
            var bit = 1 << (int)grade;
            if ((mask & bit) != 0)
                return;
            Prefs.SetInt(key, mask | bit);
            Prefs.Save();
        }

        public static bool IsOwned(ItemSlot slot, ItemGrade grade) =>
            (Prefs.GetInt(OwnedKeyPrefix + slot, 0) & (1 << (int)grade)) != 0 || EquipmentWallet.GetEquipped(slot) == grade;

        /// <summary>저장된 고른 값(-1 = 장비 따라감).</summary>
        public static int Choice(Part part) => Prefs.GetInt(ChoiceKey(part), -1);

        public static void SetChoice(Part part, int grade)
        {
            Prefs.SetInt(ChoiceKey(part), grade);
            Prefs.Save();
            HeroLook.Bump();
        }

        /// <summary>장착한 등급(미장착 = 일반).</summary>
        public static ItemGrade Equipped(Part part) => EquipmentWallet.GetEquipped(SlotOf(part)) ?? ItemGrade.Common;

        /// <summary>실제로 보일 등급 - 고른 값이 있고 얻어 본 등급이면 그것, 아니면 장비 따라감.</summary>
        public static ItemGrade Resolved(Part part)
        {
            var choice = Choice(part);
            if (choice >= 0 && choice <= (int)ItemGrade.Unknown && IsOwned(SlotOf(part), (ItemGrade)choice))
                return (ItemGrade)choice;
            return Equipped(part);
        }
    }
}
