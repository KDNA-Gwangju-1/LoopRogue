using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>보스 처치 보상 유물(사용자 결정) - 각 스테이지 보스를 처음 잡으면 3개 중 1개를 고른다: 그 보스 전용 1개 + 아직 없는
    /// 일반 유물 2개. 고르지 않은 보스 전용 유물은 다시 안 나온다. 영구 패시브(PlayerPrefs) - 죽어도 유지, 저장 초기화로만 사라진다.</summary>
    public enum RelicType
    {
        // 보스 전용(스테이지 1~10 순서)
        ChargeHorn,     // 1 돌진
        EarthHammer,    // 2 강타
        CrossCrest,     // 3 십자
        PhantomBanner,  // 4 소환
        PulseCore,      // 5 파동
        SniperEye,      // 6 저격
        PiercingSeal,   // 7 X자
        TyrantPlate,    // 8 돌진+강타
        Executioner,    // 9 십자+저격
        CrownOfEnd,     // 10 전부
        // 일반
        BloodPact,
        ChainBlast,
        SecondWind,
        TreasureHunter,
        SpinningBlade,
        HunterEye,
        Greed,
        Gale,
        RegenMoss,
        IronSkin,
        Berserker,
        FirstStrike,
    }

    public static class Relics
    {
        public const int BossRelicCount = 10;
        public const int OfferCount = 3;

        // ---- 효과 수치 ---- (범위 공격 유물은 봇 측정에서 8~10층 일반 방 사망을 목표의 15~25%로 떨어뜨려서 한 번 약화: 망치 30→20%, 관통·갑주 50→30%, 집행 25→15%, 연쇄 30→20%)
        public const int ChargeHornDashRange = 4;
        public const float ChargeHornDashDamage = 1.5f;
        public const float EarthHammerSplashRate = 0.2f;
        public const float PiercingRate = 0.3f;
        public const float TyrantLandingRate = 0.3f;
        public const float ExecutionerThreshold = 0.15f;
        public const float SniperCritDamageBonus = 0.5f;
        public const float CrownAttack = 10f;
        public const float CrownHealth = 50f;
        public const float BloodPactHealthFactor = 0.8f;
        public const float BloodPactLifeStealCap = 0.7f;
        public const float ChainBlastRate = 0.2f;
        public const float SecondWindHealRate = 0.3f;
        public const float TreasureHunterDropBonus = 0.03f;
        public const float SpinningBladeDamageRate = 1.2f;
        public const float HunterEyeVisionBonus = 2f;
        public const float GreedGoldBonus = 0.4f;
        public const float GreedDeathPenaltyRate = 0.6f;
        public const int GaleDashCooldownReduction = 2;
        public const float RegenMossHealRate = 0.15f;
        public const float IronSkinDamageFactor = 0.9f;
        public const float BerserkerThreshold = 0.5f;
        public const float BerserkerBonus = 0.3f;

        private static readonly Dictionary<RelicType, (string Name, string Description)> Info = new Dictionary<RelicType, (string, string)>
        {
            { RelicType.ChargeHorn, ("돌진의 뿔", "대시 거리 3→4칸, 대시 공격 피해 +50%") },
            { RelicType.EarthHammer, ("대지의 망치", "일반 공격 시 대상 상하좌우 몹에게도 20% 피해") },
            { RelicType.CrossCrest, ("십자 문장", "회전 베기가 상하좌우 2칸까지 닿음") },
            { RelicType.PhantomBanner, ("망령의 깃발", "방에 들어갈 때 옆에 허수아비가 생김") },
            { RelicType.PulseCore, ("파동의 핵", "회전 베기에 맞은 몹 1턴 기절") },
            { RelicType.SniperEye, ("저격수의 눈", "치명타 피해 +50%p") },
            { RelicType.PiercingSeal, ("관통의 인장", "일반 공격이 대상 뒤 1칸 몹에게도 30% 피해") },
            { RelicType.TyrantPlate, ("폭군의 갑주", "대시로 내려선 자리 주변 8칸 몹에게 30% 피해") },
            { RelicType.Executioner, ("사형 집행자", "체력 15% 이하인 일반 몹은 한 대에 처치") },
            { RelicType.CrownOfEnd, ("끝의 왕관", "공격력 +10, 최대체력 +50") },
            { RelicType.BloodPact, ("피의 계약", "최대체력(성장분) -20%, 흡혈 상한 50%→70%") },
            { RelicType.ChainBlast, ("연쇄 폭발", "일반 몹 처치 시 주변 8칸 몹에게 공격력 20% 피해") },
            { RelicType.SecondWind, ("두 번째 숨", "시도마다 1회, 죽으면 체력 30%로 부활") },
            { RelicType.TreasureHunter, ("보물 사냥꾼", "보물상자에서 아이템 2개, 몹 아이템 드롭 +3%p") },
            { RelicType.SpinningBlade, ("회전 칼날", "회전 베기 피해 80%→120%") },
            { RelicType.HunterEye, ("사냥꾼의 눈", "시야 +2칸, 출구가 처음부터 보임") },
            { RelicType.Greed, ("탐욕", "골드 +40%, 데스 패널티 40%→60%") },
            { RelicType.Gale, ("질풍", "대시 쿨타임 -2턴(최소 1턴)") },
            { RelicType.RegenMoss, ("재생의 이끼", "방 클리어 시 체력 15% 추가 회복") },
            { RelicType.IronSkin, ("철의 피부", "받는 피해 -10%(카드 피해 감소와 별도)") },
            { RelicType.Berserker, ("광전사", "체력 50% 이하일 때 주는 피해 +30%") },
            { RelicType.FirstStrike, ("첫 일격", "방마다 첫 공격은 치명타 확정") },
        };

        private const string OwnedKey = "LoopRogue_Relics_Owned";
        private const string OfferedKey = "LoopRogue_Relics_OfferedStages";
        private static readonly HashSet<RelicType> Owned = new HashSet<RelicType>();
        private static readonly HashSet<int> OfferedStages = new HashSet<int>();
        private static bool _loaded;
        private static readonly System.Random Rng = new System.Random();

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;
            Owned.Clear();
            OfferedStages.Clear();
            foreach (var part in PlayerPrefs.GetString(OwnedKey, "").Split(','))
                if (int.TryParse(part, out var v) && System.Enum.IsDefined(typeof(RelicType), v))
                    Owned.Add((RelicType)v);
            foreach (var part in PlayerPrefs.GetString(OfferedKey, "").Split(','))
                if (int.TryParse(part, out var s))
                    OfferedStages.Add(s);
            _loaded = true;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(OwnedKey, string.Join(",", Owned.Select(r => (int)r)));
            PlayerPrefs.SetString(OfferedKey, string.Join(",", OfferedStages));
            PlayerPrefs.Save();
        }

        public static bool Has(RelicType type)
        {
            EnsureLoaded();
            return Owned.Contains(type);
        }

        public static IEnumerable<RelicType> OwnedRelics()
        {
            EnsureLoaded();
            return Owned.OrderBy(r => (int)r);
        }

        public static int OwnedCount
        {
            get
            {
                EnsureLoaded();
                return Owned.Count;
            }
        }

        public static string Name(RelicType type) => Info[type].Name;
        public static string Description(RelicType type) => Info[type].Description;
        public static bool IsBossRelic(RelicType type) => (int)type < BossRelicCount;
        public static RelicType BossRelic(int stage) => (RelicType)(Mathf.Clamp(stage, 1, BossRelicCount) - 1);

        /// <summary>그 스테이지 보스를 처음 잡았을 때만 후보 3개(보스 전용 1 + 일반 2). 이미 보상을 받은 스테이지면 null.
        /// 후보를 만드는 순간 "받은 스테이지"로 기록한다(고르는 도중에 꺼도 다시 안 나옴).</summary>
        public static List<RelicType> MakeOffer(int stage)
        {
            EnsureLoaded();
            if (OfferedStages.Contains(stage))
                return null;
            OfferedStages.Add(stage);
            Save();

            var offer = new List<RelicType>();
            var boss = BossRelic(stage);
            if (!Owned.Contains(boss))
                offer.Add(boss);
            var general = Enumerable.Range(BossRelicCount, Info.Count - BossRelicCount)
                .Select(i => (RelicType)i).Where(r => !Owned.Contains(r)).OrderBy(_ => Rng.Next()).ToList();
            foreach (var r in general)
            {
                if (offer.Count >= OfferCount)
                    break;
                offer.Add(r);
            }
            return offer.Count > 0 ? offer : null;
        }

        /// <summary>유물을 얻는다 - 한 번만 적용되는 효과(피의 계약 최대체력, 끝의 왕관 스탯)는 지금 플레이어에 바로 반영한다.</summary>
        public static void Acquire(RelicType type, PlayerActor player)
        {
            EnsureLoaded();
            if (!Owned.Add(type))
                return;
            Save();

            var stats = player.Stats;
            switch (type)
            {
                case RelicType.BloodPact:
                    stats.MultiplyMaxHealth(BloodPactHealthFactor);
                    break;
                case RelicType.CrownOfEnd:
                    // 고정 몫(장비·영약과 같은 칸)으로 - RunProgress.CurrentPermanent*도 이 값을 더한다.
                    stats.FixedAttack += CrownAttack;
                    stats.FixedMaxHealth += CrownHealth;
                    stats.Heal(CrownHealth);
                    break;
            }
        }

        // ---- 자주 쓰는 계산 ----
        public static float PermanentAttack => Has(RelicType.CrownOfEnd) ? CrownAttack : 0f;
        public static float PermanentHealth => Has(RelicType.CrownOfEnd) ? CrownHealth : 0f;
        public static float ExtraGoldBonus => Has(RelicType.Greed) ? GreedGoldBonus : 0f;
        public static float DeathPenaltyRate => Has(RelicType.Greed) ? GreedDeathPenaltyRate : LoopManager.DeathGoldPenaltyRate;
        public static float DropBonus => Has(RelicType.TreasureHunter) ? TreasureHunterDropBonus : 0f;
        public static float LifeStealCap => Has(RelicType.BloodPact) ? BloodPactLifeStealCap : CharacterStats.MaxLifeSteal;
        public static float VisionRadius => FogOfWar.VisionRadius + (Has(RelicType.HunterEye) ? HunterEyeVisionBonus : 0f);
    }
}
