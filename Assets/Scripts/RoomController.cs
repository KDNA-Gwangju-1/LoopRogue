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

        private const int GoldPerKill = 4;       // "쓸 곳은 많은데 수급이 부족하다" 피드백으로 전부 2배(2/20/5 → 4/40/10)
        private const int GoldPerBossKill = 40;
        private const int GoldPerRoomClear = 10;

        /// <summary>일반 방을 클리어하면 최대체력의 이 비율만큼 회복 - 방 사이 회복이 전혀 없어서 한 스테이지 뒤쪽 방(7~10)으로
        /// 갈수록 깎인 체력을 그대로 들고 싸우다 죽던 문제(봇 100판에서 10층 일반 방 사망이 보스 사망의 약 30%) 대책.</summary>
        private const float RoomClearHealRate = 0.1f;

        private static readonly System.Random Rng = new System.Random();
        private static readonly Color TelegraphColor = new Color(1f, 0.15f, 0.15f, 0.45f);
        private static readonly Color WallColor = new Color(0.38f, 0.36f, 0.34f);

        /// <summary>자동 플레이 봇 통계용.</summary>
        public static int EventTriggeredCount;
        public static RoomEventType LastEventType;

        private readonly List<EnemyActor> _enemies = new List<EnemyActor>();
        private readonly List<GameObject> _roomObjects = new List<GameObject>();     // 바닥/벽
        private readonly List<GameObject> _telegraphObjects = new List<GameObject>();
        private readonly HashSet<Vector2Int> _dangerTiles = new HashSet<Vector2Int>();
        private RoomEventActor _event;
        private PlayerActor _player;
        private LoopManager _loopManager;
        private RoomDefinition _current;
        private int _stage;

        public int Stage => _stage;
        public IReadOnlyList<EnemyActor> Enemies => _enemies;
        public string RoomName => _current?.RoomName;
        public bool IsBossRoom => _current != null && _current.IsBossRoom;

        /// <summary>보스가 예고해둔 공격 칸(다음 보스 턴에 발동) - 비어 있으면 예고 중인 공격 없음.</summary>
        public IReadOnlyCollection<Vector2Int> DangerTiles => _dangerTiles;

        /// <summary>이 방에 아직 안 밟은 이벤트 칸이 있으면 그 액터.</summary>
        public RoomEventActor Event => _event;

        public void Initialize(PlayerActor player, LoopManager loopManager, int stage)
        {
            _player = player;
            _loopManager = loopManager;
            _stage = stage;
        }

        public void LoadRoom(RoomDefinition def)
        {
            _current = def;
            ClearRoom();

            var layout = RoomLayoutGenerator.Generate(def, Rng);
            Map = new GridMap(layout.Width, layout.Height);
            foreach (var wall in layout.Walls)
                Map.AddWall(wall);

            BuildFloor(layout, def.IsBossRoom);
            PositionCamera(layout);

            Map.PlaceActor(_player, layout.PlayerStart);

            for (var i = 0; i < layout.EnemyPositions.Count; i++)
            {
                var kind = i < def.EnemyKinds.Count ? def.EnemyKinds[i] : EnemyKind.Melee;
                SpawnEnemy(layout.EnemyPositions[i], def, kind);
            }

            if (def.HasEvent && layout.EventPosition.HasValue)
                SpawnEvent(layout.EventPosition.Value, def.EventType);

            IsInputLocked = false;
        }

        private void SpawnEnemy(Vector2Int pos, RoomDefinition def, EnemyKind kind)
        {
            var ranged = !def.IsBossRoom && kind == EnemyKind.Ranged;
            var go = new GameObject(def.IsBossRoom ? "Boss" : ranged ? "Archer" : "Enemy");
            go.transform.SetParent(transform, false);

            var enemy = go.AddComponent<EnemyActor>();
            var hp = def.EnemyMaxHealth * (ranged ? RangedHealthRatio : 1f);
            var atk = def.EnemyAttackPower * (ranged ? RangedAttackRatio : 1f);
            enemy.Initialize(hp, atk, def.IsBossRoom, ranged ? EnemyKind.Ranged : EnemyKind.Melee);
            if (def.IsBossRoom)
                enemy.SetupBossPatterns(def.BossPatterns, _stage);
            Map.PlaceActor(enemy, pos);
            _enemies.Add(enemy);
        }

        private void SpawnEvent(Vector2Int pos, RoomEventType type)
        {
            var go = new GameObject("RoomEvent");
            go.transform.SetParent(transform, false);
            _event = go.AddComponent<RoomEventActor>();
            _event.Initialize(type);
            Map.PlaceActor(_event, pos);
        }

        /// <summary>플레이어가 이벤트 칸으로 이동했을 때(PlayerActor가 호출) - 발동하고 칸을 비운다.</summary>
        public void TriggerEvent(RoomEventActor ev)
        {
            var roomClearGold = Mathf.RoundToInt(GoldPerRoomClear * StageScaling.RewardMultiplier(_stage) * (1f + _player.Stats.EffectiveGoldBonus));
            var message = ev.Trigger(_player, roomClearGold);
            EventTriggeredCount++;
            LastEventType = ev.Type;
            Map.RemoveActor(ev);
            Destroy(ev.gameObject);
            if (_event == ev)
                _event = null;
            ShowMessage(message);
        }

        public void ShowMessage(string message) => _loopManager.ShowMessage(message);

        /// <summary>보스 소환 - 보스 주변 빈 칸에 졸개를 (살아있는 졸개 포함 최대 maxAlive마리까지) 만든다.</summary>
        public void SpawnMinions(EnemyActor boss, int maxAlive)
        {
            var alive = _enemies.Count(e => e != null && e.IsMinion && !e.Stats.IsDead);
            var toSpawn = maxAlive - alive;
            if (toSpawn <= 0)
                return;

            var cells = new List<Vector2Int>();
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
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
                _enemies.Add(minion);
            }
        }

        public void ShowTelegraph(IEnumerable<Vector2Int> tiles)
        {
            ClearTelegraph();
            HitFeedback.OnTelegraph();
            foreach (var t in tiles)
            {
                _dangerTiles.Add(t);
                var go = new GameObject("Telegraph");
                go.transform.SetParent(transform, false);
                VisualUtil.CreateSquareVisual(go, TelegraphColor, GridConstants.CellSize * 0.95f, sortingOrder: -5);
                go.transform.position = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f);
                _telegraphObjects.Add(go);
            }
        }

        public void ClearTelegraph()
        {
            foreach (var go in _telegraphObjects)
                if (go != null)
                    Destroy(go);
            _telegraphObjects.Clear();
            _dangerTiles.Clear();
        }

        private void BuildFloor(RoomLayout layout, bool isBossRoom)
        {
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
            }
        }

        /// <summary>방마다 크기가 달라서(보스방은 더 큼) 매 방 로드 시 카메라를 방 중앙으로 다시
        /// 맞춘다 - 2D라 회전 없이 Z축 뒤로 물러나서 보는 표준 카메라.</summary>
        private static void PositionCamera(RoomLayout layout)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var centerX = (layout.Width - 1) * GridConstants.CellSize * 0.5f;
            var centerY = (layout.Height - 1) * GridConstants.CellSize * 0.5f;
            cam.transform.position = new Vector3(centerX, centerY, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(layout.Width, layout.Height) * GridConstants.CellSize * 0.6f;
            CameraShake.ResetOffset(cam); // 흔들리던 중 방이 바뀌면 새 위치에서 이전 흔들림을 빼지 않게
        }

        private void ClearRoom()
        {
            foreach (var enemy in _enemies)
                if (enemy != null)
                    Destroy(enemy.gameObject);
            _enemies.Clear();

            if (_event != null)
                Destroy(_event.gameObject);
            _event = null;

            foreach (var go in _roomObjects)
                if (go != null)
                    Destroy(go);
            _roomObjects.Clear();

            ClearTelegraph();
        }

        /// <summary>적 하나가 죽은 직후 호출 - 목록에서 빼고, 방이 비었으면 다음 방/승리로 진행한다.
        /// 방이 전환됐으면 true(호출부는 이번 턴엔 몹 행동을 건너뛴다). 골드는 여기서 한 곳에서만 지급한다(졸개는 없음).
        /// 보스가 죽으면 남은 졸개도 같이 사라진다.</summary>
        public bool NotifyEnemyDefeated(EnemyActor enemy)
        {
            var stageMultiplier = StageScaling.RewardMultiplier(_stage);
            var goldBonus = 1f + _player.Stats.EffectiveGoldBonus; // 골드 증감 카드(하한 -50%)
            // 반복 보상 감소는 일반 몹/방 클리어에만(보스 처치는 항상 100%).
            var repeat = StageProgress.RepeatRewardMultiplier;
            if (!enemy.IsMinion)
            {
                var killGold = enemy.IsBoss
                    ? Mathf.RoundToInt(GoldPerBossKill * stageMultiplier * goldBonus)
                    : Mathf.RoundToInt(GoldPerKill * stageMultiplier * goldBonus * repeat);
                GoldWallet.Add(killGold);
            }

            _enemies.Remove(enemy);

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

            if (_enemies.Count > 0)
                return false;

            if (!_current.IsBossRoom)
            {
                GoldWallet.Add(Mathf.RoundToInt(GoldPerRoomClear * stageMultiplier * goldBonus * repeat));
                _player.Stats.Heal(_player.Stats.MaxHealth * RoomClearHealRate);
            }

            if (_current.IsBossRoom)
                _loopManager.OnBossDefeated();
            else
                _loopManager.AdvanceToNextRoom();
            return true;
        }

        public void RunEnemyTurns()
        {
            // 스냅샷을 떠서 순회한다 - 턴 도중 죽거나 소환돼서 리스트가 바뀌어도 이번 순회엔 영향 없게.
            // 플레이어에게 가까운 몹부터 움직여서 가까운 몹이 가까운 옆 칸을 먼저 예약하게 한다(포위 AI).
            var playerPos = _player.GridPos;
            var snapshot = _enemies
                .Where(e => e != null)
                .OrderBy(e => Mathf.Abs(e.GridPos.x - playerPos.x) + Mathf.Abs(e.GridPos.y - playerPos.y))
                .ToArray();

            // 이미 붙어 있는 근접 몹의 칸은 먼저 예약해둔다 - 다른 몹이 그 칸을 목표로 삼지 않게.
            var claimedSlots = new HashSet<Vector2Int>();
            foreach (var enemy in snapshot)
            {
                if (enemy.Kind == EnemyKind.Melee && enemy.IsAdjacentTo(playerPos))
                    claimedSlots.Add(enemy.GridPos);
            }

            foreach (var enemy in snapshot)
            {
                if (enemy == null || enemy.Stats.IsDead)
                    continue;

                enemy.TakeTurn(_player, claimedSlots, this);

                if (_player.Stats.IsDead)
                {
                    ClearTelegraph();
                    _loopManager.OnPlayerDied();
                    return;
                }
            }
        }
    }
}
