using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비의 특성 창(11-02 사용자 - T로 연다). 4갈래 × 5칸 격자, 아래에 고른 칸 설명. 방향키·WASD·마우스로 고르고 Enter·Space·클릭 = 배우기
    /// (이미 배운 칸이면 되돌리기 - 같은 갈래 뒤 칸도 같이), R = 전부 되돌리기, Esc·X = 닫기. 업적 창(AchievementPanel)처럼 EventSystem 없이
    /// 직접 입력을 읽고 자체 캔버스에 그린다. 아직 없는 칸은 "준비 중"으로 잠가 둔다.</summary>
    public class TraitPanel : MonoBehaviour
    {
        private const float PanelWidth = 960f;
        private const float PanelHeight = 600f;
        private const float Header = 52f;
        private const float ColumnWidth = 210f;
        private const float ColumnGap = 16f;
        private const float BranchTitleHeight = 28f;
        private const float CellHeight = 58f;
        private const float CellGap = 10f;
        private const float GridTop = Header + BranchTitleHeight + 10f;
        private const float DescHeight = 96f;

        private static readonly Color CellEmpty = new Color(0.07f, 0.07f, 0.08f, 0.9f);
        private static readonly Color CellLocked = new Color(0.11f, 0.11f, 0.13f, 0.95f);
        private static readonly Color CellAvailable = new Color(0.17f, 0.23f, 0.34f, 0.97f);
        private static readonly Color CellLearned = new Color(0.2f, 0.42f, 0.3f, 1f);
        private static readonly Color LinkOff = new Color(0.2f, 0.2f, 0.24f, 1f);
        private static readonly Color LinkOn = new Color(0.45f, 0.85f, 0.55f, 1f);

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private Text _header;
        private Text _desc;
        private RectTransform _closeButton;
        private readonly RectTransform[,] _cellRects = new RectTransform[Traits.BranchCount, Traits.TierCount];
        private readonly Image[,] _cellImages = new Image[Traits.BranchCount, Traits.TierCount];
        private readonly Text[,] _cellTexts = new Text[Traits.BranchCount, Traits.TierCount];
        private readonly Image[,] _links = new Image[Traits.BranchCount, Traits.TierCount - 1]; // 칸 사이 잇는 줄
        private readonly Text[] _branchTitles = new Text[Traits.BranchCount];
        private int _branch;
        private int _tier;
        private int _openedFrame = -1;
        private string _notice;      // 방금 한 일(배움·되돌림·못 배우는 이유) - 설명 아래 한 줄
        private Color _noticeColor;

        public static TraitPanel Create()
        {
            var canvasGo = new GameObject("TraitCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55; // 업적·도감 창과 같은 층(동시에 열리지 않는다)
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = canvasGo.AddComponent<TraitPanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        public void Open()
        {
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _notice = null;
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

            if (GameInput.Down(Key.Escape) || GameInput.Down(Key.T))
            {
                Close();
                return;
            }
            if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A))
                _branch = (_branch + Traits.BranchCount - 1) % Traits.BranchCount;
            if (GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D))
                _branch = (_branch + 1) % Traits.BranchCount;
            if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                _tier = (_tier + Traits.TierCount - 1) % Traits.TierCount;
            if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                _tier = (_tier + 1) % Traits.TierCount;
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                Toggle(_branch, _tier);
            if (GameInput.Down(Key.R))
                ResetAll();

            if (Pointer.current != null)
            {
                var pos = GameInput.PointerPosition;
                var moved = GameInput.PointerDelta.sqrMagnitude > 0f;
                if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                for (var b = 0; b < Traits.BranchCount; b++)
                for (var t = 0; t < Traits.TierCount; t++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_cellRects[b, t], pos, null))
                        continue;
                    if (moved || GameInput.PointerDown)
                    {
                        _branch = b;
                        _tier = t;
                    }
                    if (GameInput.PointerDown)
                        Toggle(b, t);
                }
            }

            Refresh();
        }

        private void Toggle(int branch, int tier)
        {
            var def = Traits.At(branch, tier);
            if (def == null)
            {
                SetNotice(Loc.T("아직 준비 중인 칸이다"), new Color(0.6f, 0.6f, 0.65f));
                return;
            }
            var state = Traits.GetState(def);
            switch (state)
            {
                case Traits.State.Learned:
                    var count = Traits.Unlearn(def);
                    SetNotice(count > 1 ? Loc.F("{0} 외 {1}칸을 되돌렸다", def.Name, count - 1) : Loc.F("{0}을(를) 되돌렸다", def.Name),
                        new Color(0.85f, 0.85f, 0.9f));
                    SfxPlayer.Play(Sfx.Block);
                    break;
                case Traits.State.Available:
                    Traits.Learn(def);
                    SetNotice(Loc.F("{0}을(를) 배웠다!", def.Name), new Color(0.55f, 0.95f, 0.6f));
                    SfxPlayer.Play(Sfx.GradeUp);
                    break;
                default:
                    SetNotice(StateText(state), new Color(1f, 0.6f, 0.5f));
                    break;
            }
        }

        private void ResetAll()
        {
            if (Traits.SpentPoints == 0)
                return;
            Traits.ResetAll();
            SetNotice(Loc.T("특성을 전부 되돌렸다 - 포인트가 모두 돌아왔다"), new Color(0.85f, 0.85f, 0.9f));
            SfxPlayer.Play(Sfx.Block);
        }

        private void SetNotice(string text, Color color)
        {
            _notice = text;
            _noticeColor = color;
        }

        private static string StateText(Traits.State state) => state switch
        {
            Traits.State.Learned => Loc.T("배움"),
            Traits.State.Available => Loc.T("배울 수 있음"),
            Traits.State.NeedPrevious => Loc.T("앞 칸을 먼저 배워야 한다"),
            Traits.State.SignatureTaken => Loc.F("대표 특성은 {0}개까지 배울 수 있다", Traits.MaxSignatures),
            Traits.State.CombatLocked => Loc.T("전투 갈래는 하나만 3번째 칸부터 - 다른 전투 갈래는 2번째 칸까지"),
            _ => Loc.T("포인트가 부족하다"),
        };

        private void Refresh()
        {
            _header.text = Loc.F("<b>특성</b>   남은 포인트 <color=#8CE08C>{0}</color> / {1}", Traits.FreePoints, Traits.EarnedPoints) +
                           Loc.F("   <size=12><color=#9AA0AA>보스 첫 처치 {0} · 정예 {1}/{2} · 칸 비용 1·1·2·2·3</color></size>",
                               Traits.BossPoints, Traits.ElitePoints, Traits.ElitePointCap);

            for (var b = 0; b < Traits.BranchCount; b++)
            {
                _branchTitles[b].text = Traits.BranchName(b);
                for (var t = 0; t < Traits.TierCount; t++)
                {
                    var def = Traits.At(b, t);
                    var cursor = b == _branch && t == _tier;
                    Color color;
                    string text;
                    var signature = t == Traits.SignatureTier ? Loc.T("<color=#B9A0FF>★ 대표</color> ") : string.Empty;
                    if (def == null)
                    {
                        color = CellEmpty;
                        text = $"{signature}<color=#5A5A62>{t + 1}  {Loc.T("준비 중")}</color>";
                    }
                    else
                    {
                        var state = Traits.GetState(def);
                        color = state == Traits.State.Learned ? CellLearned : state == Traits.State.Available ? CellAvailable : CellLocked;
                        var nameColor = state == Traits.State.Learned ? "#FFFFFF" : state == Traits.State.Available ? "#E6ECF5" : "#8A8A92";
                        var mark = state == Traits.State.Learned ? Loc.T("<color=#8CE08C>배움</color>") : state == Traits.State.Available
                            ? Loc.T("<color=#9FC4FF>배울 수 있음</color>") : "<color=#6A6A72>-</color>";
                        var cost = state == Traits.State.Learned ? string.Empty : Loc.F("  <color=#FFD966>{0}점</color>", Traits.Cost(def));
                        text = $"{signature}<color={nameColor}><b>{t + 1}  {def.Name}</b></color>\n<size=11>{mark}{cost}</size>";
                    }
                    if (cursor)
                        color = Color.Lerp(color, new Color(0.55f, 0.65f, 0.85f, 1f), 0.45f);
                    var image = _cellImages[b, t];
                    image.color = image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;
                    _cellTexts[b, t].text = text;

                    if (t < Traits.TierCount - 1)
                    {
                        var next = Traits.At(b, t + 1);
                        _links[b, t].color = def != null && next != null && Traits.Has(def.Type) && Traits.Has(next.Type) ? LinkOn : LinkOff;
                    }
                }
            }

            var selected = Traits.At(_branch, _tier);
            string desc;
            if (selected == null)
                desc = Loc.F("<b>{0} {1}번째 칸</b>\n<color=#8A8A92>아직 준비 중인 특성이다.</color>", Traits.BranchName(_branch), _tier + 1);
            else
            {
                var state = Traits.GetState(selected);
                var stateColor = state == Traits.State.Learned ? "#8CE08C" : state == Traits.State.Available ? "#9FC4FF" : "#FF9A80";
                desc = $"<b>{selected.Name}</b>   <size=13><color={stateColor}>{StateText(state)}</color></size>\n" +
                       $"<color=#C8CCD6>{selected.Description}</color>";
                if (selected.Tier == Traits.SignatureTier)
                    desc += Loc.F("\n<size=12><color=#B9A0FF>대표 특성 - 모든 갈래를 통틀어 {0}개까지 배울 수 있다</color></size>", Traits.MaxSignatures);
            }
            if (!string.IsNullOrEmpty(_notice))
                desc += $"\n<size=13><color=#{ColorUtility.ToHtmlStringRGB(_noticeColor)}>{_notice}</color></size>";
            _desc.text = desc;
        }

        private void Build(Transform canvas)
        {
            var panelImage = HudUi.CreateImage(canvas, "TraitPanel", new Color(0.05f, 0.05f, 0.07f, 0.96f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            _header = HudUi.CreateText(_panel.transform, "Header", 18, TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            var headerRect = _header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(24f, -Header);
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

            var gridWidth = Traits.BranchCount * ColumnWidth + (Traits.BranchCount - 1) * ColumnGap;
            var left = (PanelWidth - gridWidth) * 0.5f;
            for (var b = 0; b < Traits.BranchCount; b++)
            {
                var colX = left + b * (ColumnWidth + ColumnGap);
                var title = HudUi.CreateText(_panel.transform, $"Branch{b}", 16, TextAnchor.MiddleCenter);
                title.color = new Color(0.9f, 0.92f, 1f);
                title.fontStyle = FontStyle.Bold;
                Place(title.rectTransform, colX, Header, ColumnWidth, BranchTitleHeight);
                _branchTitles[b] = title;

                for (var t = 0; t < Traits.TierCount; t++)
                {
                    var top = GridTop + t * (CellHeight + CellGap);
                    if (t < Traits.TierCount - 1)
                    {
                        var link = HudUi.CreateImage(_panel.transform, $"Link{b}_{t}", LinkOff);
                        Place(link.rectTransform, colX + ColumnWidth * 0.5f - 2f, top + CellHeight, 4f, CellGap);
                        _links[b, t] = link;
                    }
                    var cell = HudUi.CreateImage(_panel.transform, $"Cell{b}_{t}", CellLocked);
                    PixelUi.Slice(cell, "Button");
                    Place(cell.rectTransform, colX, top, ColumnWidth, CellHeight);
                    var text = HudUi.CreateText(cell.transform, "Text", 14, TextAnchor.MiddleLeft);
                    text.supportRichText = true;
                    text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                    HudUi.Stretch(text.rectTransform, 0f);
                    text.rectTransform.offsetMin = new Vector2(12f, 0f);
                    text.rectTransform.offsetMax = new Vector2(-8f, 0f);
                    _cellRects[b, t] = cell.rectTransform;
                    _cellImages[b, t] = cell;
                    _cellTexts[b, t] = text;
                }
            }

            var descTop = GridTop + Traits.TierCount * (CellHeight + CellGap) + 6f;
            var descBox = HudUi.CreateImage(_panel.transform, "DescBox", new Color(0.09f, 0.09f, 0.11f, 0.95f));
            PixelUi.Slice(descBox, "Button");
            descBox.color = descBox.sprite != null ? PixelUi.Tint(new Color(0.09f, 0.09f, 0.11f, 0.95f), 0.5f) : descBox.color;
            Place(descBox.rectTransform, left, descTop, gridWidth, DescHeight);
            _desc = HudUi.CreateText(descBox.transform, "Desc", 15, TextAnchor.UpperLeft);
            _desc.supportRichText = true;
            HudUi.Stretch(_desc.rectTransform, 0f);
            _desc.rectTransform.offsetMin = new Vector2(14f, 8f);
            _desc.rectTransform.offsetMax = new Vector2(-14f, -10f);

            var hint = HudUi.CreateText(_panel.transform, "Hint", 12, TextAnchor.MiddleCenter);
            hint.text = Loc.T("방향키·마우스: 고르기   Enter·클릭: 배우기 / 배운 칸은 되돌리기   R: 전부 되돌리기   T·Esc: 닫기   ·   다음 던전 입장부터 적용");
            hint.color = new Color(0.6f, 0.63f, 0.7f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(20f, 6f);
            hintRect.offsetMax = new Vector2(-20f, 30f);

            _panel.SetActive(false);
        }

        /// <summary>패널 왼쪽 위 기준 (x, top)에 w×h로 놓는다.</summary>
        private static void Place(RectTransform rect, float x, float top, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
