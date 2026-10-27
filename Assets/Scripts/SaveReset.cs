using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>모든 저장 데이터(골드/스테이지/장비/영약/가챠 횟수/진행 중인 런) 초기화 - 타이틀의 "초기화"
    /// 버튼용. 이 게임은 PlayerPrefs에 LoopRogue 데이터만 쓰므로 DeleteAll로 한 번에 지우고, 각 저장
    /// 클래스가 메모리에 캐싱해둔 값도 다시 읽게 한다(안 그러면 같은 Play 세션 안에선 옛 값이 남는다).</summary>
    public static class SaveReset
    {
        /// <summary>게임을 끝까지 깬 횟수(엔딩을 본 횟수) - 처음으로 돌아가도 남는다.</summary>
        private const string ClearsKey = "LoopRogue_GameClears";

        public static int GameClears => PlayerPrefs.GetInt(ClearsKey, 0);

        private const string PlayTimeKey = "LoopRogue_PlayTimeSeconds";

        // 10-27 사용자: 업적·칭호·사망 수는 회차마다 따로 - 엔딩 뒤 새 회차에서 다시 깨면 보상도 다시 준다. 스팀 업적은 한 번 열리면
        // 스팀에 남지만 게임 쪽으로 불러오지는 않는다(SteamBridge.SyncAchievements는 올리기만). 업적 진행(상인 구매·누적 처치·처치 보상
        // 기록)과 회차 대사 기록도 같이 지운다. 도감만 회차를 넘어 남는다.
        private static readonly string[] KeptStrings = { "LoopRogue_Codex" };
        private static readonly string[] KeptInts =
        {
            ClearsKey, LoopRecord.LifetimeDeathsKey, // 노인의 이야기 조각은 회차를 넘어
            // 마지막 업적(10층 클리어) 보상 이용권이 엔딩 직후 지워지던 문제 - 이용권은 남긴다.
            "LoopRogue_GachaTickets", "LoopRogue_SlotTickets",
            Tutorial.BasicsKey, Tutorial.HintsKey, TutorialStage.DoneKey, TutorialStage.RewardKey, // 첫 판 안내는 한 번 봤으면 엔딩 뒤에도 다시 안 띄운다
        };

        public static void ResetAll()
        {
            GameSettings.Preserve(() => PlayerPrefs.DeleteAll()); // 설정(볼륨·화면)은 남긴다
            ReloadAll();
        }

        /// <summary>10층 보스를 잡고 엔딩을 본 뒤 - 클리어 횟수를 하나 올리고, 도감·이용권(+엔딩에서 고른 유물 하나)만 남기고 초기화한다.</summary>
        public static void ResetForNewGame(RelicType? carried = null)
        {
            PlayerPrefs.SetInt(ClearsKey, GameClears + 1);
            var strings = new Dictionary<string, string>();
            var ints = new Dictionary<string, int>();
            foreach (var key in KeptStrings)
                if (PlayerPrefs.HasKey(key))
                    strings[key] = PlayerPrefs.GetString(key);
            foreach (var key in KeptInts)
                if (PlayerPrefs.HasKey(key))
                    ints[key] = PlayerPrefs.GetInt(key);

            var playTime = PlayerPrefs.GetFloat(PlayTimeKey, 0f); // 누적 플레이 시간(테스트 기록용)도 엔딩 뒤에 남긴다
            GameSettings.Preserve(() => PlayerPrefs.DeleteAll());
            PlayerPrefs.SetFloat(PlayTimeKey, playTime);
            foreach (var pair in strings)
                PlayerPrefs.SetString(pair.Key, pair.Value);
            foreach (var pair in ints)
                PlayerPrefs.SetInt(pair.Key, pair.Value);
            PlayerPrefs.Save();
            ReloadAll();
            if (carried.HasValue)
                Relics.KeepOnly(carried.Value); // 엔딩에서 고른 유물 하나는 다음 회차로
        }

        // ---- 새 빌드면 저장 초기화(10-27 사용자 "빌드 돌릴 때 자동으로 초기화") ----
        /// <summary>빌드할 때 에디터(BuildMenu.BuildStampWriter)가 Resources/BuildStamp.txt에 빌드 시각을 적는다. 실행 파일이 처음 켜질 때
        /// 저장된 시각과 다르면(새 빌드) 설정만 남기고 전부 지운다 - 테스트 빌드마다 예전 빌드의 저장이 섞이지 않게. 에디터 플레이는 건드리지 않는다.</summary>
        private const string BuildStampKey = "LoopRogue_BuildStamp";

        /// <summary>이 빌드를 만든 시각(Resources/BuildStamp.txt) - 에디터나 스탬프가 없으면 null.</summary>
        public static string BuildStamp
        {
            get
            {
                var stamp = Resources.Load<TextAsset>("BuildStamp");
                return stamp != null ? stamp.text.Trim() : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnNewBuild()
        {
            if (Application.isEditor)
                return;
            var stamp = BuildStamp;
            if (string.IsNullOrEmpty(stamp) || PlayerPrefs.GetString(BuildStampKey, "") == stamp)
                return;
            ResetAll();
            PlayerPrefs.SetString(BuildStampKey, stamp);
            PlayerPrefs.Save();
            Debug.Log($"[SaveReset] 새 빌드({stamp}) - 저장 초기화");
        }

        /// <summary>PlayerPrefs를 직접 바꾼 뒤 각 저장 클래스가 다시 읽게 한다(보스 러시가 층마다 기준 스펙을 써넣을 때도).</summary>
        public static void ReloadAll()
        {
            GoldWallet.Reload();
            EquipmentWallet.Reload();
            StatPotionWallet.Reload();
            StageProgress.Reload();
            GachaSystem.Reload();
            Inventory.Reload();
            Relics.Reload();
            LobbyQuests.Reload();
            RunQuest.Reload();
            LoopRecord.Reload();
            Codex.Reload();
            Achievements.Reload(); // 칭호 효과가 RunProgress 기본값에 들어가므로 그보다 먼저
            SlotMachine.Reload();
            MemoryKnight.Reload();
            RunProgress.Reload(); // 기본값 계산에 위 지갑들을 쓰므로 마지막에.
        }
    }
}
