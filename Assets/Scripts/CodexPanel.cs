using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비의 도감 창(책 아이콘 버튼) - 먼저 분류 고르기 화면(몬스터 / 유물 / 보스 카드 3장, 각 대표 그림)이 뜨고, 고르면 그 분류의
    /// 격자 + 상세 화면. 발견 전 항목은 검은 실루엣 + "???" + 해금 단서, 발견하면 그림·이름·효과. 항목이 없는 분류는 "준비 중".
    /// 조작 - 고르기 화면: ←/→·1/2/3·클릭, Enter = 열기, Esc = 닫기 / 격자 화면: 방향키·클릭 = 고르기, Esc·뒤로 = 고르기 화면으로.
    /// 다른 로비 창과 같은 이유로 EventSystem 없이 직접 읽고 자체 캔버스에 그린다.</summary>
    public class CodexPanel : MonoBehaviour
    {
        private const int Columns = 6;
        private const float CellSize = 72f;
        private const float CellGap = 8f;
        private const float PanelWidth = 1040f;
        private const float PanelHeight = 600f;
        private const float GridLeft = 28f;
        private const float GridTop = 76f;
        private const int MaxCells = 24; // 가장 많은 분류(유물 22)보다 넉넉히

        private static readonly Color Silhouette = new Color(0.02f, 0.02f, 0.03f, 0.9f);
        private static readonly Color CellNormal = new Color(0.35f, 0.38f, 0.45f, 1f);
        private static readonly Color CellFound = new Color(0.5f, 0.8f, 1f, 1f);
        private static readonly Color CellCursor = new Color(1f, 0.82f, 0.35f, 1f);

        /// <summary>분류 카드 - 이름, 대표 그림, 한 줄 설명.</summary>
        private static readonly (CodexCategory Category, string Name, string Blurb)[] Books =
        {
            (CodexCategory.Monster, "몬스터 도감", "던전에서 처치한 몹의 기록"),
            (CodexCategory.Relic, "유물 도감", "층 보스가 남긴 유물의 기록"),
            (CodexCategory.Boss, "보스 도감", "층을 지키는 문지기들의 기록"),
        };

        private static Sprite BookImage(CodexCategory category) => category switch
        {
            CodexCategory.Monster => SpriteAnimator.FirstFrame("Slime"),
            CodexCategory.Relic => PixelUi.Get("UI/Relics/Relic_CrownOfEnd"),
            _ => PixelUi.Get("UI/Relics/Codex_Boss"),
        };

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private Text _header;
        private RectTransform _closeButton;
        private RectTransform _backButton;

        // 분류 고르기 화면
        private GameObject _menuRoot;
        private readonly List<(RectTransform Rect, Image Frame, Image Icon, Text Label)> _bookCards = new List<(RectTransform, Image, Image, Text)>();
        private int _bookCursor;

        // 격자 화면
        private GameObject _gridRoot;
        private readonly List<(RectTransform Rect, Image Frame, Image Icon)> _cells = new List<(RectTransform, Image, Image)>();
        private Image _detailImage;
        private Text _detailName;
        private Text _detailBody;
        private int _cursor;
        private CodexCategory _category;
        private List<CodexEntry> _entries = new List<CodexEntry>();

        private bool InGrid => _gridRoot.activeSelf;
        private int _openedFrame = -1;

        public static CodexPanel Create()
        {
            var canvasGo = new GameObject("CodexCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 56;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            var panel = canvasGo.AddComponent<CodexPanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        public void Open()
        {
            IsOpen = true;
            _openedFrame = Time.frameCount;
            _panel.SetActive(true);
            ShowMenu();
        }

        private void Close()
        {
            IsOpen = false;
            _closedFrame = Time.frameCount;
            _panel.SetActive(false);
        }

        private static bool IsReady(CodexCategory category) => Codex.TotalCount(category) > 0;

        private void ShowMenu()
        {
            _menuRoot.SetActive(true);
            _gridRoot.SetActive(false);
            _backButton.gameObject.SetActive(false);
            _header.text = $"<b>도감</b>   <color=#FFD966>{Codex.FoundCount()}/{Codex.TotalCount()}</color>  " +
                           "<size=13><color=#9AA0AA>살펴볼 도감을 고르세요</color></size>";
            RefreshMenu();
        }

        private void OpenBook(int index)
        {
            var category = Books[index].Category;
            if (!IsReady(category))
                return; // 준비 중
            _category = category;
            _entries = Codex.All.Where(e => e.Category == category).ToList();
            _cursor = 0;
            _menuRoot.SetActive(false);
            _gridRoot.SetActive(true);
            _backButton.gameObject.SetActive(true);
            RefreshGrid();
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;
            if (InGrid)
                UpdateGrid();
            else
                UpdateMenu();
        }

        private void UpdateMenu()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    Close();
                    return;
                }
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                    _bookCursor = (_bookCursor + Books.Length - 1) % Books.Length;
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                    _bookCursor = (_bookCursor + 1) % Books.Length;
                for (var i = 0; i < Books.Length; i++)
                {
                    var key = i == 0 ? keyboard.digit1Key : i == 1 ? keyboard.digit2Key : keyboard.digit3Key;
                    if (key.wasPressedThisFrame)
                    {
                        _bookCursor = i;
                        OpenBook(i);
                        return;
                    }
                }
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                {
                    OpenBook(_bookCursor);
                    return;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                var moved = mouse.delta.ReadValue().sqrMagnitude > 0f;
                if (mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                for (var i = 0; i < _bookCards.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_bookCards[i].Rect, pos, null))
                        continue;
                    if (moved)
                        _bookCursor = i;
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        _bookCursor = i;
                        OpenBook(i);
                        return;
                    }
                }
            }
            RefreshMenu();
        }

        private void UpdateGrid()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
                {
                    ShowMenu();
                    return;
                }
                var count = _entries.Count;
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                    MoveCursor((_cursor + count - 1) % count);
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                    MoveCursor((_cursor + 1) % count);
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                    MoveCursor(_cursor - Columns >= 0 ? _cursor - Columns : _cursor);
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                    MoveCursor(_cursor + Columns < count ? _cursor + Columns : _cursor);
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var pos = mouse.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
                {
                    Close();
                    return;
                }
                if (RectTransformUtility.RectangleContainsScreenPoint(_backButton, pos, null))
                {
                    ShowMenu();
                    return;
                }
                for (var i = 0; i < _entries.Count; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(_cells[i].Rect, pos, null))
                        MoveCursor(i);
            }
        }

        private void MoveCursor(int index)
        {
            _cursor = index;
            RefreshGrid();
        }

        private void RefreshMenu()
        {
            for (var i = 0; i < _bookCards.Count; i++)
            {
                var (_, frame, icon, label) = _bookCards[i];
                var (category, name, blurb) = Books[i];
                var ready = IsReady(category);
                frame.color = i == _bookCursor && ready ? CellCursor : ready ? CellFound : CellNormal;
                var sprite = BookImage(category);
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.color = ready ? Color.white : Silhouette; // 준비 중인 도감은 표지 그림도 실루엣
                label.text = ready
                    ? $"<b>[{i + 1}] {name}</b>\n<size=13><color=#C8CCD6>{blurb}</color></size>\n<color=#FFD966>{Codex.FoundCount(category)} / {Codex.TotalCount(category)}</color>"
                    : $"<b><color=#9A9AA2>[{i + 1}] {name}</color></b>\n<size=13><color=#7A7A82>{blurb}</color></size>\n<color=#B9A0FF>준비 중</color>";
            }
        }

        private void RefreshGrid()
        {
            var book = Books.First(b => b.Category == _category);
            _header.text = $"<b>{book.Name}</b>   <color=#FFD966>{Codex.FoundCount(_category)}/{Codex.TotalCount(_category)}</color>  " +
                           "<size=13><color=#9AA0AA>방향키·클릭: 고르기   Esc: 도감 목록으로</color></size>";

            for (var i = 0; i < _cells.Count; i++)
            {
                var (rect, frame, icon) = _cells[i];
                var visible = i < _entries.Count;
                rect.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                var entry = _entries[i];
                var found = Codex.IsFound(entry);
                frame.color = i == _cursor ? CellCursor : found ? CellFound : CellNormal;
                ApplyImage(icon, entry, found);
            }

            if (_entries.Count == 0)
                return;
            var current = _entries[_cursor];
            var isFound = Codex.IsFound(current);
            ApplyImage(_detailImage, current, isFound);
            _detailName.text = isFound ? current.Name : "???";
            var note = isFound ? current.Note?.Invoke() : null;
            _detailBody.text = isFound
                ? $"{current.Description()}{(note != null ? $"\n\n{note}" : "")}"
                : $"<color=#9AA0AA>아직 발견하지 못했다.</color>\n\n<color=#B9A0FF>단서</color>  {current.Hint}";
        }

        /// <summary>그림(없으면 단색 사각형)을 넣고, 발견 전이면 검은 실루엣으로 칠한다.</summary>
        private static void ApplyImage(Image image, CodexEntry entry, bool found)
        {
            var sprite = entry.Image?.Invoke();
            image.sprite = sprite;
            image.preserveAspect = sprite != null;
            image.color = !found ? Silhouette : sprite != null ? Color.white : entry.FallbackColor;
        }

        // ===================== UI 생성 =====================

        private void Build(Transform canvas)
        {
            var panelImage = HudUi.CreateImage(canvas, "CodexPanel", new Color(0.05f, 0.05f, 0.07f, 0.96f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            // 왼쪽 위 책 아이콘 + 제목
            var book = HudUi.CreateImage(_panel.transform, "BookIcon", Color.white);
            book.sprite = PixelUi.Get("UI/Relics/Codex_Book");
            book.enabled = book.sprite != null;
            Place(book.rectTransform, GridLeft, 14f, 36f, 36f);

            _header = HudUi.CreateText(_panel.transform, "Header", 20, TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            Place(_header.rectTransform, GridLeft + 46f, 14f, PanelWidth - 260f, 36f);

            _backButton = CreateSmallButton("BackButton", "< 도감 목록", PanelWidth - 190f, 16f, 130f);
            var close = CreateSmallButton("CloseButton", "X", PanelWidth - 50f, 16f, 30f);
            close.GetComponent<Image>().color = new Color(1f, 0.6f, 0.6f);
            _closeButton = close;

            BuildMenu();
            BuildGrid();
            _panel.SetActive(false);
        }

        private RectTransform CreateSmallButton(string name, string label, float x, float y, float width)
        {
            var image = HudUi.CreateImage(_panel.transform, name, new Color(0.3f, 0.32f, 0.4f, 1f));
            PixelUi.Slice(image, "Button");
            Place(image.rectTransform, x, y, width, 30f);
            var text = HudUi.CreateText(image.transform, "Label", 14, TextAnchor.MiddleCenter);
            text.text = label;
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            HudUi.Stretch(text.rectTransform, 0f);
            return image.rectTransform;
        }

        private void BuildMenu()
        {
            _menuRoot = new GameObject("Menu", typeof(RectTransform));
            _menuRoot.transform.SetParent(_panel.transform, false);
            HudUi.Stretch(_menuRoot.GetComponent<RectTransform>(), 0f);

            const float cardWidth = 300f, cardHeight = 420f, gap = 30f;
            var left = (PanelWidth - (Books.Length * cardWidth + (Books.Length - 1) * gap)) * 0.5f;
            for (var i = 0; i < Books.Length; i++)
            {
                var frame = HudUi.CreateImage(_menuRoot.transform, $"Book{i}", CellNormal);
                PixelUi.Slice(frame, "Slot");
                Place(frame.rectTransform, left + i * (cardWidth + gap), 110f, cardWidth, cardHeight);

                var inner = HudUi.CreateImage(frame.transform, "Inner", new Color(0.08f, 0.09f, 0.12f, 0.95f));
                HudUi.Stretch(inner.rectTransform, 10f);

                var icon = HudUi.CreateImage(frame.transform, "Icon", Color.white);
                icon.preserveAspect = true;
                Place(icon.rectTransform, (cardWidth - 200f) * 0.5f, 34f, 200f, 200f);

                var label = HudUi.CreateText(frame.transform, "Label", 20, TextAnchor.UpperCenter);
                label.supportRichText = true;
                label.lineSpacing = 1.3f;
                label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                Place(label.rectTransform, 16f, 256f, cardWidth - 32f, 140f);

                _bookCards.Add((frame.rectTransform, frame, icon, label));
            }

            var hint = HudUi.CreateText(_menuRoot.transform, "Hint", 13, TextAnchor.MiddleCenter);
            hint.text = "←/→·1/2/3·클릭: 고르기   Enter: 열기   Esc: 닫기";
            hint.color = new Color(0.6f, 0.63f, 0.7f);
            Place(hint.rectTransform, 0f, PanelHeight - 50f, PanelWidth, 30f);
        }

        private void BuildGrid()
        {
            _gridRoot = new GameObject("Grid", typeof(RectTransform));
            _gridRoot.transform.SetParent(_panel.transform, false);
            HudUi.Stretch(_gridRoot.GetComponent<RectTransform>(), 0f);

            for (var i = 0; i < MaxCells; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var frame = HudUi.CreateImage(_gridRoot.transform, $"Cell{i}", CellNormal);
                PixelUi.Slice(frame, "Slot");
                Place(frame.rectTransform, GridLeft + col * (CellSize + CellGap), GridTop + row * (CellSize + CellGap), CellSize, CellSize);
                var icon = HudUi.CreateImage(frame.transform, "Icon", Color.white);
                HudUi.Stretch(icon.rectTransform, 10f);
                _cells.Add((frame.rectTransform, frame, icon));
            }

            // 상세 칸 - 격자 오른쪽
            var detailLeft = GridLeft + Columns * (CellSize + CellGap) + 20f;
            var detailWidth = PanelWidth - detailLeft - 28f;
            var detailHeight = PanelHeight - GridTop - 28f;
            var detailBg = HudUi.CreateImage(_gridRoot.transform, "Detail", new Color(0.09f, 0.1f, 0.13f, 0.95f));
            Place(detailBg.rectTransform, detailLeft, GridTop, detailWidth, detailHeight);

            var imageFrame = HudUi.CreateImage(detailBg.transform, "ImageFrame", new Color(0.14f, 0.15f, 0.2f, 1f));
            Place(imageFrame.rectTransform, (detailWidth - 180f) * 0.5f, 18f, 180f, 180f);
            _detailImage = HudUi.CreateImage(imageFrame.transform, "Image", Color.white);
            HudUi.Stretch(_detailImage.rectTransform, 12f);

            _detailName = HudUi.CreateText(detailBg.transform, "Name", 22, TextAnchor.MiddleCenter);
            _detailName.fontStyle = FontStyle.Bold;
            _detailName.color = new Color(1f, 0.88f, 0.5f);
            Place(_detailName.rectTransform, 16f, 208f, detailWidth - 32f, 32f);

            _detailBody = HudUi.CreateText(detailBg.transform, "Body", 15, TextAnchor.UpperLeft);
            _detailBody.supportRichText = true;
            _detailBody.lineSpacing = 1.2f;
            Place(_detailBody.rectTransform, 22f, 252f, detailWidth - 44f, detailHeight - 270f);

            _gridRoot.SetActive(false);
        }

        /// <summary>부모 왼쪽 위 기준 (x, y) 위치에 w x h 크기로 놓는다(y는 아래로 +).</summary>
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
