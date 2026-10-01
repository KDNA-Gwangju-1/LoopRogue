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

        /// <summary>방패병은 단단하고(정면은 피해 80% 감소) 조금 약하게 때린다. 거미는 궁수처럼 약한 대신 거미줄로 묶는다.</summary>
        private const float ShieldHealthRatio = 1.3f;
        private const float ShieldAttackRatio = 0.9f;
        private const float SpiderHealthRatio = 0.7f;
        private const float SpiderAttackRatio = 0.8f;
        /// <summary>폭발병은 약간 약한 대신 폭발(공격력 ×3)이 아프다.</summary>
        private const float BomberHealthRatio = 0.8f;
        private const float BomberAttackRatio = 1f;

        private const int GoldPerKill = 4;       // "쓸 곳은 많은데 수급이 부족하다" 피드백으로 전부 2배(2/20/5 → 4/40/10)
        private const int GoldPerBossKill = 40;
        private const int GoldPerRoomClear = 10;

        /// <summary>일반 방을 클리어하면 최대체력의 이 비율만큼 회복 - 방 사이 회복이 전혀 없어서 한 스테이지 뒤쪽 방(7~10)으로
        /// 갈수록 깎인 체력을 그대로 들고 싸우다 죽던 문제(봇 100판에서 10층 일반 방 사망이 보스 사망의 약 30%) 대책.</summary>
        private const float RoomClearHealRate = 0.1f;

        /// <summary>카메라에 보이는 높이의 절반(칸) - 화면 세로로 약 11칸. 시야(FogOfWar.VisionRadius 4.5칸)보다 조금 넓게.</summary>
        private const float CameraViewHalfHeightCells = 5.5f;

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
        public static int BombPlayerHitCount;
        private RoomEventActor _event;
        private PlayerActor _player;
        private LoopManager _loopManager;
        private RoomDefinition _current;
        private int _stage;

        public int Stage => _stage;
        public IReadOnlyList<EnemyActor> Enemies => _enemies;
        public string RoomName => _current?.RoomName;
        public bool IsBossRoom => _current != null && _current.IsBossRoom;

        /// <summary>예고된 공격 칸(보스 예고 + 불 붙은 폭발병 3x3, 다음 그 몹 턴에 발동) - 비어 있으면 예고 중인 공격 없음.</summary>
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

            Map.PlaceActor(_player, layout.PlayerStart);
            PositionCamera(layout);
            FogOfWar.Create(this, _player, layout, _roomObjects);

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
                _ => (1f, 1f),
            };
            var enemy = go.AddComponent<EnemyActor>();
            enemy.Initialize(def.EnemyMaxHealth * hpRatio, def.EnemyAttackPower * atkRatio, def.IsBossRoom, kind);
            if (def.IsBossRoom)
                enemy.SetupBossPatterns(def.BossPatterns, _stage);
            Map.PlaceActor(enemy, pos);
            enemy.FaceToward(_player.GridPos); // 방패병은 입장한 플레이어 쪽을 보고 시작
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
                _bossDangerTiles.Add(t);
                _telegraphObjects.Add(SpawnTelegraphTile(t, TelegraphColor, -5));
            }
            RebuildDangerTiles();
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
            var go = new GameObject("Telegraph");
            go.transform.SetParent(transform, false);
            VisualUtil.CreateSquareVisual(go, color, GridConstants.CellSize * 0.95f, sortingOrder);
            go.transform.position = new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f);
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
        public void ShowBombTelegraph(EnemyActor bomber, List<Vector2Int> tiles)
        {
            ClearBombTelegraph(bomber);
            var objects = tiles.Select(t => SpawnTelegraphTile(t, BombTelegraphColor, -4)).ToList();
            _bombs[bomber] = (tiles, objects);
            RebuildDangerTiles();
        }

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
            if (_enemies.Count == 0)
                FinishRoomCleared();
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
        private void PositionCamera(RoomLayout layout)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            // 방 전체가 아니라 플레이어 주변만 보이게 당기고(사용자: "시야가 너무 넓다") 플레이어를 따라간다.
            // 방 경계 바깥은 한 칸까지만 비친다.
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = CameraViewHalfHeightCells * GridConstants.CellSize;
            var cs = GridConstants.CellSize;
            var bounds = Rect.MinMaxRect(-1.5f * cs, -1.5f * cs, (layout.Width + 0.5f) * cs, (layout.Height + 0.5f) * cs);
            CameraShake.Follow(cam, _player, bounds);
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
            ExitPosition = null;
            ExitRenderer = null;

            ClearAllTelegraphs();
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
            ClearBombTelegraph(enemy); // 불 붙은 폭발병을 잡으면 불발
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

            FinishRoomCleared();
            return true;
        }

        /// <summary>방의 몹이 전부 사라졌을 때 - 일반 방은 클리어 골드 + 회복 후 다음 방, 보스방은 스테이지 클리어.</summary>
        private void FinishRoomCleared()
        {
            RoomClearCount++;
            LastClearedRoom = _current.RoomName;
            LastClearedWasBoss = _current.IsBossRoom;
            LastClearHpFraction = _player.Stats.CurrentHealth / Mathf.Max(1f, _player.Stats.MaxHealth); // 클리어 회복 전
            if (!_current.IsBossRoom)
            {
                var goldBonus = 1f + _player.Stats.EffectiveGoldBonus;
                GoldWallet.Add(Mathf.RoundToInt(GoldPerRoomClear * StageScaling.RewardMultiplier(_stage) * goldBonus * StageProgress.RepeatRewardMultiplier));
                _player.Stats.Heal(_player.Stats.MaxHealth * RoomClearHealRate);
            }

            if (_current.IsBossRoom)
                _loopManager.OnBossDefeated();
            else
                SpawnExit();
        }

        private const int ExitMinDistance = 6;

        /// <summary>일반 방을 비우면 바로 넘어가지 않고 출구 칸을 연다(사용자 요청) - 플레이어에게서 떨어진 랜덤 빈 칸.
        /// 플레이어가 그 칸을 밟으면(이동/대시) TryUseExit이 다음 방으로 보낸다. 출구는 안개에 가려 있어서 직접 찾아야 한다(메시지로만 알림).</summary>
        private void SpawnExit()
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
                if (enemy.Kind != EnemyKind.Ranged && enemy.IsAdjacentTo(playerPos))
                    claimedSlots.Add(enemy.GridPos);
            }

            foreach (var enemy in snapshot)
            {
                if (enemy == null || enemy.Stats.IsDead)
                    continue;

                enemy.TakeTurn(_player, claimedSlots, this);

                if (_player.Stats.IsDead)
                {
                    ClearAllTelegraphs();
                    _loopManager.OnPlayerDied();
                    return;
                }
            }
        }
    }
}
