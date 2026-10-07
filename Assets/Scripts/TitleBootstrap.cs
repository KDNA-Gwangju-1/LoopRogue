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
        // 도트 돌판 버튼(Resources/UI/Title/<이름>.png, <이름>_Hover.png - 배경과 같은 4배 픽셀)은 그림에 색이 있어서 흰색으로 두고,
        // 초기화 확인 대기 중에만 붉게 물들인다. 그림이 없으면 위 단색으로.
        private static readonly Color ResetConfirmTint = new Color(1f, 0.45f, 0.45f);
        private const float ResetConfirmWindow = 3f; // 첫 클릭 후 이 시간 안에 한 번 더 눌러야 초기화

        private readonly List<(RectTransform rect, Action onClick)> _buttons = new List<(RectTransform, Action)>();
        // 마우스를 올리면 횃불빛 그림으로 바꾼다.
        private readonly List<(RectTransform rect, Image image, Sprite normal, Sprite hover)> _hoverButtons =
            new List<(RectTransform, Image, Sprite, Sprite)>();

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
            UpdateHover();

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

        private void UpdateHover()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            var screenPos = mouse.position.ReadValue();
            foreach (var (rect, image, normal, hover) in _hoverButtons)
            {
                var sprite = RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null) ? hover : normal;
                if (image.sprite != sprite)
                    image.sprite = sprite;
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
        }

        private void SetResetButtonState(bool confirming)
        {
            _resetConfirmUntil = confirming ? Time.unscaledTime + ResetConfirmWindow : -1f;
            if (_resetImage.sprite != null)
                _resetImage.color = confirming ? ResetConfirmTint : Color.white;
            else
                _resetImage.color = confirming ? ResetConfirmColor : ResetButtonColor;
            _resetLabel.text = confirming ? "정말 초기화? 한 번 더 클릭" : "데이터 초기화";
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

            BuildBackground(canvasGo.transform);

            BuildLogo(canvasGo.transform);

            // 크기는 4의 배수 - 도트 그림(80x14 / 80x12 / 60x10)을 정확히 4배로.
            CreateButton(canvasGo.transform, "StartButton", StartButtonColor, -50f, 320f, 56f,
                "게임 시작  [Enter]", 24, StartGame, "Start", new Color(1f, 0.9f, 0.65f));
            CreateButton(canvasGo.transform, "QuitButton", ButtonColor, -125f, 320f, 48f,
                "종료", 20, QuitGame, "Quit");

            // 키보드 단축키는 일부러 안 둔다 - 실수로 눌려서 데이터가 날아가면 안 되니 마우스로만.
            var resetRect = CreateButton(canvasGo.transform, "ResetButton", ResetButtonColor, -210f, 240f, 40f,
                "데이터 초기화", 16, OnResetClicked, "Reset", new Color(1f, 0.82f, 0.78f));
            _resetImage = resetRect.GetComponent<Image>();
            _resetLabel = resetRect.GetComponentInChildren<Text>();
        }

        /// <summary>도트 던전 그림(Resources/Backgrounds/TitleDungeon, 320x180) - 화면 비율이 16:9가 아니어도 빈틈 없이 덮게
        /// 비율 유지 + 넘치는 쪽은 잘림. 그림이 없으면 예전 단색. 위에 얇은 어둠을 깔아 제목·버튼이 묻히지 않게.</summary>
        private static void BuildBackground(Transform parent)
        {
            var sprite = Resources.Load<Sprite>("Backgrounds/TitleDungeon");

            var background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(parent, false);
            var image = background.AddComponent<Image>();
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(1280f, 720f);
            if (sprite != null)
            {
                image.sprite = sprite;
                var fitter = background.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            else
            {
                image.color = new Color(0.08f, 0.09f, 0.12f);
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;
            }

            var shade = new GameObject("Shade", typeof(RectTransform));
            shade.transform.SetParent(parent, false);
            shade.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var shadeRect = shade.GetComponent<RectTransform>();
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = Vector2.zero;
            shadeRect.offsetMax = Vector2.zero;
        }

        /// <summary>도트 로고(Resources/UI/Title/Logo.png - LOOP는 불꽃색, ROGUE는 돌색, 첫 O는 도는 화살표)를 배경·버튼과 같은
        /// 4배 픽셀로. 그림이 없으면 예전 글자 제목.</summary>
        private static void BuildLogo(Transform parent)
        {
            const float pixelScale = 4f;
            var sprite = Resources.Load<Sprite>("UI/Title/Logo");
            if (sprite == null)
            {
                var title = CreateLabel(parent, "Title", "LoopRogue", 72, FontStyle.Bold, Color.white, 150f, 800f, 90f);
                AddShadow(title, 4f);
                return;
            }

            var go = new GameObject("Logo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().sprite = sprite;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * pixelScale;
            rect.anchoredPosition = new Vector2(0f, 150f);
        }

        private static void AddShadow(Text text, float distance)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(distance, -distance);
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
            string content, int fontSize, Action onClick, string spriteName = null, Color? textColor = null)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            var normal = spriteName != null ? Resources.Load<Sprite>($"UI/Title/{spriteName}") : null;
            var hover = spriteName != null ? Resources.Load<Sprite>($"UI/Title/{spriteName}_Hover") : null;
            if (normal != null)
                image.sprite = normal;
            else
                image.color = bgColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);

            var label = CreateLabel(go.transform, goName + "Label", content, fontSize, FontStyle.Normal,
                normal != null && textColor.HasValue ? textColor.Value : Color.white, 0f, width - 20f, height);
            if (normal != null)
            {
                AddShadow(label, 2f);
                if (hover != null)
                    _hoverButtons.Add((rect, image, normal, hover));
            }

            _buttons.Add((rect, onClick));
            return rect;
        }
    }
}
