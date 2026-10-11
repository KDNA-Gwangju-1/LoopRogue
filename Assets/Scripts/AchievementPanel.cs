using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비의 업적·칭호 창 - 업적 목록(달성 여부/조건/칭호 효과/보상)과 칭호 장착. W/S·마우스로 고르고 Enter·클릭 = 장착(이미 장착한 걸
    /// 고르면 해제), Esc·닫기 = 닫기. 대화창(NpcDialogUI)과 같은 이유로 EventSystem 없이 Keyboard/Pointer.current로 직접 읽고 자체 캔버스에 그린다.</summary>
    public class AchievementPanel : MonoBehaviour
    {
        private const float RowHeight = 27f;
        private const float RowGap = 3f;
        private const float PanelWidth = 940f;

        private static readonly Color RowLocked = new Color(0.1f, 0.1f, 0.12f, 0.92f);
        private static readonly Color RowUnlocked = new Color(0.16f, 0.2f, 0.28f, 0.95f);
        private static readonly Color RowEquipped = new Color(0.45f, 0.36f, 0.12f, 0.95f);
        private static readonly Color RowCursor = new Color(0.3f, 0.42f, 0.62f, 1f);

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private Text _header;
        private RectTransform _closeButton;
        private readonly List<(RectTransform Rect, Image Image, Text Text)> _rows = new List<(RectTransform, Image, Text)>();
        private int _cursor;
        private int _openedFrame = -1;

        public static AchievementPanel Create()
        {
            var canvasGo = new GameObject("AchievementCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 16:9가 아닌 화면에서도 잘리지 않게
            var panel = canvasGo.AddComponent<AchievementPanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        public void Open()
        {
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _panel.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            IsOpen = false;
            _closedFrame = Time.frameCount;
            _panel.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;

            {
                if (GameInput.Down(Key.Escape))
                {
                    Close();
                    return;
                }
                var count = Achievements.All.Length;
                if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                    _cursor = (_cursor + count - 1) % count;
                if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                    _cursor = (_cursor + 1) % count;
                if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                    ToggleEquip(_cursor);
            }

            var mouse = Pointer.current;
            if (mouse != null)
            {
                var pos = GameInput.PointerPosition;
                var moved = GameInput.PointerDelta.sqrMagnitude > 0f;
                if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                for (var i = 0; i < _rows.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_rows[i].Rect, pos, null))
                        continue;
                    if (moved)
                        _cursor = i;
                    if (GameInput.PointerDown)
                    {
                        _cursor = i;
                        ToggleEquip(i);
                    }
                }
            }

            Refresh();
        }

        private static void ToggleEquip(int index)
        {
            var def = Achievements.All[index];
            if (!Achievements.IsUnlocked(def))
                return;
            Achievements.EquipTitle(Achievements.EquippedTitle == def ? null : def);
        }

        private void Refresh()
        {
            var equipped = Achievements.EquippedTitle;
            _header.text = Loc.F("<b>업적 · 칭호</b>   <color=#FFD966>{0}/{1}</color>   ", Achievements.UnlockedCount, Achievements.All.Length) +
                           Loc.F("<size=14>장착: {0}</size>", (equipped != null ? $"<color=#FFD966>「{equipped.Title}」</color> ({equipped.EffectText})" : Loc.T("<color=#888888>없음</color>"))) +
                           (SteamBridge.Initialized ? Loc.T("   <size=12><color=#8FC8FF>스팀 연동됨</color></size>") : "");

            for (var i = 0; i < _rows.Count; i++)
            {
                var def = Achievements.All[i];
                var unlocked = Achievements.IsUnlocked(def);
                var isEquipped = equipped == def;
                var (_, image, text) = _rows[i];
                var color = i == _cursor ? RowCursor : isEquipped ? RowEquipped : unlocked ? RowUnlocked : RowLocked;
                image.color = image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;

                var mark = isEquipped ? Loc.T("<color=#FFD966>[장착]</color>") : unlocked ? Loc.T("<color=#8CE08C>[달성]</color>") : "<color=#777777>[ - ]</color>";
                var nameColor = unlocked ? "#FFFFFF" : "#9A9AA2";
                var detailColor = unlocked ? "#C8CCD6" : "#7A7A82";
                text.text = $"{mark}  <color={nameColor}><b>{def.Name}</b></color>  <color={detailColor}>{def.Description}</color>" +
                            $"   <color=#FFD966>「{def.Title}」</color> <color={detailColor}>{def.EffectText} · {def.RewardText}</color>";
            }
        }

        private void Build(Transform canvas)
        {
            var count = Achievements.All.Length;
            const float header = 52f, footer = 34f, side = 20f;
            var height = header + count * (RowHeight + RowGap) + footer;

            var panelImage = HudUi.CreateImage(canvas, "AchievementPanel", new Color(0.05f, 0.05f, 0.07f, 0.96f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, height);
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            _header = HudUi.CreateText(_panel.transform, "Header", 18, TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            var headerRect = _header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(side + 4f, -header);
            headerRect.offsetMax = new Vector2(-60f, -6f);

            var close = HudUi.CreateImage(_panel.transform, "CloseButton", new Color(0.45f, 0.18f, 0.18f, 1f));
            if (PixelUi.Slice(close, "Button"))
                close.color = new Color(1f, 0.6f, 0.6f);
            _closeButton = close.rectTransform;
            _closeButton.anchorMin = _closeButton.anchorMax = new Vector2(1f, 1f);
            _closeButton.pivot = new Vector2(1f, 1f);
            _closeButton.sizeDelta = new Vector2(30f, 30f);
            _closeButton.anchoredPosition = new Vector2(-14f, -12f);
            var x = HudUi.CreateText(close.transform, "X", 16, TextAnchor.MiddleCenter);
            x.text = "X";
            HudUi.Stretch(x.rectTransform, 0f);

            for (var i = 0; i < count; i++)
            {
                var image = HudUi.CreateImage(_panel.transform, $"Row{i}", RowLocked);
                PixelUi.Slice(image, "Button");
                var rowRect = image.rectTransform;
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                var top = header + i * (RowHeight + RowGap);
                rowRect.offsetMin = new Vector2(side, -(top + RowHeight));
                rowRect.offsetMax = new Vector2(-side, -top);
                var text = HudUi.CreateText(image.transform, "Text", 13, TextAnchor.MiddleLeft);
                text.supportRichText = true;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                HudUi.Stretch(text.rectTransform, 0f);
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                _rows.Add((rowRect, image, text));
            }

            var hint = HudUi.CreateText(_panel.transform, "Hint", 12, TextAnchor.MiddleCenter);
            hint.text = Loc.T("W/S·마우스: 고르기   Enter·클릭: 칭호 장착/해제 (달성한 업적만)   Esc: 닫기   ·   칭호 효과는 다음 던전 입장부터");
            hint.color = new Color(0.6f, 0.63f, 0.7f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(side, 6f);
            hintRect.offsetMax = new Vector2(-side, footer - 4f);

            _panel.SetActive(false);
        }
    }
}
