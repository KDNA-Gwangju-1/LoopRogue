using UnityEngine;

namespace LoopRogue
{
    /// <summary>영구 재화(골드) - 루프가 리셋되거나 씬을 옮겨도 안 사라진다("적 처치/방 클리어마다
    /// 조금씩 벌고, 루프 돌아도 안 사라짐" 요청). 지금은 정수 하나뿐이라 StoryRPG PrimaryStats처럼
    /// 별도 JSON 파일 대신 PlayerPrefs로 저장한다 - 나중에 영구 업그레이드 등으로 데이터가 늘어나면
    /// 그때 JSON 파일 방식으로 옮기면 된다.</summary>
    public static class GoldWallet
    {
        private const string PrefKey = "LoopRogue_Gold";

        private static int _gold;
        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static int Gold
        {
            get
            {
                EnsureLoaded();
                return _gold;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _gold = PlayerPrefs.GetInt(PrefKey, 0);
            _loaded = true;
        }

        public static void Add(int amount)
        {
            if (amount <= 0)
                return;

            EnsureLoaded();
            _gold += amount;
            PlayerPrefs.SetInt(PrefKey, _gold);
            PlayerPrefs.Save();
        }

        /// <summary>상점 등에서 나중에 쓸 차감 - 지금은 호출하는 곳이 없지만 미리 만들어둔다.</summary>
        public static bool TrySpend(int amount)
        {
            EnsureLoaded();
            if (amount <= 0 || _gold < amount)
                return false;

            _gold -= amount;
            PlayerPrefs.SetInt(PrefKey, _gold);
            PlayerPrefs.Save();
            return true;
        }
    }
}
