using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>11-20 UI 개선 1단계(사용자 "UI 다 개선") - 화면의 모든 UI 글자에 어두운 그림자를 붙여 바닥·벽 그림 위에서도 잘 읽히게 한다.
    /// 화면마다 글자를 만드는 함수가 따로라(CreateLabel·Label·CreateText…) 하나씩 고치는 대신, 게임 내내 살아 있는 관리자가
    /// 0.25초마다 새로 생긴 Text 를 찾아 그림자(Shadow·Outline)가 없으면 붙인다. 이미 효과를 단 글자는 건드리지 않는다.</summary>
    public class UiTextPolish : MonoBehaviour
    {
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Vector2 ShadowDistance = new Vector2(1.5f, -1.5f);
        private const float ScanInterval = 0.25f;

        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("UiTextPolish");
            DontDestroyOnLoad(go);
            go.AddComponent<UiTextPolish>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan)
                return;
            _nextScan = Time.unscaledTime + ScanInterval;
            foreach (var text in FindObjectsByType<Text>(FindObjectsSortMode.None))
                Apply(text);
        }

        /// <summary>그림자가 없는 글자에 그림자 하나(효과 컴포넌트가 이미 있으면 그대로).</summary>
        public static void Apply(Text text)
        {
            if (text == null || text.GetComponent<BaseMeshEffect>() != null)
                return;
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = ShadowDistance;
        }
    }
}
