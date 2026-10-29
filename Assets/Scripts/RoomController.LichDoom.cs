using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>9층 리치 죽음의 낙인(10-19 사용자) - 체력 50%·25%에 낙인(DoomTurns턴)이 찍히고 그동안 리치는 무적.
    /// 방 가장자리 세 곳에서 영혼 구체 DoomOrbCount개가 나와 DoomOrbMoveEvery턴에 한 칸씩 나를 쫓아온다(벽은 돌아오고 몹은 통과).
    /// 구체가 리치 몸에 닿으면 흡수되고, DoomAbsorbNeeded개를 흡수시키면 낙인이 리치에게 옮겨 간다(큰 피해 + 기절).
    /// 구체가 나에게 닿으면 패턴 피해를 주고 사라진다. 구체가 다 없어졌는데 아직 모자라면 새로 나온다. 낙인 턴이 다 되면 즉사.
    /// 리치를 방패 삼아 구체가 리치를 지나오게 서는 게 공략.</summary>
    public partial class RoomController
    {
        public const int DoomTurns = 40;
        public const int DoomOrbCount = 3;
        public const int DoomAbsorbNeeded = 2;
        public const int DoomOrbMoveEvery = 2; // 매 턴 → 2턴에 한 칸(10-19 사용자 - 봇 300판 9층 보스방 사망 17.1, 목표 12, 구체 피격 판당 14.7)
        private const float DoomBurstDamage = 0.1f; // 낙인이 옮겨 갔을 때 리치가 받는 피해(최대 체력 비율)
        private static readonly Color DoomColor = new Color(0.75f, 0.45f, 1f);
        private static readonly Color OrbFallbackColor = new Color(0.35f, 0.9f, 0.5f);

        private sealed class DoomOrb
        {
            public Vector2Int Pos;
            public GameObject Go;
        }

        private int _doomTurnsLeft; // 0 = 낙인 없음
        private int _doomAbsorbed;
        private int _doomOrbClock; // 구체가 움직일 차례를 센다(DoomOrbMoveEvery턴마다)
        private readonly List<DoomOrb> _doomOrbs = new List<DoomOrb>();

        // ---- 회피 낙인(10-29 사용자) - 리치의 예고 공격을 피할 때마다 1개. 3개면 다음 공격은 맞아야 한다(맞으면 피해 없이 0으로),
        // 3개에서 또 피해 4개가 되면 즉사. "계속 피하기만 할 수는 없다" - 일부러 맞을 자리를 고르는 게 공략. ----
        public const int LichForceHitStacks = 3;
        public const int LichDeathStacks = 4;
        public int LichDodgeStacks { get; private set; }

        /// <summary>9층 리치 예고 공격이 터질 때(BossBrain) - 스택을 쌓거나 비운다. 이번 공격 피해를 없애야 하면 true(3개에서 맞음).
        /// 4개가 되면 즉사 - 사망 처리는 몹 턴 루프가.</summary>
        public bool OnLichPatternResolved(bool playerHit)
        {
            if (playerHit)
            {
                if (LichDodgeStacks < LichForceHitStacks)
                    return false; // 평소처럼 맞는다(스택은 그대로)
                LichDodgeStacks = 0;
                ShowMessage(Loc.T("<color=#C890FF>회피 낙인이 걷혔다!</color> (피해 없음)"));
                if (HitFeedback.Enabled)
                    DamagePopup.SpawnText(_player.transform.position, Loc.T("낙인 해제"), new Color(0.78f, 0.56f, 1f));
                return true;
            }
            LichDodgeStacks++;
            if (LichDodgeStacks >= LichDeathStacks)
            {
                GimmickKills[9]++;
                DeathCause.Set("g9_dodge");
                ShowMessage(Loc.T("<color=#FF4040>회피 낙인이 넘쳐 영혼이 찢겨 나갔다!</color>"));
                GimmickDeathFx();
                KillPlayerInstantly();
                return false;
            }
            if (HitFeedback.Enabled)
                DamagePopup.SpawnText(_player.transform.position, Loc.F("회피 낙인 {0}/{1}", LichDodgeStacks, LichForceHitStacks),
                    LichDodgeStacks >= LichForceHitStacks ? new Color(1f, 0.35f, 0.35f) : new Color(0.78f, 0.56f, 1f));
            ShowMessage(LichDodgeStacks >= LichForceHitStacks
                ? Loc.T("<color=#FF6060>회피 낙인 3 - 다음 체스판·영혼 구체는 반드시 맞아야 한다!</color> (맞으면 피해 없이 낙인이 걷힌다, 또 피하면 즉사)")
                : Loc.F("<color=#C890FF>회피 낙인 {0}/{1}</color> - 체스판·영혼 구체를 피하면 쌓인다, 3개면 한 번은 맞아야 한다", LichDodgeStacks, LichForceHitStacks));
            return false;
        }

        /// <summary>죽음의 낙인이 나에게 찍혀 있는 동안(리치 무적).</summary>
        public bool DoomActive => _gimmickStage == 9 && _doomTurnsLeft > 0;
        public IEnumerable<Vector2Int> DoomOrbPositions => _doomOrbs.Select(o => o.Pos);

        /// <summary>봇 통계용 - 낙인을 리치에게 옮긴 횟수 / 구체에 맞은 횟수.</summary>
        public static int DoomTransferCount, DoomOrbHitCount;

        private void BeginLichDoom(GimmickCutscene c)
        {
            _doomTurnsLeft = DoomTurns;
            _doomAbsorbed = 0;
            _doomOrbClock = 0;
            c.Steps.Add(() =>
            {
                // 유령 해골이 나를 머금고 턱을 닫아 감쌌다가 가슴의 낙인으로 조여 듦(10-23) - 96px = 용사 그림(64px·1.25칸)과 같은 도트 크기라 1.875칸
                Fx.Play("doom_mark", _player.transform.position, 1.875f, fps: 16f, sortingOrder: 9);
                if (HitFeedback.Enabled)
                    ActorJuice.Get(_player).Flash(DoomColor);
                return true;
            });
            c.Steps.Add(() => SpawnDoomOrbs() > 0);
        }

        // ------------------------------------------------------------------ 구체 생성
        /// <summary>방 가장자리 서로 다른 쪽(왼·오른·아래·위 중 무작위 셋)에 구체를 하나씩 - 만든 개수.</summary>
        private int SpawnDoomOrbs()
        {
            if (_gimmickBoss == null)
                return 0;
            var free = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsInBounds(p) && !Map.IsWall(p) && p != ExitPosition && !GridMap.Occupies(_gimmickBoss, p))
                    free.Add(p);
            }
            if (free.Count == 0)
                return 0;
            int minX = free.Min(p => p.x), maxX = free.Max(p => p.x), minY = free.Min(p => p.y), maxY = free.Max(p => p.y);
            System.Func<Vector2Int, bool>[] sides =
            {
                p => p.x <= minX + 1, p => p.x >= maxX - 1, p => p.y <= minY + 1, p => p.y >= maxY - 1,
            };
            var made = 0;
            foreach (var side in sides.OrderBy(_ => Rng.Next()))
            {
                if (made >= DoomOrbCount)
                    break;
                bool Far(Vector2Int p, int d) => Mathf.Abs(p.x - _player.GridPos.x) + Mathf.Abs(p.y - _player.GridPos.y) >= d
                                                 && _doomOrbs.All(o => o.Pos != p);
                var cells = free.Where(side).ToList();
                var pick = cells.Where(p => Far(p, 6)).OrderBy(_ => Rng.Next()).Cast<Vector2Int?>().FirstOrDefault()
                           ?? cells.Where(p => Far(p, 3)).OrderBy(_ => Rng.Next()).Cast<Vector2Int?>().FirstOrDefault();
                if (!pick.HasValue)
                    continue;
                AddDoomOrb(pick.Value);
                made++;
            }
            UpdateGimmickLabel();
            return made;
        }

        private void AddDoomOrb(Vector2Int p)
        {
            var orb = new DoomOrb { Pos = p };
            if (!DamagePopup.Suppressed)
            {
                orb.Go = new GameObject("DoomOrb");
                orb.Go.transform.SetParent(transform, false);
                orb.Go.transform.position = CellWorld(p);
                if (Fx.Play("soul_wisp", orb.Go.transform.position, 1f, fps: 8f, loop: true, sortingOrder: 6, parent: orb.Go.transform) == null)
                    VisualUtil.CreateSquareVisual(orb.Go, OrbFallbackColor, GridConstants.CellSize * 0.45f, sortingOrder: 6);
                _roomObjects.Add(orb.Go);
                Fx.Play("doom_summon", orb.Go.transform.position, 1f, fps: 16f, sortingOrder: 7);
            }
            _doomOrbs.Add(orb);
        }

        private static Vector3 CellWorld(Vector2Int p) => new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);

        private void RemoveDoomOrb(DoomOrb orb)
        {
            _doomOrbs.Remove(orb);
            if (orb.Go != null)
                Destroy(orb.Go);
        }

        private void ClearDoom()
        {
            foreach (var o in _doomOrbs.ToList())
                RemoveDoomOrb(o);
            _doomTurnsLeft = 0;
            _doomAbsorbed = 0;
        }

        // ------------------------------------------------------------------ 구체 길찾기(봇도 같은 규칙으로 미리 따라가 본다)
        /// <summary>target까지 벽을 돌아가는 걸음 수(몹·리치는 통과).</summary>
        public Dictionary<Vector2Int, int> DoomField(Vector2Int target)
        {
            var dist = new Dictionary<Vector2Int, int> { [target] = 0 };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in OrbDirs)
                {
                    var n = c + d;
                    if (!Map.IsInBounds(n) || Map.IsWall(n) || dist.ContainsKey(n))
                        continue;
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        private static readonly Vector2Int[] OrbDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// <summary>구체의 다음 칸 - target에 가까워지는 이웃 중 차이가 큰 축 쪽을 먼저(같으면 가로). 길이 없으면 제자리.</summary>
        public static Vector2Int DoomOrbNext(Vector2Int from, Vector2Int target, Dictionary<Vector2Int, int> field)
        {
            if (from == target || !field.TryGetValue(from, out var here))
                return from;
            var diff = target - from;
            var primary = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                ? new Vector2Int(System.Math.Sign(diff.x), 0)
                : new Vector2Int(0, System.Math.Sign(diff.y));
            var secondary = primary.x != 0
                ? new Vector2Int(0, System.Math.Sign(diff.y))
                : new Vector2Int(System.Math.Sign(diff.x), 0);
            foreach (var d in new[] { primary, secondary }.Concat(OrbDirs))
            {
                if (d == Vector2Int.zero)
                    continue;
                if (field.TryGetValue(from + d, out var there) && there < here)
                    return from + d;
            }
            return from;
        }

        // ------------------------------------------------------------------ 몹 턴 끝마다
        /// <summary>구체가 한 칸씩 쫓아온다 - 리치 몸에 닿으면 흡수, 나에게 닿으면 피해. 내가 구체에 맞아 죽었으면 true.</summary>
        private bool TickDoomOrbs()
        {
            if (_doomOrbs.Count == 0)
                return false;
            var move = ++_doomOrbClock % DoomOrbMoveEvery == 0; // 쉬는 턴에도 내가 구체 칸으로 들어가면 맞는다
            var field = move ? DoomField(_player.GridPos) : null;
            foreach (var orb in _doomOrbs.ToList())
            {
                if (!DoomActive)
                    break; // 이번 턴에 이미 옮겼다
                if (move && orb.Pos != _player.GridPos)
                {
                    orb.Pos = DoomOrbNext(orb.Pos, _player.GridPos, field);
                    if (orb.Go != null)
                        orb.Go.transform.position = CellWorld(orb.Pos);
                }
                if (_gimmickBoss != null && GridMap.Occupies(_gimmickBoss, orb.Pos))
                    AbsorbDoomOrb(orb);
                else if (orb.Pos == _player.GridPos && HitPlayerWithOrb(orb))
                    return true;
            }
            if (DoomActive && _doomOrbs.Count == 0)
            {
                ShowMessage(Loc.T("<color=#C090FF>리치가 영혼 구체를 다시 불러낸다!</color>"));
                SpawnDoomOrbs();
            }
            return false;
        }

        private void AbsorbDoomOrb(DoomOrb orb)
        {
            var at = CellWorld(orb.Pos);
            RemoveDoomOrb(orb);
            _doomAbsorbed++;
            TrailerControls.Cue(TrailerCue.DoomOrbs); // 트레일러 - 구체를 리치에게 먹이면 성공
            if (HitFeedback.Enabled)
            {
                Fx.Play("soul_orb", at, 1.5f, fps: 18f, sortingOrder: 7);
                if (_gimmickBoss != null)
                    ActorJuice.Get(_gimmickBoss).Flash(DoomColor);
                SfxPlayer.Play(Sfx.Slam);
            }
            if (_doomAbsorbed < DoomAbsorbNeeded)
            {
                UpdateGimmickLabel();
                return;
            }
            // 낙인이 리치에게 옮겨 간다
            DoomTransferCount++;
            var boss = _gimmickBoss;
            foreach (var o in _doomOrbs.ToList())
                RemoveDoomOrb(o);
            _doomTurnsLeft = 0;
            OnGimmickBroken();
            if (boss != null)
                DamageGimmickBoss(boss.Stats.MaxHealth * DoomBurstDamage, DoomColor);
            UpdateGimmickLabel();
        }

        /// <summary>구체가 나에게 닿았다 - 패턴 피해(갑옷 "철벽" 적용) 후 사라진다. 죽었으면 사망 처리까지 하고 true.</summary>
        private bool HitPlayerWithOrb(DoomOrb orb)
        {
            RemoveDoomOrb(orb);
            DoomOrbHitCount++;
            var boss = _gimmickBoss;
            if (boss != null)
            {
                var ironWall = EquipmentEffects.Has(ItemSlot.Armor, 5) ? 1f - EquipmentEffects.BossPatternReduction : 1f;
                DeathCause.Set("doom_orb");
                var dealt = _player.Stats.TakeIncomingDamage(boss.Stats.AttackPower * BossBrain.PatternDamageMultiplier * ironWall);
                PlayLog.PlayerHit("영혼 구체", dealt, _player.Stats);
                DamagePopup.Spawn(_player.transform.position, dealt, new Color(0.6f, 1f, 0.7f), isCritical: true);
            }
            if (HitFeedback.Enabled)
            {
                Fx.Play("soul_orb", _player.transform.position, 1.5f, fps: 18f, sortingOrder: 7);
                ActorJuice.Get(_player).Flash(DoomColor);
                CameraShake.Shake(0.3f, 0.3f);
                SfxPlayer.Play(Sfx.Hurt);
            }
            ShowMessage(Loc.T("<color=#FF8080>영혼 구체에 맞았다!</color>"));
            UpdateGimmickLabel();
            if (!_player.Stats.IsDead)
                return false;
            ClearAllTelegraphs();
            _loopManager.OnPlayerDied();
            return true;
        }
    }
}
