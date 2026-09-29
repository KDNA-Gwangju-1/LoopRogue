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
            EditorApplication.isPlaying = true;
        }

        [MenuItem("LoopRogue/자동 플레이 봇 강제 끄기")]
        private static void StopBot()
        {
            EditorPrefs.SetBool(AutoPlayBot.EnabledPrefKey, false);
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }
    }
}
