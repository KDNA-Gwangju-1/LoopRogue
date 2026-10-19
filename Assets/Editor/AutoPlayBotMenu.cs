using UnityEditor;

namespace LoopRogue.EditorTools
{
    /// <summary>자동 플레이 봇 메뉴 - "봇으로 시작"을 누르면 봇을 켜고 바로 Play에 들어간다. 봇은 끝나면
    /// 스스로 꺼지고 Play를 멈춘다(로그는 프로젝트 폴더 BotLogs/). 저장 데이터는 시작할 때 초기화된다.</summary>
    public static class AutoPlayBotMenu
    {
        [MenuItem("LoopRogue/자동 플레이 봇으로 시작 (저장 백업 후 초기화)")]
        private static void StartBot()
        {
            if (EditorApplication.isPlaying || !BackupBeforeBot())
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, "");
            EditorApplication.isPlaying = true;
        }

        /// <summary>층별 순수 난이도 측정 - 일반 모드 봇이 마지막으로 남긴 기준 상태(BotLogs/reference_snapshots.tsv)로
        /// 각 층만 반복한다. 기준 파일이 없으면 바로 멈춘다(일반 모드를 먼저 한 번 돌릴 것).</summary>
        /// <summary>보스 배율 찾기 - 층마다 보스 세기를 바꿔가며 재서, 목표 사망 수에 맞는 BossPatternBalance 값을 로그에 제안한다.</summary>
        [MenuItem("LoopRogue/보스 배율 찾기 (봇)")]
        private static void StartBossSweep()
        {
            if (EditorApplication.isPlaying || !BackupBeforeBot())
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.BossSweepMode);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("LoopRogue/층별 순수 난이도 측정 (봇)")]
        private static void StartStageTest()
        {
            if (EditorApplication.isPlaying || !BackupBeforeBot())
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.StageTestMode);
            EditorApplication.isPlaying = true;
        }

        // ---- 테스트 맵: 이벤트 11종을 다 깐 방 하나 + 지금 층 보스방 ----
        // 지금 저장 그대로 들어가고, 시작 전에 BotLogs/testmap_backup.reg로 백업했다가 Play를 멈추면 자동으로 되돌린다
        // (테스트 중 받은 골드·영약·도감 등이 진짜 저장에 남지 않게).
        private const string TestRestorePendingKey = "LoopRogue_TestMap_RestorePending";

        [MenuItem("LoopRogue/테스트 맵 (이벤트 전부, 끝나면 저장 되돌림)")]
        private static void StartTestMap()
        {
            if (EditorApplication.isPlaying)
            {
                // Play 중에 눌렀으면 멈추고 다시 시작(예전엔 아무 일도 안 일어나서 메뉴가 안 먹는 것처럼 보였다)
                EditorPrefs.SetBool(TestStartAfterStopKey, true);
                EditorApplication.isPlaying = false;
                return;
            }
            UnityEngine.PlayerPrefs.Save();
            if (RunReg($"export \"{PrefsRegistryKey}\" \"{TestBackupPath}\" /y"))
                EditorPrefs.SetBool(TestRestorePendingKey, true);
            else if (!EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 테스트 중 바뀐 저장이 그대로 남습니다. 그래도 시작할까요?", "시작", "취소"))
                return;
            EditorPrefs.SetBool(TestMap.PrefKey, true);
            EditorApplication.isPlaying = true;
        }

        private static string TestBackupPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BackupPath), "testmap_backup.reg");

        [InitializeOnLoadMethod]
        private static void HookTestMapRestore() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;
            if (EditorPrefs.GetBool(TestRestorePendingKey, false))
            {
                EditorPrefs.SetBool(TestRestorePendingKey, false);
                if (RestoreFrom(TestBackupPath))
                    UnityEngine.Debug.Log("[테스트 맵] 저장을 테스트 시작 전으로 되돌렸습니다.");
            }
            if (EditorPrefs.GetBool(TestStartAfterStopKey, false))
            {
                EditorPrefs.SetBool(TestStartAfterStopKey, false);
                EditorApplication.delayCall += StartTestMap;
            }
        };

        private const string TestStartAfterStopKey = "LoopRogue_TestMap_StartAfterStop";

        // ---- 퍼즐·미니게임 하나 골라 바로 체험 - 테스트 맵과 같은 저장 백업/되돌리기 ----
        private const string PuzzleMenu = "LoopRogue/퍼즐·미니게임 체험 (끝나면 저장 되돌림)/";
        [MenuItem(PuzzleMenu + "퍼즐 1. 얼음 미끄럼")] private static void Puzzle1() => StartPuzzleTrial(PuzzleType.IceSlide);
        [MenuItem(PuzzleMenu + "퍼즐 2. 함정 복도")] private static void Puzzle2() => StartPuzzleTrial(PuzzleType.TrapCorridor);
        [MenuItem(PuzzleMenu + "퍼즐 3. 룬 순서")] private static void Puzzle3() => StartPuzzleTrial(PuzzleType.RuneOrder);
        [MenuItem(PuzzleMenu + "퍼즐 4. 상자 밀기")] private static void Puzzle4() => StartPuzzleTrial(PuzzleType.BoxPush);
        [MenuItem(PuzzleMenu + "퍼즐 5. 전투 퍼즐")] private static void Puzzle5() => StartPuzzleTrial(PuzzleType.CombatChallenge);
        [MenuItem(PuzzleMenu + "미니게임 1. 상자 섞기")] private static void Mini1() => StartPuzzleTrial(PuzzleType.ShellGame);
        [MenuItem(PuzzleMenu + "미니게임 2. 두더지 잡기")] private static void Mini2() => StartPuzzleTrial(PuzzleType.WhackSlime);
        [MenuItem(PuzzleMenu + "미니게임 3. 황금 슬라임 추격")] private static void Mini3() => StartPuzzleTrial(PuzzleType.GoldenChase);
        [MenuItem(PuzzleMenu + "미니게임 4. 보물 찾기")] private static void Mini4() => StartPuzzleTrial(PuzzleType.TreasureSweep);
        [MenuItem(PuzzleMenu + "미니게임 5. 버티기")] private static void Mini5() => StartPuzzleTrial(PuzzleType.Survival);

        private const string PuzzleStartAfterStopKey = "LoopRogue_PuzzleTrial_StartAfterStop"; // 퍼즐 번호, -1 = 없음

        private static void StartPuzzleTrial(PuzzleType type)
        {
            if (EditorApplication.isPlaying)
            {
                // Play 중에 눌렀으면 멈추고(저장 되돌리기 포함) 다시 시작
                EditorPrefs.SetInt(PuzzleStartAfterStopKey, (int)type);
                EditorApplication.isPlaying = false;
                return;
            }
            UnityEngine.PlayerPrefs.Save();
            if (RunReg($"export \"{PrefsRegistryKey}\" \"{TestBackupPath}\" /y"))
                EditorPrefs.SetBool(TestRestorePendingKey, true);
            else if (!EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 체험 중 바뀐 저장이 그대로 남습니다. 그래도 시작할까요?", "시작", "취소"))
                return;
            EditorPrefs.SetInt(PuzzleTrial.PrefKey, (int)type);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void HookPuzzleTrialRestart() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;
            var pending = EditorPrefs.GetInt(PuzzleStartAfterStopKey, -1);
            if (pending < 0)
                return;
            EditorPrefs.SetInt(PuzzleStartAfterStopKey, -1);
            EditorApplication.delayCall += () => StartPuzzleTrial((PuzzleType)pending); // 저장 되돌리기(HookTestMapRestore) 뒤에
        };

        // ---- 벽화의 방 체험(10-19) - N층 방 1을 몹 없이 + 불 꺼진 제단 + 횃불(퀵슬롯 1번). 제단 쪽으로 횃불을 쓰면 그 층 벽화의 방.
        // 테스트 맵과 같은 저장 백업/되돌리기 - 층은 저장의 현재 층을 바꿔 정한다(Play를 멈추면 되돌아간다).
        private const string MuralMenu = "LoopRogue/벽화의 방 체험 (끝나면 저장 되돌림)/";
        [MenuItem(MuralMenu + "1층")] private static void Mural1() => StartMuralTrial(1);
        [MenuItem(MuralMenu + "2층")] private static void Mural2() => StartMuralTrial(2);
        [MenuItem(MuralMenu + "3층")] private static void Mural3() => StartMuralTrial(3);
        [MenuItem(MuralMenu + "4층")] private static void Mural4() => StartMuralTrial(4);
        [MenuItem(MuralMenu + "5층")] private static void Mural5() => StartMuralTrial(5);
        [MenuItem(MuralMenu + "6층")] private static void Mural6() => StartMuralTrial(6);
        [MenuItem(MuralMenu + "7층")] private static void Mural7() => StartMuralTrial(7);
        [MenuItem(MuralMenu + "8층")] private static void Mural8() => StartMuralTrial(8);
        [MenuItem(MuralMenu + "9층")] private static void Mural9() => StartMuralTrial(9);
        [MenuItem(MuralMenu + "10층")] private static void Mural10() => StartMuralTrial(10);

        private static void StartMuralTrial(int stage)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("벽화의 방 체험", "Play를 멈춘 뒤 다시 골라 주세요(멈추면 저장이 되돌아갑니다).", "확인");
                return;
            }
            UnityEngine.PlayerPrefs.Save();
            if (RunReg($"export \"{PrefsRegistryKey}\" \"{TestBackupPath}\" /y"))
                EditorPrefs.SetBool(TestRestorePendingKey, true);
            else if (!EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 체험 중 바뀐 저장이 그대로 남습니다. 그래도 시작할까요?", "시작", "취소"))
                return;
            UnityEngine.PlayerPrefs.SetInt("LoopRogue_CurrentStage", stage);
            UnityEngine.PlayerPrefs.SetInt(LoopRogue.TutorialStage.DoneKey, 1); // 튜토리얼로 새지 않게
            UnityEngine.PlayerPrefs.Save();
            EditorPrefs.SetBool(LoopRogue.MuralTrial.PrefKey, true);
            EditorApplication.isPlaying = true;
        }

        // ---- N층 보스방 앞까지 봇이 진행 → 사람에게 인계 ----
        // 새 저장으로 시작하므로(봇 공통) 시작 전에 지금 저장을 BotLogs/save_backup.reg로 백업한다 - "봇 전 저장 되돌리기"로 복구.
        private const string HandoffMenu = "LoopRogue/봇으로 N층 보스방 앞까지 (저장 백업 후 초기화)/";
        [MenuItem(HandoffMenu + "1층")] private static void Handoff1() => StartHandoff(1);
        [MenuItem(HandoffMenu + "2층")] private static void Handoff2() => StartHandoff(2);
        [MenuItem(HandoffMenu + "3층")] private static void Handoff3() => StartHandoff(3);
        [MenuItem(HandoffMenu + "4층")] private static void Handoff4() => StartHandoff(4);
        [MenuItem(HandoffMenu + "5층")] private static void Handoff5() => StartHandoff(5);
        [MenuItem(HandoffMenu + "6층")] private static void Handoff6() => StartHandoff(6);
        [MenuItem(HandoffMenu + "7층")] private static void Handoff7() => StartHandoff(7);
        [MenuItem(HandoffMenu + "8층")] private static void Handoff8() => StartHandoff(8);
        [MenuItem(HandoffMenu + "9층")] private static void Handoff9() => StartHandoff(9);
        [MenuItem(HandoffMenu + "10층")] private static void Handoff10() => StartHandoff(10);

        // ---- 봇 기준 상태(reference_snapshots.tsv)로 N층 보스방에 바로 들어가기 - N층을 막 깬 판(N+1층 입장 시점)의 저장값을 그대로 덮어쓴다
        // (레벨·스탯·장비·영약·유물·골드). 시작 전에 지금 저장을 백업 - "봇 전 저장 되돌리기"로 복구.
        private const string BossTestMenu = "LoopRogue/기준 스펙으로 N층 보스방 (저장 백업 후 덮어쓰기)/";
        [MenuItem(BossTestMenu + "1층")] private static void BossTest1() => StartBossTest(1);
        [MenuItem(BossTestMenu + "2층")] private static void BossTest2() => StartBossTest(2);
        [MenuItem(BossTestMenu + "3층")] private static void BossTest3() => StartBossTest(3);
        [MenuItem(BossTestMenu + "4층")] private static void BossTest4() => StartBossTest(4);
        [MenuItem(BossTestMenu + "5층")] private static void BossTest5() => StartBossTest(5);
        [MenuItem(BossTestMenu + "6층")] private static void BossTest6() => StartBossTest(6);
        [MenuItem(BossTestMenu + "7층")] private static void BossTest7() => StartBossTest(7);
        [MenuItem(BossTestMenu + "8층")] private static void BossTest8() => StartBossTest(8);
        [MenuItem(BossTestMenu + "9층")] private static void BossTest9() => StartBossTest(9);
        [MenuItem(BossTestMenu + "10층")] private static void BossTest10() => StartBossTest(10);

        private static void StartBossTest(int stage)
        {
            if (EditorApplication.isPlaying)
                return;
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BackupPath), "reference_snapshots.tsv");
            if (!System.IO.File.Exists(file))
            {
                EditorUtility.DisplayDialog("기준 상태 없음", "BotLogs/reference_snapshots.tsv가 없습니다. 자동 플레이 봇(100판)을 먼저 한 번 돌리세요.", "확인");
                return;
            }
            // N층을 막 깬 상태 = N+1층 입장 시점(10층은 그다음이 없어서 10층 입장 시점)
            var snapshots = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string[]>>();
            foreach (var line in System.IO.File.ReadAllLines(file))
            {
                var parts = line.Split('	');
                if (parts.Length != 4 || !int.TryParse(parts[0], out var s))
                    continue;
                if (!snapshots.TryGetValue(s, out var list))
                    snapshots[s] = list = new System.Collections.Generic.List<string[]>();
                if (parts[1] != "-")
                    list.Add(parts);
            }
            var source = snapshots.ContainsKey(stage + 1) ? stage + 1 : stage;
            if (!snapshots.ContainsKey(source))
            {
                EditorUtility.DisplayDialog("기준 상태 없음", $"{source}층 입장 기준 상태가 없습니다.", "확인");
                return;
            }
            if (!BackupBeforeBot())
                return;
            LoopRogue.GameSettings.Preserve(() => UnityEngine.PlayerPrefs.DeleteAll());
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var p in snapshots[source])
            {
                if (p[2] == "f")
                    UnityEngine.PlayerPrefs.SetFloat(p[1], float.Parse(p[3], inv));
                else
                    UnityEngine.PlayerPrefs.SetInt(p[1], int.Parse(p[3], inv));
            }
            UnityEngine.PlayerPrefs.SetInt("LoopRogue_CurrentStage", stage);
            UnityEngine.PlayerPrefs.SetInt("LoopRogue_StageAttempts", 0);
            UnityEngine.PlayerPrefs.SetInt(LoopRogue.TutorialStage.DoneKey, 1); // 튜토리얼로 새지 않게
            UnityEngine.PlayerPrefs.Save();
            EditorPrefs.SetBool(LoopRogue.LoopManager.BossDirectPrefKey, true);
            UnityEngine.Debug.Log($"[테스트] {source}층 입장 기준 상태로 {stage}층 보스방 시작 - 끝나면 '봇 전 저장 되돌리기'로 원래 저장 복구");
            EditorSceneManagerOpenMain();
            EditorApplication.isPlaying = true;
        }

        // ---- 보스 러시(보스 기믹 체험) - 시작 층부터 10층까지 기믹 보스만 차례로(10-16 사용자 "모든 층 보스 기믹만 체험").
        // 보스는 그 층 기믹 체력으로 나와 들어가자마자 기믹 컷신. 시작 저장은 10층 입장 기준 상태(봇 중간값)지만, 보스방마다 BossRush.ApplyStageSpec이
        // 그 층 기준 스펙으로 바꿔 끼운다(10-15) - 아래 배율은 첫 방에 들어가기 전 잠깐만 쓰인다.
        // 테스트 맵처럼 시작 전에 저장을 백업했다가 Play를 멈추면 되돌린다.
        private const float BossRushAttackBoost = 1.5f;
        private const float BossRushHealthBoost = 2f;

        private const string RushMenu = "LoopRogue/보스 기믹 체험 (N층부터 10층까지, 끝나면 저장 되돌림)/";
        [MenuItem(RushMenu + "1층부터")] private static void Rush1() => StartBossRush(1);
        [MenuItem(RushMenu + "2층부터")] private static void Rush2() => StartBossRush(2);
        [MenuItem(RushMenu + "3층부터")] private static void Rush3() => StartBossRush(3);
        [MenuItem(RushMenu + "4층부터")] private static void Rush4() => StartBossRush(4);
        [MenuItem(RushMenu + "5층부터")] private static void Rush5() => StartBossRush(5);
        [MenuItem(RushMenu + "6층부터")] private static void Rush6() => StartBossRush(6);
        [MenuItem(RushMenu + "7층부터")] private static void Rush7() => StartBossRush(7);
        [MenuItem(RushMenu + "8층부터")] private static void Rush8() => StartBossRush(8);
        [MenuItem(RushMenu + "9층부터")] private static void Rush9() => StartBossRush(9);
        [MenuItem(RushMenu + "10층만")] private static void Rush10() => StartBossRush(10);

        private static void StartBossRush(int firstStage)
        {
            if (EditorApplication.isPlaying)
                return;
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BackupPath), "reference_snapshots.tsv");
            var snapshot = new System.Collections.Generic.List<string[]>();
            if (System.IO.File.Exists(file))
                foreach (var line in System.IO.File.ReadAllLines(file))
                {
                    var parts = line.Split('	');
                    if (parts.Length == 4 && parts[0] == "10" && parts[1] != "-")
                        snapshot.Add(parts);
                }
            if (snapshot.Count == 0)
            {
                EditorUtility.DisplayDialog("기준 상태 없음", "BotLogs/reference_snapshots.tsv에 10층 입장 기준 상태가 없습니다. 자동 플레이 봇(100판)을 먼저 한 번 돌리세요.", "확인");
                return;
            }
            UnityEngine.PlayerPrefs.Save();
            if (RunReg($"export \"{PrefsRegistryKey}\" \"{TestBackupPath}\" /y"))
                EditorPrefs.SetBool(TestRestorePendingKey, true);
            else if (!EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 보스 러시 중 바뀐 저장이 그대로 남습니다. 그래도 시작할까요?", "시작", "취소"))
                return;
            LoopRogue.GameSettings.Preserve(() => UnityEngine.PlayerPrefs.DeleteAll());
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var p in snapshot)
            {
                if (p[2] != "f")
                {
                    UnityEngine.PlayerPrefs.SetInt(p[1], int.Parse(p[3], inv));
                    continue;
                }
                var v = float.Parse(p[3], inv);
                if (p[1] == "LoopRogue_Run_AttackPower")
                    v *= BossRushAttackBoost;
                else if (p[1] == "LoopRogue_Run_MaxHealth" || p[1] == "LoopRogue_Run_CurrentHealth")
                    v *= BossRushHealthBoost;
                UnityEngine.PlayerPrefs.SetFloat(p[1], v);
            }
            UnityEngine.PlayerPrefs.SetInt("LoopRogue_CurrentStage", LoopRogue.BossRush.LastStage);
            UnityEngine.PlayerPrefs.SetInt("LoopRogue_StageAttempts", 0);
            UnityEngine.PlayerPrefs.SetInt(LoopRogue.TutorialStage.DoneKey, 1);
            UnityEngine.PlayerPrefs.Save();
            EditorPrefs.SetBool(LoopRogue.BossRush.PrefKey, true);
            EditorPrefs.SetInt(LoopRogue.BossRush.FirstStagePrefKey, firstStage);
            UnityEngine.Debug.Log($"[보스 기믹 체험] {firstStage}~{LoopRogue.BossRush.LastStage}층 보스 연속(기믹 체력으로 시작) - 보스방마다 그 층 기준 스펙(봇 중간값)으로. Play를 멈추면 저장 되돌림");
            EditorSceneManagerOpenMain();
            EditorApplication.isPlaying = true;
        }

        /// <summary>Main 씬에서 바로 시작(타이틀·로비를 거치지 않게).</summary>
        private static void EditorSceneManagerOpenMain()
        {
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        }

        private static void StartHandoff(int stage)
        {
            if (EditorApplication.isPlaying || !BackupBeforeBot())
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.HandoffMode);
            EditorPrefs.SetInt(AutoPlayBot.HandoffStagePrefKey, stage);
            EditorApplication.isPlaying = true;
        }

        /// <summary>봇은 저장을 지우고 시작하므로 그 전에 진짜 저장을 BotLogs/save_backup.reg로 백업한다. 아직 되돌리지 않은 백업이 있으면
        /// 덮어쓰지 않는다 - 예전엔 봇을 두 번 돌리면 두 번째 백업이 봇 저장을 담아 진짜 저장이 사라졌다. 시작해도 되면 true.</summary>
        private static bool BackupBeforeBot()
        {
            if (EditorPrefs.GetBool(BotBackupPendingKey, false) && System.IO.File.Exists(BackupPath))
                return true; // 이미 진짜 저장이 백업돼 있다("봇 전 저장 되돌리기"를 하기 전까지 그대로 둔다)
            UnityEngine.PlayerPrefs.Save(); // 메모리에만 있는 값까지 레지스트리에 쓴 뒤 백업
            if (RunReg($"export \"{PrefsRegistryKey}\" \"{BackupPath}\" /y"))
            {
                EditorPrefs.SetBool(BotBackupPendingKey, true);
                return true;
            }
            return EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 그래도 저장을 초기화하고 봇을 시작할까요?", "시작", "취소");
        }

        private const string BotBackupPendingKey = "LoopRogue_BotBackupPending";

        /// <summary>Play를 멈추면 봇 켜짐 표시를 항상 지운다 - 예전엔 봇을 중간에 멈추면 표시가 남아, 다음 평범한 Play에서 봇이 다시 켜져
        /// 저장을 지웠다. 테스트 맵 도중 에디터가 꺼졌다면(되돌리기가 남아 있으면) 에디터가 다시 켜질 때 바로 되돌린다
        /// (예전엔 며칠 뒤 아무 Play를 멈출 때 옛 백업으로 되돌아갔다).</summary>
        [InitializeOnLoadMethod]
        private static void HookSafety()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingPlayMode)
                    return;
                EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, false);
                EditorPrefs.SetString(AutoPlayBot.ModePrefKey, "");
            };
            if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorPrefs.GetBool(TestRestorePendingKey, false))
            {
                EditorPrefs.SetBool(TestRestorePendingKey, false);
                if (RestoreFrom(TestBackupPath))
                    UnityEngine.Debug.Log("[테스트 맵] 지난번 테스트가 끝나기 전에 에디터가 꺼져서, 저장을 테스트 시작 전으로 되돌렸습니다.");
            }
        }

        /// <summary>백업 파일로 저장을 되돌린다 - 파일이 없거나 비정상이면 지우지도 않는다(예전엔 지운 뒤 가져오기가 실패하면 저장이 비었다).</summary>
        private static bool RestoreFrom(string path)
        {
            if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length < 64)
                return false;
            RunReg($"delete \"{PrefsRegistryKey}\" /f");
            return RunReg($"import \"{path}\"");
        }

        /// <summary>에디터 PlayerPrefs는 레지스트리(HKCU\Software\Unity\UnityEditor\회사\제품)에 있다 - 통째로 .reg로 내보낸다.</summary>
        private static string PrefsRegistryKey =>
            $@"HKCU\Software\Unity\UnityEditor\{PlayerSettings.companyName}\{PlayerSettings.productName}";

        private static string BackupPath
        {
            get
            {
                var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "BotLogs"));
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "save_backup.reg");
            }
        }

        [MenuItem("LoopRogue/봇 전 저장 되돌리기")]
        private static void RestoreSave()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("저장 되돌리기", "Play를 멈춘 뒤에 해주세요.", "확인");
                return;
            }
            if (!System.IO.File.Exists(BackupPath))
            {
                EditorUtility.DisplayDialog("저장 되돌리기", "백업 파일이 없습니다.\n" + BackupPath, "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("저장 되돌리기", "지금 저장을 지우고 봇 시작 전 저장으로 되돌릴까요?", "되돌리기", "취소"))
                return;
            var ok = RestoreFrom(BackupPath);
            if (ok)
                EditorPrefs.SetBool(BotBackupPendingKey, false); // 진짜 저장으로 돌아왔다 - 다음 봇은 새로 백업
            EditorUtility.DisplayDialog("저장 되돌리기", ok ? "되돌렸습니다." : "되돌리기 실패 - 콘솔 로그를 확인하세요.", "확인");
        }

        private static bool RunReg(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        UnityEngine.Debug.LogError($"[AutoPlayBot] reg {args} 실패: {err}");
                    return p.ExitCode == 0;
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[AutoPlayBot] reg 실행 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>모바일 화면 버튼을 에디터에서 켜고 끈다(마우스로 탭 대신 클릭해서 시험). 다음 Play부터 적용.</summary>
        [MenuItem("LoopRogue/모바일 터치 버튼 미리보기 (켜기·끄기)")]
        private static void ToggleTouchPreview()
        {
            var on = !EditorPrefs.GetBool(TouchControls.PreviewPrefKey, false);
            EditorPrefs.SetBool(TouchControls.PreviewPrefKey, on);
            EditorUtility.DisplayDialog("모바일 터치 버튼", on ? "켰습니다. 다음 Play부터 화면 버튼이 보입니다." : "껐습니다.", "확인");
        }

        [MenuItem("LoopRogue/자동 플레이 봇 강제 끄기")]
        private static void StopBot()
        {
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, false);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, "");
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }
    }
}
