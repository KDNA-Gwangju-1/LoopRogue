using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비의 특성 창(11-02 사용자 - T로 연다). 4갈래 × 5칸 격자, 아래에 고른 칸 설명. 방향키·WASD·마우스로 고르고 Enter·Space·클릭 = 배우기
    /// (이미 배운 칸이면 되돌리기 - 같은 갈래 뒤 칸도 같이), R = 전부 되돌리기, Esc·X = 닫기. 업적 창(AchievementPanel)처럼 EventSystem 없이
    /// 직접 입력을 읽고 자체 캔버스에 그린다.
    /// 11-03 도트 단장(사용자 시안 OK): 갈래 색(대시 하늘·회전 주황빨강·탐색 초록·상인 구리) + 칸마다 12px 아이콘(UI/Icons/Trait_*, 잠긴 칸은 _Off 회색)
    /// + 갈래 문장(TraitBranch_N) + 포인트 보석 + 고른 칸 꺾쇠. 테두리·보석·꺾쇠·체크는 코드로 만든 4배 도트 그림이라 파일이 없어도 되고,
    /// 아이콘 그림이 없으면 아이콘만 빠진다. 배치 단위 = 그림 1px(4유닛), 시안 trait_panel_mock.py와 같은 좌표.</summary>
    public class TraitPanel : MonoBehaviour
    {
        private const float Px = 4f; // 그림 1px = UI 4유닛
        private const float PanelWidth = 240f * Px;
        private const float PanelHeight = 150f * Px;
        private const float Left = 10f * Px;
        private const float ColumnWidth = 52f * Px;
        private const float ColumnGap = 4f * Px;
        private const float BannerTop = 14f * Px;
        private const float BannerHeight = 11f * Px;
        private const float RowTop = 29f * Px;
        private const float RowHeight = 14f * Px;
        private const float RowStep = 17f * Px;
        private const float DescTop = 116f * Px;
        private const float DescHeight = 26f * Px;
        private const float PipLeft = 52f * Px;
        private const float PipStep = 6f * Px;
        private const int PipMax = 18; // 포인트 최대(보스 9 + 정예 9) - 테스트 보너스로 넘치면 숫자만

        // 갈래 색(밝음·중간·어두움) - Traits 갈래 순서(대시·회전·탐색·상인)
        private static readonly Color32[,] BranchColors =
        {
            { new Color32(126, 214, 240, 255), new Color32(58, 150, 196, 255), new Color32(30, 84, 120, 255) },
            { new Color32(246, 150, 96, 255), new Color32(204, 86, 58, 255), new Color32(118, 44, 34, 255) },
            { new Color32(150, 222, 116, 255), new Color32(74, 158, 80, 255), new Color32(36, 86, 48, 255) },
            { new Color32(232, 176, 120, 255), new Color32(176, 112, 66, 255), new Color32(98, 58, 34, 255) },
        };
        private static readonly Color32 Ink = new Color32(8, 8, 12, 255);
        private static readonly Color32 Bg = new Color32(13, 13, 17, 255);
        private static readonly Color32 Purple = new Color32(185, 150, 255, 255);
        private static readonly Color32 PurpleDark = new Color32(96, 64, 160, 255);
        private static readonly Color32 PurpleOff = new Color32(70, 60, 96, 255);
        private static readonly Color32 PurpleOffDark = new Color32(40, 34, 56, 255);
        private static readonly Color32 LockedFill = new Color32(20, 20, 26, 255);
        private static readonly Color32 LockedLight = new Color32(44, 46, 56, 255);
        private static readonly Color32 LockedEdge = new Color32(40, 42, 50, 255);
        private static readonly Color32 LockedDark = new Color32(28, 30, 36, 255);

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private sealed class Cell
        {
            public RectTransform Hit;
            public Image NameFrame;
            public Image IconFrame;
            public Image Icon;
            public Image Check;
            public Text Name;
            public Text Sub;
            public Image Link; // 아래 칸으로 잇는 줄(마지막 칸은 없음)
        }

        private GameObject _panel;
        private Text _points;
        private Text _info;
        private Text _desc;
        private Image _descIconFrame;
        private Image _descIcon;
        private RectTransform _closeButton;
        private readonly Image[] _pips = new Image[PipMax];
        private readonly Cell[,] _cells = new Cell[Traits.BranchCount, Traits.TierCount];
        private readonly Image[] _cursor = new Image[4];
        private readonly Image[] _banners = new Image[Traits.BranchCount];
        private readonly RectTransform[] _columns = new RectTransform[Traits.BranchCount]; // 갈래 한 줄(띠 + 칸 5개) - 고르지 않은 갈래는 숨긴다
        private readonly Text[] _bannerNames = new Text[Traits.BranchCount];
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

        /// <summary>T·허브 버튼 - 고른 갈래만 보인다(아직 안 고른 무리는 숨김, 하나도 안 골랐으면 스킬 도우미 안내).</summary>
        public void Open() => OpenCore(false);

        /// <summary>로비 [8] 스킬 도우미(11-03 사용자) - 아직 안 고른 무리의 두 갈래를 보여 주고 하나를 고르게 한다(전투 먼저, 이어서 생활).</summary>
        public void OpenChoice() => OpenCore(true);

        private bool _choiceMode;

        private void OpenCore(bool choice)
        {
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _notice = null;
            _choiceMode = choice && Traits.PendingChoice.HasValue;
            var pending = Traits.PendingChoice;
            if (_choiceMode && Traits.IsCombatBranch(_branch) != pending.Value)
            {
                _branch = pending.Value ? 0 : 2; // 아직 안 고른 무리의 첫 갈래에 커서
                _tier = 0;
            }
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
            var pending = _choiceMode ? Traits.PendingChoice : null;
            if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A))
                _branch = pending.HasValue ? OtherInGroup(_branch, pending.Value) : StepVisible(_branch, -1);
            if (GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D))
                _branch = pending.HasValue ? OtherInGroup(_branch, pending.Value) : StepVisible(_branch, 1);
            if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                _tier = (_tier + Traits.TierCount - 1) % Traits.TierCount;
            if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                _tier = (_tier + 1) % Traits.TierCount;
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                Press(_branch, _tier);
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
                {
                    if (!IsVisible(b))
                        continue;
                    if (GameInput.PointerDown && RectTransformUtility.RectangleContainsScreenPoint(_banners[b].rectTransform, pos, null))
                    {
                        _branch = b;
                        Press(b, _tier);
                    }
                }
                for (var b = 0; b < Traits.BranchCount; b++)
                for (var t = 0; t < Traits.TierCount; t++)
                {
                    if (!IsVisible(b) || !RectTransformUtility.RectangleContainsScreenPoint(_cells[b, t].Hit, pos, null))
                        continue;
                    if (moved || GameInput.PointerDown)
                    {
                        _branch = b;
                        _tier = t;
                    }
                    if (GameInput.PointerDown)
                        Press(b, t);
                }
            }

            Refresh();
        }

        /// <summary>같은 무리의 다른 갈래(대시-회전, 탐색-상인) - 고르는 중엔 좌우가 둘 사이만 오간다. 커서가 다른 무리에 있으면 그 무리 첫 갈래로.</summary>
        private static int OtherInGroup(int branch, bool combat)
        {
            if (Traits.IsCombatBranch(branch) != combat)
                return combat ? 0 : 2;
            return combat ? 1 - branch : 5 - branch;
        }

        /// <summary>고른 갈래만 보인다. 스킬 도우미로 연 창이면 지금 고르는 무리의 두 갈래도(11-03 사용자).</summary>
        private bool IsVisible(int branch)
        {
            var combat = Traits.IsCombatBranch(branch);
            var chosen = Traits.Chosen(combat);
            if (chosen >= 0)
                return chosen == branch;
            return _choiceMode && Traits.PendingChoice == combat;
        }

        private int StepVisible(int branch, int step)
        {
            for (var i = 1; i <= Traits.BranchCount; i++)
            {
                var b = ((branch + step * i) % Traits.BranchCount + Traits.BranchCount) % Traits.BranchCount;
                if (IsVisible(b))
                    return b;
            }
            return branch;
        }

        /// <summary>보이는 갈래만 가운데로 모아 놓는다 - 4줄은 원래 자리, 3줄·2줄은 사이를 넓혀 가운데.</summary>
        private void LayoutColumns()
        {
            var visible = new List<int>();
            for (var b = 0; b < Traits.BranchCount; b++)
            {
                var on = IsVisible(b);
                _columns[b].gameObject.SetActive(on);
                if (on)
                    visible.Add(b);
            }
            var gap = visible.Count >= 4 ? ColumnGap : visible.Count == 3 ? ColumnGap * 2f : ColumnGap * 4f;
            var width = visible.Count * ColumnWidth + (visible.Count - 1) * gap;
            var x = Mathf.Round((PanelWidth - width) * 0.5f / Px) * Px; // 도트 칸에 맞춘다
            foreach (var b in visible)
            {
                Place(_columns[b], x, 0f, ColumnWidth, PanelHeight);
                x += ColumnWidth + gap;
            }
            if (!IsVisible(_branch))
                _branch = visible.Count > 0 ? visible[0] : 0;
            _anyVisible = visible.Count > 0;
        }

        private bool _anyVisible;

        /// <summary>Enter·클릭 - 그 무리 갈래를 아직 안 골랐으면 이 갈래로 정하고, 골랐으면 배우기/되돌리기.</summary>
        private void Press(int branch, int tier)
        {
            var combat = Traits.IsCombatBranch(branch);
            if (Traits.Chosen(combat) < 0)
            {
                if (!_choiceMode)
                {
                    SetNotice(Loc.T("갈래는 로비의 [8] 스킬 도우미에게서 고른다"), new Color(1f, 0.85f, 0.4f));
                    return;
                }
                if (Traits.PendingChoice != combat)
                {
                    SetNotice(Loc.T("전투 갈래부터 고르자"), new Color(1f, 0.6f, 0.5f));
                    return;
                }
                Traits.Choose(branch);
                SfxPlayer.Play(Sfx.GradeUp);
                SetNotice(Loc.F("{0} 갈래를 골랐다 - 이 갈래만 배울 수 있다", Traits.BranchName(branch)), new Color(0.55f, 0.95f, 0.6f));
                var next = Traits.PendingChoice;
                if (next.HasValue)
                    _branch = next.Value ? 0 : 2; // 이어서 생활 갈래 고르기
                else
                    _choiceMode = false; // 다 골랐다 - 그대로 특성 창으로 쓴다
                _tier = 0;
                return;
            }
            Toggle(branch, tier);
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
            if (Traits.SpentPoints == 0 && Traits.Chosen(true) < 0 && Traits.Chosen(false) < 0)
                return; // 되돌릴 것도, 고른 갈래도 없다
            Traits.ResetAll();
            _branch = 0;
            _tier = 0;
            SetNotice(Loc.T("특성을 전부 되돌렸다 - 포인트가 모두 돌아왔다. 갈래는 [8] 스킬 도우미에게서 다시 고르자"), new Color(0.85f, 0.85f, 0.9f));
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
            Traits.State.BranchLocked => Loc.T("고르지 않은 갈래다 - R로 전부 되돌리면 다시 고를 수 있다"),
            Traits.State.NeedChoice => Loc.T("먼저 갈래를 골라야 한다"),
            _ => Loc.T("포인트가 부족하다"),
        };

        private static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);

        private void Refresh()
        {
            LayoutColumns();
            // 머리줄 - 보석: 쓴 포인트(어두운 초록) → 남은 포인트(밝은 초록) → 아직 못 얻음(빈 칸)
            var earned = Traits.EarnedPoints;
            var free = Traits.FreePoints;
            var spent = Mathf.Max(0, earned - free);
            for (var i = 0; i < PipMax; i++)
                _pips[i].sprite = PipSprite(i < spent ? 1 : i < spent + free ? 2 : 0);
            _points.text = $"<size=18><color=#8CE08C><b>{free}</b></color></size>  <color=#82889A>/ {earned}</color>";
            _info.text = Loc.F("보스 첫 처치 {0} · 정예 {1}/{2}", Traits.BossPoints, Traits.ElitePoints, Traits.ElitePointCap);

            var blink = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            var pending = _choiceMode ? Traits.PendingChoice : null;
            for (var b = 0; b < Traits.BranchCount; b++)
            {
                var light = BranchColors[b, 0];
                var mid = BranchColors[b, 1];
                var dark = BranchColors[b, 2];
                // 갈래 띠 - 고르는 중인 무리는 번쩍(커서 쪽이 더 밝게), 고르지 않은 갈래는 회색
                var groupChosen = Traits.Chosen(Traits.IsCombatBranch(b));
                if (groupChosen >= 0 && groupChosen != b)
                {
                    _banners[b].sprite = FrameSprite(LockedFill, LockedEdge, LockedDark);
                    _bannerNames[b].color = new Color32(110, 112, 122, 255);
                }
                else if (groupChosen < 0 && pending == Traits.IsCombatBranch(b))
                {
                    var glow = b == _branch ? blink : 0.3f;
                    _banners[b].sprite = FrameSprite(Mix(Mix(dark, Bg, 0.35f), mid, glow * 0.5f), Mix(mid, light, glow), Mix(dark, Ink, 0.4f));
                    _bannerNames[b].color = Color32.Lerp(light, new Color32(255, 255, 255, 255), b == _branch ? 0.5f : 0f);
                }
                else
                {
                    _banners[b].sprite = FrameSprite(Mix(dark, Bg, 0.35f), mid, Mix(dark, Ink, 0.4f));
                    _bannerNames[b].color = light;
                }
                for (var t = 0; t < Traits.TierCount; t++)
                {
                    var cell = _cells[b, t];
                    var def = Traits.At(b, t);
                    var state = def == null ? Traits.State.NeedPrevious : Traits.GetState(def);
                    var learned = def != null && state == Traits.State.Learned;
                    var available = def != null && state == Traits.State.Available;
                    var signature = t == Traits.SignatureTier;

                    Color32 fill, edgeLight, edgeDark;
                    if (learned) { fill = Mix(dark, Bg, 0.15f); edgeLight = light; edgeDark = dark; }
                    else if (available) { fill = Mix(dark, Bg, 0.6f); edgeLight = mid; edgeDark = Mix(dark, Ink, 0.3f); }
                    else { fill = LockedFill; edgeLight = LockedEdge; edgeDark = LockedDark; }
                    cell.NameFrame.sprite = FrameSprite(fill, edgeLight, edgeDark);
                    var iconFill = Mix(fill, Ink, 0.3f);
                    if (signature)
                        cell.IconFrame.sprite = learned || available ? FrameSprite(iconFill, Purple, PurpleDark) : FrameSprite(iconFill, PurpleOff, PurpleOffDark);
                    else
                        cell.IconFrame.sprite = FrameSprite(iconFill, learned || available ? edgeLight : LockedLight, edgeDark);

                    var icon = def == null ? null : PixelUi.Icon(learned || available ? $"Trait_{def.Type}" : $"Trait_{def.Type}_Off");
                    cell.Icon.sprite = icon;
                    cell.Icon.enabled = icon != null;
                    cell.Check.enabled = learned;

                    if (def == null)
                    {
                        cell.Name.text = $"<color=#5A5A62>{Loc.T("준비 중")}</color>";
                        cell.Sub.text = string.Empty;
                    }
                    else
                    {
                        var nameColor = learned ? "#FFFFFF" : available ? (signature ? "#DCC8FF" : "#E4EAF5") : "#6E7080";
                        cell.Name.text = $"<color={nameColor}>{def.Name}</color>";
                        string sub;
                        if (learned)
                            sub = $"<color=#8CE08C>{Loc.T("배움")}</color>";
                        else
                        {
                            var costColor = available ? "#FFD966" : "#64604F";
                            sub = $"<color={costColor}>{Loc.F("{0}점", Traits.Cost(def))}</color>";
                        }
                        if (signature)
                            sub = $"<color={(learned || available ? "#B996FF" : "#5A5078")}>{Loc.T("★ 대표")}</color>  " + sub;
                        cell.Sub.text = sub;
                    }

                    if (cell.Link != null)
                    {
                        var next = Traits.At(b, t + 1);
                        var on = learned && next != null && Traits.Has(next.Type);
                        cell.Link.sprite = LinkSprite(on ? light : new Color32(46, 48, 58, 255), on ? mid : new Color32(30, 32, 40, 255));
                    }
                }
            }

            // 고른 칸 꺾쇠(깜빡임)
            var hit = _cells[_branch, _tier].Hit;
            var x = _columns[_branch].anchoredPosition.x + hit.anchoredPosition.x;
            var top = -hit.anchoredPosition.y;
            var corner = 4f * Px;
            Place(_cursor[0].rectTransform, x - 1f * Px, top - 2f * Px, corner, corner);
            Place(_cursor[1].rectTransform, x + ColumnWidth + 1f * Px - corner, top - 2f * Px, corner, corner);
            Place(_cursor[2].rectTransform, x - 1f * Px, top + RowHeight + 2f * Px - corner, corner, corner);
            Place(_cursor[3].rectTransform, x + ColumnWidth + 1f * Px - corner, top + RowHeight + 2f * Px - corner, corner, corner);
            foreach (var c in _cursor)
            {
                c.color = new Color(1f, 1f, 1f, blink);
                c.enabled = _anyVisible;
            }
            _descIconFrame.enabled = _anyVisible;

            // 설명
            var selected = Traits.At(_branch, _tier);
            _descIconFrame.sprite = FrameSprite(new Color32(24, 26, 34, 255), BranchColors[_branch, 1], BranchColors[_branch, 2]);
            var descIcon = selected == null ? null : PixelUi.Icon($"Trait_{selected.Type}");
            _descIcon.sprite = descIcon;
            _descIcon.enabled = descIcon != null;
            string desc;
            if (selected == null)
                desc = Loc.F("<b>{0} {1}번째 칸</b>\n<color=#8A8A92>아직 준비 중인 특성이다.</color>", Traits.BranchName(_branch), _tier + 1);
            else
            {
                var state = Traits.GetState(selected);
                var stateColor = state == Traits.State.Learned ? "#8CE08C" : state == Traits.State.Available ? "#9FC4FF" : "#FF9A80";
                var cost = state == Traits.State.Learned ? string.Empty : "  ·  " + Loc.F("{0}점", Traits.Cost(selected));
                desc = $"<size=17><b>{selected.Name}</b></size>   <size=13><color={stateColor}>{StateText(state)}{cost}</color></size>\n" +
                       $"<color=#C8CCD6>{selected.Description}</color>";
                if (selected.Tier == Traits.SignatureTier)
                    desc += Loc.F("\n<size=12><color=#B9A0FF>대표 특성 - 모든 갈래를 통틀어 {0}개까지 배울 수 있다</color></size>", Traits.MaxSignatures);
            }
            if (!_anyVisible)
            {
                desc = Traits.ChoiceUnlocked
                    ? Loc.T("<color=#FFD966><b>아직 고른 갈래가 없다</b></color>\n로비의 [8] 스킬 도우미에게 말을 걸어 전투(대시·회전)와 생활(탐색·상인) 갈래를 하나씩 고르자.")
                    : Loc.T("<color=#FFD966><b>아직 고른 갈래가 없다</b></color>\n1층 보스를 쓰러뜨리면 로비의 [8] 스킬 도우미가 특성 갈래를 알려 준다.");
                _descIcon.enabled = false;
            }
            else if (!_choiceMode && Traits.PendingChoice.HasValue && Traits.ChoiceUnlocked)
                desc += "\n" + Loc.T("<size=12><color=#FFD966>남은 갈래는 [8] 스킬 도우미에게서 고르자</color></size>");
            if (pending.HasValue)
                desc = (pending.Value
                           ? Loc.T("<color=#FFD966><b>전투 갈래 고르기</b> - 대시·회전 중 하나만 배울 수 있다 (←→ 고르고 Enter·클릭)</color>")
                           : Loc.T("<color=#FFD966><b>생활 갈래 고르기</b> - 탐색·상인 중 하나만 배울 수 있다 (←→ 고르고 Enter·클릭)</color>"))
                       + "\n" + desc;
            if (!string.IsNullOrEmpty(_notice))
                desc += $"\n<size=13><color=#{ColorUtility.ToHtmlStringRGB(_noticeColor)}>{_notice}</color></size>";
            _desc.text = desc;
        }

        private void Build(Transform canvas)
        {
            var panelImage = HudUi.CreateImage(canvas, "TraitPanel", new Color(0.05f, 0.05f, 0.07f, 0.97f));
            Frame(panelImage, Bg, new Color32(70, 74, 88, 255), new Color32(34, 36, 44, 255));
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel = panelImage.gameObject;
            var root = _panel.transform;

            // 머리줄
            var rule = HudUi.CreateImage(root, "HeaderRule", new Color32(34, 36, 44, 255));
            Place(rule.rectTransform, 6f * Px, 12f * Px, PanelWidth - 12f * Px, 1f * Px);
            var title = Label(root, "Title", 22, TextAnchor.MiddleLeft, 7f * Px, 1f * Px, 20f * Px, 11f * Px);
            title.fontStyle = FontStyle.Bold;
            title.text = Loc.T("특성");
            title.color = new Color32(235, 238, 245, 255);
            var label = Label(root, "PointLabel", 14, TextAnchor.MiddleLeft, 26f * Px, 1f * Px, 26f * Px, 11f * Px);
            label.text = Loc.T("포인트");
            label.color = new Color32(170, 176, 190, 255);
            for (var i = 0; i < PipMax; i++)
            {
                var pip = HudUi.CreateImage(root, $"Pip{i}", Color.white);
                Place(pip.rectTransform, PipLeft + i * PipStep, 4f * Px, 7f * Px, 5f * Px);
                _pips[i] = pip;
            }
            _points = Label(root, "Points", 14, TextAnchor.MiddleLeft, PipLeft + PipMax * PipStep + 2f * Px, 1f * Px, 30f * Px, 11f * Px);
            _info = Label(root, "Info", 12, TextAnchor.MiddleRight, 176f * Px, 1f * Px, 49f * Px, 11f * Px);
            _info.color = new Color32(130, 136, 150, 255);

            var close = HudUi.CreateImage(root, "CloseButton", new Color(0.45f, 0.18f, 0.18f, 1f));
            Frame(close, new Color32(70, 26, 28, 255), new Color32(150, 70, 70, 255), new Color32(46, 16, 18, 255));
            _closeButton = close.rectTransform;
            _closeButton.anchorMin = _closeButton.anchorMax = new Vector2(1f, 1f);
            _closeButton.pivot = new Vector2(1f, 1f);
            _closeButton.sizeDelta = new Vector2(32f, 32f);
            _closeButton.anchoredPosition = new Vector2(-12f, -10f);
            var x = HudUi.CreateText(close.transform, "X", 16, TextAnchor.MiddleCenter);
            x.text = "X";
            x.color = new Color32(240, 170, 170, 255);
            HudUi.Stretch(x.rectTransform, 0f);

            for (var b = 0; b < Traits.BranchCount; b++)
            {
                var column = new GameObject($"Column{b}", typeof(RectTransform));
                column.transform.SetParent(root, false);
                _columns[b] = (RectTransform)column.transform;
                Place(_columns[b], Left + b * (ColumnWidth + ColumnGap), 0f, ColumnWidth, PanelHeight);
                var col = column.transform;
                const float colX = 0f; // 아래 좌표는 열 안 기준
                var light = BranchColors[b, 0];
                var mid = BranchColors[b, 1];
                var dark = BranchColors[b, 2];

                // 갈래 띠 + 문장 + 이름 + 전투/생활
                var banner = HudUi.CreateImage(col, $"Banner{b}", Color.white);
                Frame(banner, Mix(dark, Bg, 0.35f), mid, Mix(dark, Ink, 0.4f));
                Place(banner.rectTransform, colX, BannerTop, ColumnWidth, BannerHeight);
                _banners[b] = banner;
                var emblem = HudUi.CreateImage(col, $"Emblem{b}", Color.white);
                emblem.sprite = PixelUi.Icon($"TraitBranch_{b}");
                emblem.enabled = emblem.sprite != null;
                Place(emblem.rectTransform, colX + 1f * Px, BannerTop - 1f * Px, 12f * Px, 12f * Px);
                var name = Label(col, $"BranchName{b}", 17, TextAnchor.MiddleLeft, colX + 15f * Px, BannerTop, 24f * Px, BannerHeight);
                name.fontStyle = FontStyle.Bold;
                name.color = light;
                name.text = Traits.BranchName(b);
                _bannerNames[b] = name;
                var kind = Label(col, $"BranchKind{b}", 12, TextAnchor.MiddleRight, colX + 30f * Px, BannerTop, 19f * Px, BannerHeight);
                kind.color = new Color32(150, 156, 170, 255);
                kind.text = b < 2 ? Loc.T("전투") : Loc.T("생활");

                for (var t = 0; t < Traits.TierCount; t++)
                {
                    var top = RowTop + t * RowStep;
                    var cell = new Cell();
                    var hitGo = new GameObject($"Cell{b}_{t}", typeof(RectTransform));
                    hitGo.transform.SetParent(col, false);
                    cell.Hit = (RectTransform)hitGo.transform;
                    Place(cell.Hit, colX, top, ColumnWidth, RowHeight + 2f * Px);
                    if (t < Traits.TierCount - 1)
                    {
                        cell.Link = HudUi.CreateImage(col, $"Link{b}_{t}", Color.white);
                        Place(cell.Link.rectTransform, colX + 7f * Px, top + RowHeight, 4f * Px, RowStep - RowHeight);
                    }
                    cell.NameFrame = HudUi.CreateImage(col, $"NameFrame{b}_{t}", Color.white);
                    Frame(cell.NameFrame, LockedFill, LockedEdge, LockedDark);
                    Place(cell.NameFrame.rectTransform, colX + 16f * Px, top + 1f * Px, ColumnWidth - 16f * Px, RowHeight - 2f * Px);
                    cell.IconFrame = HudUi.CreateImage(col, $"IconFrame{b}_{t}", Color.white);
                    Frame(cell.IconFrame, LockedFill, LockedLight, LockedDark);
                    Place(cell.IconFrame.rectTransform, colX + 1f * Px, top, 16f * Px, RowHeight + 2f * Px);
                    cell.Icon = HudUi.CreateImage(col, $"Icon{b}_{t}", Color.white);
                    Place(cell.Icon.rectTransform, colX + 3f * Px, top + 2f * Px, 12f * Px, 12f * Px);
                    cell.Check = HudUi.CreateImage(col, $"Check{b}_{t}", Color.white);
                    cell.Check.sprite = CheckSprite();
                    Place(cell.Check.rectTransform, colX + ColumnWidth - 10f * Px, top + 3f * Px, 8f * Px, 8f * Px);
                    cell.Name = Label(col, $"Name{b}_{t}", 14, TextAnchor.UpperLeft, colX + 18.5f * Px, top + 1.5f * Px, 26f * Px, 6f * Px);
                    cell.Name.fontStyle = FontStyle.Bold;
                    cell.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
                    cell.Sub = Label(col, $"Sub{b}_{t}", 11, TextAnchor.UpperLeft, colX + 18.5f * Px, top + 7f * Px, 32f * Px, 5f * Px);
                    cell.Sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                    _cells[b, t] = cell;
                }
            }

            for (var i = 0; i < 4; i++)
            {
                _cursor[i] = HudUi.CreateImage(root, $"Cursor{i}", Color.white);
                _cursor[i].sprite = CornerSprite(i);
            }

            // 설명 상자
            var descBox = HudUi.CreateImage(root, "DescBox", Color.white);
            Frame(descBox, new Color32(18, 18, 24, 255), new Color32(52, 56, 68, 255), new Color32(28, 30, 38, 255));
            var descWidth = Traits.BranchCount * ColumnWidth + (Traits.BranchCount - 1) * ColumnGap;
            Place(descBox.rectTransform, Left, DescTop, descWidth, DescHeight);
            _descIconFrame = HudUi.CreateImage(root, "DescIconFrame", Color.white);
            Frame(_descIconFrame, LockedFill, LockedLight, LockedDark);
            Place(_descIconFrame.rectTransform, Left + 4f * Px, DescTop + 5f * Px, 16f * Px, 16f * Px);
            _descIcon = HudUi.CreateImage(root, "DescIcon", Color.white);
            Place(_descIcon.rectTransform, Left + 6f * Px, DescTop + 7f * Px, 12f * Px, 12f * Px);
            _desc = Label(root, "Desc", 14, TextAnchor.UpperLeft, Left + 24f * Px, DescTop + 2.5f * Px, descWidth - 28f * Px, DescHeight - 4f * Px);
            _desc.horizontalOverflow = HorizontalWrapMode.Wrap;

            var hint = HudUi.CreateText(root, "Hint", 12, TextAnchor.MiddleCenter);
            hint.text = Loc.T("방향키·마우스: 고르기   Enter·클릭: 배우기 / 배운 칸은 되돌리기   R: 전부 되돌리기   T·Esc: 닫기   ·   다음 던전 입장부터 적용");
            hint.color = new Color(0.47f, 0.5f, 0.56f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(20f, 2f);
            hintRect.offsetMax = new Vector2(-20f, 30f);

            _panel.SetActive(false);
        }

        private static Text Label(Transform parent, string name, int size, TextAnchor anchor, float x, float top, float w, float h)
        {
            var text = HudUi.CreateText(parent, name, size, anchor);
            text.supportRichText = true;
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            Place(text.rectTransform, x, top, w, h);
            return text;
        }

        /// <summary>패널 왼쪽 위 기준 (x, top)에 w×h로 놓는다.</summary>
        private static void Place(RectTransform rect, float x, float top, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(w, h);
        }

        // ---- 코드로 만드는 4배 도트 그림(점 필터, 1px = 4유닛) ----
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        /// <summary>그림 문자열(위 줄부터) → 스프라이트. palette에 있는 문자만 칠한다.</summary>
        private static Sprite FromRows(string key, string[] rows, Dictionary<char, Color32> palette, Vector4 border = default)
        {
            if (Sprites.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var h = rows.Length;
            var w = rows[0].Length;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (palette.TryGetValue(rows[y][x], out var c))
                    pixels[(h - 1 - y) * w + x] = c;
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprites[key] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        /// <summary>테두리 칸 - 검은 외곽 1px + 위·왼쪽 밝은 줄 / 아래·오른쪽 어두운 줄 + 속(9-slice, 테 3px).</summary>
        private static Sprite FrameSprite(Color32 fill, Color32 light, Color32 dark)
        {
            var key = $"F{fill.r},{fill.g},{fill.b}|{light.r},{light.g},{light.b}|{dark.r},{dark.g},{dark.b}";
            return FromRows(key, new[]
            {
                "KKKKKKKK",
                "KLLLLLLK",
                "KLFFFFDK",
                "KLFFFFDK",
                "KLFFFFDK",
                "KLFFFFDK",
                "KLDDDDDK",
                "KKKKKKKK",
            }, new Dictionary<char, Color32> { ['K'] = Ink, ['L'] = light, ['D'] = dark, ['F'] = fill }, new Vector4(3, 3, 3, 3));
        }

        private static void Frame(Image image, Color32 fill, Color32 light, Color32 dark)
        {
            image.sprite = FrameSprite(fill, light, dark);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / Px;
            image.color = Color.white;
        }

        /// <summary>칸 사이 잇는 줄 - 4px 폭(검정·밝음·중간·검정), 세로로 늘인다.</summary>
        private static Sprite LinkSprite(Color32 a, Color32 b) =>
            FromRows($"L{a.r},{a.g},{a.b}|{b.r},{b.g},{b.b}", new[] { "KabK" },
                new Dictionary<char, Color32> { ['K'] = Ink, ['a'] = a, ['b'] = b });

        /// <summary>포인트 보석 7x5 - 0 못 얻음 / 1 씀 / 2 남음.</summary>
        private static Sprite PipSprite(int kind)
        {
            var c = kind == 2 ? new Color32(110, 230, 130, 255) : kind == 1 ? new Color32(64, 112, 74, 255) : new Color32(34, 36, 44, 255);
            var h = kind == 2 ? new Color32(225, 255, 225, 255) : kind == 1 ? new Color32(96, 150, 106, 255) : new Color32(48, 50, 60, 255);
            return FromRows($"P{kind}", new[]
            {
                "..KKK..",
                ".KKhKK.",
                "KKcccKK",
                ".KKcKK.",
                "..KKK..",
            }, new Dictionary<char, Color32> { ['K'] = Ink, ['c'] = c, ['h'] = h });
        }

        private static Sprite CheckSprite() => FromRows("Check", new[]
        {
            "........",
            "......gg",
            ".....ggk",
            "g...ggk.",
            "gg.ggk..",
            "kgggk...",
            ".kgk....",
            "..k.....",
        }, new Dictionary<char, Color32> { ['g'] = new Color32(150, 240, 150, 255), ['k'] = Ink });

        /// <summary>고른 칸 꺾쇠 4x4 - 0 왼위 / 1 오른위 / 2 왼아래 / 3 오른아래.</summary>
        private static Sprite CornerSprite(int corner)
        {
            var rows = corner switch
            {
                0 => new[] { "WWWW", "W...", "W...", "W..." },
                1 => new[] { "WWWW", "...W", "...W", "...W" },
                2 => new[] { "W...", "W...", "W...", "WWWW" },
                _ => new[] { "...W", "...W", "...W", "WWWW" },
            };
            return FromRows($"C{corner}", rows, new Dictionary<char, Color32> { ['W'] = new Color32(255, 255, 255, 255) });
        }
    }
}
