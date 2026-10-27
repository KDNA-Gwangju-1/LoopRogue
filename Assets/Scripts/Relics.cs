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
        // 히든(보스 보상 후보로 안 나온다 - 히든 몹·NPC가 준다)
        KnightMemory,   // 기억하는 기사 첫 처치
        // 일반(10-27 추가) - 저장이 번호로 돼 있어서 KnightMemory 뒤에 붙인다(앞에 끼우면 기존 저장의 유물이 바뀐다)
        ShadowStep,
        Whirlwind,
        StoppedWatch,
        RecurrenceMark,
        PuzzleGlove,
        MerchantSeal,
        ContractBrand,
        BloodHunter,
        EliteHunter,
        EchoBlade,
        AlchemistPouch,
        PowderMaster,
        SnareHunter,
    }

    public static class Relics
    {
        public const int BossRelicCount = 10;
        public const int OfferCount = 3;

        // ---- 효과 수치 ---- (범위 공격 유물은 봇 측정에서 8~10층 일반 방 사망을 목표의 15~25%로 떨어뜨려서 한 번 약화: 망치 30→20%, 관통·갑주 50→30%, 집행 25→15%, 연쇄 30→20%)
        public const int ChargeHornDashRange = 4;
        public const float ChargeHornDashDamage = 1.5f;
        public const float EarthHammerSplashRate = 0.2f;
        public const int PulseCoreStunTurns = 2; // 파동의 핵 - 회전 베기 기절(10-19 사용자 1 → 2)
        /// <summary>망령의 깃발(10-27 테스터 제안, 사용자 7턴) - 방마다 1번, 체력이 이 비율 아래로 떨어지면 몹 옆에 허수아비가 생겨 몹들이 그것만 노린다.</summary>
        public const float PhantomBannerHealthRatio = 0.3f;
        public const int PhantomBannerTurns = 7;
        public const float PiercingRate = 0.3f;
        public const float TyrantLandingRate = 0.3f;
        /// <summary>사형 집행자 - 일반 몹(보스 아님)에게 주는 피해 배율(10-24 사용자, 예전엔 체력 15% 이하 일반 몹 한 방 처치).</summary>
        public const float ExecutionerDamage = 1.3f;
        public const float SniperCritDamageBonus = 0.5f;
        public const float CrownAttack = 10f;
        public const float CrownHealth = 50f;
        public const float BloodPactHealthFactor = 0.8f;
        public const float BloodPactLifeStealCap = 0.7f;
        public const float ChainBlastRate = 0.2f;
        public const float SecondWindHealRate = 0.3f;
        public const float TreasureHunterDropBonus = 0.03f;
        /// <summary>오래된 보물지도 - 보물상자(숨겨진 보물상자 포함)에서 나오는 골드만 배수(10-27 사용자 +100%, 골드 획득량 전체가 아님).</summary>
        public const int TreasureHunterChestGold = 2;
        /// <summary>첫 일격 - 방마다 첫 타격 피해 배율(10-27 사용자, 예전엔 치명타 확정 - 후반엔 치명타 확률이 100%를 넘어 의미가 없었다).</summary>
        public const float FirstStrikeDamage = 1.5f;
        public const float SpinningBladeDamageRate = 1.2f;
        public const float HunterEyeVisionBonus = 2f;
        public const float GreedGoldBonus = 0.4f;
        public const float GreedDeathPenaltyRate = 0.6f;
        public const int GaleDashCooldownReduction = 2;
        public const float RegenMossHealRate = 0.15f;
        public const float IronSkinDamageFactor = 0.9f;
        public const float IronSkinHealRate = 0.05f; // 철의 피부 - 체력으로 받은 피해의 5%를 곧바로 회복(10-27 사용자)
        public const float BerserkerThreshold = 0.5f;
        public const float BerserkerBonus = 0.3f;
        public const float KnightMemoryReadChance = 0.12f;
        // ---- 10-27 추가 유물 ----
        public const float WhirlwindWallRate = 0.3f;        // 소용돌이 - 밀려나다 막히면 공격력 ×
        public const int WhirlwindPushCells = 2;            // 소용돌이 - 밀어내는 칸 수(10-27 사용자 1 → 2)
        public const float StoppedWatchPerDeath = 0.005f;   // 멈춘 회중시계 - 누적 사망 1회당 주는 피해
        public const float StoppedWatchMax = 0.25f;
        public const float RecurrencePerDeath = 0.02f;      // 되풀이의 각인 - 이 층에서 죽은 1회당 받는 피해 감소(10-27 사용자 3 → 2%)
        public const float RecurrenceMax = 0.1f;
        public const int PuzzleGloveRewardMultiplier = 2;   // 퍼즐 장인의 장갑 - 퍼즐 성공 골드·영약
        public const float MerchantSealDiscount = 0.8f;     // 상인의 인장 - 던전 상인만(로비 상점 제외)
        public const float BloodHunterHealRate = 0.02f;     // 피의 사냥꾼 - 처치마다 최대 체력의
        public const float EliteHunterDamage = 1.3f;        // 정예 사냥꾼 - 정예·미믹에게 주는 피해
        public const int EliteHunterGoldMultiplier = 2;     // 정예 사냥꾼 - 미믹·도전의 깃발 보상 골드
        public const int EchoBladeEvery = 3;                // 메아리 칼날 - 일반 공격 n번째마다
        public const float EchoBladeRate = 0.25f;           // 메아리 칼날 - 추가 베기 피해(흡혈 없음, 10-27 사용자 50 → 25%)
        public const int AlchemistPouchCapacity = 2;        // 연금술사의 주머니 - 아이템 종류마다
        public const int PowderMasterRadius = 2;            // 화약 장인 - 폭탄 5x5
        public const float SnareHunterDamage = 1.5f;        // 사냥꾼의 올가미 - 덫에 걸려 기절한 몹이 받는 피해

        private static readonly LocCache<Dictionary<RelicType, (string Name, string Description)>> InfoCache = new LocCache<Dictionary<RelicType, (string Name, string Description)>>(() => new Dictionary<RelicType, (string, string)>
        {
            { RelicType.ChargeHorn, (Loc.T("돌진의 뿔"), Loc.T("대시 거리 3→4칸, 대시 공격 피해 +50%")) },
            { RelicType.EarthHammer, (Loc.T("대지의 망치"), Loc.T("일반 공격 시 대상 상하좌우 몹에게도 20% 피해")) },
            { RelicType.CrossCrest, (Loc.T("십자 문장"), Loc.T("회전 베기가 상하좌우 2칸까지 닿음")) },
            { RelicType.PhantomBanner, (Loc.T("망령의 깃발"), Loc.F("방마다 1번, 체력이 {0:0}% 아래로 떨어지면 허수아비가 나타나 {1}턴 동안 몹들의 공격을 받아 낸다", PhantomBannerHealthRatio * 100f, PhantomBannerTurns)) },
            { RelicType.PulseCore, (Loc.T("파동의 핵"), Loc.F("회전 베기에 맞은 몹 {0}턴 기절", PulseCoreStunTurns)) },
            { RelicType.SniperEye, (Loc.T("저격수의 눈"), Loc.T("치명타 피해 +50%p")) },
            { RelicType.PiercingSeal, (Loc.T("관통의 인장"), Loc.T("일반 공격이 대상 뒤 1칸 몹에게도 30% 피해")) },
            { RelicType.TyrantPlate, (Loc.T("폭군의 갑주"), Loc.T("대시로 내려선 자리 주변 8칸 몹에게 30% 피해")) },
            { RelicType.Executioner, (Loc.T("사형 집행자"), Loc.T("일반 몹에게 주는 피해 +30%")) },
            { RelicType.CrownOfEnd, (Loc.T("끝의 왕관"), Loc.T("공격력 +10, 최대체력 +50")) },
            { RelicType.BloodPact, (Loc.T("피의 계약"), Loc.T("최대체력(성장분) -20%, 흡혈 상한 50%→70%")) },
            { RelicType.ChainBlast, (Loc.T("연쇄 폭발"), Loc.T("일반 몹 처치 시 주변 8칸 몹에게 공격력 20% 피해")) },
            { RelicType.SecondWind, (Loc.T("두 번째 숨"), Loc.T("시도마다 1회, 죽으면 체력 30%로 부활")) },
            { RelicType.TreasureHunter, (Loc.T("오래된 보물지도"), Loc.T("보물상자 골드 2배, 보물상자에서 아이템 2개, 몹 아이템 드롭 +3%p")) },
            { RelicType.SpinningBlade, (Loc.T("회전 칼날"), Loc.T("회전 베기 피해 80%→120%")) },
            { RelicType.HunterEye, (Loc.T("사냥꾼의 눈"), Loc.T("시야 +2칸, 출구가 처음부터 보임")) },
            { RelicType.Greed, (Loc.T("탐욕"), Loc.T("골드 +40%, 데스 패널티 40%→60%")) },
            { RelicType.Gale, (Loc.T("질풍"), Loc.T("대시 쿨타임 -2턴(최소 1턴)")) },
            { RelicType.RegenMoss, (Loc.T("재생의 이끼"), Loc.T("방 클리어 시 체력 15% 추가 회복")) },
            { RelicType.IronSkin, (Loc.T("철의 피부"), Loc.T("받는 피해 -10%(카드 피해 감소와 별도), 받은 피해의 5%를 곧바로 회복")) },
            { RelicType.Berserker, (Loc.T("광전사"), Loc.T("체력 50% 이하일 때 주는 피해 +30%")) },
            { RelicType.FirstStrike, (Loc.T("첫 일격"), Loc.T("방마다 첫 공격 피해 1.5배")) },
            { RelicType.KnightMemory, (Loc.T("기사의 기억"), Loc.T("몹·보스의 공격을 12% 확률로 간파해 피해를 받지 않음")) },
            { RelicType.ShadowStep, (Loc.T("그림자 걸음"), Loc.T("대시가 몹을 뚫고 지나가며 길 위의 몹을 모두 벤다")) },
            { RelicType.Whirlwind, (Loc.T("소용돌이"), Loc.T("회전 베기에 맞은 몹을 2칸 밀어낸다(막히면 공격력 30% 추가 피해, 보스는 안 밀림)")) },
            { RelicType.StoppedWatch, (Loc.T("멈춘 회중시계"), Loc.T("누적 사망 1회마다 주는 피해 +0.5% (최대 +25%)")) },
            { RelicType.RecurrenceMark, (Loc.T("되풀이의 각인"), Loc.T("지금 층에서 죽을 때마다 받는 피해 -2% (최대 -10%, 다음 층에서 초기화)")) },
            { RelicType.PuzzleGlove, (Loc.T("퍼즐 장인의 장갑"), Loc.T("퍼즐·미니게임 성공 보상(골드·영약) 2배")) },
            { RelicType.MerchantSeal, (Loc.T("상인의 인장"), Loc.T("던전 떠돌이 상인 가격 -20%")) },
            { RelicType.ContractBrand, (Loc.T("계약자의 낙인"), Loc.T("저주 계약의 대가가 절반")) },
            { RelicType.BloodHunter, (Loc.T("피의 사냥꾼"), Loc.T("몹을 처치할 때마다 최대 체력의 2% 회복")) },
            { RelicType.EliteHunter, (Loc.T("정예 사냥꾼"), Loc.T("정예 몹·미믹에게 주는 피해 +30%, 미믹·도전의 깃발 보상 골드 2배")) },
            { RelicType.EchoBlade, (Loc.T("메아리 칼날"), Loc.T("일반 공격 3번째마다 같은 대상을 한 번 더 벤다(25% 피해, 흡혈 없음)")) },
            { RelicType.AlchemistPouch, (Loc.T("연금술사의 주머니"), Loc.T("아이템을 종류마다 2개까지 가질 수 있다")) },
            { RelicType.PowderMaster, (Loc.T("화약 장인"), Loc.T("폭탄 폭발 범위 3x3 → 5x5")) },
            { RelicType.SnareHunter, (Loc.T("사냥꾼의 올가미"), Loc.T("덫에 걸려 기절한 몹은 받는 피해 +50%")) },
        });

        private static Dictionary<RelicType, (string Name, string Description)> Info => InfoCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

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
        public static bool IsHiddenRelic(RelicType type) => type == RelicType.KnightMemory;
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
                .Select(i => (RelicType)i).Where(r => !Owned.Contains(r) && !IsHiddenRelic(r)).OrderBy(_ => Rng.Next()).ToList();
            foreach (var r in general)
            {
                if (offer.Count >= OfferCount)
                    break;
                offer.Add(r);
            }
            foreach (var r in offer)
                Codex.Discover(Codex.RelicKey(r)); // 후보로 본 유물도 도감에 기록(고르지 않아도)
            return offer.Count > 0 ? offer : null;
        }

        /// <summary>테스트 메뉴용 - 플레이어 없이 저장에만 넣고 뺀다(피의 계약·끝의 왕관처럼 얻는 순간 스탯이 바뀌는 유물은 Acquire를 쓴다).</summary>
        public static void GrantForTest(RelicType type)
        {
            EnsureLoaded();
            if (Owned.Add(type))
                Save();
        }

        public static void RemoveForTest(RelicType type)
        {
            EnsureLoaded();
            if (Owned.Remove(type))
                Save();
        }

        /// <summary>엔딩 뒤 새 게임(SaveReset.ResetForNewGame) - 고른 유물 하나만 가진 채로 시작한다(10-27 사용자 "10층 클리어 후 유물 하나를 들고 간다").</summary>
        public static void KeepOnly(RelicType type)
        {
            EnsureLoaded();
            Owned.Clear();
            Owned.Add(type);
            Save();
        }

        /// <summary>유물을 얻는다 - 한 번만 적용되는 효과(피의 계약 최대체력, 끝의 왕관 스탯)는 지금 플레이어에 바로 반영한다.</summary>
        public static void Acquire(RelicType type, PlayerActor player)
        {
            EnsureLoaded();
            if (!Owned.Add(type))
                return;
            Save();
            Codex.Discover(Codex.RelicKey(type));

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
        /// <summary>멈춘 회중시계 - 누적 사망 수만큼 주는 피해 배율.</summary>
        public static float StoppedWatchMultiplier => Has(RelicType.StoppedWatch)
            ? 1f + Mathf.Min(StoppedWatchMax, LoopRecord.TotalDeaths * StoppedWatchPerDeath) : 1f;
        /// <summary>되풀이의 각인 - 지금 층에서 죽은 횟수만큼 받는 피해 배율.</summary>
        public static float RecurrenceMultiplier => Has(RelicType.RecurrenceMark)
            ? 1f - Mathf.Min(RecurrenceMax, StageProgress.DeathsThisStage * RecurrencePerDeath) : 1f;
        public static int ItemCapacity => Has(RelicType.AlchemistPouch) ? AlchemistPouchCapacity : 1;
        public static float VisionRadius => FogOfWar.VisionRadius + (Has(RelicType.HunterEye) ? HunterEyeVisionBonus : 0f);
    }
}
