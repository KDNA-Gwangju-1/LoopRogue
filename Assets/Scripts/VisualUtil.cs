using UnityEngine;

namespace LoopRogue
{
    /// <summary>2D 프로토타입 전용 - 별도 아트 없이 SpriteRenderer로 단색 정사각형을 찍어낸다.
    /// 1x1 흰 텍스처 하나를 캐싱해두고 색은 SpriteRenderer.color로만 다르게 준다(텍스처를 색상별로
    /// 여러 장 만들 필요 없음). CreatePrimitive 방식과 달리 콜라이더가 안 붙어서 따로 지울 필요도
    /// 없다 - 어차피 판정은 GridMap 딕셔너리로만 하므로 더 간단해졌다.</summary>
    public static class VisualUtil
    {
        private static Sprite _cachedSquareSprite;

        private static Sprite GetSquareSprite()
        {
            if (_cachedSquareSprite != null)
                return _cachedSquareSprite;

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // pixelsPerUnit=1이라 스프라이트 원본 크기가 정확히 1x1 월드 유닛 - 이후 localScale을
            // 그대로 "칸 하나 대비 몇 배 크기"로 쓸 수 있다.
            _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _cachedSquareSprite;
        }

        public static SpriteRenderer CreateSquareVisual(GameObject owner, Color color, float size, int sortingOrder)
        {
            var renderer = owner.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = owner.AddComponent<SpriteRenderer>();

            renderer.sprite = GetSquareSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            owner.transform.localScale = new Vector3(size, size, 1f);
            return renderer;
        }

        /// <summary>진짜 아트(예: StoryRPG 로그 캐릭터 스프라이트)를 쓸 때 - spritePixelsToUnits를
        /// 텍스처 폭/높이와 똑같이 맞춰서 임포트해두면(PlayerSprite.png.meta 참고) 원본 스프라이트
        /// 자체가 정확히 1x1 월드 유닛이 되므로, CreateSquareVisual과 완전히 같은 size 단위(칸 하나
        /// 대비 배율)로 그대로 쓸 수 있다. 색은 원본 그대로 두고(흰색 = 틴트 없음) 덧씌우지 않는다.</summary>
        public static SpriteRenderer CreateSpriteVisual(GameObject owner, Sprite sprite, float size, int sortingOrder)
        {
            var renderer = owner.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = owner.AddComponent<SpriteRenderer>();

            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = sortingOrder;
            owner.transform.localScale = new Vector3(size, size, 1f);
            return renderer;
        }
    }
}
