using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>10층 보스(거울 가면 기사)를 쓰러뜨리면 뜨는 엔딩 - 화면이 어두워지며 글이 떠오르고, 쓰러뜨린 보스 10종이 한 마리씩
    /// 나란히 선 뒤 기록(누적 사망·레벨·도감·클리어 횟수)을 보여준다. [Enter]를 누르면 onDone(처음으로 돌아가기).
    /// 자기 캔버스를 따로 만들어 HUD 위를 덮는다.</summary>
    public class EndingScreen : MonoBehaviour
    {
        public struct Record
        {
            public int Clears;      // 이번 클리어 포함 몇 번째인지
            public int TotalDeaths;
            public int Level;
            public int CodexFound;
            public int CodexTotal;
        }

        private const float FadeSeconds = 1.2f;
        private const float LineSeconds = 0.9f;
        private const float BossSeconds = 0.18f;

        private static readonly LocCache<string[]> StoryLinesCache = new LocCache<string[]>(() => new string[]

        {
            Loc.T("거울 가면이 산산이 부서졌다."),
            Loc.T("가면에 비치던 얼굴들이 흩어지고, 끝없이 되풀이되던 고리가 멈췄다."),
            Loc.T("하지만 던전은 다시 숨을 쉰다. 기록만이 남아 다음 모험가를 기다린다."),
        });

        private static string[] StoryLines => StoryLinesCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private Func<bool> _canShow;
        private Action<RelicType?> _onDone; // 다음 회차로 가져갈 유물(없으면 null)
        private Record _record;
        private bool _ready;
        private bool _done;
        private Text _prompt;

        public static void Show(Record record, Func<bool> canShow, Action<RelicType?> onDone)
        {
            var go = new GameObject("EndingScreen");
            var screen = go.AddComponent<EndingScreen>();
            screen._record = record;
            screen._canShow = canShow;
            screen._onDone = onDone;
        }

        private IEnumerator Start()
        {
            // 보스 처치 경험치로 레벨업 카드가 같이 떴으면 그것부터(카드 창과 입력이 겹치지 않게)
            while (_canShow != null && !_canShow())
                yield return null;

            var canvasGo = new GameObject("EndingCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // HUD·창 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 16:9가 아닌 화면에서도 잘리지 않게
            canvasGo.AddComponent<GraphicRaycaster>(); // 뒤쪽 UI 클릭 막기

            var shade = HudUi.CreateImage(canvasGo.transform, "Shade", new Color(0.03f, 0.02f, 0.05f, 0f));
            HudUi.Stretch(shade.rectTransform, 0f);
            for (var t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                shade.color = new Color(0.03f, 0.02f, 0.05f, Mathf.Lerp(0f, 0.94f, t / FadeSeconds));
                yield return null;
            }
            shade.color = new Color(0.03f, 0.02f, 0.05f, 0.94f);

            var title = Label(canvasGo.transform, Loc.T("고리를 끊었다"), 40, new Vector2(0f, 270f), new Color(1f, 0.85f, 0.45f));
            title.fontStyle = FontStyle.Bold;
            yield return FadeIn(title);

            for (var i = 0; i < StoryLines.Length; i++)
                yield return FadeIn(Label(canvasGo.transform, StoryLines[i], 20, new Vector2(0f, 205f - i * 34f), new Color(0.85f, 0.87f, 0.95f)));

            // 쓰러뜨린 보스 10종 - 한 마리씩
            const float size = 104f, gap = 6f;
            var left = -(StageProgress.MaxStage - 1) * (size + gap) * 0.5f;
            for (var s = 1; s <= StageProgress.MaxStage; s++)
            {
                var sprite = SpriteAnimator.FirstFrame($"Boss{s}");
                var pos = new Vector2(left + (s - 1) * (size + gap), 10f);
                if (sprite != null)
                {
                    var img = HudUi.CreateImage(canvasGo.transform, $"Boss{s}", Color.white);
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.rectTransform.sizeDelta = new Vector2(size, size);
                    img.rectTransform.anchoredPosition = pos;
                }
                var name = Label(canvasGo.transform, BossBrain.BossName(s), 13, pos + new Vector2(0f, -62f), new Color(0.7f, 0.72f, 0.8f));
                name.rectTransform.sizeDelta = new Vector2(size + gap, 24f);
                name.color = new Color(0.7f, 0.72f, 0.8f, 1f); // 보스와 같이 바로 보이게(Label은 투명하게 만들어진다)
                yield return Wait(BossSeconds);
            }

            var r = _record;
            var stats = Loc.F("{0}번째 클리어   ·   누적 사망 {1}회   ·   레벨 {2}   ·   도감 {3}/{4}", r.Clears, r.TotalDeaths, r.Level, r.CodexFound, r.CodexTotal);
            yield return FadeIn(Label(canvasGo.transform, stats, 20, new Vector2(0f, -110f), new Color(1f, 0.85f, 0.45f)));
            _relics = Relics.OwnedRelics().ToList();
            yield return FadeIn(Label(canvasGo.transform,
                _relics.Count > 0
                    ? Loc.T("처음으로 돌아갑니다 - <color=#B9A0FF>도감</color>과 <color=#FFD966>유물 하나</color>가 남고, 골드·장비·레벨·업적·칭호는 처음부터.")
                    : Loc.T("처음으로 돌아갑니다 - <color=#B9A0FF>도감</color>은 남고, 골드·장비·유물·레벨·업적·칭호는 처음부터."),
                16, new Vector2(0f, -160f), new Color(0.75f, 0.77f, 0.85f)));

            _canvas = canvasGo.transform;
            _prompt = Label(canvasGo.transform, _relics.Count > 0 ? Loc.T("[Enter] 가져갈 유물 고르기") : Loc.T("[Enter] 처음으로"), 22, new Vector2(0f, -250f), Color.white);
            _prompt.fontStyle = FontStyle.Bold;
            _ready = true;
        }

        private void Update()
        {
            if (!_ready || _done)
                return;
            _prompt.color = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f)));
            if (_picking)
            {
                UpdatePick();
                return;
            }
            if (!(GameInput.Down(Key.Enter) || GameInput.Down(Key.NumpadEnter)))
                return;
            if (_relics.Count > 0)
            {
                BuildPick();
                return;
            }
            _done = true;
            _onDone?.Invoke(null);
        }

        // ---- 다음 회차로 가져갈 유물 고르기(10-27 사용자) - 가진 유물을 4줄 격자로, 방향키·클릭으로 고르고 Enter로 결정 ----
        private const int PickColumns = 4;
        private List<RelicType> _relics = new List<RelicType>();
        private Transform _canvas;
        private bool _picking;
        private int _cursor;
        private readonly List<(RectTransform Rect, Image Image)> _cells = new List<(RectTransform, Image)>();
        private Text _pickDetail;
        private static readonly Color CellColor = new Color(0.16f, 0.14f, 0.22f, 0.95f);
        private static readonly Color CellActive = new Color(0.55f, 0.42f, 0.18f, 0.95f);

        private void BuildPick()
        {
            _picking = true;
            var page = HudUi.CreateImage(_canvas, "RelicPick", new Color(0.03f, 0.02f, 0.05f, 0.97f));
            HudUi.Stretch(page.rectTransform, 0f);
            var title = Label(page.transform, Loc.T("다음 고리로 가져갈 유물 하나"), 32, new Vector2(0f, 280f), new Color(1f, 0.85f, 0.45f));
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.85f, 0.45f, 1f);
            var sub = Label(page.transform, Loc.T("고른 유물만 새 모험에 남는다"), 17, new Vector2(0f, 238f), new Color(0.75f, 0.77f, 0.85f));
            sub.color = new Color(0.75f, 0.77f, 0.85f, 1f);

            const float w = 270f, h = 62f, gap = 10f;
            var rows = (_relics.Count + PickColumns - 1) / PickColumns;
            for (var i = 0; i < _relics.Count; i++)
            {
                var col = i % PickColumns;
                var row = i / PickColumns;
                var inRow = Mathf.Min(PickColumns, _relics.Count - row * PickColumns);
                var x = (col - (inRow - 1) * 0.5f) * (w + gap);
                var y = 150f - row * (h + gap) - (3 - Mathf.Min(rows, 3)) * (h + gap) * 0.5f;
                var cell = HudUi.CreateImage(page.transform, "Relic", CellColor);
                cell.rectTransform.sizeDelta = new Vector2(w, h);
                cell.rectTransform.anchoredPosition = new Vector2(x, y);
                var sprite = PixelUi.Get($"UI/Relics/Relic_{_relics[i]}");
                if (sprite != null)
                {
                    var icon = HudUi.CreateImage(cell.transform, "Icon", Color.white);
                    icon.sprite = sprite;
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0f, 0.5f);
                    icon.rectTransform.sizeDelta = new Vector2(44f, 44f);
                    icon.rectTransform.anchoredPosition = new Vector2(10f, 0f);
                }
                var name = HudUi.CreateText(cell.transform, "Name", 16, TextAnchor.MiddleLeft);
                name.text = Relics.Name(_relics[i]);
                name.color = Color.white;
                HudUi.Stretch(name.rectTransform, 0f);
                name.rectTransform.offsetMin = new Vector2(62f, 0f);
                _cells.Add((cell.rectTransform, cell));
            }

            _pickDetail = Label(page.transform, string.Empty, 18, new Vector2(0f, -150f), Color.white);
            _pickDetail.color = new Color(0.9f, 0.92f, 1f, 1f);
            _pickDetail.rectTransform.sizeDelta = new Vector2(1200f, 64f); // 이름 + 설명 두 줄
            _prompt.transform.SetParent(page.transform, false);
            _prompt.text = Loc.T("[방향키·클릭] 고르기   [Enter] 결정");
            RefreshPick();
        }

        private void UpdatePick()
        {
            var move = GameInput.Down(Key.RightArrow) || GameInput.Down(Key.D) ? 1
                : GameInput.Down(Key.LeftArrow) || GameInput.Down(Key.A) ? -1
                : GameInput.Down(Key.DownArrow) || GameInput.Down(Key.S) ? PickColumns
                : GameInput.Down(Key.UpArrow) || GameInput.Down(Key.W) ? -PickColumns : 0;
            if (move != 0)
            {
                var next = _cursor + move;
                if (next >= 0 && next < _relics.Count)
                    _cursor = next;
                RefreshPick();
            }
            var pos = GameInput.PointerPosition;
            for (var i = 0; i < _cells.Count; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_cells[i].Rect, pos, null) || !GameInput.PointerDown)
                    continue;
                if (_cursor == i)
                {
                    Confirm(); // 이미 고른 칸을 한 번 더 누르면 결정
                    return;
                }
                _cursor = i;
                RefreshPick();
            }
            if (GameInput.Down(Key.Enter) || GameInput.Down(Key.NumpadEnter))
                Confirm();
        }

        private void RefreshPick()
        {
            for (var i = 0; i < _cells.Count; i++)
                _cells[i].Image.color = i == _cursor ? CellActive : CellColor;
            var r = _relics[_cursor];
            _pickDetail.text = $"<color=#FFD966>{Relics.Name(r)}</color>\n{Relics.Description(r)}";
        }

        private void Confirm()
        {
            _done = true;
            _onDone?.Invoke(_relics[_cursor]);
        }

        private static Text Label(Transform parent, string text, int fontSize, Vector2 pos, Color color)
        {
            var label = HudUi.CreateText(parent, "Label", fontSize, TextAnchor.MiddleCenter);
            label.text = text;
            label.supportRichText = true;
            label.color = new Color(color.r, color.g, color.b, 0f);
            label.rectTransform.sizeDelta = new Vector2(1200f, fontSize + 16f);
            label.rectTransform.anchoredPosition = pos;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        /// <summary>글자를 서서히 드러낸다(Label은 투명하게 만들어진다).</summary>
        private static IEnumerator FadeIn(Text label)
        {
            var c = label.color;
            for (var t = 0f; t < LineSeconds; t += Time.unscaledDeltaTime)
            {
                label.color = new Color(c.r, c.g, c.b, t / LineSeconds);
                yield return null;
            }
            label.color = new Color(c.r, c.g, c.b, 1f);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                yield return null;
        }
    }
}
