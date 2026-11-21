using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>도트 이펙트 - Resources/Effects/{이름}/{번호}.png 프레임을 차례로 보여주고 사라진다(반복 이펙트는 KeepAlive가 false가 될 때까지).
    /// 그림이 없거나 자동 플레이 봇 중(DamagePopup.Suppressed)이면 아무것도 안 만들고 null을 돌려준다 - 호출부는 예전 연출로 대신한다.
    /// 그림은 오른쪽을 향하게 그려져 있어서 angle(도)로 돌려 방향을 맞춘다.
    /// 다 쓴 이펙트는 지우지 않고 이름별 풀에 꺼 두었다가 다시 쓴다(10-25 렉 점검 - 큰 보스방에서 예고 칸·터지는 그림 100~200개를
    /// 한 프레임에 만들고 지우던 것이 순간 멈춤의 원인). 밖에서 오브젝트를 지울 땐 Destroy 대신 Fx.Release로 돌려주면 다시 쓴다
    /// (그냥 Destroy해도 문제는 없다 - 풀에서 빠질 뿐).</summary>
    public class Fx : MonoBehaviour
    {
        private const float DefaultFps = 18f;
        private const int DefaultSortingOrder = 7; // 몹·플레이어(0~1)와 데미지 숫자 사이
        private const int PoolLimitPerName = 400;

        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();
        private static readonly Dictionary<string, Stack<Fx>> Pool = new Dictionary<string, Stack<Fx>>();
        private static Transform _poolRoot;

        private string _name;
        private bool _pooled;

        private Sprite[] _frames;
        private SpriteRenderer _renderer;
        private float _fps;
        private bool _loop;
        private float _time;
        private Vector3 _rise;

        // 날아가는 이펙트(화살·거미줄)
        private bool _moving;
        private Vector3 _from, _to;
        private float _travelSeconds;
        private Action _onArrive;

        /// <summary>반복 이펙트를 언제까지 둘지(거미줄 묶임 등) - false가 되는 순간 사라진다.</summary>
        public Func<bool> KeepAlive;
        /// <summary>풀로 돌아가 쉬는 중이 아니다(지금 화면에 도는 중) - 반납된 뒤에도 오브젝트는 살아 있어서 null 비교로는 모른다.</summary>
        public bool IsLive => !_pooled;

        /// <summary>Frames의 나눠 읽기판(AssetWarmup) - 한 장씩 Resources.LoadAsync.</summary>
        public static System.Collections.IEnumerator WarmupAsync(string name)
        {
            if (Cache.ContainsKey(name))
                yield break;
            var list = new List<Sprite>();
            for (var i = 0; ; i++)
            {
                var req = Resources.LoadAsync<Sprite>($"Effects/{name}/{i}");
                yield return req;
                if (!(req.asset is Sprite sprite))
                    break;
                list.Add(sprite);
            }
            if (!Cache.ContainsKey(name))
                Cache[name] = list.ToArray();
        }

        public static Sprite[] Frames(string name)
        {
            if (Cache.TryGetValue(name, out var frames))
                return frames;
            var list = new List<Sprite>();
            for (var i = 0; ; i++)
            {
                var sprite = Resources.Load<Sprite>($"Effects/{name}/{i}");
                if (sprite == null)
                    break;
                list.Add(sprite);
            }
            return Cache[name] = list.ToArray();
        }

        /// <summary>Unknown(9등급) 무기 이펙트 재생 배속.</summary>
        public const float UnknownSpeed = 1.5f;

        /// <summary>이름에 등급 꼬리 "_g9"가 있으면(끝이거나 뒤에 "_") Unknown 등급 그림.</summary>
        private static bool IsUnknownGrade(string name)
        {
            var i = name.IndexOf("_g9", System.StringComparison.Ordinal);
            return i >= 0 && (i + 3 == name.Length || name[i + 3] == '_');
        }

        /// <summary>11-09 이 그림을 fps로 틀 때 쓸 배속 - Unknown이면 UnknownSpeed, 그래도 maxSeconds보다 길면 딱 maxSeconds에 끝나게 더 빠르게.
        /// 같이 맞물려 나오는 그림(아래 겹·발톱·바닥 문장)은 본 그림의 이 값을 Play(speed:)로 똑같이 넘긴다.</summary>
        public static float Speed(string name, float fps, float maxSeconds)
        {
            var speed = IsUnknownGrade(name) ? UnknownSpeed : 1f;
            var frames = Frames(name).Length;
            if (maxSeconds > 0f && frames > 0 && frames / (fps * speed) > maxSeconds)
                speed = frames / (fps * maxSeconds);
            return speed;
        }

        /// <param name="sizeCells">그림 가로 길이(칸 단위)</param>
        /// <param name="speed">0보다 크면 이 배속으로(fps×, 지연÷) - Unknown 자동 배속 대신. Speed()로 구한 값.</param>
        /// <param name="riseCells">초당 위로 떠오르는 거리(칸 단위)</param>
        public static Fx Play(string name, Vector3 position, float sizeCells, float angle = 0f, Color? tint = null,
            float fps = DefaultFps, bool loop = false, int sortingOrder = DefaultSortingOrder, Transform parent = null, float riseCells = 0f, float delay = 0f, float speed = 0f)
        {
            if (DamagePopup.Suppressed)
                return null;
            var frames = Frames(name);
            if (frames.Length == 0)
                return null;

            var fx = Take(name);
            var go = fx.gameObject;
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var renderer = fx._renderer;
            renderer.sprite = frames[0];
            renderer.color = tint ?? Color.white;
            renderer.sortingOrder = sortingOrder;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            RoomClip.Apply(renderer); // 맵 밖으로 삐져나간 부분은 안 그린다

            var spriteWidth = frames[0].rect.width / frames[0].pixelsPerUnit;
            var scale = sizeCells * GridConstants.CellSize / spriteWidth;
            if (parent != null && parent.lossyScale.x > 0.0001f)
                scale /= parent.lossyScale.x; // 부모 크기와 상관없이 같은 크기로
            go.transform.localScale = new Vector3(scale, scale, 1f);

            if (speed <= 0f) // 11-09 사용자 "언노운 등급 모든 이펙트 좀 더 빨리" - 지연도 같은 비율로 줄여 맞물린 타이밍 유지
                speed = IsUnknownGrade(name) ? UnknownSpeed : 1f;
            fps *= speed;
            delay /= speed;
            fx._frames = frames;
            fx._fps = fps;
            fx._loop = loop;
            fx._rise = Vector3.up * riseCells * GridConstants.CellSize;
            fx._time = -Mathf.Max(0f, delay); // delay초 뒤에 나타남(그동안 안 보임)
            renderer.enabled = delay <= 0f;
            fx._moving = false;
            fx._onArrive = null;
            fx.KeepAlive = null;
            go.SetActive(true);
            return fx;
        }

        /// <summary>풀에서 하나 꺼내거나(꺼진 채) 새로 만든다.</summary>
        private static Fx Take(string name)
        {
            if (_poolRoot == null)
                Pool.Clear(); // 씬이 바뀌어 풀 오브젝트가 다 지워졌다
            if (Pool.TryGetValue(name, out var stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled != null)
                    {
                        pooled._pooled = false;
                        return pooled;
                    }
                }
            }
            var go = new GameObject("Fx_" + name);
            go.SetActive(false);
            var fx = go.AddComponent<Fx>();
            fx._name = name;
            fx._renderer = go.AddComponent<SpriteRenderer>();
            return fx;
        }

        /// <summary>이펙트 오브젝트를 돌려준다 - Fx면 풀로, 아니면(예전 사각형 등) 그냥 지운다.</summary>
        public static void Release(GameObject go)
        {
            if (go == null)
                return;
            if (go.TryGetComponent<Fx>(out var fx))
                fx.Recycle();
            else
                Destroy(go);
        }

        private void Recycle()
        {
            if (_pooled)
                return;
            KeepAlive = null;
            _onArrive = null;
            if (_poolRoot == null)
            {
                Pool.Clear();
                _poolRoot = new GameObject("FxPool").transform;
            }
            if (!Pool.TryGetValue(_name, out var stack))
                Pool[_name] = stack = new Stack<Fx>();
            if (stack.Count >= PoolLimitPerName)
            {
                Destroy(gameObject);
                return;
            }
            _pooled = true;
            gameObject.SetActive(false);
            transform.SetParent(_poolRoot, false);
            stack.Push(this);
        }

        /// <summary>from → to로 seconds 동안 날아가고 도착하면 onArrive를 부른 뒤 사라진다. 그림이 없으면 false(호출부가 예전 선으로).</summary>
        public static bool Projectile(string name, Vector3 from, Vector3 to, float sizeCells, float seconds, Action onArrive = null)
        {
            var delta = to - from;
            var fx = Play(name, from, sizeCells, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, loop: true, sortingOrder: 6);
            if (fx == null)
                return false;
            fx._moving = true;
            fx._from = from;
            fx._to = to;
            fx._travelSeconds = Mathf.Max(0.01f, seconds);
            fx._onArrive = onArrive;
            return true;
        }

        /// <summary>방향(칸 단위 벡터) → 이펙트 회전 각도.</summary>
        public static float Angle(Vector2Int direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        private void Update()
        {
            _time += Time.deltaTime;
            if (_time < 0f)
                return; // 아직 delay 중
            if (!_renderer.enabled)
                _renderer.enabled = true;

            if (_moving)
            {
                var t = _time / _travelSeconds;
                transform.position = Vector3.Lerp(_from, _to, Mathf.Min(1f, t));
                if (t >= 1f)
                {
                    var arrive = _onArrive;
                    Recycle();
                    arrive?.Invoke();
                    return;
                }
            }
            else if (_rise != Vector3.zero)
            {
                transform.position += _rise * Time.deltaTime;
            }

            if (KeepAlive != null && !KeepAlive())
            {
                Recycle();
                return;
            }

            var index = (int)(_time * _fps);
            if (!_loop && index >= _frames.Length)
            {
                Recycle();
                return;
            }
            _renderer.sprite = _frames[index % _frames.Length];
        }
    }
}
