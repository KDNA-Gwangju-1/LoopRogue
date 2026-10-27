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
        /// <summary>보스 러시(BossRush) - 이 보스방의 층(보스 종류·기믹). 0이면 지금 층.</summary>
        public int BossStage;
        /// <summary>트레일러 촬영 모드의 잡몹 방 - 이 방의 층(그림·기준 스펙). BossStage와 달리 기믹 컷신은 없다. 0이면 지금 층.</summary>
        public int SpecStage;

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
    public partial class RoomController : MonoBehaviour // 4~9층 보스 기믹은 RoomController.BossGimmicks.cs
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
        private const float SentinelHealthRatio = 1.5f; // 반격병 - 튼튼하지만 자세를 피해 때리면 금방
        private const float SentinelAttackRatio = 0.9f;
        private const float CasterHealthRatio = 0.6f;   // 주문술사 - 8턴 안에 잡을 수 있게 약하게
        private const float CasterAttackRatio = 1f;

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
        /// <summary>자동 플레이 봇 인계(보스방을 새로 깔기)용.</summary>
        public LoopManager Loop => _loopManager;
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
        public PuzzleBase Puzzle
        {
            get => _puzzle;
            private set
            {
                _puzzle = value;
                CharacterStats.RevivesSuspended = value != null; // 퍼즐 안 죽음은 실패일 뿐 - 한 번뿐인 부활을 거기서 쓰지 않게
            }
        }

        private PuzzleBase _puzzle;

        private void OnDestroy() => CharacterStats.RevivesSuspended = false; // 퍼즐 도중 로비로 나가거나 다시 지어도 다음 판에 남지 않게

        /// <summary>지금 방 청사진(퍼즐이 그 방 몹 종류·세기를 빌려 쓴다).</summary>
        public RoomDefinition CurrentDefinition => _current;

        /// <summary>봇·통계용 - 퍼즐 성공/실패 누적.</summary>
        public static int PuzzleClearCount, PuzzleFailCount;

        /// <summary>전투 대신 퍼즐로 방을 깐다(안개 없음, 몹·이벤트 없음). 같은 퍼즐을 다시 도전할 때도 이걸 다시 부른다(배치는 새로).</summary>
        public void LoadPuzzle(RoomDefinition def, PuzzleType type)
        {
            _current = def;
            ClearRoom();
            MusicPlayer.Play((int)type >= (int)PuzzleType.ShellGame ? MusicPlayer.MiniGame : MusicPlayer.Puzzle); // 퍼즐 5종 / 미니게임 5종

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
            PlayLog.RoomEntered(_stage, def, $"퍼즐 {type}", _player);
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
            PlayLog.PuzzleResult(puzzle.Type, true, null);
            PlayLog.RoomCleared(_player, true);
            if (!_current.IsTestRoom)
                StageProgress.MarkRoomCleared(_current.RoomNumber);
            var rewards = puzzle.UsedHint ? new List<string>() : GrantPuzzleRewards(puzzle.Type); // 힌트를 쓰면 골드·영약 없음
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
            PlayLog.PuzzleResult(puzzle.Type, false, reason);
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

        /// <summary>이 방에 들어온 뒤 플레이어가 받은 피해가 없는지 - 층 보스 무피격 업적(LoopManager.OnBossDefeated).</summary>
        public bool NoDamageThisRoom => CharacterStats.IncomingDamageTotal <= _roomDamageStart;
        private double _roomDamageStart;

        private void LoadRoomCore(RoomDefinition def)
        {
            _current = def;
            _roomDamageStart = CharacterStats.IncomingDamageTotal;
            TrailerControls.OnRoomLoaded();
            BossRush.TrailerFinal = BossRush.Trailer && def.BossStage >= BossRush.LastStage;
            ClearRoom();
            if (def.BossStage > 0)
                _stage = def.BossStage; // 보스 러시 - 보스 그림·패턴·기믹이 이 층 것으로
            else if (def.SpecStage > 0)
                _stage = def.SpecStage; // 트레일러 잡몹 방 - 그 층 바닥·몹 그림
            if (def.IsBossRoom && !_loadCleared)
                MusicPlayer.Play(MusicPlayer.Boss(_stage)); // 층별 보스 배경음악(파일이 없으면 조용히)
            else
                MusicPlayer.Play(MusicPlayer.Dungeon); // 일반 방(같은 곡이면 방을 옮겨도 끊기지 않는다)

            var layout = def.TutorialLayout ?? (def.IsTestRoom ? TestMap.Layout() : RoomLayoutGenerator.Generate(def, Rng));
            Map = new GridMap(layout.Width, layout.Height);
            foreach (var wall in layout.Walls)
                Map.AddWall(wall);

            var glitch = !_loadCleared && GlitchRoom.ShouldGlitch(def, GameBootstrap.RoomsPerStage);
            DungeonTiles.Glitch = glitch; // 오류방 - 깨진 바닥·벽 타일을 섞어 깐다(10-19)
            try { BuildFloor(layout, def.IsBossRoom); }
            finally { DungeonTiles.Glitch = false; }
            _fitRoom = null; // 일반 방 - 플레이어 주변만 비추는 평소 카메라
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
            _knightPending = !_loadCleared && !glitch && !BossRush.Active && MemoryKnight.ShouldSpawn(def, _stage, Rng);

            if (def.HasEvent && layout.EventPosition.HasValue && !_loadCleared)
                SpawnEvent(layout.EventPosition.Value, def.EventType);
            if (def.IsTestRoom)
                foreach (var (pos, type) in TestMap.Events)
                    SpawnEvent(pos, type);
            TrySpawnSecretWall(def, layout);
            TrySpawnAltar(def, layout); // 횃불 → 벽화의 방(층마다 한 번)

            _thornsKills.Clear();
            _challengeEnemies.Clear();
            _roomCleared = false;
            if (def.BossStage > 0 || def.SpecStage > 0)
                BossRush.ApplyStageSpec(def.BossStage > 0 ? def.BossStage : def.SpecStage, _player); // 보스 러시 - 그 층 기준 스펙으로(보호막 계산 전에)
            _player.OnRoomEntered(); // 갑옷 효과(보호막/불굴/응급 처치) 방마다 다시 채움
            IsInputLocked = false;
            if (def.BossStage > 0 && !_loadCleared)
                StartCoroutine(StartRushGimmick()); // 보스 러시 - 들어가자마자 기믹 컷신
            if (glitch)
                StartGlitch(glitchTiles);
            PlayLog.RoomEntered(_stage, def, PlayLog.DescribeRoom(def, _loadCleared), _player);
            if (_loadCleared)
            {
                _roomCleared = true; // 퍼즐 실패 후 돌아온 이전 방 - 이미 깬 방이라 출구만
                SpawnExit();
            }
            else if (def.IsTutorialRoom && _enemies.Count == 0)
                FinishRoomCleared(); // 튜토리얼 첫 방(몹 없음) - 들어가자마자 출구
        }

        /// <summary>보스 러시 - 방이 다 깔린 다음 프레임에 보스 기믹을 바로 시작.</summary>
        private IEnumerator StartRushGimmick()
        {
            yield return null;
            var boss = _enemies.FirstOrDefault(e => e != null && e.IsBoss && !e.Stats.IsDead);
            if (boss != null && !_player.Stats.IsDead)
                boss.StartGimmickNow(this);
        }

        /// <summary>몹 종류별 체력·공격력 배율(기준 몹 대비).</summary>
        private static (float Hp, float Atk) KindRatios(EnemyKind kind) => kind switch
        {
            EnemyKind.Ranged => (RangedHealthRatio, RangedAttackRatio),
            EnemyKind.Shield => (ShieldHealthRatio, ShieldAttackRatio),
            EnemyKind.Spider => (SpiderHealthRatio, SpiderAttackRatio),
            EnemyKind.Bomber => (BomberHealthRatio, BomberAttackRatio),
            EnemyKind.Healer => (HealerHealthRatio, HealerAttackRatio),
            EnemyKind.Charger => (ChargerHealthRatio, ChargerAttackRatio),
            EnemyKind.Splitter => (SplitterHealthRatio, SplitterAttackRatio),
            EnemyKind.Assassin => (AssassinHealthRatio, AssassinAttackRatio),
            EnemyKind.Sentinel => (SentinelHealthRatio, SentinelAttackRatio),
            EnemyKind.Caster => (CasterHealthRatio, CasterAttackRatio),
            _ => (1f, 1f),
        };

        private EnemyActor SpawnEnemy(Vector2Int pos, RoomDefinition def, EnemyKind kind, float healthScale = 1f, float attackScale = 1f)
        {
            if (def.IsBossRoom)
                kind = EnemyKind.Melee;
            var go = new GameObject(def.IsBossRoom ? "Boss" : kind == EnemyKind.Melee ? "Enemy" : kind.ToString());
            go.transform.SetParent(transform, false);

            var (hpRatio, atkRatio) = KindRatios(kind);
            var enemy = go.AddComponent<EnemyActor>();
            var maxHealth = def.IsTutorialRoom ? 1f : def.EnemyMaxHealth * hpRatio * healthScale; // 튜토리얼 몹 - 종류 상관없이 체력 1
            enemy.Initialize(maxHealth, def.EnemyAttackPower * atkRatio * attackScale, def.IsBossRoom, kind);
            if (def.IsTutorialRoom)
                enemy.MarkAsTutorialMob(); // 골드·경험치 없음
            if (def.IsBossRoom)
                enemy.SetupBossPatterns(def.BossPatterns, _stage);
            if (def.IsBossRoom && def.BossStage > 0)
                // 보스 러시 - 기믹 체력으로 시작. 딱 그 값이면 0.8 같은 비율은 소수점 오차로 "이하" 판정을 빗나갈 수 있어서(3층 골렘 컷신이 안 나왔다) 1 더 깎는다.
                enemy.Stats.CurrentHealth = Mathf.Max(1f, enemy.Stats.MaxHealth * BossBrain.GimmickStartHealthRatio(def.BossStage) - 1f);
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
            if (type == RoomEventType.Mimic)
                StartCoroutine(PreloadMimic());
        }

        /// <summary>미믹이 있는 방 - 상자 괴물 그림(4방향 대기·걷기·공격, 200장 가까이)과 소환 이펙트를 방이 깔린 뒤 한 프레임에 한 묶음씩 미리 읽는다.
        /// 예전엔 상자를 밟아 미믹이 튀어나오는 순간 한꺼번에 읽느라 화면이 멈췄다(10-20 사용자 "미믹 소환 때 렉"). 10층 기사 변신(PreloadMirrorForms)과 같은 방식.</summary>
        private IEnumerator PreloadMimic()
        {
            yield return null;
            SpriteAnimator.Preload(EnemyActor.MimicSpriteSet);
            yield return null;
            Fx.Frames("summon");
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
            PlayLog.EventTriggered(ev.Type, message);
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
        public bool IsRoomCleared => _roomCleared;

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
            MusicPlayer.Play(MusicPlayer.Knight); // 기사 전용 곡(파일이 없으면 조용히) - 방을 나가거나 잡으면 일반 방 곡
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
                MusicPlayer.Play(MusicPlayer.Dungeon); // 기사 곡 → 일반 방 곡
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

        public void ShowMessage(string message)
        {
            PlayLog.Message(message);
            _loopManager.ShowMessage(message);
        }

        /// <summary>상하좌우 걸음 수 지도(벽만 막힘, 몹은 무시) - starts에서 각 칸까지. 못 가는 칸은 -1.</summary>
        private static int[,] WalkSteps(GridMap map, IEnumerable<Vector2Int> starts)
        {
            var dist = new int[map.Width, map.Height];
            for (var x = 0; x < map.Width; x++)
            for (var y = 0; y < map.Height; y++)
                dist[x, y] = -1;
            var queue = new Queue<Vector2Int>();
            foreach (var s in starts)
                if (map.IsInBounds(s) && dist[s.x, s.y] < 0)
                {
                    dist[s.x, s.y] = 0;
                    queue.Enqueue(s);
                }
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (!map.IsInBounds(n) || dist[n.x, n.y] >= 0 || map.IsWall(n))
                        continue;
                    dist[n.x, n.y] = dist[c.x, c.y] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>보스 몸에 상하좌우로 붙은 칸들(흡수 자리).</summary>
        private List<Vector2Int> AdjacentCells(EnemyActor boss)
        {
            var list = new List<Vector2Int>();
            var r = boss.FootprintReach + 1;
            for (var dx = -r; dx <= r; dx++)
            for (var dy = -r; dy <= r; dy++)
            {
                var p = boss.GridPos + new Vector2Int(dx, dy);
                if (Map.IsInBounds(p) && !Map.IsWall(p) && GridMap.FootprintDistance(boss, p) == 1)
                    list.Add(p);
            }
            return list;
        }

        /// <summary>보스 소환 - 보스 주변 빈 칸에 졸개를 (살아있는 졸개 포함 최대 maxAlive마리까지) 만든다.</summary>
        public void SpawnMinions(EnemyActor boss, int maxAlive)
        {
            var alive = _enemies.Count(e => e != null && e.IsMinion && !e.Stats.IsDead);
            var toSpawn = maxAlive - alive;
            if (toSpawn <= 0)
                return;

            // 보스 몸 바로 둘레 한 줄(3x3 보스면 5x5 테두리) - 4층 흡수 단계면 멀리(킹까지 기어가야 흡수된다)
            var cells = new List<Vector2Int>();
            if (SpawnMinionsFar)
            {
                // 4층 흡수 단계 - 졸개가 킹에게 붙기까지 걸음 수와 플레이어가 졸개에게 붙기까지 걸음 수가 같은 칸(10-14 사용자).
                // 멀면 못 잡고 가까우면 거저 잡는다 - 걸음이 같으면 바로 쫓아가야 막는 경주가 된다. 같은 칸이 없으면 차이가 가장 작은 칸들.
                var toKing = WalkSteps(Map, AdjacentCells(boss));
                var toPlayer = WalkSteps(Map, new[] { _player.GridPos });
                var scored = new List<(Vector2Int P, int Diff)>();
                for (var x = 0; x < Map.Width; x++)
                for (var y = 0; y < Map.Height; y++)
                {
                    var p = new Vector2Int(x, y);
                    if (!Map.IsWalkable(p) || GridMap.FootprintChebyshev(boss, p) < 4 || ExitPosition == p
                        || toKing[x, y] < 0 || toPlayer[x, y] < 2)
                        continue;
                    scored.Add((p, Mathf.Abs((toPlayer[x, y] - 1) - toKing[x, y])));
                }
                if (scored.Count > 0)
                {
                    var best = scored.Min(s => s.Diff);
                    cells.AddRange(scored.Where(s => s.Diff <= best + (toSpawn > 1 ? 1 : 0)).Select(s => s.P)); // 여러 마리면 한 칸 차이까지(한데 뭉치지 않게)
                }
            }
            // 둘레 한 줄 전부를 후보로 - 예전엔 반복문 조건에 cells.Count == 0이 붙어 첫 세로줄(보스 왼쪽)만 모으고 멈췄다.
            // 짝수 몸(2x2)도 몸에서 한 칸 떨어진 칸만(FootprintChebyshev).
            if (cells.Count == 0)
            {
                var ring = boss.FootprintReach + 2;
                for (var dx = -ring; dx <= ring; dx++)
                for (var dy = -ring; dy <= ring; dy++)
                {
                    var p = boss.GridPos + new Vector2Int(dx, dy);
                    if (Map.IsWalkable(p) && GridMap.FootprintChebyshev(boss, p) == 1)
                        cells.Add(p);
                }
            }

            // 10층 기사(슬라임 킹을 비출 때)의 졸개는 슬라임(기본 근접 몹)으로, 세기는 10층 마지막 방 몹과 같게(10-19 사용자).
            var strong = _stage >= BossBrain.MirrorStage;
            var lastRoom = strong ? GameBootstrap.LastRoomEnemySpec(_stage) : default;
            foreach (var pos in cells.OrderBy(_ => Rng.Next()).Take(toSpawn))
            {
                var go = new GameObject("Minion");
                go.transform.SetParent(transform, false);
                var minion = go.AddComponent<EnemyActor>();
                if (strong)
                    minion.Initialize(lastRoom.Hp, lastRoom.Atk, isBoss: false, EnemyKind.Melee);
                else
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
                _glitchVisual.Init(tiles, this);
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

        /// <param name="color">예고 칸 색 - 없으면 기본 빨강(포위 폭발처럼 "대시로 피하라"는 패턴은 주황).</param>
        /// <param name="display">보이는 칸이 실제 칸과 다를 때(5층 환각 - 거꾸로 보임). 없으면 tiles 그대로 보인다. 위험 판정은 늘 tiles.</param>
        /// <param name="pattern">보스 패턴 - 패턴 전용 예고 그림(Effects/tel_*)과 예고 효과음(Sounds/Telegraph/패턴 이름)을 고른다(10-23).</param>
        /// <param name="direction">돌진처럼 방향이 있는 패턴 - 예고 그림을 그쪽으로 돌린다.</param>
        public void ShowTelegraph(IEnumerable<Vector2Int> tiles, Color? color = null, IEnumerable<Vector2Int> display = null,
            BossPatternType? pattern = null, Vector2Int? direction = null)
        {
            ClearTelegraph(rebuild: false);
            HitFeedback.OnTelegraph(pattern);
            foreach (var t in tiles)
                _bossDangerTiles.Add(t);
            var style = BossBrain.TelegraphStyle(pattern);
            var angle = direction.HasValue ? Fx.Angle(direction.Value) : 0f;
            foreach (var t in display ?? tiles)
                _telegraphObjects.Add(SpawnTelegraphTile(t, color ?? TelegraphColor, -5, style, angle));
            RebuildDangerTiles();
        }

        /// <summary>순서 예고 색(10-23 사용자 - 숫자 대신 색으로만): 바로 다음 빨강 → 주황 → 노랑 → 그 뒤는 옅은 노랑.</summary>
        private static readonly Color[] TelegraphWaveColors =
        {
            new Color(1f, 0.15f, 0.15f, 0.95f), new Color(1f, 0.55f, 0.1f, 0.75f), new Color(1f, 0.95f, 0.3f, 0.5f), new Color(1f, 0.95f, 0.55f, 0.32f),
        };

        /// <summary>거울 파편 - 여러 웨이브를 한꺼번에 예고. 칸마다 몇 번째에 터지는지 색으로(빨강 → 주황 → 노랑 → 옅은 노랑, 10-23 숫자는 뺐다),
        /// 나중 칸은 옅은 보라. 실제 위험 칸(봇·위험 표시 기준)은 바로 다음 웨이브만.</summary>
        public void ShowTelegraphWaves(IReadOnlyList<HashSet<Vector2Int>> waves, int firstNumber, BossPatternType? pattern = null)
        {
            ClearTelegraph(rebuild: false);
            HitFeedback.OnTelegraph(pattern);
            if (waves.Count == 0)
                return;
            _bossDangerTiles.UnionWith(waves[0]);
            var first = new Dictionary<Vector2Int, int>(); // 칸마다 가장 먼저 터지는 웨이브
            for (var i = 0; i < waves.Count; i++)
                foreach (var t in waves[i])
                    if (!first.ContainsKey(t))
                        first[t] = i;
            var style = BossBrain.TelegraphStyle(pattern);
            foreach (var pair in first)
            {
                var color = TelegraphWaveColors[Mathf.Min(pair.Value, TelegraphWaveColors.Length - 1)];
                _telegraphObjects.Add(SpawnTelegraphTile(pair.Key, color, pair.Value == 0 ? -5 : -6, style));
            }
            RebuildDangerTiles();
        }

        /// <summary>보스 예고만 지운다(폭발병 예고는 그대로).</summary>
        public void ClearTelegraph() => ClearTelegraph(rebuild: true);

        /// <summary>rebuild = false면 위험 칸 다시 계산은 호출부가(바로 새 예고를 깔 때 두 번 하지 않게).</summary>
        private void ClearTelegraph(bool rebuild)
        {
            foreach (var go in _telegraphObjects)
                Fx.Release(go); // 지우지 않고 풀로 - 다음 예고가 그대로 다시 쓴다
            _telegraphObjects.Clear();
            _bossDangerTiles.Clear();
            if (rebuild)
                RebuildDangerTiles();
        }

        /// <summary>보스 예고 + 폭발 예고 전부 - 방을 비우거나 플레이어가 죽을 때.</summary>
        private void ClearAllTelegraphs()
        {
            foreach (var bomb in _bombs.Values)
                foreach (var go in bomb.Objects)
                    Fx.Release(go);
            _bombs.Clear();
            DestroyPlayerBombObjects(); // 던져 놓고 안 터진 폭탄(같은 턴에 출구로 나감·사망)도 - 예전엔 목록만 비워 그림이 다음 방까지 남았다
            ClearTelegraph();
        }

        private void DestroyPlayerBombObjects()
        {
            foreach (var bomb in _playerBombs)
                foreach (var go in bomb.Objects)
                    Fx.Release(go);
            _playerBombs.Clear();
        }

        private GameObject SpawnTelegraphTile(Vector2Int t, Color color, int sortingOrder, string style = null, float angle = 0f)
        {
            var position = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f);
            // 도트 예고 칸(흰 그림에 색을 곱함 - 보스 = 빨강, 폭발 = 주황, 깜빡임) - 그림이 없거나 봇 중이면 예전 사각형.
            // 패턴 전용 그림(Effects/tel_*, 10-23 - 돌진 화살표·강타 갈라진 땅·파동 고리·십자)이 있으면 그것, 방향 있는 패턴은 angle만큼 돌린다.
            var name = style != null && Fx.Frames(style).Length > 0 ? style : "telegraph";
            var fx = Fx.Play(name, position, 0.95f, angle, tint: new Color(color.r, color.g, color.b, 0.95f), fps: 6f, loop: true,
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
            foreach (var tel in _crystalTelegraphs.Values)
                _dangerTiles.UnionWith(tel.Tiles);
            _dangerTiles.UnionWith(_beatTiles); // 7층 그림자 박자(10-23)
        }

        /// <summary>폭발병이 불을 붙였을 때 - 3x3 주황 예고.</summary>
        public void ShowBombTelegraph(EnemyActor bomber, List<Vector2Int> tiles) => ShowEnemyTelegraph(bomber, tiles, BombTelegraphColor);

        /// <summary>돌진병 돌진 예고 색 - 보스 예고(빨강)보다 조금 주황 쪽.</summary>
        public static readonly Color ChargeTelegraphColor = new Color(1f, 0.3f, 0.2f, 0.45f);

        /// <summary>일반 몹 하나의 예고 칸(폭발병 3x3, 돌진병 돌진 줄) - 몹마다 따로 들고 있다가 발동하거나 그 몹이 죽으면 지운다.</summary>
        public void ShowEnemyTelegraph(EnemyActor enemy, List<Vector2Int> tiles, Color color, string style = null)
        {
            ClearBombTelegraph(enemy);
            var objects = tiles.Select(t => SpawnTelegraphTile(t, color, -4, style)).ToList();
            _bombs[enemy] = (tiles, objects);
            RebuildDangerTiles();
        }

        public void ClearEnemyTelegraph(EnemyActor enemy) => ClearBombTelegraph(enemy);

        private void ClearBombTelegraph(EnemyActor bomber)
        {
            if (!_bombs.TryGetValue(bomber, out var bomb))
                return;
            foreach (var go in bomb.Objects)
                Fx.Release(go);
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
                PlayLog.PlayerHit($"{PlayLog.EnemyLabel(bomber)} 폭발", dealt, _player.Stats);
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
            var clip = RoomClip.Create(transform, layout.Width, layout.Height); // 이펙트가 맵 밖에 안 그려지게(10-19)
            if (clip != null)
                _roomObjects.Add(clip);
            CreateBackdrop(layout);
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

        // ---- 방 바깥 배경(10-19 사용자 "양옆이 빈다") - Backgrounds/Dungeon(128x128 = 4x4칸, 던전 벽돌·기둥·쇠사슬·횃불)을
        // 방 둘레로 넓게 반복해 깐다. 모든 층 같은 그림(사용자 결정), 방보다 어둡게 - 그림이 없으면 예전처럼 검은 화면.
        private const int BackdropMargin = 30; // 방 둘레로 몇 칸까지 까는지(카메라가 방 끝에 있어도 화면 밖까지)
        private const float BackdropBrightness = 0.75f;

        private void CreateBackdrop(RoomLayout layout)
        {
            if (DamagePopup.Suppressed)
                return;
            var sprite = Resources.Load<Sprite>("Backgrounds/Dungeon");
            if (sprite == null)
                return;
            var go = new GameObject("Backdrop");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3((layout.Width - 1) * 0.5f * GridConstants.CellSize, (layout.Height - 1) * 0.5f * GridConstants.CellSize, 0f);
            go.transform.localScale = Vector3.one * GridConstants.CellSize; // 그림 1유닛 = 1칸
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.drawMode = SpriteDrawMode.Tiled;
            r.tileMode = SpriteTileMode.Continuous;
            r.size = new Vector2(layout.Width + BackdropMargin * 2, layout.Height + BackdropMargin * 2);
            r.sortingOrder = -30; // 바닥(-10)·벽(-8) 아래
            r.color = new Color(BackdropBrightness, BackdropBrightness, BackdropBrightness);
            _roomObjects.Add(go);
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

        // ---- 숨겨진 보물 벽(사용자 구상 10-13 - 이스터에그) ----
        // 전투 방(방 2부터, 보스방 제외)을 깔 때마다 SecretWallChance 확률로 가장자리 쪽 빈 칸 하나에 금 간 벽(Tiles/WallCracked)을 세운다.
        // 폭탄으로 부수면 그 자리에 숨겨진 보물상자(RoomEventType.SecretChest). 층마다 한 번 - 찾으면 그 층에선 더 안 나온다.
        // (처음엔 회차에 한 곳으로 정해 뒀는데, 폭탄 수급이 운이라 그 방을 지날 때 폭탄이 없으면 놓치기 쉬워 방마다 10% + 층마다 한 번으로 바꿈.)
        public const float SecretWallChance = 0.1f;
        private const string SecretFoundStageKey = "LoopRogue_SecretWallFoundStage"; // 이번 회차에 찾은 마지막 층(그 층에선 더 안 나옴)
        private Vector2Int? _secretWall;

        private void TrySpawnSecretWall(RoomDefinition def, RoomLayout layout)
        {
            _secretWall = null;
            if (_loadCleared || def.IsBossRoom || def.IsTestRoom || def.IsTutorialRoom || def.RoomNumber < 2)
                return;
            if (PlayerPrefs.GetInt(SecretFoundStageKey, 0) == _stage || Rng.NextDouble() >= SecretWallChance)
                return;

            // 방 가장자리(또는 기존 벽 옆) 빈 칸 중, 플레이어 시작 칸에서 3칸 이상 떨어지고 주변 빈 칸이 2개 이상인 곳(길을 막지 않게)
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (!Map.IsWalkable(p) || Mathf.Max(Mathf.Abs(p.x - layout.PlayerStart.x), Mathf.Abs(p.y - layout.PlayerStart.y)) < 3)
                    continue;
                var edge = x == 0 || y == 0 || x == Map.Width - 1 || y == Map.Height - 1 || Directions4.Any(d => Map.IsWall(p + d));
                var open = Directions4.Count(d => Map.IsWalkable(p + d));
                if (edge && open >= 2)
                    cells.Add(p);
            }
            // 주변 빈 칸 수만 보면 구석 통로·출구 쪽 길을 막아 갇힌 공간이 생겼다(10-14 봇: 몹이 갇히거나 출구에 못 가 턴 제한 초과 6판)
            // → 세워도 방이 갈라지지 않는 칸만.
            Vector2Int? chosen = null;
            foreach (var c in cells.OrderBy(_ => Rng.Next()))
            {
                if (c != ExitPosition && WallKeepsConnected(c))
                {
                    chosen = c;
                    break;
                }
            }
            if (!chosen.HasValue)
                return;
            var cell = chosen.Value;
            Map.AddWall(cell);
            var sprite = Resources.Load<Sprite>("Tiles/WallCracked");
            GameObject go;
            if (_tiledRoom && sprite != null)
                go = CreateTile(transform, cell, sprite, -8, Color.white).gameObject;
            else
            {
                go = new GameObject("SecretWall");
                go.transform.SetParent(transform, false);
                VisualUtil.CreateSquareVisual(go, WallColor * 0.9f, GridConstants.CellSize, sortingOrder: -8);
                go.transform.position = new Vector3(cell.x * GridConstants.CellSize, cell.y * GridConstants.CellSize, 0f);
            }
            _roomObjects.Add(go);
            _wallObjects[cell] = go;
            _secretWall = cell;
        }

        private static readonly Vector2Int[] Directions4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// <summary>cell을 벽으로 막아도 방의 벽 아닌 칸(몹·플레이어·출구 자리 포함)이 전부 이어져 있는지 - 갇힌 공간이 생기면 false.
        /// 금 간 벽·골렘 수정·돌무더기처럼 방을 깐 뒤에 세우는 벽은 이걸 통과한 칸에만.</summary>
        private bool WallKeepsConnected(Vector2Int cell)
        {
            var total = 0;
            Vector2Int? start = null;
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (p == cell || Map.IsWall(p))
                    continue;
                total++;
                start ??= p;
            }
            if (!start.HasValue)
                return false;
            var seen = new HashSet<Vector2Int> { start.Value };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start.Value);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Directions4)
                {
                    var n = c + d;
                    if (n == cell || !Map.IsInBounds(n) || Map.IsWall(n) || !seen.Add(n))
                        continue;
                    queue.Enqueue(n);
                }
            }
            return seen.Count == total;
        }

        /// <summary>폭탄으로 숨겨진 벽이 부서졌다 - 그 자리에 숨겨진 보물상자.</summary>
        private void OnSecretWallBroken(Vector2Int cell)
        {
            _secretWall = null;
            PlayerPrefs.SetInt(SecretFoundStageKey, _stage);
            PlayerPrefs.Save();
            SpawnEvent(cell, RoomEventType.SecretChest);
            ShowMessage(Loc.T("<color=#FFD966>무너진 벽 너머에 무언가 있다!</color> 숨겨진 보물상자를 찾았다"));
            Achievements.Unlock("ACH_SECRET_WALL");
            if (HitFeedback.Enabled)
            {
                SfxPlayer.Play(Sfx.Appear);
                HitFeedback.OnSummon(new Vector3(cell.x * GridConstants.CellSize, cell.y * GridConstants.CellSize, 0f));
            }
        }

        // ---- 3층 돌 골렘 공명 수정·돌무더기(BossBrain) ----
        private readonly HashSet<Vector2Int> _golemCrystals = new HashSet<Vector2Int>();

        public bool IsGolemCrystal(Vector2Int p) => _golemCrystals.Contains(p);
        public int GolemCrystalCount => _golemCrystals.Count;
        private readonly HashSet<Vector2Int> _golemRubble = new HashSet<Vector2Int>(); // 폭발에 깨지는 바위(10-14)
        // 바위·수정이 어느 물결(StartGolemWave 한 번 = 한 물결, 합쳐진 물결도 하나)에 나왔는지 - 수정이 터지면 같은 물결 바위만 깨진다(10-15 사용자).
        // 전부 깨지게 했더니 앞 물결 수정이 터지면서 방금 떨어진 다음 물결 바위까지 사라져 숨을 곳이 없어졌다.
        private readonly Dictionary<Vector2Int, int> _rubbleWave = new Dictionary<Vector2Int, int>();
        private readonly Dictionary<Vector2Int, int> _crystalWave = new Dictionary<Vector2Int, int>(); // 값 = 물결 번호(1~4)

        // ---- 3층 골렘 수정 폭탄(10-14 사용자 컨셉): 수정이 솟으면 GolemCrystalFuseTurns턴 뒤 폭발 - 수정에서 직선으로 보이는 칸은 전부 즉사,
        // 벽·바위(돌무더기)·다른 수정에 가려진 칸만 안전. 붙어서 수정 쪽으로 움직이면 한 번에 깨진다(PlayerActor.TryAct). 터지기
        // CrystalWarnTurns턴 전부터 맞는 칸을 빨갛게 예고. 수정 위에 남은 턴.
        // 12 → 16(10-14, 봇 3층 보스방 사망 20/목표 4) → 20 → 물결마다 20·30·40·50(10-15 사용자)
        private static readonly int[] GolemCrystalFuseByWave = { 20, 30, 40, 50 };
        public static int GolemCrystalFuseTurns(int wave) => GolemCrystalFuseByWave[Mathf.Clamp(wave, 1, GolemCrystalFuseByWave.Length) - 1];
        public static string GolemCrystalFuseText => string.Join("·", GolemCrystalFuseByWave);
        private const int CrystalWarnTurns = 2;
        private static readonly Color CrystalBlastColor = new Color(0.4f, 0.95f, 1f, 0.4f);
        private readonly Dictionary<Vector2Int, int> _crystalFuse = new Dictionary<Vector2Int, int>();
        private readonly Dictionary<Vector2Int, TextMesh> _crystalLabels = new Dictionary<Vector2Int, TextMesh>();
        private readonly Dictionary<Vector2Int, (List<Vector2Int> Tiles, List<GameObject> Objects)> _crystalTelegraphs =
            new Dictionary<Vector2Int, (List<Vector2Int>, List<GameObject>)>();
        public IReadOnlyDictionary<Vector2Int, int> GolemCrystalFuse => _crystalFuse;
        /// <summary>봇 통계용 - 깬 수정 수, 수정 폭발로 즉사한 횟수.</summary>
        public static int GolemCrystalBreakCount, GolemBlastKillCount, GolemRubbleBreakCount;

        /// <summary>플레이어가 수정을 깼다(한 턴).</summary>
        public void BreakGolemCrystal(Vector2Int p)
        {
            if (!_golemCrystals.Contains(p))
                return;
            GolemCrystalBreakCount++;
            RemoveWallObject(p); // 깨지는 연출 + 타이머·예고·숫자 정리
        }

        /// <summary>수정 c에서 직선으로 보이는(가운데에 벽·바위·다른 수정이 없는) 방 안 칸 전부 - 폭발 범위.</summary>
        public List<Vector2Int> CrystalBlastTiles(Vector2Int c)
        {
            var list = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var t = new Vector2Int(x, y);
                if (t != c && !Map.IsWall(t) && LineClear(c, t))
                    list.Add(t);
            }
            return list;
        }

        /// <summary>a에서 b까지 사이 칸(양 끝 제외)에 벽이 없으면 true - 브레젠험 직선.</summary>
        private bool LineClear(Vector2Int a, Vector2Int b)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            while (true)
            {
                if (x0 == x1 && y0 == y1)
                    return true;
                var e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
                var p = new Vector2Int(x0, y0);
                if (p != b && Map.IsWall(p))
                    return false;
            }
        }

        /// <summary>몹 턴이 끝날 때마다 - 수정 타이머를 줄이고, 2턴 전부터 예고, 0이면 폭발. 플레이어가 죽었으면 true.</summary>
        private bool TickGolemCrystals()
        {
            if (_crystalFuse.Count == 0)
                return false;
            foreach (var c in _crystalFuse.Keys.ToList())
                _crystalFuse[c]--;
            var exploding = _crystalFuse.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList();

            // 이번 턴에 터지는 수정은 동시에 - 범위를 지금 지형(바위·다른 수정 포함)으로 전부 먼저 재고 나서 치운다.
            // 터진 수정과 같은 물결에 떨어진 바위는 전부 깨진다(가려져 있어도) - 이번 폭발은 막아 주지만 다음 물결엔 그 물결 바위를 찾아야 한다.
            // 다른 물결 바위는 남는다(10-15 사용자 - 처음엔 방 전체 바위를 깼더니 앞 물결 수정이 다음 물결 바위까지 없앴다).
            var killed = exploding.Any(c => CrystalBlastTiles(c).Contains(_player.GridPos));
            if (exploding.Count > 0)
                TrailerControls.Cue(TrailerCue.CrystalBlast);
            var waves = new HashSet<int>(exploding.Select(c => _crystalWave.TryGetValue(c, out var w) ? w : 0));
            foreach (var c in exploding)
            {
                RemoveWallObject(c);
                if (HitFeedback.Enabled)
                {
                    var pos = new Vector3(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize, 0f);
                    HitFeedback.OnExplosion(pos);
                    CameraShake.Shake(0.45f, 0.8f);
                    if (!DamagePopup.Suppressed)
                        DeathBurst.Spawn(pos, new Color(0.55f, 1f, 1f, 0.95f), 30, GridConstants.CellSize * 4f, GridConstants.CellSize * 0.2f);
                }
            }
            if (exploding.Count > 0)
                foreach (var r in _golemRubble.ToList())
                {
                    if (!waves.Contains(_rubbleWave.TryGetValue(r, out var rw) ? rw : 0))
                        continue;
                    GolemRubbleBreakCount++;
                    RemoveWallObject(r);
                }

            // 남은 수정 - 숫자, 2턴 전부터 예고(바위가 방금 사라졌으면 넓어진 범위로)
            foreach (var kv in _crystalFuse.ToList())
            {
                UpdateCrystalLabel(kv.Key);
                if (kv.Value <= CrystalWarnTurns)
                    ShowCrystalTelegraph(kv.Key);
            }
            if (!killed)
                return false;
            GolemBlastKillCount++;
            ShowMessage(Loc.T("<color=#FF4040>수정이 폭발해 방 전체를 휩쓸었다!</color>"));
            KillPlayerInstantly();
            ClearAllTelegraphs();
            _loopManager.OnPlayerDied();
            return true;
        }

        private void ShowCrystalTelegraph(Vector2Int c)
        {
            ClearCrystalTelegraph(c);
            var tiles = CrystalBlastTiles(c);
            var objects = DamagePopup.Suppressed ? new List<GameObject>() : tiles.Select(t => SpawnTelegraphTile(t, CrystalBlastColor, -4)).ToList();
            _crystalTelegraphs[c] = (tiles, objects);
            RebuildDangerTiles();
        }

        private void ClearCrystalTelegraph(Vector2Int c)
        {
            if (!_crystalTelegraphs.TryGetValue(c, out var tel))
                return;
            foreach (var go in tel.Objects)
                Fx.Release(go);
            _crystalTelegraphs.Remove(c);
            RebuildDangerTiles();
        }

        private void UpdateCrystalLabel(Vector2Int c)
        {
            if (DamagePopup.Suppressed || !_crystalFuse.TryGetValue(c, out var left))
                return;
            if (!_crystalLabels.TryGetValue(c, out var label) || label == null)
            {
                var go = new GameObject("CrystalFuse");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(c.x * GridConstants.CellSize, (c.y + 0.45f) * GridConstants.CellSize, -1f);
                _roomObjects.Add(go);
                label = go.AddComponent<TextMesh>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 48;
                label.characterSize = 0.09f;
                label.anchor = TextAnchor.LowerCenter;
                label.alignment = TextAlignment.Center;
                label.fontStyle = FontStyle.Bold;
                var meshRenderer = go.GetComponent<MeshRenderer>();
                meshRenderer.material = label.font.material;
                meshRenderer.sortingOrder = 30;
                _crystalLabels[c] = label;
            }
            label.text = left.ToString();
            label.color = left <= 3 ? new Color(1f, 0.3f, 0.3f) : left <= 6 ? new Color(1f, 0.8f, 0.3f) : new Color(0.75f, 1f, 1f);
        }

        /// <summary>수정이 사라졌다(깸·폭발·폭탄) - 타이머·숫자·예고 정리.</summary>
        private void ForgetCrystal(Vector2Int c)
        {
            _crystalFuse.Remove(c);
            if (_crystalLabels.TryGetValue(c, out var label))
            {
                if (label != null)
                    Destroy(label.gameObject);
                _crystalLabels.Remove(c);
            }
            ClearCrystalTelegraph(c);
        }

        /// <summary>골렘 둘레 아무 데나 바위 count개(수정이 솟을 때 - 폭발을 막아 줄 안전지대). 길을 끊는 칸·플레이어 2칸 안은 빼고.</summary>
        public int DropGolemRubbleAround(EnemyActor golem, int count, int wave = 0)
        {
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (GridMap.FootprintChebyshev(golem, p) >= 2
                    && Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) >= 2)
                    cells.Add(p);
            }
            return DropGolemRubble(cells, count, wave);
        }

        /// <summary>공명 수정을 targetCount개가 되도록 세운다(벽 칸) - 골렘 몸에서 3칸, 플레이어에게서 2칸 이상, 수정끼리 3칸 이상 떨어진 빈 칸. 새로 세운 개수.</summary>
        public int SpawnGolemCrystals(EnemyActor golem, int targetCount, int fuseTurns, int wave = 0)
        {
            var need = targetCount - _golemCrystals.Count;
            if (need <= 0)
                return 0;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && GridMap.FootprintChebyshev(golem, p) >= 3 && ExitPosition != p && !_dangerTiles.Contains(p)
                    && Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) >= 2)
                    cells.Add(p);
            }
            var placed = 0;
            foreach (var p in cells.OrderBy(_ => Rng.Next()))
            {
                if (placed >= need)
                    break;
                if (_golemCrystals.Any(c => Mathf.Max(Mathf.Abs(c.x - p.x), Mathf.Abs(c.y - p.y)) < 3))
                    continue;
                if (!WallKeepsConnected(p))
                    continue; // 골렘이나 플레이어가 갇히지 않게
                Map.AddWall(p);
                var go = new GameObject("GolemCrystal");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
                // 공중에 뜬 마름모 수정이 위아래로 둥실(Effects/crystal_float 반복, 10-23 사용자) - 없으면 정지 그림(Props/Golem_Crystal_1), 그것도 없으면 사각형
                var floatFx = DamagePopup.Suppressed ? null : Fx.Play("crystal_float", go.transform.position, 1f, fps: 8f, loop: true, sortingOrder: -7, parent: go.transform);
                if (floatFx == null && VisualUtil.TryCreateProp(go, "Golem_Crystal_1", -7) == null)
                    VisualUtil.CreateSquareVisual(go, new Color(0.4f, 0.9f, 0.95f), GridConstants.CellSize * 0.6f, sortingOrder: -7);
                _roomObjects.Add(go);
                _wallObjects[p] = go;
                _golemCrystals.Add(p);
                _crystalFuse[p] = fuseTurns;
                _crystalWave[p] = wave;
                UpdateCrystalLabel(p);
                placed++;
                if (!DamagePopup.Suppressed)
                    DeathBurst.Spawn(go.transform.position, new Color(0.5f, 0.95f, 0.95f, 0.9f), 10, GridConstants.CellSize * 1.4f, GridConstants.CellSize * 0.12f);
            }
            if (placed > 0 && HitFeedback.Enabled)
            {
                CameraShake.Shake(0.18f, 0.35f);
                SfxPlayer.Play(Sfx.Slam);
            }
            return placed;
        }

        /// <summary>공명 수정이 터진다(골렘 십자 베기에 닿음) - 칸이 비고 청록 파편이 크게 튄다.</summary>
        public void ExplodeGolemCrystal(Vector2Int p)
        {
            if (!_golemCrystals.Contains(p))
                return;
            var pos = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
            RemoveWallObject(p); // 깨지는 연출(crystal_shatter)은 여기서
            if (HitFeedback.Enabled)
            {
                DeathBurst.Spawn(pos, new Color(0.55f, 1f, 1f, 0.95f), 16, GridConstants.CellSize * 2.6f, GridConstants.CellSize * 0.14f);
                HitFeedback.OnExplosion(pos);
            }
        }

        /// <summary>골렘 십자 베기가 지나간 칸 중 count칸(빈 칸, 출구·수정 아닌 곳)에 돌무더기(벽)를 떨어뜨린다 - 다음 십자를 막는 엄폐물.</summary>
        public int DropGolemRubble(IEnumerable<Vector2Int> tiles, int count, int wave = 0)
        {
            var cells = tiles.Where(p => Map.IsWalkable(p) && p != _player.GridPos && ExitPosition != p && !_golemCrystals.Contains(p))
                .OrderBy(_ => Rng.Next()).ToList();
            var placed = 0;
            foreach (var p in cells)
            {
                if (placed >= count)
                    break;
                if (!WallKeepsConnected(p))
                    continue; // 10-14 봇: 돌무더기·수정이 골렘 3x3을 빙 둘러싸 플레이어가 못 닿는 판이 있었다
                placed++;
                Map.AddWall(p);
                var go = new GameObject("GolemRubble");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
                if (VisualUtil.TryCreateProp(go, "Golem_Rubble", -7) == null)
                    VisualUtil.CreateSquareVisual(go, new Color(0.5f, 0.5f, 0.55f), GridConstants.CellSize * 0.85f, sortingOrder: -7);
                _roomObjects.Add(go);
                _wallObjects[p] = go;
                _golemRubble.Add(p);
                _rubbleWave[p] = wave;
                if (!DamagePopup.Suppressed)
                    DeathBurst.Spawn(go.transform.position, new Color(0.55f, 0.53f, 0.6f, 0.95f), 10, GridConstants.CellSize * 1.2f, GridConstants.CellSize * 0.13f);
            }
            return placed;
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
            ShowMessage(Loc.T("<color=#FF8080>균열에 발이 빠졌다!</color> 한 턴 움직일 수 없다"));
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
        public IReadOnlyCollection<Vector2Int> BoarPillars => _boarPillars;

        // ---- 1층 멧돼지 기둥 제한 턴(10-14 사용자) - 기믹을 안 쓰고 버티기만 하면 끝나게: 기둥이 솟은 뒤 BoarPillarTurnLimit턴 안에
        // 기둥을 다 못 부수면 멧돼지가 광폭 돌진으로 즉사시킨다(보호막·불굴·부활 무시). 남은 턴은 멧돼지 머리 위 숫자.
        public const int BoarPillarTurnLimit = 100; // 10-14 300 → 150 → 10-18 100(사용자)
        private static readonly int[] BoarWarnAt = { 50, 20, 10, 5, 3, 2, 1 };
        private int _boarTurnsLeft;
        private TextMesh _boarTimerLabel;
        public int BoarTurnsLeft => _boarPillars.Count > 0 ? _boarTurnsLeft : 0;
        /// <summary>봇 통계용 - 기둥 제한 턴을 넘겨 즉사한 횟수.</summary>
        public static int BoarDeadlineKillCount;

        /// <summary>몹 턴이 끝날 때마다(= 플레이어 행동 1번) - 남은 턴을 줄이고, 0이 되면 즉사. 플레이어가 죽었으면 true.</summary>
        private bool TickBoarDeadline()
        {
            // 기둥이 남은 채로 멧돼지를 잡았으면(피해 10%로도 잡을 수는 있다) 클리어한 방에서 즉사하지 않게 멈춘다
            if (_boarPillars.Count == 0 || _boarTurnsLeft <= 0 || _roomCleared || !_enemies.Any(e => e != null && e.IsBoss && !e.Stats.IsDead))
            {
                _boarTurnsLeft = 0;
                HideBoarTimer();
                return false;
            }
            _boarTurnsLeft--;
            UpdateBoarTimer();
            if (_boarTurnsLeft > 0)
            {
                return false;
            }

            HideBoarTimer();
            BoarDeadlineKillCount++;
            ShowMessage(Loc.T("<color=#FF4040>멧돼지가 광폭해져 모든 것을 짓밟았다!</color>"));
            if (HitFeedback.Enabled)
            {
                CameraShake.Shake(0.5f, 0.9f);
                SfxPlayer.Play(Sfx.Explosion);
            }
            KillPlayerInstantly();
            ClearAllTelegraphs();
            _loopManager.OnPlayerDied();
            return true;
        }

        /// <summary>기믹 실패 즉사 - TakeIncomingDamage를 안 거쳐 보호막·불굴·부활·피해 무효가 안 걸린다. 사망 처리(OnPlayerDied)는 호출부가.</summary>
        private void KillPlayerInstantly()
        {
            if (BossRush.TrailerInvincible)
                return; // 트레일러 촬영 - 기믹을 놓쳐도 죽지 않고 그대로 이어서 찍는다(10층은 예외 - 죽어서 끝)
            PlayLog.InstantKill($"{_stage}층 기믹");
            _player.Stats.Shield = 0f;
            _player.Stats.TakeDamage(_player.Stats.CurrentHealth);
        }

        // ---- 2층 오우거 바닥 구멍(10-14 사용자 컨셉 "강타로 던전이 흔들려 바닥에 구멍 - 플레이어가 메운다") ----
        // 강타마다 구멍(벽 칸, Props/Ogre_Hole)이 생기고, 플레이어가 붙어서 그쪽으로 움직이면 한 턴 써서 메운다(PlayerActor.TryAct).
        // 열린 구멍이 OgreHoleCollapseCount개가 되면 바닥이 무너져 즉사. 오우거 머리 위 "구멍 n/8".
        public const int OgreHoleCollapseCount = 8;
        private const int OgreHoleNearRange = 4;
        // 대지의 기세(10-14 사용자 "구멍을 메우면 버프를 줘서 잡기 편하게") - 메울 때마다 1단계(최대 OgreMightMaxStacks),
        // 단계당 오우거에게 주는 피해 +OgreMightPerStack, 마지막으로 메운 뒤 OgreMightTurns턴 유지. 메우는 턴이 손해가 아니라 투자가 되게.
        public const int OgreMightMaxStacks = 3;
        public const float OgreMightPerStack = 0.3f;
        public const int OgreMightTurns = 6;
        private int _ogreMightStacks, _ogreMightTurns;
        /// <summary>오우거가 받는 피해 배율(BossBrain.ArmorMultiplier).</summary>
        public float OgreMightMultiplier => 1f + _ogreMightStacks * OgreMightPerStack;
        /// <summary>플레이어 발밑 호박색 고리(StatusIcons "status_might").</summary>
        public static bool OgreMightActive { get; private set; }

        /// <summary>몹 턴이 끝날 때마다 - 대지의 기세 남은 턴을 줄인다.</summary>
        private void TickOgreMight()
        {
            if (_ogreMightStacks == 0 || --_ogreMightTurns > 0)
            {
                UpdateOgreHoleLabel();
                return;
            }
            _ogreMightStacks = 0;
            ShowMessage(Loc.T("대지의 기세가 흩어졌다"));
            UpdateOgreHoleLabel();
        }
        private readonly HashSet<Vector2Int> _ogreHoles = new HashSet<Vector2Int>();
        private TextMesh _ogreHoleLabel;
        public IReadOnlyCollection<Vector2Int> OgreHoles => _ogreHoles;
        public bool IsOgreHole(Vector2Int p) => _ogreHoles.Contains(p);
        /// <summary>봇 통계용 - 메운 구멍 수, 붕괴로 즉사한 횟수.</summary>
        public static int OgreHoleFillCount, OgreCollapseKillCount;

        /// <summary>강타 발동 때 구멍 count개를 연다(플레이어·오우거·출구에서 떨어진 빈 칸, 방이 갈라지지 않는 칸만).
        /// 열린 구멍이 붕괴 개수에 닿으면 즉사시키고 true - 사망 처리는 몹 턴 루프(RunEnemyTurnsCore)가 이어서 한다.</summary>
        public bool OpenOgreHoles(EnemyActor ogre, int count)
        {
            var cells = new List<Vector2Int>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWalkable(p) && ExitPosition != p && !_traps.ContainsKey(p)
                    && Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) >= 2
                    && GridMap.FootprintChebyshev(ogre, p) >= 2)
                    cells.Add(p);
            }
            var placed = 0;
            // 플레이어에게서 OgreHoleNearRange칸 안을 먼저 - 방 반대편에 뚫리면 메우러 가는 동안 다음 구멍이 열려서 따라잡을 수 없다(10-14 사용자)
            int Near(Vector2Int p) => Mathf.Max(Mathf.Abs(p.x - _player.GridPos.x), Mathf.Abs(p.y - _player.GridPos.y)) <= OgreHoleNearRange ? 0 : 1;
            foreach (var p in cells.OrderBy(Near).ThenBy(_ => Rng.Next()))
            {
                if (placed >= count)
                    break;
                if (!WallKeepsConnected(p))
                    continue;
                Map.AddWall(p);
                var go = new GameObject("OgreHole");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
                if (VisualUtil.TryCreateProp(go, "Ogre_Hole", -7) == null)
                    VisualUtil.CreateSquareVisual(go, new Color(0.05f, 0.04f, 0.06f), GridConstants.CellSize * 0.85f, sortingOrder: -7);
                _roomObjects.Add(go);
                _wallObjects[p] = go;
                _ogreHoles.Add(p);
                placed++;
                if (!DamagePopup.Suppressed)
                    DeathBurst.Spawn(go.transform.position, new Color(0.45f, 0.42f, 0.4f, 0.95f), 12, GridConstants.CellSize * 1.4f, GridConstants.CellSize * 0.12f);
            }
            if (placed > 0 && HitFeedback.Enabled)
            {
                CameraShake.Shake(0.3f, 0.5f); // 던전이 흔들린다
                SfxPlayer.Play(Sfx.Slam);
            }
            UpdateOgreHoleLabel();
            if (_ogreHoles.Count >= OgreHoleCollapseCount)
            {
                HideOgreHoleLabel();
                OgreCollapseKillCount++;
                ShowMessage(Loc.T("<color=#FF4040>바닥이 통째로 무너져 내렸다!</color>"));
                if (HitFeedback.Enabled)
                {
                    CameraShake.Shake(0.5f, 0.9f);
                    SfxPlayer.Play(Sfx.Explosion);
                }
                KillPlayerInstantly();
                return true;
            }
            return false;
        }

        /// <summary>플레이어가 구멍을 메웠다(한 턴) - 벽을 걷고 바닥을 다시 깐다.</summary>
        public void FillOgreHole(Vector2Int p)
        {
            if (!_ogreHoles.Contains(p))
                return;
            OgreHoleFillCount++;
            TrailerControls.Cue(TrailerCue.HoleFilled);
            _ogreMightStacks = Mathf.Min(OgreMightMaxStacks, _ogreMightStacks + 1);
            _ogreMightTurns = OgreMightTurns;
            if (HitFeedback.Enabled) // 구멍 자리에 흙이 쏟아져 차오르고 다져진다(Effects/hole_fill 48x48, 10-23) - 구멍 그림은 이 이펙트 첫 장에 들어 있다
                Fx.Play("hole_fill", new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f), 1.5f, fps: 14f, sortingOrder: -6);
            RemoveWallObject(p); // 구멍 목록에서도 빠지고 바닥 타일이 다시 깔린다
            ShowMessage(Loc.F("<color=#FFD070>대지의 기세 {0}단계!</color> 오우거에게 주는 피해 +{1:0}% ({2}턴)",
                _ogreMightStacks, (OgreMightMultiplier - 1f) * 100f, OgreMightTurns));
            if (HitFeedback.Enabled) // 발밑이 갈라지며 흙덩이가 몸으로 빨려 들고 금빛 기운이 솟는다(Effects/earth_might, 플레이어 그림과 같은 1.25칸)
                Fx.Play("earth_might", _player.transform.position, 1.25f, fps: 14f, sortingOrder: 8);
            if (HitFeedback.Enabled)
                SfxPlayer.Play(Sfx.ChestRattle);
        }

        private void UpdateOgreHoleLabel()
        {
            OgreMightActive = _ogreMightStacks > 0;
            if (DamagePopup.Suppressed)
                return;
            if (_ogreHoles.Count == 0 && _ogreMightStacks == 0)
            {
                HideOgreHoleLabel();
                return;
            }
            var ogre = _enemies.FirstOrDefault(e => e != null && e.IsBoss && !e.Stats.IsDead);
            if (ogre == null)
                return;
            if (_ogreHoleLabel == null)
                _ogreHoleLabel = CreateBossLabel(ogre, "OgreHoles");
            _ogreHoleLabel.text = Loc.F("구멍 {0}/{1}", _ogreHoles.Count, OgreHoleCollapseCount)
                + (_ogreMightStacks > 0 ? "\n" + Loc.F("기세 +{0:0}% ({1}턴)", (OgreMightMultiplier - 1f) * 100f, _ogreMightTurns) : "");
            _ogreHoleLabel.color = _ogreHoles.Count >= OgreHoleCollapseCount - 2 ? new Color(1f, 0.3f, 0.3f)
                : _ogreHoles.Count >= OgreHoleCollapseCount / 2 ? new Color(1f, 0.75f, 0.3f) : Color.white;
        }

        private void HideOgreHoleLabel()
        {
            if (_ogreHoleLabel != null)
            {
                Destroy(_ogreHoleLabel.gameObject);
                _ogreHoleLabel = null;
            }
        }

        /// <summary>3x3 보스 머리 위를 따라다니는 글자(멧돼지 남은 턴·오우거 구멍 수).</summary>
        private TextMesh CreateBossLabel(EnemyActor boss, string goName)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            _roomObjects.Add(go);
            var label = go.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 48;
            label.characterSize = 0.1f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.material = label.font.material;
            meshRenderer.sortingOrder = 30;
            go.AddComponent<FollowAbove>().Target = boss.transform;
            return label;
        }

        private void UpdateBoarTimer()
        {
            if (DamagePopup.Suppressed)
                return;
            var boar = _enemies.FirstOrDefault(e => e != null && e.IsBoss && !e.Stats.IsDead);
            if (boar == null)
                return;
            if (_boarTimerLabel == null)
                _boarTimerLabel = CreateBossLabel(boar, "BoarTimer");
            _boarTimerLabel.text = Loc.F("기둥 {0}개 · {1}턴", _boarPillars.Count, _boarTurnsLeft);
            _boarTimerLabel.color = _boarTurnsLeft <= 20 ? new Color(1f, 0.3f, 0.3f) : _boarTurnsLeft <= 100 ? new Color(1f, 0.75f, 0.3f) : Color.white;
        }

        private void HideBoarTimer()
        {
            if (_boarTimerLabel != null)
            {
                Destroy(_boarTimerLabel.gameObject);
                _boarTimerLabel = null;
            }
        }

        /// <summary>대상 위에 붙어 다니는 표시(대상 오브젝트엔 그림 배율이 걸려 있어 자식으로 안 붙인다).</summary>
        private sealed class FollowAbove : MonoBehaviour
        {
            public Transform Target;

            private void LateUpdate()
            {
                if (Target == null)
                {
                    Destroy(gameObject);
                    return;
                }
                transform.position = Target.position + new Vector3(0f, GridConstants.CellSize * 2.1f, -1f); // 3x3 몸 위
            }
        }

        /// <summary>멧돼지 상하좌우로 4칸(안 되면 3·5칸) 떨어진 빈 칸에 돌기둥(벽 칸)을 세운다 - 세운 개수.
        /// 기둥 뒤에 선 플레이어를 향해 돌진하면 몸 띠가 기둥에 걸려 박는다.</summary>
        private const int PillarInset = 2; // 멧돼지 기둥 - 벽에서 몇 칸 안쪽

        /// <summary>1층 멧돼지 기둥 단계 시작(BossBrain) - 짧은 컷신: 울부짖음·흔들림 → 화면이 어두워진 사이 멧돼지는 방 중앙, 플레이어는 멧돼지 몸
        /// 2칸 아래로 → 밝아지며 기둥이 솟는다. 그동안 입력은 잠근다. 봇 실행 중엔 연출 없이 바로.</summary>
        public void StartBoarPillarPhase(EnemyActor boar)
        {
            // 공용 기믹 컷신(RoomController.Cutscenes) - 어두워진 사이 멧돼지는 중앙, 플레이어는 몸 2칸 아래 → 기둥이 하나씩 솟는다
            var c = new GimmickCutscene { Fade = true };
            c.Relocate = () =>
            {
                if (boar != null && !boar.Stats.IsDead)
                    RelocateForBoarPhase(boar);
                BeginBoarPillars();
            };
            foreach (var spot in BoarPillarTemplate())
                c.Steps.Add(() => boar != null && !boar.Stats.IsDead && AddBoarPillar(spot)); // 솟아오르는 이펙트는 AddBoarPillar가
            c.Finish = () => FinishBoarPillars();
            PlayGimmickCutscene(c);
        }

        /// <summary>멧돼지를 방 중앙(못 들어가면 가장 가까운 자리)으로, 플레이어를 멧돼지 몸 2칸 아래(막혔으면 가까운 빈칸)로 옮긴다.</summary>
        private void RelocateForBoarPhase(EnemyActor boar)
        {
            Map.RemoveActor(boar);
            Map.RemoveActor(_player);
            var r = boar.FootprintRadius;
            var center = new Vector2Int(Map.Width / 2, Map.Height / 2);
            var bossPos = boar.GridPos;
            for (var d = 0; d <= 4; d++)
            {
                var found = false;
                for (var dx = -d; dx <= d && !found; dx++)
                for (var dy = -d; dy <= d && !found; dy++)
                {
                    var c = center + new Vector2Int(dx, dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == d && Map.CanOccupy(boar, c))
                    {
                        bossPos = c;
                        found = true;
                    }
                }
                if (found)
                    break;
            }
            Map.PlaceActor(boar, bossPos);

            var want = bossPos + new Vector2Int(0, -(r + 2));
            var playerPos = _player.GridPos;
            for (var d = 0; d <= 4; d++)
            {
                var found = false;
                for (var dx = -d; dx <= d && !found; dx++)
                for (var dy = -d; dy <= d && !found; dy++)
                {
                    var c = want + new Vector2Int(dx, dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == d && Map.IsWalkable(c) && GridMap.FootprintDistance(boar, c) > 1)
                    {
                        playerPos = c;
                        found = true;
                    }
                }
                if (found)
                    break;
            }
            Map.PlaceActor(_player, playerPos);
            if (boar.TryGetComponent<SpriteAnimator>(out var boarAnim))
                boarAnim.SnapToGrid(); // 걸어가는 모션 대신 순간이동
            if (_player.TryGetComponent<SpriteAnimator>(out var playerAnim))
                playerAnim.SnapToGrid();
        }

        private void SpawnBoarPillarsAndAnnounce(EnemyActor boar)
        {
            SpawnBoarPillars(boar);
        }

        public int SpawnBoarPillars(EnemyActor boar)
        {
            BeginBoarPillars();
            foreach (var spot in BoarPillarTemplate())
                AddBoarPillar(spot);
            return FinishBoarPillars();
        }

        private void BeginBoarPillars() => _boarPillars.Clear();

        /// <summary>맵 기준 배치(10-14 사용자) - 매번 무작위로 "4면의 중앙" 또는 "모서리 4칸"(둘 다 벽에서 PillarInset칸 안쪽).
        /// 예전엔 멧돼지 몸 중심 기준 상하좌우 4칸.</summary>
        private Vector2Int[] BoarPillarTemplate()
        {
            const int inset = PillarInset;
            int w = Map.Width, h = Map.Height;
            return Rng.Next(2) == 0
                ? new[] { new Vector2Int(w / 2, inset), new Vector2Int(w / 2, h - 1 - inset), new Vector2Int(inset, h / 2), new Vector2Int(w - 1 - inset, h / 2) }
                : new[] { new Vector2Int(inset, inset), new Vector2Int(w - 1 - inset, inset), new Vector2Int(inset, h - 1 - inset), new Vector2Int(w - 1 - inset, h - 1 - inset) };
        }

        /// <summary>기둥 하나 - 그 칸이 막혔으면(멧돼지 몸·플레이어·출구·예고 칸·벽, 길을 끊는 칸) 2칸 안 가까운 빈칸에. 세웠으면 true.</summary>
        private bool AddBoarPillar(Vector2Int spot)
        {
            Vector2Int? found = null;
            for (var r = 0; r <= 2 && !found.HasValue; r++)
            for (var dx = -r; dx <= r && !found.HasValue; dx++)
            for (var dy = -r; dy <= r && !found.HasValue; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                    continue; // 가까운 고리부터
                var c = spot + new Vector2Int(dx, dy);
                if (Map.IsWalkable(c) && c != _player.GridPos && ExitPosition != c && !_dangerTiles.Contains(c)
                    && !_boarPillars.Contains(c) && WallKeepsConnected(c))
                    found = c;
            }
            if (!found.HasValue)
                return false;
            var p = found.Value;
            Map.AddWall(p);
            _boarPillars.Add(p);
            // 전용 돌기둥 그림(Props/Boar_Pillar, 칸 위로 살짝 솟음) + 솟아오르는 8프레임(Effects/pillar_rise) - 10-14. 없으면 예전 벽 타일
            GameObject go;
            SpriteRenderer pillarArt = null;
            var pos = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
            if (Resources.Load<Sprite>("Sprites/Props/Boar_Pillar") != null)
            {
                go = new GameObject("Pillar");
                go.transform.SetParent(transform, false);
                go.transform.position = pos;
                pillarArt = VisualUtil.TryCreateProp(go, "Boar_Pillar", -7);
            }
            else
                go = _tiledRoom
                    ? CreateTile(transform, p, DungeonTiles.Wall(true, _tileRng), -8, PillarTint).gameObject
                    : CreatePillarSquare(p);
            _roomObjects.Add(go);
            _wallObjects[p] = go;
            if (!DamagePopup.Suppressed)
            {
                DeathBurst.Spawn(go.transform.position, new Color(0.6f, 0.52f, 0.42f, 0.9f), 10, GridConstants.CellSize * 1.4f, GridConstants.CellSize * 0.12f);
                var rise = pillarArt != null ? Fx.Play("pillar_rise", pos, 1f, fps: 16f, sortingOrder: -7) : null;
                if (rise != null)
                    StartCoroutine(ShowPillarAfterRise(pillarArt, 8f / 16f));
                else
                    StartCoroutine(RisePillar(go.transform));
            }
            return true;
        }

        /// <summary>기둥을 다 세운 뒤 - 제한 턴 시작. 세운 개수.</summary>
        private int FinishBoarPillars()
        {
            if (_boarPillars.Count > 0)
            {
                _boarTurnsLeft = BoarPillarTurnLimit;
                UpdateBoarTimer();
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
        /// <summary>솟아오르는 이펙트가 도는 동안 서 있는 기둥 그림은 숨겨 둔다(이펙트 마지막 장 = 서 있는 기둥).</summary>
        private static IEnumerator ShowPillarAfterRise(SpriteRenderer pillar, float seconds)
        {
            pillar.enabled = false;
            yield return new WaitForSeconds(seconds);
            if (pillar != null)
                pillar.enabled = true;
        }

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
                if (HitFeedback.Enabled) // 돌기둥이 산산조각 난다(Effects/pillar_break 48x48, 10-23)
                    Fx.Play("pillar_break", new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f), 1.5f, fps: 14f, sortingOrder: 6);
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
            var wasCrystal = _golemCrystals.Remove(p); // 3층 골렘 수정 - 깸·폭발·폭탄 모두 깨지는 그림
            if (wasCrystal)
                ForgetCrystal(p);
            var wasRubble = _golemRubble.Remove(p); // 3층 골렘 바위 - 폭발·폭탄에 깨지는 그림(Effects/rubble_break 8프레임)
            if (_wallObjects.TryGetValue(p, out var wallGo))
            {
                _wallObjects.Remove(p);
                if (wallGo != null)
                {
                    if (wasCrystal)
                    {
                        if (Fx.Play("crystal_shatter", wallGo.transform.position, 1.6f, fps: 14f, sortingOrder: 6) == null && !DamagePopup.Suppressed)
                            DeathBurst.Spawn(wallGo.transform.position, new Color(0.55f, 1f, 1f, 0.95f), 16, GridConstants.CellSize * 2f, GridConstants.CellSize * 0.16f);
                    }
                    else if (wasRubble)
                    {
                        if (Fx.Play("rubble_break", wallGo.transform.position, 1.7f, fps: 14f, sortingOrder: 6) == null && !DamagePopup.Suppressed)
                            DeathBurst.Spawn(wallGo.transform.position, new Color(0.6f, 0.58f, 0.6f, 0.95f), 14, GridConstants.CellSize * 1.8f, GridConstants.CellSize * 0.15f);
                    }
                    else if (!DamagePopup.Suppressed)
                        DeathBurst.Spawn(wallGo.transform.position, new Color(0.6f, 0.52f, 0.42f, 0.95f), 16, GridConstants.CellSize * 2f, GridConstants.CellSize * 0.16f);
                    Destroy(wallGo);
                }
            }
            OnWallRemoved(p);
            if (_ogreHoles.Remove(p))
                UpdateOgreHoleLabel(); // 메웠거나 폭탄으로 무너뜨렸다(폭발이 구멍을 메운다)
            if (_secretWall == p)
                OnSecretWallBroken(p);
            if (_boarPillars.Remove(p))
            {
                if (_boarPillars.Count == 0)
                {
                    HideBoarTimer();
                }
                else
                    UpdateBoarTimer();
            }
        }

        /// <summary>폭탄으로 벽이 부서진 뒤 - 그 자리에 바닥을 깔고, 바로 위 벽은 이제 아래가 바닥이니 앞면으로.</summary>
        private void OnWallRemoved(Vector2Int cell)
        {
            if (!_tiledRoom || _floorRoot == null)
                return;
            CreateTile(_floorRoot, cell, DungeonTiles.Floor(_tileRng), -10, _floorTint);
            // 바로 위가 방 벽이면 벽돌 앞면으로 - 진짜 벽(이름 "Wall")만. 예전엔 위 칸이 구멍·바위·수정·용암·항아리·숨겨진 벽이어도
            // 그림을 벽으로 덮어써서, 오우거 구멍을 메우면 그 위 구멍이 벽처럼 보였다(10-15 사용자 제보).
            if (_wallObjects.TryGetValue(cell + Vector2Int.up, out var above) && above != null && above.name == "Wall"
                && above.TryGetComponent<SpriteRenderer>(out var aboveRenderer))
                aboveRenderer.sprite = DungeonTiles.Wall(true, _tileRng);
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
            ClearBossWindup();
            _knightPending = false;
            _boarPillars.Clear();
            _boarTurnsLeft = 0;
            _boarTimerLabel = null; // 오브젝트는 _roomObjects와 함께 지워진다
            _ogreHoles.Clear();
            _ogreHoleLabel = null;
            _ogreMightStacks = _ogreMightTurns = 0;
            OgreMightActive = false;
            ClearStageGimmick();
            foreach (var c in _crystalFuse.Keys.ToList())
                ForgetCrystal(c);
            _golemRubble.Clear();
            _rubbleWave.Clear();
            _crystalWave.Clear();
            _cracks.Clear();
            _ogreFallImmune = 0;
            _golemCrystals.Clear();
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
            if (enemy != null && enemy.IsBoss)
                ClearBossWindup();
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
                    ClearBombTelegraph(minion); // 4층 포탑 등 예고 중이던 졸개의 칸도
                    Map.RemoveActor(minion);
                    Destroy(minion.gameObject);
                }
                _enemies.Clear();
                ClearTelegraph();
                foreach (var c in _golemCrystals.ToList())
                    RemoveWallObject(c); // 3층 골렘이 죽으면 남은 수정은 터지지 않고 부서진다(10-14 사용자)
                _ogreMightStacks = 0; // 2층 대지의 기세도 끝
                OgreMightActive = false;
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
            if (BossRush.Active && _current.IsBossRoom)
            {
                _roomCleared = true; // 보스 러시 - 스테이지 클리어 대신 다음 보스로(마지막이면 끝)
                if (BossRush.IsLast(_current))
                {
                    if (!BossRush.Trailer)
                        ShowMessage(Loc.F("<color=#FFD24A>보스 러시 완료!</color> {0}~{1}층 보스를 모두 쓰러뜨렸다", BossRush.FirstStage, BossRush.LastStage));
                    return;
                }
                _player.Stats.FullHeal();
                HitFeedback.OnHeal(_player);
                SpawnExit();
                if (!BossRush.Trailer) // 트레일러 화면엔 안내 글자를 띄우지 않는다
                    ShowMessage(Loc.F("{0}층 보스 격파! 체력을 회복했다 - 출구로 가면 {1}층 보스", _current.BossStage, _current.BossStage + 1));
                return;
            }
            _roomCleared = true;
            if (!_current.IsBossRoom)
                TrailerControls.Cue(TrailerCue.MobsCleared);
            RoomClearCount++;
            PlayLog.RoomCleared(_player, false);
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
            {
                MusicPlayer.Stop(); // 보스를 잡으면 보스 음악을 서서히 끈다
                _loopManager.OnBossDefeated();
            }
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
            // 다음 방으로 가는 문 도트(Resources/Tiles/ExitDoor, 사용자 요청 10-13 계단 → 문) - 없으면 예전 계단, 그것도 없으면 파란 칸
            var stairs = Resources.Load<Sprite>("Tiles/ExitDoor") ?? Resources.Load<Sprite>("Tiles/Stairs");
            ExitRenderer = stairs != null
                ? VisualUtil.CreateSpriteVisual(go, stairs, GridConstants.CellSize, sortingOrder: -6)
                : VisualUtil.CreateSquareVisual(go, ExitColor, GridConstants.CellSize * 0.85f, sortingOrder: -6);
            go.transform.position = new Vector3(best.Value.x * GridConstants.CellSize, best.Value.y * GridConstants.CellSize, 0f);
            if (stairs == null)
                go.AddComponent<ExitMarker>(); // 예전 파란 칸만 깜빡임 - 문(계단)은 늘 밝게(사용자 요청)
            _roomObjects.Add(go);
            ShowMessage(stairs != null ? Loc.T("출구가 열렸다! (문)") : Loc.T("출구가 열렸다! (파란 칸)"));
        }

        /// <summary>플레이어가 pos로 막 옮겨왔을 때 - 출구면 다음 방으로 보내고 true(호출부는 이번 턴을 그대로 끝낸다).</summary>
        public bool TryUseExit(Vector2Int pos)
        {
            if (!ExitPosition.HasValue || ExitPosition.Value != pos)
                return false;
            if (TutorialStage.BlocksExit(this))
                return false; // 튜토리얼 아이템 방 - 아직 안 써 본 아이템이 있으면 출구 칸에 서 있기만 한다
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
            _gimmickJustStarted = false; // 지난 턴(또는 보스 러시 입장 때) 시작한 기믹은 이번 몹 턴부터 깎는다
            ClearBossWindup(); // 몹 턴 동안엔 끄고(광폭화 색이 바뀌어도 덮어쓰지 않게) 끝나면 다시 판단
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
            // 이번 몹 턴 "도중에" 방이 끝났을 때만 멈춘다. 예전엔 _roomCleared만 봐서, 방을 비운 뒤 나온 몹(도전의 깃발 정예·미믹)이
            // 이미 클리어된 방이라는 이유로 매 턴 여기서 걸려 아무 행동도 안 했다.
            var clearedBefore = _roomCleared;
            for (var pass = 0; pass < passes; pass++)
            foreach (var enemy in snapshot)
            {
                if (_roomCleared && !clearedBefore)
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

            if (TickBoarDeadline())
                return; // 1층 멧돼지 기둥 제한 턴 - 즉사
            if (TickGolemCrystals())
                return; // 3층 골렘 수정 폭발 - 즉사
            TickOgreMight(); // 2층 대지의 기세 남은 턴
            if (TickStageGimmick())
                return; // 4~9층 보스 기믹 실패 - 즉사

            UpdateBossWindup();
            EndOfEnemyTurns();
            Puzzle?.TurnEnd();
        }

        // ---- 보스 준비 동작(10-15 사용자 "일반 공격이 잘 보이게, 준비 동작 같은 것") ----
        // 다음 보스 턴이 일반 턴(예고 패턴 아님)이면 보스가 붉게 숨쉬고, 몸에 붙는 칸(근접 공격이 닿는 칸)이 옅게 깔린다.
        // 그 칸에 서 있으면 다음 턴에 맞는다는 뜻 - 예고 패턴과 헷갈리지 않게 깜빡이지 않는 옅은 사각형. 위험 칸(봇·DangerTiles)에는 안 넣는다.
        private static readonly Color MeleeReachColor = new Color(1f, 0.35f, 0.2f, 0.16f);
        private readonly List<GameObject> _windupTiles = new List<GameObject>();
        private EnemyActor _windupBoss;

        private void UpdateBossWindup()
        {
            ClearBossWindup();
            if (!HitFeedback.Enabled || _player == null || MirrorFakesActive)
                return; // 10층 거울상이 있는 동안은 끈다 - 진짜만 숨쉬면 바로 들킨다
            var boss = _enemies.FirstOrDefault(e => e != null && e.IsBoss && e.BossMeleeReadyNextTurn(this));
            if (boss == null)
                return;
            _windupBoss = boss;
            ActorJuice.Get(boss).SetWindup(true);
            var reach = boss.FootprintReach + 1;
            for (var dx = -reach; dx <= reach; dx++)
            for (var dy = -reach; dy <= reach; dy++)
            {
                var c = boss.GridPos + new Vector2Int(dx, dy);
                if (!Map.IsInBounds(c) || Map.IsWall(c) || !boss.IsAdjacentTo(c))
                    continue;
                var go = new GameObject("BossReach");
                go.transform.SetParent(transform, false);
                VisualUtil.CreateSquareVisual(go, MeleeReachColor, GridConstants.CellSize * 0.9f, sortingOrder: -6);
                go.transform.position = new Vector3(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize, 0f);
                _windupTiles.Add(go);
            }
        }

        private void ClearBossWindup()
        {
            if (_windupBoss != null)
                ActorJuice.Get(_windupBoss).SetWindup(false);
            _windupBoss = null;
            foreach (var go in _windupTiles)
                if (go != null)
                    Destroy(go);
            _windupTiles.Clear();
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
            _player?.TryPhantomBanner(); // 유물 "망령의 깃발" - 이번 몹 턴에 체력이 30% 아래로 떨어졌으면

            if (_playerBombs.Count == 0)
                return;
            var bombs = _playerBombs.ToArray();
            DestroyPlayerBombObjects(); // 그림은 먼저 다 지운다 - 앞 폭탄이 마지막 몹을 잡아도 뒤 폭탄 그림이 남지 않게
            foreach (var bomb in bombs)
                ExplodePlayerBomb(bomb.Center); // 방이 비어도 남은 폭탄은 마저 터진다(금 간 벽은 부서져야)
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
            if (attacker == null || dealt <= 0f || attacker.Stats.IsDead || !EquipmentEffects.Has(ItemSlot.Armor, 2) || attacker.IsBladeProof)
                return; // 7층 분신·10층 거울상은 가시에도 안 다친다(체력이 줄면 가짜라는 티가 났다)
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

        public void PlaceDecoy(Vector2Int pos, int turns = ItemInfo.DecoyTurns)
        {
            if (ActiveDecoy != null)
            {
                Map.RemoveActor(ActiveDecoy);
                Destroy(ActiveDecoy.gameObject);
            }
            var go = new GameObject("Decoy");
            go.transform.SetParent(transform, false);
            ActiveDecoy = go.AddComponent<DecoyActor>();
            ActiveDecoy.Initialize(turns);
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
                if (Map.IsWall(t) && !IsUnbreakableGimmickWall(t))
                    RemoveWallObject(t); // 멧돼지 기둥도 폭탄으로 부서진다(용암 균열·영혼 항아리·빛 거울·조력자는 안 부서짐)
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
