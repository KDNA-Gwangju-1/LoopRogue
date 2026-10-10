using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>모바일 화면 버튼 - 왼쪽 아래 방향 패드(꾹 누르면 반복), 오른쪽 아래 대기·대화, 오른쪽 위 메뉴·능력치,
    /// 화면 가운데 위에 그때그때 필요한 버튼(레벨업 카드·유물 1/2/3, 계속하기/로비, 확인). 스킬(Q/E)·퀵슬롯(1~3)·가방(I)은
    /// HUD 칸을 직접 탭한다(GameHUD·InventoryUI가 GameInput.RegisterTap). 버튼은 키를 누른 것과 똑같이 동작한다(GameInput).
    /// 터치 화면이 있을 때만 보인다(에디터에선 메뉴 "LoopRogue/모바일 터치 버튼 미리보기"로 켜서 마우스로 시험).</summary>
    public class TouchControls : MonoBehaviour
    {
        public static bool Enabled { get; private set; }

#if UNITY_EDITOR
        public const string PreviewPrefKey = "LoopRogue_TouchPreview";
#endif

        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.16f;
        private static readonly Color PadColor = new Color(0.2f, 0.22f, 0.3f, 0.55f);
        private static readonly Color PressedColor = new Color(0.55f, 0.7f, 1f, 0.75f);

        private sealed class Button
        {
            public RectTransform Rect;
            public Image Image;
            public Key Key;
            public Func<bool> Visible;
            public bool Repeat;
        }

        private readonly List<Button> _buttons = new List<Button>();
        private Button _held;
        private float _heldSince, _lastRepeat;

        // 지금 씬의 화면들(씬이 바뀌면 다시 찾음)
        private GameHUD _hud;
        private PlayerActor _player;
        private bool _searched;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Enabled = Application.isMobilePlatform || Touchscreen.current != null;
#if UNITY_EDITOR
            Enabled = UnityEditor.EditorPrefs.GetBool(PreviewPrefKey, false);
#endif
            if (!Enabled)
                return;
            var go = new GameObject("TouchControls");
            DontDestroyOnLoad(go);
            go.AddComponent<TouchControls>().Build();
            SceneManager.sceneLoaded += (_, __) => { var tc = FindAnyObjectByType<TouchControls>(); if (tc != null) tc._searched = false; };
        }

        private bool InMain => SceneManager.GetActiveScene().name == "Main";

        private void FindScene()
        {
            if (_searched && _hud != null)
                return;
            _searched = true;
            _hud = FindAnyObjectByType<GameHUD>();
            _player = FindAnyObjectByType<PlayerActor>();
        }

        // ---- 언제 보일지 ----
        private bool Playing() => InMain && _hud != null && _player != null && !_player.Stats.IsDead && _hud.PendingUpgradeOptions == null
                                  && _hud.PendingRelicOptions == null && !_hud.IsDeathChoiceOpen && !_hud.IsStageClearOpen
                                  && !OptionsPanel.IsOpen && FindEnding() == null;
        private bool Choosing() => InMain && _hud != null && (_hud.PendingUpgradeOptions != null || _hud.PendingRelicOptions != null);
        private bool DeathOpen() => InMain && _hud != null && _hud.IsDeathChoiceOpen;
        private bool ConfirmOpen() => (InMain && _hud != null && (_hud.IsStageClearOpen || _hud.IsDeathChoiceOpen)) || FindEnding() != null;
        private bool MenuAllowed() => !OptionsPanel.IsOpen && FindEnding() == null && !Choosing();

        private EndingScreen _ending;
        private EndingScreen FindEnding()
        {
            if (_ending == null && Time.frameCount % 20 == 0)
                _ending = FindAnyObjectByType<EndingScreen>();
            return _ending;
        }

        private void Build()
        {
            var canvasGo = new GameObject("TouchCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // HUD 위, Esc 메뉴(100)·설정(150)·엔딩(500) 아래
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // 왼쪽 아래 방향 패드
            var pad = new Vector2(150f, 170f);
            const float step = 92f, size = 86f;
            Add(root, "▲", Corner.BottomLeft, pad + new Vector2(0f, step), size, Key.W, Playing, repeat: true);
            Add(root, "▼", Corner.BottomLeft, pad + new Vector2(0f, -step), size, Key.S, Playing, repeat: true);
            Add(root, "◀", Corner.BottomLeft, pad + new Vector2(-step, 0f), size, Key.A, Playing, repeat: true);
            Add(root, "▶", Corner.BottomLeft, pad + new Vector2(step, 0f), size, Key.D, Playing, repeat: true);

            // 오른쪽 아래 - 대기 / 대화(F)
            Add(root, Loc.T("대기"), Corner.BottomRight, new Vector2(-130f, 150f), 110f, Key.Space, Playing);
            Add(root, Loc.T("대화"), Corner.BottomRight, new Vector2(-250f, 95f), 80f, Key.F, Playing);

            // 오른쪽 위 - 메뉴(Esc) / 능력치(Tab)
            Add(root, Loc.T("메뉴"), Corner.TopRight, new Vector2(-70f, -150f), 76f, Key.Escape, MenuAllowed);
            Add(root, Loc.T("능력치"), Corner.TopRight, new Vector2(-70f, -236f), 76f, Key.Tab, Playing);

            // 화면 가운데 아래 - 그때 필요한 선택
            Add(root, "1", Corner.Bottom, new Vector2(-130f, 230f), 90f, Key.Digit1, Choosing);
            Add(root, "2", Corner.Bottom, new Vector2(0f, 230f), 90f, Key.Digit2, Choosing);
            Add(root, "3", Corner.Bottom, new Vector2(130f, 230f), 90f, Key.Digit3, Choosing);
            Add(root, Loc.T("확인"), Corner.Bottom, new Vector2(-90f, 230f), 120f, Key.Enter, ConfirmOpen, wide: true);
            Add(root, Loc.T("로비"), Corner.Bottom, new Vector2(90f, 230f), 120f, Key.L, DeathOpen, wide: true);
        }

        private enum Corner { BottomLeft, BottomRight, TopRight, Bottom }

        private void Add(Transform parent, string label, Corner corner, Vector2 pos, float size, Key key, Func<bool> visible,
            bool repeat = false, bool wide = false)
        {
            var image = HudUi.CreateImage(parent, "Touch_" + key, PadColor);
            if (PixelUi.Slice(image, "Button"))
                image.color = new Color(1f, 1f, 1f, 0.6f);
            var rect = image.rectTransform;
            var anchor = corner switch
            {
                Corner.BottomLeft => new Vector2(0f, 0f),
                Corner.BottomRight => new Vector2(1f, 0f),
                Corner.TopRight => new Vector2(1f, 1f),
                _ => new Vector2(0.5f, 0f),
            };
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = wide ? new Vector2(size * 1.5f, size * 0.62f) : new Vector2(size, size);
            rect.anchoredPosition = pos;
            var text = HudUi.CreateText(rect, "Label", label.Length <= 1 ? 34 : 20, TextAnchor.MiddleCenter);
            text.text = label;
            HudUi.Stretch(text.rectTransform, 0f);
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            var button = new Button { Rect = rect, Image = image, Key = key, Visible = visible, Repeat = repeat };
            _buttons.Add(button);
            GameInput.RegisterTap(rect, key, () => button.Visible());
        }

        private void Update()
        {
            FindScene();
            foreach (var b in _buttons)
            {
                var show = b.Visible();
                if (b.Rect.gameObject.activeSelf != show)
                    b.Rect.gameObject.SetActive(show);
            }

            // 누른 버튼 반짝 + 방향 패드 꾹 누르기 반복
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                GameInput.Prime(); // 이번 프레임 탭 판정을 먼저 돌려서 LastTappedRect를 갱신
                _held = _buttons.Find(b => b.Rect == GameInput.LastTappedRect && b.Rect.gameObject.activeInHierarchy &&
                                           RectTransformUtility.RectangleContainsScreenPoint(b.Rect, pointer.position.ReadValue(), null));
                _heldSince = _lastRepeat = Time.unscaledTime;
            }
            if (_held != null && (pointer == null || !pointer.press.isPressed || !_held.Rect.gameObject.activeInHierarchy))
                _held = null;
            if (_held != null && _held.Repeat && Time.unscaledTime - _heldSince > RepeatDelay && Time.unscaledTime - _lastRepeat > RepeatInterval)
            {
                _lastRepeat = Time.unscaledTime;
                GameInput.Inject(_held.Key);
            }
            foreach (var b in _buttons)
                b.Image.color = b == _held ? PressedColor : (b.Image.sprite != null ? new Color(1f, 1f, 1f, 0.6f) : PadColor);
        }
    }
}
