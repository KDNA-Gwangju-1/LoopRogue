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
        }

        protected override void OnTurnEnd()
        {
            _turn++;
            foreach (var p in _traps.Keys)
                if (Fires(p, _turn))
                    Fx.Play("hit_spark", World(p), 0.8f, fps: 18f);
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

    /// <summary>상자 밀기(소코반) - 상자를 밀어 발판에 전부 올리면 성공. 3층부터 상자와 발판에 색이 붙어 같은 색 발판에만 인정되고,
    /// 상자도 층에 따라 2~4개로 늘어난다. 방은 "상자가 제 발판 위에 있는 완성 상태"에서 플레이어가 상자를 무작위로 끌어당기며
    /// 거꾸로 흩뜨려 만든다 - 당기기를 거꾸로 하면 밀기라서 항상 풀린다. [Z]로 한 수 되돌리기(턴 소모 없음).</summary>
    public class BoxPushPuzzle : PuzzleBase
    {
        private static readonly Color[] Palette =
        {
            new Color(1f, 0.4f, 0.35f), new Color(0.4f, 0.65f, 1f), new Color(0.45f, 0.95f, 0.45f), new Color(0.9f, 0.5f, 1f),
        };

        public override string Title => Loc.T("상자 밀기");
        public override string Rule => _colored
            ? Loc.T("상자를 밀어 같은 색 발판에 올려라. 당길 수는 없다! [Z] 되돌리기, 막히면 [G] 포기 → 다시 도전")
            : Loc.T("상자를 밀어 노란 발판에 전부 올려라. 당길 수는 없다! [Z] 되돌리기, 막히면 [G] 포기 → 다시 도전");
        public override string Status => Loc.F("상자 {0} / {1}   이동 {2}", Covered, _plates.Count, _moves);
        public override string ExtraKeyLabel => Loc.T("되돌리기 [Z]");
        public override Key ExtraKey => Key.Z;

        private int _width, _height;
        private bool _colored;
        private HashSet<Vector2Int> _walls = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _plates = new List<Vector2Int>();   // 상자 i의 발판 = _plates[i]
        private List<Vector2Int> _startBoxes = new List<Vector2Int>();
        private readonly List<Vector2Int> _boxes = new List<Vector2Int>();     // 상자 i의 지금 칸
        private readonly List<SpriteRenderer> _boxRenderers = new List<SpriteRenderer>();
        private readonly Stack<(Vector2Int Player, Vector2Int[] Boxes)> _history = new Stack<(Vector2Int, Vector2Int[])>();
        private int _moves;

        private bool OnOwnPlate(int i) => _colored ? _boxes[i] == _plates[i] : _plates.Contains(_boxes[i]);
        private int Covered => Enumerable.Range(0, _boxes.Count).Count(OnOwnPlate);

        public override RoomLayout BuildLayout()
        {
            var count = Stage >= 7 ? 4 : Stage >= 3 ? 3 : 2;
            _colored = Stage >= 3;
            _width = count >= 4 ? 12 : Stage >= 3 ? 11 : 10;
            _height = count >= 4 ? 10 : Stage >= 3 ? 9 : 8;
            var start = Vector2Int.zero;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var anchor = new Vector2Int(_width / 2, _height / 2);
                var walls = RandomWalls(_width, _height, 0.1f, anchor);
                var free = new List<Vector2Int>();
                for (var x = 0; x < _width; x++)
                for (var y = 0; y < _height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!walls.Contains(c))
                        free.Add(c);
                }
                bool Open(Vector2Int c) => InBounds(c, _width, _height) && !walls.Contains(c);
                var roomy = free.Where(c => Dirs.Count(d => Open(c + d)) >= 3).ToList();
                if (roomy.Count < count + 1)
                    continue;
                Shuffle(roomy);
                var plates = roomy.Take(count).ToList();
                var boxes = new List<Vector2Int>(plates);
                var player = free.First(c => !boxes.Contains(c));

                // 거꾸로 흩뜨리기 - 플레이어가 걸어가면서 등 뒤 상자를 가끔 끌고 온다
                var steps = 350 + 100 * count;
                for (var s = 0; s < steps; s++)
                {
                    var d = Dirs[Rng.Next(Dirs.Length)];
                    var next = player + d;
                    if (!Open(next) || boxes.Contains(next))
                        continue;
                    var behind = boxes.IndexOf(player - d);
                    if (behind >= 0 && Rng.NextDouble() < 0.7)
                        boxes[behind] = player;
                    player = next;
                }

                var solved = _colored
                    ? Enumerable.Range(0, count).Count(i => boxes[i] == plates[i])
                    : boxes.Count(plates.Contains);
                var spread = Enumerable.Range(0, count).Sum(i => Manhattan(boxes[i], plates[i]));
                if (solved == 0 && spread >= count * 2)
                {
                    _walls = walls;
                    _plates.Clear();
                    _plates.AddRange(plates);
                    _startBoxes = boxes;
                    start = player;
                    break;
                }
            }
            if (_startBoxes.Count == 0)
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

        protected override void OnBegin()
        {
            for (var i = 0; i < _plates.Count; i++)
                ArtTile(_plates[i], "Plate", 0.92f, PlateColor(i), PlateColor(i), 0.8f, -9, "Plate");
            for (var i = 0; i < _startBoxes.Count; i++)
            {
                _boxes.Add(_startBoxes[i]);
                _boxRenderers.Add(ArtTile(_startBoxes[i], "Crate", 0.92f, Color.white, Color.white, 0.78f, 1, "Box")); // 나무 상자(색은 Redraw가)
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
            if (!_colored)
                return OnOwnPlate(i) ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.55f, 0.36f, 0.2f);
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
