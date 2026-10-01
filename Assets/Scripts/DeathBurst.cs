using UnityEngine;

namespace LoopRogue
{
    /// <summary>몹이 죽을 때 사방으로 튀는 작은 사각형 파편 - 파편마다 이 컴포넌트가 붙어 스스로 날아가며 줄어들고 사라진다.
    /// 파티클 시스템 없이 VisualUtil의 단색 사각형만으로 만든다.</summary>
    public class DeathBurst : MonoBehaviour
    {
        private const float Lifetime = 0.4f;
        private const float Drag = 6f;

        private Vector3 _velocity;
        private float _elapsed;
        private float _startSize;
        private SpriteRenderer _renderer;
        private Color _color;

        public static void Spawn(Vector3 center, Color color, int count, float speed, float size)
        {
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject("DeathBurst");
                var renderer = VisualUtil.CreateSquareVisual(go, color, size, sortingOrder: 6);
                go.transform.position = center + new Vector3(0f, 0f, -0.5f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 90f));

                var angle = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                var burst = go.AddComponent<DeathBurst>();
                burst._velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (speed * Random.Range(0.6f, 1.2f));
                burst._startSize = size;
                burst._renderer = renderer;
                burst._color = color;
            }

            // 가운데 흰 번쩍임 - 크게 퍼지며 사라진다.
            var pop = new GameObject("DeathBurst");
            var popRenderer = VisualUtil.CreateSquareVisual(pop, Color.white, size * 4f, sortingOrder: 5);
            pop.transform.position = center + new Vector3(0f, 0f, -0.4f);
            var popBurst = pop.AddComponent<DeathBurst>();
            popBurst._velocity = Vector3.zero;
            popBurst._startSize = -size * 4f; // 음수 = 줄어들지 말고 커지기
            popBurst._renderer = popRenderer;
            popBurst._color = new Color(1f, 1f, 1f, 0.8f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var t = _elapsed / Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += _velocity * Time.deltaTime;
            _velocity *= Mathf.Max(0f, 1f - Drag * Time.deltaTime);

            var size = _startSize >= 0f ? _startSize * (1f - t) : -_startSize * (1f + t * 0.8f);
            transform.localScale = new Vector3(size, size, 1f);
            _renderer.color = new Color(_color.r, _color.g, _color.b, _color.a * (1f - t));
        }
    }
}
