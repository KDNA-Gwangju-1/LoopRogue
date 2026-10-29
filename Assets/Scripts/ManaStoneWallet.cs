using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>두 번째 영구 재화 "마석"(10-29 사용자) - 정예 몹을 잡거나 로비에서 골드를 환전해서 모은다. 쓰는 곳은 보물 상인의 유물 뽑기
    /// (나중에 유물 강화·캐릭터 해금도 여기서 꺼내 쓸 예정). GoldWallet처럼 PlayerPrefs 정수 하나.
    /// 유물 뽑기는 던전을 떠도는 마석 상인(마석이 1개 이상일 때만 방을 비운 뒤 가끔 나온다), 환전은 로비 보물 상인.</summary>
    public static class ManaStoneWallet
    {
        public const string PrefKey = "LoopRogue_ManaStone";
        private const string RelicDrawsKey = "LoopRogue_ManaStoneRelicDraws";

        /// <summary>정예를 잡으면 이 확률로 마석 1개(10-29 사용자 - 예전 1 + (층-1)/3개 확정은 봇 100판에서 정예 판당 226마리로 마석이 너무 쌓여
        /// 한 층 한 번 만나는 마석 상인에게서도 판당 유물 22개를 뽑았다).</summary>
        public const float EliteDropChance = 0.25f;

        /// <summary>환전 - 마석 1개 = 1만 골드(10-29 사용자, 층과 상관없이 고정).</summary>
        public const int ExchangeCost = 10000;

        /// <summary>유물 뽑기(떠돌이 마석 상인) - 첫 뽑기 마석 1개, 뽑을 때마다 1개씩 비싸진다(10-29 사용자 1·2·3…).</summary>
        public const int RelicDrawBaseCost = 1;
        public const int RelicDrawCostStep = 1;
        public static int RelicDraws => PlayerPrefs.GetInt(RelicDrawsKey, 0);

        /// <summary>떠돌이 마석 상인은 한 층에 한 번만(10-29 사용자 - 봇 100판에서 판당 53번 만나 일반 유물을 거의 다 뽑았다).
        /// 마지막으로 나온 층을 저장해 두고, 그 층에선 죽고 다시 돌아도 안 나온다. 저장 초기화(새 게임)면 지워진다.</summary>
        private const string DealerStageKey = "LoopRogue_RelicDealerStage";
        public static bool DealerMetOnStage(int stage) => PlayerPrefs.GetInt(DealerStageKey, 0) == stage;

        public static void MarkDealerMet(int stage)
        {
            PlayerPrefs.SetInt(DealerStageKey, stage);
            PlayerPrefs.Save();
        }
        public static int RelicDrawCost => RelicDrawBaseCost + RelicDrawCostStep * PlayerPrefs.GetInt(RelicDrawsKey, 0);

        private static int _stones;
        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static int Stones
        {
            get
            {
                EnsureLoaded();
                return _stones;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _stones = PlayerPrefs.GetInt(PrefKey, 0);
            _loaded = true;
        }

        public static void Add(int amount)
        {
            if (amount <= 0)
                return;
            EnsureLoaded();
            _stones += amount;
            PlayerPrefs.SetInt(PrefKey, _stones);
            PlayerPrefs.Save();
        }

        public static bool TrySpend(int amount)
        {
            EnsureLoaded();
            if (amount <= 0 || _stones < amount)
                return false;
            _stones -= amount;
            PlayerPrefs.SetInt(PrefKey, _stones);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>골드 → 마석 1개. 골드가 모자라면 false.</summary>
        public static bool TryExchange()
        {
            if (!GoldWallet.TrySpend(ExchangeCost))
                return false;
            Add(1);
            return true;
        }

        /// <summary>뽑기 후보 - 아직 없는 일반 유물(보스 전용·히든 제외).</summary>
        public static RelicType[] RelicCandidates() => System.Enum.GetValues(typeof(RelicType)).Cast<RelicType>()
            .Where(r => !Relics.IsBossRelic(r) && !Relics.IsHiddenRelic(r) && !Relics.Has(r)).ToArray();

        public enum DrawFail { None, NoCandidates, NotEnough }

        /// <summary>마석으로 유물 하나를 무작위로 얻는다(던전의 마석 상인) - 얻는 순간 효과(피의 계약 최대 체력)도 지금 플레이어에 바로.</summary>
        public static RelicType? DrawRelic(PlayerActor player, out DrawFail fail)
        {
            var candidates = RelicCandidates();
            if (candidates.Length == 0)
            {
                fail = DrawFail.NoCandidates;
                return null;
            }
            if (!TrySpend(RelicDrawCost))
            {
                fail = DrawFail.NotEnough;
                return null;
            }
            PlayerPrefs.SetInt(RelicDrawsKey, PlayerPrefs.GetInt(RelicDrawsKey, 0) + 1);
            var relic = candidates[Random.Range(0, candidates.Length)];
            Relics.Acquire(relic, player);
            fail = DrawFail.None;
            return relic;
        }
    }
}
