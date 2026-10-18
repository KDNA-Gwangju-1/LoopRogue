using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LoopRogue.EditorTools
{
    /// <summary>윈도우 64비트 빌드 - 빌드 설정에 켜진 씬(타이틀·로비·메인) 그대로 사용자 폴더 아래 LoopRogue_Build(OutputDir)에 LoopRogue.exe로.
    /// 에디터 메뉴로도, 에디터를 끈 상태의 배치 모드(-executeMethod LoopRogue.EditorTools.BuildMenu.BuildWindows)로도 쓴다.</summary>
    public static class BuildMenu
    {
        public static readonly string OutputDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile).Replace('\\', '/') + "/LoopRogue_Build";

        [MenuItem("LoopRogue/윈도우 빌드 (LoopRogue_Build 폴더)")]
        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir + "/LoopRogue.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildMenu] {summary.result} - {summary.outputPath} ({summary.totalSize / (1024 * 1024)}MB, {summary.totalTime.TotalSeconds:0}초, 에러 {summary.totalErrors})");
            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
