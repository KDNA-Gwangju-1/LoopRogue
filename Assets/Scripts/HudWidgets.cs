using UnityEngine;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>화면 아래 액션바 배치 - 한 줄에 [체력 막대] [Q][E] [1][2][3] [가방], 그 아래 맨 밑에 화면 가로 전체 경험치 막대
    /// (메이플스토리처럼, 사용자 요청). GameHUD(막대, Q/E 칸)와 InventoryUI(퀵슬롯·가방 칸)가 같은 줄에 서야 해서 좌표를 한곳에
    /// 모았다. 전부 캔버스 아래 가운데(0.5, 0) 기준, 기준 해상도 1280x720.</summary>
    public static class ActionBarLayout
    {
        public const float SlotSize = 56f;
        public const float SlotGap = 8f;
        public const float GroupGap = 24f; // 스킬 칸 / 퀵슬롯 칸 / 가방 칸 사이
        public const int SkillCount = 2;

        public const float ExpBarHeight = 16f;
        public const float SlotBottom = ExpBarHeight + 6f;
        public const float HpBarWidth = 320f;
        public const float HpBarHeight = 28f;
        private const float HpBarGap = 20f;

        // HUD가 게임 화면을 가리지 않게(사용자 요청) 위·아래 띠만큼 카메라 영역을 줄인다(GameHUD.ApplyCameraViewport).
        public const float TopBarHeight = 36f;
        public const float BottomAreaHeight = SlotBottom + SlotSize + 6f;
        // 상태 안내 줄은 잠깐만 뜨는 글이라 게임 화면 아래 끝에 겹쳐 띄운다.
        public const float StatusLineBottom = BottomAreaHeight + 6f;

        public static float SlotCenterY => SlotBottom + SlotSize * 0.5f;
        public static float HpBarBottom => SlotCenterY - HpBarHeight * 0.5f;

        private static float SlotGroupWidth =>
            (SkillCount + Inventory.QuickSlotCount + 1) * SlotSize + (SkillCount + Inventory.QuickSlotCount - 2) * SlotGap + GroupGap * 2f;

        private static float RowLeft => -(HpBarWidth + HpBarGap + SlotGroupWidth) * 0.5f;
        private static float SlotsLeft => RowLeft + HpBarWidth + HpBarGap;

        public static float HpBarCenterX => RowLeft + HpBarWidth * 0.5f;

        public static float SkillX(int index) => SlotsLeft + index * (SlotSize + SlotGap) + SlotSize * 0.5f;

        public static float QuickX(int slot) =>
            SlotsLeft + SkillCount * (SlotSize + SlotGap) - SlotGap + GroupGap + slot * (SlotSize + SlotGap) + SlotSize * 0.5f;

        public static float BagX => QuickX(Inventory.QuickSlotCount - 1) + SlotSize + GroupGap;
    }

    /// <summary>정사각형 칸 하나 - 테두리 + 안쪽 배경 + 왼쪽 위 키 글자 + 가운데 이름 + 아래에서 차오르는 쿨다운 덮개와 남은 턴 숫자.
    /// 스킬 칸(GameHUD)과 퀵슬롯·인벤토리 칸(InventoryUI)이 같은 모양을 쓴다.</summary>
    public sealed class HudSlot
    {
        public static readonly Color FrameNormal = new Color(0.35f, 0.38f, 0.45f, 1f);
        public static readonly Color FrameReady = new Color(0.5f, 0.8f, 1f, 1f);
        public static readonly Color FrameActive = new Color(1f, 0.82f, 0.35f, 1f);
        public static readonly Color FillNormal = new Color(0.1f, 0.11f, 0.14f, 0.92f);
        public static readonly Color FillOwned = new Color(0.18f, 0.25f, 0.36f, 0.95f);
        public static readonly Color TextDim = new Color(0.5f, 0.5f, 0.56f);

        public readonly RectTransform Rect;
        public readonly Image Frame;
        public readonly Image Fill;
        public readonly Image Icon;
        public readonly Text Key;
        public readonly Text Label;
        private readonly RectTransform _cooldownCover;
        private readonly Text _cooldownText;
        private readonly float _inset; // 도트 쇠테(UI/Common/Slot)가 있으면 그 안쪽부터

        public HudSlot(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, string key)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Rect = go.GetComponent<RectTransform>();
            Rect.anchorMin = anchor;
            Rect.anchorMax = anchor;
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.anchoredPosition = position;
            Rect.sizeDelta = size;
            Frame = go.AddComponent<Image>();
            // 쇠테 그림은 밝은 회색이라 Frame.color(상태 색)가 그대로 곱해져 입혀진다.
            _inset = PixelUi.Slice(Frame, "Slot") ? 3f * PixelUi.Scale : 2f;
            Frame.color = FrameNormal;

            Fill = HudUi.CreateImage(go.transform, "Fill", FillNormal);
            HudUi.Stretch(Fill.rectTransform, _inset);

            // 아이콘(SetIcon) - 12x12 도트를 4배로, 기본은 가운데
            Icon = HudUi.CreateImage(go.transform, "Icon", Color.white);
            Icon.rectTransform.sizeDelta = new Vector2(12f, 12f) * PixelUi.Scale;
            Icon.enabled = false;

            Label = HudUi.CreateText(go.transform, "Label", 12, TextAnchor.MiddleCenter);
            HudUi.Stretch(Label.rectTransform, 4f);
            Label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            // 쿨다운 덮개 - 아래 기준으로 높이를 남은 비율만큼(아래에서 위로 줄어든다).
            var cover = HudUi.CreateImage(go.transform, "Cooldown", new Color(0f, 0f, 0f, 0.65f));
            _cooldownCover = cover.rectTransform;
            _cooldownCover.anchorMin = Vector2.zero;
            _cooldownCover.anchorMax = new Vector2(1f, 0f);
            _cooldownCover.pivot = new Vector2(0.5f, 0f);
            _cooldownCover.offsetMin = new Vector2(_inset, _inset);
            _cooldownCover.offsetMax = new Vector2(-_inset, _inset);

            _cooldownText = HudUi.CreateText(go.transform, "CooldownText", 22, TextAnchor.MiddleCenter);
            _cooldownText.fontStyle = FontStyle.Bold;
            HudUi.Stretch(_cooldownText.rectTransform, 0f);

            Key = HudUi.CreateText(go.transform, "Key", 11, TextAnchor.UpperLeft);
            Key.text = key;
            Key.color = new Color(0.85f, 0.88f, 0.95f);
            HudUi.Stretch(Key.rectTransform, 3f);

            SetCooldown(0, 1);
        }

        /// <summary>남은 턴이 0이면 덮개와 숫자를 숨긴다.</summary>
        public void SetCooldown(int remaining, int max)
        {
            var on = remaining > 0;
            if (_cooldownCover.gameObject.activeSelf != on)
                _cooldownCover.gameObject.SetActive(on);
            _cooldownText.text = on ? remaining.ToString() : string.Empty;
            if (!on)
                return;
            var ratio = Mathf.Clamp01(remaining / (float)Mathf.Max(1, max));
            var inner = Rect.sizeDelta.y - _inset * 2f;
            _cooldownCover.sizeDelta = new Vector2(_cooldownCover.sizeDelta.x, inner * ratio);
        }

        /// <summary>아이콘 그림(없으면 숨김). 아이콘이 있으면 호출부가 이름 글자를 비우거나 옆으로 옮긴다.</summary>
        public void SetIcon(Sprite sprite)
        {
            if (Icon.sprite != sprite)
                Icon.sprite = sprite;
            if (Icon.enabled != (sprite != null))
                Icon.enabled = sprite != null;
        }

        public bool Contains(Vector2 screenPos) => RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPos, null);
    }

    /// <summary>가로 막대 - 배경 + 왼쪽부터 차는 채움(앵커 방식, 스프라이트 없는 Image도 동작) + 가운데 글자.</summary>
    public sealed class HudBar
    {
        public readonly RectTransform Rect;
        public readonly Text Label;
        private readonly RectTransform _fill;
        private readonly RectTransform _overlay;

        public HudBar(Transform parent, string name, Vector2 position, Vector2 size, Color fillColor, int fontSize, Color? overlayColor = null)
        {
            var bg = HudUi.CreateImage(parent, name, new Color(0.06f, 0.06f, 0.08f, 0.9f));
            Rect = bg.rectTransform;
            Rect.anchorMin = new Vector2(0.5f, 0f);
            Rect.anchorMax = new Vector2(0.5f, 0f);
            Rect.pivot = new Vector2(0.5f, 0f);
            Rect.anchoredPosition = position;
            Rect.sizeDelta = size;

            _fill = HudUi.CreateImage(bg.transform, "Fill", fillColor).rectTransform;
            HudUi.Stretch(_fill, 2f);

            // 덧칠 띠(보호막 등) - 막대 위쪽 40%에 겹쳐 그린다.
            if (overlayColor.HasValue)
            {
                _overlay = HudUi.CreateImage(bg.transform, "Overlay", overlayColor.Value).rectTransform;
                _overlay.anchorMin = new Vector2(0f, 0.6f);
                _overlay.anchorMax = new Vector2(0f, 1f);
                _overlay.offsetMin = new Vector2(2f, 0f);
                _overlay.offsetMax = new Vector2(0f, -2f);
            }

            // 도트 쇠테(가운데 투명) - 채움 위, 글자 아래. 막대가 얇아서 2배 픽셀로.
            var frame = HudUi.CreateImage(bg.transform, "Frame", Color.white);
            if (PixelUi.Slice(frame, "BarFrame", 2f))
                HudUi.Stretch(frame.rectTransform, 0f);
            else
                Object.Destroy(frame.gameObject);

            Label = HudUi.CreateText(bg.transform, "Label", fontSize, TextAnchor.MiddleCenter);
            Label.supportRichText = true;
            HudUi.Stretch(Label.rectTransform, 0f);
        }

        /// <summary>화면 아래 가로 전체로 펼친다(경험치 막대).</summary>
        public void StretchAcrossBottom(float height)
        {
            Rect.anchorMin = Vector2.zero;
            Rect.anchorMax = new Vector2(1f, 0f);
            Rect.pivot = new Vector2(0.5f, 0f);
            Rect.offsetMin = Vector2.zero;
            Rect.offsetMax = new Vector2(0f, height);
        }

        public void Set(float ratio, string text)
        {
            SetWidth(_fill, ratio);
            Label.text = text;
        }

        public void SetOverlay(float ratio)
        {
            if (_overlay != null)
                SetWidth(_overlay, ratio);
        }

        /// <summary>안쪽 여백(2px) 때문에 비율이 아주 작으면 폭이 음수가 되므로 그땐 숨긴다.</summary>
        private void SetWidth(RectTransform part, float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            var visible = ratio * Rect.rect.width > 4f;
            if (part.gameObject.activeSelf != visible)
                part.gameObject.SetActive(visible);
            part.anchorMax = new Vector2(ratio, part.anchorMax.y);
        }
    }

    /// <summary>도트 UI 그림 공용 - Resources/UI/Common(9-slice 패널·버튼·칸·막대·띠), Resources/UI/Icons(아이콘).
    /// 타이틀·로비와 같은 4배 픽셀. 그림이 없으면 Slice가 false를 돌려주고 호출부는 예전 단색을 그대로 쓴다.</summary>
    public static class PixelUi
    {
        public const float Scale = 4f;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> Cache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        public static Sprite Get(string path)
        {
            if (!Cache.TryGetValue(path, out var sprite) || sprite == null)
            {
                sprite = Resources.Load<Sprite>(path);
                Cache[path] = sprite;
            }
            return sprite;
        }

        public static Sprite Icon(string name) => Get($"UI/Icons/{name}");

        /// <summary>9-slice로 입힌다. 색은 흰색으로 바꿔 그림 색 그대로(상태 색을 입힐 땐 호출부가 다시 color를 넣는다).</summary>
        public static bool Slice(Image image, string name, float scale = Scale)
        {
            var sprite = Get($"UI/Common/{name}");
            if (sprite == null)
                return false;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f / scale;
            image.color = Color.white;
            return true;
        }

        /// <summary>단색 대신 그림을 입힐 때 원래 색을 옅은 물빛으로만 남긴다(그림 색이 묻히지 않게).</summary>
        public static Color Tint(Color color, float strength = 0.4f)
        {
            var tint = Color.Lerp(Color.white, new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f), strength);
            tint.a = 1f;
            return tint;
        }
    }

    public static class HudUi
    {
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>부모를 꽉 채우고 사방으로 inset만큼 들여쓴다.</summary>
        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
