using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>얼음 미끄럼 - 얼음 위에선 벽/바위/방 끝에 닿을 때까지 미끄러진다. 보석을 전부 모은 뒤(미끄러지며 지나가기만 해도 줍는다)
    /// 깃발 칸에 딱 멈추면 성공, 이동 횟수를 다 쓰면 실패. 3층부터 진흙 칸이 생긴다(밟으면 그 자리에 멈춘다).
    /// 방은 무작위로 뽑고, (칸, 주운 보석) 상태로 BFS를 돌려 최소 이동 수가 층에 맞게 충분한 배치만 쓴다(항상 풀린다).</summary>
    public class IceSlidePuzzle : PuzzleBase
    {
        public override string Title => Loc.T("얼음 미끄럼");
        public override string Rule => _mud.Count > 0
            ? Loc.T("벽·바위까지 미끄러진다(진흙에선 멈춤). 보석을 전부 모은 뒤 깃발 칸에 딱 멈춰라!")
            : Loc.T("벽·바위까지 미끄러진다. 보석을 전부 모은 뒤 깃발 칸에 딱 멈춰라!");
        public override string Status => Loc.F("보석 {0} / {1}   이동 {2} / {3}  (최소 {4}번)", _gems.Count - _gemObjects.Count, _gems.Count, _moves, _limit, _best);

        private int _size;
        private HashSet<Vector2Int> _rocks = new HashSet<Vector2Int>();
        private HashSet<Vector2Int> _mud = new HashSet<Vector2Int>();
        private List<Vector2Int> _gems = new List<Vector2Int>();
        private readonly Dictionary<Vector2Int, GameObject> _gemObjects = new Dictionary<Vector2Int, GameObject>();
        private Vector2Int _goal;
        private int _best, _limit, _moves;

        public override RoomLayout BuildLayout()
        {
            _size = Stage >= 5 ? 13 : 11;
            var wanted = Mathf.Min(4 + Stage / 3, 9);
            var gemCount = 1 + (Stage + 1) / 4; // 1~2층 1개, 3~6층 2개, 7층부터 3개
            var mudCount = Stage >= 3 ? 2 + Stage / 4 : 0;
            var bestStart = Vector2Int.zero;
            for (var attempt = 0; attempt < 200 && _best < wanted; attempt++)
            {
                var rocks = new HashSet<Vector2Int>();
                var count = Mathf.RoundToInt(_size * _size * 0.13f);
                while (rocks.Count < count)
                    rocks.Add(new Vector2Int(Rng.Next(_size), Rng.Next(_size)));
                var free = new List<Vector2Int>();
                for (var x = 0; x < _size; x++)
                for (var y = 0; y < _size; y++)
                    if (!rocks.Contains(new Vector2Int(x, y)))
                        free.Add(new Vector2Int(x, y));
                Shuffle(free);
                var mud = new HashSet<Vector2Int>(free.Take(mudCount));
                var start = free.First(c => !mud.Contains(c));

                // 미끄러져 지나가는 칸(보석 후보)과 멈추는 칸(깃발 후보)
                var stops = new HashSet<Vector2Int> { start };
                var touched = new HashSet<Vector2Int>();
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var d in Dirs)
                    {
                        var path = SlidePath(cur, d, rocks, mud);
                        if (path.Count == 0)
                            continue;
                        touched.UnionWith(path);
                        if (stops.Add(path[path.Count - 1]))
                            queue.Enqueue(path[path.Count - 1]);
                    }
                }
                touched.Remove(start);
                var gemPool = touched.ToList();
                var goalPool = stops.Where(s => s != start).ToList();
                if (gemPool.Count < gemCount || goalPool.Count == 0)
                    continue;

                for (var combo = 0; combo < 6; combo++)
                {
                    Shuffle(gemPool);
                    var gems = gemPool.Take(gemCount).ToList();
                    var goal = Pick(goalPool);
                    if (gems.Contains(goal))
                        continue;
                    var dist = SolveMoves(start, goal, gems, rocks, mud);
                    if (dist <= _best)
                        continue;
                    _best = dist;
                    _rocks = rocks;
                    _mud = mud;
                    _gems = gems;
                    _goal = goal;
                    bestStart = start;
                }
            }
            if (_best == 0)
            {
                // 안전망 - 바위 없는 방, 오른쪽 끝까지 한 번 미끄러지기
                _rocks = new HashSet<Vector2Int>();
                _mud = new HashSet<Vector2Int>();
                _gems = new List<Vector2Int>();
                bestStart = Vector2Int.zero;
                _goal = new Vector2Int(_size - 1, 0);
            }
            _best = Mathf.Max(1, _best);
            _limit = _best + 3 + _best / 2;
            return new RoomLayout { Width = _size, Height = _size, PlayerStart = bestStart, Walls = new HashSet<Vector2Int>(_rocks) };
        }

        /// <summary>p에서 d로 미끄러질 때 지나가는 칸들(마지막 = 멈추는 칸). 못 움직이면 빈 목록. 진흙 칸에 들어가면 거기서 멈춘다.</summary>
        private List<Vector2Int> SlidePath(Vector2Int p, Vector2Int d, HashSet<Vector2Int> rocks, HashSet<Vector2Int> mud)
        {
            var path = new List<Vector2Int>();
            while (true)
            {
                var next = p + d;
                if (!InBounds(next, _size, _size) || rocks.Contains(next))
                    return path;
                p = next;
                path.Add(p);
                if (mud.Contains(p))
                    return path;
            }
        }

        /// <summary>(칸, 주운 보석 비트) BFS - 보석을 다 줍고 깃발에 멈추는 최소 이동 수, 안 되면 0.</summary>
        private int SolveMoves(Vector2Int start, Vector2Int goal, List<Vector2Int> gems, HashSet<Vector2Int> rocks, HashSet<Vector2Int> mud)
        {
            var full = (1 << gems.Count) - 1;
            var seen = new HashSet<(Vector2Int, int)> { (start, 0) };
            var queue = new Queue<(Vector2Int Pos, int Mask, int Dist)>();
            queue.Enqueue((start, 0, 0));
            while (queue.Count > 0)
            {
                var (pos, mask, dist) = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var path = SlidePath(pos, d, rocks, mud);
                    if (path.Count == 0)
                        continue;
                    var m = mask;
                    foreach (var c in path)
                    {
                        var g = gems.IndexOf(c);
                        if (g >= 0)
                            m |= 1 << g;
                    }
                    var stop = path[path.Count - 1];
                    if (stop == goal && m == full)
                        return dist + 1;
                    if (seen.Add((stop, m)))
                        queue.Enqueue((stop, m, dist + 1));
                }
            }
            return 0;
        }

        protected override void OnBegin()
        {
            for (var x = 0; x < _size; x++)
            for (var y = 0; y < _size; y++)
            {
                var p = new Vector2Int(x, y);
                if (_mud.Contains(p))
                    ArtTile(p, "Mud", 1f, Color.white, new Color(0.42f, 0.3f, 0.18f, 0.95f), 0.96f, -9, "Mud");
                else if (!_rocks.Contains(p))
                    ArtTile(p, "Ice", 1f, Color.white, new Color(0.55f, 0.8f, 0.95f, 0.35f), 0.96f, -9, "Ice");
            }
            ArtTile(_goal, "GoalPad", 1f, Color.white, new Color(1f, 0.85f, 0.3f, 0.7f), 0.9f, -7, "Goal");
            var flag = FxSprite("event_ChallengeFlag");
            if (flag != null)
                SpriteAt(World(_goal), flag, 0.8f, -6, "GoalFlag");
            foreach (var gem in _gems)
            {
                var gemArt = PuzzleArt("Gem");
                SpriteRenderer r;
                if (gemArt != null)
                    r = SpriteAt(World(gem), gemArt, 0.75f, -6, "Gem");
                else
                {
                    r = Square(gem, new Color(0.5f, 1f, 0.95f), 0.36f, -6, "Gem");
                    r.transform.rotation = Quaternion.Euler(0f, 0f, 45f); // 하늘색 마름모 = 보석
                }
                _gemObjects[gem] = r.gameObject;
            }
        }

        public override bool HandleAct(Vector2Int direction)
        {
            var path = SlidePath(Player.GridPos, direction, _rocks, _mud);
            if (path.Count == 0)
                return true; // 그쪽으론 못 미끄러진다 - 턴 소모 없음
            var from = Player.transform.position;
            Map.MoveActor(Player, path[path.Count - 1]);
            _moves++;
            if (DamagePopup.Suppressed)
            {
                foreach (var c in path)
                    PickGem(c);
                FinishSlide();
                return true;
            }
            // 칸은 바로 옮기고, 그림만 지나가는 칸을 따라 미끄러지게 - 그동안 입력을 잠근다
            Room.IsInputLocked = true;
            Room.StartCoroutine(SlideRoutine(from, path));
            return true;
        }

        private const float SlideSecondsPerCell = 0.055f;

        /// <summary>순간이동 대신 얼음 위를 미끄러져 간다 - 끝에서 살짝 느려지고, 지나가는 칸의 보석은 닿는 순간 줍는다.
        /// 벽·바위에 닿아 멈추면 몸이 찌그러지고, 진흙에서 멈추면 그냥 선다.</summary>
        private IEnumerator SlideRoutine(Vector3 from, List<Vector2Int> path)
        {
            var player = Player;
            var to = World(path[path.Count - 1]);
            var duration = SlideSecondsPerCell * path.Count + 0.05f;
            var picked = 0;
            var lastDust = 0;
            for (var time = 0f; ; time += Time.deltaTime)
            {
                if (!IsRunning || player == null)
                    break;
                var t = Mathf.Clamp01(time / duration);
                var eased = 1f - (1f - t) * (1f - t) * 0.6f - (1f - t) * 0.4f; // 거의 일정하다가 끝에서 살짝 감속
                player.transform.position = Vector3.LerpUnclamped(from, to, eased);
                var travelled = Mathf.FloorToInt(eased * path.Count + 0.35f);
                while (picked < Mathf.Min(travelled, path.Count))
                    PickGem(path[picked++]);
                if (HitFeedback.Enabled && travelled > lastDust && travelled < path.Count)
                {
                    lastDust = travelled;
                    DeathBurst.Spawn(player.transform.position, new Color(0.8f, 0.95f, 1f, 0.8f), count: 3, speed: 1.5f, size: 0.08f); // 얼음 가루
                }
                if (t >= 1f)
                    break;
                yield return null;
            }
            if (!IsRunning || player == null)
                yield break;
            player.SyncTransform();
            while (picked < path.Count)
                PickGem(path[picked++]);
            var dest = path[path.Count - 1];
            if (HitFeedback.Enabled && !_mud.Contains(dest))
                ActorJuice.Get(player).Squish(0.2f); // 벽·바위에 쿵
            Room.IsInputLocked = false;
            FinishSlide();
        }

        private void PickGem(Vector2Int c)
        {
            if (!_gemObjects.TryGetValue(c, out var gem))
                return;
            Untrack(gem);
            _gemObjects.Remove(c);
            SfxPlayer.Play(Sfx.Appear);
            RefreshHud();
        }

        private void FinishSlide()
        {
            var dest = Player.GridPos;
            if (dest == _goal && _gemObjects.Count == 0)
                Succeed();
            else if (_moves >= _limit)
                Fail(Loc.T("이동 횟수를 다 썼다"));
            else if (dest == _goal)
                Room.ShowMessage(Loc.T("보석을 전부 모아야 깃발이 인정된다"));
            Player.ConsumeTurn();
        }
    }

    /// <summary>함정 복도 - 가로로 긴 복도의 칸마다 정해진 박자로 함정이 튀어나온다(빨간 칸 = 다음 턴에 터짐). 맞으면 실패.
    /// 오른쪽 끝 출구는 잠겨 있어서 복도 가운데쯤의 레버를 먼저 밟아야 열린다. 레버를 당긴 뒤 오른쪽 끝(초록 칸)까지 가면 성공.
    /// 박자는 칸마다 무작위(2~4턴 주기)이고, (칸, 턴, 레버) BFS로 반드시 지나갈 수 있는 배치만 쓴다.</summary>
    public class TrapCorridorPuzzle : PuzzleBase
    {
        public override string Title => Loc.T("함정 복도");
        public override string Rule => Loc.T("빨간 칸은 다음 턴에 함정이 튀어나온다. 레버를 밟아 출구를 열고 오른쪽 끝까지! (Space = 대기)");
        public override string Status => Loc.F("레버 {0}   턴 {1} / {2}", _leverOn ? Loc.T("당김") : Loc.T("아직"), _turn, _limit);

        private const int Height = 7;
        private int _width;
        private readonly Dictionary<Vector2Int, (int Period, int Offset)> _traps = new Dictionary<Vector2Int, (int, int)>();
        private readonly List<GameObject> _warnings = new List<GameObject>();
        private readonly List<SpriteRenderer> _goalTiles = new List<SpriteRenderer>();
        private Vector2Int _lever;
        private SpriteRenderer _leverTile;
        private bool _leverOn, _toldLocked;
        private int _turn, _limit;

        // 가시 - 판 위에 겹친 그림(Puzzle_Spike_1~3). 다음 턴에 터질 칸은 끝만 살짝, 터지는 순간 쑥 솟았다가 들어간다. 그림이 없으면 예전처럼 불꽃만.
        private readonly Dictionary<Vector2Int, SpriteRenderer> _spikes = new Dictionary<Vector2Int, SpriteRenderer>();
        private readonly HashSet<Vector2Int> _popping = new HashSet<Vector2Int>();
        private Sprite _spikePeek, _spikeHalf, _spikeUp;

        public override RoomLayout BuildLayout()
        {
            _width = 17 + 2 * Mathf.Min(2, (Stage - 1) / 3);
            var start = new Vector2Int(0, Height / 2);
            var shortest = -1;
            for (var attempt = 0; attempt < 150 && shortest < 0; attempt++)
            {
                _traps.Clear();
                _lever = new Vector2Int(_width / 3 + Rng.Next(Mathf.Max(1, _width / 3)), Rng.Next(Height));
                var density = attempt < 75 ? 0.8f : 0.6f;
                for (var x = 1; x < _width - 1; x++)
                for (var y = 0; y < Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (c == _lever || Rng.NextDouble() >= density)
                        continue;
                    var period = Stage >= 5 ? 2 + Rng.Next(3) : 2 + Rng.Next(2);
                    _traps[c] = (period, Rng.Next(period));
                }
                shortest = Shortest(start);
            }
            if (shortest < 0)
            {
                _traps.Clear(); // 안전망 - 함정 없는 복도
                shortest = _width + Height;
            }
            _limit = shortest * 2 + 8;
            return new RoomLayout { Width = _width, Height = Height, PlayerStart = start };
        }

        private bool Fires(Vector2Int p, int turn) => _traps.TryGetValue(p, out var t) && (turn + t.Offset) % t.Period == 0;

        /// <summary>(칸, 턴 % 12, 레버) 상태 BFS - 레버를 당기고 오른쪽 끝 줄에 닿는 최소 턴, 못 가면 -1. 대기(제자리)도 한 수.</summary>
        private int Shortest(Vector2Int start)
        {
            var seen = new HashSet<(Vector2Int, int, bool)> { (start, 0, false) };
            var queue = new Queue<(Vector2Int Pos, int Turn, bool Lever)>();
            queue.Enqueue((start, 0, false));
            while (queue.Count > 0)
            {
                var (pos, turn, lever) = queue.Dequeue();
                if (turn > 300)
                    break;
                for (var i = 0; i <= Dirs.Length; i++)
                {
                    var next = i < Dirs.Length ? pos + Dirs[i] : pos;
                    if (!InBounds(next, _width, Height) || Fires(next, turn + 1))
                        continue;
                    var on = lever || next == _lever;
                    if (on && next.x == _width - 1)
                        return turn + 1;
                    if (seen.Add((next, (turn + 1) % 12, on)))
                        queue.Enqueue((next, turn + 1, on));
                }
            }
            return -1;
        }

        protected override void OnBegin()
        {
            foreach (var p in _traps.Keys)
                ArtTile(p, "TrapPlate", 0.94f, Color.white, new Color(0.35f, 0.12f, 0.12f, 0.9f), 0.7f, -9, "TrapBase");
            _spikePeek = PuzzleArt("Spike_1");
            _spikeHalf = PuzzleArt("Spike_2");
            _spikeUp = PuzzleArt("Spike_3");
            if (_spikePeek != null && _spikeHalf != null && _spikeUp != null)
                foreach (var p in _traps.Keys)
                {
                    var spike = SpriteAt(World(p), _spikePeek, 0.94f, -8, "TrapSpike");
                    spike.sprite = null;
                    _spikes[p] = spike;
                }
            for (var y = 0; y < Height; y++)
                _goalTiles.Add(ArtTile(new Vector2Int(_width - 1, y), "ExitTile", 1f, GoalColor, GoalColor, 0.94f, -9, "Goal"));
            _leverTile = ArtTile(_lever, "Lever_Off", 0.95f, Color.white, new Color(0.45f, 0.45f, 0.9f), 0.6f, -7, "Lever");
            ShowWarnings();
        }

        private Color GoalColor => _leverOn ? new Color(0.3f, 0.85f, 0.4f, 0.55f) : new Color(0.45f, 0.45f, 0.5f, 0.55f);

        private void ShowWarnings()
        {
            foreach (var go in _warnings)
                Untrack(go);
            _warnings.Clear();
            foreach (var p in _traps.Keys)
                if (Fires(p, _turn + 1))
                    _warnings.Add(Track(Room.SpawnWarningTile(p, new Color(1f, 0.15f, 0.15f, 0.45f))));
            foreach (var pair in _spikes)
                if (!_popping.Contains(pair.Key) && pair.Value != null)
                    pair.Value.sprite = RestingSpike(pair.Key);
        }

        /// <summary>쉬는 가시 - 다음 턴에 터질 칸이면 끝만 살짝, 아니면 구멍 속(그림 없음).</summary>
        private Sprite RestingSpike(Vector2Int p) => IsRunning && Fires(p, _turn + 1) ? _spikePeek : null;

        /// <summary>터지는 순간 - 반쯤 → 다 → 반쯤 → 쉬는 모양. 퍼즐이 치워져 그림이 사라졌으면 그만둔다.</summary>
        private IEnumerator PopSpike(Vector2Int p, SpriteRenderer spike)
        {
            _popping.Add(p);
            spike.sprite = _spikeHalf;
            yield return new WaitForSeconds(0.04f);
            if (spike == null) yield break;
            spike.sprite = _spikeUp;
            yield return new WaitForSeconds(0.28f);
            if (spike == null) yield break;
            spike.sprite = _spikeHalf;
            yield return new WaitForSeconds(0.06f);
            _popping.Remove(p);
            if (spike != null)
                spike.sprite = RestingSpike(p);
        }

        protected override void OnTurnEnd()
        {
            _turn++;
            foreach (var p in _traps.Keys)
                if (Fires(p, _turn))
                {
                    if (_spikes.TryGetValue(p, out var spike) && spike != null)
                    {
                        if (GameHUD.AutoPlayActive)
                            spike.sprite = null; // 봇 - 연출 생략
                        else
                            Room.StartCoroutine(PopSpike(p, spike));
                    }
                    else
                        Fx.Play("hit_spark", World(p), 0.8f, fps: 18f); // 가시 그림이 없으면 예전처럼 불꽃
                }
            var pos = Player.GridPos;
            if (Fires(pos, _turn))
            {
                HitFeedback.OnPlayerHurt(Player, null);
                Fail(Loc.T("함정에 맞았다"));
                return;
            }
            if (pos == _lever && !_leverOn)
            {
                _leverOn = true;
                var leverOn = PuzzleArt("Lever_On");
                if (leverOn != null)
                    _leverTile.sprite = leverOn; // 막대가 오른쪽으로 넘어가고 손잡이가 초록으로
                else
                    _leverTile.color = new Color(0.4f, 1f, 0.5f);
                foreach (var tile in _goalTiles)
                    tile.color = GoalColor;
                SfxPlayer.Play(Sfx.Appear);
                Room.ShowMessage(Loc.T("레버를 당겼다! 출구가 열렸다"));
            }
            if (pos.x == _width - 1)
            {
                if (_leverOn)
                {
                    foreach (var go in _warnings)
                        Untrack(go); // 깜빡이는 예고 칸이 성공 뒤에도 남지 않게
                    _warnings.Clear();
                    Succeed();
                    return;
                }
                if (!_toldLocked)
                {
                    _toldLocked = true;
                    Room.ShowMessage(Loc.T("출구가 잠겨 있다 - 파란 레버를 먼저 밟아라"));
                }
            }
            if (_turn >= _limit)
            {
                Fail(Loc.T("시간 초과"));
                return;
            }
            ShowWarnings();
        }
    }

    /// <summary>룬 순서 - 룬들이 하나씩 빛나는 순서를 보여주면 같은 순서로 밟는다. 라운드를 전부(1~6층 2라운드, 7층부터 3라운드)
    /// 맞혀야 성공 - 라운드마다 순서가 길어지고(같은 룬이 다시 나올 수 있다) 빨라진다. 4층부터 마지막 라운드는 "거꾸로" 밟아야 한다.
    /// 틀린 룬을 밟으면 실패.</summary>
    public class RuneOrderPuzzle : PuzzleBase
    {
        public override string Title => Loc.T("룬 순서");
        public override string Rule => Loc.T("룬이 빛나는 순서대로 밟아라. 라운드마다 길어지고, 4층부터 마지막 라운드는 거꾸로 밟아야 한다!");
        public override string Status
        {
            get
            {
                var round = Loc.F("라운드 {0} / {1}", _round + 1, _rounds);
                if (InIntro)
                    return round + "   " + Loc.T("잘 봐라!");
                return round + "   " + (Reversed ? Loc.T("거꾸로!") + "   " : "") + Loc.F("{0} / {1}", _progress, _order.Count);
            }
        }

        private static readonly Color[] Palette =
        {
            new Color(1f, 0.35f, 0.35f), new Color(0.35f, 0.7f, 1f), new Color(0.4f, 1f, 0.45f), new Color(1f, 0.85f, 0.3f),
            new Color(0.85f, 0.45f, 1f), new Color(1f, 0.6f, 0.2f), new Color(0.4f, 1f, 0.95f),
        };

        private const int Size = 9;
        private readonly List<Vector2Int> _runes = new List<Vector2Int>();
        private readonly List<int> _order = new List<int>();
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private int _rounds, _round, _progress;

        private bool Reversed => Stage >= 4 && _round == _rounds - 1;

        public override RoomLayout BuildLayout()
        {
            var start = new Vector2Int(Size / 2, 0);
            var count = Mathf.Min(4 + (Stage - 1) / 3, Palette.Length);
            _rounds = Stage >= 7 ? 3 : 2;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Size; x++)
            for (var y = 1; y < Size; y++)
                cells.Add(new Vector2Int(x, y));
            Shuffle(cells);
            foreach (var c in cells)
            {
                if (_runes.Count >= count)
                    break;
                if (_runes.Any(r => Mathf.Max(Mathf.Abs(r.x - c.x), Mathf.Abs(r.y - c.y)) <= 1))
                    continue; // 룬끼리 붙지 않게
                _runes.Add(c);
            }
            return new RoomLayout { Width = Size, Height = Size, PlayerStart = start };
        }

        protected override void OnBegin()
        {
            for (var i = 0; i < _runes.Count; i++)
                _renderers.Add(ArtTile(_runes[i], "Rune_" + (i % 7), 0.8f, Dim(i), Dim(i), 0.8f, -7, "Rune")); // 룬마다 다른 글자
            StartRound();
        }

        /// <summary>새 순서 - 첫 라운드는 룬마다 한 번씩, 다음 라운드부터는 한 개씩 더 길고 같은 룬이 다시 나올 수 있다(바로 연달아는 안 나옴).</summary>
        private void StartRound()
        {
            _order.Clear();
            _progress = 0;
            if (_round == 0)
            {
                for (var i = 0; i < _runes.Count; i++)
                    _order.Add(i);
                Shuffle(_order);
            }
            else
            {
                var length = _runes.Count + _round;
                while (_order.Count < length)
                {
                    var next = Rng.Next(_runes.Count);
                    if (_order.Count > 0 && _order[_order.Count - 1] == next)
                        continue;
                    _order.Add(next);
                }
            }
            RunIntro(ShowOrder());
        }

        private Color Dim(int i) => Color.Lerp(new Color(0.15f, 0.15f, 0.18f), Palette[i], 0.3f);

        private IEnumerator ShowOrder()
        {
            var on = Mathf.Max(0.25f, 0.6f - Stage * 0.03f - _round * 0.08f);
            yield return new WaitForSecondsRealtime(0.8f);
            if (Reversed)
            {
                Room.ShowMessage(Loc.T("이번엔 거꾸로 밟아라!"));
                yield return new WaitForSecondsRealtime(0.6f);
            }
            foreach (var i in _order)
            {
                _renderers[i].color = Palette[i];
                _renderers[i].transform.localScale = Vector3.one * GridConstants.CellSize * 0.95f;
                SfxPlayer.Play(Sfx.Appear);
                yield return new WaitForSecondsRealtime(on);
                _renderers[i].color = Dim(i);
                _renderers[i].transform.localScale = Vector3.one * GridConstants.CellSize * 0.8f;
                yield return new WaitForSecondsRealtime(0.15f);
            }
        }

        protected override void OnPlayerMoved(Vector2Int pos)
        {
            var index = _runes.IndexOf(pos);
            if (index < 0 || InIntro)
                return;
            var expected = Reversed ? _order[_order.Count - 1 - _progress] : _order[_progress];
            if (expected != index)
            {
                _renderers[index].color = new Color(0.5f, 0.1f, 0.1f);
                Fail(Loc.T("순서가 틀렸다"));
                return;
            }
            _progress++;
            Room.StartCoroutine(Flash(index));
            SfxPlayer.Play(Sfx.Appear);
            if (_progress < _order.Count)
                return;
            _round++;
            if (_round >= _rounds)
            {
                Succeed();
                return;
            }
            Room.ShowMessage(Loc.F("정답! 라운드 {0} / {1}", _round + 1, _rounds));
            StartRound();
        }

        /// <summary>맞게 밟은 룬을 잠깐 밝힌다(같은 룬을 다시 밟을 수 있어서 계속 켜두지 않는다).</summary>
        private IEnumerator Flash(int index)
        {
            var renderer = _renderers[index];
            renderer.color = Palette[index];
            yield return new WaitForSecondsRealtime(0.35f);
            if (renderer != null && !InIntro)
                renderer.color = Dim(index);
        }
    }

    /// <summary>상자 밀기(소코반) - 돌덩이를 밀어 석판에 전부 올리면 성공. 3층부터 돌과 석판에 모양(○△□◇✚)·색이 붙어 같은 짝에만 인정되고,
    /// (그림: 돌덩이 Puzzle_Block(_i)·석판 Puzzle_Slab(_i) - 사용자 요청 "모양이 같은 석판에 올리는" 느낌, 예전엔 나무 상자·발판)
    /// 돌은 1~2층 2개·3~6층 3개·7층부터 5개(✚ 주황 추가, 돌 5개는 ScatterByPulls - 사용자 "7~10층은 더 올려도 된다"로 하한 32/35/38/41). 방은 "돌이 전부 제 석판 위에 있는 완성 상태"에서 거꾸로 당기며 퍼지는 탐색(FarthestStart)으로
    /// 층별 최소 밀기 횟수(TargetPushes: 1층 10 ~ 8층부터 26)만큼 떨어진 배치를 골라 만든다 - 당기기를 거꾸로 하면 밀기라 항상 풀리고,
    /// 탐색 깊이가 곧 정확한 최소 밀기 횟수다(예전엔 무작위로 흩뜨리기만 해서 몇 번 밀면 끝나는 판이 많았다 - 사용자 "너무 쉽다").
    /// [Z]로 한 수 되돌리기(턴 소모 없음).</summary>
    public class BoxPushPuzzle : PuzzleBase
    {
        private static readonly Color[] Palette =
        {
            new Color(1f, 0.4f, 0.35f), new Color(0.4f, 0.65f, 1f), new Color(0.45f, 0.95f, 0.45f), new Color(0.9f, 0.5f, 1f),
            new Color(1f, 0.72f, 0.25f), // 5번째(7층부터) - 주황 ✚
        };

        public override string Title => Loc.T("상자 밀기");
        public override string Rule => _colored
            ? Loc.T("돌을 밀어 같은 모양·색 석판에 올려라. 당길 수는 없다! [Z] 되돌리기, 막히면 [G] 포기 → 다시 도전")
            : Loc.T("돌을 밀어 석판에 전부 올려라. 당길 수는 없다! [Z] 되돌리기, 막히면 [G] 포기 → 다시 도전");
        public override string Status => Loc.F("돌 {0} / {1}   이동 {2}", Covered, _plates.Count, _moves);
        public override string ExtraKeyLabel => Loc.T("되돌리기 [Z]");
        public override Key ExtraKey => Key.Z;

        private int _width, _height;
        private bool _colored, _stoneArt;
        private HashSet<Vector2Int> _walls = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _plates = new List<Vector2Int>();   // 상자 i의 발판 = _plates[i]
        private List<Vector2Int> _startBoxes = new List<Vector2Int>();
        private readonly List<Vector2Int> _boxes = new List<Vector2Int>();     // 상자 i의 지금 칸
        private readonly List<SpriteRenderer> _boxRenderers = new List<SpriteRenderer>();
        private readonly Stack<(Vector2Int Player, Vector2Int[] Boxes)> _history = new Stack<(Vector2Int, Vector2Int[])>();
        private int _moves;

        private bool OnOwnPlate(int i) => _colored ? _boxes[i] == _plates[i] : _plates.Contains(_boxes[i]);
        private int Covered => Enumerable.Range(0, _boxes.Count).Count(OnOwnPlate);

        /// <summary>층별 최소 밀기 횟수 목표(사용자: "너무 쉽다"). 완성 상태에서 거꾸로 당기며 퍼지는 탐색으로 가장 멀리 떨어진 배치를 고르고,
        /// 이 이상이면 그 판을 쓴다(시간 안에 못 찾으면 그때까지 가장 어려운 판).</summary>
        /// 3~6층(돌 3개)은 정확한 최소 밀기 횟수, 7층부터(돌 5개 - 사용자 결정)는 돌마다 "혼자 제 석판까지" 밀기 횟수 합(실제 풀이는 이보다
        /// 항상 같거나 많다 - 돌끼리 막으니까). 돌 5개는 경우의 수가 너무 많아 정확히 못 잰다(4개도 테스트에서 시간 안에 평균 11번까지만).
        private int TargetPushes => _boxCount >= 5 ? 32 + (Stage - 7) * 3 : _boxCount >= 3 ? Mathf.Min(16 + (Stage - 3) * 2, 22) : 10 + (Stage - 1) * 2;

        private const float WallDensity = 0.17f;          // 예전 0.1 - 길이 좁아야 돌 돌리기가 어렵다
        private const double GenerateBudgetMs = 350;      // 방을 깔 때 판 고르기에 쓰는 최대 시간
        private const int SearchStateLimit = 120000;      // 판 하나당 거꾸로 탐색 상태 한도
        private int _boxCount;
        public int MinPushes { get; private set; }        // 고른 판의 최소 밀기 횟수(정확값, 거꾸로 탐색 깊이)

        public override RoomLayout BuildLayout()
        {
            var count = Stage >= 7 ? 5 : Stage >= 3 ? 3 : 2;
            _boxCount = count;
            _colored = Stage >= 3;
            _width = count >= 5 ? 12 : Stage >= 3 ? 11 : 10;
            _height = count >= 5 ? 10 : Stage >= 3 ? 9 : 8;
            var start = Vector2Int.zero;
            var best = -1;
            HashSet<Vector2Int> bestWalls = null;
            List<Vector2Int> bestPlates = null, bestBoxes = null;
            var bestPlayer = Vector2Int.zero;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var attempt = 0; attempt < 200 && watch.Elapsed.TotalMilliseconds < GenerateBudgetMs; attempt++)
            {
                var anchor = new Vector2Int(_width / 2, _height / 2);
                var walls = RandomWalls(_width, _height, count >= 5 ? 0.14f : WallDensity, anchor); // 돌 5개는 조금 넓게
                bool Open(Vector2Int c) => InBounds(c, _width, _height) && !walls.Contains(c);
                var roomy = new List<Vector2Int>();
                for (var x = 0; x < _width; x++)
                for (var y = 0; y < _height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (Open(c) && Dirs.Count(d => Open(c + d)) >= 3)
                        roomy.Add(c);
                }
                if (roomy.Count < count + 1)
                    continue;
                Shuffle(roomy);
                var plates = roomy.Take(count).ToList();
                var found = count >= 5 ? ScatterByPulls(walls, plates) : FarthestStart(walls, plates, watch, GenerateBudgetMs + 120);
                if (found.Depth <= best)
                    continue;
                best = found.Depth;
                bestWalls = walls;
                bestPlates = plates;
                bestBoxes = found.Boxes;
                bestPlayer = found.Player;
                if (best >= TargetPushes)
                    break;
            }
            MinPushes = best;
            if (bestBoxes != null && best > 0)
            {
                _walls = bestWalls;
                _plates.Clear();
                _plates.AddRange(bestPlates);
                _startBoxes = bestBoxes;
                start = bestPlayer;
            }
            else
            {
                // 안전망 - 상자 하나를 발판 두 칸 아래에서 밀어 올리기
                _walls = new HashSet<Vector2Int>();
                _plates.Clear();
                _plates.Add(new Vector2Int(_width / 2, _height - 2));
                _startBoxes = new List<Vector2Int> { new Vector2Int(_width / 2, _height - 4) };
                start = new Vector2Int(_width / 2, _height - 5);
            }
            return new RoomLayout { Width = _width, Height = _height, PlayerStart = start, Walls = new HashSet<Vector2Int>(_walls) };
        }

        /// <summary>돌 5개(7층부터) - 완성 상태에서 플레이어가 무작위로 걸으며 등 뒤 돌을 끌어 흩뜨린다(거꾸로 하면 밀기라 항상 풀림).
        /// 흩뜨리는 동안 "돌마다 혼자 제 석판까지 밀기 횟수의 합"(실제 최소 밀기의 하한)이 가장 큰, 석판 위 돌이 없는 배치를 고른다.</summary>
        private (int Depth, List<Vector2Int> Boxes, Vector2Int Player) ScatterByPulls(HashSet<Vector2Int> walls, List<Vector2Int> plates)
        {
            var w = _width;
            var h = _height;
            bool Open(Vector2Int c) => InBounds(c, w, h) && !walls.Contains(c);

            // 석판마다: 돌이 그 칸에서 (다른 돌 없이) 석판까지 밀리는 최소 횟수 - 석판에서 거꾸로 당기기 BFS
            int[,] PushDistance(Vector2Int goal)
            {
                var dist = new int[w, h];
                for (var x = 0; x < w; x++)
                for (var y = 0; y < h; y++)
                    dist[x, y] = -1;
                dist[goal.x, goal.y] = 0;
                var q = new Queue<Vector2Int>();
                q.Enqueue(goal);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    foreach (var d in Dirs)
                    {
                        var from = c - d;       // 돌이 from에서 c로 밀려 왔다
                        var stand = from - d;   // 그때 플레이어 자리
                        if (!Open(from) || !Open(stand) || dist[from.x, from.y] >= 0)
                            continue;
                        dist[from.x, from.y] = dist[c.x, c.y] + 1;
                        q.Enqueue(from);
                    }
                }
                return dist;
            }

            var count = plates.Count;
            var dists = plates.Select(PushDistance).ToArray();
            int Bound(List<Vector2Int> b)
            {
                var sum = 0;
                for (var i = 0; i < count; i++)
                {
                    var v = dists[i][b[i].x, b[i].y];
                    if (v < 0)
                        return -1;
                    sum += v;
                }
                return sum;
            }

            var boxes = new List<Vector2Int>(plates);
            var player = Vector2Int.zero;
            var openCells = new List<Vector2Int>();
            for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
                if (Open(new Vector2Int(x, y)) && !boxes.Contains(new Vector2Int(x, y)))
                    openCells.Add(new Vector2Int(x, y));
            if (openCells.Count == 0)
                return (0, null, Vector2Int.zero);
            player = openCells[Rng.Next(openCells.Count)];

            var best = 0;
            List<Vector2Int> bestBoxes = null;
            var bestPlayer = player;
            var target = TargetPushes;
            for (var s = 0; s < 12000; s++)
            {
                var d = Dirs[Rng.Next(Dirs.Length)];
                var next = player + d;
                if (!Open(next) || boxes.Contains(next))
                    continue;
                var behind = boxes.IndexOf(player - d);
                if (behind >= 0 && Rng.NextDouble() < 0.6)
                    boxes[behind] = player;
                player = next;
                if (behind < 0)
                    continue;
                if (Enumerable.Range(0, count).Any(i => boxes[i] == plates[i]))
                    continue;
                var bound = Bound(boxes);
                if (bound <= best)
                    continue;
                best = bound;
                bestBoxes = new List<Vector2Int>(boxes);
                bestPlayer = player;
                if (best >= target + 4)
                    break; // 목표보다 조금 넉넉하면 충분
            }
            return (best, bestBoxes, bestPlayer);
        }

        /// <summary>완성 상태(돌이 전부 제 석판 위)에서 거꾸로 "당기기"로 BFS - 깊이 = 그 배치에서 최소 밀기 횟수(정확값).
        /// 상태 = 돌 위치 + 플레이어가 갈 수 있는 구역의 대표 칸. 가장 깊은 배치 중 석판 위에 돌이 하나도 없는 것을 돌려준다.</summary>
        private (int Depth, List<Vector2Int> Boxes, Vector2Int Player) FarthestStart(HashSet<Vector2Int> walls, List<Vector2Int> plates,
            System.Diagnostics.Stopwatch watch, double deadlineMs)
        {
            var w = _width;
            var h = _height;
            var n = w * h;
            var wall = new bool[n];
            foreach (var c in walls)
                wall[c.y * w + c.x] = true;
            var dx = new[] { 0, 0, -1, 1 };
            var dy = new[] { 1, -1, 0, 0 };
            var count = plates.Count;
            var plateIdx = plates.Select(c => c.y * w + c.x).ToArray();
            var isPlate = new bool[n];
            foreach (var p in plateIdx)
                isPlate[p] = true;

            var occ = new bool[n];
            var mark = new int[n];
            var stampValue = 0;
            var stack = new int[n];

            // from에서 갈 수 있는 칸을 mark에 표시, 대표 칸(가장 작은 번호) 반환
            int Flood(int from)
            {
                stampValue++;
                var top = 0;
                stack[top++] = from;
                mark[from] = stampValue;
                var min = from;
                while (top > 0)
                {
                    var c = stack[--top];
                    if (c < min)
                        min = c;
                    int cx = c % w, cy = c / w;
                    for (var k = 0; k < 4; k++)
                    {
                        int nx = cx + dx[k], ny = cy + dy[k];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        var nc = ny * w + nx;
                        if (wall[nc] || occ[nc] || mark[nc] == stampValue)
                            continue;
                        mark[nc] = stampValue;
                        stack[top++] = nc;
                    }
                }
                return min;
            }

            long Key(int[] b, int rep)
            {
                long key = rep;
                if (_colored)
                {
                    foreach (var v in b)
                        key = key * 256 + v;
                    return key;
                }
                var sorted = (int[])b.Clone();
                System.Array.Sort(sorted); // 색 구분이 없으면 돌끼리 같은 것 - 같은 배치를 하나로
                foreach (var v in sorted)
                    key = key * 256 + v;
                return key;
            }

            // 펼칠 때 대표 칸을 구해서 중복을 거른다(자식마다 구역을 다시 칠하지 않아 빠르다). 자식은 (돌, 정확한 플레이어 칸)으로만 1차 중복 제거.
            var seen = new HashSet<long>();
            var queued = new HashSet<long>();
            var frontier = new List<(int[] Boxes, int Player)>();
            foreach (var c in plateIdx)
                occ[c] = true;
            for (var c = 0; c < n; c++)
                if (!wall[c] && !occ[c])
                    frontier.Add(((int[])plateIdx.Clone(), c));
            foreach (var c in plateIdx)
                occ[c] = false;

            var target = TargetPushes;
            var bestDepth = 0;
            int[] bestBoxes = null;
            var bestPlayer = 0;
            for (var depth = 0; frontier.Count > 0; depth++)
            {
                var next = new List<(int[] Boxes, int Player)>();
                queued.Clear();
                foreach (var (b, p) in frontier)
                {
                    foreach (var c in b)
                        occ[c] = true;
                    var rep = Flood(p);
                    if (!seen.Add(Key(b, rep)))
                    {
                        foreach (var c in b)
                            occ[c] = false;
                        continue;
                    }
                    var reach = stampValue;
                    var onPlate = false;
                    for (var i = 0; i < count && !onPlate; i++)
                        onPlate = _colored ? b[i] == plateIdx[i] : isPlate[b[i]];
                    if (!onPlate && (bestBoxes == null || depth > bestDepth))
                    {
                        bestDepth = depth;
                        bestBoxes = b;
                        bestPlayer = p;
                        if (depth >= target)
                        {
                            foreach (var c in b)
                                occ[c] = false;
                            return (bestDepth, bestBoxes.Select(v => new Vector2Int(v % w, v / w)).ToList(), new Vector2Int(bestPlayer % w, bestPlayer / w));
                        }
                    }
                    for (var i = 0; i < count; i++)
                    {
                        int bx = b[i] % w, by = b[i] / w;
                        for (var k = 0; k < 4; k++)
                        {
                            // 당기기: 플레이어가 돌 옆 P(= 돌 - d)에 서서 한 칸 더 물러나면(P - d) 돌이 P로 따라온다
                            int px = bx - dx[k], py = by - dy[k], qx = px - dx[k], qy = py - dy[k];
                            if (qx < 0 || qy < 0 || qx >= w || qy >= h || px < 0 || py < 0 || px >= w || py >= h)
                                continue;
                            var pc = py * w + px;
                            var qc = qy * w + qx;
                            if (mark[pc] != reach || wall[qc] || occ[qc])
                                continue;
                            var nb = (int[])b.Clone();
                            nb[i] = pc;
                            if (queued.Add(Key(nb, qc)))
                                next.Add((nb, qc));
                        }
                    }
                    foreach (var c in b)
                        occ[c] = false;
                    if (seen.Count > SearchStateLimit || watch.Elapsed.TotalMilliseconds > deadlineMs)
                    {
                        next.Clear();
                        break;
                    }
                }
                frontier = next;
            }
            if (bestBoxes == null)
                return (0, null, Vector2Int.zero);
            return (bestDepth, bestBoxes.Select(v => new Vector2Int(v % w, v / w)).ToList(), new Vector2Int(bestPlayer % w, bestPlayer / w));
        }

        protected override void OnBegin()
        {
            for (var i = 0; i < _plates.Count; i++)
            {
                // 석판 - 3층부터 돌과 같은 모양 홈(○△□◇), 그 전엔 둥근 문양(아무 돌이나)
                var slabTint = PlateColor(i);
                slabTint.a = 0.9f;
                ArtTile(_plates[i], _colored ? "Slab_" + (i % 5) : "Slab", 1f, slabTint, PlateColor(i), 0.8f, -9, "Plate");
            }
            _stoneArt = PuzzleArt("Block") != null;
            for (var i = 0; i < _startBoxes.Count; i++)
            {
                _boxes.Add(_startBoxes[i]);
                _boxRenderers.Add(ArtTile(_startBoxes[i], _colored ? "Block_" + (i % 5) : "Block", 0.95f, Color.white, Color.white, 0.78f, 1, "Box")); // 돌덩이(색은 Redraw가)
            }
            Redraw();
        }

        private Color PlateColor(int i)
        {
            var c = _colored ? Palette[i % Palette.Length] : new Color(0.95f, 0.8f, 0.25f);
            return new Color(c.r, c.g, c.b, 0.5f);
        }

        private Color BoxColor(int i)
        {
            if (!_colored) // 돌 그림이면 평소엔 돌 색 그대로, 그림이 없으면 예전 갈색 사각형
                return OnOwnPlate(i) ? new Color(0.95f, 0.75f, 0.3f) : _stoneArt ? new Color(0.82f, 0.8f, 0.78f) : new Color(0.55f, 0.36f, 0.2f);
            var c = Palette[i % Palette.Length];
            return OnOwnPlate(i) ? Color.Lerp(c, Color.white, 0.35f) : c * 0.7f + new Color(0f, 0f, 0f, 0.3f);
        }

        private void Redraw()
        {
            for (var i = 0; i < _boxes.Count; i++)
            {
                _boxRenderers[i].transform.position = World(_boxes[i]);
                _boxRenderers[i].color = BoxColor(i);
            }
        }

        private bool Blocked(Vector2Int c) => !Map.IsInBounds(c) || Map.IsWall(c);

        public override bool HandleAct(Vector2Int direction)
        {
            var next = Player.GridPos + direction;
            if (Blocked(next))
                return true;
            var box = _boxes.IndexOf(next);
            var snapshot = (Player.GridPos, _boxes.ToArray());
            if (box >= 0)
            {
                var beyond = next + direction;
                if (Blocked(beyond) || _boxes.Contains(beyond))
                    return true; // 못 민다 - 턴 소모 없음
                _boxes[box] = beyond;
                PlayPlayerAttack(direction);
            }
            _history.Push(snapshot);
            Map.MoveActor(Player, next);
            _moves++;
            Redraw();
            if (Covered == _plates.Count)
                Succeed();
            Player.ConsumeTurn();
            return true;
        }

        public override bool HandleKeys()
        {
            if (!GameInput.Down(Key.Z))
                return false;
            if (_history.Count == 0)
            {
                Room.ShowMessage(Loc.T("되돌릴 수가 없다"));
                return true;
            }
            var (player, boxes) = _history.Pop();
            for (var i = 0; i < boxes.Length; i++)
                _boxes[i] = boxes[i];
            Map.MoveActor(Player, player);
            if (Player.TryGetComponent<SpriteAnimator>(out var anim))
                anim.SnapToGrid(); // 걸어가는 모션 없이 그 자리로
            _moves++; // 되돌리기도 이동 수에는 센다(기록용)
            Redraw();
            RefreshHud();
            return true;
        }
    }

    /// <summary>전투 퍼즐 - 그 방 몹으로 조건 달성(무작위 셋 중 하나): "제한 턴 안에 전멸", "한 대도 안 맞고 전멸",
    /// "머리 위 번호 순서대로 처치"(다른 몹을 먼저 잡으면 실패 - 회전 베기·폭탄은 조심). 잡은 몹은 골드·경험치 없음.
    /// 대시·회전 베기·아이템 사용 가능.</summary>
    public class CombatChallengePuzzle : PuzzleBase
    {
        private enum Mode { TimeAttack, NoHit, Order }

        public override bool AllowsTools => true;
        public override string Title => Loc.T("전투 퍼즐");
        public override string Rule => _mode switch
        {
            Mode.TimeAttack => Loc.F("{0}턴 안에 몹을 전부 쓰러뜨려라.", _limit),
            Mode.NoHit => Loc.T("한 대도 맞지 말고 몹을 전부 쓰러뜨려라."),
            _ => Loc.T("머리 위 번호 순서대로 쓰러뜨려라. 다른 몹을 먼저 잡으면 실패! (회전 베기·폭탄 조심)"),
        };
        public override string Status => _mode switch
        {
            Mode.TimeAttack => Loc.F("남은 몹 {0}   남은 턴 {1}", Alive, Mathf.Max(0, _limit - _turns)),
            Mode.Order => Loc.F("다음 번호 {0} / {1}", _next + 1, _ordered.Count),
            _ => Loc.F("남은 몹 {0}", Alive),
        };

        private const int Size = 11;
        private Mode _mode;
        private int _count, _limit, _turns, _next;
        private double _damageAtStart;
        private Vector2Int _start;
        private readonly List<EnemyActor> _ordered = new List<EnemyActor>();
        private readonly List<TextMesh> _labels = new List<TextMesh>();

        private int Alive => Room.Enemies.Count(e => e != null && !e.Stats.IsDead);

        public override RoomLayout BuildLayout()
        {
            _mode = (Mode)Rng.Next(3);
            _count = _mode switch
            {
                Mode.TimeAttack => Mathf.Min(4 + Stage / 2, 9),
                Mode.NoHit => Mathf.Min(3 + Stage / 3, 6),
                _ => Mathf.Min(3 + Stage / 3, 6),
            };
            _limit = 8 + _count * 2;
            _start = new Vector2Int(Size / 2, 0);
            return new RoomLayout { Width = Size, Height = Size, PlayerStart = _start, Walls = RandomWalls(Size, Size, 0.06f, _start) };
        }

        protected override void OnBegin()
        {
            var kinds = Room.CurrentDefinition?.EnemyKinds?.ToList() ?? new List<EnemyKind>();
            if (_mode == Mode.Order) // 스스로 터지는 폭발병·갈라지는 분열체는 순서 판정이 꼬여서 뺀다
                kinds.RemoveAll(k => k == EnemyKind.Bomber || k == EnemyKind.Splitter);
            if (kinds.Count == 0)
                kinds.Add(EnemyKind.Melee);
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var c = new Vector2Int(x, y);
                if (Map.IsWalkable(c) && Manhattan(c, _start) >= 4)
                    cells.Add(c);
            }
            Shuffle(cells);
            var hp = _mode == Mode.TimeAttack ? 0.6f : 0.5f;
            foreach (var c in cells.Take(_count))
                _ordered.Add(Room.SpawnPuzzleEnemy(c, Pick(kinds), hp, 1f));
            if (_mode == Mode.Order)
            {
                Shuffle(_ordered);
                for (var i = 0; i < _ordered.Count; i++)
                    _labels.Add(AttachNumber(_ordered[i], i + 1));
                RecolorLabels();
            }
            _damageAtStart = CharacterStats.IncomingDamageTotal;
        }

        /// <summary>몹 머리 위 번호 - 몹의 자식이라 같이 움직이고 같이 사라진다(몹 크기 배율은 상쇄).</summary>
        private static TextMesh AttachNumber(EnemyActor enemy, int number)
        {
            var go = new GameObject("OrderNumber");
            go.transform.SetParent(enemy.transform, false);
            var scale = Mathf.Max(0.0001f, enemy.transform.lossyScale.x);
            go.transform.localScale = Vector3.one / scale;
            go.transform.localPosition = new Vector3(0f, GridConstants.CellSize * 0.7f / scale, -0.4f / scale);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = number.ToString();
            mesh.fontSize = 48;
            mesh.characterSize = 0.08f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = mesh.font.material;
            renderer.sortingOrder = 6;
            return mesh;
        }

        private void RecolorLabels()
        {
            for (var i = 0; i < _labels.Count; i++)
                if (_labels[i] != null)
                    _labels[i].color = i == _next ? new Color(1f, 0.9f, 0.3f) : new Color(0.8f, 0.8f, 0.85f);
        }

        protected override void OnEnemyDefeated(EnemyActor enemy)
        {
            if (_mode == Mode.Order)
            {
                var index = _ordered.IndexOf(enemy);
                if (index < 0)
                    return;
                if (index != _next)
                {
                    Fail(Loc.F("순서가 틀렸다 - {0}번을 먼저 잡아야 했다", _next + 1));
                    return;
                }
                _next++;
                if (_next >= _ordered.Count)
                {
                    Succeed();
                    return;
                }
                RecolorLabels();
                RefreshHud();
                return;
            }
            if (Alive == 0)
                Succeed();
        }

        protected override void OnTurnEnd()
        {
            _turns++;
            if (_mode == Mode.NoHit && CharacterStats.IncomingDamageTotal > _damageAtStart + 0.001)
                Fail(Loc.T("맞았다!"));
            else if (_mode == Mode.TimeAttack && _turns >= _limit && Alive > 0)
                Fail(Loc.T("시간 초과"));
        }
    }
}
