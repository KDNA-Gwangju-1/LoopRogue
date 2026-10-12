using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>퍼즐·미니게임 안내 - 시작하면 가운데 큰 설명 창(이름·규칙)을 띄우고, [Space]/[Enter]/버튼으로 닫으면 퍼즐이 시작된다
    /// (사용자 결정 - 위에 큰 패널을 계속 띄우면 판을 가리거나 화면이 작아져서). 닫은 뒤엔 위 띠 바로 아래 얇은 한 줄(이름·진행 상황)과
    /// [H] 설명 다시 보기 / [G] 포기 버튼만 남는다. 실패하면 가운데에 "[R] 다시 도전 / [B] 이전 방으로" 창.
    /// 자기 캔버스를 따로 만든다(성공하면 레벨업 카드가 뜨기 전에 사라지고, 실패 창이 떠 있는 동안엔 카드가 안 뜬다).</summary>
    public class PuzzleHud : MonoBehaviour
    {
        private const float BarWidth = 820f, BarHeight = 34f, BarGap = 4f;
        private const float RulesWidth = 760f, RulesHeight = 400f;

        /// <summary>얇은 진행 줄이 떠 있는 동안 위 띠 아래로 더 비우는 높이(캔버스 단위, 기준 1280x720) - GameHUD가 카메라 영역을
        /// 이만큼만 내려서 판이 줄에 가려지지 않게 한다. 퍼즐이 없으면 0.</summary>
        public static float ExtraTopInset => _active != null ? BarGap + BarHeight + BarGap : 0f;

        /// <summary>"다시 도전"이면 설명 창을 건너뛴다(방금 읽었으니까) - LoopManager.RetryPuzzle이 켠다.</summary>
        public static bool SkipRulesNext;

        // 다시 도전하면 새 패널이 먼저 생기고 옛 패널은 프레임 끝에 지워져서, "지금 패널"을 기억해 둔다
        private static PuzzleHud _active;

        private PuzzleBase _puzzle;
        private Text _status;
        private GameObject _bar;
        private GameObject _rulesPanel;
        private Action _onRulesClosed;
        private bool _rulesOpen, _closeNextFrame;
        private GameObject _failPanel;
        private Text _failReason, _backLabel;
        private Action _onRetry, _onBack;
        private bool _failOpen;

        public bool RulesOpen => _rulesOpen || _closeNextFrame;

        public static PuzzleHud Create(PuzzleBase puzzle)
        {
            var go = new GameObject("PuzzleHud");
            var hud = go.AddComponent<PuzzleHud>();
            hud._puzzle = puzzle;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            var canvasGo = new GameObject("PuzzleCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // HUD(0) 위, 대화창(50)·모바일 버튼(90)·Esc 메뉴(100) 아래
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _active = this;

            BuildBar(canvasGo.transform);
            BuildRulesPanel(canvasGo.transform);
            BuildFailPanel(canvasGo.transform);
        }

        private string Kind => PuzzleBase.IsMiniGame(_puzzle.Type) ? Loc.T("미니게임") : Loc.T("퍼즐");

        /// <summary>위 띠 바로 아래 얇은 한 줄 - 왼쪽에 이름·진행 상황, 오른쪽에 (퍼즐 전용 키) [H] 설명 [G] 포기.</summary>
        private void BuildBar(Transform parent)
        {
            var bar = HudUi.CreateImage(parent, "PuzzleBar", new Color(0.08f, 0.09f, 0.12f, 0.92f));
            PixelUi.Slice(bar, "Panel", 2f);
            var rect = bar.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(BarWidth, BarHeight);
            rect.anchoredPosition = new Vector2(0f, -ActionBarLayout.TopBarHeight - BarGap);
            _bar = bar.gameObject;
            _bar.SetActive(false); // 퍼즐이 실제로 시작되면(ShowBar) 보인다

            var buttons = _puzzle.ExtraKeyLabel != null ? 3 : 2;
            const float buttonWidth = 92f;
            _status = HudUi.CreateText(bar.transform, "Status", 14, TextAnchor.MiddleLeft);
            var statusRect = _status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.offsetMin = new Vector2(12f, 2f);
            statusRect.offsetMax = new Vector2(-(buttonWidth + 6f) * buttons - 8f, -2f);
            _status.supportRichText = true;
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 10;
            _status.resizeTextMaxSize = 14;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            var x = -6f;
            BarButton(bar.transform, ref x, buttonWidth, Loc.T("포기 [G]"), Key.G, new Color(0.35f, 0.12f, 0.12f, 0.95f));
            BarButton(bar.transform, ref x, buttonWidth, Loc.T("설명 [H]"), Key.H, new Color(0.22f, 0.24f, 0.3f, 0.95f));
            if (_puzzle.ExtraKeyLabel != null)
                BarButton(bar.transform, ref x, buttonWidth, _puzzle.ExtraKeyLabel, _puzzle.ExtraKey, new Color(0.18f, 0.3f, 0.45f, 0.95f));
        }

        private void BarButton(Transform parent, ref float x, float width, string label, Key key, Color color)
        {
            var image = HudUi.CreateImage(parent, "Button", color);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, BarHeight - 8f);
            rect.anchoredPosition = new Vector2(x, 0f);
            x -= width + 6f;
            var text = HudUi.CreateText(image.transform, "Text", 13, TextAnchor.MiddleCenter);
            HudUi.Stretch(text.rectTransform, 0f);
            text.text = label;
            GameInput.RegisterTap(rect, key, () => this != null && !_failOpen && !RulesOpen);
        }

        /// <summary>가운데 큰 설명 창 - 이름·규칙(큰 글씨)·진행 상황, 아래에 시작/닫기 버튼.</summary>
        private void BuildRulesPanel(Transform parent)
        {
            // 뒤 판을 살짝 어둡게
            var dim = HudUi.CreateImage(parent, "RulesDim", new Color(0f, 0f, 0f, 0.55f));
            HudUi.Stretch(dim.rectTransform, 0f);
            _rulesPanel = dim.gameObject;

            var panel = HudUi.CreateImage(dim.transform, "Rules", new Color(0.08f, 0.09f, 0.12f, 0.97f));
            PixelUi.Slice(panel, "Panel", 2f);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(RulesWidth, RulesHeight);

            var kind = HudUi.CreateText(panel.transform, "Kind", 16, TextAnchor.MiddleCenter);
            Place(kind.rectTransform, -22f, 24f);
            kind.color = new Color(0.7f, 0.72f, 0.8f);
            kind.text = Kind;

            var title = HudUi.CreateText(panel.transform, "Title", 34, TextAnchor.MiddleCenter);
            Place(title.rectTransform, -50f, 46f);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.85f, 0.45f);
            title.text = _puzzle.Title;

            var rule = HudUi.CreateText(panel.transform, "Rule", 22, TextAnchor.MiddleCenter);
            Place(rule.rectTransform, -112f, 180f, 48f);
            rule.color = new Color(0.92f, 0.94f, 0.98f);
            rule.supportRichText = true;
            rule.horizontalOverflow = HorizontalWrapMode.Wrap;
            rule.resizeTextForBestFit = true; // 번역 등으로 길면 줄여서
            rule.resizeTextMinSize = 14;
            rule.resizeTextMaxSize = 22;
            rule.text = _puzzle.Rule;

            var start = HudUi.CreateImage(panel.transform, "Start", new Color(0.2f, 0.42f, 0.28f, 0.97f));
            var startRect = start.rectTransform;
            startRect.anchorMin = startRect.anchorMax = startRect.pivot = new Vector2(0.5f, 0f);
            startRect.sizeDelta = new Vector2(260f, 52f);
            startRect.anchoredPosition = new Vector2(0f, 24f);
            var startText = HudUi.CreateText(start.transform, "Text", 20, TextAnchor.MiddleCenter);
            HudUi.Stretch(startText.rectTransform, 2f);
            startText.fontStyle = FontStyle.Bold;
            startText.text = Loc.T("확인 [Space]");
            GameInput.RegisterTap(startRect, Key.Enter, () => this != null && _rulesOpen);

            _rulesPanel.SetActive(false);
        }

        private static void Place(RectTransform rect, float top, float height, float sidePadding = 24f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePadding, top - height);
            rect.offsetMax = new Vector2(-sidePadding, top);
        }

        /// <summary>설명 창을 연다 - 닫으면(다음 프레임에) onClosed. 같은 프레임에 Space가 "제자리 대기"로도 읽히지 않게 한 프레임 늦춘다.</summary>
        public void ShowRules(Action onClosed)
        {
            _onRulesClosed = onClosed;
            _rulesOpen = true;
            _closeNextFrame = false;
            _rulesPanel.SetActive(true);
        }

        /// <summary>퍼즐이 실제로 시작될 때 - 얇은 진행 줄을 보인다.</summary>
        public void ShowBar()
        {
            _bar.SetActive(true);
            Refresh();
        }

        private void BuildFailPanel(Transform parent)
        {
            var panel = HudUi.CreateImage(parent, "PuzzleFail", new Color(0.08f, 0.06f, 0.08f, 0.95f));
            PixelUi.Slice(panel, "Panel", 2f);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(460f, 190f);
            _failPanel = panel.gameObject;

            var title = HudUi.CreateText(panel.transform, "Title", 22, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 60f);
            title.rectTransform.sizeDelta = new Vector2(440f, 30f);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.5f, 0.45f);
            title.text = Loc.T("실패!");

            _failReason = HudUi.CreateText(panel.transform, "Reason", 15, TextAnchor.MiddleCenter);
            _failReason.rectTransform.anchoredPosition = new Vector2(0f, 26f);
            _failReason.rectTransform.sizeDelta = new Vector2(440f, 24f);

            Button(panel.transform, new Vector2(-108f, -40f), Loc.T("[R] 다시 도전"), Key.R, new Color(0.2f, 0.35f, 0.55f, 0.95f));
            _backLabel = Button(panel.transform, new Vector2(108f, -40f), string.Empty, Key.B, new Color(0.3f, 0.3f, 0.34f, 0.95f));
            _failPanel.SetActive(false);
        }

        private Text Button(Transform parent, Vector2 pos, string label, Key key, Color color)
        {
            var image = HudUi.CreateImage(parent, "Button", color);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 46f);
            rect.anchoredPosition = pos;
            var text = HudUi.CreateText(image.transform, "Text", 16, TextAnchor.MiddleCenter);
            HudUi.Stretch(text.rectTransform, 2f);
            text.text = label;
            GameInput.RegisterTap(rect, key, () => this != null && _failOpen);
            return text;
        }

        public void Refresh()
        {
            if (_status != null && _puzzle != null)
                _status.text = $"<color=#FFD873><b>{Kind} · {_puzzle.Title}</b></color>   {_puzzle.Status}";
        }

        public void ShowFail(string reason, string backLabel, Action retry, Action back)
        {
            _failReason.text = reason;
            _backLabel.text = backLabel;
            _onRetry = retry;
            _onBack = back;
            _failOpen = true;
            _failPanel.SetActive(true);
        }

        private void OnDestroy()
        {
            if (_active == this)
                _active = null; // 퍼즐이 끝나면 카메라 영역을 원래대로
        }

        private void Update()
        {
            if (_closeNextFrame)
            {
                _closeNextFrame = false;
                var closed = _onRulesClosed;
                _onRulesClosed = null;
                closed?.Invoke();
                return;
            }
            if (_rulesOpen)
            {
                if (GameInput.Down(Key.Space) || GameInput.Down(Key.Enter) || GameInput.Down(Key.NumpadEnter) || GameInput.Down(Key.H) || GameInput.Down(Key.Escape))
                {
                    _rulesOpen = false;
                    _closeNextFrame = true;
                    _rulesPanel.SetActive(false);
                }
                return; // 설명을 읽는 동안엔 시간도 멈춘다
            }
            if (!_failOpen)
            {
                _puzzle?.Tick(Time.deltaTime);
                return;
            }
            if (GameInput.Down(Key.R))
            {
                _failOpen = false;
                _onRetry?.Invoke();
            }
            else if (GameInput.Down(Key.B))
            {
                _failOpen = false;
                _onBack?.Invoke();
            }
        }
    }
}
