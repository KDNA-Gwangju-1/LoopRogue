using UnityEngine;

namespace LoopRogue
{
    /// <summary>맞은 자리 위로 떠오르며 사라지는 데미지 숫자 - TextMesh(월드 스페이스 3D 텍스트)로
    /// 만든다. Canvas/UI 필요 없이 그냥 씬 안에 떠 있는 오브젝트라, 격자 액터들과 똑같이 카메라가
    /// 알아서 잡아준다.</summary>
    public class DamagePopup : MonoBehaviour
    {
        private const float Lifetime = 0.7f;
        private const float FloatSpeed = 1.2f;

        private TextMesh _text;
        private float _elapsed;

        /// <summary>자동 플레이 봇이 초고속으로 돌 때 켠다 - 팝업 오브젝트가 수백 개씩 쌓이는 걸 막는다.</summary>
        public static bool Suppressed;

        public static void Spawn(Vector3 worldPos, float amount, Color color, bool isCritical = false)
        {
            if (Suppressed)
                return;

            var go = new GameObject("DamagePopup");
            var jitterX = Random.Range(-0.15f, 0.15f);
            // z를 살짝 당겨서(카메라 쪽으로) 액터 스프라이트보다 항상 앞에 그려지게 한다 -
            // sortingOrder만 믿기엔 카메라의 Transparency Sort Mode 설정이 불확실해서 이중 안전장치.
            go.transform.position = worldPos + new Vector3(jitterX, 0.5f, -1f);

            var textMesh = go.AddComponent<TextMesh>();
            textMesh.text = isCritical ? $"CRIT -{amount:0}!" : (amount > 0 ? $"-{amount:0}" : "0");
            textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textMesh.fontSize = isCritical ? 64 : 48;
            textMesh.characterSize = isCritical ? 0.14f : 0.12f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = isCritical ? new Color(1f, 0.8f, 0.1f) : color;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = textMesh.font.material;
            renderer.sortingOrder = 10;

            var popup = go.AddComponent<DamagePopup>();
            popup._text = textMesh;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            transform.position += Vector3.up * (FloatSpeed * Time.deltaTime);

            var alpha = Mathf.Clamp01(1f - _elapsed / Lifetime);
            var c = _text.color;
            _text.color = new Color(c.r, c.g, c.b, alpha);

            if (_elapsed >= Lifetime)
                Destroy(gameObject);
        }
    }
}
