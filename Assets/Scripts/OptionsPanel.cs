using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>설정 창 - 타이틀의 "설정" 버튼이나 Esc 메뉴에서 연다. ↑/↓ 줄 고르기, ←/→ 값 바꾸기(바꾸는 즉시 적용·저장),
    /// Esc 닫기. 마우스는 줄의 ◀/▶ 를 누르거나 줄을 눌러 고른다. 해상도는 누르면 펼쳐지는 목록(스크롤바·마우스 휠·드래그).
    /// 자체 캔버스를 Esc 메뉴보다 위에 그린다. 버튼 클릭은 다른 메뉴처럼 수동 사각형 히트테스트(EventSystem 없음).</summary>
    public class OptionsPanel : MonoBehaviour
    {
        public static bool IsOpen => _instance != null;

        /// <summary>열려 있거나 "이번 프레임에 막 닫혔으면" true - 닫는 Esc가 뒤의 Esc 메뉴까지 닫지 않게.</summary>
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;

        private static OptionsPanel _instance;
        private static int _closedFrame = -1;

        private const float RowHeight = 44f;
        private const float PanelWidth = 640f;
        private static readonly Color RowNormal = new Color(0.14f, 0.15f, 0.2f, 0.92f);
        private static readonly Color RowCursor = new Color(0.3f, 0.42f, 0.62f, 1f);
        private static readonly Color BoxColor = new Color(0.07f, 0.08f, 0.11f, 1f);

        private sealed class Row
        {
            public string Label;
            public Func<string> Value;
            public Action<int> Change; // -1 / +1 (목록형 줄은 null)
            public bool IsList;        // 해상도 - 누르면 목록이 펼쳐진다
            public Image Background;
            public Text ValueText;
            public RectTransform Rect, Left, Right, Box;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<(RectTransform Rect, Action OnClick)> _buttons = new List<(RectTransform, Action)>();
        private int _cursor;
        private int _openedFrame;
        private Action _onClose;
        private List<Vector2Int> _resolutions;

        // ---- 해상도 목록(펼침) ----
        private const int ListVisible = 7;
        private const float ListItemHeight = 32f;
        private const float ListWidth = 260f;
        private const float ScrollbarWidth = 14f;
        private GameObject _list;
        private readonly List<(Image Bg, Text Text, RectTransform Rect)> _listItems = new List<(Image, Text, RectTransform)>();
        private RectTransform _track, _thumb;
        private int _listScroll;  // 맨 위에 보이는 항목 번호
        private int _listCursor;  // 고르고 있는 항목 번호
        private bool _dragging;
        private float _touchDrag;
        private int _listOpenedFrame;

        public static void Open(Action onClose = null)
        {
            if (_instance != null)
                return;
            var go = new GameObject("OptionsPanel");
            _instance = go.AddComponent<OptionsPanel>();
            _instance._onClose = onClose;
            _instance._openedFrame = Time.frameCount;
            _instance.Build();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Close()
        {
            _closedFrame = Time.frameCount;
            _instance = null;
            Destroy(gameObject);
            _onClose?.Invoke();
            if (_languageChanged) // 버튼·제목처럼 이미 그려진 글자까지 새 언어로 - 지금 화면(타이틀·로비)을 다시 띄운다
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private bool _languageChanged;

        /// <summary>게임(Main) 중엔 못 바꾼다 - 다시 띄우면 진행 중인 방이 날아간다.</summary>
        private static bool CanChangeLanguage => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Main";

        // ===================== 줄 정의 =====================

        private void DefineRows()
        {
            _resolutions = GameSettings.AvailableResolutions();
            // 언어 - 화면에 이미 그려진 글자(버튼 등)까지 바꾸려면 화면을 다시 띄워야 해서, 타이틀·로비에서만 바꾼다(창을 닫으면 그 화면을 다시 띄움).
            AddRow("언어 / Language", () => CanChangeLanguage
                ? Loc.DisplayName(Loc.Current)
                : $"{Loc.DisplayName(Loc.Current)}  <color=#9AA0AA>({Loc.T("타이틀에서 변경")})</color>", d =>
            {
                if (!CanChangeLanguage)
                    return;
                Loc.SetLanguage((GameLanguage)(((int)Loc.Current + d + 4) % 4));
                _languageChanged = true;
            });
            AddRow(Loc.T("전체 볼륨"), () => Bar(GameSettings.Master), d => GameSettings.SetMaster(GameSettings.Master + d * GameSettings.VolumeStep));
            AddRow(Loc.T("효과음"), () => Bar(GameSettings.Sfx), d => GameSettings.SetSfx(GameSettings.Sfx + d * GameSettings.VolumeStep));
            AddRow(Loc.T("배경음악"), () => Bar(GameSettings.Bgm), d => GameSettings.SetBgm(GameSettings.Bgm + d * GameSettings.VolumeStep));
            if (!Application.isMobilePlatform) // 폰은 항상 전체 화면 - 화면 모드·해상도 줄을 뺀다
            AddRow(Loc.T("화면 모드"), () => GameSettings.Mode switch
            {
                GameSettings.ScreenMode.Fullscreen => Loc.T("전체 화면"),
                GameSettings.ScreenMode.Borderless => Loc.T("테두리 없는 창"),
                _ => Loc.T("창 모드"),
            }, d => GameSettings.SetMode((GameSettings.ScreenMode)(((int)GameSettings.Mode + d + 3) % 3)));
            if (!Application.isMobilePlatform)
            _rows.Add(new Row { Label = Loc.T("해상도"), Value = () => $"{GameSettings.Resolution.x} x {GameSettings.Resolution.y}", IsList = true });
            AddRow(Loc.T("수직 동기화"), () => OnOff(GameSettings.VSync), _ => GameSettings.SetVSync(!GameSettings.VSync));
            AddRow(Loc.T("화면 흔들림"), () => OnOff(GameSettings.ScreenShake), _ => GameSettings.SetScreenShake(!GameSettings.ScreenShake));
            AddRow(Loc.T("피해 숫자 표시"), () => OnOff(GameSettings.DamageNumbers), _ => GameSettings.SetDamageNumbers(!GameSettings.DamageNumbers));
        }

        private void AddRow(string label, Func<string> value, Action<int> change) =>
            _rows.Add(new Row { Label = label, Value = value, Change = change });

        private static string Bar(int volume)
        {
            var filled = volume / GameSettings.VolumeStep;
            return $"<color=#FFD966>{new string('■', filled)}</color><color=#555A66>{new string('■', 10 - filled)}</color>  {volume,3}%";
        }

        private static string OnOff(bool on) => on ? Loc.T("<color=#8CE08C>켜기</color>") : Loc.T("<color=#9A9AA2>끄기</color>");

        // ===================== 입력 =====================

        private void Update()
        {
            if (_list != null && _list.activeSelf)
            {
                UpdateList();
                Refresh();
                return;
            }

            if (Time.frameCount != _openedFrame)
            {
                if (GameInput.Down(Key.Escape))
                {
                    Close();
                    return;
                }
                if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                    _cursor = (_cursor + _rows.Count - 1) % _rows.Count;
                else if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                    _cursor = (_cursor + 1) % _rows.Count;
                else if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A))
                    Change(_cursor, -1);
                else if (GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D)
                         || GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                    Change(_cursor, +1);
            }

            var mouse = Pointer.current;
            if (mouse != null && GameInput.PointerDown && Time.frameCount != _openedFrame)
                Click(GameInput.PointerPosition);

            Refresh();
        }

        private void Change(int index, int delta)
        {
            var row = _rows[index];
            if (row.IsList)
            {
                OpenList();
                return;
            }
            row.Change(delta);
            SfxPlayer.Play(Sfx.Hit); // 효과음 볼륨을 바로 들어볼 수 있게
        }

        private void Click(Vector2 screenPos)
        {
            foreach (var (rect, onClick) in _buttons)
                if (Hit(rect, screenPos))
                {
                    onClick();
                    return;
                }
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.IsList && Hit(row.Box, screenPos))
                {
                    _cursor = i;
                    OpenList();
                    return;
                }
                if (!row.IsList && Hit(row.Left, screenPos))
                {
                    _cursor = i;
                    Change(i, -1);
                    return;
                }
                if (!row.IsList && Hit(row.Right, screenPos))
                {
                    _cursor = i;
                    Change(i, +1);
                    return;
                }
                if (Hit(row.Rect, screenPos))
                {
                    _cursor = i;
                    return;
                }
            }
        }

        private static bool Hit(RectTransform rect, Vector2 screenPos) =>
            rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null);

        private void Refresh()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Background.color = i == _cursor ? RowCursor : RowNormal;
                var value = row.IsList ? $"{row.Value()}   <color=#9AA0AA>▼</color>" : row.Value();
                if (row.ValueText.text != value)
                    row.ValueText.text = value;
            }
        }

        // ===================== 해상도 목록(스크롤) =====================

        private void OpenList()
        {
            _resolutions = GameSettings.AvailableResolutions();
            _listCursor = Mathf.Max(0, _resolutions.IndexOf(GameSettings.Resolution));
            _listScroll = Mathf.Clamp(_listCursor - ListVisible / 2, 0, MaxScroll);
            _listOpenedFrame = Time.frameCount;
            _dragging = false;
            _list.SetActive(true);
            RefreshList();
        }

        private void CloseList()
        {
            _list.SetActive(false);
            _dragging = false;
        }

        private int MaxScroll => Mathf.Max(0, _resolutions.Count - ListVisible);

        private void UpdateList()
        {
            var mouse = Pointer.current;
            if (Time.frameCount == _listOpenedFrame)
                return;

            {
                if (GameInput.Down(Key.Escape))
                {
                    CloseList();
                    return;
                }
                if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                    MoveListCursor(-1);
                else if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                    MoveListCursor(+1);
                else if (GameInput.Down(Key.PageUp))
                    MoveListCursor(-ListVisible);
                else if (GameInput.Down(Key.PageDown))
                    MoveListCursor(+ListVisible);
                else if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
                {
                    PickResolution(_listCursor);
                    return;
                }
            }

            if (mouse == null)
                return;

            var wheel = GameInput.ScrollY;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _listScroll = Mathf.Clamp(_listScroll - Math.Sign(wheel), 0, MaxScroll);
                RefreshList();
            }
            // 터치 - 목록 위를 끌면 항목 높이만큼 끌 때마다 한 칸(위로 끌면 아래 항목이 올라온다)
            if (!_dragging && GameInput.PointerHeld)
            {
                _touchDrag += GameInput.TouchDragY;
                var step = ListItemHeight * Mathf.Max(0.01f, _list.transform.lossyScale.y);
                while (Mathf.Abs(_touchDrag) >= step)
                {
                    var dir = Math.Sign(_touchDrag);
                    _listScroll = Mathf.Clamp(_listScroll + dir, 0, MaxScroll);
                    _touchDrag -= dir * step;
                    RefreshList();
                }
            }
            else
                _touchDrag = 0f;

            var pos = GameInput.PointerPosition;
            if (GameInput.PointerDown)
            {
                if (Hit(_thumb, pos) || Hit(_track, pos))
                {
                    _dragging = true;
                }
                else
                {
                    for (var i = 0; i < _listItems.Count; i++)
                        if (_listItems[i].Bg.gameObject.activeSelf && Hit(_listItems[i].Rect, pos))
                        {
                            PickResolution(_listScroll + i);
                            return;
                        }
                    if (!Hit((RectTransform)_list.transform, pos))
                    {
                        CloseList(); // 목록 밖을 누르면 닫기
                        return;
                    }
                }
            }
            if (_dragging)
            {
                if (!GameInput.PointerHeld)
                    _dragging = false;
                else
                    DragThumb(pos);
            }

            // 마우스가 올라간 항목을 커서로
            for (var i = 0; i < _listItems.Count; i++)
                if (!_dragging && _listItems[i].Bg.gameObject.activeSelf && Hit(_listItems[i].Rect, pos) && GameInput.PointerDelta.sqrMagnitude > 0f)
                    _listCursor = _listScroll + i;
            RefreshList();
        }

        private void MoveListCursor(int delta)
        {
            _listCursor = Mathf.Clamp(_listCursor + delta, 0, _resolutions.Count - 1);
            if (_listCursor < _listScroll)
                _listScroll = _listCursor;
            else if (_listCursor >= _listScroll + ListVisible)
                _listScroll = _listCursor - ListVisible + 1;
            RefreshList();
        }

        /// <summary>스크롤바를 잡고 끌 때 - 트랙 위 마우스 높이를 스크롤 위치로.</summary>
        private void DragThumb(Vector2 screenPos)
        {
            if (MaxScroll == 0)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_track, screenPos, null, out var local);
            var h = _track.rect.height;
            var thumbH = _thumb.rect.height;
            var t = Mathf.InverseLerp(h * 0.5f - thumbH * 0.5f, -h * 0.5f + thumbH * 0.5f, local.y);
            _listScroll = Mathf.Clamp(Mathf.RoundToInt(t * MaxScroll), 0, MaxScroll);
        }

        private void PickResolution(int index)
        {
            if (index < 0 || index >= _resolutions.Count)
                return;
            GameSettings.SetResolution(_resolutions[index]);
            SfxPlayer.Play(Sfx.Hit);
            CloseList();
        }

        private void RefreshList()
        {
            var current = GameSettings.Resolution;
            for (var i = 0; i < _listItems.Count; i++)
            {
                var index = _listScroll + i;
                var (bg, text, _) = _listItems[i];
                var visible = index < _resolutions.Count;
                bg.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                var r = _resolutions[index];
                text.text = r == current ? Loc.F("<color=#FFD966>{0} x {1}  (지금)</color>", r.x, r.y) : $"{r.x} x {r.y}";
                bg.color = index == _listCursor ? RowCursor : new Color(0.12f, 0.13f, 0.17f, 1f);
            }

            // 스크롤바 - 보이는 비율만큼 손잡이 길이, 스크롤 위치만큼 아래로
            var trackH = _track.rect.height;
            var ratio = _resolutions.Count <= ListVisible ? 1f : (float)ListVisible / _resolutions.Count;
            var thumbH = Mathf.Max(24f, trackH * ratio);
            var t = MaxScroll == 0 ? 0f : (float)_listScroll / MaxScroll;
            _thumb.sizeDelta = new Vector2(ScrollbarWidth - 4f, thumbH);
            _thumb.anchoredPosition = new Vector2(0f, Mathf.Lerp(trackH * 0.5f - thumbH * 0.5f, -trackH * 0.5f + thumbH * 0.5f, t));
            _thumb.GetComponent<Image>().color = _dragging ? new Color(0.75f, 0.82f, 0.95f) : new Color(0.5f, 0.56f, 0.68f);
        }

        // ===================== UI =====================

        private void Build()
        {
            DefineRows();

            var canvasGo = new GameObject("OptionsCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150; // Esc 메뉴(100)보다 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 16:9가 아닌 화면에서도 잘리지 않게

            var dim = HudUi.CreateImage(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.65f));
            HudUi.Stretch(dim.rectTransform, 0f);

            var height = 120f + _rows.Count * RowHeight + 80f;
            var board = HudUi.CreateImage(canvasGo.transform, "Board", new Color(0.08f, 0.08f, 0.11f, 0.97f));
            PixelUi.Slice(board, "Panel");
            board.rectTransform.sizeDelta = new Vector2(PanelWidth, height);

            var top = height * 0.5f;
            Label(board.transform, Loc.T("설정"), 30, new Vector2(0f, top - 45f), 300f, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            Label(board.transform, Loc.T("↑↓ 고르기   ←→ 바꾸기   Esc 닫기"), 14, new Vector2(0f, top - 78f), 500f, TextAnchor.MiddleCenter)
                .color = new Color(0.65f, 0.68f, 0.75f);

            var y = top - 120f;
            var listRowY = 0f;
            foreach (var row in _rows)
            {
                var bg = HudUi.CreateImage(board.transform, "Row", RowNormal);
                bg.rectTransform.sizeDelta = new Vector2(PanelWidth - 60f, RowHeight - 6f);
                bg.rectTransform.anchoredPosition = new Vector2(0f, y);
                row.Background = bg;
                row.Rect = bg.rectTransform;
                Label(bg.transform, row.Label, 18, new Vector2(-150f, 0f), 240f, TextAnchor.MiddleLeft);
                if (row.IsList)
                {
                    // 펼침 상자(누르면 아래로 목록)
                    var box = HudUi.CreateImage(bg.transform, "Box", BoxColor);
                    box.rectTransform.sizeDelta = new Vector2(ListWidth, RowHeight - 14f);
                    box.rectTransform.anchoredPosition = new Vector2(140f, 0f);
                    row.Box = box.rectTransform;
                    row.ValueText = Label(box.transform, "", 17, Vector2.zero, ListWidth - 10f, TextAnchor.MiddleCenter);
                    listRowY = y;
                }
                else
                {
                    row.Left = Label(bg.transform, "◀", 18, new Vector2(20f, 0f), 30f, TextAnchor.MiddleCenter).rectTransform;
                    row.ValueText = Label(bg.transform, "", 17, new Vector2(140f, 0f), 210f, TextAnchor.MiddleCenter);
                    row.Right = Label(bg.transform, "▶", 18, new Vector2(260f, 0f), 30f, TextAnchor.MiddleCenter).rectTransform;
                }
                y -= RowHeight;
            }

            y -= 20f;
            Button(board.transform, Loc.T("기본값으로"), new Vector2(-120f, y), () => { GameSettings.ResetToDefaults(); SfxPlayer.Play(Sfx.Hit); });
            Button(board.transform, Loc.T("닫기  [Esc]"), new Vector2(120f, y), Close);

            BuildList(board.transform, new Vector2(140f, listRowY - (RowHeight - 14f) * 0.5f));
            Refresh();
        }

        /// <summary>해상도 상자 바로 아래로 펼쳐지는 목록 + 오른쪽 스크롤바(다른 줄 위를 덮는다).</summary>
        private void BuildList(Transform board, Vector2 boxBottom)
        {
            var listH = ListVisible * ListItemHeight + 8f;
            var frame = HudUi.CreateImage(board, "ResolutionList", new Color(0.05f, 0.05f, 0.08f, 0.99f));
            frame.rectTransform.sizeDelta = new Vector2(ListWidth, listH);
            frame.rectTransform.pivot = new Vector2(0.5f, 1f);
            frame.rectTransform.anchoredPosition = boxBottom;
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.5f, 0.62f);
            outline.effectDistance = new Vector2(2f, -2f);
            _list = frame.gameObject;

            for (var i = 0; i < ListVisible; i++)
            {
                var item = HudUi.CreateImage(frame.transform, "Item", RowNormal);
                item.rectTransform.sizeDelta = new Vector2(ListWidth - ScrollbarWidth - 12f, ListItemHeight - 2f);
                item.rectTransform.anchorMin = item.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                item.rectTransform.anchoredPosition = new Vector2(-ScrollbarWidth * 0.5f - 2f, -4f - ListItemHeight * (i + 0.5f));
                var text = Label(item.transform, "", 16, Vector2.zero, ListWidth - 40f, TextAnchor.MiddleCenter);
                _listItems.Add((item, text, item.rectTransform));
            }

            var track = HudUi.CreateImage(frame.transform, "Scrollbar", new Color(0.16f, 0.17f, 0.22f, 1f));
            track.rectTransform.sizeDelta = new Vector2(ScrollbarWidth, listH - 8f);
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.anchoredPosition = new Vector2(-ScrollbarWidth * 0.5f - 4f, 0f);
            _track = track.rectTransform;
            var thumb = HudUi.CreateImage(track.transform, "Thumb", new Color(0.5f, 0.56f, 0.68f));
            _thumb = thumb.rectTransform;
            _list.SetActive(false);
        }

        private void Button(Transform parent, string content, Vector2 pos, Action onClick)
        {
            var image = HudUi.CreateImage(parent, "Button", new Color(0.25f, 0.28f, 0.35f));
            if (PixelUi.Slice(image, "Button"))
                image.color = PixelUi.Tint(new Color(0.25f, 0.28f, 0.35f));
            image.rectTransform.sizeDelta = new Vector2(200f, 44f);
            image.rectTransform.anchoredPosition = pos;
            Label(image.transform, content, 17, Vector2.zero, 190f, TextAnchor.MiddleCenter);
            _buttons.Add((image.rectTransform, onClick));
        }

        private static Text Label(Transform parent, string content, int size, Vector2 pos, float width, TextAnchor align)
        {
            var text = HudUi.CreateText(parent, "Label", size, align);
            text.text = content;
            text.supportRichText = true;
            text.rectTransform.sizeDelta = new Vector2(width, 36f);
            text.rectTransform.anchoredPosition = pos;
            return text;
        }
    }
}
