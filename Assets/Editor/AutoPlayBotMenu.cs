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
        [MenuItem("LoopRogue/층별 순수 난이도 측정 (봇)")]
        private static void StartStageTest()
        {
            if (EditorApplication.isPlaying)
                return;
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, true);
            EditorPrefs.SetString(AutoPlayBot.ModePrefKey, AutoPlayBot.StageTestMode);
            EditorApplication.isPlaying = true;
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
