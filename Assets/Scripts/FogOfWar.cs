using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>시야 제한(안개) - 칸마다 검은 덮개를 깔고, 플레이어에게서 VisionRadius 안이면서 벽에 가리지 않은 칸만 걷어낸다.
    /// 한 번 본 칸은 어둡게 기억(지형만 보이고 몹은 안 보임), 못 본 칸은 거의 까맣게. 몹은 지금 보이는 칸에 있을 때만 그린다
    /// (보스는 항상 보임). 예고 칸(보스/폭발)은 어둠 속이어도 비쳐 보인다 - 안 보이는 공격에 맞는 건 불합리해서.
    /// 순수 표시용이라 게임 로직(몹 행동, 판정)은 그대로이고, 자동 플레이 봇(DamagePopup.Suppressed) 중엔 아무것도 안 한다.</summary>
    public class FogOfWar : MonoBehaviour
    {
        /// <summary>시야 반경(칸) - 유클리드 거리.</summary>
        public const float VisionRadius = 4.5f;

        private const float UnseenAlpha = 0.95f;
        private const float ExploredAlpha = 0.6f;
        private const float DangerMaxAlpha = 0.3f;
        private const int SortingOrder = 8; // 액터(0~1)·파편(5~6) 위, 데미지 숫자(10) 아래

        private RoomController _room;
        private PlayerActor _player;
        private int _width;
        private int _height;
        private SpriteRenderer[,] _cells;
        private bool[,] _explored;
        private readonly HashSet<Vector2Int> _visible = new HashSet<Vector2Int>();
        private SpriteRenderer _exitTracked;
        private bool _exitSeen;

        /// <summary>방을 깔 때마다 새로 만든다(덮개 오브젝트는 방 오브젝트 목록에 넣어 방과 같이 지워지게).</summary>
        public static FogOfWar Create(RoomController room, PlayerActor player, RoomLayout layout, List<GameObject> roomObjects)
        {
            if (DamagePopup.Suppressed)
                return null; // 봇은 방을 수천 번 깔아서 칸마다 오브젝트를 만들면 느려진다 - 안 보이니 아예 안 만든다.

            var go = new GameObject("FogOfWar");
            go.transform.SetParent(room.transform, false);
            roomObjects.Add(go);

            var fog = go.AddComponent<FogOfWar>();
            fog._room = room;
            fog._player = player;
            fog._width = layout.Width;
            fog._height = layout.Height;
            fog._cells = new SpriteRenderer[layout.Width, layout.Height];
            fog._explored = new bool[layout.Width, layout.Height];

            for (var x = 0; x < layout.Width; x++)
            for (var y = 0; y < layout.Height; y++)
            {
                var cell = new GameObject("Fog");
                cell.transform.SetParent(go.transform, false);
                var renderer = VisualUtil.CreateSquareVisual(cell, new Color(0f, 0f, 0f, UnseenAlpha), GridConstants.CellSize * 1.01f, SortingOrder);
                cell.transform.position = new Vector3(x * GridConstants.CellSize, y * GridConstants.CellSize, -0.2f);
                fog._cells[x, y] = renderer;
            }

            fog.Refresh();
            return fog;
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (_player == null || _room.Map == null)
                return;

            _visible.Clear();
            ComputeVisible(_room.Map, _player.GridPos, VisionRadius);
            var danger = _room.DangerTiles;

            for (var x = 0; x < _width; x++)
            for (var y = 0; y < _height; y++)
            {
                var p = new Vector2Int(x, y);
                float alpha;
                if (_visible.Contains(p))
                {
                    _explored[x, y] = true;
                    alpha = 0f;
                }
                else
                {
                    alpha = _explored[x, y] ? ExploredAlpha : UnseenAlpha;
                }
                if (danger.Contains(p))
                    alpha = Mathf.Min(alpha, DangerMaxAlpha);
                _cells[x, y].color = new Color(0f, 0f, 0f, alpha);
            }

            // 출구 - 생긴 뒤 시야에 한 번이라도 들어와야 보인다. 그 전엔 이미 지나가서 어둡게 기억된 칸에 생겨도 비쳐 보이지 않게.
            var exit = _room.ExitRenderer;
            if (exit != _exitTracked)
            {
                _exitTracked = exit;
                _exitSeen = false;
            }
            if (exit != null && _room.ExitPosition.HasValue)
            {
                if (_visible.Contains(_room.ExitPosition.Value))
                    _exitSeen = true;
                exit.enabled = _exitSeen;
            }

            foreach (var enemy in _room.Enemies)
            {
                if (enemy == null)
                    continue;
                var renderer = enemy.GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.enabled = enemy.IsBoss || _visible.Contains(enemy.GridPos);
            }
        }

        /// <summary>반경 안 칸 중 플레이어 칸에서 직선으로 이었을 때 사이에 벽이 없는 칸(벽 자체는 보임).</summary>
        private void ComputeVisible(GridMap map, Vector2Int origin, float radius)
        {
            var r = Mathf.CeilToInt(radius);
            for (var dx = -r; dx <= r; dx++)
            for (var dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                var p = origin + new Vector2Int(dx, dy);
                if (!map.IsInBounds(p))
                    continue;
                if (HasLineOfSight(map, origin, p))
                    _visible.Add(p);
            }
        }

        /// <summary>브레젠험 직선 - 시작/끝 칸을 뺀 중간 칸에 벽이 있으면 가림.</summary>
        private static bool HasLineOfSight(GridMap map, Vector2Int from, Vector2Int to)
        {
            int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            while (true)
            {
                if (x0 == x1 && y0 == y1)
                    return true;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
                if ((x0 != x1 || y0 != y1) && map.IsWall(new Vector2Int(x0, y0)))
                    return false;
            }
        }
    }
}
