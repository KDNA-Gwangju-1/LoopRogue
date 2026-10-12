using UnityEditor;

namespace LoopRogue.EditorTools
{
    /// <summary>퍼즐 테스트 모드 켜기/끄기 - 켜면 일반 방에 들어갈 때마다(보물상자 방 제외) 퍼즐·미니게임 10종이 1번부터 차례로 나온다.
    /// 저장은 건드리지 않는다. 끄면 원래대로 15% 확률.</summary>
    public static class PuzzleTestMenu
    {
        private const string MenuPath = "LoopRogue/퍼즐 테스트 모드 (방마다 퍼즐 차례로)";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            var on = !EditorPrefs.GetBool(PuzzleBase.TestModePrefKey, false);
            EditorPrefs.SetBool(PuzzleBase.TestModePrefKey, on);
            Menu.SetChecked(MenuPath, on);
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PuzzleBase.TestModePrefKey, false));
            return true;
        }

        private const string GlitchMenuPath = "LoopRogue/오류방 테스트 모드 (일반 방마다 오류방)";

        /// <summary>오류방 테스트 모드 - 켜면 일반 전투 방(보물상자 방·보스방 제외)마다 오류방, 규칙(방향 반대·되살아남·두 번 행동)은 차례로.
        /// 퍼즐 테스트 모드가 켜져 있으면 퍼즐이 먼저 나온다.</summary>
        [MenuItem(GlitchMenuPath)]
        private static void ToggleGlitch()
        {
            var on = !EditorPrefs.GetBool(GlitchRoom.TestModePrefKey, false);
            EditorPrefs.SetBool(GlitchRoom.TestModePrefKey, on);
            Menu.SetChecked(GlitchMenuPath, on);
        }

        [MenuItem(GlitchMenuPath, true)]
        private static bool ToggleGlitchValidate()
        {
            Menu.SetChecked(GlitchMenuPath, EditorPrefs.GetBool(GlitchRoom.TestModePrefKey, false));
            return true;
        }
    }
}
