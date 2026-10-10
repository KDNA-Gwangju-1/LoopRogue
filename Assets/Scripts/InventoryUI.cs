using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>화면 아래 액션바의 퀵슬롯 3칸(1/2/3 키, GameHUD의 Q/E 칸 옆 - 배치는 ActionBarLayout) + 가방 칸 + I 키로 여닫는
    /// 인벤토리 창(아이템 10칸). 아이템은 퀵슬롯에 등록된 것만 쓸 수 있고(사용자 결정), 창은 등록용이다 - 방향키나 클릭으로 고르고
    /// 1/2/3 키나 아래 퀵슬롯 칸 클릭 = 그 퀵슬롯에 등록, I·닫기 버튼 = 닫기. 창이 열려 있는 동안은 게임 입력이 멈춘다
    /// (PlayerActor.CanAct). 업그레이드 카드와 같은 이유로 EventSystem 없이 Keyboard/Mouse.current로 직접 읽는다.</summary>
    public class InventoryUI : MonoBehaviour
    {
        private const int Columns = 5;
        private static readonly Color SlotUsedFlash = new Color(0.95f, 0.75f, 0.25f, 1f);
        private static readonly Color CellCursorFill = new Color(0.3f, 0.42f, 0.62f, 1f);

        /// <summary>인벤토리 창이 열려 있으면 true - 이동/공격 입력을 막는다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>창을 닫은 프레임 - 닫으면서 누른 Enter/Space가 같은 프레임에 대기 등으로 한 번 더 읽히지 않게(PlayerActor).</summary>
        public static int LastCloseFrame { get; private set; } = -1;

        private PlayerActor _player;
        private GameObject _panel;
        private readonly HudSlot[] _cells = new HudSlot[ItemInfo.Count];
        private Text _detailName;
        private Text _detail;
        private RectTransform _closeButton;
        private readonly HudSlot[] _quickSlots = new HudSlot[Inventory.QuickSlotCount];
        private HudSlot _bagSlot;
        private int _cursor;

        private const float QuickUseFlashDuration = 0.35f;
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
                ToggleOpen();

            HandleMouse();

            if (IsOpen)
                HandlePanelInput(keyboard);

            Refresh();
        }

        private void ToggleOpen()
        {
            if (IsOpen)
                SetOpen(false);
            else if (_player.CanAct)
                SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            if (IsOpen && !open)
                LastCloseFrame = Time.frameCount;
            IsOpen = open;
            _panel.SetActive(open);
        }

        /// <summary>가방 칸 = 여닫기. 창이 열려 있을 때만: 칸 클릭 = 고르기, 퀵슬롯 칸 클릭 = 고른 아이템 등록, 닫기 버튼.</summary>
        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || PauseMenu.BlocksInput)
                return;
            var pos = mouse.position.ReadValue();

            if (_bagSlot.Contains(pos))
            {
                ToggleOpen();
                return;
            }
            if (!IsOpen)
                return;

            if (RectTransformUtility.RectangleContainsScreenPoint(_closeButton, pos, null))
            {
                SetOpen(false);
                return;
            }
            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Contains(pos))
                {
                    _cursor = i;
                    return;
                }
            }
            for (var s = 0; s < _quickSlots.Length; s++)
            {
                if (_quickSlots[s].Contains(pos))
                {
                    Inventory.SetQuickSlot(s, (ItemType)_cursor);
                    return;
                }
            }
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
                var cell = _cells[i];
                var selected = IsOpen && i == _cursor;
                cell.Frame.color = selected ? HudSlot.FrameActive : owned ? HudSlot.FrameReady : HudSlot.FrameNormal;
                cell.Fill.color = selected ? CellCursorFill : owned ? HudSlot.FillOwned : HudSlot.FillNormal;
                cell.Key.text = quick >= 0 ? $"[{quick + 1}]" : string.Empty;
                cell.Label.text = $"{ItemInfo.Name(type)}\n<size=11>{(owned ? Loc.T("보유") : "-")}</size>";
                cell.Label.color = owned ? Color.white : HudSlot.TextDim;
                cell.Icon.color = owned ? Color.white : new Color(0.4f, 0.4f, 0.45f);
            }
            var cur = (ItemType)_cursor;
            _detailName.text = $"{ItemInfo.Name(cur)}{(Inventory.Has(cur) ? "" : Loc.T("  <color=#888888>(없음)</color>"))}";
            _detail.text = Loc.F("{0}\n<color=#9AA0AA>방향키·클릭: 고르기   1/2/3 키·아래 퀵슬롯 클릭: 등록(등록된 것만 사용 가능)   I: 닫기</color>", ItemInfo.Description(cur));

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
                var slot = _quickSlots[s];
                // 창이 열려 있으면 퀵슬롯 칸이 "여기 클릭해서 등록" 자리라는 걸 테두리로 알린다.
                slot.Frame.color = aiming ? HudSlot.FrameActive : IsOpen ? CellCursorFill : owned ? HudSlot.FrameReady : HudSlot.FrameNormal;
                slot.Fill.color = flashing ? SlotUsedFlash : owned ? HudSlot.FillOwned : HudSlot.FillNormal;
                var icon = item.HasValue && !flashing ? ItemIcon(item.Value) : null;
                slot.SetIcon(icon);
                slot.Icon.color = owned ? Color.white : new Color(0.4f, 0.4f, 0.45f);
                slot.Label.text = !item.HasValue ? Loc.T("<size=11>비어\n있음</size>")
                    : flashing ? Loc.T("사용!")
                    : icon != null ? string.Empty
                    : ItemInfo.Name(item.Value).Replace(" ", "\n");
                slot.Label.color = owned || flashing ? Color.white : HudSlot.TextDim;
            }

            _bagSlot.Frame.color = IsOpen ? HudSlot.FrameActive : HudSlot.FrameNormal;
        }

        private static Sprite ItemIcon(ItemType type) => PixelUi.Icon($"Item_{type}");

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
            var bottom = new Vector2(0.5f, 0f);
            var size = new Vector2(ActionBarLayout.SlotSize, ActionBarLayout.SlotSize);
            for (var s = 0; s < Inventory.QuickSlotCount; s++)
            {
                _quickSlots[s] = new HudSlot(canvas, $"QuickSlot{s}", bottom,
                    new Vector2(ActionBarLayout.QuickX(s), ActionBarLayout.SlotCenterY), size, (s + 1).ToString());
                _quickSlots[s].Label.supportRichText = true;
            }

            // 가방 칸 - 퀵슬롯 오른쪽, 클릭하면 인벤토리 창 여닫기.
            _bagSlot = new HudSlot(canvas, "BagSlot", bottom, new Vector2(ActionBarLayout.BagX, ActionBarLayout.SlotCenterY), size, "I");
            _bagSlot.SetIcon(PixelUi.Icon("Bag"));
            _bagSlot.Label.text = _bagSlot.Icon.enabled ? string.Empty : Loc.T("가방");
        }

        private void BuildPanel(Transform canvas)
        {
            const float cellW = 120f, cellH = 64f, gap = 8f, side = 24f, header = 52f, footer = 92f;
            var rows = (ItemInfo.Count + Columns - 1) / Columns;
            var panelW = Columns * cellW + (Columns - 1) * gap + side * 2f;
            var panelH = header + rows * cellH + (rows - 1) * gap + footer;

            var panelImage = HudUi.CreateImage(canvas, "InventoryPanel", new Color(0.05f, 0.05f, 0.07f, 0.94f));
            var skinned = PixelUi.Slice(panelImage, "Panel");
            var panelRect = panelImage.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(panelW, panelH);
            panelRect.anchoredPosition = new Vector2(0f, 30f);
            _panel = panelImage.gameObject;
            if (!skinned)
                _panel.AddComponent<Outline>().effectColor = new Color(0.35f, 0.38f, 0.45f, 1f);
            var edge = skinned ? 5f * PixelUi.Scale : 0f; // 테두리 두께

            // 제목 띠
            var headerImage = HudUi.CreateImage(_panel.transform, "Header", new Color(0.12f, 0.13f, 0.17f, 1f));
            var headerRect = headerImage.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(edge, -44f);
            headerRect.offsetMax = new Vector2(-edge, edge > 0f ? -(edge - 4f) : 0f);

            var title = HudUi.CreateText(headerImage.transform, "Title", 17, TextAnchor.MiddleLeft);
            title.supportRichText = true;
            title.text = Loc.T("<b>인벤토리</b>   <size=12><color=#9AA0AA>모두 1회용 · 종류마다 1개 · 죽으면 전부 잃음</color></size>");
            HudUi.Stretch(title.rectTransform, 0f);
            title.rectTransform.offsetMin = new Vector2(16f, 0f);

            var close = HudUi.CreateImage(headerImage.transform, "CloseButton", new Color(0.45f, 0.18f, 0.18f, 1f));
            if (PixelUi.Slice(close, "Button"))
                close.color = new Color(1f, 0.6f, 0.6f);
            _closeButton = close.rectTransform;
            _closeButton.anchorMin = _closeButton.anchorMax = new Vector2(1f, 0.5f);
            _closeButton.pivot = new Vector2(1f, 0.5f);
            _closeButton.sizeDelta = new Vector2(28f, 28f);
            _closeButton.anchoredPosition = new Vector2(-8f, 0f);
            var closeText = HudUi.CreateText(close.transform, "X", 16, TextAnchor.MiddleCenter);
            closeText.text = "X";
            HudUi.Stretch(closeText.rectTransform, 0f);

            var center = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var x = -panelW * 0.5f + side + col * (cellW + gap) + cellW * 0.5f;
                var y = panelH * 0.5f - header - row * (cellH + gap) - cellH * 0.5f;
                _cells[i] = new HudSlot(_panel.transform, $"Item{i}", center, new Vector2(x, y), new Vector2(cellW, cellH), string.Empty);
                _cells[i].Label.fontSize = 15;
                _cells[i].Label.supportRichText = true;
                _cells[i].Key.color = new Color(1f, 0.82f, 0.35f);
                _cells[i].SetIcon(ItemIcon((ItemType)i));
                if (_cells[i].Icon.enabled)
                {
                    _cells[i].Icon.rectTransform.anchoredPosition = new Vector2(-cellW * 0.5f + 34f, 0f);
                    _cells[i].Label.rectTransform.offsetMin = new Vector2(62f, 4f);
                    _cells[i].Label.fontSize = 14;
                }
            }

            // 아래: 고른 아이템 이름 + 설명 + 조작 안내
            _detailName = HudUi.CreateText(_panel.transform, "DetailName", 16, TextAnchor.UpperLeft);
            _detailName.supportRichText = true;
            _detailName.fontStyle = FontStyle.Bold;
            _detailName.color = new Color(1f, 0.85f, 0.45f);
            var nameRect = _detailName.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.offsetMin = new Vector2(side, footer - 34f);
            nameRect.offsetMax = new Vector2(-side, footer - 12f);

            _detail = HudUi.CreateText(_panel.transform, "Detail", 13, TextAnchor.UpperLeft);
            _detail.supportRichText = true;
            _detail.lineSpacing = 1.2f;
            var detailRect = _detail.rectTransform;
            detailRect.anchorMin = Vector2.zero;
            detailRect.anchorMax = new Vector2(1f, 0f);
            detailRect.pivot = new Vector2(0.5f, 0f);
            detailRect.offsetMin = new Vector2(side, 22f);
            detailRect.offsetMax = new Vector2(-side, footer - 38f);

            _panel.SetActive(false);
        }
    }
}
