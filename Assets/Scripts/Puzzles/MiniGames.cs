using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>상자 섞기 - 보물이 든 상자를 보여준 뒤 상자들을 섞는다. 다 섞이면 상자에 부딪혀 열고, 보물 상자면 그 라운드 성공.
    /// 라운드를 전부(1~3층 2라운드, 4층부터 3라운드) 연달아 맞혀야 성공 - 라운드마다 더 많이·빨리 섞고, 3층부터 마지막 라운드는
    /// 섞는 도중 상자가 어두워지는 "어둠 섞기"가 섞인다. 3개(6층부터 4개).</summary>
    public class ShellGameMiniGame : PuzzleBase
    {
        public override string Title => Loc.T("상자 섞기");
        public override string Rule => Loc.T("보물 상자를 눈으로 쫓아 부딪혀 열어라. 라운드를 전부 맞혀야 성공! 마지막 라운드엔 상자가 어두워진다.");
        public override string Status => (InIntro ? Loc.T("잘 봐라!") : Loc.T("보물이 든 상자를 골라라")) + "   " + Loc.F("라운드 {0} / {1}", _round + 1, _rounds);

        private const int Height = 7, ChestRow = 4;
        private int _count, _width, _rounds, _round;
        private readonly List<Vector2Int> _slots = new List<Vector2Int>();
        private readonly List<GameObject> _chestAtSlot = new List<GameObject>();
        private GameObject _treasureChest;
        private GameObject _coin;

        public override RoomLayout BuildLayout()
        {
            _count = Stage >= 6 ? 4 : 3;
            _rounds = Stage >= 4 ? 3 : 2;
            _width = _count * 3 + 1;
            for (var i = 0; i < _count; i++)
                _slots.Add(new Vector2Int(1 + i * 3, ChestRow));
            return new RoomLayout { Width = _width, Height = Height, PlayerStart = new Vector2Int(_width / 2, 0) };
        }

        protected override void OnBegin()
        {
            var sprite = FxSprite("event_TreasureChest");
            foreach (var slot in _slots)
            {
                Map.AddWall(slot); // 상자 칸으로는 못 걸어 들어간다(부딪히면 연다 - HandleAct)
                var chest = sprite != null
                    ? SpriteAt(World(slot), sprite, 0.9f, 1, "Chest").gameObject
                    : Square(slot, new Color(0.6f, 0.4f, 0.2f), 0.8f, 1, "Chest").gameObject;
                _chestAtSlot.Add(chest);
            }
            StartRound();
        }

        private void StartRound()
        {
            _treasureChest = _chestAtSlot[Rng.Next(_count)];
            RunIntro(ShowAndShuffle());
        }

        private IEnumerator ShowAndShuffle()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (_coin != null)
                Untrack(_coin); // 지난 라운드 정답 표시
            _coin = ShowCoin(_treasureChest.transform.position);
            yield return new WaitForSecondsRealtime(1.2f);
            Untrack(_coin);
            _coin = null;
            yield return new WaitForSecondsRealtime(0.3f);

            var lastRound = _round == _rounds - 1;
            var swaps = Mathf.Min(3 + Stage + _round * 2, 16);
            var seconds = Mathf.Max(0.14f, 0.42f - Stage * 0.02f - _round * 0.06f);
            var dark = lastRound && Stage >= 3;
            for (var s = 0; s < swaps; s++)
            {
                var a = Rng.Next(_count);
                var b = (a + 1 + Rng.Next(_count - 1)) % _count;
                var chestA = _chestAtSlot[a];
                var chestB = _chestAtSlot[b];
                var fromA = World(_slots[a]);
                var fromB = World(_slots[b]);
                var hide = dark && s % 2 == 1; // 어둠 섞기 - 두 번에 한 번은 상자가 어두워진 채로 바뀐다
                SetDark(hide);
                for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                {
                    var k = t / seconds;
                    var arc = Mathf.Sin(k * Mathf.PI) * GridConstants.CellSize * 0.7f;
                    chestA.transform.position = Vector3.Lerp(fromA, fromB, k) + Vector3.up * arc;
                    chestB.transform.position = Vector3.Lerp(fromB, fromA, k) - Vector3.up * arc;
                    yield return null;
                }
                chestA.transform.position = fromB;
                chestB.transform.position = fromA;
                _chestAtSlot[a] = chestB;
                _chestAtSlot[b] = chestA;
            }
            SetDark(false);
        }

        private void SetDark(bool dark)
        {
            foreach (var chest in _chestAtSlot)
                if (chest.TryGetComponent<SpriteRenderer>(out var renderer))
                    renderer.color = dark ? new Color(0.12f, 0.12f, 0.16f, 1f) : Color.white;
        }

        private GameObject ShowCoin(Vector3 chestPos)
        {
            var coin = FxSprite("coin");
            var pos = chestPos + Vector3.up * GridConstants.CellSize * 0.8f;
            if (coin != null)
                return SpriteAt(pos, coin, 0.5f, 5, "Treasure").gameObject;
            var square = Square(Vector2Int.zero, new Color(1f, 0.85f, 0.2f), 0.4f, 5, "Treasure").gameObject;
            square.transform.position = pos;
            return square;
        }

        public override bool HandleAct(Vector2Int direction)
        {
            var slot = _slots.IndexOf(Player.GridPos + direction);
            if (slot < 0)
                return false;
            PlayPlayerAttack(direction);
            _coin = ShowCoin(_treasureChest.transform.position);
            if (_chestAtSlot[slot] != _treasureChest)
            {
                Fail(Loc.T("꽝! 보물은 다른 상자에 있었다"));
                return true;
            }
            _round++;
            if (_round >= _rounds)
            {
                Succeed();
                return true;
            }
            SfxPlayer.Play(Sfx.Appear);
            Room.ShowMessage(Loc.F("정답! 라운드 {0} / {1}", _round + 1, _rounds));
            StartRound();
            RefreshHud();
            return true;
        }
    }

    /// <summary>두더지 잡기 - 4x4 구멍에서 두더지가 실제 시간으로 잠깐씩 튀어나왔다 숨는다. 구멍 쪽으로 부딪혀 때린다. 갈색 +1,
    /// 금색 왕관 +3(금방 숨음), 폭탄을 인 두더지는 때리면 -2(그냥 두면 저절로 숨는다). 1분 20초 안에 목표 점수를 채우면 성공(사용자 결정 - 턴 제한 대신 카운트다운).
    /// 레벨업 카드·Esc 메뉴·인벤토리가 열려 있으면 시간이 멈춘다.
    /// 그림은 Resources/Sprites/Props/Mole_*(구멍·보통·금색·폭탄·맞음) - 없으면 예전처럼 슬라임 그림에 색을 입힌다.</summary>
    public class WhackSlimeMiniGame : PuzzleBase
    {
        private enum Mole { Normal, Gold, Bomb }

        public override string Title => Loc.T("두더지 잡기");
        // 그림 유무는 OnBegin에서 정해지는데 설명 창은 그 전에 뜨므로 파일이 있는지로 바로 판단한다
        public override string Rule => Resources.Load<Sprite>("Sprites/Props/Mole_Normal") != null
            ? Loc.F("1분 20초 안에 {0}점을 모아라!\n구멍 옆에 서서 구멍 쪽으로 방향키를 누르면 두더지를 때린다. 두더지는 저절로 튀어나왔다 숨는다.\n갈색 두더지 +1   금색 왕관 두더지 +3(금방 숨음)   폭탄을 인 두더지는 때리면 -2!", _target)
            : Loc.F("1분 20초 안에 {0}점을 모아라!\n구멍 옆에 서서 구멍 쪽으로 방향키를 누르면 슬라임을 때린다. 슬라임은 저절로 튀어나왔다 숨는다.\n초록 +1   금색 +3(금방 숨음)   빨간 폭탄 슬라임은 때리면 -2!", _target);
        public override string Status
        {
            get
            {
                var left = Mathf.Max(0, Mathf.CeilToInt(TimeLimit - _elapsed));
                return Loc.F("점수 {0} / {1}   남은 시간 {2}:{3:00}", _score, _target, left / 60, left % 60);
            }
        }

        private const int Size = 9;
        private const float TimeLimit = 80f; // 1분 20초
        private const float PopSeconds = 0.14f, HitShowSeconds = 0.3f, GoldUpSeconds = 1f;
        private readonly List<Vector2Int> _holes = new List<Vector2Int>();
        private readonly Dictionary<Vector2Int, (Mole Kind, float Left)> _up = new Dictionary<Vector2Int, (Mole, float)>(); // 남은 초
        private readonly Dictionary<Vector2Int, SpriteRenderer> _moles = new Dictionary<Vector2Int, SpriteRenderer>();
        private readonly Dictionary<Vector2Int, Vector3> _baseScale = new Dictionary<Vector2Int, Vector3>();
        private readonly Dictionary<Vector2Int, float> _popAge = new Dictionary<Vector2Int, float>();   // 튀어나온 뒤 지난 시간(통통 튀는 연출)
        private readonly Dictionary<Vector2Int, float> _hitShow = new Dictionary<Vector2Int, float>();  // 맞은 얼굴을 보여줄 남은 시간
        private int _target, _maxUp, _score, _shownSeconds = -1;
        private float _upSeconds, _popEvery, _nextPop, _elapsed;
        private float _bombChance, _goldChance;
        private bool _sprite, _moleArt;
        private Sprite _moleNormal, _moleGold, _moleBomb, _moleHit;

        public override RoomLayout BuildLayout()
        {
            for (var x = 1; x < Size; x += 2)
            for (var y = 1; y < Size; y += 2)
                _holes.Add(new Vector2Int(x, y));
            _target = 20 + Stage;                        // 1층 21점 ~ 10층 30점(80초 기준 첫 값 - 해보고 조정)
            _upSeconds = Stage >= 4 ? 1.3f : 1.7f;       // 나와 있는 시간
            _popEvery = Stage >= 6 ? 0.45f : 0.6f;       // 이 간격마다 한 마리씩 튀어나온다
            _maxUp = Stage >= 6 ? 4 : 3;
            _bombChance = Mathf.Min(0.2f + Stage * 0.02f, 0.35f);
            _goldChance = Stage >= 3 ? 0.12f : 0f;
            return new RoomLayout { Width = Size, Height = Size, PlayerStart = new Vector2Int(Size / 2, 0) };
        }

        protected override void OnBegin()
        {
            var slime = SpriteAnimator.FirstFrame("Slime");
            _sprite = slime != null;
            var holeArt = Resources.Load<Sprite>("Sprites/Props/Mole_Hole");
            _moleNormal = Resources.Load<Sprite>("Sprites/Props/Mole_Normal");
            _moleGold = Resources.Load<Sprite>("Sprites/Props/Mole_Gold");
            _moleBomb = Resources.Load<Sprite>("Sprites/Props/Mole_Bomb");
            _moleHit = Resources.Load<Sprite>("Sprites/Props/Mole_Hit");
            _moleArt = _moleNormal != null && _moleGold != null && _moleBomb != null;
            foreach (var hole in _holes)
            {
                Map.AddWall(hole);
                if (holeArt != null)
                    SpriteAt(World(hole), holeArt, 1f, -7, "Hole");
                else
                    Square(hole, new Color(0.05f, 0.04f, 0.06f), 0.75f, -7, "Hole");
                var mole = _moleArt
                    ? SpriteAt(World(hole), _moleNormal, 1f, 1, "Mole")
                    : slime != null
                        ? SpriteAt(World(hole), slime, 0.9f, 1, "Mole")
                        : Square(hole, Color.white, 0.55f, 1, "Mole");
                mole.gameObject.SetActive(false);
                _moles[hole] = mole;
                _baseScale[hole] = mole.transform.localScale;
            }
            PopUp();
            PopUp();
            _nextPop = _popEvery;
        }

        private Color Tint(Mole kind) => kind switch
        {
            Mole.Gold => new Color(1f, 0.85f, 0.2f),
            Mole.Bomb => new Color(1f, 0.3f, 0.3f),
            _ => _sprite ? Color.white : new Color(0.45f, 0.8f, 0.3f),
        };

        private Sprite ArtOf(Mole kind) => kind switch
        {
            Mole.Gold => _moleGold,
            Mole.Bomb => _moleBomb,
            _ => _moleNormal,
        };

        /// <summary>빈 구멍 하나에서 두더지 한 마리 - 이미 최대 마릿수면 안 나온다.</summary>
        private void PopUp()
        {
            // 맞은 얼굴을 보여주는 중인 구멍은 비워둔다
            var hidden = _holes.Where(h => !_up.ContainsKey(h) && !_hitShow.ContainsKey(h)).ToList();
            if (_up.Count >= _maxUp || hidden.Count == 0)
                return;
            var hole = Pick(hidden);
            var roll = Rng.NextDouble();
            var kind = roll < _goldChance ? Mole.Gold : roll < _goldChance + _bombChance ? Mole.Bomb : Mole.Normal;
            _up[hole] = (kind, kind == Mole.Gold ? GoldUpSeconds : _upSeconds);
            var renderer = _moles[hole];
            if (_moleArt)
            {
                renderer.sprite = ArtOf(kind);
                renderer.color = Color.white;
            }
            else
                renderer.color = Tint(kind);
            renderer.gameObject.SetActive(true);
            if (_moleArt && HitFeedback.Enabled)
                _popAge[hole] = 0f;
        }

        private void Hide(Vector2Int hole)
        {
            _up.Remove(hole);
            _popAge.Remove(hole);
            _moles[hole].transform.localScale = _baseScale[hole];
            _moles[hole].gameObject.SetActive(false);
        }

        /// <summary>맞은 두더지 - 눈이 X가 된 얼굴을 잠깐 보여주고 숨긴다(그림이 없으면 바로 숨김).</summary>
        private void ShowHit(Vector2Int hole)
        {
            _up.Remove(hole);
            _popAge.Remove(hole);
            if (!_moleArt || _moleHit == null || !HitFeedback.Enabled)
            {
                Hide(hole);
                return;
            }
            var renderer = _moles[hole];
            renderer.sprite = _moleHit;
            renderer.color = Color.white;
            renderer.transform.localScale = Vector3.Scale(_baseScale[hole], new Vector3(1.15f, 0.85f, 1f)); // 납작
            _hitShow[hole] = HitShowSeconds;
        }

        protected override void OnTick(float deltaTime)
        {
            if (!Player.CanAct)
                return; // 레벨업 카드·Esc 메뉴·인벤토리 중엔 시간이 멈춘다
            _elapsed += deltaTime;
            if (_elapsed >= TimeLimit)
            {
                Fail(Loc.T("시간 초과"));
                return;
            }

            // 숨을 때가 된 두더지
            foreach (var hole in _up.Keys.ToList())
            {
                var mole = _up[hole];
                if (mole.Left - deltaTime <= 0f)
                    Hide(hole);
                else
                    _up[hole] = (mole.Kind, mole.Left - deltaTime);
            }
            _nextPop -= deltaTime;
            if (_nextPop <= 0f)
            {
                _nextPop += _popEvery;
                PopUp();
            }

            var seconds = Mathf.CeilToInt(TimeLimit - _elapsed);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                RefreshHud();
            }

            foreach (var hole in _popAge.Keys.ToList())
            {
                var age = _popAge[hole] + deltaTime;
                var t = Mathf.Clamp01(age / PopSeconds);
                // 쏙 올라오며 위로 늘어났다가 제자리
                var stretch = Mathf.Sin(t * Mathf.PI) * 0.18f;
                _moles[hole].transform.localScale = Vector3.Scale(_baseScale[hole], new Vector3(1f - stretch * 0.5f, 1f + stretch, 1f));
                if (t >= 1f)
                {
                    _popAge.Remove(hole);
                    _moles[hole].transform.localScale = _baseScale[hole];
                }
                else
                    _popAge[hole] = age;
            }
            foreach (var hole in _hitShow.Keys.ToList())
            {
                var left = _hitShow[hole] - deltaTime;
                if (left > 0f)
                {
                    _hitShow[hole] = left;
                    continue;
                }
                _hitShow.Remove(hole);
                if (!_up.ContainsKey(hole))
                    Hide(hole);
            }
        }

        public override bool HandleAct(Vector2Int direction)
        {
            var target = Player.GridPos + direction;
            if (!_holes.Contains(target))
                return false;
            PlayPlayerAttack(direction);
            if (_up.TryGetValue(target, out var mole))
            {
                if (mole.Kind == Mole.Bomb)
                {
                    Hide(target);
                    _score = Mathf.Max(0, _score - 2);
                    Fx.Play("explosion", World(target), 1f, fps: 16f);
                    SfxPlayer.Play(Sfx.Explosion);
                    Room.ShowMessage(_moleArt ? Loc.T("폭탄 두더지! 점수 -2") : Loc.T("폭탄 슬라임! 점수 -2"));
                }
                else
                {
                    ShowHit(target);
                    _score += mole.Kind == Mole.Gold ? 3 : 1;
                    Fx.Play(mole.Kind == Mole.Gold ? "coin" : "hit_spark", World(target), 0.9f, fps: 18f);
                    SfxPlayer.Play(mole.Kind == Mole.Gold ? Sfx.Crit : Sfx.Hit);
                    if (_score >= _target)
                    {
                        Succeed();
                        return true;
                    }
                }
            }
            Player.ConsumeTurn(); // 빈 구멍을 쳐도 한 턴(두더지는 턴이 아니라 실제 시간으로 움직인다)
            return true;
        }
    }

    /// <summary>황금 고블린 추격 - 도망 다니는 황금 고블린에 부딪히면 잡는다(5층부터 2마리, 다 잡아야 성공). 고블린은 플레이어에게서
    /// 가장 먼 쪽으로 쉬지 않고 도망치지만 가끔(PanicChance) 허둥대며 엉뚱한 칸으로 간다(같은 속도면 영원히 못 잡으니까 - 사용자 결정,
    /// 예전엔 몇 턴마다 멈춰 쉬었다). 지나간 칸엔 뿌린 끈끈이가 3턴 남고,
    /// 밟으면 발이 묶여 한 턴 쉬어야 한다. 제한 턴을 넘기면 실패.
    /// 맵은 30x30(사용자 요청)이라 판 전체 보기 대신 플레이어를 따라가는 카메라 - 화면 밖 고블린은 가장자리 금색 화살표로 방향을 알린다.</summary>
    public class GoldenChaseMiniGame : PuzzleBase
    {
        public override string Title => Loc.T("황금 고블린 추격");
        public override string Rule => Loc.T("황금 고블린을 구석으로 몰아 부딪혀라(가끔 허둥댄다). 지나간 자리에 뿌린 끈끈이를 밟으면 발이 묶인다!");
        public override string Status => Loc.F("남은 고블린 {0}   남은 턴 {1}", _slimes.Count, Mathf.Max(0, _limit - _turns));

        private const int Size = 30, TrailTurns = 3, CornerFree = 4;
        public override bool FitsCamera => false;
        private const float GoblinSpriteScale = 1.3f; // 48x48 캔버스에 몸이 작아서 고블린 폭탄병과 같은 배율
        private int _limit, _turns, _slimeCount;
        private const float PanicChance = 0.3f; // 이 확률로 최선 대신 아무 빈칸으로(시뮬레이션: 1마리 78턴 안에 약 76% 잡힘)
        private readonly List<Vector2Int> _slimeStarts = new List<Vector2Int>();
        private readonly List<PuzzleActor> _slimes = new List<PuzzleActor>();
        private readonly Dictionary<Vector2Int, (int Turns, GameObject Go)> _trail = new Dictionary<Vector2Int, (int, GameObject)>();

        public override RoomLayout BuildLayout()
        {
            _slimeCount = Stage >= 5 ? 2 : 1;
            // 시뮬레이션(쉬지 않음 + 허둥댐 30%, 단순 추격 봇)에서 약 90% 잡히는 턴 - 1마리 100턴, 2마리 210턴
            _limit = _slimeCount > 1 ? 210 : 100;
            var start = new Vector2Int(Size / 2, Size / 2); // 맵 한가운데서 시작(사용자 요청) - 고블린은 가장 먼 쪽(구석 근처)에
            // 네 구석 4x4는 벽을 안 깐다 - 구석 근처에서 나오는 고블린이 벽에 갇혀 못 도망가는 일이 없게(사용자 지적)
            var corners = new HashSet<Vector2Int>();
            for (var x = 0; x < CornerFree; x++)
            for (var y = 0; y < CornerFree; y++)
            {
                corners.Add(new Vector2Int(x, y));
                corners.Add(new Vector2Int(Size - 1 - x, y));
                corners.Add(new Vector2Int(x, Size - 1 - y));
                corners.Add(new Vector2Int(Size - 1 - x, Size - 1 - y));
            }
            var walls = RandomWalls(Size, Size, 0.1f, start, corners);
            var dist = Distances(start, Size, Size, walls.Contains);
            var far = dist.Max(kv => kv.Value);
            var candidates = dist.Where(kv => kv.Value >= far - 3).Select(kv => kv.Key).ToList();
            Shuffle(candidates);
            _slimeStarts.Add(candidates[0]);
            if (_slimeCount > 1)
            {
                // 두 번째 고블린은 첫 고블린과 멀리(다른 구석) - 붙어 나오면 한 번에 몰려서 재미없다(사용자 지적)
                var wide = dist.Where(kv => kv.Value >= far * 2 / 3).Select(kv => kv.Key).ToList();
                var apart = wide.Max(c => Manhattan(c, _slimeStarts[0]));
                var picks = wide.Where(c => Manhattan(c, _slimeStarts[0]) >= apart - 3).ToList();
                _slimeStarts.Add(picks[Rng.Next(picks.Count)]);
            }
            return new RoomLayout { Width = Size, Height = Size, PlayerStart = start, Walls = walls };
        }

        protected override void OnBegin()
        {
            foreach (var pos in _slimeStarts)
            {
                var go = new GameObject("GoldenGoblin");
                go.transform.SetParent(Room.transform, false);
                Track(go);
                var slime = go.AddComponent<PuzzleActor>();
                // 황금 고블린 도트(Sprites/GoldGoblin, 4방향 대기·걷기) - 없으면 예전 황금빛 슬라임, 그것도 없으면 사각형
                var goblin = SpriteAnimator.FirstFrame("GoldGoblin");
                var first = goblin == null ? SpriteAnimator.FirstFrame("Slime") : null;
                if (goblin != null)
                {
                    var renderer = VisualUtil.CreateSpriteVisual(go, goblin, GridConstants.CellSize * GoblinSpriteScale, sortingOrder: 0);
                    go.AddComponent<SpriteAnimator>().Setup("GoldGoblin", renderer, new Color(1f, 0.8f, 0.25f));
                }
                else if (first != null)
                {
                    var renderer = VisualUtil.CreateSpriteVisual(go, first, GridConstants.CellSize, sortingOrder: 0);
                    go.AddComponent<SpriteAnimator>().Setup("Slime", renderer, new Color(1f, 0.85f, 0.2f));
                    renderer.color = new Color(1f, 0.85f, 0.25f);
                }
                else
                    VisualUtil.CreateSquareVisual(go, new Color(1f, 0.85f, 0.2f), GridConstants.CellSize * 0.6f, sortingOrder: 0);
                Map.PlaceActor(slime, pos);
                _slimes.Add(slime);

                var arrowGo = new GameObject("GoblinArrow");
                arrowGo.transform.SetParent(Room.transform, false);
                Track(arrowGo);
                VisualUtil.CreateTriangleVisual(arrowGo, new Color(1f, 0.82f, 0.2f, 0.95f), ArrowSize, sortingOrder: 30);
                arrowGo.SetActive(false);
                _arrows.Add(arrowGo);
            }
        }

        private readonly List<GameObject> _arrows = new List<GameObject>();
        private static float ArrowSize => GridConstants.CellSize * 0.45f;

        /// <summary>화면 밖으로 나간 고블린 쪽을 가리키는 화살표 - 화면 가장자리 안쪽에 붙어서 깜빡이듯 커졌다 작아진다.</summary>
        protected override void OnTick(float deltaTime)
        {
            var cam = Camera.main;
            for (var i = 0; i < _arrows.Count; i++)
            {
                var arrow = _arrows[i];
                var goblin = i < _slimes.Count ? _slimes[i] : null;
                if (cam == null || goblin == null || !goblin.gameObject.activeSelf)
                {
                    arrow.SetActive(false);
                    continue;
                }
                var center = (Vector2)cam.transform.position;
                var half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
                var target = (Vector2)goblin.transform.position;
                var offset = target - center;
                var margin = GridConstants.CellSize * 0.6f;
                var inner = half - new Vector2(margin, margin);
                if (Mathf.Abs(offset.x) <= half.x - margin * 0.3f && Mathf.Abs(offset.y) <= half.y - margin * 0.3f)
                {
                    arrow.SetActive(false); // 화면 안에 보임
                    continue;
                }
                // 중심→고블린 직선이 안쪽 사각형 테두리와 만나는 점
                var k = Mathf.Min(inner.x / Mathf.Max(0.0001f, Mathf.Abs(offset.x)), inner.y / Mathf.Max(0.0001f, Mathf.Abs(offset.y)));
                arrow.SetActive(true);
                arrow.transform.position = center + offset * k;
                arrow.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg - 90f);
                arrow.transform.localScale = Vector3.one * (ArrowSize * (1f + 0.15f * Mathf.Sin(Time.time * 8f)));
            }
        }

        public override bool HandleAct(Vector2Int direction)
        {
            var slime = _slimes.FirstOrDefault(s => s.GridPos == Player.GridPos + direction);
            if (slime == null)
                return false;
            PlayPlayerAttack(direction);
            Fx.Play("coin", slime.transform.position, 0.6f, fps: 14f, riseCells: 0.9f);
            SfxPlayer.Play(Sfx.Crit);
            Map.RemoveActor(slime);
            slime.gameObject.SetActive(false);
            _slimes.Remove(slime);
            if (_slimes.Count == 0)
            {
                Succeed();
                return true;
            }
            Room.ShowMessage(Loc.T("한 마리 잡았다! 남은 고블린을 쫓아라"));
            Player.ConsumeTurn();
            return true;
        }

        protected override void OnPlayerMoved(Vector2Int pos)
        {
            if (!_trail.ContainsKey(pos))
                return;
            Player.ApplyRoot(1);
            Room.ShowMessage(Loc.T("끈끈이에 발이 묶였다! (Space로 한 턴 쉬기)"));
        }

        protected override void OnTurnEnd()
        {
            _turns++;
            foreach (var cell in _trail.Keys.ToList())
            {
                var t = _trail[cell];
                if (t.Turns <= 1)
                {
                    Untrack(t.Go);
                    _trail.Remove(cell);
                }
                else
                    _trail[cell] = (t.Turns - 1, t.Go);
            }
            if (_turns >= _limit)
            {
                Fail(Loc.T("놓쳤다"));
                return;
            }
            var dist = Distances(Player.GridPos, Size, Size, c => Map.IsWall(c));
            foreach (var slime in _slimes)
            {
                var self = slime;
                // 다른 고블린과 4칸 안으로 붙는 칸은 감점 - 둘이 같은 구석으로 몰려 다니지 않게 흩어진다
                int Crowd(Vector2Int c) => _slimes.Where(o => o != self).Sum(o => Mathf.Max(0, 4 - Manhattan(c, o.GridPos)) * 8);
                int Score(Vector2Int c) => (dist.TryGetValue(c, out var d) ? d : 0) * 10 + Dirs.Count(dd => Map.IsWalkable(c + dd)) - Crowd(c);
                // 점수 ×4 + 무작위 0~3(같은 점수면 무작위로 고름), 제자리는 -4(같은 점수면 움직이는 쪽을 고른다)
                var from = slime.GridPos;
                var best = from;
                var bestScore = Score(best) * 4 - 4 + Rng.Next(4);
                foreach (var d in Dirs)
                {
                    var next = from + d;
                    if (!Map.IsWalkable(next))
                        continue;
                    var score = Score(next) * 4 + Rng.Next(4);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = next;
                    }
                }
                if (Rng.NextDouble() < PanicChance)
                {
                    // 허둥댐 - 갈 수 있는 아무 칸으로(멈추지는 않는다)
                    var options = Dirs.Select(d => from + d).Where(c => Map.IsWalkable(c)).ToList();
                    if (options.Count > 0)
                        best = options[Rng.Next(options.Count)];
                }
                if (best == from)
                    continue;
                Map.MoveActor(slime, best);
                LeaveTrail(from);
            }
        }

        private void LeaveTrail(Vector2Int cell)
        {
            if (_trail.TryGetValue(cell, out var old))
            {
                _trail[cell] = (TrailTurns, old.Go);
                return;
            }
            var go = Square(cell, new Color(0.85f, 0.8f, 0.2f, 0.45f), 0.8f, -7, "GlueTrail").gameObject;
            _trail[cell] = (TrailTurns, go);
        }
    }

    /// <summary>보물 찾기(지뢰찾기 변형) - 보물 상자는 잠겨 있다. 먼저 필드 어딘가의 열쇠를 주운 뒤 오른쪽 위 보물 상자까지 걸어가면 성공.
    /// 숨은 함정을 밟으면 실패(걸음 수 제한은 없앰 - 사용자 요청). 밟은 칸엔 주변 8칸의 함정 수가 뜨고, 0이면 이웃 칸이 저절로 열린다.
    /// 시작 칸 주변은 안전하고, 함정을 피해 열쇠와 보물까지 가는 길이 반드시 있다.</summary>
    public class TreasureSweepMiniGame : PuzzleBase
    {
        public override string Title => Loc.T("보물 찾기");
        public override string Rule => Loc.T("숫자 = 주변 8칸의 함정 수. 먼저 열쇠를 주운 뒤 보물 상자까지 가라.");
        public override string Status => Loc.F("숨은 함정 {0}개   열쇠 {1}", _traps.Count, _hasKey ? Loc.T("있음") : Loc.T("없음"));

        private static readonly Color[] NumberColors =
        {
            Color.white, new Color(0.45f, 0.75f, 1f), new Color(0.5f, 1f, 0.5f), new Color(1f, 0.55f, 0.45f),
            new Color(0.8f, 0.5f, 1f), new Color(1f, 0.8f, 0.3f), new Color(0.4f, 1f, 1f), Color.white, Color.white,
        };

        private int _size;
        private Vector2Int _treasure, _key;
        private bool _hasKey;
        private GameObject _keyObject;
        private HashSet<Vector2Int> _traps = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _revealed = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, SpriteRenderer> _covers = new Dictionary<Vector2Int, SpriteRenderer>();

        public override RoomLayout BuildLayout()
        {
            _size = Stage >= 6 ? 11 : 9;
            var start = Vector2Int.zero;
            _treasure = new Vector2Int(_size - 1, _size - 1);
            var count = Mathf.RoundToInt(_size * _size * (Stage >= 6 ? 0.16f : 0.14f));
            for (var attempt = 0; attempt < 200; attempt++)
            {
                // 열쇠는 왼쪽 위나 오른쪽 아래 구석 근처 - 시작→열쇠→보물로 돌아가야 한다
                var corner = Rng.Next(2) == 0 ? new Vector2Int(0, _size - 1) : new Vector2Int(_size - 1, 0);
                var key = corner + new Vector2Int(corner.x == 0 ? Rng.Next(2) : -Rng.Next(2), corner.y == 0 ? Rng.Next(2) : -Rng.Next(2));
                var traps = new HashSet<Vector2Int>();
                while (traps.Count < count)
                {
                    var p = new Vector2Int(Rng.Next(_size), Rng.Next(_size));
                    if (Mathf.Max(p.x, p.y) <= 1 || p == _treasure || p == key)
                        continue;
                    traps.Add(p);
                }
                var fromStart = Distances(start, _size, _size, traps.Contains);
                var fromKey = Distances(key, _size, _size, traps.Contains);
                if (!fromStart.ContainsKey(key) || !fromKey.ContainsKey(_treasure))
                    continue;
                _traps = traps;
                _key = key;
                break;
            }
            if (_traps.Count == 0)
                _key = new Vector2Int(0, _size - 1); // 안전망 - 함정 없는 판
            return new RoomLayout { Width = _size, Height = _size, PlayerStart = start };
        }

        protected override void OnBegin()
        {
            for (var x = 0; x < _size; x++)
            for (var y = 0; y < _size; y++)
            {
                var p = new Vector2Int(x, y);
                _covers[p] = Square(p, new Color(0.34f, 0.3f, 0.24f), 0.94f, -9, "Cover");
            }
            var chest = FxSprite("event_TreasureChest");
            if (chest != null)
                SpriteAt(World(_treasure), chest, 0.85f, -6, "Treasure").color = new Color(0.6f, 0.6f, 0.65f); // 잠김 - 어둡게
            else
                Square(_treasure, new Color(0.6f, 0.5f, 0.2f), 0.7f, -6, "Treasure");
            // 금 열쇠 도트(Sprites/Props/Key) - 없으면 예전 노란 마름모
            var keyArt = Resources.Load<Sprite>("Sprites/Props/Key");
            if (keyArt != null)
                _keyObject = SpriteAt(World(_key), keyArt, 0.7f, -6, "Key").gameObject;
            else
            {
                var keyRenderer = Square(_key, new Color(1f, 0.85f, 0.25f), 0.38f, -6, "Key");
                keyRenderer.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                _keyObject = keyRenderer.gameObject;
            }
            Reveal(Player.GridPos);
        }

        private int Count(Vector2Int p)
        {
            var n = 0;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                if ((dx != 0 || dy != 0) && _traps.Contains(p + new Vector2Int(dx, dy)))
                    n++;
            return n;
        }

        private void Reveal(Vector2Int from)
        {
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (!InBounds(p, _size, _size) || _traps.Contains(p) || !_revealed.Add(p))
                    continue;
                _covers[p].color = new Color(0.17f, 0.17f, 0.2f);
                var n = Count(p);
                if (n > 0)
                {
                    Label(p, n.ToString(), NumberColors[n]);
                    continue;
                }
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    queue.Enqueue(p + new Vector2Int(dx, dy));
            }
        }

        protected override void OnPlayerMoved(Vector2Int pos)
        {
            if (_traps.Contains(pos))
            {
                foreach (var t in _traps)
                    _covers[t].color = new Color(0.6f, 0.12f, 0.12f);
                Fx.Play("explosion", World(pos), 1.2f, fps: 16f);
                HitFeedback.OnPlayerHurt(Player, null);
                Fail(Loc.T("함정을 밟았다"));
                return;
            }
            if (pos == _key && !_hasKey)
            {
                _hasKey = true;
                Untrack(_keyObject);
                SfxPlayer.Play(Sfx.Appear);
                Room.ShowMessage(Loc.T("열쇠를 주웠다! 이제 보물 상자로"));
            }
            if (pos == _treasure)
            {
                if (_hasKey)
                {
                    Succeed();
                    return;
                }
                Room.ShowMessage(Loc.T("잠겨 있다 - 열쇠가 필요하다"));
            }
            Reveal(pos);
        }
    }

    /// <summary>버티기 - 가운데에서 시작해 사방 끝에서 몹이 계속 몰려온다. 실제 시간 2분을 버티면 성공(사용자 결정: 턴 대신 분),
    /// 남은 몹은 사라짐. 쓰러지면 실패. 턴제라 가만히 서서 시간만 보내지 못하게 AutoTurnSeconds 동안 아무것도 안 하면 저절로 한 턴
    /// (제자리 대기)이 넘어가 몹이 움직인다. 시간은 직접 움직일 수 있을 때만 흐른다(레벨업 카드·Esc 메뉴 중엔 멈춤).
    /// 추가 규칙: 10초마다 낙석(빨간 칸, 두 턴 뒤에 떨어져 최대 체력의 25% - 저절로 넘어가는 턴이 0.8초라 한 턴이면 피할 틈이 없다), 1분이 지나면 몹이 더 자주 몰려오고 정예 1마리가 나온다.
    /// 잡은 몹은 골드·경험치 없음. 대시·회전 베기·아이템 사용 가능.</summary>
    public class SurvivalMiniGame : PuzzleBase
    {
        public override bool AllowsTools => true;
        public override string Title => Loc.T("버티기");
        public override string Rule => Loc.T("2분을 버텨라. 가만히 있어도 몹은 움직인다. 빨간 칸엔 돌이 떨어지고, 1분이 지나면 더 거세진다!");
        public override string Status
        {
            get
            {
                var left = Mathf.CeilToInt(Mathf.Max(0f, Duration - _elapsed));
                return Loc.F("남은 시간 {0}:{1:00}", left / 60, left % 60);
            }
        }

        private const int Size = 11; // 몹 수 제한 없음(사용자 결정) - 가장자리 빈 칸이 다 차면 그 웨이브는 들어갈 자리만큼만
        private const float Duration = 120f;       // 2분
        private const float AutoTurnSeconds = 0.8f; // 이만큼 아무것도 안 하면 저절로 한 턴
        private const float WaveSeconds = 6f;       // 몹이 몰려오는 간격(처음 1분)
        private const float LateWaveSeconds = 4f;   // 1분 뒤
        private const float RockSeconds = 10f;      // 낙석 간격
        private const float RockDamageRate = 0.25f;
        private int _perWave;
        private float _elapsed, _idle, _nextWave, _nextRock;
        private bool _eliteSpawned;
        private int _rockTurns; // 낙석까지 남은 턴
        private int _shownSeconds = -1;
        private readonly List<Vector2Int> _rocks = new List<Vector2Int>();
        private readonly List<GameObject> _rockWarnings = new List<GameObject>();

        public override RoomLayout BuildLayout()
        {
            _perWave = 1 + Stage / 4;
            var start = new Vector2Int(Size / 2, Size / 2);
            return new RoomLayout { Width = Size, Height = Size, PlayerStart = start, Walls = RandomWalls(Size, Size, 0.06f, start) };
        }

        protected override void OnBegin()
        {
            SpawnWave();
            _nextWave = WaveSeconds;
            _nextRock = 8f;
        }

        private List<Vector2Int> EdgeCells()
        {
            var edges = new List<Vector2Int>();
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var c = new Vector2Int(x, y);
                var edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
                if (edge && Map.IsWalkable(c) && Manhattan(c, Player.GridPos) >= 3)
                    edges.Add(c);
            }
            Shuffle(edges);
            return edges;
        }

        private void SpawnWave()
        {
            var kinds = Room.CurrentDefinition?.EnemyKinds;
            foreach (var c in EdgeCells().Take(_perWave))
            {
                var kind = Stage >= 5 && kinds != null && kinds.Count > 0 ? Pick(kinds) : EnemyKind.Melee;
                Room.SpawnPuzzleEnemy(c, kind, 0.5f, 0.7f);
                HitFeedback.OnSummon(World(c));
            }
        }

        private void SpawnElite()
        {
            var cells = EdgeCells();
            if (cells.Count == 0)
                return;
            var cell = cells[0];
            var elite = Room.SpawnPuzzleEnemy(cell, EnemyKind.Melee, 1.5f, 1f);
            elite.MarkAsElite();
            HitFeedback.OnSummon(World(cell));
            Room.ShowMessage(Loc.T("정예 몹이 나타났다!"));
        }

        /// <summary>낙석 예고 - 플레이어 칸을 포함해 빈 칸 몇 개를 빨갛게. 두 턴 뒤 턴 끝에 떨어진다.</summary>
        private void DropRocks()
        {
            foreach (var go in _rockWarnings)
                Untrack(go);
            _rockWarnings.Clear();
            _rocks.Clear();
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var c = new Vector2Int(x, y);
                if (!Map.IsWall(c) && c != Player.GridPos)
                    cells.Add(c);
            }
            Shuffle(cells);
            _rocks.Add(Player.GridPos); // 가만히 있으면 맞는다
            _rocks.AddRange(cells.Take(4 + Stage / 3));
            foreach (var c in _rocks)
                _rockWarnings.Add(Track(Room.SpawnWarningTile(c, new Color(1f, 0.35f, 0.15f, 0.5f))));
            _rockTurns = 2;
            _idle = 0f; // 예고가 뜬 순간부터 저절로 넘어가는 턴을 다시 센다(피할 틈)
            HitFeedback.OnTelegraph();
        }

        private void ResolveRocks()
        {
            foreach (var go in _rockWarnings)
                Untrack(go);
            _rockWarnings.Clear();
            foreach (var c in _rocks)
                Fx.Play("hit_spark", World(c), 1f, fps: 18f);
            var hit = _rocks.Contains(Player.GridPos);
            _rocks.Clear();
            if (!hit)
                return;
            var dealt = Player.Stats.TakeIncomingDamage(Player.Stats.MaxHealth * RockDamageRate);
            DamagePopup.Spawn(Player.transform.position, dealt, new Color(1f, 0.5f, 0.2f), isCritical: true);
            HitFeedback.OnPlayerHurt(Player, null);
            if (Player.Stats.IsDead)
                PlayerKilled();
        }

        protected override void OnTurnEnd()
        {
            _idle = 0f; // 직접 움직였든 저절로 넘어갔든 한 턴이 지나면 다시 센다
            if (_rocks.Count > 0 && --_rockTurns <= 0)
                ResolveRocks();
        }

        protected override void OnTick(float deltaTime)
        {
            if (!Player.CanAct)
                return; // 레벨업 카드·Esc 메뉴·인벤토리 중엔 시간이 멈춘다
            _elapsed += deltaTime;
            if (_elapsed >= Duration)
            {
                Succeed();
                return;
            }
            if (_elapsed >= _nextWave)
            {
                _nextWave += _elapsed >= Duration / 2f ? LateWaveSeconds : WaveSeconds;
                SpawnWave();
            }
            if (!_eliteSpawned && _elapsed >= Duration / 2f)
            {
                _eliteSpawned = true;
                SpawnElite();
            }
            if (_elapsed >= _nextRock && _rocks.Count == 0)
            {
                _nextRock += RockSeconds;
                DropRocks();
            }
            _idle += deltaTime;
            if (_idle >= AutoTurnSeconds)
                Player.ConsumeTurn(); // 가만히 있으면 제자리 대기로 한 턴(몹이 다가온다)

            var seconds = Mathf.CeilToInt(Duration - _elapsed);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                RefreshHud();
            }
        }
    }
}
