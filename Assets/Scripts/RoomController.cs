using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 하나의 청사진 - 크기/입구/적 배치. GameBootstrap이 고정 시퀀스(방 1~5 + 보스방)를
    /// 이걸로 하드코딩해서 들고 있는다(절차적 생성은 2주 범위 밖).</summary>
    public class RoomDefinition
    {
        public string RoomName;
        public int Width;
        public int Height;
        public Vector2Int PlayerStart;
        public List<Vector2Int> EnemyPositions = new List<Vector2Int>();
        public float EnemyMaxHealth;
        public float EnemyAttackPower;
        public bool IsBossRoom;
    }

    /// <summary>방 하나의 실제 상태(격자/적 목록/바닥 시각화)를 관리하고, 적 전멸 시 다음 방 전환 또는
    /// 보스 격파를 LoopManager에 알린다. 방을 옮길 때마다 이전 방 내용을 지우고 같은 오브젝트를
    /// 재사용한다(방마다 새 씬을 만들 필요 없음).</summary>
    public class RoomController : MonoBehaviour
    {
        public GridMap Map { get; private set; }
        public bool IsInputLocked { get; set; }

        private readonly List<EnemyActor> _enemies = new List<EnemyActor>();
        private GameObject _floorGo;
        private PlayerActor _player;
        private LoopManager _loopManager;
        private RoomDefinition _current;
        private int _stage;

        public int Stage => _stage;
        public IReadOnlyList<EnemyActor> Enemies => _enemies;
        public string RoomName => _current?.RoomName;
        public bool IsBossRoom => _current != null && _current.IsBossRoom;

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

            Map = new GridMap(def.Width, def.Height);
            BuildFloor(def);
            PositionCamera(def);

            Map.PlaceActor(_player, def.PlayerStart);

            foreach (var pos in def.EnemyPositions)
                SpawnEnemy(pos, def);

            IsInputLocked = false;
        }

        private void SpawnEnemy(Vector2Int pos, RoomDefinition def)
        {
            var go = new GameObject(def.IsBossRoom ? "Boss" : "Enemy");
            go.transform.SetParent(transform, false);

            var enemy = go.AddComponent<EnemyActor>();
            enemy.Initialize(def.EnemyMaxHealth, def.EnemyAttackPower, def.IsBossRoom);
            Map.PlaceActor(enemy, pos);
            _enemies.Add(enemy);
        }

        private void BuildFloor(RoomDefinition def)
        {
            _floorGo = new GameObject("Floor");
            _floorGo.transform.SetParent(transform, false);

            var sizeX = def.Width * GridConstants.CellSize;
            var sizeY = def.Height * GridConstants.CellSize;
            var color = def.IsBossRoom ? new Color(0.22f, 0.05f, 0.05f) : new Color(0.15f, 0.15f, 0.18f);

            // sortingOrder를 액터들(0 이상)보다 낮게 둬서 항상 맨 뒤에 깔린다.
            var renderer = VisualUtil.CreateSquareVisual(_floorGo, color, 1f, sortingOrder: -10);
            _floorGo.transform.localScale = new Vector3(sizeX, sizeY, 1f);
            renderer.transform.position = new Vector3(
                (def.Width - 1) * GridConstants.CellSize * 0.5f,
                (def.Height - 1) * GridConstants.CellSize * 0.5f,
                0f);
        }

        /// <summary>방마다 크기가 달라서(보스방은 더 큼) 매 방 로드 시 카메라를 방 중앙으로 다시
        /// 맞춘다 - 2D라 회전 없이 Z축 뒤로 물러나서 보는 표준 카메라.</summary>
        private static void PositionCamera(RoomDefinition def)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var centerX = (def.Width - 1) * GridConstants.CellSize * 0.5f;
            var centerY = (def.Height - 1) * GridConstants.CellSize * 0.5f;
            cam.transform.position = new Vector3(centerX, centerY, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(def.Width, def.Height) * GridConstants.CellSize * 0.6f;
        }

        private void ClearRoom()
        {
            foreach (var enemy in _enemies)
                if (enemy != null)
                    Destroy(enemy.gameObject);
            _enemies.Clear();

            if (_floorGo != null)
                Destroy(_floorGo);
        }

        private const int GoldPerKill = 4;       // "쓸 곳은 많은데 수급이 부족하다" 피드백으로 전부 2배(2/20/5 → 4/40/10)
        private const int GoldPerBossKill = 40;
        private const int GoldPerRoomClear = 10;

        /// <summary>적 하나가 죽은 직후 호출 - 목록에서 빼고, 방이 비었으면 다음 방/승리로 진행한다.
        /// 방이 전환됐으면 true(호출부는 이번 턴엔 몹 행동을 건너뛴다 - 새 방으로 넘어간 직후 바로
        /// 두들겨 맞으면 억울하다). 골드는 여기서 한 곳에서만 지급한다 - 루프가 리셋돼도(LoopManager.
        /// OnPlayerDied) 전혀 안 건드리는 GoldWallet(영구 저장)에 쌓이므로 "루프 돌아도 골드는
        /// 유지" 요청이 자동으로 만족된다. 몹 스탯과 같은 StageScaling 배율을 곱해서 지급한다 -
        /// "스테이지 지날수록 골드도 더 주나?" 질문으로 발견한 문제(몹은 세지는데 골드는 고정값)의
        /// 수정.</summary>
        public bool NotifyEnemyDefeated(EnemyActor enemy)
        {
            var stageMultiplier = StageScaling.RewardMultiplier(_stage);
            var goldBonus = 1f + _player.Stats.EffectiveGoldBonus; // 골드 증감 카드(하한 -50%)
            var killGold = Mathf.RoundToInt((enemy.IsBoss ? GoldPerBossKill : GoldPerKill) * stageMultiplier * goldBonus);
            GoldWallet.Add(killGold);

            _enemies.Remove(enemy);
            if (_enemies.Count > 0)
                return false;

            if (!_current.IsBossRoom)
                GoldWallet.Add(Mathf.RoundToInt(GoldPerRoomClear * stageMultiplier * goldBonus));

            if (_current.IsBossRoom)
                _loopManager.OnBossDefeated();
            else
                _loopManager.AdvanceToNextRoom();
            return true;
        }

        public void RunEnemyTurns()
        {
            // 스냅샷을 떠서 순회한다 - 턴 도중 죽어서 리스트가 바뀌어도 이번 순회엔 영향 없게.
            var snapshot = _enemies.ToArray();
            foreach (var enemy in snapshot)
            {
                if (enemy == null || enemy.Stats.IsDead)
                    continue;

                enemy.TakeTurn(_player);

                if (_player.Stats.IsDead)
                {
                    _loopManager.OnPlayerDied();
                    return;
                }
            }
        }
    }
}
