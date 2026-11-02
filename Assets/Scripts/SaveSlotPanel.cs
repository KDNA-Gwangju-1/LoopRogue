using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>타이틀 세이브 슬롯 선택(11-02 사용자 "타이틀에서 3칸 선택") - "게임 시작"을 누르면 뜬다. 슬롯마다 층·레벨·골드·클리어·플레이 시간,
    /// 빈 슬롯은 "새 모험". W/S·방향키·마우스로 고르고 Enter·클릭 = 그 슬롯으로 시작, Delete = 그 슬롯 지우기(3초 안에 한 번 더), Esc = 닫기.
    /// 업적 창처럼 EventSystem 없이 직접 입력을 읽고 자체 캔버스에 그린다.</summary>
    public class SaveSlotPanel : MonoBehaviour
    {
        private const float CardWidth = 560f;
        private const float CardHeight = 84f;
        private const float Gap = 12f;
        private const float DeleteConfirmSeconds = 3f;

        private static readonly Color CardNormal = new Color(0.14f, 0.15f, 0.2f, 0.97f);
        private static readonly Color CardCurrent = new Color(0.18f, 0.3f, 0.22f, 0.97f);
        private static readonly Color CursorTint = new Color(0.55f, 0.65f, 0.85f, 1f);

        public static bool IsOpen { get; private set; }
        public static bool BlocksInput => IsOpen || Time.frameCount == _closedFrame;
        private static int _closedFrame = -1;

        private GameObject _panel;
        private readonly RectTransform[] _cardRects = new RectTransform[Prefs.SlotCount];
        private readonly Image[] _cardImages = new Image[Prefs.SlotCount];
        private readonly Text[] _cardTexts = new Text[Prefs.SlotCount];
        private Text _hint;
        private int _cursor;
        private int _openedFrame = -1;
        private int _deleteSlot;          // 지우기 확인 중인 슬롯(0 = 없음)
        private float _deleteUntil;
        private Action _onChosen;

        public static SaveSlotPanel Create()
        {
            var canvasGo = new GameObject("SaveSlotCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var panel = canvasGo.AddComponent<SaveSlotPanel>();
            panel.Build(canvasGo.transform);
            return panel;
        }

        private void OnDestroy() => IsOpen = false;

        /// <param name="onChosen">슬롯을 고른 뒤(이미 Prefs.SelectSlot이 끝난 상태) - 게임 시작</param>
        public void Open(Action onChosen)
        {
            _onChosen = onChosen;
            _cursor = Prefs.Slot - 1;
            _deleteSlot = 0;
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
            if (_deleteSlot != 0 && Time.unscaledTime > _deleteUntil)
                _deleteSlot = 0;

            if (GameInput.Down(Key.Escape))
            {
                Close();
                return;
            }
            if (GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W))
                _cursor = (_cursor + Prefs.SlotCount - 1) % Prefs.SlotCount;
            if (GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S))
                _cursor = (_cursor + 1) % Prefs.SlotCount;
            for (var i = 0; i < Prefs.SlotCount; i++)
                if (GameInput.Down(Key.Digit1 + i))
                {
                    Choose(i + 1);
                    return;
                }
            if (GameInput.Down(Key.Delete))
                Delete(_cursor + 1);
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.Space))
            {
                Choose(_cursor + 1);
                return;
            }

            if (Pointer.current != null)
            {
                var pos = GameInput.PointerPosition;
                for (var i = 0; i < Prefs.SlotCount; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_cardRects[i], pos, null))
                        continue;
                    if (GameInput.PointerDelta.sqrMagnitude > 0f)
                        _cursor = i;
                    if (GameInput.PointerDown)
                    {
                        Choose(i + 1);
                        return;
                    }
                }
            }
            Refresh();
        }

        private void Choose(int slot)
        {
            Close();
            if (slot != Prefs.Slot)
                Prefs.SelectSlot(slot);
            _onChosen?.Invoke();
        }

        /// <summary>실수 방지로 두 번 - 첫 번째는 카드를 붉게 바꾸고 안내만.</summary>
        private void Delete(int slot)
        {
            if (IsEmpty(slot))
                return;
            if (_deleteSlot != slot)
            {
                _deleteSlot = slot;
                _deleteUntil = Time.unscaledTime + DeleteConfirmSeconds;
                return;
            }
            _deleteSlot = 0;
            var current = Prefs.Slot;
            if (current != slot)
                Prefs.SelectSlot(slot);
            SaveReset.ResetAll();
            if (current != slot)
                Prefs.SelectSlot(current);
            Refresh();
        }

        private static bool IsEmpty(int slot) =>
            !Prefs.PeekHasKey(slot, StageProgress.StageKey) && !Prefs.PeekHasKey(slot, "LoopRogue_Gold") && !Prefs.PeekHasKey(slot, "LoopRogue_Run_Level");

        private static string Summary(int slot)
        {
            if (IsEmpty(slot))
                return Loc.T("<color=#8A8A92>비어 있음 - 새 모험</color>");
            var minutes = Mathf.FloorToInt(Prefs.PeekFloat(slot, "LoopRogue_PlayTimeSeconds", 0f) / 60f);
            var clears = Prefs.PeekInt(slot, "LoopRogue_GameClears", 0);
            var text = Loc.F("{0}층 · 레벨 {1} · 골드 {2:N0} · 플레이 {3}시간 {4}분",
                Prefs.PeekInt(slot, StageProgress.StageKey, 1), Prefs.PeekInt(slot, "LoopRogue_Run_Level", 1),
                Prefs.PeekInt(slot, "LoopRogue_Gold", 0), minutes / 60, minutes % 60);
            if (clears > 0)
                text += Loc.F("  · <color=#FFD966>클리어 {0}회</color>", clears);
            return text;
        }

        private void Refresh()
        {
            for (var i = 0; i < Prefs.SlotCount; i++)
            {
                var slot = i + 1;
                var deleting = _deleteSlot == slot;
                var color = deleting ? new Color(0.5f, 0.15f, 0.15f, 0.97f) : slot == Prefs.Slot ? CardCurrent : CardNormal;
                if (i == _cursor)
                    color = Color.Lerp(color, CursorTint, 0.4f);
                var image = _cardImages[i];
                image.color = image.sprite != null ? PixelUi.Tint(color, 0.5f) : color;
                var title = Loc.F("<b>슬롯 {0}</b>  <size=12><color=#9AA0AA>[{0}]</color></size>", slot);
                if (slot == Prefs.Slot)
                    title += Loc.T("  <size=12><color=#8CE08C>마지막으로 한 슬롯</color></size>");
                _cardTexts[i].text = deleting
                    ? title + "\n" + Loc.T("<color=#FF9A80>정말 지울까요? Delete를 한 번 더</color>")
                    : title + "\n<size=14>" + Summary(slot) + "</size>";
            }
        }

        private void Build(Transform canvas)
        {
            const float header = 60f, footer = 40f, side = 30f;
            var height = header + Prefs.SlotCount * CardHeight + (Prefs.SlotCount - 1) * Gap + footer;

            var dim = HudUi.CreateImage(canvas, "Dim", new Color(0f, 0f, 0f, 0.55f));
            HudUi.Stretch(dim.rectTransform, 0f);
            var panelImage = HudUi.CreateImage(dim.transform, "SaveSlotPanel", new Color(0.05f, 0.05f, 0.07f, 0.96f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CardWidth + side * 2f, height);
            _panel = dim.gameObject;
            if (!skinned)
                panelImage.gameObject.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);

            var title = HudUi.CreateText(panelImage.transform, "Title", 20, TextAnchor.MiddleCenter);
            title.text = Loc.T("<b>세이브 슬롯</b>");
            title.supportRichText = true;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(side, -header);
            titleRect.offsetMax = new Vector2(-side, -8f);

            for (var i = 0; i < Prefs.SlotCount; i++)
            {
                var card = HudUi.CreateImage(panelImage.transform, $"Slot{i + 1}", CardNormal);
                PixelUi.Slice(card, "Button");
                var cardRect = card.rectTransform;
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(0f, 1f);
                cardRect.pivot = new Vector2(0f, 1f);
                cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
                cardRect.anchoredPosition = new Vector2(side, -(header + i * (CardHeight + Gap)));
                var text = HudUi.CreateText(card.transform, "Text", 17, TextAnchor.MiddleLeft);
                text.supportRichText = true;
                text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                HudUi.Stretch(text.rectTransform, 0f);
                text.rectTransform.offsetMin = new Vector2(18f, 0f);
                text.rectTransform.offsetMax = new Vector2(-12f, 0f);
                _cardRects[i] = cardRect;
                _cardImages[i] = card;
                _cardTexts[i] = text;
            }

            _hint = HudUi.CreateText(panelImage.transform, "Hint", 12, TextAnchor.MiddleCenter);
            _hint.text = Loc.T("W/S·마우스: 고르기   Enter·클릭·숫자: 시작   Delete: 슬롯 지우기(두 번)   Esc: 닫기   ·   설정은 모든 슬롯 공용");
            _hint.color = new Color(0.6f, 0.63f, 0.7f);
            var hintRect = _hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.offsetMin = new Vector2(side, 6f);
            hintRect.offsetMax = new Vector2(-side, footer - 6f);

            _panel.SetActive(false);
        }
    }
}
