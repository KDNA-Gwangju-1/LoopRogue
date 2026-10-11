using UnityEngine;

namespace LoopRogue
{
    /// <summary>퍼즐·미니게임 체험(에디터 메뉴 "LoopRogue/퍼즐·미니게임 체험/...") - Play를 시작하면 바로 Main으로 가서 지금 층 방 1 자리에
    /// 고른 퍼즐을 깐다(시작 방 고르기 없음). 난이도는 지금 저장의 층 기준. 저장은 메뉴가 시작 전에 백업했다가 Play를 멈추면 되돌린다
    /// (테스트 맵과 같은 백업).</summary>
    public static class PuzzleTrial
    {
        public static bool Active => Type.HasValue;
        public static PuzzleType? Type { get; private set; }

#if UNITY_EDITOR
        public const string PrefKey = "LoopRogue_PuzzleTrial"; // 퍼즐 번호, -1 = 꺼짐

        /// <summary>Play 시작(타이틀 씬) - 메뉴로 켰으면 바로 Main으로. 한 번 쓰면 꺼서 다음 Play는 평소처럼.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var index = UnityEditor.EditorPrefs.GetInt(PrefKey, -1);
            UnityEditor.EditorPrefs.SetInt(PrefKey, -1);
            Type = index >= 0 && index < PuzzleBase.TypeCount ? (PuzzleType)index : (PuzzleType?)null;
            if (!Active)
                return;
            Debug.Log($"[퍼즐 체험] {Type} - Main으로 바로 이동");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
#endif
    }
}
