using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>Esc로 여닫는 공용 메뉴 - "로비로 이동" / "계속하기"(+ 선택적으로 "게임 종료"). Main/Title 씬이
    /// 각자 AddComponent 후 Setup으로 로비 이동 동작을 넘겨준다(Main은 런 저장 후 이동, Title은 바로 이동).
    /// 버튼 클릭은 로비와 같은 수동 사각형 히트테스트(EventSystem 없음). 자체 캔버스를 HUD보다 위에 그린다.</summary>
    public class PauseMenu : MonoBehaviour
    {
        /// <summary>열려 있는 동안 PlayerActor가 이동/공격 입력을 안 받는다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>메뉴가 열려 있거나 "이번 프레임에 막 닫혔으면" true - 닫는 클릭/키가 같은 프레임에 뒤에
        /// 있는 다른 버튼(타이틀 시작 버튼 등)까지 눌러버리지 않게, 다른 입력 처리는 이걸로 막는다.</summary>
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;

        private static int _closedFrame = -1;

        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.35f);
        private static readonly Color LobbyButtonColor = new Color(0.2f, 0.45f, 0.25f);

        private readonly List<(RectTransform rect, Action onClick)> _buttons = new List<(RectTransform, Action)>();

        private Action _goToLobby;
        private Action _goToTitle;
        private Func<bool> _canOpen;
        private GameObject _panel;

        /// <param name="goToLobby">"로비로 이동"을 눌렀을 때 할 일.</param>
        /// <param name="canOpen">지금 메뉴를 열어도 되는지 - 사망/레벨업/클리어 창이 떠 있을 땐 안 열리게.</param>
        /// <param name="showQuit">"게임 종료" 버튼도 보여줄지(타이틀용).</param>
        /// <param name="lobbyLabel">"로비로 이동" 버튼 문구(Main은 데스 패널티 안내를 붙인다).</param>
        /// <param name="goToTitle">있으면 "타이틀로 이동 [T]" 버튼도 보여준다(Main용).</param>
        public void Setup(Action goToLobby, Func<bool> canOpen = null, bool showQuit = false, string lobbyLabel = null,
            Action goToTitle = null)
        {
            _goToLobby = goToLobby;
            _goToTitle = goToTitle;
            _canOpen = canOpen;
            BuildUI(showQuit, lobbyLabel ?? "로비로 이동  [L]");
            SetOpen(false);
        }

        private void OnDestroy()
        {
            // 씬이 넘어가며 파괴되면 정적 플래그도 같이 풀어준다(안 그러면 다음 씬에서 입력이 잠긴 채로 남는다).
            IsOpen = false;
        }

        private void Update()
        {
            if (_panel == null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen)
                    SetOpen(false);
                else if (_canOpen == null || _canOpen())
                    SetOpen(true);
                return;
            }

            if (!IsOpen)
                return;

            if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
            {
                GoToLobby();
                return;
            }

            if (_goToTitle != null && keyboard != null && keyboard.tKey.wasPressedThisFrame)
            {
                GoToTitle();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            var screenPos = mouse.position.ReadValue();
            foreach (var (rect, onClick) in _buttons)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null))
                {
                    onClick();
                    return;
                }
            }
        }

        private void SetOpen(bool open)
        {
            if (IsOpen && !open)
                _closedFrame = Time.frameCount;
            IsOpen = open;
            _panel.SetActive(open);
        }

        private void GoToLobby()
        {
            SetOpen(false);
            _goToLobby?.Invoke();
        }

        private void GoToTitle()
        {
            SetOpen(false);
            _goToTitle?.Invoke();
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildUI(bool showQuit, string lobbyLabel)
        {
            var canvasGo = new GameObject("PauseMenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // HUD/로비 UI보다 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            // 화면 전체를 어둡게 덮는 배경 + 가운데 메뉴
            _panel = new GameObject("PauseMenu", typeof(RectTransform));
            _panel.transform.SetParent(canvasGo.transform, false);
            var dim = _panel.GetComponent<RectTransform>();
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero;
            dim.offsetMax = Vector2.zero;
            _panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            // 메뉴 뒤 돌 테두리 패널(도트 그림이 있을 때만) - 버튼 수에 맞춘 높이
            var buttonCount = 2 + (_goToTitle != null ? 1 : 0) + (showQuit ? 1 : 0);
            var board = new GameObject("Board", typeof(RectTransform));
            board.transform.SetParent(_panel.transform, false);
            var boardImage = board.AddComponent<Image>();
            if (PixelUi.Slice(boardImage, "Panel"))
            {
                var boardRect = board.GetComponent<RectTransform>();
                var top = 150f;
                var bottom = 40f - (buttonCount - 1) * 60f - 44f;
                boardRect.sizeDelta = new Vector2(500f, top - bottom);
                boardRect.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f);
            }
            else
                Destroy(board);

            CreateLabel(_panel.transform, "메뉴", 30, FontStyle.Bold, 110f, 400f, 44f);

            var y = 40f;
            CreateButton(_panel.transform, lobbyLabel, LobbyButtonColor, y, GoToLobby);
            if (_goToTitle != null)
            {
                y -= 60f;
                CreateButton(_panel.transform, "타이틀로 이동  [T]", ButtonColor, y, GoToTitle);
            }
            y -= 60f;
            CreateButton(_panel.transform, "계속하기  [Esc]", ButtonColor, y, () => SetOpen(false));
            if (showQuit)
            {
                y -= 60f;
                CreateButton(_panel.transform, "게임 종료", new Color(0.4f, 0.2f, 0.2f), y, QuitGame);
            }
        }

        private static Text CreateLabel(Transform parent, string content, int fontSize, FontStyle style,
            float y, float width, float height)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.text = content;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);
            return text;
        }

        private void CreateButton(Transform parent, string content, Color bgColor, float y, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = PixelUi.Slice(image, "Button") ? PixelUi.Tint(bgColor) : bgColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 48f);
            rect.anchoredPosition = new Vector2(0f, y);

            var label = CreateLabel(go.transform, content, 18, FontStyle.Normal, 0f, 400f, 48f);
            label.rectTransform.anchoredPosition = Vector2.zero;

            _buttons.Add((rect, onClick));
        }
    }
}
