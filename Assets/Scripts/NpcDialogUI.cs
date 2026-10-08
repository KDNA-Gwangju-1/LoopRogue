using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>NPC 대화창 - 화면 아래쪽에 이름표 + 대사 + 선택지 목록. Main(던전 NPC)과 Lobby(안내자)가 같이 쓴다.
    /// 선택지를 고르면 그 선택지의 동작이 실행되고, 동작 안에서 Show를 다시 부르면 내용이 바뀐 채로 계속 열려 있다(상점 구매 후 갱신 등).
    /// 조작: 숫자키 = 바로 고르기, 방향키(W/S)로 커서 + Enter/Space, 마우스 클릭, Esc/F = 닫기.
    /// 다른 UI와 같은 이유로 EventSystem 없이 Keyboard/Mouse.current와 사각형 히트테스트로 직접 읽는다. 자체 캔버스를 HUD보다 위에 그린다.</summary>
    public class NpcDialogUI : MonoBehaviour
    {
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly bool Enabled;
            public readonly Action OnPick;

            public Choice(string label, Action onPick, bool enabled = true)
            {
                Label = label;
                OnPick = onPick;
                Enabled = enabled;
            }
        }

        private const int MaxChoices = 6;
        private const float PanelWidth = 760f;
        private const float ChoiceHeight = 30f;
        private const float ChoiceGap = 4f;

        private static readonly Color ChoiceNormal = new Color(0.16f, 0.17f, 0.22f, 0.95f);
        private static readonly Color ChoiceCursor = new Color(0.3f, 0.42f, 0.62f, 1f);
        private static readonly Color ChoiceDisabled = new Color(0.1f, 0.1f, 0.12f, 0.9f);

        public static NpcDialogUI Instance { get; private set; }

        /// <summary>대화창이 열려 있으면 true - 이동/공격 입력과 다른 창(인벤토리, Esc 메뉴, 로비 버튼)을 막는다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>열려 있거나 이번 프레임에 막 닫혔으면 true - 닫는 키(Esc/Space/숫자)가 같은 프레임에 다른 곳에서 또 읽히지 않게.</summary>
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;

        private static int _closedFrame = -1;
        private int _openedFrame = -1;

        private GameObject _panel;
        private RectTransform _panelRect;
        private Text _speakerText;
        private Text _bodyText;
        private readonly List<(RectTransform Rect, Image Image, Text Label)> _choiceViews = new List<(RectTransform, Image, Text)>();
        private readonly List<Choice> _choices = new List<Choice>();
        private int _cursor;

        /// <summary>씬마다 한 번 - 자체 캔버스(HUD/로비 UI 위)에 대화창을 만든다.</summary>
        public static NpcDialogUI Create()
        {
            var canvasGo = new GameObject("NpcDialogCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // HUD(0) 위, Esc 메뉴보다는 아래여도 대화 중엔 Esc 메뉴가 안 열린다
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var ui = canvasGo.AddComponent<NpcDialogUI>();
            ui.Build(canvasGo.transform);
            Instance = ui;
            return ui;
        }

        private void OnDestroy()
        {
            // 씬이 넘어가며 파괴되면 정적 플래그도 풀어준다(안 그러면 다음 씬에서 입력이 잠긴 채로 남는다).
            IsOpen = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>대화창을 열거나(이미 열려 있으면) 내용을 바꾼다.</summary>
        public void Show(string speaker, string body, IList<Choice> choices)
        {
            if (!IsOpen)
                _openedFrame = Time.frameCount; // 연 키(F/숫자)가 같은 프레임에 선택/닫기로 또 읽히지 않게
            _speakerText.text = speaker;
            _bodyText.text = body;

            _choices.Clear();
            for (var i = 0; i < choices.Count && i < MaxChoices; i++)
                _choices.Add(choices[i]);
            _cursor = FirstEnabled();
            Layout();

            IsOpen = true;
            _panel.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            _closedFrame = Time.frameCount;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.fKey.wasPressedThisFrame)
                {
                    Close();
                    return;
                }

                for (var i = 0; i < _choices.Count; i++)
                {
                    if (DigitKey(keyboard, i).wasPressedThisFrame)
                    {
                        Pick(i);
                        return;
                    }
                }

                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                    MoveCursor(-1);
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                    MoveCursor(1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                {
                    Pick(_cursor);
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                var moved = mouse.delta.ReadValue().sqrMagnitude > 0f;
                for (var i = 0; i < _choices.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_choiceViews[i].Rect, pos, null))
                        continue;
                    if (moved && _choices[i].Enabled)
                        _cursor = i; // 마우스를 움직여 올리면 커서도 따라온다(가만히 있으면 W/S 커서를 덮어쓰지 않게)
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        Pick(i);
                        return;
                    }
                }
            }

            RefreshChoiceColors();
        }

        private static KeyControl DigitKey(Keyboard keyboard, int index) => index switch
        {
            0 => keyboard.digit1Key,
            1 => keyboard.digit2Key,
            2 => keyboard.digit3Key,
            3 => keyboard.digit4Key,
            4 => keyboard.digit5Key,
            _ => keyboard.digit6Key,
        };

        private void Pick(int index)
        {
            if (index < 0 || index >= _choices.Count || !_choices[index].Enabled)
                return;
            var action = _choices[index].OnPick;
            if (action == null)
                Close();
            else
                action(); // 동작이 Show를 다시 부르면 계속 열려 있고, 아니면 동작이 Close를 부른다
        }

        private void MoveCursor(int delta)
        {
            if (_choices.Count == 0)
                return;
            for (var step = 1; step <= _choices.Count; step++)
            {
                var next = (_cursor + delta * step + _choices.Count * step) % _choices.Count;
                if (_choices[next].Enabled)
                {
                    _cursor = next;
                    return;
                }
            }
        }

        private int FirstEnabled()
        {
            for (var i = 0; i < _choices.Count; i++)
                if (_choices[i].Enabled)
                    return i;
            return 0;
        }

        // ===================== UI 생성 =====================

        private void Build(Transform canvas)
        {
            var panelImage = HudUi.CreateImage(canvas, "NpcDialogPanel", new Color(0.05f, 0.05f, 0.07f, 0.95f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            _panelRect = panelImage.rectTransform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0f);
            _panelRect.pivot = new Vector2(0.5f, 0f);
            _panelRect.anchoredPosition = new Vector2(0f, ActionBarLayout.BottomAreaHeight + 12f); // 액션바 바로 위
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            // 이름표 - 패널 왼쪽 위에 걸친 띠
            var tag = HudUi.CreateImage(_panel.transform, "SpeakerTag", new Color(0.12f, 0.13f, 0.17f, 1f));
            if (PixelUi.Slice(tag, "Button"))
                tag.color = new Color(1f, 0.85f, 0.55f);
            var tagRect = tag.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 0.5f);
            tagRect.sizeDelta = new Vector2(220f, 34f);
            tagRect.anchoredPosition = new Vector2(20f, 0f);
            _speakerText = HudUi.CreateText(tag.transform, "Speaker", 17, TextAnchor.MiddleCenter);
            _speakerText.fontStyle = FontStyle.Bold;
            _speakerText.color = new Color(1f, 0.88f, 0.5f);
            _speakerText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            HudUi.Stretch(_speakerText.rectTransform, 0f);

            _bodyText = HudUi.CreateText(_panel.transform, "Body", 16, TextAnchor.UpperLeft);
            _bodyText.supportRichText = true;
            _bodyText.lineSpacing = 1.15f;
            _bodyText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            for (var i = 0; i < MaxChoices; i++)
            {
                var image = HudUi.CreateImage(_panel.transform, $"Choice{i}", ChoiceNormal);
                PixelUi.Slice(image, "Button");
                var rect = image.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                var label = HudUi.CreateText(image.transform, "Label", 15, TextAnchor.MiddleLeft);
                label.supportRichText = true;
                label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                HudUi.Stretch(label.rectTransform, 0f);
                label.rectTransform.offsetMin = new Vector2(14f, 0f);
                _choiceViews.Add((rect, image, label));
            }

            var hint = HudUi.CreateText(_panel.transform, "Hint", 12, TextAnchor.LowerRight);
            hint.text = "숫자키·클릭: 고르기   W/S·Enter: 커서로 고르기   Esc/F: 닫기";
            hint.color = new Color(0.6f, 0.63f, 0.7f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(20f, 8f);
            hintRect.offsetMax = new Vector2(-20f, 24f);

            _panel.SetActive(false);
        }

        /// <summary>선택지 수와 대사 길이에 맞춰 패널 높이와 칸 위치를 다시 잡는다(아래에서 위로: 안내 줄 → 선택지들 → 대사).</summary>
        private void Layout()
        {
            const float side = 24f, bottom = 30f, minBodyHeight = 92f, top = 26f;
            // 대사가 길면(기억의 조각 등) 본문 칸을 늘린다 - 너비는 패널 너비로 고정이라 미리 알 수 있다.
            var bodyRectWidth = _bodyText.rectTransform;
            bodyRectWidth.anchorMin = new Vector2(0f, 1f);
            bodyRectWidth.anchorMax = new Vector2(1f, 1f);
            _panelRect.sizeDelta = new Vector2(PanelWidth, _panelRect.sizeDelta.y);
            bodyRectWidth.offsetMin = new Vector2(side, bodyRectWidth.offsetMin.y);
            bodyRectWidth.offsetMax = new Vector2(-side, bodyRectWidth.offsetMax.y);
            var bodyHeight = Mathf.Max(minBodyHeight, _bodyText.preferredHeight + 6f);
            var choicesHeight = _choices.Count * ChoiceHeight + Mathf.Max(0, _choices.Count - 1) * ChoiceGap;
            var height = bottom + choicesHeight + 12f + bodyHeight + top;
            _panelRect.sizeDelta = new Vector2(PanelWidth, height);

            for (var i = 0; i < _choiceViews.Count; i++)
            {
                var (rect, _, label) = _choiceViews[i];
                var visible = i < _choices.Count;
                rect.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                var fromBottom = bottom + (_choices.Count - 1 - i) * (ChoiceHeight + ChoiceGap);
                rect.offsetMin = new Vector2(side, fromBottom);
                rect.offsetMax = new Vector2(-side, fromBottom + ChoiceHeight);
                label.text = $"<b>{i + 1}.</b>  {_choices[i].Label}";
            }

            var bodyRect = _bodyText.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = new Vector2(side, -(top + bodyHeight));
            bodyRect.offsetMax = new Vector2(-side, -top);

            RefreshChoiceColors();
        }

        private void RefreshChoiceColors()
        {
            for (var i = 0; i < _choices.Count; i++)
            {
                var (_, image, label) = _choiceViews[i];
                var enabled = _choices[i].Enabled;
                var color = !enabled ? ChoiceDisabled : i == _cursor ? ChoiceCursor : ChoiceNormal;
                image.color = image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;
                label.color = enabled ? Color.white : HudSlot.TextDim;
            }
        }
    }
}
