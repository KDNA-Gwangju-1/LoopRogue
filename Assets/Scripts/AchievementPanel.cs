using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비의 업적·칭호 창 - 대화창(NpcDialogUI)과 같은 이유로 EventSystem 없이 Keyboard/Pointer.current로 직접 읽고 자체 캔버스에 그린다.
    /// 11-20 UI 개선(사용자 "업적창하고 칭호창 개선"): 한 줄에 다 몰려 있던 목록을 탭 두 개로 나눴다.
    /// - [업적] 탭: 모든 업적(달성 여부 · 이름 · 조건 | 칭호 · 효과 · 보상). 보기만.
    /// - [칭호] 탭: 달성해서 얻은 칭호만. W/S·마우스로 고르고 Enter·클릭 = 장착(장착한 걸 고르면 해제).
    /// A/D·←/→·탭 클릭 = 탭 바꾸기, Esc·X = 닫기. 창 뒤는 화면 전체를 어둡게(로비 안내 줄이 비쳐 보이던 문제).</summary>
    public class AchievementPanel : MonoBehaviour
    {
        private const float RowHeight = 34f;
        private const float RowGap = 4f;
        private const float MaxPanelHeight = 690f;
        private const float PanelWidth = 1000f;
        private const float HeaderHeight = 98f;
        private const float FooterHeight = 34f;
        private const float Side = 20f;

        private static readonly Color RowLocked = new Color(0.09f, 0.09f, 0.11f, 0.92f);
        private static readonly Color RowUnlocked = new Color(0.15f, 0.19f, 0.27f, 0.95f);
        private static readonly Color RowEquipped = new Color(0.45f, 0.36f, 0.12f, 0.95f);
        private static readonly Color RowCursor = new Color(0.3f, 0.42f, 0.62f, 1f);
        private static readonly Color TabOn = new Color(0.36f, 0.48f, 0.7f, 1f);
        private static readonly Color TabOff = new Color(0.16f, 0.17f, 0.21f, 1f);

        private enum Tab { Achievements, Titles }

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _root;
        private Text _title;
        private Text _summary;
        private RectTransform _progressFill;
        private RectTransform _closeButton;
        private (RectTransform Rect, Image Image, Text Text)[] _tabs;
        private Text _hint;
        private Text _empty;
        private readonly List<(RectTransform Rect, Image Image, Text Left, Text Right)> _rows = new List<(RectTransform, Image, Text, Text)>();
        private readonly List<AchievementDef> _list = new List<AchievementDef>();
        private Tab _tab;
        private int _cursor;
        private int _scroll;
        private int _openedFrame = -1;
        private int VisibleRows => _rows.Count;

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
            _root.SetActive(true);
            SetTab(_tab);
        }

        private void Close()
        {
            IsOpen = false;
            _closedFrame = Time.frameCount;
            _root.SetActive(false);
        }

        private void SetTab(Tab tab)
        {
            _tab = tab;
            _list.Clear();
            foreach (var def in Achievements.All)
                if (tab == Tab.Achievements || Achievements.IsUnlocked(def))
                    _list.Add(def);
            // 칭호 탭은 장착한 칭호에 커서를 두고 연다
            var equipped = Achievements.EquippedTitle;
            _cursor = tab == Tab.Titles && equipped != null ? Mathf.Max(0, _list.IndexOf(equipped)) : 0;
            _scroll = Mathf.Clamp(_cursor - VisibleRows / 2, 0, Mathf.Max(0, _list.Count - VisibleRows));
            Refresh();
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;

            if (GameInput.Down(Key.Escape))
            {
                Close();
                return;
            }
            if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A) || GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D))
                SetTab(_tab == Tab.Achievements ? Tab.Titles : Tab.Achievements);

            var count = _list.Count;
            if (count > 0)
            {
                if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                    _cursor = (_cursor + count - 1) % count;
                if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                    _cursor = (_cursor + 1) % count;
                if (_tab == Tab.Titles && (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space)))
                    ToggleEquip(_list[_cursor]);
                // 휠 - 커서는 그대로 두고 목록만 굴린다
                var wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
                if (wheel != 0f)
                    _scroll = Mathf.Clamp(_scroll + (wheel > 0f ? -3 : 3), 0, Mathf.Max(0, count - VisibleRows));
                else if (_cursor < _scroll)
                    _scroll = _cursor;
                else if (_cursor >= _scroll + VisibleRows)
                    _scroll = _cursor - VisibleRows + 1;
            }

            if (Pointer.current != null)
            {
                var pos = GameInput.PointerPosition;
                var moved = GameInput.PointerDelta.sqrMagnitude > 0f;
                if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                for (var t = 0; t < _tabs.Length; t++)
                    if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_tabs[t].Rect, pos, null) && (Tab)t != _tab)
                        SetTab((Tab)t);
                for (var i = 0; i < _rows.Count && _scroll + i < count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_rows[i].Rect, pos, null))
                        continue;
                    if (moved)
                        _cursor = _scroll + i;
                    if (GameInput.PointerDown)
                    {
                        _cursor = _scroll + i;
                        if (_tab == Tab.Titles)
                            ToggleEquip(_list[_cursor]);
                    }
                }
            }

            Refresh();
        }

        private static void ToggleEquip(AchievementDef def)
        {
            if (!Achievements.IsUnlocked(def))
                return;
            Achievements.EquipTitle(Achievements.EquippedTitle == def ? null : def);
        }

        private void Refresh()
        {
            var equipped = Achievements.EquippedTitle;
            var unlockedCount = Achievements.UnlockedCount;
            var total = Achievements.All.Length;
            _title.text = Loc.T("업적 · 칭호") + (SteamBridge.Initialized ? Loc.T("   <size=12><color=#8FC8FF>스팀 연동됨</color></size>") : "");
            _summary.text = Loc.F("달성 <color=#FFD966>{0}</color> / {1}", unlockedCount, total) + "      " +
                            Loc.F("장착한 칭호: {0}", equipped != null
                                ? $"<color=#FFD966>「{equipped.Title}」</color> <color=#C8CCD6>{equipped.EffectText}</color>"
                                : Loc.T("<color=#888888>없음</color>"));
            _progressFill.anchorMax = new Vector2(total > 0 ? unlockedCount / (float)total : 0f, 1f);

            for (var t = 0; t < _tabs.Length; t++)
            {
                var on = (int)_tab == t;
                _tabs[t].Image.color = _tabs[t].Image.sprite != null ? PixelUi.Tint(on ? TabOn : TabOff, 0.5f) : on ? TabOn : TabOff;
                _tabs[t].Text.color = on ? Color.white : new Color(0.62f, 0.65f, 0.72f);
            }
            _tabs[0].Text.text = Loc.F("업적  {0}/{1}", unlockedCount, total);
            _tabs[1].Text.text = Loc.F("칭호  {0}", unlockedCount);

            var count = _list.Count;
            _empty.gameObject.SetActive(count == 0);
            _hint.text = _tab == Tab.Titles
                ? Loc.T("W/S·마우스: 고르기   Enter·클릭: 장착/해제   A/D: 업적 탭   Esc: 닫기   ·   칭호 효과는 다음 던전 입장부터")
                : Loc.T("W/S·휠: 넘기기   A/D·탭 클릭: 칭호 탭(장착은 칭호 탭에서)   Esc: 닫기") +
                  (count > VisibleRows ? Loc.F("   ·   {0}~{1} / {2}", _scroll + 1, Mathf.Min(count, _scroll + VisibleRows), count) : "");

            for (var i = 0; i < _rows.Count; i++)
            {
                var (rect, image, left, right) = _rows[i];
                var index = _scroll + i;
                var visible = index < count;
                if (rect.gameObject.activeSelf != visible)
                    rect.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                var def = _list[index];
                var unlocked = Achievements.IsUnlocked(def);
                var isEquipped = equipped == def;
                var color = index == _cursor ? RowCursor : isEquipped ? RowEquipped : unlocked ? RowUnlocked : RowLocked;
                // 커서 줄은 틴트 없이 밝게(틴트 0.5면 다른 줄과 구분이 안 됐다)
                image.color = index == _cursor && image.sprite != null ? new Color(0.62f, 0.78f, 1f) : image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;
                var dim = unlocked ? "#C8CCD6" : "#7A7A82";

                if (_tab == Tab.Titles)
                {
                    left.text = $"<color=#FFD966><b>「{def.Title}」</b></color>   <color=#9AA0AA><size=13>{def.Name}</size></color>";
                    right.text = $"<color=#E6E8EE>{def.EffectText}</color>" + (isEquipped ? Loc.T("   <color=#FFD966><b>장착 중</b></color>") : "");
                    continue;
                }

                var mark = isEquipped ? Loc.T("<color=#FFD966>[장착]</color>") : unlocked ? Loc.T("<color=#8CE08C>[달성]</color>") : "<color=#666670>[ - ]</color>";
                if (def.Hidden && !unlocked)
                {
                    // 이름·조건·칭호·보상 전부 가린다(사용자 결정 - 조건도 모르게)
                    left.text = $"{mark}  <color=#7FE0D8><b>???</b></color>   <color=#7A7A82><size=13>???</size></color>";
                    right.text = "<color=#7A7A82>「???」  ???</color>";
                    continue;
                }
                left.text = $"{mark}  <color={(unlocked ? "#FFFFFF" : "#A0A0A8")}><b>{def.Name}</b></color>   <color={dim}><size=13>{def.Description}</size></color>";
                right.text = $"<color={(unlocked ? "#FFD966" : "#8C7A40")}>「{def.Title}」</color>  <color={dim}><size=13>{def.EffectText} · {def.RewardText}</size></color>";
            }
        }

        private void Build(Transform canvas)
        {
            // 창 뒤 화면 전체를 어둡게
            var dimmer = HudUi.CreateImage(canvas, "Dimmer", new Color(0f, 0f, 0f, 0.62f));
            HudUi.Stretch(dimmer.rectTransform, 0f);
            dimmer.raycastTarget = false;
            _root = dimmer.gameObject;

            // 업적이 늘어도 화면(높이 720)을 넘지 않게 - 줄 높이는 그대로 두고, 들어가는 줄 수만 보여 주며 스크롤한다
            var rowStep = RowHeight + RowGap;
            var count = Mathf.Min(Achievements.All.Length, Mathf.FloorToInt((MaxPanelHeight - HeaderHeight - FooterHeight) / rowStep));
            var height = HeaderHeight + count * rowStep + FooterHeight;

            var panelImage = HudUi.CreateImage(_root.transform, "AchievementPanel", new Color(0.05f, 0.05f, 0.07f, 0.97f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, height);
            var panel = panelImage.transform;
            if (!skinned)
                panelImage.gameObject.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            _title = TopText(panel, "Title", 22, Side + 4f, 12f, 520f, 30f);
            _title.fontStyle = FontStyle.Bold;
            _summary = TopText(panel, "Summary", 15, Side + 4f, 44f, 700f, 22f);

            // 달성률 막대(제목 오른쪽)
            var bar = HudUi.CreateImage(panel, "ProgressBar", new Color(0.12f, 0.12f, 0.15f, 1f));
            var barRect = bar.rectTransform;
            barRect.anchorMin = barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 1f);
            barRect.sizeDelta = new Vector2(240f, 10f);
            barRect.anchoredPosition = new Vector2(-64f, -24f);
            var fill = HudUi.CreateImage(bar.transform, "Fill", new Color(0.95f, 0.78f, 0.3f, 1f));
            _progressFill = fill.rectTransform;
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = _progressFill.offsetMax = Vector2.zero;

            // 탭 두 개(요약 줄 아래)
            _tabs = new (RectTransform, Image, Text)[2];
            for (var t = 0; t < 2; t++)
            {
                var tab = HudUi.CreateImage(panel, $"Tab{t}", TabOff);
                PixelUi.Slice(tab, "Button");
                var tabRect = tab.rectTransform;
                tabRect.anchorMin = tabRect.anchorMax = new Vector2(0f, 1f);
                tabRect.pivot = new Vector2(0f, 1f);
                tabRect.sizeDelta = new Vector2(160f, 26f);
                tabRect.anchoredPosition = new Vector2(Side + t * 168f, -68f);
                var label = HudUi.CreateText(tab.transform, "Label", 15, TextAnchor.MiddleCenter);
                label.fontStyle = FontStyle.Bold;
                HudUi.Stretch(label.rectTransform, 0f);
                _tabs[t] = (tabRect, tab, label);
            }

            var close = HudUi.CreateImage(panel, "CloseButton", new Color(0.45f, 0.18f, 0.18f, 1f));
            if (PixelUi.Slice(close, "Button"))
                close.color = new Color(1f, 0.6f, 0.6f);
            _closeButton = close.rectTransform;
            _closeButton.anchorMin = _closeButton.anchorMax = new Vector2(1f, 1f);
            _closeButton.pivot = new Vector2(1f, 1f);
            _closeButton.sizeDelta = new Vector2(32f, 32f);
            _closeButton.anchoredPosition = new Vector2(-14f, -12f);
            var x = HudUi.CreateText(close.transform, "X", 17, TextAnchor.MiddleCenter);
            x.text = "X";
            HudUi.Stretch(x.rectTransform, 0f);

            for (var i = 0; i < count; i++)
            {
                var image = HudUi.CreateImage(panel, $"Row{i}", RowLocked);
                PixelUi.Slice(image, "Button");
                var rowRect = image.rectTransform;
                rowRect.anchorMin = new Vector2(0f, 1f);
                rowRect.anchorMax = new Vector2(1f, 1f);
                rowRect.pivot = new Vector2(0.5f, 1f);
                var top = HeaderHeight + i * rowStep;
                rowRect.offsetMin = new Vector2(Side, -(top + RowHeight));
                rowRect.offsetMax = new Vector2(-Side, -top);
                var left = RowText(image.transform, "Left", TextAnchor.MiddleLeft);
                left.rectTransform.offsetMin = new Vector2(14f, 0f);
                left.rectTransform.offsetMax = new Vector2(-380f, 0f);
                var right = RowText(image.transform, "Right", TextAnchor.MiddleRight);
                right.rectTransform.offsetMin = new Vector2(560f, 0f);
                right.rectTransform.offsetMax = new Vector2(-14f, 0f);
                _rows.Add((rowRect, image, left, right));
            }

            _empty = HudUi.CreateText(panel, "Empty", 16, TextAnchor.MiddleCenter);
            _empty.text = Loc.T("아직 얻은 칭호가 없다 - 업적을 달성하면 칭호를 얻는다");
            _empty.color = new Color(0.62f, 0.65f, 0.72f);
            HudUi.Stretch(_empty.rectTransform, 0f);

            _hint = HudUi.CreateText(panel, "Hint", 13, TextAnchor.MiddleCenter);
            _hint.color = new Color(0.66f, 0.69f, 0.76f);
            var hintRect = _hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(Side, 6f);
            hintRect.offsetMax = new Vector2(-Side, FooterHeight - 4f);

            _root.SetActive(false);
        }

        private static Text TopText(Transform panel, string name, int size, float x, float top, float width, float height)
        {
            var text = HudUi.CreateText(panel, name, size, TextAnchor.MiddleLeft);
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var r = text.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(width, height);
            r.anchoredPosition = new Vector2(x, -top);
            return text;
        }

        private static Text RowText(Transform row, string name, TextAnchor anchor)
        {
            var text = HudUi.CreateText(row, name, 15, anchor);
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            HudUi.Stretch(text.rectTransform, 0f);
            return text;
        }
    }
}
