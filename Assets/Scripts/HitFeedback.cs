using UnityEngine;

namespace LoopRogue
{
    /// <summary>타격감 연출 모음 - 화면 흔들림(CameraShake), 맞은 쪽 번쩍임/찌그러짐과 때린 쪽 들이받기(ActorJuice),
    /// 처치 파편(DeathBurst), 효과음(SfxPlayer)을 상황별 한 줄 호출로 묶는다. 게임 로직(데미지/턴)은 전혀 안 건드리고
    /// 보이는 것과 들리는 것만 더한다. 자동 플레이 봇이 돌 때(DamagePopup.Suppressed)는 전부 건너뛴다 - 컴포넌트도 안 붙인다.</summary>
    public static class HitFeedback
    {
        private static readonly Color PlayerHurtTint = new Color(1f, 0.35f, 0.35f);
        private const float BumpDistance = 0.28f; // 칸 크기 대비 비율

        public static bool Enabled => !DamagePopup.Suppressed;

        /// <summary>플레이어가 몹을 때렸을 때(죽었든 아니든 먼저 부른다).</summary>
        public static void OnPlayerHitEnemy(PlayerActor player, EnemyActor enemy, Vector2Int direction, bool isCritical)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Bump(direction, BumpDistance);
            var juice = ActorJuice.Get(enemy);
            juice.Flash(Color.white);
            juice.Squish(isCritical ? 0.3f : 0.18f);

            if (isCritical)
            {
                CameraShake.Shake(0.14f, 0.15f);
                SfxPlayer.Play(Sfx.Crit);
            }
            else
            {
                CameraShake.Shake(0.04f, 0.08f);
                SfxPlayer.Play(Sfx.Hit);
            }
        }

        /// <summary>몹이 죽는 순간 - Destroy 전에 불러야 색/위치를 읽을 수 있다.</summary>
        public static void OnEnemyKilled(EnemyActor enemy)
        {
            if (!Enabled)
                return;

            var renderer = enemy.GetComponent<SpriteRenderer>();
            var color = ActorJuice.BaseColorOf(enemy, renderer);
            if (enemy.IsBoss)
            {
                DeathBurst.Spawn(enemy.transform.position, color, count: 24, speed: 6f, size: 0.3f);
                CameraShake.Shake(0.4f, 0.45f);
                SfxPlayer.Play(Sfx.BossDown);
            }
            else
            {
                DeathBurst.Spawn(enemy.transform.position, color, count: 10, speed: 4f, size: 0.18f);
                CameraShake.Shake(0.08f, 0.12f);
                SfxPlayer.Play(Sfx.Kill);
            }
        }

        /// <summary>몹 평타/궁수 사격에 플레이어가 맞았을 때. 붙어 있는 몹이면 플레이어 쪽으로 들이받는 모션도.</summary>
        public static void OnPlayerHurt(PlayerActor player, EnemyActor attacker)
        {
            if (!Enabled)
                return;

            if (attacker != null && attacker.IsAdjacentTo(player.GridPos))
                ActorJuice.Get(attacker).Bump(player.GridPos - attacker.GridPos, BumpDistance);
            ActorJuice.Get(player).Flash(PlayerHurtTint);
            CameraShake.Shake(attacker != null && attacker.IsBoss ? 0.14f : 0.08f, 0.12f);
            SfxPlayer.Play(Sfx.Hurt);
        }

        /// <summary>보스 예고 공격이 발동하는 순간 - 피했어도 "쾅" 하는 느낌은 주고, 맞았으면 훨씬 크게.</summary>
        public static void OnBossPatternResolved(PlayerActor player, bool hitPlayer)
        {
            if (!Enabled)
                return;

            SfxPlayer.Play(Sfx.Slam);
            if (hitPlayer)
            {
                ActorJuice.Get(player).Flash(PlayerHurtTint);
                CameraShake.Shake(0.35f, 0.3f);
                SfxPlayer.Play(Sfx.Hurt);
            }
            else
            {
                CameraShake.Shake(0.12f, 0.18f);
            }
        }

        /// <summary>보스가 예고 칸을 깔 때 - 짧은 경고음.</summary>
        public static void OnTelegraph()
        {
            if (!Enabled)
                return;

            SfxPlayer.Play(Sfx.Warning);
        }
    }
}
