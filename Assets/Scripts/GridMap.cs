using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 하나의 격자 좌표계 - "이 칸에 뭐가 있는지"를 순수 딕셔너리 조회로만 판정한다
    /// (물리 콜라이더/OverlapSphere 없음). 이동 가능 여부/공격 대상 판정이 전부 이 클래스 하나로
    /// 끝난다 - 턴제라 매 프레임이 아니라 턴이 넘어갈 때만 조회하면 되므로 이 정도로 충분하다.</summary>
    public class GridMap
    {
        public int Width { get; }
        public int Height { get; }

        private readonly Dictionary<Vector2Int, GridActor> _occupancy = new Dictionary<Vector2Int, GridActor>();

        public GridMap(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool IsInBounds(Vector2Int pos) =>
            pos.x >= 0 && pos.x < Width && pos.y >= 0 && pos.y < Height;

        public GridActor GetActorAt(Vector2Int pos) =>
            _occupancy.TryGetValue(pos, out var actor) ? actor : null;

        public bool IsWalkable(Vector2Int pos) => IsInBounds(pos) && !_occupancy.ContainsKey(pos);

        public void PlaceActor(GridActor actor, Vector2Int pos)
        {
            _occupancy[pos] = actor;
            actor.Map = this;
            actor.GridPos = pos;
            actor.SyncTransform();
        }

        public void MoveActor(GridActor actor, Vector2Int newPos)
        {
            _occupancy.Remove(actor.GridPos);
            _occupancy[newPos] = actor;
            actor.GridPos = newPos;
            actor.SyncTransform();
        }

        /// <summary>죽었거나 방을 떠나는 액터를 점유 목록에서 뺀다 - 이미 다른 액터가 그 자리를
        /// 차지했으면(이론상 안 생기지만 안전장치) 건드리지 않는다.</summary>
        public void RemoveActor(GridActor actor)
        {
            if (_occupancy.TryGetValue(actor.GridPos, out var occupant) && occupant == actor)
                _occupancy.Remove(actor.GridPos);
        }
    }
}
