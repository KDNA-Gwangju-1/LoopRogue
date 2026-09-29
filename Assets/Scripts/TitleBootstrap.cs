using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>타이틀 씬(빌드 0번)의 진입점 - 게임 이름 + "게임 시작"(로비로) / "종료" 버튼. 버튼 클릭은
    /// LobbyBootstrap과 같은 방식(UGUI Button/EventSystem 없이 Mouse.current + 사각형 히트테스트)이라
    /// EventSystem 런타임 AddComponent 함정과 무관하다. 키보드로도 Enter/Space = 시작, Esc = 메뉴(PauseMenu).</summary>
    public class TitleBootstrap : MonoBehaviour
    {
        private const string LobbySceneName = "Lobby";

        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.35f);
        private static readonly Color StartButtonColor = new Color(0.2f, 0.45f, 0.25f);
        private static readonly Color ResetButtonColor = new Color(0.4f, 0.2f, 0.2f);
        private static readonly Color ResetConfirmColor = new Color(0.75f, 0.15f, 0.15f);
        private const float ResetConfirmWindow = 3f; // 첫 클릭 후 이 시간 안에 한 번 더 눌러야 초기화

        private readonly List<(RectTransform rect, Action onClick)> _buttons = new List<(RectTransform, Action)>();

        private Text _progressText;
        private Text _resetLabel;
        private Image _resetImage;
        private float _resetConfirmUntil = -1f;

        private void Awake()
        {
            StageProgress.EnsureLoaded();
            BuildUI();
            gameObject.AddComponent<PauseMenu>().Setup(StartGame, showQuit: true);
        }

        private void Update()
        {
            if (PauseMenu.BlocksInput)
                return; // Esc 메뉴가 열려 있으면 뒤의 타이틀 버튼은 안 눌리게.

            HandleMouseClick();

            // 확인 대기 시간이 지나면 초기화 버튼을 원래 상태로 되돌린다.
            if (_resetConfirmUntil > 0f && Time.unscaledTime > _resetConfirmUntil)
                SetResetButtonState(confirming: false);

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                StartGame();
        }

        private void HandleMouseClick()
        {
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

        private static void StartGame() => SceneManager.LoadScene(LobbySceneName);

        /// <summary>실수 방지로 두 번 눌러야 초기화된다 - 첫 클릭은 버튼을 빨갛게 바꾸고 "한 번 더"를 안내만 한다.</summary>
        private void OnResetClicked()
        {
            if (_resetConfirmUntil < 0f)
            {
                SetResetButtonState(confirming: true);
                return;
            }

            SaveReset.ResetAll();
            SetResetButtonState(confirming: false);
            _resetLabel.text = "초기화 완료!";
            RefreshProgress();
        }

        private void SetResetButtonState(bool confirming)
        {
            _resetConfirmUntil = confirming ? Time.unscaledTime + ResetConfirmWindow : -1f;
            _resetImage.color = confirming ? ResetConfirmColor : ResetButtonColor;
            _resetLabel.text = confirming ? "정말 초기화? 한 번 더 클릭" : "데이터 초기화";
        }

        private void RefreshProgress()
        {
            _progressText.text = StageProgress.CurrentStage > 1
                ? $"진행 중: Stage {StageProgress.CurrentStage}/{StageProgress.MaxStage}"
                : string.Empty;
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("TitleCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(canvasGo.transform, false);
            background.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            CreateLabel(canvasGo.transform, "Title", "LoopRogue", 72, FontStyle.Bold, Color.white, 150f, 800f, 90f);
            CreateLabel(canvasGo.transform, "Subtitle", "죽어도 강해진다 - 10개의 벽을 넘어라", 20, FontStyle.Normal,
                new Color(0.75f, 0.78f, 0.85f), 80f, 800f, 32f);

            _progressText = CreateLabel(canvasGo.transform, "Progress", string.Empty, 16, FontStyle.Normal,
                new Color(1f, 0.85f, 0.3f), 30f, 800f, 28f);
            RefreshProgress();

            CreateButton(canvasGo.transform, "StartButton", StartButtonColor, -50f, 320f, 56f,
                "게임 시작  [Enter]", 24, StartGame);
            CreateButton(canvasGo.transform, "QuitButton", ButtonColor, -125f, 320f, 46f,
                "종료", 20, QuitGame);

            // 키보드 단축키는 일부러 안 둔다 - 실수로 눌려서 데이터가 날아가면 안 되니 마우스로만.
            var resetRect = CreateButton(canvasGo.transform, "ResetButton", ResetButtonColor, -210f, 240f, 38f,
                "데이터 초기화", 16, OnResetClicked);
            _resetImage = resetRect.GetComponent<Image>();
            _resetLabel = resetRect.GetComponentInChildren<Text>();
        }

        private static Text CreateLabel(Transform parent, string goName, string content, int fontSize,
            FontStyle style, Color color, float y, float width, float height)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.text = content;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);
            return text;
        }

        private RectTransform CreateButton(Transform parent, string goName, Color bgColor, float y, float width, float height,
            string content, int fontSize, Action onClick)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = bgColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);

            CreateLabel(go.transform, goName + "Label", content, fontSize, FontStyle.Normal, Color.white, 0f, width - 20f, height);

            _buttons.Add((rect, onClick));
            return rect;
        }
    }
}
