using UnityEngine;

namespace LoopRogue
{
    /// <summary>보스 러시(에디터 메뉴 "LoopRogue/보스 러시 1~9층") - 기믹 보스만 1층부터 9층까지 차례로 나오는 테스트 스테이지(10-14).
    /// 방 9개가 전부 보스방이고 각 방은 그 층 보스(체력·공격력·패턴·방 크기 그대로). 보스를 잡으면 체력을 다 채우고 출구가 열려 다음 보스,
    /// 9층 보스를 잡으면 끝. 플레이어는 메뉴가 10층 입장 기준 상태(봇 중간값)에 체력·공격력을 더 얹어 넣는다. 저장은 메뉴가 시작 전에 백업했다가
    /// Play를 멈추면 되돌린다(테스트 맵과 같음).</summary>
    public static class BossRush
    {
        public static bool Active { get; private set; }

        public const int LastStage = 9;

#if UNITY_EDITOR
        public const string PrefKey = "LoopRogue_BossRush";

        /// <summary>Play 시작 - 메뉴로 켰으면 바로 Main으로. 한 번 쓰면 꺼서 다음 Play는 평소처럼.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            UnityEditor.EditorPrefs.SetBool(PrefKey, false);
            if (!Active)
                return;
            Debug.Log("[보스 러시] 시작 - Main으로 바로 이동");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main")
                UnityEngine.SceneManagement.SceneManager.LoadScene("Main");
        }
#endif

        /// <summary>RoomController.FinishRoomCleared - 러시 보스를 잡았을 때(스테이지 클리어 대신). 다음 보스가 있으면 회복 + 출구.</summary>
        public static bool IsLast(RoomDefinition def) => def.BossStage >= LastStage;
    }
}
