using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비 층 선택(11-02 사용자) - 10층 보스를 잡고 엔딩에서 "그대로 계속"을 고른 뒤(StageProgress.FinalBossCleared) 로비 문으로 들어가면 뜬다.
    /// 1~10층을 5칸 × 2줄로 보여 주고, 방향키·WASD·마우스로 고르고 Enter·클릭(또는 숫자 1~9, 0 = 10층)으로 들어간다. Esc = 취소.
    /// 업적 창처럼 EventSystem 없이 직접 입력을 읽고 자체 캔버스에 그린다.</summary>
    public class StageSelectPanel : MonoBehaviour
    {
        private const int Columns = 5;
        private const float CellWidth = 150f;
        private const float CellHeight = 74f;
        private const float Gap = 12f;

        private static readonly Color CellNormal = new Color(0.14f, 0.15f, 0.2f, 0.97f);
        private static readonly Color CellCurrent = new Color(0.2f, 0.36f, 0.26f, 0.97f);
        private static readonly Color CursorTint = new Color(0.55f, 0.65f, 0.85f, 1f);

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private readonly RectTransform[] _cellRects = new RectTransform[StageProgress.MaxStage];
        private readonly Image[] _cellImages = new Image[StageProgress.MaxStage];
        private int _cursor;
        private int _openedFrame = -1;
        private Action<int> _onPick;
        private Action _onCancel;

        public static StageSelectPanel Create()
        {
            var canvasGo = new GameObject("StageSelectCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = canvasGo.AddComponent<StageSelectPanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        /// <param name="onPick">고른 층(1~10)</param>
        public void Open(Action<int> onPick, Action onCancel)
        {
            _onPick = onPick;
            _onCancel = onCancel;
            _cursor = Mathf.Clamp(StageProgress.CurrentStage, 1, StageProgress.MaxStage) - 1;
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

            if (GameInput.Down(Key.Escape))
            {
                Close();
                _onCancel?.Invoke();
                return;
            }
            var count = StageProgress.MaxStage;
            if (GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A))
                _cursor = (_cursor + count - 1) % count;
            if (GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D))
                _cursor = (_cursor + 1) % count;
            if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W) || GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                _cursor = (_cursor + Columns) % count;
            for (var i = 0; i < count; i++)
                if (GameInput.Down(i == 9 ? Key.Digit0 : Key.Digit1 + i))
                {
                    Pick(i);
                    return;
                }
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
            {
                Pick(_cursor);
                return;
            }

            if (Pointer.current != null)
            {
                var pos = GameInput.PointerPosition;
                for (var i = 0; i < count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_cellRects[i], pos, null))
                        continue;
                    if (GameInput.PointerDelta.sqrMagnitude > 0f)
                        _cursor = i;
                    if (GameInput.PointerDown)
                    {
                        Pick(i);
                        return;
                    }
                }
            }
            Refresh();
        }

        private void Pick(int index)
        {
            Close();
            _onPick?.Invoke(index + 1);
        }

        private void Refresh()
        {
            for (var i = 0; i < _cellImages.Length; i++)
            {
                var color = i + 1 == StageProgress.CurrentStage ? CellCurrent : CellNormal;
                if (i == _cursor)
                    color = Color.Lerp(color, CursorTint, 0.45f);
                var image = _cellImages[i];
                image.color = image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;
            }
        }

        private void Build(Transform canvas)
        {
            var rows = (StageProgress.MaxStage + Columns - 1) / Columns;
            var gridW = Columns * CellWidth + (Columns - 1) * Gap;
            var gridH = rows * CellHeight + (rows - 1) * Gap;
            const float header = 60f, footer = 40f, side = 30f;

            var panelImage = HudUi.CreateImage(canvas, "StageSelectPanel", new Color(0.05f, 0.05f, 0.07f, 0.96f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(gridW + side * 2f, header + gridH + footer);
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            var title = HudUi.CreateText(_panel.transform, "Title", 20, TextAnchor.MiddleCenter);
            title.text = Loc.T("<b>들어갈 층 고르기</b>");
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(side, -header);
            titleRect.offsetMax = new Vector2(-side, -8f);

            for (var i = 0; i < StageProgress.MaxStage; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var cell = HudUi.CreateImage(_panel.transform, $"Stage{i + 1}", CellNormal);
                PixelUi.Slice(cell, "Button");
                var cellRect = cell.rectTransform;
                cellRect.anchorMin = cellRect.anchorMax = new Vector2(0f, 1f);
                cellRect.pivot = new Vector2(0f, 1f);
                cellRect.sizeDelta = new Vector2(CellWidth, CellHeight);
                cellRect.anchoredPosition = new Vector2(side + col * (CellWidth + Gap), -(header + row * (CellHeight + Gap)));
                var text = HudUi.CreateText(cell.transform, "Text", 15, TextAnchor.MiddleCenter);
                text.supportRichText = true;
                var key = i == 9 ? "0" : (i + 1).ToString();
                text.text = Loc.F("<b>{0}층</b>  <size=12><color=#9AA0AA>[{1}]</color></size>", i + 1, key) +
                            $"\n<size=12><color=#C8CCD6>{BossBrain.BossName(i + 1)}</color></size>";
                text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                HudUi.Stretch(text.rectTransform, 4f);
                _cellRects[i] = cellRect;
                _cellImages[i] = cell;
            }

            var hint = HudUi.CreateText(_panel.transform, "Hint", 12, TextAnchor.MiddleCenter);
            hint.text = Loc.T("방향키·마우스: 고르기   Enter·클릭·숫자: 들어가기   Esc: 취소   ·   초록 칸 = 지금 층(다른 층은 방 1부터)");
            hint.color = new Color(0.6f, 0.63f, 0.7f);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(side, 6f);
            hintRect.offsetMax = new Vector2(-side, footer - 6f);

            _panel.SetActive(false);
        }
    }
}
