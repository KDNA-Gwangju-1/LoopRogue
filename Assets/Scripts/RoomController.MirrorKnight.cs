using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>10층 거울 가면 기사 기믹 4단계(10-16 사용자) - 체력 75/50/25/0%(BossBrain.MirrorPhaseHealth)에서 하나씩, 못 막으면 즉사.
    /// 1 거울상(75%): 좌우가 뒤집힌 가짜 3개 - 진짜의 예고 공격에 맞혀 깬다(RoomController.BossGimmicks).
    /// 2 내 거울상(50%): 방 가운데 세로줄을 기준으로 좌우가 뒤집힌 내가 따라 움직인다 - 기사의 예고 공격에 ReflectionHits번 맞혀 깬다.
    /// 3 빛(25%): 의문의 조력자가 빛을 쏘고, 기사는 무적 - 거울에 붙어 밀면 /·\가 바뀐다. 빛이 거울 LightMirrorCount개를 모두 거쳐 기사에게 닿으면 무적이 풀린다.
    /// 4 발악(0%): 체력이 0이 돼도 쓰러지지 않고, 사람은 누르는 즉시 움직이되 LastStandTurnSeconds초 동안 가만히 있으면 턴이 저절로 흐른다
    ///   (봇은 턴 그대로) - 기사는 쉬지 않고 예고 공격만 하고 맞으면 즉사. 실제 LastStandSeconds초(봇은 턴당 LastStandTurnSeconds초로 셈)를
    ///   버티면 쓰러진다(10-23 사용자 - 빨리 움직여도 일찍 안 끝나게 턴 수 대신 실제 시간). 칼은 통하지 않는다.
    /// 다음 단계 체력보다 더 깎이지 않게 단계마다 보스 체력 하한(CharacterStats.HealthFloor)을 건다 - 기믹을 건너뛰고 잡을 수 없다.</summary>
    public partial class RoomController
    {
        public const int ReflectionHits = 3;
        public const int ReflectionTurnLimit = 60;
        public const int LightMirrorCount = 3;
        /// <summary>빛 단계 제한 턴 - 최소 LightTurnLimit, 거울이 멀리 흩어져 있으면 LightTurnBase + 거울을 다 돌고 오는 걸음 수 × LightTurnPerStep
        /// (10-16 사용자 "거울 위치가 넓으면 턴이 짧다").</summary>
        public const int LightTurnLimit = 80;
        public const int LightTurnBase = 50;
        public const int LightTurnPerStep = 2;
        public const float LastStandSeconds = 30f;
        public const float LastStandTurnSeconds = 0.6f;

        /// <summary>봇 통계용 - 10층 단계별 기믹 즉사 횟수(인덱스 = 단계 1~4).</summary>
        public static readonly int[] MirrorPhaseKills = new int[5];

        private int _mirrorPhase;            // 0 없음, 1~4 진행 중인(또는 마지막으로 시작한) 단계, 5 = 발악까지 끝남
        private int _reflectionHitCount;
        private GameObject _reflection;
        private Vector2Int _lightHelper;
        private readonly List<Vector2Int> _lightWalls = new List<Vector2Int>(); // 실제로 세운 조력자·거울 벽(치울 때)
        private Vector2Int _lightHelperDir;
        private readonly Dictionary<Vector2Int, bool> _lightMirrors = new Dictionary<Vector2Int, bool>();   // true = '/', false = '\'
        private readonly Dictionary<Vector2Int, bool> _lightSolution = new Dictionary<Vector2Int, bool>();
        private readonly List<Vector2Int> _lightFinalLine = new List<Vector2Int>();
        private Vector2Int _lightFinalDir;
        private bool _lightActive;
        private readonly List<GameObject> _beamObjects = new List<GameObject>();
        private float _lastStandSeconds;   // 발악 남은 시간(초)
        private bool _lastStandRunning;

        /// <summary>지금 10층 기믹 단계(1~4) - 다른 층이거나 없으면 0.</summary>
        public int MirrorPhase => _gimmickStage == BossBrain.MirrorStage && _mirrorPhase <= 4 ? _mirrorPhase : 0;

        /// <summary>10층 기믹 단계가 아직 안 끝났는지 - 끝나야 다음 단계가 시작된다(BossBrain.TryStartGimmick).</summary>
        public bool MirrorPhaseActive => MirrorPhase switch
        {
            1 => _mirrorFakes.Count > 0,
            2 => _reflection != null && _reflectionHitCount < ReflectionHits,
            3 => _lightActive,
            4 => _lastStandRunning,
            _ => false,
        };

        /// <summary>발악 중 - 보스는 예고 공격만 하고(BossBrain), 맞으면 즉사, 사람은 시간이 턴을 넘긴다(PlayerActor).</summary>
        public bool MirrorLastStand => MirrorPhase == 4 && _lastStandRunning;

        /// <summary>2단계 - 거울 속 나의 칸(방 가운데 세로줄 기준 좌우 반전). 단계가 아니면 null.</summary>
        public Vector2Int? ReflectionPos => MirrorPhase == 2 && _reflection != null ? MirrorOf(_player.GridPos) : (Vector2Int?)null;
        private Vector2Int MirrorOf(Vector2Int p) => new Vector2Int(Map.Width - 1 - p.x, p.y);

        public bool IsLightMirror(Vector2Int p) => _lightActive && _lightMirrors.ContainsKey(p);
        public IReadOnlyDictionary<Vector2Int, bool> LightMirrors => _lightMirrors;
        public IReadOnlyDictionary<Vector2Int, bool> LightSolution => _lightSolution;
        /// <summary>정답대로 돌렸을 때 마지막 거울에서 벽까지 빛이 지나는 칸 - 봇이 기사를 이 줄로 끌어들인다.</summary>
        public IReadOnlyList<Vector2Int> LightFinalLine => _lightFinalLine;
        public Vector2Int LightFinalDir => _lightFinalDir;

        // ------------------------------------------------------------------ 시작
        public void BeginMirrorPhase(int phase, EnemyActor boss)
        {
            if (phase == 1)
            {
                BeginStageGimmick(BossBrain.MirrorStage, boss); // 거울상 - 예전 그대로
                _mirrorPhase = 1;
                SetMirrorFloor(boss, running: true);
                return;
            }
            _gimmickStage = BossBrain.MirrorStage;
            _gimmickBoss = boss;
            _gimmickJustStarted = true;
            _mirrorPhase = phase;
            SetMirrorFloor(boss, running: true);
            var c = new GimmickCutscene();
            switch (phase)
            {
                case 2:
                    _gimmickTurnsLeft = ReflectionTurnLimit;
                    _reflectionHitCount = 0;
                    c.Steps.Add(() => SpawnReflection());
                    break;
                case 3:
                    if (!BuildLightPuzzle(boss, awayFromWalls: true) && !BuildLightPuzzle(boss, awayFromWalls: false)) // 벽에 안 붙는 배치가 없을 때만 붙어도
                    {
                        // 방이 좁아 빛 길을 못 만들었다 - 이 단계는 건너뛴다(무적 없음)
                        SetMirrorFloor(boss, running: false);
                        return;
                    }
                    _lightActive = true;
                    _gimmickTurnsLeft = Mathf.Max(LightTurnLimit, LightTurnBase + LightTurnPerStep * LightTourSteps());
                    var helper = _lightHelper;
                    c.Steps.Add(WallStep(() => { SpawnLightHelperProp(helper); return true; }, "candle_light"));
                    foreach (var m in _lightMirrors.Keys.ToList())
                    {
                        var at = m;
                        c.Steps.Add(WallStep(() => { SpawnLightMirrorProp(at); return true; }, "morph"));
                    }
                    break;
                case 4:
                    _lastStandSeconds = LastStandSeconds;
                    _lastStandRunning = true;
                    ClearLightPuzzle();
                    foreach (var m in _enemies.Where(e => e != null && e.IsMinion && !e.Stats.IsDead).ToList())
                        DespawnEnemy(m, new Color(0.8f, 0.88f, 1f));
                    boss.ClearStun();
                    break;
            }
            c.Finish = () =>
            {
                RefreshLightBeam();
                UpdateGimmickLabel();
            };
            UpdateGimmickLabel();
            PlayGimmickCutscene(c);
        }

        /// <summary>보스 체력 하한 - 1~3단계가 진행 중이면 다음 단계 체력까지만, 아니면 1(발악 전엔 안 죽는다), 발악이 끝나면 0.</summary>
        private void SetMirrorFloor(EnemyActor boss, bool running)
        {
            if (boss == null)
                return;
            var phase = _mirrorPhase;
            if (phase >= 5)
                boss.Stats.HealthFloor = 0f;
            else if (running && phase >= 1 && phase <= 3)
                boss.Stats.HealthFloor = Mathf.Max(1f, boss.Stats.MaxHealth * BossBrain.MirrorPhaseHealth[phase]);
            else
                boss.Stats.HealthFloor = 1f;
        }

        // ------------------------------------------------------------------ 2단계 내 거울상
        private bool SpawnReflection()
        {
            if (_reflection != null)
                return false;
            _reflection = new GameObject("PlayerReflection");
            _reflection.transform.SetParent(transform, false);
            _roomObjects.Add(_reflection);
            if (!DamagePopup.Suppressed)
            {
                var r = _reflection.AddComponent<SpriteRenderer>();
                _reflection.AddComponent<PlayerReflection>().Setup(_player, r, (Map.Width - 1) * GridConstants.CellSize, this);
                HitFeedback.OnSummon(new Vector3(MirrorOf(_player.GridPos).x * GridConstants.CellSize, _player.GridPos.y * GridConstants.CellSize, 0f));
            }
            return true;
        }

        /// <summary>예고 공격이 거울 속 나를 덮었다 - 금이 하나. 다 깨면 기사가 비틀거린다.</summary>
        private void HitReflection(HashSet<Vector2Int> tiles)
        {
            var pos = ReflectionPos;
            if (!pos.HasValue || Map.IsWall(pos.Value) || !tiles.Contains(pos.Value))
                return;
            _reflectionHitCount++;
            var last = _reflectionHitCount >= ReflectionHits;
            if (HitFeedback.Enabled)
            {
                // 금이 갈 때는 조각 몇 개만 떨어져 나가고, 마지막엔 거울 속 내가 그 모습 그대로 산산조각(10-19 사용자)
                if (_reflection.TryGetComponent<SpriteRenderer>(out var reflRenderer) && reflRenderer.sprite != null)
                    SpriteShatter.Spawn(reflRenderer, pieceCells: 0.14f, fraction: last ? 1f : 0.15f, force: last ? 4.5f : 2.2f, silver: 0.25f);
                else
                    Fx.Play("crystal_shatter", _reflection.transform.position, 1.6f, fps: 14f, sortingOrder: 6);
                SfxPlayer.Play(Sfx.Crit);
            }
            if (!last)
            {
                UpdateGimmickLabel();
                return;
            }
            Destroy(_reflection);
            _reflection = null;
            OnGimmickBroken();
            UpdateGimmickLabel();
        }

        /// <summary>플레이어 그림을 좌우 뒤집어 방 반대편에 그린다(은빛 반투명). 거울 칸이 벽이면 더 흐리게.</summary>
        private sealed class PlayerReflection : MonoBehaviour
        {
            private PlayerActor _player;
            private SpriteRenderer _renderer;
            private SpriteRenderer _source;
            private float _mirrorX;
            private RoomController _room;

            public void Setup(PlayerActor player, SpriteRenderer renderer, float mirrorX, RoomController room)
            {
                _player = player;
                _renderer = renderer;
                _source = player.GetComponent<SpriteRenderer>();
                _mirrorX = mirrorX;
                _room = room;
                _renderer.sortingOrder = 1;
            }

            private void LateUpdate()
            {
                if (_player == null || _renderer == null)
                    return;
                if (_source != null)
                {
                    _renderer.sprite = _source.sprite;
                    _renderer.flipX = !_source.flipX;
                    transform.localScale = _player.transform.localScale;
                }
                var p = _player.transform.position;
                transform.position = new Vector3(_mirrorX - p.x, p.y, p.z);
                var cell = _room.MirrorOf(_player.GridPos);
                _renderer.color = new Color(0.78f, 0.86f, 1f, _room.Map.IsWall(cell) ? 0.25f : 0.7f);
            }
        }

        // ------------------------------------------------------------------ 3단계 빛
        private static Vector2Int Reflect(Vector2Int d, bool slash) => slash ? new Vector2Int(d.y, d.x) : new Vector2Int(-d.y, -d.x);

        /// <summary>조력자 자리·방향과 거울 LightMirrorCount개를 정한다 - 정답대로 돌리면 빛이 거울을 차례로 거쳐 마지막에 4칸 이상 곧게 뻗는다.
        /// 거울은 처음엔 적어도 2개가 틀린 방향. 못 만들면 false.
        /// awayFromWalls(10-28 사용자) - 거울 둘레 8칸에 벽·방 끝이 없게(벽에 딱 붙은 거울은 그쪽에서 밀 수가 없고 보기에도 답답하다).</summary>
        private bool BuildLightPuzzle(EnemyActor boss, bool awayFromWalls)
        {
            ClearLightPuzzle();
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            var starts = new List<(Vector2Int Cell, Vector2Int Dir)>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (!Map.IsWalkable(p) || p == ExitPosition || GridMap.FootprintChebyshev(boss, p) < 3
                    || Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) < 2)
                    continue;
                foreach (var d in dirs)
                    if (!Map.IsInBounds(p - d) || Map.IsWall(p - d))
                        starts.Add((p, d));
            }
            if (starts.Count == 0)
                return false;

            // 10-16 사용자 "거울이 넓게 퍼져서 나오면" - 맞는 배치를 여럿 모아서 조력자·거울끼리 가장 멀리 떨어진(가장 가까운 쌍의 거리가 큰) 걸 고른다.
            (Vector2Int Helper, List<(Vector2Int Cell, bool Slash)> Mirrors, List<Vector2Int> Final, Vector2Int Dir)? best = null;
            var bestSpread = -1;
            var found = 0;
            for (var attempt = 0; attempt < 1500 && found < LightLayoutCandidates; attempt++)
            {
                var (helper, dir) = starts[Rng.Next(starts.Count)];
                var used = new HashSet<Vector2Int> { helper };
                var lit = new HashSet<Vector2Int>();
                var mirrors = new List<(Vector2Int Cell, bool Slash)>();
                var cur = helper;
                var ok = true;
                for (var m = 0; m < LightMirrorCount && ok; m++)
                {
                    var len = Rng.Next(4, 14);
                    var seg = new List<Vector2Int>();
                    for (var k = 0; k < len; k++)
                    {
                        var n = cur + dir;
                        if (!Map.IsInBounds(n) || Map.IsWall(n) || used.Contains(n))
                            break;
                        seg.Add(n);
                        cur = n;
                    }
                    if (seg.Count < 3)
                    {
                        ok = false;
                        break;
                    }
                    var mirror = seg[seg.Count - 1];
                    if (!Map.IsWalkable(mirror) || mirror == _player.GridPos || mirror == ExitPosition || lit.Contains(mirror)
                        || GridMap.FootprintChebyshev(boss, mirror) < 2 || (awayFromWalls && TouchesWall(mirror)))
                    {
                        ok = false;
                        break;
                    }
                    foreach (var s in seg)
                        lit.Add(s);
                    used.Add(mirror);
                    var turn = Rng.Next(2) == 0 ? new Vector2Int(dir.y, dir.x) : new Vector2Int(-dir.y, -dir.x);
                    var slash = turn == new Vector2Int(dir.y, dir.x);
                    mirrors.Add((mirror, slash));
                    dir = turn;
                }
                if (!ok)
                    continue;

                var final = new List<Vector2Int>();
                for (var n = cur + dir; Map.IsInBounds(n) && !Map.IsWall(n); n += dir)
                {
                    if (used.Contains(n))
                    {
                        ok = false;
                        break;
                    }
                    final.Add(n);
                }
                if (!ok || final.Count < 4)
                    continue;

                // 벽(조력자·거울)을 세워도 방이 끊기지 않는지 하나씩 확인 - 안 되면 되돌리고 다시
                var placed = new List<Vector2Int>();
                foreach (var w in new[] { helper }.Concat(mirrors.Select(mm => mm.Cell)))
                {
                    if (!WallKeepsConnected(w))
                    {
                        ok = false;
                        break;
                    }
                    Map.AddWall(w);
                    placed.Add(w);
                }
                foreach (var w in placed)
                    Map.RemoveWall(w);
                if (!ok)
                    continue;

                found++;
                var points = mirrors.Select(mm => mm.Cell).Append(helper).ToList();
                var spread = int.MaxValue;
                for (var a = 0; a < points.Count; a++)
                for (var b = a + 1; b < points.Count; b++)
                    spread = Mathf.Min(spread, Mathf.Max(Mathf.Abs(points[a].x - points[b].x), Mathf.Abs(points[a].y - points[b].y)));
                spread = spread * 100 + final.Count; // 같으면 마지막 빛줄이 긴 쪽(기사를 끌어들이기 쉽게)
                if (spread > bestSpread)
                {
                    bestSpread = spread;
                    best = (helper, mirrors, final, dir);
                }
            }
            if (!best.HasValue)
                return false;
            {
                var (helper, mirrors, final, dir) = best.Value;
                _lightHelper = helper;
                _lightHelperDir = mirrors.Count > 0 ? StartDir(helper, mirrors[0].Cell) : dir;
                foreach (var (cell, slash) in mirrors)
                {
                    _lightSolution[cell] = slash;
                    _lightMirrors[cell] = slash;
                }
                // 처음엔 적어도 2개를 틀리게
                foreach (var cell in mirrors.Select(mm => mm.Cell))
                    if (Rng.Next(2) == 0)
                        _lightMirrors[cell] = !_lightMirrors[cell];
                while (_lightMirrors.Count(kv => kv.Value != _lightSolution[kv.Key]) < Mathf.Min(2, mirrors.Count))
                {
                    var right = _lightMirrors.Keys.Where(k => _lightMirrors[k] == _lightSolution[k]).ToList();
                    var flip = right[Rng.Next(right.Count)];
                    _lightMirrors[flip] = !_lightMirrors[flip];
                }
                _lightFinalLine.AddRange(final);
                _lightFinalDir = dir;
                return true;
            }
        }

        private const int LightLayoutCandidates = 40;

        /// <summary>플레이어 자리에서 거울마다 옆 칸을 한 번씩 들르는 가장 짧은 걸음 수(순서는 전부 따져 봄). 벽·조력자·거울은 못 지나가고 몹은 무시.</summary>
        private int LightTourSteps()
        {
            var blocked = new HashSet<Vector2Int>(_lightMirrors.Keys) { _lightHelper };
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            Dictionary<Vector2Int, int> Bfs(IEnumerable<Vector2Int> from)
            {
                var dist = new Dictionary<Vector2Int, int>();
                var q = new Queue<Vector2Int>();
                foreach (var f in from)
                    if (dist.TryAdd(f, 0))
                        q.Enqueue(f);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    foreach (var d in dirs)
                    {
                        var n = cur + d;
                        if (!Map.IsInBounds(n) || Map.IsWall(n) || blocked.Contains(n) || dist.ContainsKey(n))
                            continue;
                        dist[n] = dist[cur] + 1;
                        q.Enqueue(n);
                    }
                }
                return dist;
            }
            List<Vector2Int> Beside(Vector2Int m) =>
                dirs.Select(d => m + d).Where(c => Map.IsInBounds(c) && !Map.IsWall(c) && !blocked.Contains(c)).ToList();
            int Steps(Dictionary<Vector2Int, int> dist, Vector2Int m)
            {
                var best = 999;
                foreach (var c in Beside(m))
                    if (dist.TryGetValue(c, out var s) && s < best)
                        best = s;
                return best;
            }
            var mirrors = _lightMirrors.Keys.ToList();
            var fromPlayer = Bfs(new[] { _player.GridPos });
            var fromMirror = mirrors.ToDictionary(m => m, m => Bfs(Beside(m)));
            var bestTour = 999;
            foreach (var order in Permutations(mirrors))
            {
                var total = Steps(fromPlayer, order[0]);
                for (var i = 1; i < order.Count; i++)
                    total += Steps(fromMirror[order[i - 1]], order[i]);
                bestTour = Mathf.Min(bestTour, total);
            }
            return bestTour >= 999 ? 0 : bestTour;
        }

        private static IEnumerable<List<Vector2Int>> Permutations(List<Vector2Int> items)
        {
            if (items.Count <= 1)
            {
                yield return new List<Vector2Int>(items);
                yield break;
            }
            for (var i = 0; i < items.Count; i++)
            {
                var rest = new List<Vector2Int>(items);
                rest.RemoveAt(i);
                foreach (var tail in Permutations(rest))
                {
                    tail.Insert(0, items[i]);
                    yield return tail;
                }
            }
        }

        private static Vector2Int StartDir(Vector2Int from, Vector2Int to) =>
            new Vector2Int(System.Math.Sign(to.x - from.x), System.Math.Sign(to.y - from.y));

        private void SpawnLightHelperProp(Vector2Int p)
        {
            AddWallProp(p, "Light_Helper", new Color(1f, 0.93f, 0.6f), "LightHelper");
            _lightWalls.Add(p);
        }

        private void SpawnLightMirrorProp(Vector2Int p)
        {
            AddWallProp(p, "Light_Mirror", new Color(0.55f, 0.6f, 0.7f), "LightMirror");
            _lightWalls.Add(p);
            if (!_wallObjects.TryGetValue(p, out var go) || go == null)
                return;
            // 거울 면 - 그림(Props/Light_Mirror_Face, "/" 방향, 10-23)이 있으면 받침대 위에 얹어 "\"는 좌우 반전, 없으면 얇은 막대를 /·\ 방향으로 눕힌다
            var bar = new GameObject("MirrorFace");
            bar.transform.SetParent(go.transform, false);
            var faceSprite = Resources.Load<Sprite>("Sprites/Props/Light_Mirror_Face");
            if (faceSprite != null)
                VisualUtil.CreateSpriteVisual(bar, faceSprite, 1f, sortingOrder: -5);
            else
            {
                VisualUtil.CreateSquareVisual(bar, new Color(0.85f, 0.95f, 1f), 1f, sortingOrder: -5);
                bar.transform.localScale = new Vector3(1.25f, 0.18f, 1f);
            }
            UpdateMirrorFace(p);
        }

        private void UpdateMirrorFace(Vector2Int p)
        {
            if (!_wallObjects.TryGetValue(p, out var go) || go == null)
                return;
            var face = go.transform.Find("MirrorFace");
            if (face == null)
                return;
            if (face.TryGetComponent<SpriteRenderer>(out var r) && r.sprite != null)
                r.flipX = !_lightMirrors[p];
            else
                face.localRotation = Quaternion.Euler(0f, 0f, _lightMirrors[p] ? 45f : -45f);
        }

        /// <summary>거울에 붙어 그쪽으로 움직였다 - /와 \가 바뀐다(한 턴).</summary>
        public void RotateLightMirror(Vector2Int p)
        {
            if (!IsLightMirror(p))
                return;
            _lightMirrors[p] = !_lightMirrors[p];
            UpdateMirrorFace(p);
            if (HitFeedback.Enabled)
                SfxPlayer.Play(Sfx.ChestOpen);
            RefreshLightBeam();
        }

        /// <summary>빛을 따라간다 - 지나간 칸(들어온 방향·나간 방향, 거울 칸만 둘이 다름), 거친 거울 수, 기사에게 닿았는지.</summary>
        private List<(Vector2Int Cell, Vector2Int In, Vector2Int Out)> TraceLight(out int mirrorsHit, out bool hitBoss)
        {
            var cells = new List<(Vector2Int, Vector2Int, Vector2Int)>();
            var hitMirrors = new HashSet<Vector2Int>();
            hitBoss = false;
            var cur = _lightHelper;
            var dir = _lightHelperDir;
            for (var guard = 0; guard < 400; guard++)
            {
                cur += dir;
                if (!Map.IsInBounds(cur))
                    break;
                if (_lightMirrors.TryGetValue(cur, out var slash))
                {
                    hitMirrors.Add(cur);
                    var turned = Reflect(dir, slash);
                    cells.Add((cur, dir, turned));
                    dir = turned;
                    continue;
                }
                if (Map.IsWall(cur))
                    break;
                if (_gimmickBoss != null && !_gimmickBoss.Stats.IsDead && GridMap.Occupies(_gimmickBoss, cur))
                {
                    hitBoss = true;
                    break;
                }
                cells.Add((cur, dir, dir));
            }
            mirrorsHit = hitMirrors.Count;
            return cells;
        }

        /// <summary>빛줄기 그림(Props/Light_Beam_{0,1} 한 칸·Light_Beam_Half_{0,1} 가운데→오른쪽 반 칸, 10-16 사용자 OK) - 없으면 예전 노란 점.</summary>
        private static Sprite[] _beamFull, _beamHalf;
        private static float DirAngle(Vector2Int d) => d == Vector2Int.up ? 90f : d == Vector2Int.left ? 180f : d == Vector2Int.down ? 270f : 0f;

        /// <summary>빛줄기 그림을 다시 그리고, 거울을 다 거쳐 기사에게 닿았으면 무적을 푼다. 풀었으면 true.</summary>
        private bool RefreshLightBeam()
        {
            foreach (var go in _beamObjects)
                if (go != null)
                    Destroy(go);
            _beamObjects.Clear();
            if (!_lightActive)
                return false;
            var cells = TraceLight(out var mirrorsHit, out var hitBoss);
            if (hitBoss && mirrorsHit >= _lightMirrors.Count)
            {
                SolveLight();
                return true;
            }
            if (DamagePopup.Suppressed)
                return false;

            if (_beamFull == null)
            {
                _beamFull = new[] { Resources.Load<Sprite>("Sprites/Props/Light_Beam_0"), Resources.Load<Sprite>("Sprites/Props/Light_Beam_1") };
                _beamHalf = new[] { Resources.Load<Sprite>("Sprites/Props/Light_Beam_Half_0"), Resources.Load<Sprite>("Sprites/Props/Light_Beam_Half_1") };
            }
            var root = new GameObject("LightBeam");
            root.transform.SetParent(transform, false);
            _beamObjects.Add(root);
            _roomObjects.Add(root);
            if (_beamFull[0] == null || _beamHalf[0] == null)
            {
                // 그림이 없으면 예전 점
                var dot = hitBoss ? new Color(1f, 0.6f, 0.4f, 0.55f) : new Color(1f, 0.95f, 0.55f, 0.45f);
                foreach (var (c, _, _) in cells.Where(t => !_lightMirrors.ContainsKey(t.Cell)))
                {
                    var go = new GameObject("BeamDot");
                    go.transform.SetParent(root.transform, false);
                    VisualUtil.CreateSquareVisual(go, dot, GridConstants.CellSize * 0.32f, sortingOrder: -5);
                    go.transform.position = new Vector3(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize, 0f);
                }
                return false;
            }

            // 거울을 다 거치기 전에 기사에게 막혔으면 주황빛
            var tint = hitBoss ? new Color(1f, 0.65f, 0.45f, 1f) : Color.white;
            var anim = root.AddComponent<LightBeamAnim>();
            void Piece(Vector2Int c, Vector2Int toward, bool half)
            {
                var go = new GameObject(half ? "BeamHalf" : "Beam");
                go.transform.SetParent(root.transform, false);
                var r = VisualUtil.CreateSpriteVisual(go, half ? _beamHalf[0] : _beamFull[0], GridConstants.CellSize, sortingOrder: -6);
                r.color = tint;
                go.transform.position = new Vector3(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, DirAngle(toward));
                anim.Add(r, half ? _beamHalf : _beamFull);
            }
            Piece(_lightHelper, _lightHelperDir, half: true); // 조력자 - 가운데에서 출발
            foreach (var (c, inDir, outDir) in cells)
            {
                if (inDir == outDir)
                    Piece(c, outDir, half: false);
                else
                {
                    Piece(c, -inDir, half: true); // 들어온 쪽 반
                    Piece(c, outDir, half: true); // 나가는 쪽 반
                }
            }
            return false;
        }

        /// <summary>빛줄기 2프레임을 번갈아 - 반짝이가 흐르게.</summary>
        private sealed class LightBeamAnim : MonoBehaviour
        {
            private const float FrameSeconds = 0.14f;
            private readonly List<(SpriteRenderer R, Sprite[] Frames)> _parts = new List<(SpriteRenderer, Sprite[])>();
            private int _frame;
            private float _t;

            public void Add(SpriteRenderer r, Sprite[] frames) => _parts.Add((r, frames));

            private void Update()
            {
                _t += Time.deltaTime;
                if (_t < FrameSeconds)
                    return;
                _t = 0f;
                _frame ^= 1;
                foreach (var (r, frames) in _parts)
                    if (r != null && frames[_frame] != null)
                        r.sprite = frames[_frame];
            }
        }

        private void SolveLight()
        {
            if (HitFeedback.Enabled && _gimmickBoss != null)
            {
                ActorJuice.Get(_gimmickBoss).Flash(new Color(1f, 0.95f, 0.6f));
                ScreenFlash.Play(new Color(1f, 0.95f, 0.7f), 0.5f, 0.6f);
            }
            ClearLightPuzzle();
            OnGimmickBroken();
            UpdateGimmickLabel();
        }

        private void ClearLightPuzzle()
        {
            _lightActive = false;
            foreach (var go in _beamObjects)
                if (go != null)
                    Destroy(go);
            _beamObjects.Clear();
            foreach (var p in _lightWalls)
                if (_wallObjects.ContainsKey(p))
                    RemoveWallObject(p);
            _lightWalls.Clear();
            _lightMirrors.Clear();
            _lightSolution.Clear();
            _lightFinalLine.Clear();
        }

        // ------------------------------------------------------------------ 4단계 발악
        /// <summary>발악 중 예고 공격에 맞았다 - 즉사(사망 처리는 몹 턴 루프가).</summary>
        public void LastStandHit()
        {
            GimmickKills[BossBrain.MirrorStage]++;
            MirrorPhaseKills[4]++;
            DeathCause.Set("g10_laststand"); // 사망 대사
            ShowMessage(Loc.T("<color=#FF4040>기사의 마지막 일격에 쓰러졌다!</color>"));
            GimmickDeathFx();
            KillPlayerInstantly();
        }

        /// <summary>발악을 버텼다 - 기사가 쓰러진다(처치 처리까지). 방이 넘어갔으면 true.</summary>
        private bool FinishLastStand()
        {
            var boss = _gimmickBoss;
            _mirrorPhase = 5;
            _lastStandRunning = false;
            _lastStandSeconds = 0f;
            ClearTelegraph();
            if (boss == null || boss.Stats.IsDead)
                return false;
            boss.CancelBossPattern(this);
            SetMirrorFloor(boss, running: false);
            ShowMessage(Loc.T("<color=#FFD24A>거울 가면이 산산조각 났다!</color> 거울 가면 기사가 쓰러진다"));
            if (HitFeedback.Enabled)
            {
                // 기사가 그 모습 그대로 산산조각(10-19) - 그림이 없으면 예전 수정 깨짐
                if (boss.TryGetComponent<SpriteRenderer>(out var bossRenderer) && bossRenderer.sprite != null)
                {
                    SpriteShatter.Spawn(bossRenderer, pieceCells: 0.32f, force: 6f, silver: 0.3f);
                    bossRenderer.enabled = false;
                }
                else
                    Fx.Play("crystal_shatter", boss.transform.position, 3.2f, fps: 14f, sortingOrder: 6);
                CameraShake.Shake(0.5f, 0.9f);
            }
            boss.Stats.TakeDamage(boss.Stats.CurrentHealth + 1f);
            return _player.ClaimKill(boss);
        }

        /// <summary>발악 시계 - 사람이 조작할 수 있는 동안 PlayerActor가 매 프레임 부른다(일시정지·창이 열린 동안은 멈춤).
        /// 시간이 다 됐으면 true - 그 자리에서 한 턴을 넘겨 몹 턴 끝(TickMirrorPhase)에 기사가 쓰러지게.</summary>
        public bool TickLastStandClock(float deltaTime)
        {
            if (!MirrorLastStand)
                return false;
            var before = Mathf.CeilToInt(_lastStandSeconds * 10f);
            _lastStandSeconds -= deltaTime;
            if (Mathf.CeilToInt(_lastStandSeconds * 10f) != before)
                UpdateGimmickLabel(); // 0.1초마다 남은 시간 표시
            return _lastStandSeconds <= 0f;
        }

        // ------------------------------------------------------------------ 몹 턴 끝
        /// <summary>10층 단계별 - 제한 턴·빛 확인·발악 시간. 즉사면 true(사망 처리는 TickStageGimmick이), 발악을 버텨 방이 넘어갔으면 finished.</summary>
        private bool TickMirrorPhase(out bool finished)
        {
            finished = false;
            _mirrorFakes.RemoveAll(f => f == null || f.Stats.IsDead);
            if (_gimmickCutscenePlaying)
                return false; // 단계를 막 시작한 몹 턴 - 거울상·거울 속 나가 컷신에서 나오기 전이라 끝난 걸로 보면 체력 하한이 풀린다
            if (!MirrorPhaseActive)
            {
                SetMirrorFloor(_gimmickBoss, running: false);
                return false;
            }
            if (MirrorPhase == 3 && RefreshLightBeam())
                return false;
            if (MirrorPhase == 4)
            {
                if (GameHUD.AutoPlayActive)
                    _lastStandSeconds -= LastStandTurnSeconds; // 봇은 실제 시간이 아니라 턴으로 센다
                if (_lastStandSeconds <= 0f)
                {
                    FinishLastStand();
                    finished = true; // 처치 처리로 방이 넘어갔을 수 있다 - 이번 몹 턴은 여기서 끝
                    return false;
                }
                return false;
            }
            if (--_gimmickTurnsLeft <= 0)
            {
                ShowMessage(MirrorPhase switch
                {
                    1 => Loc.T("<color=#FF4040>거울 속 기사들이 한꺼번에 베어 들었다!</color>"),
                    2 => Loc.T("<color=#FF4040>거울 속 내가 거울 밖으로 걸어 나와 나를 베었다!</color>"),
                    _ => Loc.T("<color=#FF4040>빛이 꺼지고 거울 갑옷의 기사가 검을 내리쳤다!</color>"),
                });
                MirrorPhaseKills[MirrorPhase]++;
                return true;
            }
            return false;
        }

        private string MirrorLabelText(out bool danger)
        {
            danger = false;
            switch (MirrorPhase)
            {
                case 2 when MirrorPhaseActive:
                    danger = _gimmickTurnsLeft <= 20;
                    return Loc.F("거울 속 나 {0}/{1} · {2}턴", _reflectionHitCount, ReflectionHits, _gimmickTurnsLeft);
                case 3 when MirrorPhaseActive:
                    danger = _gimmickTurnsLeft <= 20;
                    return Loc.F("단단함 · 빛 {0}턴", _gimmickTurnsLeft);
                case 4 when MirrorPhaseActive:
                    danger = true;
                    return Loc.F("발악 {0:0.0}초", Mathf.Max(0f, _lastStandSeconds));
            }
            return null;
        }

        private void ClearMirrorPhases()
        {
            if (_reflection != null)
                Destroy(_reflection);
            _reflection = null;
            _reflectionHitCount = 0;
            ClearLightPuzzle();
            _lastStandRunning = false;
            _lastStandSeconds = 0f;
            _mirrorPhase = 0;
        }
    }
}
