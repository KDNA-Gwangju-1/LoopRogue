using UnityEditor;
using UnityEditor.SceneManagement;

namespace LoopRogue.EditorTools
{
    /// <summary>에디터에서 Play를 누르면 지금 열린 씬이 아니라 항상 빌드 0번 씬(Title)부터 시작하게 한다 -
    /// "게임 시작 시 타이틀에서 시작" 요청. 실제 빌드는 EditorBuildSettings 0번이 Title이라 원래 그렇다.
    /// Main/Lobby를 바로 테스트하고 싶으면 이 파일을 지우거나 아래 줄을 주석 처리하면 된다.</summary>
    [InitializeOnLoad]
    public static class PlayFromTitle
    {
        private const string TitleScenePath = "Assets/Scenes/Title.unity";

        static PlayFromTitle()
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath);
        }
    }
}
