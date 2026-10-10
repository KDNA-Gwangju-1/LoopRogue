using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 하나를 실제로 깔 때의 배치 결과 - 시도할 때마다 새로 뽑는다.</summary>
    public class RoomLayout
    {
        public int Width;
        public int Height;
        public Vector2Int PlayerStart;
        public HashSet<Vector2Int> Walls = new HashSet<Vector2Int>();
        public List<Vector2Int> EnemyPositions = new List<Vector2Int>(); // 플레이어에게서 가까운 순
        public Vector2Int? EventPosition;
    }

    /// <summary>방 구성 랜덤화 - 같은 방이라도 시도(루프)마다 크기(±1)/벽/몹 위치/이벤트 칸이 달라진다.
    /// 벽은 모든 빈 칸이 시작 칸에서 이어지도록(갇힌 칸 없게) 검사하고, 안 되면 다시 뽑는다.</summary>
    public static class RoomLayoutGenerator
    {
        private const int MinSize = 5;
        private const int MaxSize = 18;
        private const float NormalWallDensity = 0.08f; // 일반 방 면적의 8%
        private const int MinEnemyDistance = 5;        // 시작 칸에서 몹까지 최소 거리(맨해튼) - 방을 키우면서 3 -> 5(입장하자마자 시야 안에 몰려 있지 않게)
        private const int MaxTries = 30;

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public static RoomLayout Generate(RoomDefinition def, System.Random rng)
        {
            for (var attempt = 0; attempt < MaxTries; attempt++)
            {
                var layout = TryGenerate(def, rng, withWalls: true);
                if (layout != null)
                    return layout;
            }
            return TryGenerate(def, rng, withWalls: false); // 안전망 - 벽 없이
        }

        private static RoomLayout TryGenerate(RoomDefinition def, System.Random rng, bool withWalls)
        {
            var layout = new RoomLayout
            {
                Width = Mathf.Clamp(def.Width + rng.Next(-1, 2), MinSize, MaxSize),
                Height = Mathf.Clamp(def.Height + rng.Next(-1, 2), MinSize, MaxSize),
                PlayerStart = Vector2Int.zero,
            };

            // 보스는 시작 칸 반대편 구석 근처 - 입장하자마자 붙어 있지 않게.
            var bossPos = new Vector2Int(layout.Width - 2, layout.Height - 2);

            // 벽을 깔면 안 되는 칸: 시작 칸 주변(반경 1), 보스 자리 주변
            var reserved = new HashSet<Vector2Int>();
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                reserved.Add(layout.PlayerStart + new Vector2Int(dx, dy));
                if (def.IsBossRoom)
                    reserved.Add(bossPos + new Vector2Int(dx, dy));
            }

            // 보스방은 벽 없이 탁 트인 방(사용자 요청) - 보스 패턴을 기둥 뒤에 숨어 피하는 대신 움직여서 피하게.
            if (withWalls && !def.IsBossRoom)
            {
                var wallCount = Mathf.RoundToInt(layout.Width * layout.Height * NormalWallDensity);
                PlaceWallShapes(layout, reserved, wallCount, def.IsBossRoom, rng);

                if (!AllFloorConnected(layout))
                    return null;
            }

            var free = AllCells(layout).Where(c => !layout.Walls.Contains(c) && c != layout.PlayerStart).ToList();

            if (def.IsBossRoom)
            {
                layout.EnemyPositions.Add(bossPos);
                return layout;
            }

            // 몹: 시작 칸에서 MinEnemyDistance 이상 떨어진 칸 중 랜덤(모자라면 거리 조건 완화).
            var far = free.Where(c => Distance(c, layout.PlayerStart) >= MinEnemyDistance).ToList();
            if (far.Count < def.EnemyCount)
                far = free.Where(c => Distance(c, layout.PlayerStart) >= 2).ToList();
            if (far.Count < def.EnemyCount)
            {
                if (withWalls)
                    return null; // 다시 뽑기
                far = free; // 마지막 안전망(벽 없음) - 거리 조건 없이 들어가는 만큼만
            }

            Shuffle(far, rng);
            layout.EnemyPositions = far.Take(def.EnemyCount)
                .OrderBy(c => Distance(c, layout.PlayerStart)) // 가까운 순 - 뒤쪽(먼 쪽)이 궁수 자리
                .ToList();

            if (def.HasEvent)
            {
                // 이벤트 칸은 캐릭터가 못 지나가는 칸이라, 좁은 통로에 놓이면 그 너머가 갇힌다(봇 로그: 몹의 유일한 출구가
                // 이벤트 칸이라 서로 못 닿아 1500턴 정지). 그 칸을 벽처럼 막아도 나머지가 전부 이어지는 칸에만 놓는다.
                var eventCells = free.Where(c => !layout.EnemyPositions.Contains(c) && Distance(c, layout.PlayerStart) >= 2).ToList();
                Shuffle(eventCells, rng);
                foreach (var c in eventCells)
                {
                    layout.Walls.Add(c);
                    var ok = AllFloorConnected(layout);
                    layout.Walls.Remove(c);
                    if (!ok)
                        continue;
                    layout.EventPosition = c;
                    break;
                }
                if (!layout.EventPosition.HasValue && withWalls)
                    return null; // 이벤트 칸(층마다 확정인 보물상자 포함)을 놓을 자리가 없으면 다시 뽑기
            }

            return layout;
        }

        /// <summary>벽을 한 칸씩 흩뿌리는 대신 모양 있는 덩어리로 깐다(사용자: "갇히는 것 없이 기둥이 생기는 것도 좋다") -
        /// 2x2 기둥, 3~5칸 일자 벽, ㄱ자 벽. 덩어리끼리는 한 칸 이상 띄워서(대각선 포함) 통로가 막히지 않게 하고,
        /// 그래도 갇힌 칸이 생기면 호출부의 연결 검사가 방 전체를 다시 뽑는다. 보스방은 패턴 피할 엄폐물이라 2x2 기둥만.</summary>
        private static void PlaceWallShapes(RoomLayout layout, HashSet<Vector2Int> reserved, int targetCount, bool bossRoom, System.Random rng)
        {
            for (var tries = 0; tries < 300 && layout.Walls.Count < targetCount; tries++)
            {
                var shape = RandomShape(bossRoom, rng);
                var origin = new Vector2Int(rng.Next(layout.Width), rng.Next(layout.Height));
                var cells = shape.Select(o => origin + o).ToList();

                var ok = cells.All(c => c.x >= 0 && c.y >= 0 && c.x < layout.Width && c.y < layout.Height && !reserved.Contains(c));
                if (ok)
                {
                    foreach (var c in cells)
                    {
                        for (var dx = -1; dx <= 1 && ok; dx++)
                        for (var dy = -1; dy <= 1 && ok; dy++)
                        {
                            if (layout.Walls.Contains(c + new Vector2Int(dx, dy)))
                                ok = false;
                        }
                    }
                }
                if (!ok)
                    continue;

                foreach (var c in cells)
                    layout.Walls.Add(c);
            }
        }

        private static List<Vector2Int> RandomShape(bool bossRoom, System.Random rng)
        {
            var shape = new List<Vector2Int>();
            var roll = bossRoom ? 0 : rng.Next(100);
            if (roll < 40)
            {
                // 2x2 기둥
                shape.Add(new Vector2Int(0, 0));
                shape.Add(new Vector2Int(1, 0));
                shape.Add(new Vector2Int(0, 1));
                shape.Add(new Vector2Int(1, 1));
            }
            else if (roll < 75)
            {
                // 일자 벽 3~5칸(가로/세로)
                var length = rng.Next(3, 6);
                var horizontal = rng.Next(2) == 0;
                for (var i = 0; i < length; i++)
                    shape.Add(horizontal ? new Vector2Int(i, 0) : new Vector2Int(0, i));
            }
            else
            {
                // ㄱ자 벽(3칸 + 꺾여서 2칸), 네 방향 중 하나로 뒤집기
                var sx = rng.Next(2) == 0 ? 1 : -1;
                var sy = rng.Next(2) == 0 ? 1 : -1;
                for (var i = 0; i < 3; i++)
                    shape.Add(new Vector2Int(i * sx, 0));
                shape.Add(new Vector2Int(0, sy));
                shape.Add(new Vector2Int(0, 2 * sy));
            }
            return shape;
        }

        private static IEnumerable<Vector2Int> AllCells(RoomLayout layout)
        {
            for (var x = 0; x < layout.Width; x++)
            for (var y = 0; y < layout.Height; y++)
                yield return new Vector2Int(x, y);
        }

        /// <summary>시작 칸에서 벽이 아닌 모든 칸에 갈 수 있는지(갇힌 칸이 없는지).</summary>
        private static bool AllFloorConnected(RoomLayout layout)
        {
            var floorCount = layout.Width * layout.Height - layout.Walls.Count;
            var visited = new HashSet<Vector2Int> { layout.PlayerStart };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(layout.PlayerStart);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next.x < 0 || next.y < 0 || next.x >= layout.Width || next.y >= layout.Height)
                        continue;
                    if (layout.Walls.Contains(next) || !visited.Add(next))
                        continue;
                    queue.Enqueue(next);
                }
            }
            return visited.Count == floorCount;
        }

        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
