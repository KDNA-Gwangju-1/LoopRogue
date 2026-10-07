using UnityEngine;

namespace LoopRogue
{
    /// <summary>캐릭터를 감싸는 빛 고리 - 처음엔 조금 크게 나타나 몸을 조이듯 감쌌다가(앞 35%), 바깥으로 퍼지며 사라진다.
    /// 정화제처럼 "몸에 걸린 걸 풀어내는" 연출용. 고리 그림은 코드로 한 번만 만든다(그림 파일 없음). 따라갈 대상이 움직여도 붙어 다닌다.</summary>
    public class AuraEffect : MonoBehaviour
    {
        private const float Duration = 0.6f;
        private const float WrapPart = 0.35f;

        private static Sprite _ring;

        private SpriteRenderer _renderer;
        private Transform _follow;
        private Color _color;
        private float _elapsed;

        // 칸 크기 대비 배율: 나타날 때 → 감쌀 때 → 퍼져 사라질 때
        private const float StartSize = 1.5f, WrapSize = 0.95f, EndSize = 2.3f;

        public static void Spawn(Transform follow, Color color)
        {
            var go = new GameObject("Aura");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = RingSprite();
            renderer.sortingOrder = 5; // 캐릭터 위
            renderer.color = color;
            var aura = go.AddComponent<AuraEffect>();
            aura._renderer = renderer;
            aura._follow = follow;
            aura._color = color;
            aura.Place(StartSize);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var k = _elapsed / Duration;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float size, alpha;
            if (k < WrapPart)
            {
                var t = k / WrapPart;
                size = Mathf.Lerp(StartSize, WrapSize, 1f - (1f - t) * (1f - t)); // 빠르게 조여든다
                alpha = Mathf.Lerp(0.3f, 1f, t);
            }
            else
            {
                var t = (k - WrapPart) / (1f - WrapPart);
                size = Mathf.Lerp(WrapSize, EndSize, t * t);                       // 천천히 시작해 확 퍼진다
                alpha = 1f - t;
            }
            Place(size);
            _renderer.color = new Color(_color.r, _color.g, _color.b, _color.a * alpha);
        }

        private void Place(float size)
        {
            var s = size * GridConstants.CellSize;
            transform.localScale = new Vector3(s, s, 1f);
            if (_follow != null)
                transform.position = _follow.position + new Vector3(0f, 0f, -0.45f);
        }

        /// <summary>32x32 도트 고리(바깥 두꺼운 테 + 안쪽 옅은 빛) - 1유닛 크기.</summary>
        private static Sprite RingSprite()
        {
            if (_ring != null)
                return _ring;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var c = (n - 1) * 0.5f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                var a = d > 15.5f ? 0f : d >= 13f ? 1f : d >= 10f ? 0.35f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _ring = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _ring;
        }
    }
}
