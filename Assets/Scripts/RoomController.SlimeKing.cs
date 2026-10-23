using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>4층 슬라임 킹 기믹(10-16 사용자, 예전 "졸개 흡수"를 대신함) - 체력 50% 아래에서 한 번.
    /// 킹이 2x2로 작아지며 둘로 갈라지고, 떨어져 나간 분열체는 벽에 붙은 무적 포탑이 된다. 포탑은 벽을 따라 미끄러져 내 줄에 맞춘 뒤
    /// 내 쪽 일직선 + 위아래 대각선 2줄(3줄) 점액 파동을 예고하고 쏜다. 킹은 점프 짓누르기(BossBrain.Jump)로 내 자리에 내려앉고
    /// 내려앉은 칸에 점액을 남긴다 - 밟으면 한 턴 묶이고(점액은 떨어진다), 포탑 파동이 지나가면 지워진다.
    /// 점액이 SlimePuddleLimit칸 쌓이면 바닥이 뒤덮여 즉사(보호막·부활 무시).</summary>
    public partial class RoomController
    {
        public const int SlimePuddleLimit = 8; // 16 → 8(10-16 사용자 "점액 지우기가 생각보다 쉽다") - 점프 한 번에 4칸이라 안 씻으면 점프 두 번에 즉사
        public const int SlimeTurretCooldown = 1;   // 파동을 쏜 뒤 쉬는 턴
        public const int SlimeTurretHopEvery = 3;   // 이만큼 쏘면 다른 벽으로 옮겨 붙는다
        public const float SlimeTurretDamageRate = 1f; // 킹 공격력 ×
        private static readonly Color SlimeTelegraphColor = new Color(1f, 0.45f, 0.75f, 0.42f); // 보스 예고(빨강)와 구분되는 분홍
        private static readonly Color SlimeColor = new Color(0.84f, 0.25f, 0.33f, 0.8f);      // 킹 그림(Boss4) 몸 색

        private readonly Dictionary<Vector2Int, GameObject> _slimePuddles = new Dictionary<Vector2Int, GameObject>();
        private EnemyActor _slimeTurret;
        private Vector2Int _turretNormal;           // 포탑이 붙은 벽에서 방 안쪽 방향
        private List<Vector2Int> _turretPending;    // 예고해 둔 파동 칸
        private int _turretCooldown;
        private int _turretShots;

        public IReadOnlyCollection<Vector2Int> SlimePuddles => _slimePuddles.Keys;
        public bool IsSlimePuddle(Vector2Int p) => _slimePuddles.ContainsKey(p);
        public EnemyActor SlimeTurret => _slimeTurret;
        public Vector2Int SlimeTurretNormal => _turretNormal;

        // ------------------------------------------------------------------ 시작
        private void BeginSlimeSplit(EnemyActor boss, GimmickCutscene c)
        {
            // 1) 몸을 옆으로 늘이다 가운데가 잘록해져 둘로 갈라지는 그림(Boss4/split_south_0~4) - 그림이 없으면 건너뜀
            c.Steps.Add(() => boss.TryGetComponent<SpriteAnimator>(out var anim) && anim.PlayClip("split", 0.9f, 0.25f));
            c.StepDelays.Add(1.15f);
            // 2) 2x2로 줄고, 떨어져 나간 조각이 포물선으로 날아가 벽에 철썩 붙는다(10-16 사용자 "벽에 붙는 것까지 컷신으로")
            c.Steps.Add(() =>
            {
                var from = boss.transform.position + new Vector3(GridConstants.CellSize * 0.9f, 0f, 0f); // 분열 그림의 오른쪽 조각 자리
                boss.ShrinkToSlimeHalf();
                if (!SpawnSlimeTurret(boss))
                    return true;
                if (HitFeedback.Enabled)
                {
                    CameraShake.Focus(_slimeTurret); // 날아가는 조각을 따라간다
                    StartCoroutine(FlyBlobToWall(_slimeTurret, from));
                }
                return true;
            });
            c.StepDelays.Add(BlobFlySeconds + 0.55f);
        }

        // ---- 포탑은 2x2(10-16 사용자) - 기준점 a가 왼쪽 아래 칸, 몸은 a ~ a+(1,1). normal = 등진 벽에서 방 안쪽 방향.
        private static readonly Vector2Int[] TurretDirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        private static Vector2Int TangentOf(Vector2Int n) => new Vector2Int(Mathf.Abs(n.y), Mathf.Abs(n.x)); // 벽을 따라가는 축(+쪽)

        private static IEnumerable<Vector2Int> TurretBody(Vector2Int a)
        {
            for (var dx = 0; dx <= 1; dx++)
            for (var dy = 0; dy <= 1; dy++)
                yield return a + new Vector2Int(dx, dy);
        }

        /// <summary>몸 중 벽 쪽(뒤) 두 칸 / 방 안쪽(앞) 두 칸.</summary>
        private static IEnumerable<Vector2Int> TurretSide(Vector2Int a, Vector2Int n, bool back)
        {
            var towardBack = back ? -n : n;
            return TurretBody(a).Where(c => !TurretBody(a).Contains(c + towardBack));
        }

        /// <summary>2x2 몸이 a에 들어가고(벽·출구·다른 몹 없음), 뒤 두 칸이 모두 벽을 등지는지.</summary>
        private bool TurretFits(Vector2Int a, Vector2Int n, GridActor self)
        {
            foreach (var c in TurretBody(a))
            {
                if (!Map.IsInBounds(c) || Map.IsWall(c) || c == ExitPosition)
                    return false;
                var who = Map.GetActorAt(c);
                if (who != null && who != self)
                    return false;
            }
            return TurretSide(a, n, back: true).All(b => !Map.IsInBounds(b - n) || Map.IsWall(b - n));
        }

        private List<(Vector2Int A, Vector2Int N)> TurretSpots(GridActor self, System.Func<Vector2Int, Vector2Int, bool> extra)
        {
            var spots = new List<(Vector2Int, Vector2Int)>();
            for (var x = 0; x < Map.Width - 1; x++)
            for (var y = 0; y < Map.Height - 1; y++)
            {
                var a = new Vector2Int(x, y);
                foreach (var n in TurretDirs)
                    if (TurretFits(a, n, self) && extra(a, n))
                        spots.Add((a, n));
            }
            return spots;
        }

        private int DistanceToPlayer(Vector2Int a) =>
            TurretBody(a).Min(c => Mathf.Abs(c.x - _player.GridPos.x) + Mathf.Abs(c.y - _player.GridPos.y));

        private bool SpawnSlimeTurret(EnemyActor boss)
        {
            if (_slimeTurret != null)
                return false;
            var spots = TurretSpots(null, (a, n) => TurretBody(a).All(c => GridMap.FootprintChebyshev(boss, c) >= 2));
            if (spots.Count == 0)
                return false;
            // 나에게서 먼 쪽 벽부터(바로 옆에 붙으면 첫 파동을 못 피한다)
            var far = spots.OrderByDescending(sp => DistanceToPlayer(sp.A)).Take(Mathf.Max(1, spots.Count / 3)).ToList();
            var (spot, normal) = far[Rng.Next(far.Count)];

            var go = new GameObject("SlimeTurret");
            go.transform.SetParent(transform, false);
            var turret = go.AddComponent<EnemyActor>();
            turret.Initialize(boss.Stats.MaxHealth, boss.Stats.AttackPower * SlimeTurretDamageRate, isBoss: false, kind: EnemyKind.Melee);
            turret.MarkAsMinion();          // 보상 없음, 킹이 죽으면 같이 사라짐
            turret.MarkAsSlimeTurret(boss); // 2x2 몸 + 칼이 안 통함 + 킹 그림
            Map.PlaceActor(turret, spot);
            turret.SyncTransform();
            _enemies.Add(turret);
            _slimeTurret = turret;
            _turretNormal = normal;
            _turretPending = null;
            _turretCooldown = 1;
            _turretShots = 0;
            return true;
        }

        private const float BlobFlySeconds = 0.6f;

        /// <summary>분열 컷신 - 떨어져 나간 조각(킹 그림을 작게)이 from에서 포탑 자리까지 포물선으로 날아가 벽에 붙고, 그때 포탑이 나타난다.</summary>
        private IEnumerator FlyBlobToWall(EnemyActor turret, Vector3 from)
        {
            var turretRenderers = turret.GetComponentsInChildren<SpriteRenderer>();
            foreach (var r in turretRenderers)
                r.enabled = false;
            var blob = new GameObject("SlimeBlob");
            blob.transform.SetParent(transform, false);
            var sprite = SpriteAnimator.FirstFrame("Boss4");
            var size = turret.transform.localScale;
            var renderer = sprite != null
                ? VisualUtil.CreateSpriteVisual(blob, sprite, 1f, sortingOrder: 9)
                : VisualUtil.CreateSquareVisual(blob, SlimeColor, 1f, sortingOrder: 9);
            var to = turret.transform.position;
            var height = GridConstants.CellSize * (2f + Vector3.Distance(from, to) / GridConstants.CellSize * 0.15f);
            for (var t = 0f; t < BlobFlySeconds; t += Time.deltaTime)
            {
                var k = t / BlobFlySeconds;
                blob.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (height * 4f * k * (1f - k));
                var spin = Mathf.Sin(k * Mathf.PI * 2f) * 0.12f; // 날면서 출렁
                blob.transform.localScale = new Vector3(size.x * (0.7f + 0.3f * k) * (1f + spin), size.y * (0.7f + 0.3f * k) * (1f - spin), 1f);
                yield return null;
            }
            Destroy(blob);
            foreach (var r in turretRenderers)
                if (r != null)
                    r.enabled = true;
            if (turret == null)
                yield break;
            ActorJuice.Get(turret).Squish(0.5f); // 철썩
            Fx.Play("morph", turret.transform.position, 2.2f, fps: 16f, sortingOrder: 9, tint: SlimeColor);
            DeathBurst.Spawn(turret.transform.position, SlimeColor, 14, GridConstants.CellSize * 2f, GridConstants.CellSize * 0.14f);
            CameraShake.Shake(0.25f, 0.4f);
            SfxPlayer.Play(Sfx.Slam);
        }

        // ------------------------------------------------------------------ 포탑 턴(EnemyActor.TakeTurn이 부른다)
        public void SlimeTurretTurn(EnemyActor turret, PlayerActor player)
        {
            if (turret != _slimeTurret || _gimmickBoss == null || _gimmickBoss.Stats.IsDead)
                return;
            if (_turretPending != null)
            {
                FireSlimeWave(turret, player);
                return;
            }
            // 내 줄에 맞춘다 - 몸 두 줄(벽을 따라 0·1번째) 안에 내가 들어오게 한 칸씩 미끄러진다
            var t = TangentOf(_turretNormal);
            int Along() => (player.GridPos.x - turret.GridPos.x) * t.x + (player.GridPos.y - turret.GridPos.y) * t.y;
            if (_turretCooldown > 0)
                _turretCooldown--;
            var along = Along();
            if (along < 0 || along > 1)
            {
                var step = turret.GridPos + t * (along < 0 ? -1 : 1);
                if (TurretFits(step, _turretNormal, turret))
                {
                    Map.MoveActor(turret, step);
                    along = Along();
                }
            }
            if (_turretCooldown > 0 || along < -1 || along > 2)
                return; // 아직 쉬는 중이거나 줄이 멀다 - 계속 미끄러진다
            if (player.RootedTurns > 0)
                return; // 점액에 묶여 다음 턴에 못 움직이면 예고해도 못 피한다 - 풀릴 때까지 미룬다
            _turretPending = SlimeWaveTiles(turret.GridPos);
            ShowEnemyTelegraph(turret, _turretPending, SlimeTelegraphColor, "tel_slime"); // 흘러내리는 점액 예고(10-23)
        }

        /// <summary>포탑 파동 - 앞 두 칸에서 방 안쪽으로 곧게(두 줄 폭) + 앞 모서리에서 바깥쪽 대각선 2줄, 벽이나 방 끝까지.</summary>
        private List<Vector2Int> SlimeWaveTiles(Vector2Int a)
        {
            var n = _turretNormal;
            var t = TangentOf(n);
            var front = TurretSide(a, n, back: false).OrderBy(c => c.x * t.x + c.y * t.y).ToList(); // 벽을 따라 작은 쪽 → 큰 쪽
            var rays = new List<(Vector2Int From, Vector2Int D)>();
            foreach (var f in front)
                rays.Add((f, n));
            rays.Add((front[0], n - t));
            rays.Add((front[front.Count - 1], n + t));
            var tiles = new List<Vector2Int>();
            foreach (var (from, d) in rays)
                for (var p = from + d; Map.IsInBounds(p) && !Map.IsWall(p); p += d)
                    if ((_gimmickBoss == null || !GridMap.Occupies(_gimmickBoss, p)) && !tiles.Contains(p))
                        tiles.Add(p);
            return tiles;
        }

        private void FireSlimeWave(EnemyActor turret, PlayerActor player)
        {
            var tiles = _turretPending;
            _turretPending = null;
            ClearEnemyTelegraph(turret);
            var set = new HashSet<Vector2Int>(tiles);
            if (HitFeedback.Enabled)
            {
                foreach (var t in tiles)
                {
                    var at = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f);
                    if (Fx.Play("slime_wave", at, 1.15f, fps: 16f, sortingOrder: 4) == null) // 포탑 전용 점액 물결(10-19) - 없으면 예전 포자를 점액색으로
                        Fx.Play("boss_ring", at, 1f, fps: 16f, sortingOrder: 4, tint: SlimeColor);
                }
                SfxPlayer.Play(Sfx.Slam);
                if (turret.TryGetComponent<SpriteAnimator>(out var shootAnim))
                    shootAnim.PlayAttack(_turretNormal);
            }
            if (set.Contains(player.GridPos))
            {
                BossBrain.PatternHitCount++;
                if (!player.ReflectInto(_gimmickBoss, this)) // 반사 부적 - 파동도 막고 킹에게 되돌린다(포탑은 무적)
                {
                    var dealt = player.Stats.TakeIncomingDamage(turret.Stats.AttackPower);
                    PlayLog.PlayerHit("슬라임 포탑 파동", dealt, player.Stats);
                    DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.4f, 0.55f), isCritical: true);
                }
            }
            BossBrain.PatternResolveCount++;
            var washed = _slimePuddles.Keys.Where(set.Contains).ToList();
            foreach (var p in washed)
                RemoveFloorProp(_slimePuddles, p, SlimeColor);
            if (washed.Count > 0)
                ShowMessage(Loc.F("<color=#FF7088>파동이 점액 {0}칸을 씻어 냈다</color> (남은 점액 {1}/{2})", washed.Count, _slimePuddles.Count, SlimePuddleLimit));
            _turretCooldown = SlimeTurretCooldown;
            if (++_turretShots % SlimeTurretHopEvery == 0)
                HopSlimeTurret(turret);
            UpdateGimmickLabel();
        }

        /// <summary>다른 벽으로 옮겨 붙는다 - 지금 벽과 다른 방향 벽 중 나에게서 4칸 넘게 떨어진 자리.</summary>
        private void HopSlimeTurret(EnemyActor turret)
        {
            var spots = TurretSpots(turret, (a, n) => n != _turretNormal && DistanceToPlayer(a) >= 4
                                                     && (_gimmickBoss == null || TurretBody(a).All(c => !GridMap.Occupies(_gimmickBoss, c))));
            if (spots.Count == 0)
                return;
            var (spot, normal) = spots[Rng.Next(spots.Count)];
            Map.MoveActor(turret, spot);
            if (turret.TryGetComponent<SpriteAnimator>(out var anim))
                anim.SnapToGrid();
            turret.SyncTransform();
            _turretNormal = normal;
            if (HitFeedback.Enabled)
                HitFeedback.OnSummon(turret.transform.position);
        }

        // ------------------------------------------------------------------ 점액
        /// <summary>킹이 내려앉은 칸(몸 칸)에 점액 - 즉사 개수에 닿았으면 true(사망 처리는 몹 턴 루프가).</summary>
        public bool AddSlimePuddles(IEnumerable<Vector2Int> cells)
        {
            if (_gimmickStage != 4)
                return false;
            foreach (var p in cells)
                if (Map.IsInBounds(p) && !Map.IsWall(p) && !_slimePuddles.ContainsKey(p) && p != ExitPosition)
                    SpawnFloorProp(_slimePuddles, p, "Slime_Puddle", SlimeColor);
            UpdateGimmickLabel();
            if (_slimePuddles.Count >= SlimePuddleLimit)
            {
                ShowMessage(Loc.T("<color=#FF4040>점액이 바닥을 뒤덮어 삼켜 버렸다!</color>"));
                GimmickKills[4]++;
                GimmickDeathFx();
                KillPlayerInstantly();
                return true;
            }
            if (_slimePuddles.Count >= SlimePuddleLimit - 4)
                ShowMessage(Loc.F("<color=#FF6060>바닥이 점액으로 끈적해진다...</color> {0}/{1}", _slimePuddles.Count, SlimePuddleLimit));
            return false;
        }

        /// <summary>플레이어가 막 밟은 칸이 점액이면 한 턴 묶이고 점액은 떨어진다(PlayerActor.TryAct).</summary>
        public void CheckSlimePuddle()
        {
            if (!_slimePuddles.ContainsKey(_player.GridPos))
                return;
            RemoveFloorProp(_slimePuddles, _player.GridPos, SlimeColor);
            _player.ApplyRoot(1);
            ShowMessage(Loc.T("<color=#FF7088>점액에 발이 붙었다!</color> 한 턴 움직일 수 없다"));
            if (HitFeedback.Enabled)
                ActorJuice.Get(_player).Flash(new Color(1f, 0.45f, 0.55f));
            UpdateGimmickLabel();
        }

        private void ClearSlimeKing()
        {
            foreach (var go in _slimePuddles.Values)
                if (go != null)
                    Destroy(go);
            _slimePuddles.Clear();
            _slimeTurret = null;
            _turretPending = null;
            _turretCooldown = 0;
            _turretShots = 0;
        }
    }
}
