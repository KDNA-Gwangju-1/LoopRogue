using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 하나의 격자 좌표계 - "이 칸에 뭐가 있는지"를 순수 딕셔너리 조회로만 판정한다
    /// (물리 콜라이더/OverlapSphere 없음). 이동 가능 여부/공격 대상 판정이 전부 이 클래스 하나로
    /// 끝난다 - 턴제라 매 프레임이 아니라 턴이 넘어갈 때만 조회하면 되므로 이 정도로 충분하다.
    /// 큰 액터(보스 3x3, GridActor.FootprintRadius)는 차지한 칸 전부가 그 액터로 잡힌다 - 어느 칸을 때려도 맞고, 아무도 못 지나간다.</summary>
    public class GridMap
    {
        public int Width { get; }
        public int Height { get; }

        private readonly Dictionary<Vector2Int, GridActor> _occupancy = new Dictionary<Vector2Int, GridActor>();
        private readonly HashSet<Vector2Int> _walls = new HashSet<Vector2Int>();

        public IReadOnlyCollection<Vector2Int> Walls => _walls;

        public GridMap(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool IsInBounds(Vector2Int pos) =>
            pos.x >= 0 && pos.x < Width && pos.y >= 0 && pos.y < Height;

        public GridActor GetActorAt(Vector2Int pos) =>
            _occupancy.TryGetValue(pos, out var actor) ? actor : null;

        /// <summary>벽(장애물) 칸 - 아무도 못 지나가고 궁수 화살도 막는다(방 구성 랜덤화에서 깐다).</summary>
        public bool IsWall(Vector2Int pos) => _walls.Contains(pos);

        public void AddWall(Vector2Int pos) => _walls.Add(pos);

        /// <summary>폭탄 아이템으로 벽을 부술 때.</summary>
        public void RemoveWall(Vector2Int pos) => _walls.Remove(pos);

        public bool IsWalkable(Vector2Int pos) => IsInBounds(pos) && !_walls.Contains(pos) && !_occupancy.ContainsKey(pos);

        // ---------------- 큰 액터(차지 칸 반경) ----------------

        /// <summary>center를 가운데로 반경 r의 정사각형 칸들(r = 0이면 그 칸 하나).</summary>
        public static IEnumerable<Vector2Int> Cells(Vector2Int center, int r)
        {
            for (var dx = -r; dx <= r; dx++)
            for (var dy = -r; dy <= r; dy++)
                yield return new Vector2Int(center.x + dx, center.y + dy);
        }

        public static IEnumerable<Vector2Int> CellsOf(GridActor actor) => Cells(actor.GridPos, actor.FootprintRadius);

        /// <summary>그 액터가 p 칸을 차지하고 있는지.</summary>
        public static bool Occupies(GridActor actor, Vector2Int p) =>
            Mathf.Abs(p.x - actor.GridPos.x) <= actor.FootprintRadius && Mathf.Abs(p.y - actor.GridPos.y) <= actor.FootprintRadius;

        /// <summary>반경 r 몸(가운데 center)에서 p까지 상하좌우 걸음 수 - 몸에 붙은 칸이면 1, 몸 안이면 0.</summary>
        public static int FootprintDistance(Vector2Int center, int r, Vector2Int p) =>
            Mathf.Max(0, Mathf.Abs(p.x - center.x) - r) + Mathf.Max(0, Mathf.Abs(p.y - center.y) - r);

        public static int FootprintDistance(GridActor actor, Vector2Int p) => FootprintDistance(actor.GridPos, actor.FootprintRadius, p);

        /// <summary>몸 가장자리 기준 체비쇼프 거리(대각선도 1) - 반경 효과(연막·약점 표식 등)용.</summary>
        public static int FootprintChebyshev(GridActor actor, Vector2Int p) =>
            Mathf.Max(Mathf.Max(0, Mathf.Abs(p.x - actor.GridPos.x) - actor.FootprintRadius),
                      Mathf.Max(0, Mathf.Abs(p.y - actor.GridPos.y) - actor.FootprintRadius));

        /// <summary>actor가 center로 가면 차지할 칸이 전부 방 안·벽 아님·(자기 말고) 빈 칸인지.</summary>
        public bool CanOccupy(GridActor actor, Vector2Int center)
        {
            var r = actor.FootprintRadius;
            for (var dx = -r; dx <= r; dx++)
            for (var dy = -r; dy <= r; dy++)
            {
                var p = new Vector2Int(center.x + dx, center.y + dy);
                if (!IsInBounds(p) || _walls.Contains(p))
                    return false;
                if (_occupancy.TryGetValue(p, out var other) && other != actor)
                    return false;
            }
            return true;
        }

        public void PlaceActor(GridActor actor, Vector2Int pos)
        {
            actor.Map = this;
            actor.GridPos = pos;
            Claim(actor);
            actor.SyncTransform();
        }

        public void MoveActor(GridActor actor, Vector2Int newPos)
        {
            Release(actor);
            actor.GridPos = newPos;
            Claim(actor);
            actor.SyncTransform();
        }

        /// <summary>죽었거나 방을 떠나는 액터를 점유 목록에서 뺀다 - 이미 다른 액터가 그 자리를
        /// 차지했으면(이론상 안 생기지만 안전장치) 건드리지 않는다.</summary>
        public void RemoveActor(GridActor actor) => Release(actor);

        private void Claim(GridActor actor)
        {
            if (actor.FootprintRadius == 0)
            {
                _occupancy[actor.GridPos] = actor;
                return;
            }
            foreach (var p in CellsOf(actor))
                _occupancy[p] = actor;
        }

        private void Release(GridActor actor)
        {
            if (actor.FootprintRadius == 0)
            {
                if (_occupancy.TryGetValue(actor.GridPos, out var occupant) && occupant == actor)
                    _occupancy.Remove(actor.GridPos);
                return;
            }
            foreach (var p in CellsOf(actor))
                if (_occupancy.TryGetValue(p, out var occupant) && occupant == actor)
                    _occupancy.Remove(p);
        }
    }
}
