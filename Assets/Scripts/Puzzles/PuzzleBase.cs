using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>퍼즐 5종 + 미니게임 5종 - 순서는 PuzzleBase.Create와 맞춘다(뒤 5개가 미니게임).</summary>
    public enum PuzzleType
    {
        IceSlide,        // 얼음 미끄럼
        TrapCorridor,    // 함정 복도
        RuneOrder,       // 룬 순서
        BoxPush,         // 상자 밀기
        CombatChallenge, // 전투 퍼즐
        ShellGame,       // 상자 섞기
        WhackSlime,      // 두더지 잡기
        GoldenChase,     // 황금 고블린 추격
        TreasureSweep,   // 보물 찾기
        Survival,        // 버티기
    }

    /// <summary>퍼즐·미니게임 방 하나(사용자 결정: 일반 방에 들어갈 때 Chance 확률로 전투 대신 나온다, 성공하면 매번 레벨업 카드 1장,
    /// 실패하면 다시 도전 / 이전 방으로 중 고른다). RoomController.LoadPuzzle이 BuildLayout으로 방을 깔고 Begin을 부른 뒤,
    /// 플레이어 행동(HandleAct, PlayerMoved)과 턴 끝(TurnEnd)·몹 처치(EnemyDefeated)를 넘겨준다. 퍼즐이 만든 오브젝트는 Track으로
    /// 등록해두면 방을 떠날 때 같이 지워진다. 자동 플레이 봇에겐 퍼즐방이 안 나온다(밸런스 측정 기준 유지).</summary>
    public abstract class PuzzleBase
    {
        public const float Chance = 0.15f; // 25% -> 15%(사용자 결정)
        public const int TypeCount = 10;

        public static PuzzleBase Create(PuzzleType type) => type switch
        {
            PuzzleType.IceSlide => new IceSlidePuzzle(),
            PuzzleType.TrapCorridor => new TrapCorridorPuzzle(),
            PuzzleType.RuneOrder => new RuneOrderPuzzle(),
            PuzzleType.BoxPush => new BoxPushPuzzle(),
            PuzzleType.CombatChallenge => new CombatChallengePuzzle(),
            PuzzleType.ShellGame => new ShellGameMiniGame(),
            PuzzleType.WhackSlime => new WhackSlimeMiniGame(),
            PuzzleType.GoldenChase => new GoldenChaseMiniGame(),
            PuzzleType.TreasureSweep => new TreasureSweepMiniGame(),
            _ => new SurvivalMiniGame(),
        };

        public static bool IsMiniGame(PuzzleType type) => type >= PuzzleType.ShellGame;

        /// <summary>에디터 메뉴 "퍼즐 테스트 모드"(EditorPrefs) - 켜져 있으면 일반 방마다 퍼즐이 1번부터 차례로 나온다(확률 무시).</summary>
        public const string TestModePrefKey = "LoopRogue_PuzzleTestMode";
        private static int _testIndex;

        public static bool TestModeNext(out PuzzleType type)
        {
            type = default;
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.GetBool(TestModePrefKey, false))
                return false;
            type = (PuzzleType)(_testIndex++ % TypeCount);
            return true;
#else
            return false;
#endif
        }

        protected RoomController Room { get; private set; }
        protected PlayerActor Player { get; private set; }
        protected int Stage { get; private set; }
        protected System.Random Rng { get; private set; }
        protected GridMap Map => Room.Map;
        public PuzzleType Type { get; private set; }

        private bool _done;
        public bool IsRunning => !_done;

        /// <summary>대시·회전 베기·아이템을 쓸 수 있는지 - 몹과 싸우는 것(전투 퍼즐·버티기)만 true.</summary>
        public virtual bool AllowsTools => false;

        /// <summary>true면 판 전체가 한 화면에 들어오게 카메라가 물러난다(판을 보고 계획하는 퍼즐). false면 일반 방처럼 플레이어를 따라간다(넓은 맵).</summary>
        public virtual bool FitsCamera => true;

        public abstract string Title { get; }
        public abstract string Rule { get; }
        /// <summary>안내 패널 셋째 줄(남은 턴 등) - 턴마다 다시 읽는다.</summary>
        public virtual string Status => string.Empty;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private float _hpAtStart;
        private PuzzleHud _hud;
        private Coroutine _intro;

        public void Init(RoomController room, PlayerActor player, int stage, System.Random rng, PuzzleType type)
        {
            Room = room;
            Player = player;
            Stage = stage;
            Rng = rng;
            Type = type;
        }

        /// <summary>방 크기·벽·시작 칸 - 퍼즐 내용(정답 칸 등)도 여기서 같이 정해둔다.</summary>
        public abstract RoomLayout BuildLayout();

        public void Begin()
        {
            _hpAtStart = Player.Stats.CurrentHealth;
            _hud = PuzzleHud.Create(this);
            OnBegin();
            RefreshHud();
        }

        protected abstract void OnBegin();

        /// <summary>방향키 행동을 퍼즐이 직접 처리하면 true(턴을 쓰려면 Player.ConsumeTurn). false면 평소처럼 이동/공격.</summary>
        public virtual bool HandleAct(Vector2Int direction) => false;

        /// <summary>퍼즐 전용 키(상자 밀기 [Z] 되돌리기 등) - 처리했으면 true. 안내 패널에 같은 키 버튼이 생긴다(ExtraKeyLabel).</summary>
        public virtual bool HandleKeys() => false;
        public virtual string ExtraKeyLabel => null;
        public virtual UnityEngine.InputSystem.Key ExtraKey => UnityEngine.InputSystem.Key.None;

        public void PlayerMoved(Vector2Int pos)
        {
            if (IsRunning)
                OnPlayerMoved(pos);
        }

        protected virtual void OnPlayerMoved(Vector2Int pos) { }

        public void TurnEnd()
        {
            if (!IsRunning)
                return;
            OnTurnEnd();
            if (IsRunning)
                RefreshHud();
        }

        protected virtual void OnTurnEnd() { }

        /// <summary>매 프레임(PuzzleHud.Update가 부른다) - 실제 시간을 쓰는 미니게임(버티기)만.</summary>
        public void Tick(float deltaTime)
        {
            if (IsRunning)
                OnTick(deltaTime);
        }

        protected virtual void OnTick(float deltaTime) { }

        public void EnemyDefeated(EnemyActor enemy)
        {
            if (IsRunning)
                OnEnemyDefeated(enemy);
        }

        protected virtual void OnEnemyDefeated(EnemyActor enemy) { }

        /// <summary>퍼즐 안에서 체력이 0이 되면 죽는 대신 실패(체력은 퍼즐 시작 때로 돌아간다).</summary>
        public void PlayerKilled() => Fail(Loc.T("쓰러졌다!"));

        public void GiveUp()
        {
            if (IsRunning)
                Fail(Loc.T("포기했다"));
        }

        protected void Succeed()
        {
            if (!IsRunning)
                return;
            _done = true;
            StopIntro();
            Room.CompletePuzzle(this, _objects); // 퍼즐 그림은 방에 넘겨서 방을 떠날 때 지운다
            _objects.Clear();
            DisposeHud();
        }

        protected void Fail(string reason)
        {
            if (!IsRunning)
                return;
            _done = true;
            StopIntro();
            if (Player.Stats.CurrentHealth < _hpAtStart)
                Player.Stats.CurrentHealth = _hpAtStart;
            Room.FailPuzzle(this, reason);
        }

        public void ShowFail(string reason, string backLabel, Action retry, Action back) => _hud?.ShowFail(reason, backLabel, retry, back);

        protected void RefreshHud() => _hud?.Refresh();

        public void Dispose()
        {
            StopIntro();
            foreach (var go in _objects)
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            _objects.Clear();
            DisposeHud();
        }

        private void DisposeHud()
        {
            if (_hud != null)
                UnityEngine.Object.Destroy(_hud.gameObject);
            _hud = null;
        }

        // ---- 시작 연출(룬 순서 보여주기, 상자 섞기) - 그동안 입력을 잠근다 ----

        protected bool InIntro => _intro != null;

        protected void RunIntro(IEnumerator routine)
        {
            Room.IsInputLocked = true;
            _intro = Room.StartCoroutine(IntroWrapper(routine));
        }

        private IEnumerator IntroWrapper(IEnumerator routine)
        {
            yield return routine;
            _intro = null;
            if (!IsRunning)
                yield break;
            Room.IsInputLocked = false;
            RefreshHud();
        }

        private void StopIntro()
        {
            if (_intro == null)
                return;
            Room.StopCoroutine(_intro);
            _intro = null;
        }

        // ---- 그림 도우미 ----

        protected static Vector3 World(Vector2Int p, float z = 0f) => new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, z);

        protected GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
        }

        protected void Untrack(GameObject go)
        {
            _objects.Remove(go);
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }

        /// <summary>칸 위 단색 사각형(size = 칸 대비). 바닥 -10, 벽 -8, 퍼즐 바닥 -9, 표시물 -7, 액터 0 이상.</summary>
        protected SpriteRenderer Square(Vector2Int p, Color color, float size, int order, string name = "PuzzleTile")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Room.transform, false);
            var renderer = VisualUtil.CreateSquareVisual(go, color, GridConstants.CellSize * size, order);
            go.transform.position = World(p);
            Track(go);
            return renderer;
        }

        /// <summary>그림 한 장을 가로 sizeCells칸 크기로(그림 해상도와 상관없이).</summary>
        protected SpriteRenderer SpriteAt(Vector3 world, Sprite sprite, float sizeCells, int order, string name = "PuzzleSprite")
        {
            var go = new GameObject(name);
            go.transform.SetParent(Room.transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            var width = sprite.rect.width / sprite.pixelsPerUnit;
            go.transform.localScale = Vector3.one * (GridConstants.CellSize * sizeCells / Mathf.Max(0.0001f, width));
            go.transform.position = world;
            Track(go);
            return renderer;
        }

        protected TextMesh Label(Vector2Int p, string text, Color color, float characterSize = 0.07f, int order = 4)
        {
            var go = new GameObject("PuzzleLabel");
            go.transform.SetParent(Room.transform, false);
            go.transform.position = World(p, -0.4f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = mesh.font.material;
            renderer.sortingOrder = order;
            Track(go);
            return mesh;
        }

        /// <summary>이펙트 그림(Resources/Effects/{name}/0) 첫 장 - 없으면 null.</summary>
        protected static Sprite FxSprite(string name)
        {
            var frames = Fx.Frames(name);
            return frames.Length > 0 ? frames[0] : null;
        }

        /// <summary>플레이어 공격 모션(그쪽을 바라보며) - 퍼즐에서 상자를 열거나 두더지를 칠 때.</summary>
        protected void PlayPlayerAttack(Vector2Int direction)
        {
            if (Player.TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(direction);
        }

        // ---- 격자 도우미 ----

        protected static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        protected static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        protected static bool InBounds(Vector2Int p, int width, int height) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;

        protected T Pick<T>(IList<T> list) => list[Rng.Next(list.Count)];

        protected void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>start에서 4방향으로 갈 수 있는 칸(blocked 제외) - BFS 거리.</summary>
        protected static Dictionary<Vector2Int, int> Distances(Vector2Int start, int width, int height, Func<Vector2Int, bool> blocked)
        {
            var dist = new Dictionary<Vector2Int, int> { [start] = 0 };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var next = cur + d;
                    if (!InBounds(next, width, height) || dist.ContainsKey(next) || blocked(next))
                        continue;
                    dist[next] = dist[cur] + 1;
                    queue.Enqueue(next);
                }
            }
            return dist;
        }

        /// <summary>방 면적의 density만큼 벽을 뿌린다 - 벽이 아닌 모든 칸이 start에서 이어지고 keepFree 칸은 비워둔다.</summary>
        protected HashSet<Vector2Int> RandomWalls(int width, int height, float density, Vector2Int start, ICollection<Vector2Int> keepFree = null)
        {
            var count = Mathf.RoundToInt(width * height * density);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                var walls = new HashSet<Vector2Int>();
                for (var tries = 0; walls.Count < count && tries < count * 10; tries++)
                {
                    var p = new Vector2Int(Rng.Next(width), Rng.Next(height));
                    if (p == start || Manhattan(p, start) <= 1 || (keepFree != null && keepFree.Contains(p)))
                        continue;
                    walls.Add(p);
                }
                var reach = Distances(start, width, height, walls.Contains);
                if (reach.Count == width * height - walls.Count)
                    return walls;
            }
            return new HashSet<Vector2Int>();
        }
    }
}
