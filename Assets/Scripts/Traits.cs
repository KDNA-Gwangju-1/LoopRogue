using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>캐릭터 특성(11-02 사용자 - 유틸을 보완하는 방향). 로비에서 T로 여는 특성 창(TraitPanel)에서 배운다.
    /// 4갈래 × 5칸, 같은 갈래는 앞 칸을 배워야 다음 칸, 5번째 칸(대표 특성)은 전체에서 MaxSignatures개까지(11-02 사용자 1 → 2). 한 칸 = 1포인트, 언제든 되돌릴 수 있다.
    /// 포인트 = 층 보스 첫 처치 수(지금 층 - 1). PlayerPrefs 정수 하나씩이라 저장 초기화(DeleteAll)로 같이 지워진다.</summary>
    public enum TraitType
    {
        DashClone, // 대시 1 - 대시하면 분신 둘이 앞 대각선 두 갈래로 같이 돌진
        DashReturn, // 대시 2 - 대시 바로 다음 행동에 R을 누르면 시작 자리로 돌아온다
        DashChain,  // 대시 3 - 대시를 쿨타임 없이 3번까지 이어서
        DashWave,   // 대시 4 - 대시로 지나온 길과 양옆, 폭 3줄에 대시 피해
        DashMark,   // 대시 5(대표) - 대시로 같은 몹을 3번 치면 표식이 터져 추가 피해
        SpinHold,   // 회전 1 - 회전 베기가 한 턴에 3번 벤다(매번 기본 피해, 11-02 - 예전엔 3턴 제자리 회전)
        SpinFree,   // 회전 2 - 회전이 3턴 동안 이어지고 그동안 다른 행동을 할 수 있다(바퀴마다 피해 50%)
        SpinLong,   // 회전 3 - 회전 유지 3턴 → 5턴
        SpinGuard,  // 회전 4 - 도는 턴엔 받는 피해 -30% + 화살·거미줄 쳐내기
        SpinStorm,  // 회전 5(대표) - 도는 동안 3마리 처치마다 회전 +1턴
        // 생활 갈래(11-02 사용자 "탐색 / 상인")
        ExploreVision,  // 탐색 1 - 시야 +1칸
        ExploreExit,    // 탐색 2 - 출구가 처음부터 보임
        ExploreChest,   // 탐색 3 - 보물상자 골드 +50%
        ExplorePuzzle,  // 탐색 4 - 퍼즐·미니게임 골드 +50%
        ExploreFind,    // 탐색 5(대표) - 일반 방을 비우면 20% 확률로 방 클리어 골드 5배(11-02, 예전엔 15% 아이템)
        TradeMerchant,  // 상인 1 - 던전 떠돌이 상인 가격 -15%
        TradeShop,      // 상인 2 - 로비 잡화점 가격 -20%
        TradeGold,      // 상인 3 - 골드 획득 ×1.3(카드 상한과 따로)
        TradeInsurance, // 상인 4 - 데스 패널티 -15%p
        TradeLuck,      // 상인 5(대표) - 떠돌이 상인 등장 확률 +10%p, 마석 상인 한 층 2번까지
    }

    /// <summary>특성 칸 하나 - Branch 0~3, Tier 0~4(4 = 대표 특성).</summary>
    public sealed class TraitDef
    {
        public readonly TraitType Type;
        public readonly int Branch;
        public readonly int Tier;
        private readonly string _name;
        private readonly string _description;

        public TraitDef(TraitType type, int branch, int tier, string name, string description)
        {
            Type = type;
            Branch = branch;
            Tier = tier;
            _name = name;
            _description = description;
        }

        public string Name => Loc.T(_name);
        public string Description => Loc.T(_description);
    }

    public static class Traits
    {
        private const string KeyPrefix = "LoopRogue_Trait_";
        private const string BonusPointsKey = "LoopRogue_TraitBonusPoints"; // 테스트 메뉴로 더한 포인트

        public const int BranchCount = 4;
        public const int TierCount = 5;
        public const int SignatureTier = TierCount - 1;
        /// <summary>대표 특성(5번째 칸)을 모든 갈래를 통틀어 이만큼까지.</summary>
        public const int MaxSignatures = 2;

        /// <summary>전투 갈래(대시·회전) / 생활 갈래(탐색·상인) - 11-03 사용자 "전투는 한쪽만, 생활도 한쪽만": 같은 무리에서 한 갈래라도 찍었으면
        /// 다른 갈래는 1번째 칸부터 못 찍는다(되돌리면 다시 고를 수 있다). 예전(11-02)엔 다른 전투 갈래 2번째 칸까지 허용.</summary>
        public static bool IsCombatBranch(int branch) => branch <= 1;

        // ---- 고른 갈래(11-03 사용자 "특성 창에 들어가면 대시/회전 중 하나, 탐색/상인 중 하나를 먼저 고르고 그 갈래만") ----
        private const string ChosenCombatKey = "LoopRogue_TraitChosenCombat";
        private const string ChosenLifeKey = "LoopRogue_TraitChosenLife";

        /// <summary>그 무리(전투/생활)에서 고른 갈래 - 아직 안 골랐으면 -1. 예전 저장(고르기 전에 찍은 칸이 있음)은 찍은 갈래로 본다.</summary>
        public static int Chosen(bool combat)
        {
            var chosen = Prefs.GetInt(combat ? ChosenCombatKey : ChosenLifeKey, -1);
            if (chosen >= 0)
                return chosen;
            var learned = All.FirstOrDefault(d => IsCombatBranch(d.Branch) == combat && Has(d.Type));
            return learned != null ? learned.Branch : -1;
        }

        public static void Choose(int branch)
        {
            Prefs.SetInt(IsCombatBranch(branch) ? ChosenCombatKey : ChosenLifeKey, branch);
            Prefs.Save();
        }

        /// <summary>스킬 도우미가 갈래를 골라 주는 때 - 1층 보스를 잡은 뒤(11-03 사용자).</summary>
        public static bool ChoiceUnlocked => BossPoints >= 1;

        /// <summary>아직 갈래를 안 고른 무리가 있으면 그 무리(전투 먼저) - 없으면 null.</summary>
        public static bool? PendingChoice => Chosen(true) < 0 ? true : Chosen(false) < 0 ? false : (bool?)null;

        // ---- 생활 갈래 수치 ----
        public const float ExploreVisionBonus = 1f;
        public const float ExploreChestGoldRate = 1.5f;
        public const float ExplorePuzzleGoldRate = 1.5f;
        public const float ExploreFindChance = 0.2f;
        public const int ExploreFindGoldRooms = 5; // 노다지 - 방 클리어 골드 × 이만큼 더
        public const float TradeMerchantDiscount = 0.85f;
        public const float TradeShopDiscount = 0.8f;
        public const float TradeGoldBonus = 0.3f; // 11-02 사용자 15 → 30%, 골드 카드 상한(100%)과 따로 곱한다
        public const float TradeInsuranceCut = 0.15f;
        public const float TradeLuckNpcBonus = 0.1f; // 11-02 사용자 - 상인·거래상·마석 상인 등장 확률 각 +10%p
        public const int TradeLuckDealersPerStage = 2; // 마석 상인 한 층 최대(기본 1)

        /// <summary>분신 대시 - 분신 한 명이 주는 피해(대시 피해 대비).</summary>
        public const float DashCloneDamageRate = 0.5f;

        /// <summary>되돌아오기 - 대시 뒤 이만큼의 행동 안에 Q(11-02 사용자 1턴 - 대시 바로 다음 행동에서만, 다른 행동을 하면 사라진다).</summary>
        public const int DashReturnTurns = 1;

        /// <summary>연속 대시 - 쿨타임 없이 이어 쓸 수 있는 대시 수(첫 대시 포함).</summary>
        public const int DashChainCount = 3;

        /// <summary>대시 파동 - 지나온 길 폭 3줄에 공격력 × 이만큼(대시와 같은 100%, 11-02 사용자).</summary>
        public const float DashWaveDamageRate = 1f;

        /// <summary>표식 폭발 - 대시로 같은 몹을 이만큼 치면 터진다, 공격력 × DashMarkDamageRate(11-02 사용자 "20% 정도로 낮게").</summary>
        public const int DashMarkCount = 3;
        public const float DashMarkDamageRate = 1f; // 11-02 사용자 20% → 100%

        /// <summary>회전 유지 - 회전 베기를 쓴 턴 포함 이만큼 돈다(회전 3 "긴 회전"이면 5).</summary>
        public static int SpinHoldTurns => Has(TraitType.SpinLong) ? 5 : 3;

        /// <summary>회전 유지 - 바퀴마다 회전 베기 피해의 이만큼(첫 바퀴도).</summary>
        public const float SpinHoldDamageRate = 0.5f;

        /// <summary>삼연 회전(회전 1) - 한 턴에 베는 횟수.</summary>
        public const int SpinTripleCount = 3;

        /// <summary>칼날 방패 - 돈 턴에 받는 피해 배율(-30%).</summary>
        public const float SpinGuardDamageFactor = 0.7f;

        /// <summary>끝없는 회전 - 이만큼 처치할 때마다 회전 +1턴(11-02 사용자 1 → 3, 5x5 확장은 뺌).</summary>
        public const int SpinStormKillsPerTurn = 3;

        // 갈래 이름 - 3·4번째 갈래는 아직 정하지 않아 "???"(칸도 전부 준비 중)
        private static readonly string[] BranchNameKeys = { "대시", "회전", "탐색", "상인" };

        public static string BranchName(int branch) => Loc.T(BranchNameKeys[branch]);

        public static readonly TraitDef[] All =
        {
            new TraitDef(TraitType.DashClone, 0, 0, "분신 대시", "대시하면 분신 둘이 앞 대각선 두 갈래로 함께 돌진해 처음 만난 몹을 벤다(대시 피해의 50%, 대시 유물 효과도 적용)"),
            new TraitDef(TraitType.DashReturn, 0, 1, "되돌아오기", "대시 바로 다음 턴에 R을 누르면 대시를 시작한 자리로 돌아온다"),
            new TraitDef(TraitType.DashChain, 0, 2, "연속 대시", "대시를 쿨타임 없이 3번까지 이어서 쓸 수 있다 - Q는 처음 한 번만, 그 뒤엔 방향키만. 다른 행동을 하거나 3번을 다 쓰면 쿨타임"),
            new TraitDef(TraitType.DashWave, 0, 3, "대시 파동", "대시로 지나온 길과 그 양옆 줄(폭 3줄)에 있는 몹에게도 대시 피해"),
            new TraitDef(TraitType.DashMark, 0, 4, "표식 폭발", "대시로 친 몹에 표식이 쌓이고, 같은 몹에 3개가 쌓이면 터져 공격력의 100% 추가 피해"),
            new TraitDef(TraitType.SpinHold, 1, 0, "삼연 회전", "회전 베기가 한 턴에 3번 벤다(매번 회전 베기 피해 그대로)"),
            new TraitDef(TraitType.SpinLong, 1, 2, "긴 회전", "회전 유지가 3턴에서 5턴으로 늘어난다"),
            new TraitDef(TraitType.SpinGuard, 1, 3, "칼날 방패", "도는 턴에는 받는 피해 -30%, 화살과 거미줄을 쳐낸다"),
            new TraitDef(TraitType.SpinStorm, 1, 4, "끝없는 회전", "도는 동안 몹을 3마리 처치할 때마다 회전이 1턴 늘어난다"),
            new TraitDef(TraitType.SpinFree, 1, 1, "회전 중 행동", "회전 베기가 3턴 동안 이어지고(매 바퀴 회전 베기 피해의 50%), 그동안 걷고 공격하고 대시할 수 있다 - 내 턴이 끝날 때마다 그 자리에서 한 바퀴"),
            new TraitDef(TraitType.ExploreVision, 2, 0, "넓은 시야", "시야 +1칸"),
            new TraitDef(TraitType.ExploreExit, 2, 1, "길잡이", "방에 들어가면 출구가 처음부터 보인다"),
            new TraitDef(TraitType.ExploreChest, 2, 2, "보물 감각", "보물상자에서 얻는 골드 +50%"),
            new TraitDef(TraitType.ExplorePuzzle, 2, 3, "퍼즐 달인", "퍼즐·미니게임 성공 골드 +50%"),
            new TraitDef(TraitType.ExploreFind, 2, 4, "노다지", "일반 방을 비우면 20% 확률로 방 클리어 골드의 5배를 더 얻는다"),
            new TraitDef(TraitType.TradeMerchant, 3, 0, "흥정", "던전 떠돌이 상인 가격 -15%"),
            new TraitDef(TraitType.TradeShop, 3, 1, "단골", "로비 잡화점 가격 -20%"),
            new TraitDef(TraitType.TradeGold, 3, 2, "큰손", "골드 획득 +30%(골드 카드 상한과 따로 적용)"),
            new TraitDef(TraitType.TradeInsurance, 3, 3, "보험", "데스 패널티(이번 시도 골드 손실) -15%p"),
            new TraitDef(TraitType.TradeLuck, 3, 4, "행운의 손님", "떠돌이 상인·그림자 거래상·마석 상인 등장 확률 각 +10%p, 마석 상인은 한 층에 2번까지"),
        };

        /// <summary>그 칸의 특성 - 아직 없는 칸이면 null.</summary>
        public static TraitDef At(int branch, int tier) => All.FirstOrDefault(d => d.Branch == branch && d.Tier == tier);

        public static TraitDef Def(TraitType type) => All.First(d => d.Type == type);

        public static bool Has(TraitType type) => Prefs.GetInt(KeyPrefix + type, 0) != 0;

        public static void Set(TraitType type, bool on) => Prefs.SetInt(KeyPrefix + type, on ? 1 : 0);

        // ---- 포인트(11-02 사용자) - 칸 비용 1·1·2·2·3(한 갈래 9점). 보스 첫 처치 1점 + 정예 처치 10% 확률로 1점(최대 EliteMaxPoints) →
        // 9층 보스까지 잡으면 보스 9 + 정예 9 = 18점 = 두 갈래 5칸까지. ----
        private const string ElitePointsKey = "LoopRogue_TraitElitePoints";
        public const float EliteDropChance = 0.1f;
        public const int EliteMaxPoints = 9;

        /// <summary>칸 하나 비용 - 1·2번 칸 1점, 3·4번 칸 2점, 5번(대표) 칸 3점.</summary>
        public static int Cost(TraitDef def) => def.Tier < 2 ? 1 : def.Tier < 4 ? 2 : 3;

        /// <summary>보스 첫 처치로 얻은 포인트(올라가 본 가장 높은 층 - 1 = 1~9층 보스, 10층 보스는 안 준다 - 11-02 사용자). 층 선택으로 낮은 층에 들어가도 줄지 않는다.</summary>
        public static int BossPoints => Mathf.Max(0, StageProgress.MaxStageReached - 1);

        /// <summary>정예 처치로 얻은 포인트(최대 EliteMaxPoints).</summary>
        public static int ElitePoints => Mathf.Min(EliteMaxPoints, Prefs.GetInt(ElitePointsKey, 0));

        /// <summary>지금 정예 포인트 상한 - 잡은 보스 수(B)에 따라 (B-1)×9/8(내림): 보스 5 → 4(합계 9), 보스 9 → 9(합계 18)
        /// (11-02 사용자 "5층에서 9개, 9층에서 18개가 최고").</summary>
        public static int ElitePointCap => Mathf.Clamp((BossPoints - 1) * EliteMaxPoints / 8, 0, EliteMaxPoints);

        /// <summary>정예를 잡았다(RoomController) - 10% 확률로 1점(지금 상한이면 안 굴린다). 얻었으면 true.</summary>
        public static bool TryEliteDrop(System.Random rng)
        {
            if (ElitePoints >= ElitePointCap)
                return false; // 상한이면 굴리지도 않는다(보정 횟수도 안 쌓임)
            // 운 보정(11-02 사용자) - EliteDropPity마리 연속 못 얻으면 그다음은 확정
            var misses = Prefs.GetInt(ElitePityKey, 0);
            if (misses + 1 < EliteDropPity && rng.NextDouble() >= EliteDropChance)
            {
                Prefs.SetInt(ElitePityKey, misses + 1);
                return false;
            }
            Prefs.SetInt(ElitePityKey, 0);
            Prefs.SetInt(ElitePointsKey, ElitePoints + 1);
            Prefs.Save();
            return true;
        }

        private const string ElitePityKey = "LoopRogue_TraitElitePity";
        /// <summary>정예 특성 포인트 운 보정 - 이만큼째 정예는 확정(앞에서 한 번도 못 얻었으면).</summary>
        public const int EliteDropPity = 10;

        public static int EarnedPoints => BossPoints + ElitePoints + Prefs.GetInt(BonusPointsKey, 0);

        public static int SpentPoints => All.Where(d => Has(d.Type)).Sum(Cost);

        public static int FreePoints => EarnedPoints - SpentPoints;

        public static void AddBonusPoints(int amount) => Prefs.SetInt(BonusPointsKey, Prefs.GetInt(BonusPointsKey, 0) + amount);

        public enum State
        {
            Learned,
            Available,
            NeedPrevious,   // 같은 갈래 앞 칸을 먼저
            SignatureTaken, // 대표 특성을 이미 MaxSignatures개 배움
            BranchLocked,   // 같은 무리(전투·생활)에서 다른 갈래를 골랐음
            NeedChoice,     // 그 무리 갈래를 아직 안 골랐음
            NoPoints,
        }

        public static State GetState(TraitDef def)
        {
            if (Has(def.Type))
                return State.Learned;
            if (def.Tier > 0)
            {
                var previous = At(def.Branch, def.Tier - 1);
                if (previous == null || !Has(previous.Type))
                    return State.NeedPrevious;
            }
            if (def.Tier == SignatureTier && All.Count(d => d.Tier == SignatureTier && Has(d.Type)) >= MaxSignatures)
                return State.SignatureTaken;
            var chosen = Chosen(IsCombatBranch(def.Branch));
            if (chosen < 0)
                return State.NeedChoice;
            if (chosen != def.Branch)
                return State.BranchLocked;
            return FreePoints >= Cost(def) ? State.Available : State.NoPoints;
        }

        public static bool Learn(TraitDef def)
        {
            if (Chosen(IsCombatBranch(def.Branch)) < 0)
                Choose(def.Branch); // 창을 안 거치는 쪽(자동 플레이 봇·테스트) - 처음 찍는 갈래가 곧 고른 갈래
            if (GetState(def) != State.Available)
                return false;
            Set(def.Type, true);
            Prefs.Save();
            return true;
        }

        /// <summary>되돌린다 - 같은 갈래의 뒤 칸도 같이(앞 칸 없이 뒤 칸만 남지 않게). 되돌린 칸 수.</summary>
        public static int Unlearn(TraitDef def)
        {
            var count = 0;
            foreach (var d in All.Where(d => d.Branch == def.Branch && d.Tier >= def.Tier))
                if (Has(d.Type))
                {
                    Set(d.Type, false);
                    count++;
                }
            Prefs.Save();
            return count;
        }

        public static void ResetAll()
        {
            foreach (var d in All)
                Set(d.Type, false);
            Prefs.SetInt(ChosenCombatKey, -1); // 갈래도 다시 고른다
            Prefs.SetInt(ChosenLifeKey, -1);
            Prefs.Save();
        }
    }
}
