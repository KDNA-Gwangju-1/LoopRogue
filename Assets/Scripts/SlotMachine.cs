using UnityEngine;

namespace LoopRogue
{
    /// <summary>로비 슬롯머신("도박으로 골드 불리기", 사람 전용 - 봇은 안 씀). 확률 조작 없이 릴 3개가
    /// 각자 그림 5종 중 하나를 균등하게 뽑는다: 다 다르면 꽝, 2개 같으면 0.5배, 3개 같으면 10배.
    /// 기댓값 = 3개(1/25)*10 + 2개(12/25)*0.5 = 64% - 오래 하면 줄어드는 진짜 도박 구조(사용자 확정).
    /// 베팅액은 골드 보상과 같은 곡선(RewardMultiplier)으로 커져서 스테이지가 올라도 체감 크기가 비슷하다.</summary>
    public static class SlotMachine
    {
        private static readonly LocCache<string[]> SymbolsCache = new LocCache<string[]>(() => new string[] { Loc.T("검"), Loc.T("방패"), Loc.T("왕관"), Loc.T("해골"), Loc.T("보석") });
        public static string[] Symbols => SymbolsCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private const int BaseBet = 20;
        public const int HighBetMultiplier = 10;
        private const float PairPayout = 0.5f;
        private const float TriplePayout = 10f;

        public struct SpinResult
        {
            public int[] Reels;
            public int Bet;
            public int Payout;
            public int MatchCount; // 1 = 다 다름, 2 = 2개 같음, 3 = 3개 같음
            public bool UsedTicket; // 골드 대신 이용권으로 돌렸으면 true
        }

        public static int GetBet(bool high) =>
            Mathf.RoundToInt(BaseBet * StageScaling.RewardMultiplier(StageProgress.CurrentStage)) * (high ? HighBetMultiplier : 1);

        // ---- 이용권(던전 떠돌이 상인이 10장 묶음으로 판다) - 일반 베팅 1회를 골드 없이 돌린다. 영구 저장. ----
        private const string TicketKey = "LoopRogue_SlotTickets";
        private static bool _ticketsLoaded;
        private static int _tickets;

        public static int Tickets
        {
            get
            {
                if (!_ticketsLoaded)
                {
                    _tickets = PlayerPrefs.GetInt(TicketKey, 0);
                    _ticketsLoaded = true;
                }
                return _tickets;
            }
        }

        public static void Reload() => _ticketsLoaded = false;

        public static void AddTickets(int count)
        {
            _tickets = Tickets + count;
            PlayerPrefs.SetInt(TicketKey, _tickets);
            PlayerPrefs.Save();
        }

        /// <summary>골드가 모자라면 null. 베팅액을 먼저 빼고 당첨금을 더한다. 일반 베팅은 이용권이 있으면 골드 대신 이용권 1장을 쓴다.</summary>
        public static SpinResult? Spin(bool high)
        {
            var bet = GetBet(high);
            var usedTicket = !high && Tickets > 0;
            if (usedTicket)
                AddTickets(-1);
            else if (!GoldWallet.TrySpend(bet))
                return null;

            var reels = new int[3];
            for (var i = 0; i < reels.Length; i++)
                reels[i] = Random.Range(0, Symbols.Length);

            var matchCount = 1;
            if (reels[0] == reels[1] && reels[1] == reels[2])
                matchCount = 3;
            else if (reels[0] == reels[1] || reels[1] == reels[2] || reels[0] == reels[2])
                matchCount = 2;

            var payout = matchCount == 3 ? Mathf.RoundToInt(bet * TriplePayout)
                : matchCount == 2 ? Mathf.RoundToInt(bet * PairPayout)
                : 0;
            GoldWallet.Add(payout);
            if (matchCount == 3)
                Achievements.Unlock("ACH_JACKPOT");

            return new SpinResult { Reels = reels, Bet = bet, Payout = payout, MatchCount = matchCount, UsedTicket = usedTicket };
        }
    }
}
