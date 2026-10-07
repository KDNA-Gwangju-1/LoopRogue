using UnityEngine;

namespace LoopRogue
{
    /// <summary>약점 표식에 걸린 몹 몸에 붙는 붉은 과녁(아이템 아이콘 그림) - 효과가 끝나거나 죽으면 사라지고, 안개로 몹이 가려지면
    /// 같이 숨는다. 몹 그림 크기(배율)가 제각각이라 월드 크기를 직접 맞춘다. 봇 실행 중엔 안 붙인다.</summary>
    public class WeaknessMark : MonoBehaviour
    {
        private const float WorldSize = 0.6f;                              // 표식 한 변(월드 유닛, 칸 = 1.5)
        private static readonly Vector3 Offset = new Vector3(0.42f, 0.5f, -0.3f); // 몸 오른쪽 위

        private EnemyActor _enemy;
        private SpriteRenderer _renderer;
        private SpriteRenderer _owner;
        private float _spriteSize;

        public static void Attach(EnemyActor enemy)
        {
            if (!HitFeedback.Enabled || enemy.GetComponentInChildren<WeaknessMark>() != null)
                return;
            var sprite = PixelUi.Icon("Item_Weakness");
            var go = new GameObject("WeaknessMark");
            go.transform.SetParent(enemy.transform, false);
            var renderer = sprite != null
                ? VisualUtil.CreateSpriteVisual(go, sprite, 1f, sortingOrder: 3)
                : VisualUtil.CreateSquareVisual(go, new Color(0.9f, 0.2f, 0.2f), 1f, sortingOrder: 3);
            var mark = go.AddComponent<WeaknessMark>();
            mark._enemy = enemy;
            mark._renderer = renderer;
            mark._owner = enemy.GetComponent<SpriteRenderer>();
            mark._spriteSize = sprite != null ? sprite.bounds.size.x : 1f;
            mark.LateUpdate();
        }

        private void LateUpdate()
        {
            if (_enemy == null || _enemy.Stats == null || _enemy.Stats.IsDead || _enemy.VulnerableTurns <= 0)
            {
                Destroy(gameObject);
                return;
            }

            _renderer.enabled = _owner == null || _owner.enabled;
            // 부모 배율·회전(옛 삼각형 방패병은 돌아감)과 상관없이 월드 기준 크기·자리
            var pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
            var parentScale = Mathf.Max(0.001f, _enemy.transform.lossyScale.x);
            var s = WorldSize * pulse / (_spriteSize * parentScale);
            transform.localScale = new Vector3(s, s, 1f);
            transform.rotation = Quaternion.identity;
            transform.position = _enemy.transform.position + Offset;
        }
    }
}
