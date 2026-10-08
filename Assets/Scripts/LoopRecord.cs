using UnityEngine;

namespace LoopRogue
{
    /// <summary>지금까지 죽은 횟수(= 고리를 다시 돈 횟수) - 영구 저장. LoopManager.LoopCount는 Main 씬마다 1부터 다시 세서
    /// NPC 대사(LobbyGuide, NpcActor)가 쓸 수 없어 따로 둔다. 회차 구간(Tier)마다 대사가 바뀌고, 몇몇 회차(Milestones)에는
    /// 시간지기 노인이 한 번만 하는 특별한 말이 있다.</summary>
    public static class LoopRecord
    {
        private const string DeathsKey = "LoopRogue_TotalDeaths";
        private const string MilestoneSeenKey = "LoopRogue_LoopMilestoneSeen";

        /// <summary>노인의 특별한 말이 나오는 누적 사망 수.</summary>
        public static readonly int[] Milestones = { 1, 10, 30, 50, 100, 200 };

        /// <summary>대사 구간 경계 - 0번 / 1~4번 / 5~19번 / 20~49번 / 50~99번 / 100번 이상.</summary>
        private static readonly int[] TierStarts = { 0, 1, 5, 20, 50, 100 };

        private static bool _loaded;
        private static int _deaths;
        private static int _milestoneSeen;

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
            _deaths = PlayerPrefs.GetInt(DeathsKey, 0);
            _milestoneSeen = PlayerPrefs.GetInt(MilestoneSeenKey, 0);
        }

        public static int TotalDeaths
        {
            get
            {
                EnsureLoaded();
                return _deaths;
            }
        }

        /// <summary>0~5 - TierStarts 구간 번호.</summary>
        public static int Tier
        {
            get
            {
                var tier = 0;
                for (var i = 0; i < TierStarts.Length; i++)
                    if (TotalDeaths >= TierStarts[i])
                        tier = i;
                return tier;
            }
        }

        /// <summary>죽을 때마다(LoopManager.OnPlayerDied).</summary>
        public static void AddDeath()
        {
            EnsureLoaded();
            _deaths++;
            PlayerPrefs.SetInt(DeathsKey, _deaths);
            PlayerPrefs.Save();
            Achievements.Check(); // 사망 횟수 업적
        }

        /// <summary>도달했지만 아직 노인에게 못 들은 가장 큰 회차 - 없으면 null. 건너뛴 회차(로비에 안 들르고 여러 번 죽음)는 큰 쪽 하나만.</summary>
        public static int? PendingMilestone
        {
            get
            {
                EnsureLoaded();
                int? pending = null;
                foreach (var m in Milestones)
                    if (m <= _deaths && m > _milestoneSeen)
                        pending = m;
                return pending;
            }
        }

        public static void MarkMilestoneSeen(int milestone)
        {
            EnsureLoaded();
            _milestoneSeen = Mathf.Max(_milestoneSeen, milestone);
            PlayerPrefs.SetInt(MilestoneSeenKey, _milestoneSeen);
            PlayerPrefs.Save();
        }
    }
}
