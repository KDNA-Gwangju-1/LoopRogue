using UnityEditor;
using UnityEngine;

namespace LoopRogue.EditorTools
{
    /// <summary>모바일(안드로이드·iOS) 설정 - 가로 고정(왼쪽·오른쪽으로 눕힌 것만 자동 회전), 안드로이드는 IL2CPP + ARM64(구글 플레이 필수).
    /// 에디터가 켜질 때마다 맞춰둔다(ProjectSettings를 손으로 고치면 열린 에디터가 덮어쓸 수 있어서 코드로).</summary>
    [InitializeOnLoad]
    public static class MobileBuildSettings
    {
        static MobileBuildSettings()
        {
            EditorApplication.delayCall += Apply;
        }

        private static void Apply()
        {
            var changed = false;
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                changed = true;
            }
            if (PlayerSettings.allowedAutorotateToPortrait || PlayerSettings.allowedAutorotateToPortraitUpsideDown
                || !PlayerSettings.allowedAutorotateToLandscapeLeft || !PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                PlayerSettings.allowedAutorotateToPortrait = false;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
                changed = true;
            }
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            if (PlayerSettings.GetScriptingBackend(android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
                changed = true;
            }
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
            {
                PlayerSettings.Android.targetArchitectures |= AndroidArchitecture.ARM64;
                changed = true;
            }
            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[모바일 설정] 가로 고정 · 안드로이드 IL2CPP/ARM64로 맞췄습니다.");
            }
        }
    }
}
