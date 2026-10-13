using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 하나의 청사진 - 기본 크기/몹 수·종류/스탯. 실제 배치(크기 ±1, 벽, 몹 위치, 이벤트 칸)는 방을 깔
    /// 때마다 RoomLayoutGenerator가 새로 뽑는다(시도할 때마다 다른 방).</summary>
    public class RoomDefinition
    {
        public string RoomName;
        /// <summary>층 안에서 몇 번째 방인지(1부터, 보스방이 마지막) - LoopManager가 채운다. 모험가 NPC가 방 1/방 10을 구분할 때 쓴다.</summary>
        public int RoomNumber;
        public int Width;
        public int Height;
        public int EnemyCount;
        /// <summary>몹 종류 - 가까운 순으로 놓인 몹 위치와 같은 순서(뒤쪽 = 먼 쪽이 궁수). 짧으면 나머지는 근접.</summary>
        public List<EnemyKind> EnemyKinds = new List<EnemyKind>();
        public float EnemyMaxHealth;
        public float EnemyAttackPower;
        public bool IsBossRoom;
        public List<BossPatternType> BossPatterns = new List<BossPatternType>();

        /// <summary>이번 시도에 이 방에 이벤트 칸이 있는지 - LoopManager가 시도마다 스테이지당 한 방만 켠다.</summary>
        public bool HasEvent;
        public RoomEventType EventType;

        /// <summary>테스트 맵 방(TestMap) - 정해진 배치에 이벤트 11종을 전부 깐다.</summary>
        public bool IsTestRoom;
        /// <summary>튜토리얼 스테이지 방(TutorialStage) - 배치 고정(TutorialLayout), 몹 보상 없음, 층 진행·이벤트·퍼즐·오류방·히든 몹과 무관.</summary>
        public bool IsTutorialRoom;
        public int TutorialStep;
        public RoomLayout TutorialLayout;
    }

    /// <summary>방 하나의 실제 상태(격자/벽/적 목록/이벤트/바닥 시각화/보스 예고 칸)를 관리하고, 적 전멸 시 다음 방
    /// 전환 또는 보스 격파를 LoopManager에 알린다. 방을 옮길 때마다 이전 방 내용을 지우고 같은 오브젝트를 재사용한다.</summary>
    public class RoomController : MonoBehaviour
    {
        public GridMap Map { get; private set; }
        public bool IsInputLocked { get; set; }

        /// <summary>궁수는 근접 몹 대비 체력/공격력 비율 - 멀리서 때리는 대신 약하다.</summary>
        private const float RangedHealthRatio = 0.6f;
        private const float RangedAttackRatio = 0.8f;

        /// <summary>방패병은 단단하고(정면은 피해 80% 감소) 조금 약하게 때린다. 거미는 궁수처럼 약한 대신 거미줄로 묶는다.</summary>
        private const float ShieldHealthRatio = 1.3f;
        private const float ShieldAttackRatio = 0.9f;
        private const float SpiderHealthRatio = 0.7f;
        private const float SpiderAttackRatio = 0.8f;
        /// <summary>폭발병은 약간 약한 대신 폭발(공격력 ×3)이 아프다.</summary>
        private const float BomberHealthRatio = 0.8f;
        private const float BomberAttackRatio = 1f;
        /// <summary>5층부터: 치유사는 약하고(먼저 잡아야 할 몹), 돌진병은 단단한 대신 돌진(공격력 ×2)이 예고된다,
        /// 분열체는 단단하고 죽으면 둘로 갈라진다, 도적는 약한 대신 등 뒤로 순간이동해 기습(×1.5)한다.</summary>
        private const float HealerHealthRatio = 0.7f;
        private const float HealerAttackRatio = 0.6f;
        private const float ChargerHealthRatio = 1.2f;
        private const float ChargerAttackRatio = 1f;
        private const float SplitterHealthRatio = 1.2f;
        private const float SplitterAttackRatio = 0.9f;
        private const float AssassinHealthRatio = 0.7f;
        private const float AssassinAttackRatio = 1.1f;

        private const int GoldPerKill = 4;       // "쓸 곳은 많은데 수급이 부족하다" 피드백으로 전부 2배(2/20/5 → 4/40/10)
        private const int GoldPerBossKill = 40;
        private const int GoldPerRoomClear = 10;

        /// <summary>일반 방을 클리어하면 최대체력의 이 비율만큼 회복 - 방 사이 회복이 전혀 없어서 한 스테이지 뒤쪽 방(7~10)으로
        /// 갈수록 깎인 체력을 그대로 들고 싸우다 죽던 문제(봇 100판에서 10층 일반 방 사망이 보스 사망의 약 30%) 대책.</summary>
        private const float RoomClearHealRate = 0.1f;

        /// <summary>카메라에 보이는 높이의 절반(칸) - 화면 전체 세로로 약 11칸. 시야(FogOfWar.VisionRadius 4.5칸)보다 조금 넓게.</summary>
        private const float CameraViewHalfHeightCells = 5.5f;

        /// <summary>HUD 띠만큼 카메라 영역(cam.rect)이 줄어도 칸이 화면에서 같은 크기로 보이게, 보이는 높이도 같은 비율로 줄인다
        /// (위아래로 보이는 칸 수가 줄어드는 대신 칸 크기 유지 - 사용자 선택). GameHUD.ApplyCameraViewport도 이걸 쓴다.</summary>
        public static float CameraOrthoSize(Camera cam)
        {
            var size = CameraViewHalfHeightCells * GridConstants.CellSize * cam.rect.height;
            if (!_fitRoom.HasValue)
                return size;
            // 퍼즐·미니게임 방 - 판 전체를 보고 계획해야 해서 방이 화면보다 크면 방 전체(+반 칸 여백)가 들어오게 물러난다
            var room = _fitRoom.Value;
            var cs = GridConstants.CellSize;
            var fit = Mathf.Max((room.y + 1f) * 0.5f * cs, (room.x + 1f) * 0.5f * cs / Mathf.Max(0.1f, cam.aspect));
            return Mathf.Max(size, fit);
        }

        /// <summary>지금 방이 퍼즐이면 그 크기(카메라가 전체를 비추게) - 일반 방은 null.</summary>
        private static Vector2Int? _fitRoom;

        private static readonly System.Random Rng = new System.Random();
        private static readonly Color TelegraphColor = new Color(1f, 0.15f, 0.15f, 0.45f);
        private static readonly Color WallColor = new Color(0.38f, 0.36f, 0.34f);

        /// <summary>자동 플레이 봇 통계용.</summary>
        public static int EventTriggeredCount;
        public static RoomEventType LastEventType;

        private readonly List<EnemyActor> _enemies = new List<EnemyActor>();
        private readonly List<GameObject> _roomObjects = new List<GameObject>();     // 바닥/벽
        private readonly List<GameObject> _telegraphObjects = new List<GameObject>();
        private readonly HashSet<Vector2Int> _bossDangerTiles = new HashSet<Vector2Int>();
        /// <summary>불 붙은 폭발병마다 예고 칸 - 보스 예고와 따로 지운다(보스 예고 발동이 폭발 예고를 지우면 안 됨).</summary>
        private readonly Dictionary<EnemyActor, (List<Vector2Int> Tiles, List<GameObject> Objects)> _bombs =
            new Dictionary<EnemyActor, (List<Vector2Int>, List<GameObject>)>();
        /// <summary>보스 예고 + 폭발 예고 합집합(봇 회피/맵 표시용).</summary>
        private readonly HashSet<Vector2Int> _dangerTiles = new HashSet<Vector2Int>();

        private static readonly Color BombTelegraphColor = new Color(1f, 0.6f, 0.1f, 0.45f);

        /// <summary>자동 플레이 봇 통계용 - 폭발 횟수, 그중 플레이어 적중.</summary>
        public static int BombExplosionCount;
        /// <summary>봇 통계용 - 방 클리어 횟수와 마지막 클리어 방, 그 순간 체력 비율(클리어 회복 받기 전).</summary>
        public static int RoomClearCount;
        public static string LastClearedRoom;
        public static bool LastClearedWasBoss;
        public static float LastClearHpFraction;
        public static int ExitUseCount;

        private static readonly Color ExitColor = new Color(0.3f, 0.75f, 1f, 0.9f);

        /// <summary>몹을 다 잡은 일반 방의 출구 칸 - 없으면 null.</summary>
        public Vector2Int? ExitPosition { get; private set; }
        /// <summary>출구 칸 그림 - 안개(FogOfWar)가 직접 본 뒤에만 보이게 켜고 끈다.</summary>
        public SpriteRenderer ExitRenderer { get; private set; }

        // ---- 아이템이 방에 남기는 것 ----
        /// <summary>미끼(허수아비) - 있으면 몹들이 플레이어 대신 노린다.</summary>
        public DecoyActor ActiveDecoy { get; private set; }
        private readonly List<Vector2Int> _torches = new List<Vector2Int>();
        public IReadOnlyList<Vector2Int> Torches => _torches;
        private readonly Dictionary<Vector2Int, GameObject> _traps = new Dictionary<Vector2Int, GameObject>();
        private readonly List<(Vector2Int Center, List<GameObject> Objects)> _playerBombs = new List<(Vector2Int, List<GameObject>)>();
        private readonly Dictionary<Vector2Int, GameObject> _wallObjects = new Dictionary<Vector2Int, GameObject>();
        private static readonly Color PlayerBombColor = new Color(1f, 0.85f, 0.2f, 0.4f);
        public static int BombPlayerHitCount;
        private readonly List<RoomEventActor> _events = new List<RoomEventActor>(); // 보통 방마다 하나, 테스트 맵은 11개
        private NpcActor _npc;
        private PlayerActor _player;
        private LoopManager _loopManager;
        private RoomDefinition _current;
        private int _stage;

        public int Stage => _stage;
        public IReadOnlyList<EnemyActor> Enemies => _enemies;
        public PlayerActor Player => _player;
        public string RoomName => _current?.RoomName;
        public bool IsBossRoom => _current != null && _current.IsBossRoom;

        /// <summary>예고된 공격 칸(보스 예고 + 불 붙은 폭발병 3x3, 다음 그 몹 턴에 발동) - 비어 있으면 예고 중인 공격 없음.</summary>
        public IReadOnlyCollection<Vector2Int> DangerTiles => _dangerTiles;
        public bool IsDangerTile(Vector2Int pos) => _dangerTiles.Contains(pos);

        // ===================== 퍼즐·미니게임 방 =====================

        /// <summary>지금 방이 퍼즐·미니게임이면 그 퍼즐(성공하면 null) - PlayerActor가 행동을 넘기고, 몹 턴 끝·처치·사망을 여기서 넘긴다.</summary>
        public PuzzleBase Puzzle { get; private set; }

        /// <summary>지금 방 청사진(퍼즐이 그 방 몹 종류·세기를 빌려 쓴다).</summary>
        public RoomDefinition CurrentDefinition => _current;

        /// <summary>봇·통계용 - 퍼즐 성공/실패 누적.</summary>
        public static int PuzzleClearCount, PuzzleFailCount;

        /// <summary>전투 대신 퍼즐로 방을 깐다(안개 없음, 몹·이벤트 없음). 같은 퍼즐을 다시 도전할 때도 이걸 다시 부른다(배치는 새로).</summary>
        public void LoadPuzzle(RoomDefinition def, PuzzleType type)
        {
            _current = def;
            ClearRoom();

            var puzzle = PuzzleBase.Create(type);
            puzzle.Init(this, _player, _stage, Rng, type);
            var layout = puzzle.BuildLayout();
            Map = new GridMap(layout.Width, layout.Height);
            foreach (var wall in layout.Walls)
                Map.AddWall(wall);
            BuildFloor(layout, false);
            _fitRoom = puzzle.FitsCamera ? new Vector2Int(layout.Width, layout.Height) : (Vector2Int?)null;
            Map.PlaceActor(_player, layout.PlayerStart);
            if (_player.TryGetComponent<SpriteAnimator>(out var playerAnim))
                playerAnim.SnapToGrid();
            PositionCamera(layout);

            _thornsKills.Clear();
            _challengeEnemies.Clear();
            _roomCleared = false;
            Puzzle = puzzle;
            if (puzzle.AllowsTools)
                _player.OnRoomEntered(); // 갑옷 보호막 등 - 몹과 싸우는 퍼즐만(허수아비 유물이 퍼즐 칸을 막지 않게)
            IsInputLocked = false;
            puzzle.Begin();
        }

        /// <summary>퍼즐에 실패하고 "이전 방으로"를 골랐을 때 - 그 방을 몹 없이 출구만 열린 채로 깐다.</summary>
        public void LoadClearedRoom(RoomDefinition def)
        {
            _loadCleared = true;
            try { LoadRoom(def); }
            finally { _loadCleared = false; }
        }

        private bool _loadCleared;

        /// <summary>퍼즐 몹 - 그 방 몹 세기 × 배율, 골드·경험치·아이템 없음.</summary>
        public EnemyActor SpawnPuzzleEnemy(Vector2Int pos, EnemyKind kind, float healthScale, float attackScale)
        {
            var enemy = SpawnEnemy(pos, _current, kind, healthScale, attackScale);
            enemy.MarkAsPuzzleMob();
            return enemy;
        }

        /// <summary>예고 칸 그림(보스·폭발과 같은 깜빡이는 칸) - 지우는 건 호출부가.</summary>
        public GameObject SpawnWarningTile(Vector2Int tile, Color color) => SpawnTelegraphTile(tile, color, -5);

        /// <summary>퍼즐 성공 - 레벨업 카드 1장(사용자 결정: 매번), 출구가 열리고 시작 방 고르기에서 다음 방이 열린다. 남은 퍼즐 몹은 사라진다.</summary>
        public void CompletePuzzle(PuzzleBase puzzle, List<GameObject> objects)
        {
            if (Puzzle != puzzle)
                return;
            Puzzle = null;
            foreach (var enemy in _enemies)
            {
                if (enemy == null)
                    continue;
                Map.RemoveActor(enemy);
                Destroy(enemy.gameObject);
            }
            _enemies.Clear();
            ClearAllTelegraphs();
            _roomObjects.AddRange(objects);
            _roomCleared = true;
            PuzzleClearCount++;
            if (!_current.IsTestRoom)
                StageProgress.MarkRoomCleared(_current.RoomNumber);
            var rewards = GrantPuzzleRewards(puzzle.Type);
            if (rewards.Count > 0)
                HitFeedback.OnGold(_player.transform.position);
            ShowMessage(rewards.Count > 0
                ? Loc.F("성공! {0}, 레벨업 카드 1장", string.Join(", ", rewards))
                : Loc.T("성공! 레벨업 카드 1장"));
            HitFeedback.OnBonusReward(_player);
            _player.Levels.GrantBonusUpgrade();
            SpawnExit();
        }

        /// <summary>자동 플레이 봇 - 퍼즐방이 걸리면 성공으로 친다(레벨업 카드 1장, 시작 방 고르기 다음 방 열림). 방은 깔지 않는다 -
        /// 호출부(LoopManager)가 곧바로 다음 방을 깐다.</summary>
        public void SkipPuzzleAsCleared(RoomDefinition def)
        {
            PuzzleClearCount++;
            if (!def.IsTestRoom)
                StageProgress.MarkRoomCleared(def.RoomNumber);
            // 사람과 같은 보상 - 종류를 굴려서 골드·영약 주는 퍼즐이면 그것도(봇 밸런스 측정이 실제와 맞게)
            GrantPuzzleRewards((PuzzleType)Rng.Next(PuzzleBase.TypeCount));
            _player.Levels.GrantBonusUpgrade();
        }

        /// <summary>퍼즐 종류별 성공 보상(골드·영약, PuzzleBase.BonusGold 등)을 주고, 메시지에 넣을 항목들을 돌려준다(없으면 빈 목록).</summary>
        private List<string> GrantPuzzleRewards(PuzzleType type)
        {
            var parts = new List<string>();
            var gold = PuzzleBase.BonusGold(type, _stage);
            if (gold > 0)
            {
                GoldWallet.Add(gold);
                parts.Add(Loc.F("골드 +{0}", gold));
            }
            var potions = new List<PotionType>();
            if (PuzzleBase.GivesPotions(type))
            {
                potions.Add(PotionType.Attack);
                potions.Add(PotionType.Health);
            }
            if (PuzzleBase.GivesCriticalPotion(type))
                potions.Add(CriticalOrAttack());
            // 같은 영약이 두 번이면(치명타가 아직 안 열려 공격력으로 바뀐 경우) "+2"로 묶는다
            foreach (var group in potions.GroupBy(p => p))
            {
                foreach (var potion in group)
                    GrantPotionNow(potion);
                parts.Add(Loc.F("{0} 영약 +{1}", StatPotionWallet.Name(group.Key), group.Count()));
            }
            if (potions.Count > 0)
                parts[parts.Count - 1] += " " + Loc.T("(영구)"); // 마지막 영약 뒤에 한 번만
            return parts;
        }

        /// <summary>치명타 영약이 열렸으면(3스테이지부터) 치명타, 아니면 공격력.</summary>
        private static PotionType CriticalOrAttack() =>
            StatPotionWallet.IsUnlocked(PotionType.Critical) ? PotionType.Critical : PotionType.Attack;

        /// <summary>영약 1개 - 영구로 저장하고 지금 런의 스탯에도 바로 더한다(영약의 샘과 같은 방식: 영구 몫이 늘어난 만큼만).</summary>
        private void GrantPotionNow(PotionType type)
        {
            var attackBefore = RunProgress.CurrentPermanentAttack();
            var healthBefore = RunProgress.CurrentPermanentHealth();
            var criticalBefore = RunProgress.CurrentPermanentCritical();
            StatPotionWallet.AddPotion(type);
            var stats = _player.Stats;
            stats.FixedAttack += RunProgress.CurrentPermanentAttack() - attackBefore;
            var healthGain = RunProgress.CurrentPermanentHealth() - healthBefore;
            stats.FixedMaxHealth += healthGain;
            stats.Heal(healthGain);
            stats.CriticalChanceRate += RunProgress.CurrentPermanentCritical() - criticalBefore;
        }

        /// <summary>퍼즐 실패 - 입력을 잠그고 "다시 도전 / 이전 방으로" 창(방 1이면 이전 방이 없어서 평소 전투 방으로).</summary>
        public void FailPuzzle(PuzzleBase puzzle, string reason)
        {
            if (Puzzle != puzzle)
                return;
            PuzzleFailCount++;
            IsInputLocked = true;
            ClearAllTelegraphs();
            var back = _loopManager.CanReturnToPreviousRoom ? Loc.T("[B] 이전 방으로") : Loc.T("[B] 전투 방으로");
            puzzle.ShowFail(reason, back, () => _loopManager.RetryPuzzle(puzzle.Type), () => _loopManager.LeavePuzzle());
        }

        /// <summary>이 방에 아직 안 밟은 이벤트 칸이 있으면 그 액터(여러 개면 처음 것).</summary>
        public RoomEventActor Event => _events.Count > 0 ? _events[0] : null;

        /// <summary>이 방의 NPC(없으면 null).</summary>
        public NpcActor Npc => _npc;

        /// <summary>pos 주변 8칸에 있는 NPC - 플레이어가 F로 말을 걸 때.</summary>
        public NpcActor FindNpcNear(Vector2Int pos)
        {
            if (_npc == null)
                return null;
            var d = _npc.GridPos - pos;
            return Mathf.Abs(d.x) <= 1 && Mathf.Abs(d.y) <= 1 ? _npc : null;
        }

        public void Initialize(PlayerActor player, LoopManager loopManager, int stage)
        {
            _player = player;
            _loopManager = loopManager;
            _stage = stage;
        }

        /// <summary>봇 속도 측정용 - 몹 턴 전체 / 방 생성에 쓴 시간 누적.</summary>
        public static readonly System.Diagnostics.Stopwatch EnemyTurnWatch = new System.Diagnostics.Stopwatch();
        public static readonly System.Diagnostics.Stopwatch LoadRoomWatch = new System.Diagnostics.Stopwatch();

        public void LoadRoom(RoomDefinition def)
        {
            var nested = LoadRoomWatch.IsRunning;
            LoadRoomWatch.Start();
            try { LoadRoomCore(def); }
            finally { if (!nested) LoadRoomWatch.Stop(); }
        }

        private void LoadRoomCore(RoomDefinition def)
        {
            _current = def;
            ClearRoom();

            var layout = def.TutorialLayout ?? (def.IsTestRoom ? TestMap.Layout() : RoomLayoutGenerator.Generate(def, Rng));
            Map = new GridMap(layout.Width, layout.Height);
            foreach (var wall in layout.Walls)
                Map.AddWall(wall);

            BuildFloor(layout, def.IsBossRoom);
            _fitRoom = null; // 일반 방 - 플레이어 주변만 비추는 평소 카메라
            var glitch = !_loadCleared && GlitchRoom.ShouldGlitch(def, GameBootstrap.RoomsPerStage);
            var glitchTiles = glitch && !DamagePopup.Suppressed
                ? _roomObjects.Where(o => o != null).SelectMany(o => o.GetComponentsInChildren<SpriteRenderer>()).ToList() // 안개를 깔기 전 - 바닥·벽만
                : null;

            Map.PlaceActor(_player, layout.PlayerStart);
            if (_player.TryGetComponent<SpriteAnimator>(out var playerAnim))
                playerAnim.SnapToGrid(); // 옛 방 칸에서 미끄러져 들어오는 걷기 모션이 나오지 않게
            PositionCamera(layout);
            if (!def.IsBossRoom && !def.IsTestRoom) // 보스방·테스트 방은 안개 없이 전부 보이게(사용자 결정 - 보스 패턴/소환 몹을 다 보고 싸우게)
                FogOfWar.Create(this, _player, layout, _roomObjects);

            for (var i = 0; i < layout.EnemyPositions.Count && !_loadCleared; i++)
            {
                var kind = i < def.EnemyKinds.Count ? def.EnemyKinds[i] : EnemyKind.Melee;
                SpawnEnemy(layout.EnemyPositions[i], def, kind);
            }

            // 히든 몹 기사 - 방을 깔 때 굴리고, 나오는 건 방을 비운 순간(FinishRoomCleared). 기사를 잡아야 출구·NPC가 나온다.
            _knightPending = !_loadCleared && !glitch && MemoryKnight.ShouldSpawn(def, _stage, Rng);

            if (def.HasEvent && layout.EventPosition.HasValue && !_loadCleared)
                SpawnEvent(layout.EventPosition.Value, def.EventType);
            if (def.IsTestRoom)
                foreach (var (pos, type) in TestMap.Events)
                    SpawnEvent(pos, type);

            _thornsKills.Clear();
            _challengeEnemies.Clear();
            _roomCleared = false;
            _player.OnRoomEntered(); // 갑옷 효과(보호막/불굴/응급 처치) 방마다 다시 채움
            IsInputLocked = false;
            if (glitch)
                StartGlitch(glitchTiles);
            if (_loadCleared)
            {
                _roomCleared = true; // 퍼즐 실패 후 돌아온 이전 방 - 이미 깬 방이라 출구만
                SpawnExit();
            }
            else if (def.IsTutorialRoom && _enemies.Count == 0)
                FinishRoomCleared(); // 튜토리얼 첫 방(몹 없음) - 들어가자마자 출구
        }

        private EnemyActor SpawnEnemy(Vector2Int pos, RoomDefinition def, EnemyKind kind, float healthScale = 1f, float attackScale = 1f)
        {
            if (def.IsBossRoom)
                kind = EnemyKind.Melee;
            var go = new GameObject(def.IsBossRoom ? "Boss" : kind == EnemyKind.Melee ? "Enemy" : kind.ToString());
            go.transform.SetParent(transform, false);

            var (hpRatio, atkRatio) = kind switch
            {
                EnemyKind.Ranged => (RangedHealthRatio, RangedAttackRatio),
                EnemyKind.Shield => (ShieldHealthRatio, ShieldAttackRatio),
                EnemyKind.Spider => (SpiderHealthRatio, SpiderAttackRatio),
                EnemyKind.Bomber => (BomberHealthRatio, BomberAttackRatio),
                EnemyKind.Healer => (HealerHealthRatio, HealerAttackRatio),
                EnemyKind.Charger => (ChargerHealthRatio, ChargerAttackRatio),
                EnemyKind.Splitter => (SplitterHealthRatio, SplitterAttackRatio),
                EnemyKind.Assassin => (AssassinHealthRatio, AssassinAttackRatio),
                _ => (1f, 1f),
            };
            var enemy = go.AddComponent<EnemyActor>();
            enemy.Initialize(def.EnemyMaxHealth * hpRatio * healthScale, def.EnemyAttackPower * atkRatio * attackScale, def.IsBossRoom, kind);
            if (def.IsTutorialRoom)
                enemy.MarkAsTutorialMob(); // 골드·경험치 없음
            if (def.IsBossRoom)
                enemy.SetupBossPatterns(def.BossPatterns, _stage);
            Map.PlaceActor(enemy, pos);
            enemy.FaceToward(_player.GridPos); // 방패병은 입장한 플레이어 쪽을 보고 시작
            _enemies.Add(enemy);
            return enemy;
        }

        private void SpawnEvent(Vector2Int pos, RoomEventType type)
        {
            var go = new GameObject("RoomEvent");
            go.transform.SetParent(transform, false);
            var ev = go.AddComponent<RoomEventActor>();
            ev.Initialize(type);
            Map.PlaceActor(ev, pos);
            _events.Add(ev);
        }

        /// <summary>일반 방 클리어 시 NPC 등장 - 모험가: 의뢰를 들고 있으면 방 10에서 반드시(보고 받으러), 없으면 방 1에서 WandererChance.
        /// 모험가가 안 나오면 상인을 MerchantChance로 굴린다.</summary>
        private const float MerchantChance = 0.1f;
        private const float WandererChance = 0.33f;
        private const float CurseChance = 0.07f; // 상인도 안 나오면 그림자 거래상

        /// <summary>일반 방을 비운 직후(출구가 생긴 뒤) - 확률로 NPC를 플레이어 근처에 세운다. 봇도 걸어가서 말을 건다(AutoPlayBot).</summary>
        private void TrySpawnRoomClearNpc()
        {
            if (_npc != null)
                return;

            var room = _current.RoomNumber;
            var report = RunQuest.Active && room == RunQuest.ReportRoom;
            NpcType type;
            if (!report && LoopRogue.Blacksmith.ShouldAppear(Rng))
                type = NpcType.Blacksmith; // 히든 - 조각을 들고 있으면 반드시
            else if (report || (!RunQuest.Active && room == RunQuest.AcceptRoom && Rng.NextDouble() < WandererChance))
                type = NpcType.Wanderer;
            else if (Rng.NextDouble() < MerchantChance)
                type = NpcType.Merchant;
            else if (Rng.NextDouble() < CurseChance)
                type = NpcType.Curse;
            else
                return;

            var pos = FindNpcSpot();
            if (!pos.HasValue)
                return;

            var go = new GameObject("Npc");
            go.transform.SetParent(transform, false);
            _npc = go.AddComponent<NpcActor>();
            _npc.Initialize(type, _stage, this, _player, isReport: report);
            Map.PlaceActor(_npc, pos.Value);
            _npc.PlayAppearEffect();
            if (type == NpcType.Blacksmith)
                Achievements.Unlock("ACH_SMITH_MET");
            ShowMessage(report
                ? Loc.F("{0}이(가) 기다리고 있었다! (옆에서 F: 보고)", _npc.DisplayName)
                : Loc.F("{0}이(가) 나타났다! (옆에서 F: 대화)", _npc.DisplayName));
        }

        /// <summary>지금 층의 방 클리어 골드(보너스 반영) - 이벤트·저주 계약 보상의 기준.</summary>
        /// <summary>처치·방 클리어 골드 배율 - 골드 카드(하한 -50%) + 반지 "행운" + 유물 "탐욕" + 칭호, × 저주 계약(황금·무모).</summary>
        private float GoldBonus =>
            (1f + _player.Stats.EffectiveGoldBonus + EquipmentEffects.ExtraGoldBonus + Relics.ExtraGoldBonus + Achievements.TitleGoldBonus)
            * Curses.GoldMultiplier;

        public int RoomClearGold =>
            Mathf.RoundToInt(GoldPerRoomClear * StageScaling.RewardMultiplier(_stage) * (1f + _player.Stats.EffectiveGoldBonus));

        /// <summary>NPC가 볼일을 마치고 떠날 때(그림자 거래상의 계약 성립 등) - 연기 속에 사라지고 칸을 비운다.</summary>
        public void DismissNpc(NpcActor npc)
        {
            if (npc == null || npc != _npc)
                return;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(npc.transform.position, new Color(0.3f, 0.12f, 0.4f, 0.9f), 16, GridConstants.CellSize * 2.4f, GridConstants.CellSize * 0.18f);
            Map.RemoveActor(npc);
            Destroy(npc.gameObject);
            _npc = null;
        }

        /// <summary>NPC 자리 - 플레이어에게서 2~3칸(보이는 곳) 빈 칸 중, 그 칸을 막아도 나머지 빈 칸과 출구에 전부 갈 수 있는 칸.
        /// 가까운 거리부터 무작위로 보고, 없으면 거리 제한을 풀어서 아무 데나.</summary>
        private Vector2Int? FindNpcSpot()
        {
            var from = _player.GridPos;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && ExitPosition != p && !_traps.ContainsKey(p))
                    cells.Add(p);
            }

            int Dist(Vector2Int p) => Mathf.Max(Mathf.Abs(p.x - from.x), Mathf.Abs(p.y - from.y));
            var ordered = cells.Where(c => Dist(c) >= 2 && Dist(c) <= 3).OrderBy(_ => Rng.Next())
                .Concat(cells.Where(c => Dist(c) < 2 || Dist(c) > 3).OrderBy(Dist));
            foreach (var c in ordered)
                if (StaysConnectedWithout(c))
                    return c;
            return null;
        }

        /// <summary>blocked 칸을 막았을 때 플레이어 칸에서 다른 모든 빈 칸(출구 포함)에 갈 수 있는지 - 벽과 다른 액터(이벤트 칸 등)도 막힌 것으로 본다.</summary>
        private bool StaysConnectedWithout(Vector2Int blocked)
        {
            var start = _player.GridPos;
            var visited = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var next = cur + d;
                    if (next == blocked || !Map.IsWalkable(next) || !visited.Add(next))
                        continue;
                    queue.Enqueue(next);
                }
            }

            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (p != blocked && p != start && Map.IsWalkable(p) && !visited.Contains(p))
                    return false;
            }
            return true;
        }

        /// <summary>플레이어가 이벤트 칸으로 이동했을 때(PlayerActor가 호출) - 발동하고 칸을 비운다.
        /// 칸이 사라졌으면 true(플레이어가 그 칸으로 들어간다) - 피의 제단처럼 고르는 창이 뜨면 false(칸이 남는다).</summary>
        public bool TriggerEvent(RoomEventActor ev)
        {
            var message = ev.Trigger(_player, this, out var consumed);
            if (message != null)
                ShowMessage(message);
            if (!consumed)
                return false;
            ConsumeEvent(ev);
            return true;
        }

        /// <summary>이벤트 칸을 다 썼을 때 - 통계를 세고 칸을 비운다(피의 제단은 고르는 창에서 바친 순간).</summary>
        public void ConsumeEvent(RoomEventActor ev)
        {
            if (ev == null)
                return;
            EventTriggeredCount++;
            LastEventType = ev.Type;
            Map.RemoveActor(ev);
            Destroy(ev.gameObject);
            _events.Remove(ev);
        }

        // ---- 이벤트로 나오는 몹(미믹·도전의 깃발·운명의 주사위) ----
        public const float MimicHealthRatio = 3f;
        public const float MimicAttackRatio = 1.5f;
        public const int MimicGoldRooms = 12;      // 미믹 처치 골드 = 방 클리어 골드 × 이 값(+ 아이템 1개)
        public const int ChallengeEnemyCount = 3;
        public const float EliteHealthRatio = 1.8f;
        public const float EliteAttackRatio = 1.3f;
        public const int ChallengeGoldRooms = 6;   // 도전 성공 골드 = 방 클리어 골드 × 이 값(+ 레벨업 카드 1장)
        private readonly HashSet<EnemyActor> _challengeEnemies = new HashSet<EnemyActor>();
        private bool _roomCleared;

        /// <summary>미믹 - 상자 자리(곧 플레이어가 들어갈 칸) 옆 빈 칸에서 튀어나온다.</summary>
        public void SpawnMimic(Vector2Int chestPos)
        {
            var pos = FreeCellNear(chestPos, chestPos);
            if (!pos.HasValue)
                return;
            var mimic = SpawnEnemy(pos.Value, _current, EnemyKind.Melee, MimicHealthRatio, MimicAttackRatio);
            mimic.MarkAsMimic();
            mimic.FaceToward(chestPos);
            HitFeedback.OnSummon(mimic.transform.position);
        }

        /// <summary>이 방을 비우면 기사가 나온다(방을 깔 때 MemoryKnight.ShouldSpawn으로 정함).</summary>
        private bool _knightPending;

        /// <summary>히든 몹 "기억하는 기사" - 방을 비운 순간 플레이어에게서 3~4칸 떨어진 빈 칸(시야 안쪽)에 나타난다(없으면 2칸 이상 아무 데나).
        /// 그 방의 마지막 몹이 되어, 잡아야 방 클리어(출구·NPC). 나왔으면 true.</summary>
        private bool SpawnKnight()
        {
            var cells = new List<Vector2Int>();
            for (var pass = 0; pass < 2 && cells.Count == 0; pass++)
                for (var x = 0; x < Map.Width; x++)
                for (var y = 0; y < Map.Height; y++)
                {
                    var p = new Vector2Int(x, y);
                    var d = Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y));
                    var inRange = pass == 0 ? d >= 3 && d <= 4 : d >= 2;
                    if (inRange && Map.IsWalkable(p) && ExitPosition != p && !_traps.ContainsKey(p))
                        cells.Add(p);
                }
            if (cells.Count == 0)
                return false;
            var (bossHp, bossAtk) = GameBootstrap.BossStats(_stage);
            var knight = SpawnEnemy(cells[Rng.Next(cells.Count)], _current, EnemyKind.Melee,
                bossHp * MemoryKnight.BossHealthRate / Mathf.Max(0.01f, _current.EnemyMaxHealth),
                MemoryKnight.NormalAttackRatio);
            knight.MarkAsKnight(bossAtk * MemoryKnight.BossPatternRate);
            knight.FaceToward(_player.GridPos);
            KnightSpawnCount++;
            MemoryKnight.MarkAppeared(_stage);
            Achievements.Unlock("ACH_KNIGHT_MET");
            if (HitFeedback.Enabled)
                StartCoroutine(KnightEntrance(knight));
            else
                ShowMessage(MemoryKnight.Say(MemoryKnight.AppearLine())); // 봇 - 연출 없이 바로
            return true;
        }

        private const float KnightRumbleSeconds = 1f;
        private const float KnightRumbleStep = 0.12f;

        /// <summary>기사 등장 연출 - 던전이 점점 세게 울리며(화면 흔들림 + 어두워짐 + 천장 먼지) 1초쯤 뒤 쿵 하고 기사가 나타난다.
        /// 그동안 입력은 잠그고, 기사는 크기 0으로 숨겨 둔다(칸은 이미 차지 - 판정은 그대로).</summary>
        private IEnumerator KnightEntrance(EnemyActor knight)
        {
            var lockedHere = !IsInputLocked;
            if (lockedHere)
                IsInputLocked = true;
            var baseScale = knight.transform.localScale;
            knight.transform.localScale = Vector3.zero;
            var cs = GridConstants.CellSize;

            ShowMessage(Loc.T("...던전이 흔들린다."));
            SfxPlayer.Play(Sfx.Slam);
            ScreenFlash.Play(Color.black, 0.35f, KnightRumbleSeconds + 0.3f);
            for (var t = 0f; t < KnightRumbleSeconds; t += KnightRumbleStep)
            {
                CameraShake.Shake(Mathf.Lerp(0.04f, 0.14f, t / KnightRumbleSeconds), KnightRumbleStep + 0.05f);
                if (_player != null && Rng.NextDouble() < 0.7) // 천장에서 떨어지는 먼지
                {
                    var dust = _player.transform.position + new Vector3((float)(Rng.NextDouble() * 8 - 4) * cs, (float)(Rng.NextDouble() * 6 - 3) * cs, 0f);
                    DeathBurst.Spawn(dust, new Color(0.55f, 0.5f, 0.45f, 0.85f), 5, cs * 0.6f, cs * 0.08f);
                }
                yield return new WaitForSeconds(KnightRumbleStep);
            }

            if (knight != null)
            {
                knight.transform.localScale = baseScale;
                var pos = knight.transform.position;
                HitFeedback.OnSummon(pos);
                DeathBurst.Spawn(pos, new Color(0.45f, 0.95f, 0.88f, 0.9f), 18, cs * 2.4f, cs * 0.16f);
                DeathBurst.Spawn(pos, new Color(0.5f, 0.45f, 0.4f, 0.9f), 12, cs * 1.6f, cs * 0.14f);
                CameraShake.Shake(0.32f, 0.4f);
                SfxPlayer.Play(Sfx.Slam);
                ScreenFlash.Play(new Color(0.5f, 0.95f, 0.9f), 0.25f, 0.3f);
                ShowMessage(MemoryKnight.Say(MemoryKnight.AppearLine()));
            }
            if (lockedHere && this != null)
                IsInputLocked = false;
        }

        /// <summary>봇 통계용 - 기사가 나온 횟수, 잡은 횟수.</summary>
        public static int KnightSpawnCount, KnightKillCount;

        /// <summary>도전의 깃발 - 정예 몹 한 무리. 전부 잡으면 보상(GiveEventEnemyReward).</summary>
        public void StartChallenge(Vector2Int flagPos)
        {
            _challengeEnemies.Clear();
            for (var i = 0; i < ChallengeEnemyCount; i++)
            {
                var enemy = SpawnOneEventEnemy(flagPos, EliteHealthRatio, EliteAttackRatio, elite: true);
                if (enemy != null)
                    _challengeEnemies.Add(enemy);
            }
        }

        /// <summary>이벤트로 몹 count마리를 더 부른다(플레이어에게서 2칸 이상 떨어진 빈 칸). 실제로 나온 수를 돌려준다.</summary>
        public int SpawnEventEnemies(int count, float healthRatio, float attackRatio, bool elite)
        {
            var spawned = 0;
            for (var i = 0; i < count; i++)
                if (SpawnOneEventEnemy(_player.GridPos, healthRatio, attackRatio, elite) != null)
                    spawned++;
            return spawned;
        }

        /// <param name="avoid">비워둘 칸(곧 플레이어가 들어갈 이벤트 칸)</param>
        private EnemyActor SpawnOneEventEnemy(Vector2Int avoid, float healthRatio, float attackRatio, bool elite)
        {
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                var d = Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y));
                if (Map.IsWalkable(p) && p != avoid && ExitPosition != p && !_traps.ContainsKey(p) && d >= 2)
                    cells.Add(p);
            }
            if (cells.Count == 0)
                return null;
            var pos = cells[Rng.Next(cells.Count)];
            var kinds = _current.EnemyKinds?.Where(k => !elite || k != EnemyKind.Bomber).ToList(); // 정예 폭탄병은 스스로 터져 도전 보상이 막혔다
            var kind = kinds != null && kinds.Count > 0 ? kinds[Rng.Next(kinds.Count)] : EnemyKind.Melee;
            var enemy = SpawnEnemy(pos, _current, kind, healthRatio, attackRatio);
            if (elite)
                enemy.MarkAsElite();
            HitFeedback.OnSummon(enemy.transform.position);
            return enemy;
        }

        /// <summary>center 주변(1~3칸 고리 순서) 빈 칸 중 하나(avoid·플레이어 칸 제외).</summary>
        private Vector2Int? FreeCellNear(Vector2Int center, Vector2Int avoid)
        {
            for (var r = 1; r <= 3; r++)
            {
                var ring = new List<Vector2Int>();
                for (var dx = -r; dx <= r; dx++)
                for (var dy = -r; dy <= r; dy++)
                {
                    var p = center + new Vector2Int(dx, dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r && p != avoid && p != _player.GridPos && Map.IsWalkable(p)
                        && ExitPosition != p && !_traps.ContainsKey(p))
                        ring.Add(p);
                }
                if (ring.Count > 0)
                    return ring[Rng.Next(ring.Count)];
            }
            return null;
        }

        /// <summary>미믹·도전 정예를 잡았을 때의 추가 보상.</summary>
        private void GiveEventEnemyReward(EnemyActor enemy)
        {
            if (enemy.IsMimic)
            {
                var gold = RoomClearGold * MimicGoldRooms;
                GoldWallet.Add(gold);
                var item = Inventory.GiveRandomMissing();
                ShowMessage(Loc.F("미믹 처치! 골드 +{0}{1}", gold, (item.HasValue ? Loc.F(", {0} 획득", ItemInfo.Name(item.Value)) : "")));
                HitFeedback.OnGold(enemy.transform.position);
            }
            if (enemy.IsKnight)
            {
                var gold = RoomClearGold * MemoryKnight.GoldRooms;
                GoldWallet.Add(gold);
                HitFeedback.OnGold(enemy.transform.position);
                KnightKillCount++;
                Achievements.Unlock("ACH_KNIGHT_KILL");
                if (!enemy.KnightPatternLanded)
                    Achievements.Unlock("ACH_KNIGHT_PERFECT");
                if (LoopRogue.Blacksmith.OnKnightDefeated())
                    ShowMessage(Loc.F("<color=#7FE0D8>{0}</color>을 얻었다! 다음 방을 비우면 대장장이가 찾아온다", LoopRogue.Blacksmith.FragmentName));
                var first = MemoryKnight.MarkDefeated(_stage);
                var relic = !Relics.Has(RelicType.KnightMemory) && MemoryKnight.CanDropRelic(_stage);
                ShowMessage(MemoryKnight.Say(MemoryKnight.DeathLine(relic)));
                if (relic)
                {
                    Relics.Acquire(RelicType.KnightMemory, _player);
                    Achievements.Unlock("ACH_KNIGHT_RELIC");
                    ShowMessage(Loc.F("{0} 처치! 골드 +{1}, 히든 유물 <color=#7FE0D8>「{2}」</color> 획득", MemoryKnight.Name, gold, Relics.Name(RelicType.KnightMemory)));
                    HitFeedback.OnBonusReward(_player);
                }
                else
                {
                    ShowMessage(Loc.F("{0} 처치! 골드 +{1}", MemoryKnight.Name, gold));
                    if (first && !Relics.Has(RelicType.KnightMemory)) // 얕은 층 - 유물은 더 깊은 곳에서(처음 잡았을 때만 귀띔)
                        ShowMessage(Loc.T("<color=#9AA0AA>투구 틈의 빛이 희미하게 깜빡이다 꺼졌다... 더 깊은 곳의 기사라면 무언가 남길지도.</color>"));
                }
            }
            if (_challengeEnemies.Remove(enemy) && _challengeEnemies.Count == 0)
            {
                var gold = RoomClearGold * ChallengeGoldRooms;
                GoldWallet.Add(gold);
                _player.Levels.GrantBonusUpgrade();
                ShowMessage(Loc.F("도전 성공! 골드 +{0}, 레벨업 카드 1장", gold));
                HitFeedback.OnBonusReward(_player);
            }
        }

        public void ShowMessage(string message) => _loopManager.ShowMessage(message);

        /// <summary>보스 소환 - 보스 주변 빈 칸에 졸개를 (살아있는 졸개 포함 최대 maxAlive마리까지) 만든다.</summary>
        public void SpawnMinions(EnemyActor boss, int maxAlive)
        {
            var alive = _enemies.Count(e => e != null && e.IsMinion && !e.Stats.IsDead);
            var toSpawn = maxAlive - alive;
            if (toSpawn <= 0)
                return;

            // 보스 몸 바로 둘레 한 줄(3x3 보스면 5x5 테두리)
            var cells = new List<Vector2Int>();
            var ring = boss.FootprintRadius + 1;
            for (var dx = -ring; dx <= ring; dx++)
            for (var dy = -ring; dy <= ring; dy++)
            {
                var p = boss.GridPos + new Vector2Int(dx, dy);
                if (Map.IsWalkable(p))
                    cells.Add(p);
            }

            foreach (var pos in cells.OrderBy(_ => Rng.Next()).Take(toSpawn))
            {
                var go = new GameObject("Minion");
                go.transform.SetParent(transform, false);
                var minion = go.AddComponent<EnemyActor>();
                minion.Initialize(boss.Stats.MaxHealth * BossBrain.MinionHealthRatio,
                    boss.Stats.AttackPower * BossBrain.MinionAttackRatio, isBoss: false);
                minion.MarkAsMinion();
                Map.PlaceActor(minion, pos);
                HitFeedback.OnSummon(new Vector3(pos.x * GridConstants.CellSize, pos.y * GridConstants.CellSize, 0f));
                _enemies.Add(minion);
            }
        }

        /// <summary>분열체가 죽은 자리(이미 맵에서 빠진 뒤) 근처 빈 칸에 작은 분열 조각을 만든다 - 조각은 더 갈라지지 않는다.
        /// 처치 처리(NotifyEnemyDefeated) 전에 불러야 방이 비었다고 판정되지 않는다.</summary>
        public void SpawnSplitChildren(EnemyActor parent)
        {
            var cells = new List<Vector2Int>();
            if (Map.IsWalkable(parent.GridPos))
                cells.Add(parent.GridPos);
            foreach (var p in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }.Select(d => parent.GridPos + d).OrderBy(_ => Rng.Next()))
                if (Map.IsWalkable(p) && ExitPosition != p)
                    cells.Add(p);

            foreach (var pos in cells.Take(EnemyActor.SplitCount))
            {
                var go = new GameObject("SplitChild");
                go.transform.SetParent(transform, false);
                var child = go.AddComponent<EnemyActor>();
                child.Initialize(parent.Stats.MaxHealth * EnemyActor.SplitHealthRatio, parent.Stats.AttackPower * EnemyActor.SplitAttackRatio,
                    isBoss: false, EnemyKind.Splitter);
                child.MarkAsSplitChild();
                if (parent.IsPuzzleMob)
                    child.MarkAsPuzzleMob();
                if (parent.IsGlitchRevived)
                    child.MarkGlitchRevived(); // 되살아난 분열체의 조각은 다시 살아나지 않는다(분열↔되살아남 무한 반복 방지)
                Map.PlaceActor(child, pos);
                _enemies.Add(child);
            }
            SplitCount++;
        }

        /// <summary>봇 통계용 - 분열체가 갈라진 횟수.</summary>
        public static int SplitCount;

        // ===================== 오류방 =====================

        private GlitchVisual _glitchVisual;

        /// <summary>봇·통계용 - 오류방에 들어간 횟수 / 바로잡은 횟수.</summary>
        public static int GlitchEnterCount, GlitchClearCount;

        private void StartGlitch(List<SpriteRenderer> tiles)
        {
            var rule = GlitchRoom.PickRule(Rng);
            GlitchRoom.Begin(rule);
            GlitchEnterCount++;
            if (tiles != null)
            {
                var go = new GameObject("GlitchVisual");
                go.transform.SetParent(transform, false);
                _glitchVisual = go.AddComponent<GlitchVisual>();
                _glitchVisual.Init(tiles);
                _roomObjects.Add(go);
                ScreenFlash.Play(new Color(0.7f, 0.3f, 1f), 0.4f, 0.4f);
                CameraShake.Shake(0.2f, 0.3f);
            }
            ShowMessage(Loc.F("세계가 일그러진다… {0}", GlitchRoom.RuleText(rule)));
        }

        /// <summary>오류방을 비웠다 - 골드(방 클리어 골드 × ClearGoldRooms) + 오류 조각 1개, 이 층의 오류방은 바로잡힌 걸로.</summary>
        private void FinishGlitch()
        {
            var gold = Mathf.RoundToInt(GoldPerRoomClear * StageScaling.RewardMultiplier(_stage) * GoldBonus * GlitchRoom.ClearGoldRooms);
            GoldWallet.Add(gold);
            var total = GlitchRoom.AddFragment();
            StageProgress.MarkGlitchCleared();
            Achievements.Unlock("ACH_GLITCH");
            GlitchRoom.End();
            GlitchClearCount++;
            _glitchVisual?.Restore();
            HitFeedback.OnBonusReward(_player);
            ShowMessage(Loc.F("오류를 바로잡았다! 골드 +{0}, 오류 조각 +1 (모은 조각 {1}개)", gold, total));
        }

        /// <summary>오류방 "되살아남" - 쓰러진 몹이 그 자리(막혔으면 옆 칸)에서 체력 절반으로 한 번 더(보상 없음, 다시는 안 살아남).</summary>
        public void SpawnGlitchRevive(EnemyActor dead)
        {
            Vector2Int? pos = Map.IsWalkable(dead.GridPos) ? dead.GridPos : (Vector2Int?)null;
            if (!pos.HasValue)
                foreach (var p in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }.Select(d => dead.GridPos + d))
                    if (Map.IsWalkable(p) && ExitPosition != p)
                    {
                        pos = p;
                        break;
                    }
            if (!pos.HasValue)
                return;
            var go = new GameObject("GlitchRevived");
            go.transform.SetParent(transform, false);
            var enemy = go.AddComponent<EnemyActor>();
            enemy.Initialize(dead.Stats.MaxHealth * GlitchRoom.ReviveHealthRatio, dead.Stats.AttackPower, isBoss: false, dead.Kind);
            enemy.MarkGlitchRevived();
            if (dead.IsSplitChild)
                enemy.MarkAsSplitChild(); // 조각이 되살아나도 조각 - 다시 분열하지 않는다(예전엔 분열↔되살아남이 끝없이 반복됐다)
            if (dead.IsPuzzleMob)
                enemy.MarkAsPuzzleMob();
            Map.PlaceActor(enemy, pos.Value);
            _enemies.Add(enemy);
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(enemy.transform.position, new Color(0.7f, 0.3f, 1f), count: 18, speed: 4f, size: 0.18f);
        }

        public void ShowTelegraph(IEnumerable<Vector2Int> tiles)
        {
            ClearTelegraph();
            HitFeedback.OnTelegraph();
            foreach (var t in tiles)
            {
                _bossDangerTiles.Add(t);
                _telegraphObjects.Add(SpawnTelegraphTile(t, TelegraphColor, -5));
            }
            RebuildDangerTiles();
        }

        private static readonly Color TelegraphLaterColor = new Color(0.7f, 0.45f, 1f, 0.35f);

        /// <summary>거울 파편 - 여러 웨이브를 한꺼번에 예고. 칸마다 몇 번째에 터지는지 숫자를 적고, 바로 다음(waves[0]) 칸은 진한 빨강,
        /// 나중 칸은 옅은 보라. 실제 위험 칸(봇·위험 표시 기준)은 바로 다음 웨이브만.</summary>
        public void ShowTelegraphWaves(IReadOnlyList<HashSet<Vector2Int>> waves, int firstNumber)
        {
            ClearTelegraph();
            HitFeedback.OnTelegraph();
            if (waves.Count == 0)
                return;
            _bossDangerTiles.UnionWith(waves[0]);
            var numbers = new Dictionary<Vector2Int, List<int>>();
            for (var i = 0; i < waves.Count; i++)
                foreach (var t in waves[i])
                {
                    if (!numbers.TryGetValue(t, out var list))
                        numbers[t] = list = new List<int>();
                    list.Add(firstNumber + i);
                }
            foreach (var pair in numbers)
            {
                var next = pair.Value[0] == firstNumber;
                _telegraphObjects.Add(SpawnTelegraphTile(pair.Key, next ? TelegraphColor : TelegraphLaterColor, next ? -5 : -6));
                _telegraphObjects.Add(SpawnTelegraphLabel(pair.Key, string.Join(" ", pair.Value), next));
            }
            RebuildDangerTiles();
        }

        /// <summary>예고 칸 위 숫자(월드 글자) - 다음 웨이브 숫자는 흰색으로 크게.</summary>
        private GameObject SpawnTelegraphLabel(Vector2Int t, string text, bool next)
        {
            var go = new GameObject("TelegraphNumber");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, -0.4f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = next ? 0.075f : 0.06f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = next ? Color.white : new Color(0.85f, 0.75f, 1f, 0.9f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.material = mesh.font.material;
            renderer.sortingOrder = 4;
            return go;
        }

        /// <summary>보스 예고만 지운다(폭발병 예고는 그대로).</summary>
        public void ClearTelegraph()
        {
            foreach (var go in _telegraphObjects)
                if (go != null)
                    Destroy(go);
            _telegraphObjects.Clear();
            _bossDangerTiles.Clear();
            RebuildDangerTiles();
        }

        /// <summary>보스 예고 + 폭발 예고 전부 - 방을 비우거나 플레이어가 죽을 때.</summary>
        private void ClearAllTelegraphs()
        {
            foreach (var bomb in _bombs.Values)
                foreach (var go in bomb.Objects)
                    if (go != null)
                        Destroy(go);
            _bombs.Clear();
            ClearTelegraph();
        }

        private GameObject SpawnTelegraphTile(Vector2Int t, Color color, int sortingOrder)
        {
            var position = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f);
            // 도트 예고 칸(흰 그림에 색을 곱함 - 보스 = 빨강, 폭발 = 주황, 깜빡임) - 그림이 없거나 봇 중이면 예전 사각형.
            var fx = Fx.Play("telegraph", position, 0.95f, tint: new Color(color.r, color.g, color.b, 0.95f), fps: 6f, loop: true,
                sortingOrder: sortingOrder, parent: transform);
            if (fx != null)
                return fx.gameObject;
            var go = new GameObject("Telegraph");
            go.transform.SetParent(transform, false);
            VisualUtil.CreateSquareVisual(go, color, GridConstants.CellSize * 0.95f, sortingOrder);
            go.transform.position = position;
            return go;
        }

        private void RebuildDangerTiles()
        {
            _dangerTiles.Clear();
            _dangerTiles.UnionWith(_bossDangerTiles);
            foreach (var bomb in _bombs.Values)
                _dangerTiles.UnionWith(bomb.Tiles);
        }

        /// <summary>폭발병이 불을 붙였을 때 - 3x3 주황 예고.</summary>
        public void ShowBombTelegraph(EnemyActor bomber, List<Vector2Int> tiles) => ShowEnemyTelegraph(bomber, tiles, BombTelegraphColor);

        /// <summary>돌진병 돌진 예고 색 - 보스 예고(빨강)보다 조금 주황 쪽.</summary>
        public static readonly Color ChargeTelegraphColor = new Color(1f, 0.3f, 0.2f, 0.45f);

        /// <summary>일반 몹 하나의 예고 칸(폭발병 3x3, 돌진병 돌진 줄) - 몹마다 따로 들고 있다가 발동하거나 그 몹이 죽으면 지운다.</summary>
        public void ShowEnemyTelegraph(EnemyActor enemy, List<Vector2Int> tiles, Color color)
        {
            ClearBombTelegraph(enemy);
            var objects = tiles.Select(t => SpawnTelegraphTile(t, color, -4)).ToList();
            _bombs[enemy] = (tiles, objects);
            RebuildDangerTiles();
        }

        public void ClearEnemyTelegraph(EnemyActor enemy) => ClearBombTelegraph(enemy);

        private void ClearBombTelegraph(EnemyActor bomber)
        {
            if (!_bombs.TryGetValue(bomber, out var bomb))
                return;
            foreach (var go in bomb.Objects)
                if (go != null)
                    Destroy(go);
            _bombs.Remove(bomber);
            RebuildDangerTiles();
        }

        /// <summary>폭발병이 터진다 - 예고했던 3x3 안에 플레이어가 있으면 damage(다른 몹은 안 맞음). 폭발병 자신은 보상 없이
        /// 사라진다. 그게 마지막 몹이었으면 몹 턴 도중이라도 여기서 바로 다음 방으로 넘어간다
        /// (RunEnemyTurns의 남은 순회는 이미 죽은 옛 몹들이라 건너뛴다).</summary>
        public void ResolveExplosion(EnemyActor bomber, float damage)
        {
            var tiles = _bombs.TryGetValue(bomber, out var bomb) ? bomb.Tiles : bomber.BlastTiles();
            ClearBombTelegraph(bomber);
            BombExplosionCount++;
            HitFeedback.OnExplosion(bomber.transform.position);

            if (tiles.Contains(_player.GridPos))
            {
                BombPlayerHitCount++;
                var dealt = _player.Stats.TakeIncomingDamage(damage);
                DamagePopup.Spawn(_player.transform.position, dealt, new Color(1f, 0.5f, 0.1f), isCritical: true);
                HitFeedback.OnPlayerHurt(_player, null);
            }

            // 폭발병 자신 - 죽음 표시만 하고 보상 없이 제거(방 비었는지는 맨 끝에서 한 번만 본다).
            bomber.Stats.TakeDamage(bomber.Stats.CurrentHealth + 1f);
            Map.RemoveActor(bomber);
            _enemies.Remove(bomber);
            Destroy(bomber.gameObject);

            if (_player.Stats.IsDead)
                return; // RunEnemyTurns가 사망 처리

            // 다른 몹은 안 맞는다(사용자 결정) - 맞게 했더니 판당 73마리를 대신 잡아주는 청소부가 됐다.
            if (Puzzle != null)
                Puzzle.EnemyDefeated(bomber); // 퍼즐 몹 폭발병 - 방 클리어 판정은 퍼즐이
            else if (_enemies.Count == 0)
                FinishRoomCleared();
        }

        private void BuildFloor(RoomLayout layout, bool isBossRoom)
        {
            _tiledRoom = !GameHUD.AutoPlayActive && DungeonTiles.Available;
            if (_tiledRoom)
            {
                BuildTiledRoom(layout, isBossRoom);
                return;
            }

            var floor = new GameObject("Floor");
            floor.transform.SetParent(transform, false);
            var color = isBossRoom ? new Color(0.22f, 0.05f, 0.05f) : new Color(0.15f, 0.15f, 0.18f);

            // sortingOrder를 액터들(0 이상)보다 낮게 둬서 항상 맨 뒤에 깔린다.
            var renderer = VisualUtil.CreateSquareVisual(floor, color, 1f, sortingOrder: -10);
            floor.transform.localScale = new Vector3(layout.Width * GridConstants.CellSize, layout.Height * GridConstants.CellSize, 1f);
            renderer.transform.position = new Vector3(
                (layout.Width - 1) * GridConstants.CellSize * 0.5f,
                (layout.Height - 1) * GridConstants.CellSize * 0.5f,
                0f);
            _roomObjects.Add(floor);

            foreach (var wall in layout.Walls)
            {
                var go = new GameObject("Wall");
                go.transform.SetParent(transform, false);
                VisualUtil.CreateSquareVisual(go, WallColor, GridConstants.CellSize, sortingOrder: -8);
                go.transform.position = new Vector3(wall.x * GridConstants.CellSize, wall.y * GridConstants.CellSize, 0f);
                _roomObjects.Add(go);
                _wallObjects[wall] = go;
            }
        }

        // ---- 도트 타일 방(DungeonTiles) ----
        private bool _tiledRoom;
        private Transform _floorRoot;
        private Color _floorTint;
        private RoomLayout _tileLayout;
        private readonly System.Random _tileRng = new System.Random();

        /// <summary>칸마다 돌판 바닥, 벽은 아래가 바닥이면 벽돌 앞면 / 아니면 윗면, 방 둘레엔 장식용 벽 한 줄(이동 판정엔 없음).
        /// 보스방은 바닥을 붉게 물들인다.</summary>
        private void BuildTiledRoom(RoomLayout layout, bool isBossRoom)
        {
            _tileLayout = layout;
            _floorTint = isBossRoom ? new Color(1f, 0.58f, 0.55f) : Color.white;
            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(transform, false);
            _floorRoot = floorGo.transform;
            _roomObjects.Add(floorGo);

            for (var x = 0; x < layout.Width; x++)
            for (var y = 0; y < layout.Height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (!layout.Walls.Contains(cell))
                    CreateTile(_floorRoot, cell, DungeonTiles.Floor(_tileRng), -10, _floorTint);
            }

            foreach (var wall in layout.Walls)
            {
                var go = CreateTile(transform, wall, DungeonTiles.Wall(ShowsWallFace(wall), _tileRng), -8, Color.white).gameObject;
                go.name = "Wall";
                _roomObjects.Add(go);
                _wallObjects[wall] = go;
            }

            // 방 둘레 장식 벽 - 안개 밖이라 조금 어둡게
            var border = new Color(0.72f, 0.72f, 0.78f);
            for (var x = -1; x <= layout.Width; x++)
            for (var y = -1; y <= layout.Height; y++)
            {
                if (x >= 0 && y >= 0 && x < layout.Width && y < layout.Height)
                    continue;
                var cell = new Vector2Int(x, y);
                _roomObjects.Add(CreateTile(transform, cell, DungeonTiles.Wall(ShowsWallFace(cell), _tileRng), -8, border).gameObject);
            }
        }

        /// <summary>바로 아래 칸이 방 안의 바닥이면 앞면이 보인다.</summary>
        private bool ShowsWallFace(Vector2Int cell)
        {
            var below = cell + Vector2Int.down;
            var inside = below.x >= 0 && below.y >= 0 && below.x < _tileLayout.Width && below.y < _tileLayout.Height;
            return inside && !_tileLayout.Walls.Contains(below);
        }

        private static SpriteRenderer CreateTile(Transform parent, Vector2Int cell, Sprite sprite, int sortingOrder, Color tint)
        {
            var go = new GameObject("Tile");
            go.transform.SetParent(parent, false);
            var renderer = VisualUtil.CreateSpriteVisual(go, sprite, GridConstants.CellSize, sortingOrder);
            renderer.color = tint;
            go.transform.position = new Vector3(cell.x * GridConstants.CellSize, cell.y * GridConstants.CellSize, 0f);
            return renderer;
        }

        // ---- 2층 오우거 균열(BossBrain) ----
        public const int OgreFallStunTurns = 2;
        public const int OgreFallImmuneTurns = 2;   // 빠졌다 나온 뒤 다시 안 빠지는 턴(연속 기절 방지)
        public const int PlayerCrackRootTurns = 1;
        private readonly Dictionary<Vector2Int, GameObject> _cracks = new Dictionary<Vector2Int, GameObject>();
        private int _ogreFallImmune;

        public int CrackCount => _cracks.Count;
        public bool IsCrack(Vector2Int pos) => _cracks.ContainsKey(pos);

        /// <summary>강타가 내려찍은 칸 중 count칸(빈 바닥, 출구·이미 균열인 칸 제외)에 균열을 남긴다.</summary>
        public void AddOgreCracks(IEnumerable<Vector2Int> tiles, int count)
        {
            var candidates = tiles.Where(p => Map.IsInBounds(p) && !Map.IsWall(p) && !_cracks.ContainsKey(p) && ExitPosition != p
                                              && !(Map.GetActorAt(p) is EnemyActor e && e.IsBoss)).OrderBy(_ => Rng.Next()).Take(count).ToList();
            foreach (var p in candidates)
            {
                var go = new GameObject("Crack");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
                if (VisualUtil.TryCreateProp(go, "Floor_Crack_" + Rng.Next(2), -7) == null)
                    VisualUtil.CreateSquareVisual(go, new Color(0.05f, 0.04f, 0.06f, 0.8f), GridConstants.CellSize * 0.7f, sortingOrder: -7);
                _roomObjects.Add(go);
                _cracks[p] = go;
                if (!DamagePopup.Suppressed)
                    DeathBurst.Spawn(go.transform.position, new Color(0.45f, 0.42f, 0.48f, 0.9f), 6, GridConstants.CellSize * 0.9f, GridConstants.CellSize * 0.09f);
            }
        }

        private void CollapseCrack(Vector2Int p)
        {
            if (!_cracks.TryGetValue(p, out var go))
                return;
            _cracks.Remove(p);
            if (go == null)
                return;
            if (!DamagePopup.Suppressed)
                DeathBurst.Spawn(go.transform.position, new Color(0.5f, 0.45f, 0.4f, 0.95f), 12, GridConstants.CellSize * 1.4f, GridConstants.CellSize * 0.12f);
            Destroy(go);
        }

        /// <summary>몹 턴이 끝날 때마다 - 오우거(보스) 몸이 균열에 걸쳐 있으면 빠진다: 그 균열들이 무너지고 기절.</summary>
        private void CheckOgreCrack(EnemyActor enemy)
        {
            if (!enemy.IsBoss || _cracks.Count == 0 || enemy.Stats.IsDead)
                return;
            if (_ogreFallImmune > 0)
            {
                _ogreFallImmune--;
                return;
            }
            if (enemy.StunTurns > 0)
                return;
            var under = GridMap.CellsOf(enemy).Where(c => _cracks.ContainsKey(c)).ToList();
            if (under.Count == 0)
                return;
            foreach (var c in under)
                CollapseCrack(c);
            enemy.Stun(OgreFallStunTurns);
            _ogreFallImmune = OgreFallStunTurns + OgreFallImmuneTurns;
            OgreFallCount++;
            ShowMessage(Loc.T("<color=#FFB060>오우거가 균열에 빠졌다!</color> 다리가 걸려 꼼짝 못 한다 - 지금이다!"));
            if (HitFeedback.Enabled)
            {
                CameraShake.Shake(0.3f, 0.35f);
                SfxPlayer.Play(Sfx.Slam);
            }
        }

        /// <summary>플레이어가 한 칸 들어선 직후(이동·대시 착지) - 균열이면 발이 빠져 묶이고 균열은 무너진다. 묶였으면 true.</summary>
        public bool CheckPlayerCrack()
        {
            var p = _player.GridPos;
            if (!_cracks.ContainsKey(p))
                return false;
            CollapseCrack(p);
            _player.ApplyRoot(PlayerCrackRootTurns);
            PlayerCrackCount++;
            ShowMessage(Loc.T("<color=#FF8080>균열에 발이 빠졌다!</color> 한 턴 움직일 수 없다 (공격은 가능)"));
            if (HitFeedback.Enabled)
                CameraShake.Shake(0.12f, 0.15f);
            return true;
        }

        /// <summary>봇 통계용 - 오우거가 균열에 빠진 횟수 / 플레이어가 빠진 횟수.</summary>
        public static int OgreFallCount, PlayerCrackCount;

        // ---- 1층 거대 멧돼지 기둥(BossBrain) ----
        private readonly HashSet<Vector2Int> _boarPillars = new HashSet<Vector2Int>();
        private static readonly Color PillarTint = new Color(0.95f, 0.82f, 0.68f);

        public int BoarPillarCount => _boarPillars.Count;

        /// <summary>멧돼지 상하좌우로 4칸(안 되면 3·5칸) 떨어진 빈 칸에 돌기둥(벽 칸)을 세운다 - 세운 개수.
        /// 기둥 뒤에 선 플레이어를 향해 돌진하면 몸 띠가 기둥에 걸려 박는다.</summary>
        public int SpawnBoarPillars(EnemyActor boar)
        {
            _boarPillars.Clear();
            foreach (var dir in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                foreach (var dist in new[] { 4, 3, 5 })
                {
                    var p = boar.GridPos + dir * dist;
                    if (!Map.IsWalkable(p) || p == _player.GridPos || ExitPosition == p || _dangerTiles.Contains(p))
                        continue;
                    Map.AddWall(p);
                    _boarPillars.Add(p);
                    var go = _tiledRoom
                        ? CreateTile(transform, p, DungeonTiles.Wall(true, _tileRng), -8, PillarTint).gameObject
                        : CreatePillarSquare(p);
                    _roomObjects.Add(go);
                    _wallObjects[p] = go;
                    if (!DamagePopup.Suppressed)
                    {
                        DeathBurst.Spawn(go.transform.position, new Color(0.6f, 0.52f, 0.42f, 0.9f), 10, GridConstants.CellSize * 1.4f, GridConstants.CellSize * 0.12f);
                        StartCoroutine(RisePillar(go.transform));
                    }
                    break;
                }
            }
            if (_boarPillars.Count > 0 && HitFeedback.Enabled)
            {
                CameraShake.Shake(0.22f, 0.4f);
                SfxPlayer.Play(Sfx.Slam);
            }
            return _boarPillars.Count;
        }

        private GameObject CreatePillarSquare(Vector2Int p)
        {
            var go = new GameObject("Pillar");
            go.transform.SetParent(transform, false);
            VisualUtil.CreateSquareVisual(go, new Color(0.45f, 0.4f, 0.35f), GridConstants.CellSize, sortingOrder: -8);
            go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
            return go;
        }

        /// <summary>기둥이 땅에서 솟아오르는 연출(크기만 - 칸은 이미 막혀 있다).</summary>
        private static IEnumerator RisePillar(Transform pillar)
        {
            var baseScale = pillar.localScale;
            for (var t = 0f; t < 0.3f && pillar != null; t += Time.deltaTime)
            {
                pillar.localScale = new Vector3(baseScale.x, baseScale.y * Mathf.SmoothStep(0.1f, 1f, t / 0.3f), 1f);
                yield return null;
            }
            if (pillar != null)
                pillar.localScale = baseScale;
        }

        /// <summary>cells 중 멧돼지 기둥을 전부 부순다(돌진에 박힘) - 하나라도 부쉈으면 true.</summary>
        public bool BreakBoarPillars(IEnumerable<Vector2Int> cells)
        {
            var broke = false;
            foreach (var p in cells.Where(c => _boarPillars.Contains(c)).ToList())
            {
                RemoveWallObject(p);
                broke = true;
            }
            if (broke && HitFeedback.Enabled)
            {
                CameraShake.Shake(0.35f, 0.4f);
                SfxPlayer.Play(Sfx.Explosion);
            }
            return broke;
        }

        /// <summary>벽 칸 하나를 지운다(그림·바닥 정리 포함) - 멧돼지 기둥이면 기둥 목록에서도 뺀다.</summary>
        private void RemoveWallObject(Vector2Int p)
        {
            Map.RemoveWall(p);
            if (_wallObjects.TryGetValue(p, out var wallGo))
            {
                _wallObjects.Remove(p);
                if (wallGo != null)
                {
                    if (!DamagePopup.Suppressed)
                        DeathBurst.Spawn(wallGo.transform.position, new Color(0.6f, 0.52f, 0.42f, 0.95f), 16, GridConstants.CellSize * 2f, GridConstants.CellSize * 0.16f);
                    Destroy(wallGo);
                }
            }
            OnWallRemoved(p);
            if (_boarPillars.Remove(p) && _boarPillars.Count == 0)
                ShowMessage(Loc.T("<color=#FFB060>기둥이 모두 무너졌다!</color> 멧돼지의 단단함이 풀렸다!"));
        }

        /// <summary>폭탄으로 벽이 부서진 뒤 - 그 자리에 바닥을 깔고, 바로 위 벽은 이제 아래가 바닥이니 앞면으로.</summary>
        private void OnWallRemoved(Vector2Int cell)
        {
            if (!_tiledRoom || _floorRoot == null)
                return;
            CreateTile(_floorRoot, cell, DungeonTiles.Floor(_tileRng), -10, _floorTint);
            if (_wallObjects.TryGetValue(cell + Vector2Int.up, out var above) && above != null)
                above.GetComponent<SpriteRenderer>().sprite = DungeonTiles.Wall(true, _tileRng);
        }

        /// <summary>방마다 크기가 달라서(보스방은 더 큼) 매 방 로드 시 카메라를 방 중앙으로 다시
        /// 맞춘다 - 2D라 회전 없이 Z축 뒤로 물러나서 보는 표준 카메라.</summary>
        private void PositionCamera(RoomLayout layout)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            // 방 전체가 아니라 플레이어 주변만 보이게 당기고(사용자: "시야가 너무 넓다") 플레이어를 따라간다.
            // 방 경계 바깥은 한 칸까지만 비친다.
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthoSize(cam);
            var cs = GridConstants.CellSize;
            var bounds = Rect.MinMaxRect(-1.5f * cs, -1.5f * cs, (layout.Width + 0.5f) * cs, (layout.Height + 0.5f) * cs);
            CameraShake.Follow(cam, _player, bounds);
        }

        private void ClearRoom()
        {
            _knightPending = false;
            _boarPillars.Clear();
            _cracks.Clear();
            _ogreFallImmune = 0;
            GlitchRoom.End();
            _glitchVisual = null;
            if (Puzzle != null)
            {
                Puzzle.Dispose();
                Puzzle = null;
            }

            foreach (var enemy in _enemies)
                if (enemy != null)
                    Destroy(enemy.gameObject);
            _enemies.Clear();

            foreach (var ev in _events) // 방을 다시 깔 때 남은 이벤트 칸을 전부 지운다(예전엔 하나만 지워서 테스트 맵에 옛 그림이 남았다)
                if (ev != null)
                    Destroy(ev.gameObject);
            _events.Clear();

            if (_npc != null)
                Destroy(_npc.gameObject);
            _npc = null;
            if (NpcDialogUI.Instance != null)
                NpcDialogUI.Instance.Close(); // 대화 중 방이 넘어가면(폭탄 등) 창을 닫는다

            foreach (var go in _roomObjects)
                if (go != null)
                    Destroy(go);
            _roomObjects.Clear();
            ExitPosition = null;
            ExitRenderer = null;
            if (ActiveDecoy != null)
                Destroy(ActiveDecoy.gameObject);
            ActiveDecoy = null;
            _torches.Clear();
            _traps.Clear();
            _playerBombs.Clear();
            _wallObjects.Clear();

            ClearAllTelegraphs();
        }

        /// <summary>적 하나가 죽은 직후 호출 - 목록에서 빼고, 방이 비었으면 다음 방/승리로 진행한다.
        /// 방이 전환됐으면 true(호출부는 이번 턴엔 몹 행동을 건너뛴다). 골드는 여기서 한 곳에서만 지급한다(졸개는 없음).
        /// 보스가 죽으면 남은 졸개도 같이 사라진다.</summary>
        public bool NotifyEnemyDefeated(EnemyActor enemy)
        {
            var stageMultiplier = StageScaling.RewardMultiplier(_stage);
            var goldBonus = GoldBonus;
            // 반복 보상 감소는 일반 몹/방 클리어에만(보스 처치는 항상 100%).
            var repeat = StageProgress.RepeatRewardMultiplier;
            ClearBombTelegraph(enemy); // 불 붙은 폭발병을 잡으면 불발, 예고한 돌진병을 잡으면 취소
            if (!enemy.NoReward)
            {
                var killGold = enemy.IsBoss
                    ? Mathf.RoundToInt(GoldPerBossKill * stageMultiplier * goldBonus)
                    : Mathf.RoundToInt(GoldPerKill * stageMultiplier * goldBonus * repeat);
                GoldWallet.Add(killGold);
                HitFeedback.OnGold(enemy.transform.position);
            }

            _enemies.Remove(enemy);
            if (Puzzle != null)
            {
                Puzzle.EnemyDefeated(enemy); // 퍼즐 몹 - 방 클리어 판정은 퍼즐이 한다
                return false;
            }
            GiveEventEnemyReward(enemy);

            if (enemy.IsBoss)
            {
                foreach (var minion in _enemies.ToArray())
                {
                    if (minion == null)
                        continue;
                    Map.RemoveActor(minion);
                    Destroy(minion.gameObject);
                }
                _enemies.Clear();
                ClearTelegraph();
            }

            if (_enemies.Count > 0 || _roomCleared)
                return false; // 이미 비운 방에서 이벤트로 나온 몹(미믹·정예)을 잡은 경우 - 방 클리어는 한 번만

            FinishRoomCleared();
            return true;
        }

        /// <summary>방의 몹이 전부 사라졌을 때 - 일반 방은 클리어 골드 + 회복 후 다음 방, 보스방은 스테이지 클리어.</summary>
        private void FinishRoomCleared()
        {
            if (_roomCleared)
                return;
            if (_knightPending)
            {
                _knightPending = false;
                if (SpawnKnight())
                    return; // 기사를 잡으면 다시 여기로 와서 진짜 클리어
            }
            if (_current.IsTutorialRoom)
            {
                _roomCleared = true; // 튜토리얼 - 골드·회복·방 클리어 기록·NPC 없이 출구만
                SpawnExit();
                return;
            }
            _roomCleared = true;
            RoomClearCount++;
            LastClearedRoom = _current.RoomName;
            LastClearedWasBoss = _current.IsBossRoom;
            LastClearHpFraction = _player.Stats.CurrentHealth / Mathf.Max(1f, _player.Stats.MaxHealth); // 클리어 회복 전
            Achievements.Check(); // 누적 처치 업적 - 몹마다 보지 않고 방을 비울 때 한 번
            if (!_current.IsBossRoom && !_current.IsTestRoom)
                StageProgress.MarkRoomCleared(_current.RoomNumber); // 시작 방 고르기에서 다음 방이 열린다
            if (!_current.IsBossRoom)
            {
                var goldBonus = GoldBonus;
                GoldWallet.Add(Mathf.RoundToInt(GoldPerRoomClear * StageScaling.RewardMultiplier(_stage) * goldBonus * StageProgress.RepeatRewardMultiplier));
                _player.Stats.Heal(_player.Stats.MaxHealth * (RoomClearHealRate + (Relics.Has(RelicType.RegenMoss) ? Relics.RegenMossHealRate : 0f)) // + 유물 "재생의 이끼"
                                   * Curses.RoomHealMultiplier); // × 저주 "메마른 저주"
                HitFeedback.OnHeal(_player);
            }

            if (GlitchRoom.IsActive && !_current.IsBossRoom)
                FinishGlitch();

            if (_current.IsBossRoom)
                _loopManager.OnBossDefeated();
            else
            {
                SpawnExit();
                if (ExitPosition.HasValue)
                    TrySpawnRoomClearNpc(); // 안내는 같은 프레임의 "출구가 열렸다" 아래 줄에 붙는다(GameHUD.ShowBanner)
            }
        }

        private const int ExitMinDistance = 6;

        /// <summary>일반 방을 비우면 바로 넘어가지 않고 출구 칸을 연다(사용자 요청) - 플레이어에게서 떨어진 랜덤 빈 칸.
        /// 플레이어가 그 칸을 밟으면(이동/대시) TryUseExit이 다음 방으로 보낸다. 출구는 안개에 가려 있어서 직접 찾아야 한다(메시지로만 알림).</summary>
        private void SpawnExit()
        {
            // 위치는 랜덤(사용자 요청) - 단 플레이어 바로 옆이면 찾는 재미가 없으니 ExitMinDistance칸 이상 떨어진 빈 칸 중에서
            // 고르고, 그런 칸이 없으면 아무 빈 칸.
            var player = _player.GridPos;
            var far = new List<Vector2Int>();
            var any = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (!Map.IsWalkable(p))
                    continue;
                any.Add(p);
                if (Mathf.Abs(p.x - player.x) + Mathf.Abs(p.y - player.y) >= ExitMinDistance)
                    far.Add(p);
            }
            var pool = far.Count > 0 ? far : any;
            Vector2Int? best = pool.Count > 0 ? pool[Rng.Next(pool.Count)] : (Vector2Int?)null;
            if (!best.HasValue)
            {
                _loopManager.AdvanceToNextRoom(); // 안전망 - 빈 칸이 하나도 없으면 예전처럼 바로 이동
                return;
            }

            ExitPosition = best.Value;
            var go = new GameObject("Exit");
            go.transform.SetParent(transform, false);
            // 내려가는 던전 계단 도트(Resources/Tiles/Stairs) - 그림이 없으면 예전 파란 칸
            var stairs = Resources.Load<Sprite>("Tiles/Stairs");
            ExitRenderer = stairs != null
                ? VisualUtil.CreateSpriteVisual(go, stairs, GridConstants.CellSize, sortingOrder: -6)
                : VisualUtil.CreateSquareVisual(go, ExitColor, GridConstants.CellSize * 0.85f, sortingOrder: -6);
            go.transform.position = new Vector3(best.Value.x * GridConstants.CellSize, best.Value.y * GridConstants.CellSize, 0f);
            if (stairs == null)
                go.AddComponent<ExitMarker>(); // 예전 파란 칸만 깜빡임 - 계단은 늘 밝게(사용자 요청)
            _roomObjects.Add(go);
            ShowMessage(stairs != null ? Loc.T("출구가 열렸다! (계단)") : Loc.T("출구가 열렸다! (파란 칸)"));
        }

        /// <summary>플레이어가 pos로 막 옮겨왔을 때 - 출구면 다음 방으로 보내고 true(호출부는 이번 턴을 그대로 끝낸다).</summary>
        public bool TryUseExit(Vector2Int pos)
        {
            if (!ExitPosition.HasValue || ExitPosition.Value != pos)
                return false;
            ExitPosition = null;
            ExitUseCount++;
            _loopManager.AdvanceToNextRoom();
            return true;
        }

        public void RunEnemyTurns()
        {
            var nested = EnemyTurnWatch.IsRunning;
            EnemyTurnWatch.Start();
            try { RunEnemyTurnsCore(); }
            finally { if (!nested) EnemyTurnWatch.Stop(); }
        }

        private void RunEnemyTurnsCore()
        {
            if (Puzzle != null && !Puzzle.IsRunning)
                return; // 퍼즐이 방금 실패했다(실패 창이 떠 있는 동안 몹이 때리지 않게)
            // 스냅샷을 떠서 순회한다 - 턴 도중 죽거나 소환돼서 리스트가 바뀌어도 이번 순회엔 영향 없게.
            // 플레이어에게 가까운 몹부터 움직여서 가까운 몹이 가까운 옆 칸을 먼저 예약하게 한다(포위 AI).
            var playerPos = ActiveDecoy != null ? ActiveDecoy.GridPos : _player.GridPos; // 미끼가 있으면 미끼 기준으로 포위
            var snapshot = _enemies
                .Where(e => e != null)
                .OrderBy(e => Mathf.Abs(e.GridPos.x - playerPos.x) + Mathf.Abs(e.GridPos.y - playerPos.y))
                .ToArray();

            // 이미 붙어 있는 근접 몹의 칸은 먼저 예약해둔다 - 다른 몹이 그 칸을 목표로 삼지 않게.
            var claimedSlots = new HashSet<Vector2Int>();
            foreach (var enemy in snapshot)
            {
                if (enemy.Kind != EnemyKind.Ranged && enemy.IsAdjacentTo(playerPos))
                    claimedSlots.Add(enemy.GridPos);
            }

            var passes = 1;
            if (GlitchRoom.Active == GlitchRoom.Rule.DoubleTurn && ++GlitchRoom.EnemyPhase % 2 == 0)
                passes = 2; // 오류방 - 두 턴에 한 번 더 움직인다
            for (var pass = 0; pass < passes; pass++)
            foreach (var enemy in snapshot)
            {
                if (_roomCleared)
                    return; // 반사 부적 등으로 이번 몹 턴 도중 보스가 죽어 방이 끝났다 - 남은 졸개는 행동하지 않는다
                if (enemy == null || enemy.Stats.IsDead || !_enemies.Contains(enemy))
                    continue; // 보스와 함께 치워진 졸개(Destroy는 프레임 끝이라 아직 null이 아니다)

                enemy.TakeTurn(_player, claimedSlots, this);
                CheckTrap(enemy);
                CheckOgreCrack(enemy); // 2층 오우거 - 균열에 빠짐

                if (_player.Stats.IsDead)
                {
                    ClearAllTelegraphs();
                    if (Puzzle != null)
                        Puzzle.PlayerKilled(); // 퍼즐 안에선 죽지 않고 실패(체력은 퍼즐 시작 때로)
                    else
                        _loopManager.OnPlayerDied();
                    return;
                }
            }

            // 가시로 죽은 몹은 몹 턴이 다 끝난 뒤에 처치 처리(턴 도중에 방이 넘어가지 않게).
            var thornsKills = _thornsKills.ToArray();
            _thornsKills.Clear(); // 처치 도중 방이 넘어가도 같은 몹을 다음 턴에 또 처치하지 않게 먼저 비운다
            foreach (var dead in thornsKills)
            {
                if (dead != null && _player.ClaimKill(dead))
                    return;
            }

            EndOfEnemyTurns();
            Puzzle?.TurnEnd();
        }

        /// <summary>몹 턴이 다 끝난 뒤 - 던져둔 폭탄이 터지고, 미끼 남은 턴이 준다.</summary>
        private void EndOfEnemyTurns()
        {
            if (ActiveDecoy != null && ActiveDecoy.Tick())
            {
                Map.RemoveActor(ActiveDecoy);
                Destroy(ActiveDecoy.gameObject);
                ActiveDecoy = null;
            }

            if (_playerBombs.Count == 0)
                return;
            var bombs = _playerBombs.ToArray();
            _playerBombs.Clear();
            foreach (var bomb in bombs)
            {
                foreach (var go in bomb.Objects)
                    if (go != null)
                        Destroy(go);
                if (ExplodePlayerBomb(bomb.Center))
                    return; // 방 전환
            }
        }

        // ===================== 아이템이 방에 하는 일 =====================

        public bool CanPlaceAt(Vector2Int pos) => Map.IsWalkable(pos) && ExitPosition != pos;

        /// <summary>두 번째 숨 부활 위치 - 빈 칸 중 예고(보스 패턴·폭발) 칸, 출구, 덫을 빼고, 궁수·거미가 같은 줄
        /// 사거리에서 노리는 칸을 피하면서 가장 가까운 몹과 제일 먼 칸. 같으면 지금 자리에서 가까운 칸. 갈 데가 없으면 제자리.</summary>
        public Vector2Int FindSafeTile(Vector2Int from)
        {
            var alive = _enemies.Where(e => e != null && !e.Stats.IsDead).ToList();
            var best = from;
            var bestScore = float.MinValue;
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var pos = new Vector2Int(x, y);
                if (!Map.IsWalkable(pos) || ExitPosition == pos || _traps.ContainsKey(pos) || _dangerTiles.Contains(pos))
                    continue;

                var nearest = 99;
                var aimed = false;
                foreach (var e in alive)
                {
                    var dx = Mathf.Abs(e.GridPos.x - x);
                    var dy = Mathf.Abs(e.GridPos.y - y);
                    nearest = Mathf.Min(nearest, GridMap.FootprintDistance(e, pos)); // 3x3 보스는 몸 가장자리까지
                    if ((e.Kind == EnemyKind.Ranged || e.Kind == EnemyKind.Spider) && (dx == 0 || dy == 0) && dx + dy <= EnemyActor.RangedAttackRange)
                        aimed = true;
                }

                // 몹과 6칸이면 충분히 안전 - 그 이상은 똑같이 치고 덜 움직이는 쪽을 고른다.
                var score = Mathf.Min(nearest, 6) * 100f - (aimed ? 250f : 0f)
                            - (Mathf.Abs(x - from.x) + Mathf.Abs(y - from.y));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = pos;
                }
            }
            return best;
        }

        public void PlaceTorch(Vector2Int pos)
        {
            _torches.Add(pos);
            var go = new GameObject("Torch");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x * GridConstants.CellSize, pos.y * GridConstants.CellSize, 0f);
            if (Fx.Play("obj_torch", go.transform.position, 0.8f, fps: 8f, loop: true, sortingOrder: -6, parent: go.transform) == null
                && VisualUtil.TryCreateProp(go, "Torch", sortingOrder: -6) == null)
            {
                VisualUtil.CreateSquareVisual(go, new Color(1f, 0.7f, 0.2f), GridConstants.CellSize * 0.3f, sortingOrder: -6);
                go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            _roomObjects.Add(go);
        }

        public void PlaceTrap(Vector2Int pos)
        {
            var go = new GameObject("Trap");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(pos.x * GridConstants.CellSize, pos.y * GridConstants.CellSize, 0f);
            if (Fx.Play("obj_trap", go.transform.position, 0.8f, fps: 4f, loop: true, sortingOrder: -6, parent: go.transform) == null
                && VisualUtil.TryCreateProp(go, "Trap", sortingOrder: -6) == null)
                VisualUtil.CreateSquareVisual(go, new Color(0.55f, 0.55f, 0.6f, 0.9f), GridConstants.CellSize * 0.5f, sortingOrder: -6);
            _roomObjects.Add(go);
            _traps[pos] = go;
        }

        private readonly List<EnemyActor> _thornsKills = new List<EnemyActor>();

        /// <summary>갑옷 "가시"(신화 이상) - 근접으로 플레이어를 때린 몹에게 받은 피해의 일부를 돌려준다.</summary>
        public void ApplyThorns(EnemyActor attacker, float dealt)
        {
            if (attacker == null || dealt <= 0f || attacker.Stats.IsDead || !EquipmentEffects.Has(ItemSlot.Armor, 2))
                return;
            var damage = dealt * EquipmentEffects.ThornsRate;
            attacker.Stats.TakeDamage(damage);
            DamagePopup.Spawn(attacker.transform.position, damage, new Color(0.75f, 0.85f, 0.6f));
            if (attacker.Stats.IsDead)
                _thornsKills.Add(attacker);
        }

        private void CheckTrap(EnemyActor enemy)
        {
            if (enemy == null || enemy.Stats.IsDead)
                return;
            // 몸이 덮은 칸 중 덫이 있는 칸(3x3 보스는 몸 어디로든 밟는다)
            var trapCell = GridMap.CellsOf(enemy).FirstOrDefault(c => _traps.ContainsKey(c));
            if (!_traps.TryGetValue(trapCell, out var go) || !GridMap.Occupies(enemy, trapCell))
                return;
            _traps.Remove(trapCell);
            if (go != null)
                Destroy(go);
            enemy.Stun(ItemInfo.TrapStunTurns);
            ShowMessage(Loc.F("{0}이(가) 덫에 걸렸다! ({1}턴 기절)", enemy.DisplayName, ItemInfo.TrapStunTurns));
        }

        public void PlaceDecoy(Vector2Int pos)
        {
            if (ActiveDecoy != null)
            {
                Map.RemoveActor(ActiveDecoy);
                Destroy(ActiveDecoy.gameObject);
            }
            var go = new GameObject("Decoy");
            go.transform.SetParent(transform, false);
            ActiveDecoy = go.AddComponent<DecoyActor>();
            ActiveDecoy.Initialize(ItemInfo.DecoyTurns);
            Map.PlaceActor(ActiveDecoy, pos);
        }

        /// <summary>폭탄을 던져둔다 - 이번 몹 턴이 끝나면(다음 턴) 터진다. 노란 예고는 표시만(플레이어는 안 맞음).</summary>
        public void ThrowBomb(Vector2Int center)
        {
            var objects = new List<GameObject>();
            foreach (var t in BlastArea(center))
                objects.Add(SpawnTelegraphTile(t, PlayerBombColor, -4));
            // 떨어진 폭탄(심지 불꽃) - 터지면 예고 칸과 같이 지워진다. 이펙트가 없으면 정지 폭탄 그림.
            var bombPos = new Vector3(center.x * GridConstants.CellSize, center.y * GridConstants.CellSize, 0f);
            var bomb = Fx.Play("obj_bomb", bombPos, 0.7f, fps: 10f, loop: true, sortingOrder: 2, parent: transform);
            if (bomb != null)
                objects.Add(bomb.gameObject);
            else
            {
                var bombGo = new GameObject("Bomb");
                bombGo.transform.SetParent(transform, false);
                if (VisualUtil.TryCreateProp(bombGo, "Bomb", sortingOrder: -3) != null)
                {
                    bombGo.transform.position = bombPos;
                    objects.Add(bombGo);
                }
                else
                    Destroy(bombGo);
            }
            _playerBombs.Add((center, objects));
        }

        private IEnumerable<Vector2Int> BlastArea(Vector2Int center)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                var p = center + new Vector2Int(dx, dy);
                if (Map.IsInBounds(p))
                    yield return p;
            }
        }

        /// <summary>플레이어 폭탄 폭발 - 3x3 안 몹에게 피해(방패 무시, 약점 표식 적용), 벽은 부순다. 방이 넘어가면 true.</summary>
        private bool ExplodePlayerBomb(Vector2Int center)
        {
            var area = BlastArea(center).ToList();
            HitFeedback.OnExplosion(new Vector3(center.x * GridConstants.CellSize, center.y * GridConstants.CellSize, 0f));

            foreach (var t in area)
            {
                if (Map.IsWall(t))
                    RemoveWallObject(t); // 멧돼지 기둥도 폭탄으로 부서진다
            }

            var damage = _player.Stats.AttackPower * ItemInfo.BombDamageRate;
            foreach (var enemy in _enemies.Where(e => e != null && !e.Stats.IsDead && area.Any(t => GridMap.Occupies(e, t))).ToArray())
            {
                var dealt = damage * enemy.DamageTakenMultiplier;
                enemy.Stats.TakeDamage(dealt);
                DamagePopup.Spawn(enemy.transform.position, dealt, new Color(1f, 0.85f, 0.3f));
                if (enemy.Stats.IsDead && _player.ClaimKill(enemy))
                    return true;
            }
            return false;
        }

        /// <summary>반사 부적 등 아이템으로 보스가 깎였을 때 - 죽었으면 처치 처리.</summary>
        public void HandleBossDamagedByItem(EnemyActor boss)
        {
            if (boss != null && boss.Stats.IsDead)
                _player.ClaimKill(boss);
        }

        /// <summary>주변(체비쇼프 반경) 살아있는 몹 - 연막탄/섬광탄/약점 표식용.</summary>
        public List<EnemyActor> EnemiesWithin(Vector2Int center, int radius) =>
            _enemies.Where(e => e != null && !e.Stats.IsDead && GridMap.FootprintChebyshev(e, center) <= radius).ToList(); // 몸 가장자리 기준
    }
}
