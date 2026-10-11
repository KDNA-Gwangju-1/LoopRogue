using System.Collections.Generic;

namespace LoopRogue
{
    /// <summary>전설 등급부터 붙는 장비 고유 효과(사용자 결정) - 전설에서 1개, 등급이 오를 때마다 1개씩 더 열리고 윗 등급은
    /// 아랫 등급 효과를 전부 갖는다(영원 = 5개). 검은 공격 수치, 갑옷은 생존기, 반지는 편의 기능. 전부 자동 발동이라
    /// 봇도 그대로 혜택을 받는다. 효과 단계(Tier) = 0(전설 미만) ~ 5(영원).</summary>
    public static class EquipmentEffects
    {
        public const int MaxTier = 5;

        // ---- 검 ----
        public const int ComboEvery = 3;              // 연격: 일반 공격 3번째마다
        public const int ComboEveryUpgraded = 2;      // 영원: 2번째마다
        public const float ComboBonus = 0.5f;
        public const float ComboBonusUpgraded = 1f;
        public const float ExecuteThreshold = 0.25f;  // 처형: 체력 25% 이하
        public const float ExecuteBonus = 0.3f;
        public const float BossHunterBonus = 0.15f;
        public const float FirstStrikeBonus = 0.4f;   // 선제타격: 체력이 가득 찬 적

        // ---- 갑옷 ----
        public const float RoomShieldRate = 0.05f;    // 보호막: 방 입장 시 최대체력 5% (봇 측정 후 10%→5%)
        public const float ThornsRate = 0.2f;         // 가시: 근접 피해의 20% 반사
        public const float EmergencyThreshold = 0.3f; // 응급 처치: 체력 30% 미만
        public const float EmergencyHealRate = 0.2f;
        public const float BossPatternReduction = 0.3f; // 철벽: 보스 예고 공격 피해 -30%

        // ---- 반지 ----
        public const float ItemSaveChance = 0.2f;     // 절약
        public const float LuckGoldBonus = 0.15f;     // 행운
        public const float LuckDropChance = 0.06f;    // 몹 아이템 드롭 3% → 6%

        private static readonly LocCache<Dictionary<ItemSlot, (string Name, string Description)[]>> EffectsCache = new LocCache<Dictionary<ItemSlot, (string Name, string Description)[]>>(() => new Dictionary<ItemSlot, (string, string)[]>
            {
                {
                    ItemSlot.Weapon, new[]
                    {
                        (Loc.T("연격"), Loc.T("일반 공격 3번째마다 피해 +50%")),
                        (Loc.T("처형"), Loc.T("체력 25% 이하인 적에게 피해 +30%")),
                        (Loc.T("보스 사냥꾼"), Loc.T("보스에게 피해 +15%")),
                        (Loc.T("선제타격"), Loc.T("체력이 가득 찬 적에게 피해 +40%")),
                        (Loc.T("연격 강화"), Loc.T("연격이 2번째마다, 피해 +100%")),
                    }
                },
                {
                    ItemSlot.Armor, new[]
                    {
                        (Loc.T("보호막"), Loc.T("방에 들어갈 때 최대체력 5% 보호막")),
                        (Loc.T("가시"), Loc.T("근접 공격 피해의 20%를 되돌려줌")),
                        (Loc.T("불굴"), Loc.T("시도마다 1회, 죽을 피해를 체력 1로 버팀")),
                        (Loc.T("응급 처치"), Loc.T("시도마다 1회, 체력 30% 미만이면 20% 회복")),
                        (Loc.T("철벽"), Loc.T("보스 예고 공격 피해 -30%")),
                    }
                },
                {
                    ItemSlot.Accessory, new[]
                    {
                        (Loc.T("민첩"), Loc.T("대시·회전 베기 쿨타임 -1턴")),
                        (Loc.T("절약"), Loc.T("아이템을 써도 20% 확률로 안 없어짐")),
                        (Loc.T("행운"), Loc.T("골드 +15%, 몹 아이템 드롭 3%→6%")),
                        (Loc.T("민첩 강화"), Loc.T("쿨타임 1턴 더 감소(총 -2턴)")),
                        (Loc.T("유품"), Loc.T("죽어도 퀵슬롯 1번 아이템은 안 잃음")),
                    }
                },
            });

        private static Dictionary<ItemSlot, (string Name, string Description)[]> Effects => EffectsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        public static int TierOf(ItemGrade grade) => grade >= ItemGrade.Legendary ? grade - ItemGrade.Legendary + 1 : 0;

        /// <summary>지금 장착한 장비의 효과 단계.</summary>
        public static int Tier(ItemSlot slot)
        {
            var grade = EquipmentWallet.GetEquipped(slot);
            return grade.HasValue ? TierOf(grade.Value) : 0;
        }

        public static bool Has(ItemSlot slot, int tier) => Tier(slot) >= tier;

        public static string Name(ItemSlot slot, int tier) => Effects[slot][tier - 1].Name;

        public static string Description(ItemSlot slot, int tier) => Effects[slot][tier - 1].Description;

        // ---- 자주 쓰는 계산 ----
        public static int SkillCooldownReduction => Has(ItemSlot.Accessory, 4) ? 2 : Has(ItemSlot.Accessory, 1) ? 1 : 0;

        public static float ExtraGoldBonus => Has(ItemSlot.Accessory, 3) ? LuckGoldBonus : 0f;

        public static float MobDropChance => Has(ItemSlot.Accessory, 3) ? LuckDropChance : ItemInfo.MobDropChance;
    }
}
