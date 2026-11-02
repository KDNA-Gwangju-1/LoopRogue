using UnityEngine;

namespace LoopRogue
{
    /// <summary>로비 슬롯머신("도박으로 골드 불리기", 사람 전용 - 봇은 안 씀). 확률 조작 없이 릴 3개가
    /// 각자 그림 5종 중 하나를 균등하게 뽑는다: 3개 같음 1/25, 2개 같음 12/25, 다 다름 12/25.
    /// 11-02 사용자: 거는 돈을 직접 정하고(BetStep 단위, 최대 = 100 + 층 × 100 - 1층 200·2층 300…), 배율을 2~5배(0.5 단위)로 고른다.
    /// 맞히면 거는 돈 × 배율, 못 맞히면 0 - 당첨 확률은 배율이 높을수록 낮다(WinChance = Expectation / 배율, 기댓값은 어느 배율이든 40%(11-02, 처음엔 64%),
    /// 예전 슬롯과 같은 "오래 하면 줄어드는 진짜 도박" - 사용자 확정). 릴 그림은 결과에 맞춰 그린다(당첨 = 3개 같음).
    /// 이용권(떠돌이 상인)은 골드 대신 TicketBet만큼 걸고 지금 배율로 한 번 돌린다.</summary>
    public static class SlotMachine
    {
        private static readonly LocCache<string[]> SymbolsCache = new LocCache<string[]>(() => new string[] { Loc.T("검"), Loc.T("방패"), Loc.T("왕관"), Loc.T("해골"), Loc.T("보석") });
        public static string[] Symbols => SymbolsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private const int BaseBet = 20;
        public const int MinBet = 10;
        public const int BetStep = 10;
        public const int BigBetStep = 100; // Shift

        private const string BetKey = "LoopRogue_SlotBet";
        private const string MultiplierKey = "LoopRogue_SlotMultiplier";

        public struct SpinResult
        {
            public int[] Reels;
            public int Bet;
            public int Payout;
            public int MatchCount; // 3 = 당첨(3개 같음), 1·2 = 꽝(릴 그림만)
            public float Multiplier;
            public bool UsedTicket; // 골드 대신 이용권으로 돌렸으면 true
        }

        /// <summary>거는 돈 최대 - 1층 200, 2층 300, … 10층 1100.</summary>
        public static int MaxBet => 100 + 100 * Mathf.Clamp(StageProgress.CurrentStage, 1, StageProgress.MaxStage);

        /// <summary>지금 거는 돈(저장됨) - 층이 바뀌어 최대가 줄었으면 최대로.</summary>
        public static int Bet
        {
            get => Mathf.Clamp(Prefs.GetInt(BetKey, Mathf.Min(MaxBet, TicketBet)), MinBet, MaxBet);
            set
            {
                Prefs.SetInt(BetKey, Mathf.Clamp(value, MinBet, MaxBet));
                Prefs.Save();
            }
        }

        // ---- 배율(2~5배, 0.5 단위) ----
        public const float MinMultiplier = 2f;
        public const float MaxMultiplier = 5f;
        public const float MultiplierStep = 0.5f;
        /// <summary>기댓값(돌려받는 비율) - 어느 배율이든 같다.</summary>
        public const float Expectation = 0.4f; // 11-02 사용자 "당첨 확률이 너무 높다" - 64% → 40%

        public static float Multiplier
        {
            get => Mathf.Clamp(Prefs.GetFloat(MultiplierKey, MinMultiplier), MinMultiplier, MaxMultiplier);
            set
            {
                Prefs.SetFloat(MultiplierKey, Mathf.Clamp(Mathf.Round(value / MultiplierStep) * MultiplierStep, MinMultiplier, MaxMultiplier));
                Prefs.Save();
            }
        }

        /// <summary>그 배율의 당첨 확률 - 2배 20%, 3배 13.3%, 4배 10%, 5배 8%.</summary>
        public static float WinChance(float multiplier) => Expectation / multiplier;

        /// <summary>이용권 한 장의 값(예전 일반 베팅액) - 떠돌이 상인 이용권 가격도 이걸로 잰다.</summary>
        public static int TicketBet => Mathf.RoundToInt(BaseBet * StageScaling.RewardMultiplier(StageProgress.CurrentStage));

        // ---- 이용권(던전 떠돌이 상인이 10장 묶음으로 판다) - 골드 없이 TicketBet만큼 걸고 한 번 돌린다. 영구 저장. ----
        public const string TicketKey = "LoopRogue_SlotTickets";
        private static bool _ticketsLoaded;
        private static int _tickets;

        public static int Tickets
        {
            get
            {
                if (!_ticketsLoaded)
                {
                    _tickets = Prefs.GetInt(TicketKey, 0);
                    _ticketsLoaded = true;
                }
                return _tickets;
            }
        }

        public static void Reload() => _ticketsLoaded = false;

        public static void AddTickets(int count)
        {
            _tickets = Tickets + count;
            Prefs.SetInt(TicketKey, _tickets);
            Prefs.Save();
        }

        /// <summary>골드(또는 이용권)가 모자라면 null. 베팅액을 먼저 빼고 당첨금을 더한다.</summary>
        public static SpinResult? Spin(bool useTicket)
        {
            int bet;
            if (useTicket)
            {
                if (Tickets <= 0)
                    return null;
                bet = TicketBet;
                AddTickets(-1);
            }
            else
            {
                bet = Bet;
                if (!GoldWallet.TrySpend(bet))
                    return null;
            }

            var multiplier = Multiplier;
            var win = Random.value < WinChance(multiplier);
            var reels = new int[3];
            if (win)
                reels[0] = reels[1] = reels[2] = Random.Range(0, Symbols.Length);
            else
                do
                {
                    for (var i = 0; i < reels.Length; i++)
                        reels[i] = Random.Range(0, Symbols.Length);
                } while (reels[0] == reels[1] && reels[1] == reels[2]); // 꽝은 3개가 같으면 안 된다

            var matchCount = win ? 3 : reels[0] == reels[1] || reels[1] == reels[2] || reels[0] == reels[2] ? 2 : 1;
            var payout = win ? Mathf.RoundToInt(bet * multiplier) : 0;
            GoldWallet.Add(payout);
            if (win && multiplier >= MaxMultiplier)
                Achievements.Unlock("ACH_JACKPOT"); // 잭팟 업적 - 최고 배율(5배)로 맞혔을 때

            PlayLog.Lobby($"슬롯 {multiplier}배 {(useTicket ? "이용권 " : "")}{bet}골드 → {(win ? "당첨" : "꽝")} {payout}");
            return new SpinResult { Reels = reels, Bet = bet, Payout = payout, MatchCount = matchCount, UsedTicket = useTicket, Multiplier = multiplier };
        }
    }
}
