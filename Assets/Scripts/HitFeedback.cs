using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>타격감 연출 모음 - 화면 흔들림(CameraShake), 맞은 쪽 번쩍임/찌그러짐과 때린 쪽 들이받기(ActorJuice),
    /// 처치 파편(DeathBurst), 효과음(SfxPlayer)을 상황별 한 줄 호출로 묶는다. 게임 로직(데미지/턴)은 전혀 안 건드리고
    /// 보이는 것과 들리는 것만 더한다. 자동 플레이 봇이 돌 때(DamagePopup.Suppressed)는 전부 건너뛴다 - 컴포넌트도 안 붙인다.</summary>
    public static class HitFeedback
    {
        private static readonly Color SpriteHitTint = new Color(1f, 0.45f, 0.45f);

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
                Fx.Play("hit_spark", enemy.transform.position, 0.6f, tint: new Color(0.75f, 0.8f, 0.9f));
                CameraShake.Shake(0.03f, 0.06f);
                SfxPlayer.Play(Sfx.Block);
                return;
            }
            juice.Flash(enemy.GetComponent<SpriteAnimator>() != null ? SpriteHitTint : Color.white); // 그림은 흰색 틴트면 티가 안 나서 붉게
            juice.Squish(isCritical ? 0.3f : 0.18f);

            // 베기 - 몹 자리에서 플레이어 쪽으로 살짝 당겨서, 때린 방향을 향하게(치명타는 크고 붉은 금빛).
            var cell = GridConstants.CellSize;
            var slashPos = enemy.transform.position - new Vector3(direction.x, direction.y, 0f) * cell * 0.2f;
            Fx.Play(isCritical ? "slash_crit" : "slash", slashPos, isCritical ? 1.7f : 1.2f, Fx.Angle(direction));
            Fx.Play("hit_spark", enemy.transform.position, isCritical ? 0.9f : 0.6f);

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
            // 사라지는 연기(몹 색으로 살짝 물들임) - 연기 그림이 있으면 사각형 파편은 조금만.
            var smokeTint = Color.Lerp(Color.white, color, 0.35f);
            var smoke = Fx.Play("death_smoke", enemy.transform.position, enemy.IsBoss ? 2.8f : 1.2f, tint: smokeTint);
            if (enemy.IsBoss)
            {
                DeathBurst.Spawn(enemy.transform.position, color, count: smoke != null ? 14 : 24, speed: 6f, size: 0.3f);
                CameraShake.Shake(0.4f, 0.45f);
                SfxPlayer.Play(Sfx.BossDown);
            }
            else
            {
                DeathBurst.Spawn(enemy.transform.position, color, count: smoke != null ? 5 : 10, speed: 4f, size: 0.18f);
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
            Fx.Play("hit_spark", player.transform.position, 0.7f, tint: new Color(1f, 0.45f, 0.4f));
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

            var fire = Fx.Play("explosion", position, 3.3f, fps: 16f); // 주변 3x3이 터지는 크기
            DeathBurst.Spawn(position, new Color(1f, 0.6f, 0.15f), count: fire != null ? 10 : 22, speed: 8f, size: 0.25f);
            CameraShake.Shake(0.3f, 0.3f);
            SfxPlayer.Play(Sfx.Explosion);
        }

        /// <summary>거미줄에 맞았을 때 - 하얗게 번쩍이고 끈적한 소리.</summary>
        public static void OnPlayerWebbed(PlayerActor player)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Flash(new Color(0.85f, 0.8f, 1f));
            // 묶여 있는 동안 몸에 거미줄(풀리거나 죽으면 사라짐) - 이미 감겨 있으면 하나만.
            if (player.transform.Find("Fx_web_bind") == null)
            {
                var web = Fx.Play("web_bind", player.transform.position, 1.1f, fps: 6f, loop: true, sortingOrder: 3, parent: player.transform);
                if (web != null)
                    web.KeepAlive = () => player != null && player.RootedTurns > 0 && !player.Stats.IsDead;
            }
            CameraShake.Shake(0.05f, 0.1f);
            SfxPlayer.Play(Sfx.Web);
        }

        /// <summary>보스 예고 공격이 발동하는 순간 - 피했어도 "쾅" 하는 느낌은 주고, 맞았으면 훨씬 크게.</summary>
        public static void OnBossPatternResolved(PlayerActor player, bool hitPlayer, BossPatternType type, IEnumerable<Vector2Int> tiles)
        {
            if (!Enabled)
                return;

            // 패턴마다 칸 위에 터지는 그림(강타 = 흙·돌, 돌진 = 먼지, 십자 = 얼음 균열, 파동 = 포자, 저격 = 빛줄기, X자 = 보라 베기)
            var name = type switch
            {
                BossPatternType.Slam => "boss_slam",
                BossPatternType.Charge => "boss_charge",
                BossPatternType.Cross => "boss_cross",
                BossPatternType.Ring => "boss_ring",
                BossPatternType.Snipe => "boss_snipe",
                BossPatternType.SnipeFollowUp => "boss_snipe",
                BossPatternType.Diagonal => "boss_diag",
                BossPatternType.MirrorShards => "morph",         // 거울 조각이 칸마다 터진다
                _ => null,
            };
            if (name != null)
                foreach (var t in tiles)
                    Fx.Play(name, new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f), 1.15f, fps: 16f, sortingOrder: 4);

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
            var path = to - from;
            if (path.sqrMagnitude > 0.01f)
                Fx.Play("dash", (from + to) * 0.5f, path.magnitude / GridConstants.CellSize + 0.6f,
                    Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg, fps: 22f, sortingOrder: 2);
            CameraShake.Shake(0.05f, 0.08f);
            SfxPlayer.Play(Sfx.Dash);
        }

        /// <summary>회전 베기 - 플레이어가 한 바퀴 돌고 주변으로 파편이 퍼진다(타격 연출은 몹마다 OnPlayerHitEnemy가 따로).</summary>
        public static void OnSpin(PlayerActor player)
        {
            if (!Enabled)
                return;

            ActorJuice.Get(player).Spin();
            var swirl = Fx.Play("spin", player.transform.position, 3.3f, fps: 22f); // 주변 8칸을 감싸는 고리
            DeathBurst.Spawn(player.transform.position, new Color(0.7f, 0.9f, 1f), count: swirl != null ? 8 : 16, speed: 7f, size: 0.12f);
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

        /// <summary>크게 회복했을 때(방 클리어 회복, 회복 샘) - 초록 십자가 떠오른다.</summary>
        public static void OnHeal(PlayerActor player)
        {
            if (!Enabled)
                return;
            Fx.Play("heal", player.transform.position, 1.3f, fps: 14f, sortingOrder: 8);
        }

        /// <summary>레벨업 - 발밑에서 금빛 기둥이 솟는다.</summary>
        public static void OnLevelUp(PlayerActor player)
        {
            if (!Enabled)
                return;
            // 그림이 세로로 길어서(48x96) 아래쪽 고리가 발밑에 오게 위로 올린다.
            Fx.Play("levelup", player.transform.position + Vector3.up * GridConstants.CellSize * 0.95f, 1.3f, fps: 14f, sortingOrder: 8);
        }

        /// <summary>골드를 얻은 자리 - 동전이 돌며 떠오른다.</summary>
        public static void OnGold(Vector3 position)
        {
            if (!Enabled)
                return;
            Fx.Play("coin", position + Vector3.up * GridConstants.CellSize * 0.3f, 0.4f, fps: 14f, sortingOrder: 8, riseCells: 0.9f);
        }

        /// <summary>보스가 졸개를 부른 칸 - 붉은 마법진.</summary>
        public static void OnSummon(Vector3 position)
        {
            if (!Enabled)
                return;
            Fx.Play("summon", position, 1.5f, fps: 14f, sortingOrder: 2);
        }

        /// <summary>거울 깨기 - 크게 흔들리며 거울 조각이 더 크게 터진다.</summary>
        public static void OnMirrorBroken(EnemyActor boss)
        {
            if (!Enabled)
                return;
            Fx.Play("morph", boss.transform.position, 4.2f, fps: 14f, sortingOrder: 8);
            CameraShake.Shake(0.25f, 0.3f);
            SfxPlayer.Play(Sfx.Crit);
        }

        /// <summary>거울 가면 기사가 다른 보스로 변신 - 거울 조각이 터져 나간다.</summary>
        public static void OnMorph(EnemyActor boss)
        {
            if (!Enabled)
                return;
            Fx.Play("morph", boss.transform.position, 3.2f, fps: 16f, sortingOrder: 8);
            CameraShake.Shake(0.08f, 0.15f);
        }

        /// <summary>아이템을 쓴 순간 - 설치형은 놓은 칸에 먼지, 범위형은 그 범위만큼 퍼지는 그림, 몸에 거는 건 색 고리.
        /// (보호막·반사 부적·약점 표식·기절은 그 뒤로 StatusIcons가 계속 보여준다.)</summary>
        public static void OnItemUsed(PlayerActor player, ItemType type, Vector2Int target)
        {
            if (!Enabled)
                return;
            var cell = GridConstants.CellSize;
            var at = player.transform.position;
            var targetPos = new Vector3(target.x * cell, target.y * cell, 0f);
            switch (type)
            {
                case ItemType.Torch:
                case ItemType.Trap:
                case ItemType.Decoy:
                case ItemType.Bomb:
                    Fx.Play("item_place", targetPos, 0.9f, fps: 14f, sortingOrder: 3);
                    break;
                case ItemType.Smoke:
                    Fx.Play("item_smoke", at, ItemInfo.SmokeRadius * 2 + 1.5f, fps: 10f, sortingOrder: 8);
                    break;
                case ItemType.Flash:
                    Fx.Play("item_flash", at, ItemInfo.FlashRadius * 2 + 2f, fps: 14f, sortingOrder: 8);
                    CameraShake.Shake(0.08f, 0.12f);
                    break;
                case ItemType.Cleanse:
                    Fx.Play("item_cleanse", at, 1.3f, fps: 14f, sortingOrder: 8);
                    break;
                case ItemType.Reflect:
                    Fx.Play("item_ring", at, 1.8f, tint: new Color(1f, 0.85f, 0.35f), fps: 14f, sortingOrder: 8);
                    break;
                case ItemType.Weakness:
                    Fx.Play("item_ring", at, ItemInfo.WeaknessRadius * 2 + 1f, tint: new Color(1f, 0.35f, 0.35f), fps: 12f, sortingOrder: 8);
                    break;
                case ItemType.Barrier:
                    Fx.Play("item_ring", at, 1.6f, tint: new Color(0.55f, 0.85f, 1f), fps: 14f, sortingOrder: 8);
                    break;
            }
        }
    }
}
