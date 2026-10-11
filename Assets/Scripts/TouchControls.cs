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
            public Text Label;
            public string LabelKey;
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
            if (_searched)
                return; // 씬이 바뀌면 sceneLoaded가 다시 찾게 한다
            _searched = true;
            _hud = FindAnyObjectByType<GameHUD>();
            _player = FindAnyObjectByType<PlayerActor>();
        }

        // ---- 언제 보일지 ----
        /// <summary>방향 패드 - 평소 + 시작 방 고르기(←/→로 방을 고른다).</summary>
        private bool PadVisible() => InMain && _hud != null && _player != null && !_player.Stats.IsDead && _hud.PendingUpgradeOptions == null
                                     && _hud.PendingRelicOptions == null && !_hud.IsDeathChoiceOpen && !_hud.IsStageClearOpen
                                     && !OptionsPanel.IsOpen && !PauseMenu.IsOpen && FindEnding() == null;
        /// <summary>대기·대화·능력치 - 실제로 방 안에서 움직일 때만.</summary>
        private bool Playing() => PadVisible() && !_hud.IsRoomSelectOpen;
        private bool Choosing() => InMain && _hud != null && (_hud.PendingUpgradeOptions != null || _hud.PendingRelicOptions != null)
                                   && !PauseMenu.IsOpen;
        private bool DeathOpen() => InMain && _hud != null && _hud.IsDeathChoiceOpen;
        private bool ConfirmOpen() => (InMain && _hud != null && (_hud.IsStageClearOpen || _hud.IsDeathChoiceOpen || _hud.IsRoomSelectOpen))
                                      || FindEnding() != null;
        private bool MenuAllowed() => !OptionsPanel.IsOpen && FindEnding() == null && !Choosing()
                                      && (!InMain || (_hud != null && !_hud.IsStatPanelOpen && !_hud.IsRoomSelectOpen && !_hud.IsDeathChoiceOpen
                                                      && !_hud.IsStageClearOpen));

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
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 16:9가 아닌 화면에서도 잘리지 않게
            var root = canvasGo.transform;

            // 왼쪽 아래 방향 패드
            var pad = new Vector2(150f, 170f);
            const float step = 92f, size = 86f;
            Add(root, "▲", Corner.BottomLeft, pad + new Vector2(0f, step), size, Key.W, PadVisible, repeat: true);
            Add(root, "▼", Corner.BottomLeft, pad + new Vector2(0f, -step), size, Key.S, PadVisible, repeat: true);
            Add(root, "◀", Corner.BottomLeft, pad + new Vector2(-step, 0f), size, Key.A, PadVisible, repeat: true);
            Add(root, "▶", Corner.BottomLeft, pad + new Vector2(step, 0f), size, Key.D, PadVisible, repeat: true);

            // 오른쪽 아래 - 대기 / 대화(F) / 능력치(Tab)
            Add(root, "대기", Corner.BottomRight, new Vector2(-130f, 150f), 110f, Key.Space, Playing);
            Add(root, "대화", Corner.BottomRight, new Vector2(-250f, 95f), 80f, Key.F, Playing);
            Add(root, "능력치", Corner.BottomRight, new Vector2(-250f, 195f), 80f, Key.Tab, Playing);

            // 오른쪽 위 - 메뉴(Esc). 게임 밖(타이틀·로비)에선 Esc가 "뒤로"라 이름도 바꾼다(Update).
            _menuButton = Add(root, "메뉴", Corner.TopRight, new Vector2(-70f, -150f), 76f, Key.Escape, MenuAllowed);

            // 화면 가운데 아래(하단 스킬 줄 바로 위) - 그때 필요한 선택. 카드·사망 창 글자를 가리지 않게 낮게.
            Add(root, "1", Corner.Bottom, new Vector2(-130f, 135f), 84f, Key.Digit1, Choosing);
            Add(root, "2", Corner.Bottom, new Vector2(0f, 135f), 84f, Key.Digit2, Choosing);
            Add(root, "3", Corner.Bottom, new Vector2(130f, 135f), 84f, Key.Digit3, Choosing);
            Add(root, "확인", Corner.Bottom, new Vector2(-90f, 135f), 120f, Key.Enter, ConfirmOpen, wide: true);
            Add(root, "로비", Corner.Bottom, new Vector2(90f, 135f), 120f, Key.L, DeathOpen, wide: true);
        }

        private enum Corner { BottomLeft, BottomRight, TopRight, Bottom }

        private Button _menuButton;

        /// <param name="label">한국어 글자(번역 열쇠) - 숫자·화살표는 그대로 보인다.</param>
        private Button Add(Transform parent, string label, Corner corner, Vector2 pos, float size, Key key, Func<bool> visible,
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

            var button = new Button { Rect = rect, Image = image, Key = key, Visible = visible, Repeat = repeat, Label = text, LabelKey = label };
            _buttons.Add(button);
            GameInput.RegisterTap(rect, key, () => button.Visible());
            return button;
        }

        private void Update()
        {
            FindScene();
            if (_menuButton != null)
                _menuButton.LabelKey = InMain ? "메뉴" : "뒤로";
            foreach (var b in _buttons)
            {
                var show = b.Visible();
                if (b.Rect.gameObject.activeSelf != show)
                    b.Rect.gameObject.SetActive(show);
                if (show)
                {
                    var text = Loc.T(b.LabelKey); // 지금 언어
                    if (b.Label.text != text)
                        b.Label.text = text;
                }
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
