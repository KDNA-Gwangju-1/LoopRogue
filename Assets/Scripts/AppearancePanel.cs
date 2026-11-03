using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>재봉사 외형 창(11-03 사용자) - 방어구 외형·무기 외형·이펙트 세 줄, 줄마다 [장비] + 등급 10칸.
    /// 얻어 본 등급만 고를 수 있고(나머지는 ?), 왼쪽에 지금 고른 모습(남쪽 대기 몸+무기 겹 + 고른 이펙트의 베기 그림)을 보여 준다.
    /// ↑↓ 줄, ←→ 칸, Enter·Space·클릭 = 고르기, Esc·9 = 닫기. 업적·특성 창처럼 EventSystem 없이 자체 캔버스.</summary>
    public class AppearancePanel : MonoBehaviour
    {
        private const float PanelWidth = 980f;
        private const float PanelHeight = 470f;
        private const int Columns = 11; // 0 = 장비 따라감, 1~10 = 등급
        private const float CellW = 62f;
        private const float CellH = 52f;
        private const float GridLeft = 300f;
        private const float RowTop = 110f;
        private const float RowStep = 92f;

        private static readonly Appearance.Part[] Parts = { Appearance.Part.Armor, Appearance.Part.Weapon, Appearance.Part.Effect };

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private readonly Image[,] _cells = new Image[3, Columns];
        private readonly Text[,] _cellTexts = new Text[3, Columns];
        private readonly Text[] _rowNotes = new Text[3];
        private Image _previewBody, _previewWeapon, _previewFx;
        private Text _notice;
        private RectTransform _closeButton;
        private int _row, _col;
        private int _openedFrame = -1;

        public static AppearancePanel Create()
        {
            var canvasGo = new GameObject("AppearanceCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = canvasGo.AddComponent<AppearancePanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        public void Open()
        {
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _notice.text = string.Empty;
            _row = 0;
            _col = Mathf.Clamp(Appearance.Choice(Parts[0]) + 1, 0, ColumnsFor(Parts[0]) - 1);
            _panel.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            IsOpen = false;
            _closedFrame = Time.frameCount;
            _panel.SetActive(false);
        }

        private static bool Selectable(Appearance.Part part, int col) =>
            col == 0 || Appearance.IsOwned(Appearance.SlotOf(part), (ItemGrade)(col - 1));

        /// <summary>그 줄의 칸 수 - Unknown 칸은 그 부위 Unknown을 얻은 뒤에만 생긴다(11-03 사용자 - 히든 등급이라 ?로도 안 보이게).
        /// 이펙트 줄은 무기 쪽 기록(Appearance.SlotOf).</summary>
        private static int ColumnsFor(Appearance.Part part) =>
            Appearance.IsOwned(Appearance.SlotOf(part), ItemGrade.Unknown) ? Columns : Columns - 1;

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;
            if (GameInput.Down(Key.Escape) || GameInput.Down(Key.Digit9))
            {
                Close();
                return;
            }
            if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                _row = (_row + 2) % 3;
            if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                _row = (_row + 1) % 3;
            var cols = ColumnsFor(Parts[_row]);
            _col = Mathf.Min(_col, cols - 1); // Unknown 칸이 없는 줄로 옮겨 오면 끝 칸으로
            if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A))
                _col = (_col + cols - 1) % cols;
            if (GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D))
                _col = (_col + 1) % cols;
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                Pick(_row, _col);

            if (Pointer.current != null)
            {
                var pos = GameInput.PointerPosition;
                if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                var moved = GameInput.PointerDelta.sqrMagnitude > 0f;
                for (var r = 0; r < 3; r++)
                for (var c = 0; c < Columns; c++)
                {
                    if (!_cells[r, c].gameObject.activeSelf || !RectTransformUtility.RectangleContainsScreenPoint(_cells[r, c].rectTransform, pos, null))
                        continue;
                    if (moved || GameInput.PointerDown)
                    {
                        _row = r;
                        _col = c;
                    }
                    if (GameInput.PointerDown)
                        Pick(r, c);
                }
            }
            Refresh();
        }

        private void Pick(int row, int col)
        {
            var part = Parts[row];
            if (!Selectable(part, col))
            {
                _notice.text = Loc.T("<color=#FF9A80>아직 얻어 본 적 없는 등급이다 - 뽑기로 한 번이라도 얻으면 고를 수 있다</color>");
                return;
            }
            Appearance.SetChoice(part, col - 1);
            SfxPlayer.Play(Sfx.GradeUp);
            _notice.text = col == 0
                ? Loc.F("<color=#8CE08C>{0}: 장착한 장비를 따라간다</color>", PartName(part))
                : Loc.F("<color=#8CE08C>{0}: {1}</color>", PartName(part), EquipmentData.Grades[(ItemGrade)(col - 1)].Name);
        }

        private static string PartName(Appearance.Part part) => part switch
        {
            Appearance.Part.Armor => Loc.T("방어구 외형"),
            Appearance.Part.Weapon => Loc.T("무기 외형"),
            _ => Loc.T("공격·스킬 이펙트"),
        };

        private void Refresh()
        {
            var blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            for (var r = 0; r < 3; r++)
            {
                var part = Parts[r];
                var choice = Appearance.Choice(part) + 1; // 0 = 장비
                var cols = ColumnsFor(part);
                for (var c = 0; c < Columns; c++)
                {
                    var shown = c < cols; // Unknown 칸은 얻은 뒤에만
                    if (_cells[r, c].gameObject.activeSelf != shown)
                        _cells[r, c].gameObject.SetActive(shown);
                    if (!shown)
                        continue;
                    var ok = Selectable(part, c);
                    var picked = c == choice;
                    var cursor = r == _row && c == _col;
                    var baseColor = picked ? new Color(0.25f, 0.45f, 0.3f) : ok ? new Color(0.17f, 0.2f, 0.27f) : new Color(0.1f, 0.1f, 0.12f);
                    if (cursor)
                        baseColor = Color.Lerp(baseColor, new Color(0.6f, 0.65f, 0.8f), 0.35f * blink);
                    _cells[r, c].color = _cells[r, c].sprite != null ? PixelUi.Tint(baseColor, 0.5f) : baseColor;
                    string label;
                    if (c == 0)
                        label = Loc.T("장비");
                    else if (!ok)
                        label = "<color=#55555E>?</color>";
                    else
                    {
                        var g = (ItemGrade)(c - 1);
                        label = EquipmentData.Colorize(g, EquipmentData.Grades[g].Name);
                    }
                    _cellTexts[r, c].text = picked ? $"<b>{label}</b>" : label;
                }
                var resolved = Appearance.Resolved(part);
                _rowNotes[r].text = Loc.F("지금: {0}", EquipmentData.Colorize(resolved, EquipmentData.Grades[resolved].Name)) +
                                    (Appearance.Choice(part) < 0 ? $"  <color=#8A8A92>({Loc.T("장비 따라감")})</color>" : string.Empty);
            }
            // 미리보기 - 고른 외형의 남쪽 대기 첫 장 + 고른 이펙트의 베기 그림
            _previewBody.sprite = Resources.Load<Sprite>($"Sprites/HeroBody{(int)Appearance.Resolved(Appearance.Part.Armor)}/idle_south_0");
            _previewWeapon.sprite = Resources.Load<Sprite>($"Sprites/HeroWeapon{(int)Appearance.Resolved(Appearance.Part.Weapon)}/idle_south_0");
            _previewFx.sprite = Resources.Load<Sprite>($"Effects/slash_g{(int)Appearance.Resolved(Appearance.Part.Effect)}/1");
            _previewBody.enabled = _previewBody.sprite != null;
            _previewWeapon.enabled = _previewWeapon.sprite != null;
            _previewFx.enabled = _previewFx.sprite != null;
        }

        private void Build(Transform canvas)
        {
            var panelImage = HudUi.CreateImage(canvas, "AppearancePanel", new Color(0.05f, 0.05f, 0.07f, 0.97f));
            PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel = panelImage.gameObject;
            var root = _panel.transform;

            var title = Label(root, 22, TextAnchor.MiddleLeft, 28f, 14f, 600f, 40f);
            title.text = Loc.T("<b>재봉사</b>   <size=14><color=#9AA0AA>얻어 본 등급의 외형·이펙트로 갈아입는다 (능력치는 장착한 장비 그대로)</color></size>");

            var close = HudUi.CreateImage(root, "Close", new Color(0.45f, 0.18f, 0.18f, 1f));
            if (PixelUi.Slice(close, "Button"))
                close.color = new Color(1f, 0.6f, 0.6f);
            _closeButton = close.rectTransform;
            _closeButton.anchorMin = _closeButton.anchorMax = new Vector2(1f, 1f);
            _closeButton.pivot = new Vector2(1f, 1f);
            _closeButton.sizeDelta = new Vector2(32f, 32f);
            _closeButton.anchoredPosition = new Vector2(-14f, -12f);
            var x = HudUi.CreateText(close.transform, "X", 16, TextAnchor.MiddleCenter);
            x.text = "X";
            HudUi.Stretch(x.rectTransform, 0f);

            // 미리보기(왼쪽) - 용사 64px 그림을 4배, 베기 그림은 오른쪽 아래 작게
            var frame = HudUi.CreateImage(root, "PreviewFrame", new Color(0.09f, 0.09f, 0.11f, 0.95f));
            PixelUi.Slice(frame, "Slot");
            Place(frame.rectTransform, 28f, 70f, 250f, 340f);
            _previewBody = HudUi.CreateImage(frame.transform, "Body", Color.white);
            Place(_previewBody.rectTransform, -3f, 30f, 256f, 256f);
            _previewWeapon = HudUi.CreateImage(_previewBody.transform, "Weapon", Color.white);
            HudUi.Stretch(_previewWeapon.rectTransform, 0f);
            _previewFx = HudUi.CreateImage(frame.transform, "Fx", Color.white);
            Place(_previewFx.rectTransform, 150f, 250f, 84f, 84f);
            foreach (var img in new[] { _previewBody, _previewWeapon, _previewFx })
                img.preserveAspect = true;

            for (var r = 0; r < 3; r++)
            {
                var top = RowTop + r * RowStep;
                var name = Label(root, 17, TextAnchor.MiddleLeft, GridLeft, top - 34f, 300f, 28f);
                name.fontStyle = FontStyle.Bold;
                name.text = PartName(Parts[r]);
                _rowNotes[r] = Label(root, 14, TextAnchor.MiddleRight, GridLeft + 260f, top - 34f, Columns * CellW - 260f, 28f);
                for (var c = 0; c < Columns; c++)
                {
                    var cell = HudUi.CreateImage(root, $"Cell{r}_{c}", Color.white);
                    PixelUi.Slice(cell, "Button");
                    Place(cell.rectTransform, GridLeft + c * CellW, top, CellW - 4f, CellH);
                    var t = HudUi.CreateText(cell.transform, "T", 13, TextAnchor.MiddleCenter);
                    t.supportRichText = true;
                    t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                    HudUi.Stretch(t.rectTransform, 2f);
                    _cells[r, c] = cell;
                    _cellTexts[r, c] = t;
                }
            }

            _notice = Label(root, 14, TextAnchor.MiddleLeft, GridLeft, RowTop + 3 * RowStep - 26f, 680f, 26f);
            var hint = Label(root, 12, TextAnchor.MiddleCenter, 0f, PanelHeight - 34f, PanelWidth, 26f);
            hint.color = new Color(0.55f, 0.58f, 0.66f);
            hint.text = Loc.T("↑↓ 줄 · ←→ 칸 · Enter·클릭 고르기 · 9·Esc 닫기   (? = 아직 얻어 본 적 없는 등급)");
            _panel.SetActive(false);
        }

        private static Text Label(Transform parent, int size, TextAnchor anchor, float x, float top, float w, float h)
        {
            var t = HudUi.CreateText(parent, "Label", size, anchor);
            t.supportRichText = true;
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            Place(t.rectTransform, x, top, w, h);
            return t;
        }

        private static void Place(RectTransform rect, float x, float top, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
