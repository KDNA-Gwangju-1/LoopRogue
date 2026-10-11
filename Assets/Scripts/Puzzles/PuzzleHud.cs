using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>퍼즐·미니게임 안내 패널 - 화면 위 띠 바로 아래에 이름/규칙/진행 상황, 오른쪽에 [G] 포기 버튼.
    /// 실패하면 가운데에 "[R] 다시 도전 / [B] 이전 방으로" 창. 자기 캔버스를 따로 만든다(레벨업 카드 창(HUD)보다 아래가 아니라
    /// 위지만, 성공하면 카드가 뜨기 전에 사라지고 실패 창이 떠 있는 동안엔 카드가 안 뜬다).</summary>
    public class PuzzleHud : MonoBehaviour
    {
        private const float PanelWidth = 640f, PanelHeight = 104f;

        private PuzzleBase _puzzle;
        private Text _title, _rule, _status;
        private GameObject _failPanel;
        private Text _failReason, _backLabel;
        private Action _onRetry, _onBack;
        private bool _failOpen;

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

            var panel = HudUi.CreateImage(canvasGo.transform, "PuzzlePanel", new Color(0.08f, 0.09f, 0.12f, 0.92f));
            PixelUi.Slice(panel, "Panel", 2f);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rect.anchoredPosition = new Vector2(0f, -ActionBarLayout.TopBarHeight - 6f);

            var kind = PuzzleBase.IsMiniGame(_puzzle.Type) ? Loc.T("미니게임") : Loc.T("퍼즐");
            _title = Line(panel.transform, 18, -8f, new Color(1f, 0.85f, 0.45f));
            _title.fontStyle = FontStyle.Bold;
            _title.text = $"{kind} · {_puzzle.Title}";
            _rule = Line(panel.transform, 14, -34f, new Color(0.88f, 0.9f, 0.95f), height: 34f);
            _rule.resizeTextForBestFit = true; // 규칙이 길면(번역 등) 두 줄로 줄여서
            _rule.resizeTextMinSize = 10;
            _rule.resizeTextMaxSize = 14;
            _rule.text = _puzzle.Rule;
            _status = Line(panel.transform, 14, -72f, new Color(0.55f, 0.9f, 1f));

            // 포기 버튼 - 클릭/터치하면 G 키와 같다
            var giveUp = HudUi.CreateImage(panel.transform, "GiveUp", new Color(0.35f, 0.12f, 0.12f, 0.95f));
            var giveRect = giveUp.rectTransform;
            giveRect.anchorMin = giveRect.anchorMax = new Vector2(1f, 1f);
            giveRect.pivot = new Vector2(1f, 1f);
            giveRect.sizeDelta = new Vector2(84f, 26f);
            giveRect.anchoredPosition = new Vector2(-8f, -8f);
            var giveText = HudUi.CreateText(giveUp.transform, "Text", 13, TextAnchor.MiddleCenter);
            HudUi.Stretch(giveText.rectTransform, 0f);
            giveText.text = Loc.T("포기 [G]");
            GameInput.RegisterTap(giveRect, Key.G, () => this != null && !_failOpen);

            // 퍼즐 전용 키 버튼(상자 밀기 [Z] 되돌리기 등) - 포기 버튼 아래
            if (_puzzle.ExtraKeyLabel != null)
            {
                var extra = HudUi.CreateImage(panel.transform, "Extra", new Color(0.18f, 0.3f, 0.45f, 0.95f));
                var extraRect = extra.rectTransform;
                extraRect.anchorMin = extraRect.anchorMax = new Vector2(1f, 1f);
                extraRect.pivot = new Vector2(1f, 1f);
                extraRect.sizeDelta = new Vector2(84f, 26f);
                extraRect.anchoredPosition = new Vector2(-8f, -38f);
                var extraText = HudUi.CreateText(extra.transform, "Text", 13, TextAnchor.MiddleCenter);
                HudUi.Stretch(extraText.rectTransform, 0f);
                extraText.text = _puzzle.ExtraKeyLabel;
                GameInput.RegisterTap(extraRect, _puzzle.ExtraKey, () => this != null && !_failOpen);
            }

            BuildFailPanel(canvasGo.transform);
        }

        private Text Line(Transform parent, int size, float y, Color color, float height = 0f)
        {
            var text = HudUi.CreateText(parent, "Line", size, TextAnchor.MiddleCenter);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(12f, y - (height > 0f ? height : size + 8f));
            rect.offsetMax = new Vector2(-100f, y);
            text.color = color;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
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
                _status.text = _puzzle.Status;
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

        private void Update()
        {
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
