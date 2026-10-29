using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>8층 미노타우로스 기믹 "조여 오는 벽"(10-16 사용자, 예전 용암 균열을 대신함) - 체력 50% 아래에서 한 번.
    /// 방 바깥 테두리부터 네 벽이 한 줄씩 안으로 조여 온다(처음 CrushStartInterval턴마다, 한 번 조일 때마다 1턴씩 빨라져 CrushMinInterval까지).
    /// 다음에 닫힐 줄은 CrushWarnTurns턴 전부터 예고. 닫히는 줄에 서 있으면 끼어 즉사, 방이 CrushMinRoom칸보다 좁아져도 즉사.
    /// 미노타우로스의 도끼 충격파가 조여 온 벽에 닿으면 그쪽 벽이 한 줄 부서져 물러나고 조이는 박자가 CrushSlowdown턴 느려진다 -
    /// 다가오는 벽을 등지고 서서 충격파를 그쪽으로 유도하는 게 공략.</summary>
    public partial class RoomController
    {
        public const int CrushStartInterval = 10;
        public const int CrushMinInterval = 4;
        public const int CrushWarnTurns = 2;
        public const int CrushSlowdown = 2;
        public const int CrushMinRoom = 7;
        private static readonly Color CrushTelegraphColor = new Color(1f, 0.55f, 0.15f, 0.4f);
        private static readonly Color CrushWallColor = new Color(0.45f, 0.38f, 0.33f);

        // 0 왼쪽 · 1 오른쪽 · 2 아래 · 3 위 - 그쪽 벽이 몇 줄 들어왔는지
        private readonly int[] _crushInset = new int[4];
        private readonly Dictionary<Vector2Int, (int Side, int Line)> _crushWalls = new Dictionary<Vector2Int, (int, int)>();
        private int _crushInterval, _crushCountdown;
        private bool _crushActive;

        public bool CrushActive => _gimmickStage == 8 && _crushActive;
        public IReadOnlyList<int> CrushInset => _crushInset;

        private static readonly Vector2Int[] CrushNormals = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down }; // 그 벽에서 방 안쪽 방향

        /// <summary>방 안쪽 남은 넓이(가로·세로 칸 수).</summary>
        public int CrushInnerWidth => Map.Width - _crushInset[0] - _crushInset[1];
        public int CrushInnerHeight => Map.Height - _crushInset[2] - _crushInset[3];

        private void BeginCrusher()
        {
            System.Array.Clear(_crushInset, 0, 4);
            _crushWalls.Clear();
            _crushInterval = CrushStartInterval;
            _crushCountdown = CrushStartInterval;
            _crushActive = true;
        }

        /// <summary>다음에 닫힐 칸 = 한 줄씩 조인 뒤 안쪽 사각형 밖의 빈 칸 전부. 그 줄만 닫으면 부서졌던 줄의 모서리나 보스 몸 때문에
        /// 비워 둔 칸이 벽 밖에 1칸짜리로 남아 들어가면 못 나오는 "뒤주"가 됐다(10-28 사용자) - 밖에 남은 빈 칸도 같이 닫는다.</summary>
        private HashSet<Vector2Int> NextCrushCells()
        {
            var set = new HashSet<Vector2Int>();
            foreach (var (p, _, _) in CrushTargets())
                set.Add(p);
            return set;
        }

        /// <summary>이번에 닫을 칸과 그 칸이 속할 벽(쪽·줄). 쪽은 왼·오·아래·위 순서로 먼저 걸리는 쪽(예전 줄 닫기와 같은 모서리 배정),
        /// 줄은 그쪽 끝에서 몇 번째 줄인지 - 도끼 충격파가 맨 안쪽 줄을 부술 때 이 줄 번호로 찾는다.</summary>
        private List<(Vector2Int Cell, int Side, int Line)> CrushTargets()
        {
            var list = new List<(Vector2Int, int, int)>();
            for (var x = 0; x < Map.Width; x++)
            for (var y = 0; y < Map.Height; y++)
            {
                var p = new Vector2Int(x, y);
                if (Map.IsWall(p))
                    continue;
                var depth = new[] { x, Map.Width - 1 - x, y, Map.Height - 1 - y }; // 왼·오·아래·위 끝에서 몇 번째 줄
                for (var side = 0; side < 4; side++)
                    if (depth[side] <= _crushInset[side])
                    {
                        list.Add((p, side, depth[side]));
                        break;
                    }
            }
            return list;
        }

        /// <summary>몹 턴 끝마다 - 남은 턴, 예고, 조이기. 즉사면 true(사망 처리는 TickStageGimmick이).</summary>
        private bool TickCrusher()
        {
            if (!_crushActive)
                return false;
            _crushCountdown--;
            if (_crushCountdown > 0)
            {
                if (_crushCountdown <= CrushWarnTurns)
                {
                    ShowEnemyTelegraph(_gimmickBoss, NextCrushCells().ToList(), CrushTelegraphColor, "tel_crush"); // 벽돌 무늬(10-23)
                }
                return false;
            }
            return CloseCrushRing();
        }

        private bool CloseCrushRing()
        {
            ClearEnemyTelegraph(_gimmickBoss);
            var crushed = false;
            foreach (var (p, side, line) in CrushTargets())
            {
                if (Map.IsWall(p))
                    continue;
                if (p == _player.GridPos)
                {
                    crushed = true;
                    continue;
                }
                if (_gimmickBoss != null && GridMap.Occupies(_gimmickBoss, p))
                {
                    // 보스 몸에 걸리면 안쪽으로 한 칸 밀고, 못 밀면 그 칸은 이번엔 비워 둔다(다음 조이기 때 같이 닫힌다)
                    var push = _gimmickBoss.GridPos + CrushNormals[side];
                    if (Map.CanOccupy(_gimmickBoss, push) && !GridMap.Cells(push, _gimmickBoss.FootprintMin, _gimmickBoss.FootprintMax).Contains(_player.GridPos))
                    {
                        Map.MoveActor(_gimmickBoss, push);
                        if (_gimmickBoss.TryGetComponent<SpriteAnimator>(out var anim))
                            anim.SnapToGrid();
                        _gimmickBoss.SyncTransform();
                    }
                    if (GridMap.Occupies(_gimmickBoss, p))
                        continue;
                }
                if (Map.GetActorAt(p) is EnemyActor other && other != _gimmickBoss)
                    DespawnEnemy(other, CrushWallColor); // 졸개 등은 벽에 끼어 사라진다
                AddCrushWall(p);
                _crushWalls[p] = (side, line);
            }
            for (var side = 0; side < 4; side++)
                _crushInset[side]++;
            RefreshCrushWallFaces();
            if (HitFeedback.Enabled)
            {
                CameraShake.Shake(0.3f, 0.5f);
                SfxPlayer.Play(Sfx.Slam);
            }
            _crushInterval = Mathf.Max(CrushMinInterval, _crushInterval - 1);
            _crushCountdown = _crushInterval;
            UpdateGimmickLabel();
            if (crushed)
            {
                DeathCause.Set("g8_crush"); // 사망 대사
                ShowMessage(Loc.T("<color=#FF4040>조여 온 벽에 끼어 버렸다!</color>"));
                return true;
            }
            if (CrushInnerWidth < CrushMinRoom || CrushInnerHeight < CrushMinRoom)
            {
                DeathCause.Set("g8_walls"); // 사망 대사
                ShowMessage(Loc.T("<color=#FF4040>사방의 벽이 맞붙어 모든 것을 짓눌렀다!</color>"));
                return true;
            }
            if (CrushInnerWidth <= CrushMinRoom + 1 || CrushInnerHeight <= CrushMinRoom + 1)
                ShowMessage(Loc.T("<color=#FF6060>방이 거의 닫혔다...</color>"));
            return false;
        }

        /// <summary>도끼 충격파(BossBrain) - 진행 방향 d로 뻗은 줄 끝이 조여 온 벽에 닿았으면 그쪽 벽이 한 줄 부서져 물러난다.</summary>
        public void OnAxeWave(IEnumerable<Vector2Int> tiles, Vector2Int d)
        {
            if (!CrushActive)
                return;
            var side = System.Array.IndexOf(CrushNormals, -d); // 충격파가 향한 벽
            if (side < 0 || _crushInset[side] == 0)
                return;
            var line = _crushInset[side] - 1;
            if (!tiles.Any(t => _crushWalls.TryGetValue(t + d, out var w) && w.Side == side && w.Line == line))
                return;
            foreach (var p in _crushWalls.Where(kv => kv.Value.Side == side && kv.Value.Line == line).Select(kv => kv.Key).ToList())
            {
                _crushWalls.Remove(p);
                if (HitFeedback.Enabled)
                    Fx.Play("crush_dust", new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f), 1.3f, fps: 14f, sortingOrder: 6); // 무너지는 돌먼지(10-23)
                RemoveWallObject(p);
            }
            _crushInset[side]--;
            RefreshCrushWallFaces();
            TrailerControls.Cue(TrailerCue.WallBreak);
            _crushInterval = Mathf.Min(CrushStartInterval, _crushInterval + CrushSlowdown);
            _crushCountdown += CrushSlowdown;
            if (_crushCountdown > CrushWarnTurns)
            {
                ClearEnemyTelegraph(_gimmickBoss);
            }
            else
                ShowEnemyTelegraph(_gimmickBoss, NextCrushCells().ToList(), CrushTelegraphColor, "tel_crush"); // 벽돌 무늬(10-23)
            if (HitFeedback.Enabled)
                CameraShake.Shake(0.25f, 0.4f);
            UpdateGimmickLabel();
        }

        /// <summary>조여 온 벽 한 칸 - 방 벽과 같은 던전 벽 타일(10-16 사용자 "벽 이미지 그대로"). 타일 방이 아니면 예전 소품 방식.</summary>
        private void AddCrushWall(Vector2Int p)
        {
            if (!_tiledRoom || DamagePopup.Suppressed)
            {
                if (!_tiledRoom)
                    AddWallProp(p, "Crusher_Wall", CrushWallColor, "CrusherWall");
                else
                    Map.AddWall(p); // 봇 실행 중 - 그림 없이 벽만
                return;
            }
            Map.AddWall(p);
            var renderer = CreateTile(transform, p, DungeonTiles.Wall(CrushWallShowsFace(p), _tileRng), -8, Color.white);
            renderer.gameObject.name = "CrusherWall";
            _roomObjects.Add(renderer.gameObject);
            _wallObjects[p] = renderer.gameObject;
            if (HitFeedback.Enabled && Fx.Play("crush_dust", renderer.transform.position, 1.3f, fps: 14f, sortingOrder: 6) == null) // 내려앉는 돌먼지(10-23)
                DeathBurst.Spawn(renderer.transform.position, CrushWallColor, 6, GridConstants.CellSize * 1.1f, GridConstants.CellSize * 0.1f);
        }

        /// <summary>아래 칸이 방 안 바닥(벽 아님)이면 앞면이 보인다 - 방 벽(ShowsWallFace)과 같은 규칙을 지금 지도로.</summary>
        private bool CrushWallShowsFace(Vector2Int p)
        {
            var below = p + Vector2Int.down;
            return Map.IsInBounds(below) && !Map.IsWall(below);
        }

        /// <summary>줄이 닫히거나 물러나면 위아래 벽의 앞면/윗면이 바뀐다 - 조여 온 벽 그림을 다시 고른다.</summary>
        private void RefreshCrushWallFaces()
        {
            if (!_tiledRoom || DamagePopup.Suppressed)
                return;
            foreach (var p in _crushWalls.Keys)
                if (_wallObjects.TryGetValue(p, out var go) && go != null && go.TryGetComponent<SpriteRenderer>(out var r))
                    r.sprite = DungeonTiles.Wall(CrushWallShowsFace(p), _tileRng);
        }

        private string CrushLabelText(out bool danger)
        {
            danger = _crushCountdown <= CrushWarnTurns || CrushInnerWidth <= CrushMinRoom + 2 || CrushInnerHeight <= CrushMinRoom + 2;
            return Loc.F("벽 {0}턴 · 방 {1}x{2}", _crushCountdown, CrushInnerWidth, CrushInnerHeight);
        }

        private void ClearCrusher()
        {
            _crushActive = false;
            System.Array.Clear(_crushInset, 0, 4);
            _crushWalls.Clear();
        }
    }
}
