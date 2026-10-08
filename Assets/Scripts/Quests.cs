using UnityEngine;

namespace LoopRogue
{
    /// <summary>던전 NPC "길 잃은 모험가"의 의뢰 - 각 층의 방 AcceptRoom(1)을 비우면 가끔 나타나 의뢰를 주고, 같은 층 방 ReportRoom(10, 보스 직전 방)을
    /// 비우면 다시 나타나서 말을 걸면 보상을 준다(RoomController.TrySpawnRoomClearNpc, NpcActor). 죽어도 취소되지 않고 로비를 오가도 이어지게
    /// 영구 저장한다. 의뢰를 들고 있는 동안엔 방 1에 다시 나오지 않는다. 방 10을 비우고 보고하지 않고 나가면 다음에 방 10을 비울 때 또 나온다.</summary>
    public static class RunQuest
    {
        private const string ActiveKey = "LoopRogue_Quest_Wanderer";

        public const int AcceptRoom = 1;
        public const int ReportRoom = 10;
        private const int RewardGoldBase = 150; // 지금 층의 골드 보상 배율을 곱한다
        public const int RewardGachaTickets = 1;

        private static bool _loaded;
        private static bool _active;

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _active = PlayerPrefs.GetInt(ActiveKey, 0) == 1;
        }

        public static bool Active
        {
            get
            {
                EnsureLoaded();
                return _active;
            }
        }

        public static string Goal => $"방 {ReportRoom}을 비우고 모험가에게 보고";

        public static int RewardGold => Mathf.RoundToInt(RewardGoldBase * StageScaling.RewardMultiplier(StageProgress.CurrentStage));

        public static string RewardText => $"골드 {RewardGold} + 뽑기권 {RewardGachaTickets}장";

        public static void Accept() => SetActive(true);

        /// <summary>방 10에 다시 나타난 모험가에게 보고했을 때(NpcActor) - 보상을 주고 안내 문구를 돌려준다.</summary>
        public static string Complete()
        {
            if (!Active)
                return null;
            SetActive(false);
            var gold = RewardGold;
            GoldWallet.Add(gold);
            GachaSystem.AddTickets(RewardGachaTickets);
            Achievements.Unlock("ACH_WANDERER");
            return $"모험가의 의뢰 완료! 골드 +{gold}, 뽑기권 +{RewardGachaTickets}";
        }

        /// <summary>GameHUD 왼쪽 위 의뢰 줄. 의뢰가 없으면 null.</summary>
        public static string TrackerText => Active ? $"의뢰: {Goal}" : null;

        private static void SetActive(bool active)
        {
            EnsureLoaded();
            _active = active;
            PlayerPrefs.SetInt(ActiveKey, active ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>로비 안내자의 장기 의뢰 - 영구 저장(PlayerPrefs). 스테이지를 새로 돌파할 때마다 1번, 누적 처치 KillMilestone마리마다 1번
    /// 보상을 받을 수 있고, 로비에서 안내자에게 말을 걸어 한꺼번에 받는다.</summary>
    public static class LobbyQuests
    {
        private const string StageClaimedKey = "LoopRogue_Quest_StageClaimed";
        private const string KillsKey = "LoopRogue_Quest_Kills";
        private const string KillClaimedKey = "LoopRogue_Quest_KillClaimed";

        public const int KillMilestone = 100;
        private const int StageRewardBase = 80;
        private const int KillRewardBase = 50;

        private static bool _loaded;
        private static int _stageClaimed; // 보상을 받은 마지막 "도달 스테이지"(처음 1 = 받을 것 없음)
        private static int _kills;
        private static int _killClaimed;  // 받은 처치 보상 횟수

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _stageClaimed = PlayerPrefs.GetInt(StageClaimedKey, 1);
            _kills = PlayerPrefs.GetInt(KillsKey, 0);
            _killClaimed = PlayerPrefs.GetInt(KillClaimedKey, 0);
        }

        public static int LifetimeKills
        {
            get
            {
                EnsureLoaded();
                return _kills;
            }
        }

        /// <summary>몹을 잡을 때마다(PlayerActor.ClaimKill, 졸개 제외). 저장은 다른 지갑이 PlayerPrefs.Save를 부를 때 같이 된다.</summary>
        public static void AddKill()
        {
            EnsureLoaded();
            _kills++;
            PlayerPrefs.SetInt(KillsKey, _kills);
        }

        public static int PendingStageRewards
        {
            get
            {
                EnsureLoaded();
                return Mathf.Max(0, StageProgress.CurrentStage - _stageClaimed);
            }
        }

        public static int PendingKillRewards
        {
            get
            {
                EnsureLoaded();
                return Mathf.Max(0, _kills / KillMilestone - _killClaimed);
            }
        }

        public static int PendingCount => PendingStageRewards + PendingKillRewards;

        /// <summary>다음 처치 보상까지 남은 마리 수.</summary>
        public static int KillsToNextMilestone
        {
            get
            {
                EnsureLoaded();
                return KillMilestone - _kills % KillMilestone;
            }
        }

        /// <summary>stage에 새로 도달한 보상 - 방금 깬 스테이지(stage - 1)의 보상 배율.</summary>
        private static int StageReward(int reachedStage) =>
            Mathf.RoundToInt(StageRewardBase * StageScaling.RewardMultiplier(Mathf.Max(1, reachedStage - 1)));

        private static int KillReward => Mathf.RoundToInt(KillRewardBase * StageScaling.RewardMultiplier(StageProgress.CurrentStage));

        /// <summary>받을 수 있는 보상을 전부 받고 받은 골드 합계를 돌려준다.</summary>
        public static int ClaimAll()
        {
            EnsureLoaded();
            var total = 0;
            for (var s = _stageClaimed + 1; s <= StageProgress.CurrentStage; s++)
                total += StageReward(s);
            _stageClaimed = Mathf.Max(_stageClaimed, StageProgress.CurrentStage);

            var killRewards = PendingKillRewards;
            total += killRewards * KillReward;
            _killClaimed += killRewards;

            PlayerPrefs.SetInt(StageClaimedKey, _stageClaimed);
            PlayerPrefs.SetInt(KillClaimedKey, _killClaimed);
            GoldWallet.Add(total); // 안에서 PlayerPrefs.Save
            return total;
        }
    }
}
