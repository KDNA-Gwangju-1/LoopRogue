using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>보스 러시(에디터 메뉴 "LoopRogue/보스 기믹 체험") - 기믹 보스만 시작 층(FirstStage)부터 10층까지 차례로 나오는 테스트 스테이지(10-14, 10-16에 10층까지·시작 층 선택).
    /// 방이 전부 보스방이고 각 방은 그 층 보스(체력·공격력·패턴·방 크기 그대로). 보스는 그 층 기믹이 시작하는 체력으로 나와 들어가자마자 기믹 컷신.
    /// 보스를 잡으면 체력을 다 채우고 출구가 열려 다음 보스, 10층 보스를 잡으면 끝. 플레이어 스펙은 보스방에 들어갈 때마다 그 층 기준 상태(봇 중간값)로 바꿔 끼운다(ApplyStageSpec, 10-15).
    /// 저장은 메뉴가 시작 전에 백업했다가 Play를 멈추면 되돌린다(테스트 맵과 같음).</summary>
    public static class BossRush
    {
        public static bool Active { get; private set; }

        public const int LastStage = 10;

        /// <summary>러시가 시작하는 층(1~10) - 메뉴 "보스 기믹 체험/N층부터".</summary>
        public static int FirstStage { get; private set; } = 1;

        /// <summary>트레일러 촬영 모드(10-26) - 튜토리얼 7방 → 1~10층 보스. 9층까지는 죽지 않고, 촬영용 키(TrailerControls)를 켠다.</summary>
        public static bool Trailer { get; private set; }

        /// <summary>트레일러 마지막 장면(10층 보스방) - 무적을 풀어 실제로 죽고 사망 연출(리플레이 → 되감기)로 끝난다(10-26 사용자).</summary>
        public static bool TrailerFinal { get; set; }

        /// <summary>지금 죽지 않게 지켜 줄지 - 트레일러이고 아직 마지막 장면이 아닐 때.</summary>
        public static bool TrailerInvincible => Trailer && !TrailerFinal;

        /// <summary>트레일러 튜토리얼을 이미 시작했는지 - 튜토리얼이 끝나 같은 씬을 다시 지을 때 보스 러시로 넘어가게.</summary>
        public static bool TrailerTutorialStarted { get; set; }

#if UNITY_EDITOR
        public const string PrefKey = "LoopRogue_BossRush";
        public const string FirstStagePrefKey = "LoopRogue_BossRush_First";
        public const string TrailerPrefKey = "LoopRogue_BossRush_Trailer";

        /// <summary>Play 시작 - 메뉴로 켰으면 바로 Main으로. 한 번 쓰면 꺼서 다음 Play는 평소처럼.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Active = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            UnityEditor.EditorPrefs.SetBool(PrefKey, false);
            FirstStage = Mathf.Clamp(UnityEditor.EditorPrefs.GetInt(FirstStagePrefKey, 1), 1, LastStage);
            Trailer = Active && UnityEditor.EditorPrefs.GetBool(TrailerPrefKey, false);
            TrailerTutorialStarted = false;
            TrailerFinal = false;
            UnityEditor.EditorPrefs.SetBool(TrailerPrefKey, false);
            if (!Active)
                return;
            if (Trailer)
                TrailerControls.Create();
            // 트레일러는 타이틀부터(10-26 사용자: 타이틀 → 튜토리얼 → 로비 → 던전 입장 → 1~10층 보스)
            var first = Trailer ? "Title" : "Main";
            Debug.Log($"[보스 러시] 시작 - {first}(으)로 바로 이동");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != first)
                UnityEngine.SceneManagement.SceneManager.LoadScene(first);
        }
#endif

        /// <summary>RoomController.FinishRoomCleared - 러시 보스를 잡았을 때(스테이지 클리어 대신). 다음 보스가 있으면 회복 + 출구.</summary>
        public static bool IsLast(RoomDefinition def) => def.BossStage >= LastStage;

        // ---- 층별 기준 스펙(10-15 사용자 "보스 러시도 층별 스탯에 맞게") ----
        // 보스방에 들어갈 때마다(죽고 다시 들어가도) 봇 기준 상태 BotLogs/reference_snapshots.tsv의 (N+1)층 입장 저장값 = N층을 막 깬 판의
        // 레벨·스탯·장비·영약·골드를 써넣고 플레이어를 그 값으로 다시 맞춘다("기준 스펙으로 N층 보스방" 메뉴와 같은 기준). 지난 보스에서 오른
        // 레벨은 버린다. 그 층 기준 상태에 없는 키는 지운다(앞 층 값이 남지 않게). 층·시도 횟수는 러시 것을 그대로 둔다.
        private static readonly string[] KeepKeys = { StageProgress.StageKey, StageProgress.AttemptsKey };

        public static void ApplyStageSpec(int bossStage, PlayerActor player)
        {
#if UNITY_EDITOR
            if (!Active || player == null)
                return;
            var snapshots = LoadSnapshots();
            var source = snapshots.ContainsKey(bossStage + 1) ? bossStage + 1 : bossStage;
            if (!snapshots.TryGetValue(source, out var snap))
            {
                Debug.LogWarning($"[보스 러시] {source}층 기준 상태가 없어 지금 스펙 그대로 - 자동 플레이 봇(100판)을 한 번 돌리면 생긴다");
                return;
            }
            foreach (var key in snapshots.Values.SelectMany(s => s.Keys).Distinct())
                if (!snap.ContainsKey(key) && System.Array.IndexOf(KeepKeys, key) < 0)
                    PlayerPrefs.DeleteKey(key);
            foreach (var pair in snap)
            {
                if (System.Array.IndexOf(KeepKeys, pair.Key) >= 0)
                    continue;
                if (pair.Value.IsFloat)
                    PlayerPrefs.SetFloat(pair.Key, float.Parse(pair.Value.Text, System.Globalization.CultureInfo.InvariantCulture));
                else
                    PlayerPrefs.SetInt(pair.Key, int.Parse(pair.Value.Text, System.Globalization.CultureInfo.InvariantCulture));
            }
            PlayerPrefs.Save();
            SaveReset.ReloadAll();
            player.ReloadStatsFromSave();
            Debug.Log($"[보스 러시] {bossStage}층 보스 - {source}층 입장 기준 상태(봇 중간값): Lv{RunProgress.Level} " +
                      $"공격력 {player.Stats.AttackPower:0} 최대HP {player.Stats.MaxHealth:0}");
#endif
        }

#if UNITY_EDITOR
        private static Dictionary<int, Dictionary<string, (bool IsFloat, string Text)>> LoadSnapshots()
        {
            var result = new Dictionary<int, Dictionary<string, (bool IsFloat, string Text)>>();
            var file = System.IO.Path.Combine(Application.dataPath, "..", "BotLogs", "reference_snapshots.tsv");
            if (!System.IO.File.Exists(file))
                return result;
            foreach (var line in System.IO.File.ReadAllLines(file))
            {
                var parts = line.Split('\t');
                if (parts.Length != 4 || !int.TryParse(parts[0], out var stage) || parts[1] == "-")
                    continue;
                if (!result.TryGetValue(stage, out var d))
                    result[stage] = d = new Dictionary<string, (bool IsFloat, string Text)>();
                d[parts[1]] = (parts[2] == "f", parts[3]);
            }
            return result;
        }
#endif
    }
}
