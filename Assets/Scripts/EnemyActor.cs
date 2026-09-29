using UnityEngine;

namespace LoopRogue
{
    /// <summary>일반 몹/보스 공통 - 턴제라 실시간 이동 대신 TurnManager 역할을 하는
    /// RoomController가 불러주는 TakeTurn() 안에서만 행동한다. 경로탐색 없이 "플레이어 쪽으로
    /// 한 칸" 그리디 추적만 한다(2주 프로토타입 범위 - 벽에 막히면 그냥 그 턴은 패스).</summary>
    public class EnemyActor : GridActor
    {
        public bool IsBoss { get; private set; }

        public void Initialize(float maxHealth, float attackPower, bool isBoss)
        {
            IsBoss = isBoss;
            Stats = new CharacterStats(maxHealth, attackPower);

            var color = isBoss ? new Color(0.55f, 0.05f, 0.05f) : new Color(0.8f, 0.2f, 0.2f);
            var scale = isBoss ? GridConstants.CellSize * 1.1f : GridConstants.CellSize * 0.6f;
            VisualUtil.CreateSquareVisual(gameObject, color, scale, sortingOrder: 0);
        }

        public void TakeTurn(PlayerActor player)
        {
            if (Stats.IsDead)
                return;

            var diff = player.GridPos - GridPos;
            if (Mathf.Abs(diff.x) + Mathf.Abs(diff.y) == 1)
            {
                var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower);
                DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f));
                return;
            }

            // 대각선 이동 없음 - 더 멀리 떨어진 축으로만 한 칸 이동.
            Vector2Int step;
            if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
                step = new Vector2Int(diff.x == 0 ? 0 : (diff.x > 0 ? 1 : -1), 0);
            else
                step = new Vector2Int(0, diff.y == 0 ? 0 : (diff.y > 0 ? 1 : -1));

            if (step == Vector2Int.zero)
                return;

            var target = GridPos + step;
            if (Map.IsWalkable(target))
                Map.MoveActor(this, target);
            // 막혀 있으면(다른 몹이 그 칸을 차지 등) 이번 턴은 그냥 패스 - 진짜 경로탐색은 범위 밖.
        }
    }
}
