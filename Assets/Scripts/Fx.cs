using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>도트 이펙트 - Resources/Effects/{이름}/{번호}.png 프레임을 차례로 보여주고 사라진다(반복 이펙트는 KeepAlive가 false가 될 때까지).
    /// 그림이 없거나 자동 플레이 봇 중(DamagePopup.Suppressed)이면 아무것도 안 만들고 null을 돌려준다 - 호출부는 예전 연출로 대신한다.
    /// 그림은 오른쪽을 향하게 그려져 있어서 angle(도)로 돌려 방향을 맞춘다.</summary>
    public class Fx : MonoBehaviour
    {
        private const float DefaultFps = 18f;
        private const int DefaultSortingOrder = 7; // 몹·플레이어(0~1)와 데미지 숫자 사이

        private static readonly Dictionary<string, Sprite[]> Cache = new Dictionary<string, Sprite[]>();

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

        /// <param name="sizeCells">그림 가로 길이(칸 단위)</param>
        /// <param name="riseCells">초당 위로 떠오르는 거리(칸 단위)</param>
        public static Fx Play(string name, Vector3 position, float sizeCells, float angle = 0f, Color? tint = null,
            float fps = DefaultFps, bool loop = false, int sortingOrder = DefaultSortingOrder, Transform parent = null, float riseCells = 0f)
        {
            if (DamagePopup.Suppressed)
                return null;
            var frames = Frames(name);
            if (frames.Length == 0)
                return null;

            var go = new GameObject("Fx_" + name);
            if (parent != null)
                go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = frames[0];
            renderer.color = tint ?? Color.white;
            renderer.sortingOrder = sortingOrder;

            var spriteWidth = frames[0].rect.width / frames[0].pixelsPerUnit;
            var scale = sizeCells * GridConstants.CellSize / spriteWidth;
            if (parent != null && parent.lossyScale.x > 0.0001f)
                scale /= parent.lossyScale.x; // 부모 크기와 상관없이 같은 크기로
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var fx = go.AddComponent<Fx>();
            fx._frames = frames;
            fx._renderer = renderer;
            fx._fps = fps;
            fx._loop = loop;
            fx._rise = Vector3.up * riseCells * GridConstants.CellSize;
            return fx;
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

            if (_moving)
            {
                var t = _time / _travelSeconds;
                transform.position = Vector3.Lerp(_from, _to, Mathf.Min(1f, t));
                if (t >= 1f)
                {
                    _onArrive?.Invoke();
                    Destroy(gameObject);
                    return;
                }
            }
            else if (_rise != Vector3.zero)
            {
                transform.position += _rise * Time.deltaTime;
            }

            if (KeepAlive != null && !KeepAlive())
            {
                Destroy(gameObject);
                return;
            }

            var index = (int)(_time * _fps);
            if (!_loop && index >= _frames.Length)
            {
                Destroy(gameObject);
                return;
            }
            _renderer.sprite = _frames[index % _frames.Length];
        }
    }
}
