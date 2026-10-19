using UnityEngine;

namespace LoopRogue
{
    /// <summary>벽화의 방 체험(에디터 메뉴 "LoopRogue/벽화의 방 체험/N층", 10-19 사용자) - Play를 시작하면 바로 Main으로 가서 N층 방 1을
    /// 몹 없이(이미 비운 채) 깔고, 불 꺼진 제단을 세우고 횃불을 퀵슬롯 1번에 넣어 준다. 제단 쪽으로 횃불을 쓰면 그 층 벽화의 방.
    /// 층은 메뉴가 저장의 현재 층을 바꿔 정하고, 저장은 시작 전에 백업했다가 Play를 멈추면 되돌린다(테스트 맵과 같은 백업).</summary>
    public static class MuralTrial
    {
        public static bool Active { get; private set; }

#if UNITY_EDITOR
        public const string PrefKey = "LoopRogue_MuralTrial"; // 1 = 켜짐(층은 저장의 현재 층)

        /// <summary>Play 시작(타이틀 씬) - 메뉴로 켰으면 바로 Main으로. 한 번 쓰면 꺼서 다음 Play는 평소처럼.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            UnityEditor.EditorPrefs.SetBool(PrefKey, false);
            if (!Active)
                return;
            Debug.Log($"[벽화의 방 체험] {StageProgress.CurrentStage}층 - Main으로 바로 이동");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
#endif

        /// <summary>횃불을 퀵슬롯 1번에(없으면 하나 준다).</summary>
        public static void GiveTorch()
        {
            Inventory.EnsureLoaded();
            if (!Inventory.Has(ItemType.Torch))
                Inventory.Give(ItemType.Torch);
            if (!Inventory.IsInQuickSlot(ItemType.Torch))
                Inventory.SetQuickSlot(0, ItemType.Torch);
        }
    }
}
