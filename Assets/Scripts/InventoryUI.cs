using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>화면 아래 퀵슬롯 바(3칸, 1/2/3 키) + I 키로 여닫는 인벤토리 창(아이템 10칸). 아이템은 퀵슬롯에 등록된 것만 쓸 수
    /// 있고(사용자 결정), 창은 등록용이다 - 방향키로 고르고 1/2/3 = 그 퀵슬롯에 등록, I = 닫기. 마우스 없이 키보드로만. 창이 열려 있는 동안은 게임 입력이 멈춘다
    /// (PlayerActor.CanAct). 업그레이드 카드와 같은 이유로 EventSystem 없이 Keyboard.current로 직접 읽는다.</summary>
    public class InventoryUI : MonoBehaviour
    {
        private const int Columns = 5;
        private static readonly Color SlotOwned = new Color(0.22f, 0.3f, 0.42f, 0.95f);
        private static readonly Color SlotEmpty = new Color(0.12f, 0.12f, 0.14f, 0.85f);
        private static readonly Color SlotCursor = new Color(0.45f, 0.65f, 0.95f, 1f);

        /// <summary>인벤토리 창이 열려 있으면 true - 이동/공격 입력을 막는다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>창을 닫은 프레임 - 닫으면서 누른 Enter/Space가 같은 프레임에 대기 등으로 한 번 더 읽히지 않게(PlayerActor).</summary>
        public static int LastCloseFrame { get; private set; } = -1;

        private PlayerActor _player;
        private GameObject _panel;
        private readonly Image[] _cellImages = new Image[ItemInfo.Count];
        private readonly Text[] _cellTexts = new Text[ItemInfo.Count];
        private Text _detail;
        private readonly Image[] _quickImages = new Image[Inventory.QuickSlotCount];
        private readonly Text[] _quickTexts = new Text[Inventory.QuickSlotCount];
        private int _cursor;

        private const float QuickUseFlashDuration = 0.35f;
        private static readonly Color SlotUsedFlash = new Color(0.95f, 0.75f, 0.25f, 1f);
        private readonly bool[] _quickWasOwned = new bool[Inventory.QuickSlotCount];
        private readonly float[] _quickFlashUntil = new float[Inventory.QuickSlotCount];

        public void Build(Transform canvas, PlayerActor player)
        {
            _player = player;
            IsOpen = false;
            Inventory.EnsureLoaded();
            BuildQuickBar(canvas);
            BuildPanel(canvas);
            Refresh();
        }

        private void OnDestroy() => IsOpen = false;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _player == null)
                return;

            if (keyboard.iKey.wasPressedThisFrame)
            {
                if (IsOpen)
                    SetOpen(false);
                else if (_player.CanAct)
                    SetOpen(true);
            }

            if (IsOpen)
                HandlePanelInput(keyboard);

            Refresh();
        }

        private void SetOpen(bool open)
        {
            if (IsOpen && !open)
                LastCloseFrame = Time.frameCount;
            IsOpen = open;
            _panel.SetActive(open);
        }

        private void HandlePanelInput(Keyboard keyboard)
        {
            var rows = (ItemInfo.Count + Columns - 1) / Columns;
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                _cursor = (_cursor + ItemInfo.Count - 1) % ItemInfo.Count;
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                _cursor = (_cursor + 1) % ItemInfo.Count;
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                _cursor = (_cursor - Columns + Columns * rows) % (Columns * rows) % ItemInfo.Count;
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                _cursor = (_cursor + Columns) % (Columns * rows) % ItemInfo.Count;

            var type = (ItemType)_cursor;
            if (keyboard.digit1Key.wasPressedThisFrame)
                Inventory.SetQuickSlot(0, type);
            if (keyboard.digit2Key.wasPressedThisFrame)
                Inventory.SetQuickSlot(1, type);
            if (keyboard.digit3Key.wasPressedThisFrame)
                Inventory.SetQuickSlot(2, type);

        }

        private void Refresh()
        {
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var type = (ItemType)i;
                var owned = Inventory.Has(type);
                var quick = QuickIndexOf(type);
                _cellImages[i].color = IsOpen && i == _cursor ? SlotCursor : owned ? SlotOwned : SlotEmpty;
                _cellTexts[i].text = $"{ItemInfo.Name(type)}{(quick >= 0 ? $"  [{quick + 1}]" : "")}\n{(owned ? "보유" : "-")}";
                _cellTexts[i].color = owned ? Color.white : new Color(0.55f, 0.55f, 0.6f);
            }
            var cur = (ItemType)_cursor;
            _detail.text = $"{ItemInfo.Name(cur)} - {ItemInfo.Description(cur)}\n방향키: 고르기   1/2/3: 퀵슬롯 등록(등록된 것만 사용 가능)   I: 닫기";

            for (var s = 0; s < Inventory.QuickSlotCount; s++)
            {
                var item = Inventory.QuickSlot(s);
                var owned = item.HasValue && Inventory.Has(item.Value);
                var aiming = item.HasValue && _player.AimingItem == item;

                // 방금 써서 없어졌으면 잠깐 번쩍 - 이름만 회색으로 바뀌면 썼는지 잘 안 보였다.
                if (_quickWasOwned[s] && !owned)
                    _quickFlashUntil[s] = Time.time + QuickUseFlashDuration;
                _quickWasOwned[s] = owned;

                var flashing = Time.time < _quickFlashUntil[s];
                _quickImages[s].color = flashing ? SlotUsedFlash : aiming ? SlotCursor : owned ? SlotOwned : SlotEmpty;
                _quickTexts[s].text = !item.HasValue ? $"[{s + 1}] 비어 있음"
                    : owned ? $"[{s + 1}] {ItemInfo.Name(item.Value)}\n{(aiming ? "방향 고르기" : "사용 가능")}"
                    : $"[{s + 1}] {ItemInfo.Name(item.Value)}\n{(flashing ? "사용!" : "없음")}";
                _quickTexts[s].color = owned || flashing ? Color.white : new Color(0.45f, 0.45f, 0.5f);
            }
        }

        private static int QuickIndexOf(ItemType type)
        {
            for (var s = 0; s < Inventory.QuickSlotCount; s++)
                if (Inventory.QuickSlot(s) == type)
                    return s;
            return -1;
        }

        // ===================== UI 생성 =====================

        private void BuildQuickBar(Transform canvas)
        {
            const float width = 150f, height = 44f, gap = 8f;
            var total = Inventory.QuickSlotCount * width + (Inventory.QuickSlotCount - 1) * gap;
            for (var s = 0; s < Inventory.QuickSlotCount; s++)
            {
                var x = -total * 0.5f + s * (width + gap) + width * 0.5f;
                var (image, text) = CreateBox(canvas, $"QuickSlot{s}", new Vector2(0.5f, 0f), new Vector2(x, 16f + height * 0.5f), new Vector2(width, height));
                text.fontSize = 13;
                _quickImages[s] = image;
                _quickTexts[s] = text;
            }

            var hint = CreateBox(canvas, "InventoryHint", new Vector2(0.5f, 0f), new Vector2(total * 0.5f + 70f, 16f + height * 0.5f), new Vector2(120f, height));
            hint.Image.color = new Color(0f, 0f, 0f, 0f);
            hint.Text.text = "I: 인벤토리";
            hint.Text.fontSize = 13;
            hint.Text.color = new Color(0.7f, 0.72f, 0.78f);
        }

        private void BuildPanel(Transform canvas)
        {
            const float cellW = 130f, cellH = 56f, gap = 8f;
            var rows = (ItemInfo.Count + Columns - 1) / Columns;
            var panelW = Columns * cellW + (Columns - 1) * gap + 40f;
            var panelH = rows * cellH + (rows - 1) * gap + 110f;

            var (panelImage, _) = CreateBox(canvas, "InventoryPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(panelW, panelH));
            panelImage.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
            _panel = panelImage.gameObject;

            var title = CreateBox(_panel.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(panelW, 30f));
            title.Image.color = new Color(0f, 0f, 0f, 0f);
            title.Text.text = "인벤토리 (모두 1회용, 종류마다 1개, 죽으면 전부 잃음)";
            title.Text.fontSize = 16;

            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var x = -panelW * 0.5f + 20f + col * (cellW + gap) + cellW * 0.5f;
                var y = panelH * 0.5f - 50f - row * (cellH + gap) - cellH * 0.5f;
                var (image, text) = CreateBox(_panel.transform, $"Item{i}", new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(cellW, cellH));
                text.fontSize = 14;
                _cellImages[i] = image;
                _cellTexts[i] = text;
            }

            var detail = CreateBox(_panel.transform, "Detail", new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(panelW - 30f, 50f));
            detail.Image.color = new Color(0f, 0f, 0f, 0f);
            detail.Text.fontSize = 13;
            _detail = detail.Text;

            _panel.SetActive(false);
        }

        private static (Image Image, Text Text) CreateBox(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = SlotEmpty;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 2f);
            textRect.offsetMax = new Vector2(-4f, -2f);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 14;
            return (image, text);
        }
    }
}
