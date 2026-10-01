using UnityEngine;

namespace LoopRogue
{
    /// <summary>출구 칸 - 천천히 숨 쉬듯 밝아졌다 어두워져서 눈에 띄게 한다(봇 실행 중엔 그냥 둔다).</summary>
    public class ExitMarker : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Color _baseColor;
        private Vector3 _baseScale;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _baseColor = _renderer != null ? _renderer.color : Color.white;
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            if (DamagePopup.Suppressed || _renderer == null)
                return;

            var k = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            _renderer.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, Mathf.Lerp(0.45f, 1f, k));
            transform.localScale = _baseScale * Mathf.Lerp(0.85f, 1f, k);
        }
    }
}
