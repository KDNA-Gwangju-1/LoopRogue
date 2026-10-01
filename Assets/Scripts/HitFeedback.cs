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
        public static void OnPlayerHitEnemy(PlayerActor player, EnemyActor enemy, Vector2Int direction, bool isCritical, bool blocked = false)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Bump(direction, BumpDistance);
            var juice = ActorJuice.Get(enemy);
            if (blocked)
            {
                // 방패에 막힘 - 번쩍임 대신 살짝만 찌그러지고 "깡" 소리.
                juice.Squish(0.06f);
                CameraShake.Shake(0.03f, 0.06f);
                SfxPlayer.Play(Sfx.Block);
                return;
            }
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

        /// <summary>폭발병이 불을 붙였을 때 - 치익.</summary>
        public static void OnFuseLit(EnemyActor bomber)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(bomber).Squish(0.25f);
            SfxPlayer.Play(Sfx.Fuse);
        }

        /// <summary>폭발 - 주황 파편 크게 + 화면 크게 흔들림 + 쾅.</summary>
        public static void OnExplosion(Vector3 position)
        {
            if (!Enabled)
                return;

            DeathBurst.Spawn(position, new Color(1f, 0.6f, 0.15f), count: 22, speed: 8f, size: 0.25f);
            CameraShake.Shake(0.3f, 0.3f);
            SfxPlayer.Play(Sfx.Explosion);
        }

        /// <summary>거미줄에 맞았을 때 - 하얗게 번쩍이고 끈적한 소리.</summary>
        public static void OnPlayerWebbed(PlayerActor player)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Flash(new Color(0.85f, 0.8f, 1f));
            CameraShake.Shake(0.05f, 0.1f);
            SfxPlayer.Play(Sfx.Web);
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

        /// <summary>대시 직후(이미 새 칸으로 옮긴 뒤) - 지나온 길에 잔상을 남긴다.</summary>
        public static void OnDash(PlayerActor player, Vector3 from)
        {
            if (!Enabled)
                return;

            var to = player.GridWorldPosition;
            var renderer = player.GetComponent<SpriteRenderer>();
            if (renderer != null && (to - from).sqrMagnitude > 0.01f)
            {
                const int ghosts = 4;
                for (var i = 0; i < ghosts; i++)
                {
                    var pos = Vector3.Lerp(from, to, (float)i / ghosts);
                    DeathBurst.SpawnGhost(renderer.sprite, pos, player.transform.localScale.x, new Color(0.5f, 0.8f, 1f, 0.15f + 0.1f * i));
                }
            }
            CameraShake.Shake(0.05f, 0.08f);
            SfxPlayer.Play(Sfx.Dash);
        }

        /// <summary>회전 베기 - 플레이어가 한 바퀴 돌고 주변으로 파편이 퍼진다(타격 연출은 몹마다 OnPlayerHitEnemy가 따로).</summary>
        public static void OnSpin(PlayerActor player)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Spin();
            DeathBurst.Spawn(player.transform.position, new Color(0.7f, 0.9f, 1f), count: 16, speed: 7f, size: 0.12f);
            CameraShake.Shake(0.1f, 0.12f);
            SfxPlayer.Play(Sfx.Spin);
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
