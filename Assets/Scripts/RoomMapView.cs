using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>시작 방 고르기용 "방 지도 한 줄" - 칸 11개(방 1~10 + 보스)를 가로로 놓고, 고른 칸 아래에 그 방 정보를
    /// 보여준다. 스테이지 입장 창과 사망 창이 같은 위젯 하나를 자기 패널 안으로 옮겨 붙여 쓴다(GameHUD).
    /// 조작: ←/→(A/D) 한 칸, B 보스방, 클릭 = 고르기, 이미 고른 칸 다시 클릭 = 확정(Enter와 같음).
    /// 클릭 판정은 HUD의 다른 버튼과 같은 수동 사각형 히트테스트(EventSystem 없음).</summary>
    public class RoomMapView
    {
        private const float CellWidth = 50f, CellHeight = 40f, Gap = 6f, BossCellWidth = 66f;
        // 도트 문 그림(Resources/UI/Room) - 일반 문 11x14 / 보스 문 15x16 도트를 4배로, 칸 사이는 점선 길, 고른 칸 위엔 화살표.
        private const float PixelScale = 4f, DoorGap = 12f, CellY = 30f;

        private readonly Sprite _door, _doorSelected, _bossDoor, _bossDoorSelected;
        private readonly RectTransform _marker;
        private bool Pixel => _door != null;

        private static readonly Color CellNormal = new Color(0.22f, 0.25f, 0.32f, 0.95f);
        private static readonly Color CellBoss = new Color(0.45f, 0.14f, 0.14f, 0.95f);
        private static readonly Color CellSelected = new Color(0.45f, 0.65f, 0.95f, 1f);
        private static readonly Color CellBossSelected = new Color(0.95f, 0.35f, 0.3f, 1f);

        private readonly RectTransform _root;
        private readonly List<Image> _cells = new List<Image>();
        private readonly List<RectTransform> _cellRects = new List<RectTransform>();
        private readonly Text _info;
        private readonly RectTransform _infoRect;

        private IReadOnlyList<RoomDefinition> _rooms;

        public int Selected { get; private set; }

        public RoomMapView(Transform parent, int roomCount)
        {
            var go = new GameObject("RoomMap", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _root = go.GetComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);

            _door = PixelUi.Get("UI/Room/Room");
            _doorSelected = PixelUi.Get("UI/Room/Room_Selected");
            _bossDoor = PixelUi.Get("UI/Room/RoomBoss");
            _bossDoorSelected = PixelUi.Get("UI/Room/RoomBoss_Selected");
            if (_doorSelected == null || _bossDoor == null || _bossDoorSelected == null)
                _door = null; // 하나라도 없으면 예전 칸으로

            Vector2 SizeOf(bool boss) => Pixel
                ? new Vector2((boss ? _bossDoor : _door).rect.width, (boss ? _bossDoor : _door).rect.height) * PixelScale
                : new Vector2(boss ? BossCellWidth : CellWidth, CellHeight);
            var gap = Pixel ? DoorGap : Gap;

            var total = (roomCount - 1) * (SizeOf(false).x + gap) + SizeOf(true).x;
            _root.sizeDelta = new Vector2(total, CellHeight + 70f);

            // 칸 사이 점선 길 - 문 아래쪽 높이로 줄 전체에 깔고 문이 그 위를 덮는다.
            var path = PixelUi.Get("UI/Room/RoomPath");
            if (Pixel && path != null)
            {
                var pathGo = new GameObject("Path", typeof(RectTransform));
                pathGo.transform.SetParent(_root, false);
                var pathImage = pathGo.AddComponent<Image>();
                pathImage.sprite = path;
                pathImage.type = Image.Type.Tiled;
                pathImage.pixelsPerUnitMultiplier = 1f / PixelScale;
                var pathRect = pathImage.rectTransform;
                pathRect.anchorMin = pathRect.anchorMax = new Vector2(0.5f, 0.5f);
                pathRect.sizeDelta = new Vector2(total, path.rect.height * PixelScale);
                pathRect.anchoredPosition = new Vector2(0f, CellY - SizeOf(false).y * 0.5f + 16f);
            }

            var x = -total * 0.5f;
            for (var i = 0; i < roomCount; i++)
            {
                var isBoss = i == roomCount - 1;
                var size = SizeOf(isBoss);
                // 문은 아래를 맞춰 세운다(보스 문이 더 커서 위로 솟음).
                var y = Pixel ? CellY - SizeOf(false).y * 0.5f + size.y * 0.5f : CellY;
                var (image, text) = CreateBox(_root, $"Cell{i}", new Vector2(x + size.x * 0.5f, y), size);
                text.text = isBoss ? (Pixel ? string.Empty : "BOSS") : (i + 1).ToString();
                text.fontSize = isBoss ? 15 : 17;
                text.fontStyle = FontStyle.Bold;
                text.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                if (Pixel)
                {
                    image.sprite = isBoss ? _bossDoor : _door;
                    text.rectTransform.offsetMin = new Vector2(0f, 4f); // 번호는 문짝 가운데쯤
                }
                else
                    PixelUi.Slice(image, "Slot", 2f); // 칸이 작아서 쇠테 그림은 2배 픽셀로
                _cells.Add(image);
                _cellRects.Add(image.rectTransform);
                x += size.x + gap;
            }

            var markerSprite = PixelUi.Get("UI/Room/RoomMarker");
            if (Pixel && markerSprite != null)
            {
                var markerGo = new GameObject("Marker", typeof(RectTransform));
                markerGo.transform.SetParent(_root, false);
                markerGo.AddComponent<Image>().sprite = markerSprite;
                _marker = markerGo.GetComponent<RectTransform>();
                _marker.anchorMin = _marker.anchorMax = new Vector2(0.5f, 0.5f);
                _marker.sizeDelta = new Vector2(markerSprite.rect.width, markerSprite.rect.height) * PixelScale;
            }

            var info = CreateBox(_root, "Info", new Vector2(0f, -30f), new Vector2(200f, 50f));
            if (!PixelUi.Slice(info.Image, "Panel", 2f))
                info.Image.color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            info.Text.fontSize = 14;
            _info = info.Text;
            _infoRect = info.Image.rectTransform;
        }

        /// <summary>parent 패널 안으로 옮겨 붙이고(입장 창/사망 창 공용) 기본 선택 칸을 정한다.</summary>
        public void Show(Transform parent, Vector2 position, IReadOnlyList<RoomDefinition> rooms, int selected)
        {
            _root.SetParent(parent, false);
            _root.anchoredPosition = position;
            _rooms = rooms;
            Selected = Mathf.Clamp(selected, 0, rooms.Count - 1);
            Refresh();
        }

        /// <summary>매 프레임 - 확정(Enter 또는 고른 칸 다시 클릭)이면 true.</summary>
        public bool HandleInput()
        {
            var count = _rooms.Count;
            var before = Selected;
            var confirm = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                    Selected = (Selected + count - 1) % count;
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                    Selected = (Selected + 1) % count;
                if (keyboard.bKey.wasPressedThisFrame)
                    Selected = count - 1;
                confirm = keyboard.enterKey.wasPressedThisFrame;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var pos = mouse.position.ReadValue();
                for (var i = 0; i < _cellRects.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(_cellRects[i], pos, null))
                        continue;
                    if (i == Selected)
                        confirm = true;
                    Selected = i;
                    break;
                }
            }

            if (Selected != before)
                Refresh();
            PlaceMarker();
            return confirm;
        }

        public static string RoomLabel(int index, int roomCount) => index == roomCount - 1 ? "보스방" : $"방 {index + 1}";

        private void Refresh()
        {
            for (var i = 0; i < _cells.Count; i++)
            {
                var isBoss = i == _cells.Count - 1;
                var selected = i == Selected;
                if (Pixel)
                {
                    // 고른 문은 열리며 빛이 새어 나온다(크기는 그대로 - 도트가 뭉개지지 않게)
                    _cells[i].sprite = isBoss ? (selected ? _bossDoorSelected : _bossDoor) : (selected ? _doorSelected : _door);
                    continue;
                }
                _cells[i].color = selected ? (isBoss ? CellBossSelected : CellSelected) : (isBoss ? CellBoss : CellNormal);
                _cellRects[i].localScale = selected ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
            }
            PlaceMarker();

            var room = _rooms[Selected];
            _info.text = room.IsBossRoom
                ? $"보스방  {room.Width}x{room.Height}\n보스 패턴 {room.BossPatterns.Count}종"
                : $"{RoomLabel(Selected, _rooms.Count)}  {room.Width}x{room.Height}\n몹 {room.EnemyCount}마리  체력 {room.EnemyMaxHealth:0} / 공격력 {room.EnemyAttackPower:0}";

            // 정보 상자를 고른 칸 바로 아래로
            _infoRect.anchoredPosition = new Vector2(_cellRects[Selected].anchoredPosition.x, -30f);
            _infoRect.sizeDelta = new Vector2(room.IsBossRoom ? 160f : 230f, 50f);
        }

        /// <summary>고른 문 위 화살표 - 1도트(4)씩 위아래로 까딱인다.</summary>
        private void PlaceMarker()
        {
            if (_marker == null || _rooms == null)
                return;
            var cell = _cellRects[Selected];
            var bob = Mathf.Sin(Time.unscaledTime * 5f) > 0f ? PixelScale : 0f;
            _marker.anchoredPosition = new Vector2(cell.anchoredPosition.x,
                cell.anchoredPosition.y + cell.sizeDelta.y * 0.5f + _marker.sizeDelta.y * 0.5f + 4f + bob);
        }

        private static (Image Image, Text Text) CreateBox(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return (image, text);
        }
    }
}
