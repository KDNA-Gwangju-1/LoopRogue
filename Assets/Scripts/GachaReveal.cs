using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>뽑기 상점 연출(10-14 사용자 요청) - 결과는 이미 정해졌고(GachaSystem) 보여주기만 한다.
    /// 어두워진 화면 가운데 보물상자가 덜컹이다(영웅 이상이면 더 오래, 틈새로 등급 색 빛) 번쩍 열리며 등급 색 빛줄기·반짝이와 함께
    /// 장비가 솟아오른다. 10연차는 상자에서 카드 10장이 하나씩 튀어나와 깔리고 최고 등급 카드에 빛줄기. 전설 이상은 화면 흔들림 + 팡파르.
    /// 클릭·Space·Enter로 흔들림 건너뛰기/닫기. 그림(Resources/UI/Gacha)이 없으면 연출 없이 바로 onDone.</summary>
    public class GachaReveal : MonoBehaviour
    {
        private const float Px = 4f; // UI 4배 도트
        private static GachaReveal _instance;
        private static int _closedFrame = -1;

        /// <summary>연출 중이거나 방금 닫힌 프레임 - 로비가 같은 입력으로 또 뽑지 않게.</summary>
        public static bool BlocksInput => (_instance != null && _instance._playing) || Time.frameCount == _closedFrame;

        private bool _playing;
        private bool _skipAll;          // 건너뛰기 - 남은 연출(등급 오르기·열림·카드 깔기)을 한 번에 끝낸다
        private RectTransform _skipButton;
        private RectTransform _root;
        private RectTransform _shakeRoot;
        private Image _dim;
        private Image _flash;
        private Text _hint;
        private Sprite _closed, _open, _glow, _rays, _sparkle, _panel;
        private readonly List<GameObject> _temp = new List<GameObject>();
        private readonly List<Particle> _particles = new List<Particle>();
        private float _shake;

        private sealed class Particle
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Life, MaxLife;
        }

        public static void Play(ItemSlot slot, IList<(ItemGrade Grade, bool Equipped, int Exp)> results, Action onDone)
        {
            if (_instance == null)
                _instance = Create();
            if (_instance == null || _instance._playing)
            {
                onDone();
                return;
            }
            _instance.gameObject.SetActive(true); // 꺼진 오브젝트에선 코루틴이 안 돈다(켜기 전에 시작하면 연출이 통째로 빠짐)
            _instance.StartCoroutine(_instance.Run(slot, results, onDone));
        }

        private static GachaReveal Create()
        {
            var closed = Resources.Load<Sprite>("UI/Gacha/Chest_Closed");
            if (closed == null)
                return null;
            var go = new GameObject("GachaReveal", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var r = go.AddComponent<GachaReveal>();
            r._closed = closed;
            r._open = Resources.Load<Sprite>("UI/Gacha/Chest_Open");
            r._glow = Resources.Load<Sprite>("UI/Gacha/Chest_Glow");
            r._rays = Resources.Load<Sprite>("UI/Gacha/Rays");
            r._sparkle = Resources.Load<Sprite>("UI/Gacha/Sparkle");
            r._panel = Resources.Load<Sprite>("UI/Common/Panel");
            r._root = go.GetComponent<RectTransform>();
            r._dim = r.FullScreen("Dim", new Color(0f, 0f, 0f, 0f));
            var shake = new GameObject("Shake", typeof(RectTransform));
            shake.transform.SetParent(r._root, false);
            r._shakeRoot = shake.GetComponent<RectTransform>();
            r._flash = r.FullScreen("Flash", new Color(1f, 1f, 1f, 0f));
            r._hint = r.MakeText(r._root, "Hint", 15, new Color(0.75f, 0.75f, 0.8f), new Vector2(0f, -320f), 600f);
            r._hint.text = Loc.T("클릭 / Space - 넘기기");
            r._skipButton = r.MakeSkipButton();
            go.SetActive(false);
            return r;
        }

        private IEnumerator Run(ItemSlot slot, IList<(ItemGrade Grade, bool Equipped, int Exp)> results, Action onDone)
        {
            _playing = true;
            _hint.gameObject.SetActive(false);
            _skipAll = !GameSettings.GachaAnimation; // 설정에서 끄면 처음부터 건너뛴 것처럼 바로 결과
            _skipButton.gameObject.SetActive(!_skipAll);
            var best = results[0];
            foreach (var res in results)
                if (res.Grade > best.Grade)
                    best = res;
            var bestColor = EquipmentData.Grades[best.Grade].Color;
            var jackpot = best.Grade >= ItemGrade.Legendary; // 흔들림 + 색 번쩍 + 팡파르

            // 1) 어두워지고 상자 등장(통 튀어 오름)
            var chestPos = new Vector2(0f, results.Count > 1 ? 150f : 40f);
            var chest = MakeImage(_shakeRoot, "Chest", _closed, chestPos);
            yield return Tween(_skipAll ? 0.05f : 0.18f, t =>
            {
                _dim.color = new Color(0f, 0f, 0f, 0.72f * t);
                chest.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(t));
            });

            // 2) 등급 오르기 - 항상 일반(흰빛)에서 시작해 덜컹할 때마다 한 칸씩 오르고(후광·반짝이 색, 음이 높아짐 - 등급 이름은 안 보여 줌, 사용자 요청),
            //    실제 등급에 닿으면 한 번 더 덜컹했다 실패한 뒤 열린다 - 처음부터 결과가 보이지 않게(사용자 요청). 클릭하면 최종 등급으로 건너뛰고 열림.
            var aura = _rays != null ? MakeImage(_shakeRoot, "Aura", _rays, chestPos + new Vector2(0f, 8f)) : null;
            if (aura != null)
            {
                aura.transform.SetSiblingIndex(chest.transform.GetSiblingIndex()); // 상자 뒤
                aura.rectTransform.localScale = Vector3.one * 0.75f;
                StartCoroutine(Spin(aura.rectTransform, 25f));
            }
            var shown = ItemGrade.Common;
            void ShowGrade(ItemGrade g, float auraAlpha)
            {
                var c = EquipmentData.Grades[g].Color;
                if (aura != null)
                {
                    aura.color = new Color(c.r, c.g, c.b, auraAlpha);
                    aura.rectTransform.localScale = Vector3.one * (0.75f + 0.08f * (int)g);
                }
            }
            ShowGrade(shown, 0.25f);

            var steps = (int)best.Grade; // 일반 → 최고 등급까지 오를 칸 수
            var skipped = _skipAll;
            for (var h = 0; h <= steps && !skipped; h++)
            {
                var success = h < steps; // 마지막 한 번은 실패
                var strength = 1f + Mathf.Min(h, 6) * 0.25f;
                var hopTime = Mathf.Lerp(0.36f, 0.22f, h / 8f);
                SfxPlayer.Play(Sfx.ChestRattle);
                for (var t = 0f; t < hopTime && !skipped; t += Time.deltaTime)
                {
                    var k = t / hopTime;
                    var jump = Mathf.Sin(k * Mathf.PI) * 10f * strength;
                    var wobble = Mathf.Sin(k * Mathf.PI * 6f) * 4f * strength * (1f - k);
                    chest.rectTransform.anchoredPosition = chestPos + new Vector2(wobble, jump);
                    chest.rectTransform.localEulerAngles = new Vector3(0f, 0f, wobble * 0.8f);
                    if (UnityEngine.Random.value < 0.25f + 0.05f * (int)shown) // 틈새로 새는 빛 - 지금 보이는 등급 색
                        Spawn(chestPos + new Vector2(UnityEngine.Random.Range(-60f, 60f), 10f), EquipmentData.Grades[shown].Color,
                            new Vector2(UnityEngine.Random.Range(-40f, 40f), UnityEngine.Random.Range(60f, 160f)), 0.5f, 2f);
                    skipped = CheckSkip();
                    yield return null;
                }
                chest.rectTransform.anchoredPosition = chestPos;
                chest.rectTransform.localEulerAngles = Vector3.zero;
                if (skipped)
                    break;
                if (success)
                {
                    shown = (ItemGrade)((int)shown + 1);
                    ShowGrade(shown, 0.25f + 0.07f * (int)shown);
                    SfxPlayer.PlayPitched(Sfx.GradeUp, 1f + 0.09f * (int)shown);
                    Burst(chestPos + new Vector2(0f, 20f), EquipmentData.Grades[shown].Color, 6 + (int)shown * 2);
                    if (shown >= ItemGrade.Legendary)
                        _shake = 4f + (int)shown;
                }
                for (var t = 0f; t < (success ? 0.12f : 0.3f) && !skipped; t += Time.deltaTime)
                {
                    skipped = CheckSkip();
                    yield return null;
                }
            }
            if (skipped)
                ShowGrade(best.Grade, 0.25f + 0.07f * (int)best.Grade);
            if (aura != null)
                aura.gameObject.SetActive(false);

            // 3) 번쩍 열림
            chest.sprite = _open ?? _closed;
            SfxPlayer.Play(Sfx.ChestOpen);
            if (jackpot)
            {
                SfxPlayer.Play(Sfx.Fanfare);
                _shake = 14f;
            }
            StartCoroutine(Flash(jackpot ? Color.Lerp(Color.white, bestColor, 0.45f) : Color.white, jackpot ? 0.85f : 0.55f));
            if (_glow != null)
            {
                var glow = MakeImage(_shakeRoot, "Glow", _glow, chestPos + new Vector2(0f, 12f * Px)); // 열린 입에서 위로 솟는 빛
                glow.color = bestColor;
                glow.transform.SetSiblingIndex(chest.transform.GetSiblingIndex()); // 상자 뒤
            }
            Burst(chestPos + new Vector2(0f, 30f), bestColor, jackpot ? 34 : best.Grade >= ItemGrade.Epic ? 22 : 12);

            if (results.Count == 1)
                yield return RevealSingle(slot, results[0], chestPos);
            else
                yield return RevealMulti(slot, results, best, chestPos);

            // 4) 닫기 - 클릭/Space, 혹은 6초
            _skipButton.gameObject.SetActive(false);
            _hint.gameObject.SetActive(true);
            for (var t = 0f; t < 6f; t += Time.deltaTime)
            {
                if (t > 0.3f && SkipPressed())
                    break;
                yield return null;
            }
            _hint.gameObject.SetActive(false);
            yield return Tween(0.15f, t =>
            {
                _dim.color = new Color(0f, 0f, 0f, 0.72f * (1f - t));
                _shakeRoot.localScale = Vector3.one * (1f - 0.05f * t);
            });
            _playing = false; // 반짝이 루프(Twinkle)가 먼저 멈추게
            Cleanup();
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            onDone();
        }

        private IEnumerator RevealSingle(ItemSlot slot, (ItemGrade Grade, bool Equipped, int Exp) result, Vector2 chestPos)
        {
            var color = EquipmentData.Grades[result.Grade].Color;
            var rays = _rays != null ? MakeImage(_shakeRoot, "Rays", _rays, chestPos + new Vector2(0f, 150f)) : null;
            if (rays != null)
            {
                rays.color = new Color(color.r, color.g, color.b, 0f);
                rays.transform.SetAsFirstSibling();
                StartCoroutine(Spin(rays.rectTransform, result.Grade >= ItemGrade.Legendary ? 50f : 28f));
            }
            var icon = MakeImage(_shakeRoot, "Icon", EquipIcon(slot, result.Grade), chestPos);
            var name = MakeText(_shakeRoot, "Name", 30, color, new Vector2(0f, -130f), 700f);
            name.fontStyle = FontStyle.Bold;
            name.text = EquipmentData.DisplayName(slot, result.Grade);
            var sub = MakeText(_shakeRoot, "Sub", 18, result.Equipped ? new Color(0.5f, 1f, 0.55f) : new Color(0.65f, 0.65f, 0.7f), new Vector2(0f, -168f), 700f);
            sub.text = result.Equipped ? Loc.T("장착!")
                : result.Exp > 0 ? Loc.F("이미 더 좋은 장비가 있음 → 강화 경험치 +{0}", result.Exp)
                : Loc.T("이미 더 좋은 장비가 있음 (강화 MAX)");
            name.color = new Color(color.r, color.g, color.b, 0f);
            sub.color = new Color(sub.color.r, sub.color.g, sub.color.b, 0f);
            var subColor = new Color(sub.color.r, sub.color.g, sub.color.b, 1f);

            yield return Tween(_skipAll ? 0.05f : 0.45f, t =>
            {
                var e = EaseOutBack(t);
                icon.rectTransform.anchoredPosition = Vector2.LerpUnclamped(chestPos, chestPos + new Vector2(0f, 150f), e);
                icon.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1.3f, e); // 32x32 그림 x4 x1.3
                if (rays != null)
                {
                    rays.color = new Color(color.r, color.g, color.b, 0.9f * t);
                    rays.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.5f, t);
                }
                name.color = new Color(color.r, color.g, color.b, t);
                sub.color = new Color(subColor.r, subColor.g, subColor.b, t);
            });
            StartCoroutine(Bob(icon.rectTransform, chestPos + new Vector2(0f, 150f)));
            if (result.Grade >= ItemGrade.Epic)
                StartCoroutine(Twinkle(chestPos + new Vector2(0f, 150f), color, result.Grade >= ItemGrade.Legendary ? 0.05f : 0.12f));
        }

        private IEnumerator RevealMulti(ItemSlot slot, IList<(ItemGrade Grade, bool Equipped, int Exp)> results, (ItemGrade Grade, bool Equipped, int Exp) best, Vector2 chestPos)
        {
            var bestIndex = -1;
            for (var i = 0; i < results.Count; i++)
                if (bestIndex < 0 && results[i].Grade == best.Grade)
                    bestIndex = i;
            const float cardW = 150f, cardH = 132f;
            for (var i = 0; i < results.Count; i++)
            {
                var res = results[i];
                var color = EquipmentData.Grades[res.Grade].Color;
                var col = i % 5;
                var row = i / 5;
                var target = new Vector2((col - 2) * (cardW + 14f), -50f - row * (cardH + 14f));
                if (i == bestIndex && _rays != null)
                {
                    var rays = MakeImage(_shakeRoot, "BestRays", _rays, target);
                    rays.color = new Color(color.r, color.g, color.b, 0.85f);
                    rays.rectTransform.localScale = Vector3.one * 0.9f;
                    StartCoroutine(Spin(rays.rectTransform, 40f));
                }
                var card = MakeCard(color, res, EquipIcon(slot, res.Grade), slot, cardW, cardH);
                card.anchoredPosition = chestPos;
                card.localScale = Vector3.one * 0.2f;
                var from = chestPos + new Vector2(0f, 20f);
                StartCoroutine(Tween(_skipAll ? 0.03f : 0.22f, t =>
                {
                    var e = EaseOutBack(t);
                    card.anchoredPosition = Vector2.LerpUnclamped(from, target, e) + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * 40f);
                    card.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, e);
                }));
                if (res.Grade >= ItemGrade.Epic)
                    Burst(target, color, res.Grade >= ItemGrade.Legendary ? 14 : 7);
                if (_skipAll)
                    continue; // 소리·간격 없이 나머지 카드를 한 번에
                SfxPlayer.Play(res.Grade >= ItemGrade.Epic ? Sfx.ChestOpen : Sfx.ChestRattle);
                for (var t = 0f; t < 0.09f && !CheckSkip(); t += Time.deltaTime)
                    yield return null;
            }
            yield return Wait(_skipAll ? 0.05f : 0.25f);
        }

        private RectTransform MakeCard(Color color, (ItemGrade Grade, bool Equipped, int Exp) res, Sprite icon, ItemSlot slot, float w, float h)
        {
            var go = new GameObject("Card", typeof(RectTransform));
            go.transform.SetParent(_shakeRoot, false);
            _temp.Add(go);
            var bg = go.AddComponent<Image>();
            if (_panel != null)
            {
                bg.sprite = _panel;
                bg.type = Image.Type.Sliced;
                bg.pixelsPerUnitMultiplier = 1f / Px;
            }
            bg.color = Color.Lerp(Color.white, color, 0.55f);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(w, h);
            if (icon != null)
            {
                var img = MakeImage(rect, "Icon", icon, new Vector2(0f, 12f));
                img.rectTransform.localScale = Vector3.one * 0.6f; // 32x32 그림
            }
            var grade = MakeText(rect, "Grade", 16, color, new Vector2(0f, -46f), w - 10f);
            grade.fontStyle = FontStyle.Bold;
            grade.text = EquipmentData.Grades[res.Grade].Name;
            if (res.Equipped)
            {
                var eq = MakeText(rect, "Equipped", 13, new Color(0.5f, 1f, 0.55f), new Vector2(0f, 54f), w - 10f);
                eq.text = Loc.T("장착!");
            }
            else if (res.Exp > 0)
            {
                var xp = MakeText(rect, "Exp", 13, new Color(1f, 0.85f, 0.45f), new Vector2(0f, 54f), w - 10f);
                xp.text = Loc.F("강화 +{0}", res.Exp);
            }
            return rect;
        }

        // ------------------------------------------------------------ 입자·흔들림
        private void Update()
        {
            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Life -= Time.deltaTime;
                if (p.Life <= 0f || p.Rect == null)
                {
                    if (p.Rect != null)
                        Destroy(p.Rect.gameObject);
                    _particles.RemoveAt(i);
                    continue;
                }
                p.Velocity += new Vector2(0f, -220f) * Time.deltaTime;
                p.Rect.anchoredPosition += p.Velocity * Time.deltaTime;
                var c = p.Image.color;
                c.a = Mathf.Clamp01(p.Life / p.MaxLife * 1.4f);
                p.Image.color = c;
                p.Rect.localEulerAngles = new Vector3(0f, 0f, p.Life * 360f);
            }
            if (_shake > 0.01f)
            {
                _shakeRoot.anchoredPosition = UnityEngine.Random.insideUnitCircle * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, 40f * Time.deltaTime);
            }
            else
                _shakeRoot.anchoredPosition = Vector2.zero;
        }

        private void Burst(Vector2 at, Color color, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var speed = UnityEngine.Random.Range(160f, 420f);
                var c = UnityEngine.Random.value < 0.35f ? Color.white : color;
                Spawn(at, c, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f + 0.5f) * speed, UnityEngine.Random.Range(0.6f, 1.1f),
                    UnityEngine.Random.Range(2f, 3.5f));
            }
        }

        private void Spawn(Vector2 at, Color color, Vector2 velocity, float life, float scale)
        {
            if (_sparkle == null)
                return;
            var img = MakeImage(_shakeRoot, "Spark", _sparkle, at, track: false);
            img.color = color;
            img.rectTransform.localScale = Vector3.one * (scale / Px);
            _particles.Add(new Particle { Rect = img.rectTransform, Image = img, Velocity = velocity, Life = life, MaxLife = life });
        }

        private IEnumerator Twinkle(Vector2 at, Color color, float interval)
        {
            while (_playing && gameObject.activeSelf)
            {
                Spawn(at + UnityEngine.Random.insideUnitCircle * 90f, UnityEngine.Random.value < 0.5f ? Color.white : color,
                    new Vector2(0f, UnityEngine.Random.Range(20f, 70f)), 0.7f, UnityEngine.Random.Range(1.5f, 3f));
                yield return Wait(interval);
            }
        }

        private IEnumerator Flash(Color color, float alpha)
        {
            yield return Tween(0.35f, t => _flash.color = new Color(color.r, color.g, color.b, alpha * (1f - t)));
        }

        private static IEnumerator Spin(RectTransform rect, float degPerSec)
        {
            while (rect != null)
            {
                rect.localEulerAngles += new Vector3(0f, 0f, degPerSec * Time.deltaTime);
                yield return null;
            }
        }

        private static IEnumerator Bob(RectTransform rect, Vector2 center)
        {
            var t = 0f;
            while (rect != null)
            {
                t += Time.deltaTime;
                rect.anchoredPosition = center + new Vector2(0f, Mathf.Sin(t * 3f) * 6f);
                yield return null;
            }
        }

        // ------------------------------------------------------------ 도우미
        /// <summary>등급별 장비 그림(Equip_{부위}_{등급}, 24x24) - 없으면 부위 공용 그림.</summary>
        public static Sprite EquipIcon(ItemSlot slot, ItemGrade grade) =>
            Resources.Load<Sprite>($"UI/Gacha/Equip_{slot}_{grade}") ?? Resources.Load<Sprite>($"UI/Gacha/Equip_{slot}");

        /// <summary>건너뛰기 버튼·클릭·Space - 한 번 누르면 남은 연출 전부 생략(_skipAll).</summary>
        private bool CheckSkip()
        {
            if (!_skipAll && SkipPressed())
                _skipAll = true;
            return _skipAll;
        }

        /// <summary>오른쪽 위 [▶▶ 건너뛰기] - 화면 어디를 눌러도 건너뛰지만, 있는 줄 알게 보이는 버튼.</summary>
        private RectTransform MakeSkipButton()
        {
            var go = new GameObject("SkipButton", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (_panel != null)
            {
                img.sprite = _panel;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f / Px;
            }
            else
                img.color = new Color(0f, 0f, 0f, 0.6f);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(190f, 44f);
            rect.anchoredPosition = new Vector2(-115f, -40f);
            var label = MakeText(rect, "Label", 16, Color.white, Vector2.zero, 180f);
            _temp.Remove(label.gameObject); // 버튼은 연출마다 지우지 않고 다시 쓴다
            label.text = Loc.T("▶▶ 건너뛰기 [Space]");
            go.SetActive(false);
            return rect;
        }

        private static bool SkipPressed() =>
            GameInput.PointerDown || GameInput.Down(Key.Space) || GameInput.Down(Key.Enter) || GameInput.Down(Key.Escape);

        private static IEnumerator Tween(float seconds, Action<float> step)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                step(t / seconds);
                yield return null;
            }
            step(1f);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
                yield return null;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private void Cleanup()
        {
            foreach (var go in _temp)
                if (go != null)
                    Destroy(go);
            _temp.Clear();
            foreach (var p in _particles)
                if (p.Rect != null)
                    Destroy(p.Rect.gameObject);
            _particles.Clear();
            _shake = 0f;
            _shakeRoot.localScale = Vector3.one;
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _dim.color = new Color(0f, 0f, 0f, 0f);
        }

        private Image FullScreen(string goName, Color color)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return img;
        }

        private Image MakeImage(Transform parent, string goName, Sprite sprite, Vector2 position, bool track = true)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (track)
                _temp.Add(go);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            if (sprite != null)
                rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * Px;
            rect.anchoredPosition = position;
            return img;
        }

        private Text MakeText(Transform parent, string goName, int size, Color color, Vector2 position, float width)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (parent != _root)
                _temp.Add(go);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(2f, -2f);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, size + 12f);
            rect.anchoredPosition = position;
            return text;
        }
    }
}
