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

            // 덧칠 구간(보호막 등) - 채움 끝에서 이어지는 막대 높이 전체 구간(SetOverlay). 처음엔 위쪽 40%에 왼쪽부터 겹쳐 그렸는데
            // 체력 위에 얇게 깔려 잘 안 보였다(10-14 피드백).
            if (overlayColor.HasValue)
            {
                _overlay = HudUi.CreateImage(bg.transform, "Overlay", overlayColor.Value).rectTransform;
                _overlay.anchorMin = new Vector2(0f, 0f);
                _overlay.anchorMax = new Vector2(0f, 1f);
                _overlay.offsetMin = new Vector2(0f, 2f);
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

        /// <summary>채움(fillRatio) 바로 뒤에 ratio만큼 덧칠 - 합이 1을 넘으면 오른쪽 끝에 맞춰 채움 위로 겹친다.</summary>
        public void SetOverlay(float fillRatio, float ratio)
        {
            if (_overlay == null)
                return;
            ratio = Mathf.Clamp01(ratio);
            var end = Mathf.Min(1f, Mathf.Clamp01(fillRatio) + ratio);
            var start = Mathf.Max(0f, end - ratio);
            var visible = ratio * Rect.rect.width > 3f;
            if (_overlay.gameObject.activeSelf != visible)
                _overlay.gameObject.SetActive(visible);
            // 채움(_fill)과 같은 좌표 규칙(앵커 비율 + 오른쪽 2px 안쪽)이라 이음새에 틈이 안 생긴다
            _overlay.anchorMin = new Vector2(start, 0f);
            _overlay.anchorMax = new Vector2(end, 1f);
            _overlay.offsetMin = new Vector2(start > 0f ? -2f : 2f, 2f);
            _overlay.offsetMax = new Vector2(-2f, -2f);
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

    /// <summary>화면 위 기믹 게이지 한 줄(10-25) - 로스트아크 아이덴티티 게이지처럼(사용자): 둥근 금장 받침에 기믹 아이콘(Icons/Gimmick_*),
    /// 양 끝 뾰족한 금장 테(GaugeFrame) 안에 빛나는 채움(GaugeFill, 색을 곱함). 글자 없이 그림만.
    /// - 채움 위로 사선 광택(GaugeShine)이 계속 흐르고, 채움 맨 앞은 밝게 빛난다(GaugeEdge).
    /// - steps가 있으면 칸 눈금 - 한 칸 찰 때마다 그 칸이 하얗게 번쩍이고 아이콘이 톡 튄다.
    /// - 가득 차거나 위험하면 막대 둘레가 같은 색으로 맥박 치며 빛난다(GaugeGlow).</summary>
    public sealed class GimmickGauge
    {
        private const float BarHeight = 28f;  // 테 14px × 2
        public const float IconBox = 36f;     // 받침 18px × 2
        private const float InsetX = 12f, InsetY = 8f; // 테 안쪽 채움 자리(6·4px × 2)
        private const float ShineWidth = 32f, ShinePeriod = 1.6f;
        public readonly RectTransform Rect;
        private readonly RectTransform _fill, _fillArea, _shine, _edge, _flash, _slot;
        private readonly Image _fillImage, _edgeImage, _flashImage, _glow, _icon;
        private readonly System.Collections.Generic.List<GameObject> _ticks = new System.Collections.Generic.List<GameObject>();
        private string _iconName;
        private int _steps = -1, _lastStep = -1;
        private float _shown = -1f, _flashT, _punchT;

        public GimmickGauge(Transform parent, float width)
        {
            var root = new GameObject("GimmickGauge", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Rect = (RectTransform)root.transform;
            Rect.anchorMin = Rect.anchorMax = new Vector2(0.5f, 1f);
            Rect.pivot = new Vector2(0.5f, 1f);
            Rect.sizeDelta = new Vector2(width, IconBox);

            var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(root.transform, false);
            bar.anchorMin = new Vector2(0f, 0.5f);
            bar.anchorMax = new Vector2(1f, 0.5f);
            bar.offsetMin = new Vector2(IconBox - 8f, -BarHeight * 0.5f);
            bar.offsetMax = new Vector2(0f, BarHeight * 0.5f);

            // 둘레 발광(맨 뒤) - 평소엔 투명
            _glow = HudUi.CreateImage(bar, "Glow", Color.white);
            HudUi.Stretch(_glow.rectTransform, -10f);
            PixelUi.Slice(_glow, "GaugeGlow", 2f);
            _glow.color = new Color(1f, 1f, 1f, 0f);

            var back = HudUi.CreateImage(bar, "Back", new Color(0.05f, 0.04f, 0.07f, 0.95f)).rectTransform;
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = new Vector2(InsetX, InsetY);
            back.offsetMax = new Vector2(-InsetX, -InsetY);

            _fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            _fillArea.SetParent(bar, false);
            _fillArea.anchorMin = Vector2.zero;
            _fillArea.anchorMax = Vector2.one;
            _fillArea.offsetMin = new Vector2(InsetX, InsetY);
            _fillArea.offsetMax = new Vector2(-InsetX, -InsetY);

            _fillImage = HudUi.CreateImage(_fillArea, "Fill", Color.white);
            _fill = _fillImage.rectTransform;
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = Vector2.one;
            _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            if (PixelUi.Get("UI/Common/GaugeFill") != null)
                PixelUi.Slice(_fillImage, "GaugeFill", 2f);
            _fill.gameObject.AddComponent<RectMask2D>(); // 광택이 채운 데까지만

            var shine = HudUi.CreateImage(_fill, "Shine", new Color(1f, 1f, 1f, 0.55f));
            shine.sprite = PixelUi.Get("UI/Common/GaugeShine");
            if (shine.sprite == null)
                shine.color = new Color(1f, 1f, 1f, 0.18f);
            _shine = shine.rectTransform;
            _shine.anchorMin = new Vector2(0f, 0f);
            _shine.anchorMax = new Vector2(0f, 1f);
            _shine.pivot = new Vector2(0f, 0.5f);
            _shine.sizeDelta = new Vector2(ShineWidth, 0f);

            _flashImage = HudUi.CreateImage(_fillArea, "Flash", new Color(1f, 1f, 1f, 0f));
            _flash = _flashImage.rectTransform;

            _edgeImage = HudUi.CreateImage(_fillArea, "Edge", Color.white);
            _edgeImage.sprite = PixelUi.Get("UI/Common/GaugeEdge");
            _edge = _edgeImage.rectTransform;
            _edge.anchorMin = new Vector2(0f, 0f);
            _edge.anchorMax = new Vector2(0f, 1f);
            _edge.pivot = new Vector2(0.5f, 0.5f);
            _edge.sizeDelta = new Vector2(12f, 4f);

            var frame = HudUi.CreateImage(bar, "Frame", Color.white);
            if (PixelUi.Slice(frame, "GaugeFrame", 2f) || PixelUi.Slice(frame, "BarFrame", 2f))
                HudUi.Stretch(frame.rectTransform, 0f);
            else
                Object.Destroy(frame.gameObject);

            // 둥근 금장 받침 + 아이콘 - 막대 왼쪽 끝을 덮는다
            var slot = HudUi.CreateImage(root.transform, "IconSlot", new Color(0.1f, 0.1f, 0.13f, 0.95f));
            var slotSprite = PixelUi.Get("UI/Common/GaugeIconSlot");
            if (slotSprite != null)
            {
                slot.sprite = slotSprite;
                slot.color = Color.white;
            }
            _slot = slot.rectTransform;
            _slot.anchorMin = _slot.anchorMax = new Vector2(0f, 0.5f);
            _slot.pivot = new Vector2(0.5f, 0.5f);
            _slot.sizeDelta = new Vector2(IconBox, IconBox);
            _slot.anchoredPosition = new Vector2(IconBox * 0.5f, 0f);
            _icon = HudUi.CreateImage(slot.transform, "Icon", Color.white);
            HudUi.Stretch(_icon.rectTransform, 6f);
            _icon.preserveAspect = true;
            _icon.enabled = false;
        }

        public void Set(string icon, float ratio, int steps, Color color, bool alert)
        {
            if (_iconName != icon)
            {
                _iconName = icon;
                _shown = -1f; // 다른 기믹으로 바뀌었다 - 이전 막대에서 미끄러지지 않게
                _lastStep = -1;
                var sprite = icon != null ? PixelUi.Get("UI/Icons/Gimmick_" + icon) : null;
                _icon.sprite = sprite;
                _icon.enabled = sprite != null;
            }
            if (_steps != steps)
                BuildTicks(steps);
            var dt = Time.unscaledDeltaTime;
            ratio = Mathf.Clamp01(ratio);
            // 턴이 넘어가 목표가 바뀌면 부드럽게 차오른다(처음 나타날 땐 바로)
            _shown = _shown < 0f ? ratio : Mathf.MoveTowards(_shown, ratio, dt * 4f);
            var visible = _shown > 0.005f;
            if (_fill.gameObject.activeSelf != visible)
                _fill.gameObject.SetActive(visible);
            _fill.anchorMax = new Vector2(_shown, 1f);

            var full = ratio >= 0.999f;
            var t = Time.unscaledTime;
            var pulse = 0.5f + 0.5f * Mathf.Sin(t * (full ? 9f : 6f));
            _fillImage.color = full || alert ? Color.Lerp(color, Color.white, (full ? 0.3f : 0.15f) * pulse) : color;

            // 광택 - 채운 폭을 왼→오로 지나간다
            var fillWidth = _fillArea.rect.width * _shown;
            var phase = Mathf.Repeat(t / ShinePeriod, 1f);
            _shine.anchoredPosition = new Vector2(Mathf.Lerp(-ShineWidth, fillWidth + ShineWidth, phase), 0f);

            // 맨 앞 빛 - 채우는 중일 때만
            var edgeOn = _shown > 0.01f && _shown < 0.995f;
            if (_edge.gameObject.activeSelf != edgeOn)
                _edge.gameObject.SetActive(edgeOn);
            if (edgeOn)
            {
                _edge.anchorMin = new Vector2(_shown, 0f);
                _edge.anchorMax = new Vector2(_shown, 1f);
                _edge.anchoredPosition = Vector2.zero;
                var ec = Color.Lerp(color, Color.white, 0.6f);
                ec.a = 0.75f + 0.25f * pulse;
                _edgeImage.color = ec;
            }

            // 칸이 찰 때 번쩍 + 아이콘 톡
            if (steps > 0)
            {
                var step = Mathf.Clamp(Mathf.FloorToInt(ratio * steps + 0.001f), 0, steps);
                if (_lastStep >= 0 && step > _lastStep)
                {
                    _flash.anchorMin = new Vector2((step - 1) / (float)steps, 0f);
                    _flash.anchorMax = new Vector2(step / (float)steps, 1f);
                    _flash.offsetMin = new Vector2(0f, -2f);
                    _flash.offsetMax = new Vector2(0f, 2f);
                    _flashT = 1f;
                    _punchT = 1f;
                }
                _lastStep = step;
            }
            _flashT = Mathf.Max(0f, _flashT - dt * 3f);
            _flashImage.color = new Color(1f, 1f, 1f, _flashT * _flashT * 0.9f);
            _punchT = Mathf.Max(0f, _punchT - dt * 5f);
            var scale = 1f + 0.18f * _punchT * _punchT + (full ? 0.06f * pulse : 0f);
            _slot.localScale = new Vector3(scale, scale, 1f);

            // 둘레 발광 - 가득 차면 강하게, 위험하면 약하게 맥박
            var glow = color;
            glow.a = full ? 0.45f + 0.45f * pulse : alert ? 0.15f + 0.3f * pulse : 0f;
            _glow.color = glow;
        }

        /// <summary>칸 사이 세로 눈금(어두운 2px 줄) - 채움 위, 번쩍임 아래.</summary>
        private void BuildTicks(int steps)
        {
            _steps = steps;
            _lastStep = -1;
            foreach (var t in _ticks)
                Object.Destroy(t);
            _ticks.Clear();
            for (var i = 1; i < steps; i++)
            {
                var tick = HudUi.CreateImage(_fillArea, "Tick", new Color(0.04f, 0.03f, 0.05f, 0.9f)).rectTransform;
                tick.anchorMin = new Vector2(i / (float)steps, 0f);
                tick.anchorMax = new Vector2(i / (float)steps, 1f);
                tick.sizeDelta = new Vector2(2f, 0f);
                tick.anchoredPosition = Vector2.zero;
                tick.SetSiblingIndex(_flash.GetSiblingIndex()); // 번쩍임·맨 앞 빛보다 아래
                _ticks.Add(tick.gameObject);
            }
        }

        /// <summary>다시 보일 때 이전 값에서 미끄러져 오지 않게.</summary>
        public void ResetFill()
        {
            _shown = -1f;
            _lastStep = -1;
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
            var fit = image.GetComponent<SliceFit>() ?? image.gameObject.AddComponent<SliceFit>();
            fit.Scale = scale;
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

        /// <summary>보유 골드 칸(10-28 테스터 의견) - 쇠테 + 코인 그림(Effects/coin/0 2배) + 금색 숫자. 던전 HUD·로비 방 공용.
        /// anchor·pivot 기준 position에 150x40. 숫자 글자를 돌려준다(채우는 건 부르는 쪽).</summary>
        public static Text CreateGoldBox(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position) =>
            CreateCurrencyBox(parent, anchor, pivot, position, "Effects/coin/0", new Color(1f, 0.85f, 0.4f));

        /// <summary>골드 칸과 같은 모양의 재화 칸 - 왼쪽 그림(16px 도트 2배) + 오른쪽 숫자. 마석 칸(로비)도 이걸로.</summary>
        public static Text CreateCurrencyBox(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, string iconPath, Color textColor)
        {
            var box = CreateImage(parent, "GoldBox", HudSlot.FillNormal);
            PixelUi.Slice(box, "Slot");
            var rect = box.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(150f, 40f);
            rect.anchoredPosition = position;
            var coin = CreateImage(box.transform, "Coin", Color.white);
            coin.sprite = PixelUi.Get(iconPath);
            coin.enabled = coin.sprite != null;
            var coinRect = coin.rectTransform;
            coinRect.anchorMin = coinRect.anchorMax = new Vector2(0f, 0.5f);
            coinRect.pivot = new Vector2(0f, 0.5f);
            coinRect.sizeDelta = new Vector2(32f, 32f); // 16px 도트 2배
            coinRect.anchoredPosition = new Vector2(8f, 0f);
            var text = CreateText(box.transform, "Gold", 16, TextAnchor.MiddleRight);
            text.color = textColor;
            text.fontStyle = FontStyle.Bold;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(44f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            return text;
        }
    }

    /// <summary>작은 버튼(닫기 X·28~34px 줄)은 4배 쇠테 양쪽이 칸보다 커서 테가 찌그러진다 - 크기가 정해질 때마다
    /// 테가 들어갈 때까지 픽셀 배율을 4→2→1로 반으로 줄인다(정수 배율이라 도트는 그대로 또렷하다).</summary>
    public class SliceFit : MonoBehaviour
    {
        public float Scale = PixelUi.Scale;
        private Image _image;

        private void OnEnable() => Fit();
        private void OnRectTransformDimensionsChange() => Fit();

        private void Fit()
        {
            if (_image == null)
                _image = GetComponent<Image>();
            if (_image == null || _image.sprite == null || _image.type != Image.Type.Sliced)
                return;
            var size = ((RectTransform)transform).rect.size;
            if (size.x <= 0f || size.y <= 0f)
                return; // 아직 크기가 안 정해졌다
            var border = _image.sprite.border; // 왼, 아래, 오른, 위(그림 픽셀)
            var unitsPerPixel = (_image.canvas != null ? _image.canvas.referencePixelsPerUnit : 100f) / _image.sprite.pixelsPerUnit;
            var scale = Scale;
            while (scale > 1f && ((border.x + border.z) * unitsPerPixel * scale > size.x || (border.y + border.w) * unitsPerPixel * scale > size.y))
                scale *= 0.5f;
            if (!Mathf.Approximately(_image.pixelsPerUnitMultiplier, 1f / scale))
                _image.pixelsPerUnitMultiplier = 1f / scale;
        }
    }
}
