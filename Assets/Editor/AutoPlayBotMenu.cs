using UnityEditor;

namespace LoopRogue.EditorTools
{
    /// <summary>자동 플레이 봇 메뉴 - "봇으로 시작"을 누르면 봇을 켜고 바로 Play에 들어간다. 봇은 끝나면
    /// 스스로 꺼지고 Play를 멈춘다(로그는 프로젝트 폴더 BotLogs/). 저장 데이터는 시작할 때 초기화된다.</summary>
    public static class AutoPlayBotMenu
    {
        [MenuItem("LoopRogue/자동 플레이 봇으로 시작 (저장 초기화됨)")]
        private static void StartBot()
        {
            if (EditorApplication.isPlaying)
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
            if (EditorApplication.isPlaying)
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.BossSweepMode);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("LoopRogue/층별 순수 난이도 측정 (봇)")]
        private static void StartStageTest()
        {
            if (EditorApplication.isPlaying)
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
                RunReg($"delete \"{PrefsRegistryKey}\" /f");
                if (RunReg($"import \"{TestBackupPath}\""))
                    UnityEngine.Debug.Log("[테스트 맵] 저장을 테스트 시작 전으로 되돌렸습니다.");
            }
            if (EditorPrefs.GetBool(TestStartAfterStopKey, false))
            {
                EditorPrefs.SetBool(TestStartAfterStopKey, false);
                EditorApplication.delayCall += StartTestMap;
            }
        };

        private const string TestStartAfterStopKey = "LoopRogue_TestMap_StartAfterStop";

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

        private static void StartHandoff(int stage)
        {
            if (EditorApplication.isPlaying)
                return;
            UnityEngine.PlayerPrefs.Save(); // 메모리에만 있는 값까지 레지스트리에 쓴 뒤 백업
            if (!RunReg($"export \"{PrefsRegistryKey}\" \"{BackupPath}\" /y")
                && !EditorUtility.DisplayDialog("저장 백업 실패", "지금 저장을 백업하지 못했습니다. 그래도 저장을 초기화하고 봇을 시작할까요?", "시작", "취소"))
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.HandoffMode);
            EditorPrefs.SetInt(AutoPlayBot.HandoffStagePrefKey, stage);
            EditorApplication.isPlaying = true;
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
            RunReg($"delete \"{PrefsRegistryKey}\" /f");
            var ok = RunReg($"import \"{BackupPath}\"");
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
