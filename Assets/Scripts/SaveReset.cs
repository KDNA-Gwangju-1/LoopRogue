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

        /// <summary>엔딩 후 처음으로 돌아갈 때 남기는 것(사용자 결정: 도감 + 업적·칭호) - 업적 진행용 누적 기록(누적 사망·상인 구매)과
        /// 노인의 누적 사망 대사 기록도 업적과 같이 남긴다. 나머지(골드·층·장비·영약·유물·아이템·런·퀘스트·이용권)는 전부 처음부터.</summary>
        private static readonly string[] KeptStrings = { "LoopRogue_Codex", "LoopRogue_Achievements", "LoopRogue_Title" };
        private static readonly string[] KeptInts = { "LoopRogue_MerchantBuys", "LoopRogue_TotalDeaths", "LoopRogue_LoopMilestoneSeen", ClearsKey };

        public static void ResetAll()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            ReloadAll();
        }

        /// <summary>10층 보스를 잡고 엔딩을 본 뒤 - 클리어 횟수를 하나 올리고, 도감·업적·칭호만 남기고 초기화한다.</summary>
        public static void ResetForNewGame()
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

            PlayerPrefs.DeleteAll();
            foreach (var pair in strings)
                PlayerPrefs.SetString(pair.Key, pair.Value);
            foreach (var pair in ints)
                PlayerPrefs.SetInt(pair.Key, pair.Value);
            PlayerPrefs.Save();
            ReloadAll();
        }

        private static void ReloadAll()
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
            RunProgress.Reload(); // 기본값 계산에 위 지갑들을 쓰므로 마지막에.
        }
    }
}
