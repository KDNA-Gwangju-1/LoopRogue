using UnityEngine;

namespace LoopRogue
{
    /// <summary>미끼(허수아비) - 칸 하나를 차지하고, 남은 턴 동안 방의 몹들이 플레이어 대신 이걸 노린다(보스 예고 패턴은 그대로
    /// 플레이어 기준). 맞아도 부서지지 않고 시간이 다 되면 사라진다.</summary>
    public class DecoyActor : GridActor
    {
        public int TurnsLeft { get; private set; }

        public void Initialize(int turns)
        {
            TurnsLeft = turns;
            Stats = new CharacterStats(1f, 0f);
            var renderer = VisualUtil.CreateSquareVisual(gameObject, new Color(0.75f, 0.6f, 0.35f), GridConstants.CellSize * 0.6f, sortingOrder: 1);
            renderer.color = new Color(0.75f, 0.6f, 0.35f);
        }

        /// <summary>몹이 미끼를 때렸을 때 - 피해 숫자만 보여준다.</summary>
        public void TakeHit(EnemyActor attacker, float damage)
        {
            DamagePopup.Spawn(transform.position, damage, new Color(0.9f, 0.8f, 0.6f));
            if (HitFeedback.Enabled)
                ActorJuice.Get(this).Squish(0.2f);
        }

        /// <summary>몹 턴이 한 번 끝날 때마다. 다 됐으면 true.</summary>
        public bool Tick() => --TurnsLeft <= 0;
    }
}
