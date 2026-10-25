using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>4층 슬라임 킹 기믹(10-16 사용자, 예전 "졸개 흡수"를 대신함) - 체력 50% 아래에서 한 번.
    /// 킹이 2x2로 작아지며 둘로 갈라지고, 떨어져 나간 분열체는 방 둘레 장식 벽 위로 올라간 무적 포탑이 된다(10-25 사용자 - 예전엔 방 안 벽가에
    /// 2x2로 붙어 한 벽만 미끄러지다 세 번 쏘면 다른 벽으로 순간이동). 포탑은 둘레 벽을 타고 모퉁이를 돌며 나를 따라다니다
    /// 내 줄에 오면 방 안쪽으로 일직선(두 줄 폭) + 양옆 대각선 2줄 점액 파동을 예고하고 쏜다. 바닥 칸은 차지하지 않는다.
    /// 킹은 점프 짓누르기(BossBrain.Jump)로 내 자리에 내려앉고 내려앉은 몸 4칸 중 3칸에 점액을 남긴다 - 밟으면 한 턴 묶이고(점액은 떨어진다),
    /// 포탑 파동이 지나가면 지워진다. 점액이 SlimePuddleLimit칸 쌓이면 바닥이 뒤덮여 즉사(보호막·부활 무시).</summary>
    public partial class RoomController
    {
        public const int SlimePuddleLimit = 8; // 16 → 8(10-16 사용자 "점액 지우기가 생각보다 쉽다") - 점프 한 번에 3칸이라 안 씻으면 점프 세 번에 즉사
        public const int SlimePuddlesPerJump = 3; // 킹이 내려앉을 때 몸 4칸 중 점액이 남는 칸(10-25 사용자 4 → 3)
        public const int SlimeTurretCooldown = 1;   // 파동을 쏜 뒤 쉬는 턴
        public const int SlimeTurretFarSteps = 3;   // 따라갈 자리가 이보다 멀면 한 턴에 두 칸씩 벽을 탄다
        public const float SlimeTurretDamageRate = 1f; // 킹 공격력 ×
        private static readonly Color SlimeTelegraphColor = new Color(1f, 0.45f, 0.75f, 0.42f); // 보스 예고(빨강)와 구분되는 분홍
        private static readonly Color SlimeColor = new Color(0.84f, 0.25f, 0.33f, 0.8f);      // 킹 그림(Boss4) 몸 색

        private readonly Dictionary<Vector2Int, GameObject> _slimePuddles = new Dictionary<Vector2Int, GameObject>();
        private EnemyActor _slimeTurret;
        private Vector2Int _turretNormal;           // 포탑이 앉은 벽에서 방 안쪽 방향
        private List<Vector2Int> _turretPending;    // 예고해 둔 파동 칸
        private int _turretCooldown;
        private readonly List<Vector2Int> _wallRing = new List<Vector2Int>(); // 방 둘레 장식 벽(시계 방향, 네 모서리 칸 제외)
        private int _turretRing;                    // 포탑이 앉은 자리 - 고리 순서상 i·i+1 두 칸

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

        // ---- 포탑 자리 = 방 둘레 장식 벽(방 칸 바로 바깥 한 줄, 이동 판정 없음) 위의 같은 변 이웃 두 칸. 바닥 칸을 차지하지 않는다.
        private static int Dot2(Vector2Int a, Vector2Int b) => a.x * b.x + a.y * b.y;
        private static Vector2Int TangentOf(Vector2Int n) => new Vector2Int(Mathf.Abs(n.y), Mathf.Abs(n.x)); // 벽을 따라가는 축(+쪽)

        private void BuildWallRing()
        {
            _wallRing.Clear();
            int w = Map.Width, h = Map.Height;
            for (var x = 0; x < w; x++) _wallRing.Add(new Vector2Int(x, -1));      // 아래 변
            for (var y = 0; y < h; y++) _wallRing.Add(new Vector2Int(w, y));       // 오른쪽 변
            for (var x = w - 1; x >= 0; x--) _wallRing.Add(new Vector2Int(x, h));  // 위 변
            for (var y = h - 1; y >= 0; y--) _wallRing.Add(new Vector2Int(-1, y)); // 왼쪽 변
        }

        private Vector2Int Ring(int i) => _wallRing[((i % _wallRing.Count) + _wallRing.Count) % _wallRing.Count];

        private Vector2Int InwardOf(Vector2Int c) =>
            c.y < 0 ? Vector2Int.up : c.x >= Map.Width ? Vector2Int.left : c.y >= Map.Height ? Vector2Int.down : Vector2Int.right;

        /// <summary>고리 i·i+1이 같은 변이면 포탑이 앉을 수 있다(모퉁이를 걸치면 안 됨). a = 벽을 따라 작은 쪽 칸, n = 방 안쪽.</summary>
        private bool RingSpot(int i, out Vector2Int a, out Vector2Int n)
        {
            var c0 = Ring(i);
            var c1 = Ring(i + 1);
            n = InwardOf(c0);
            var t = TangentOf(n);
            a = Dot2(c0, t) <= Dot2(c1, t) ? c0 : c1;
            return InwardOf(c1) == n && Mathf.Abs(Dot2(c1 - c0, t)) == 1;
        }

        /// <summary>포탑 몸(벽 위 두 칸) 기준으로 내가 벽을 따라 몇 칸째에 있나 - 0·1이면 일직선 두 줄 안.</summary>
        private static int AlongOf(Vector2Int a, Vector2Int n, Vector2Int p) => Dot2(p - a, TangentOf(n));

        private int RingDistance(int from, int to)
        {
            var d = ((to - from) % _wallRing.Count + _wallRing.Count) % _wallRing.Count;
            return Mathf.Min(d, _wallRing.Count - d);
        }

        private int DistanceToPlayer(int i)
        {
            RingSpot(i, out var a, out var n);
            var t = TangentOf(n);
            return Mathf.Min(Mathf.Abs((a + n).x - _player.GridPos.x) + Mathf.Abs((a + n).y - _player.GridPos.y),
                             Mathf.Abs((a + t + n).x - _player.GridPos.x) + Mathf.Abs((a + t + n).y - _player.GridPos.y));
        }

        /// <summary>포탑을 고리 i 자리에 앉힌다 - 지도 칸은 차지하지 않고(GridPos만), 그림은 두 벽 칸 사이 가운데.</summary>
        private void SeatSlimeTurret(EnemyActor turret, int i)
        {
            RingSpot(i, out var a, out var n);
            _turretRing = i;
            _turretNormal = n;
            var t = TangentOf(n);
            turret.WallVisualOffset = new Vector3(t.x, t.y, 0f) * (GridConstants.CellSize * 0.5f);
            turret.Map = Map;
            turret.GridPos = a;
        }

        private bool SpawnSlimeTurret(EnemyActor boss)
        {
            if (_slimeTurret != null)
                return false;
            BuildWallRing();
            var spots = Enumerable.Range(0, _wallRing.Count).Where(i => RingSpot(i, out _, out _)).ToList();
            if (spots.Count == 0)
                return false;
            // 나에게서 먼 쪽 벽부터(바로 옆에 앉으면 첫 파동을 못 피한다)
            var far = spots.OrderByDescending(DistanceToPlayer).Take(Mathf.Max(1, spots.Count / 3)).ToList();
            var spot = far[Rng.Next(far.Count)];

            var go = new GameObject("SlimeTurret");
            go.transform.SetParent(transform, false);
            var turret = go.AddComponent<EnemyActor>();
            turret.Initialize(boss.Stats.MaxHealth, boss.Stats.AttackPower * SlimeTurretDamageRate, isBoss: false, kind: EnemyKind.Melee);
            turret.MarkAsMinion();          // 보상 없음, 킹이 죽으면 같이 사라짐
            turret.MarkAsSlimeTurret(boss); // 칼이 안 통함 + 킹 그림(2x2 크기)
            SeatSlimeTurret(turret, spot);  // 벽 위 - 지도 칸은 차지하지 않는다
            turret.SyncTransform();
            _enemies.Add(turret);
            _slimeTurret = turret;
            _turretPending = null;
            _turretCooldown = 1;
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
            if (_turretCooldown > 0)
                _turretCooldown--;
            FollowAlongWall(turret, player.GridPos);
            RingSpot(_turretRing, out var a, out var n);
            var along = AlongOf(a, n, player.GridPos);
            if (_turretCooldown > 0 || along < -1 || along > 2)
                return; // 아직 쉬는 중이거나 줄이 멀다 - 계속 따라간다
            if (player.RootedTurns > 0)
                return; // 점액에 묶여 다음 턴에 못 움직이면 예고해도 못 피한다 - 풀릴 때까지 미룬다
            _turretPending = SlimeWaveTiles(a, n);
            ShowEnemyTelegraph(turret, _turretPending, SlimeTelegraphColor, "tel_slime"); // 흘러내리는 점액 예고(10-23)
        }

        /// <summary>둘레 벽을 타고(모퉁이를 돌아) 내 줄과 맞는 가장 가까운 자리로 - 한 턴 한 칸, 멀면 두 칸.</summary>
        private void FollowAlongWall(EnemyActor turret, Vector2Int target)
        {
            int? best = null;
            for (var i = 0; i < _wallRing.Count; i++)
            {
                if (!RingSpot(i, out var a, out var n))
                    continue;
                var along = AlongOf(a, n, target);
                if (along < 0 || along > 1)
                    continue;
                if (best == null || RingDistance(_turretRing, i) < RingDistance(_turretRing, best.Value))
                    best = i;
            }
            if (best == null || best.Value == _turretRing)
                return;
            var forward = ((best.Value - _turretRing) % _wallRing.Count + _wallRing.Count) % _wallRing.Count;
            var dir = forward <= _wallRing.Count - forward ? 1 : -1;
            var steps = RingDistance(_turretRing, best.Value) > SlimeTurretFarSteps ? 2 : 1;
            int Wrap(int i) => ((i % _wallRing.Count) + _wallRing.Count) % _wallRing.Count;
            var i2 = _turretRing;
            for (var k = 0; k < steps && i2 != best.Value; k++)
            {
                i2 = Wrap(i2 + dir);
                if (!RingSpot(i2, out _, out _) && i2 != best.Value)
                    i2 = Wrap(i2 + dir); // 모퉁이는 건너뛴다(두 변에 걸쳐 앉지 않게)
            }
            SeatSlimeTurret(turret, i2);
        }

        /// <summary>포탑 파동 - 벽 위 두 칸에서 방 안쪽으로 곧게(두 줄 폭) + 양 끝에서 바깥쪽 대각선 2줄, 벽이나 방 끝까지.</summary>
        private List<Vector2Int> SlimeWaveTiles(Vector2Int a, Vector2Int n)
        {
            var t = TangentOf(n);
            var rays = new List<(Vector2Int From, Vector2Int D)> { (a, n), (a + t, n), (a, n - t), (a + t, n + t) };
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
            UpdateGimmickLabel();
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
            _wallRing.Clear();
        }
    }
}
