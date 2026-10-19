using UnityEngine;

namespace LoopRogue
{
    /// <summary>이펙트가 맵 밖(방 둘레 장식 벽 바깥의 검은 곳)에 그려지지 않게 자르는 마스크(10-19 사용자).
    /// 방을 깔 때마다 방 + 둘레 벽 한 줄 크기의 SpriteMask를 하나 두고, 이펙트(Fx)·처치 파편·산산조각 조각은
    /// 마스크가 있을 때만 "마스크 안에서만 보이기"로 그린다(마스크가 없는 로비 등에서는 평소대로).</summary>
    public class RoomClip : MonoBehaviour
    {
        private static int _alive;
        private static Sprite _square;

        /// <summary>지금 방에 마스크가 있는지 - 방을 바꾸는 프레임엔 옛 마스크가 늦게 지워지므로 개수로 센다.</summary>
        public static bool Active => _alive > 0;

        public static GameObject Create(Transform parent, int width, int height)
        {
            if (DamagePopup.Suppressed)
                return null; // 봇 실행 중엔 그리는 게 없다
            if (_square == null)
            {
                var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
                var pixels = new Color32[16];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply();
                _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // 1유닛 정사각형
            }
            var go = new GameObject("RoomClip");
            go.transform.SetParent(parent, false);
            var cell = GridConstants.CellSize;
            // 칸 (x, y)의 가운데가 (x, y) × 칸 크기 - 둘레 벽 한 줄(-1 ~ 너비)까지 덮는다
            go.transform.position = new Vector3((width - 1) * 0.5f * cell, (height - 1) * 0.5f * cell, 0f);
            go.transform.localScale = new Vector3((width + 2) * cell, (height + 2) * cell, 1f);
            var mask = go.AddComponent<SpriteMask>();
            mask.sprite = _square;
            go.AddComponent<RoomClip>();
            return go;
        }

        /// <summary>이 그림을 방 안에서만 보이게(마스크가 있을 때만).</summary>
        public static void Apply(SpriteRenderer renderer)
        {
            if (renderer != null && Active)
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        private void OnEnable() => _alive++;
        private void OnDisable() => _alive--;
    }
}
