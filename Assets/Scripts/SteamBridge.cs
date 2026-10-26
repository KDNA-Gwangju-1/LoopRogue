using UnityEngine;
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace LoopRogue
{
    /// <summary>스팀 연동(Steamworks.NET) - 게임 시작 때 스스로 만들어져 씬이 바뀌어도 남는다. 스팀 API 초기화 → 매 프레임 콜백 →
    /// 업적 열기(Achievements.Unlock이 부른다) → 종료 때 정리. Steamworks.NET 패키지가 설치되면 STEAMWORKS_NET 정의가 자동으로 붙어서
    /// 아래 코드가 켜지고, 패키지가 없거나 스팀이 안 켜져 있으면 아무 일도 안 한다(게임은 그대로 동작).
    ///
    /// 출시 전 할 일: 1) 프로젝트 루트 steam_appid.txt의 480(밸브 테스트 앱 Spacewar)을 진짜 앱 ID로 바꾸고 AppId도 같이
    /// 2) Steamworks 파트너 사이트 → 업적에 Achievements.All의 SteamId(ACH_...)와 똑같은 API 이름으로 등록.
    /// 480으로는 우리 업적 이름이 없어서 SetAchievement가 실패하지만 무해하다(초기화/연결 확인용).</summary>
    public class SteamBridge : MonoBehaviour
    {
        /// <summary>스팀 앱 ID - 출시 빌드에서 스팀 밖으로 실행하면 스팀으로 다시 띄운다(RestartAppIfNecessary). 480 = 테스트용이라 재시작 안 함.</summary>
        public const uint AppId = 480;

        public static bool Initialized { get; private set; }

        private static SteamBridge _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (_instance != null)
                return;
            var go = new GameObject("SteamBridge");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SteamBridge>();
        }

        private void Awake()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogWarning("[SteamBridge] Steamworks.NET 바이너리가 이 플랫폼과 맞지 않습니다 - 스팀 연동 없이 진행");
                return;
            }
            try
            {
#if !UNITY_EDITOR
                if (AppId != 480 && SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
                {
                    Application.Quit(); // 스팀이 게임을 다시 띄운다
                    return;
                }
#endif
                var result = SteamAPI.InitEx(out var error);
                Initialized = result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                if (!Initialized)
                {
                    Debug.Log($"[SteamBridge] 스팀 초기화 안 됨({result}: {error}) - 스팀 클라이언트가 켜져 있는지 확인. 업적은 게임 안에만 기록됩니다.");
                    return;
                }
                Debug.Log($"[SteamBridge] 스팀 연결됨 - {SteamFriends.GetPersonaName()}");
                SyncAchievements();
            }
            catch (System.DllNotFoundException e)
            {
                Debug.LogWarning($"[SteamBridge] steam_api 라이브러리를 못 찾음 - 스팀 연동 없이 진행 ({e.Message})");
            }
#endif
        }

        private void Update()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (Initialized)
                SteamAPI.RunCallbacks();
#endif
        }

        private void OnApplicationQuit()
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (Initialized)
            {
                SteamAPI.Shutdown();
                Initialized = false;
            }
#endif
        }

        /// <summary>업적 열기(Achievements.Unlock이 부른다). 자동 플레이 봇 중엔 개발자 계정에 업적이 열리면 안 되므로 건너뛴다.</summary>
        public static void UnlockAchievement(string apiName)
        {
#if STEAMWORKS_NET && !DISABLESTEAMWORKS
            if (!Initialized || GameHUD.AutoPlayActive)
                return;
            if (SteamUserStats.SetAchievement(apiName))
                SteamUserStats.StoreStats();
#endif
        }

#if STEAMWORKS_NET && !DISABLESTEAMWORKS
        /// <summary>연결 직후 한 번 - 오프라인으로 달성해 둔 업적을 스팀에 올린다(한 방향만). 게임 안 업적은 저장마다 따로라(10-26 사용자 -
        /// 저장을 초기화하면 게임 안 업적은 처음부터, 스팀에는 한 번 연 업적이 그대로 남는다) 스팀에만 있는 업적을 게임 쪽에 되살리지 않는다.
        /// 그래서 이미 지나간 층의 보스 무피격처럼 놓친 업적은 새로 시작해서 다시 노릴 수 있다.</summary>
        private static void SyncAchievements()
        {
            var changed = false;
            foreach (var def in Achievements.All)
            {
                if (!SteamUserStats.GetAchievement(def.SteamId, out var achieved))
                    continue; // 스팀에 등록 안 된 이름(테스트 앱 480 등)
                if (!achieved && Achievements.IsUnlocked(def))
                    changed |= SteamUserStats.SetAchievement(def.SteamId);
            }
            if (changed)
                SteamUserStats.StoreStats();
        }
#endif
    }
}
