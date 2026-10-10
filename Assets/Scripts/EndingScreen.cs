using System;
using System.Collections;
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
        private Action _onDone;
        private Record _record;
        private bool _ready;
        private bool _done;
        private Text _prompt;

        public static void Show(Record record, Func<bool> canShow, Action onDone)
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
            yield return FadeIn(Label(canvasGo.transform,
                Loc.T("처음으로 돌아갑니다 - <color=#B9A0FF>도감·업적·칭호</color>는 남고, 골드·장비·유물·레벨은 처음부터."),
                16, new Vector2(0f, -160f), new Color(0.75f, 0.77f, 0.85f)));

            _prompt = Label(canvasGo.transform, Loc.T("[Enter] 처음으로"), 22, new Vector2(0f, -250f), Color.white);
            _prompt.fontStyle = FontStyle.Bold;
            _ready = true;
        }

        private void Update()
        {
            if (!_ready || _done)
                return;
            _prompt.color = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f)));
            var keyboard = Keyboard.current;
            if (keyboard == null || !(keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                return;
            _done = true;
            _onDone?.Invoke();
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
