using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>던전 바닥·벽 도트 타일(Resources/Tiles, 32x32 = 한 칸) - 위에서 비스듬히 내려다보는 시점이라 아래 칸이 바닥인 벽은
    /// 벽돌 앞면(WallFace), 아니면 덮개돌 윗면(WallTop). 바닥은 기본 돌판 4종에 가끔 장식(금·이끼·뼈·물웅덩이).
    /// 그림이 없거나 봇 실행 중이면 RoomController가 예전 단색 바닥/벽을 쓴다(봇 방 생성 속도 유지).</summary>
    public static class DungeonTiles
    {
        private const float DecorChance = 0.1f;
        private const float TorchChance = 0.12f;

        private static bool _loaded;
        private static Sprite[] _floor, _decor, _wallTop, _wallFace;
        private static Sprite _torch;

        public static bool Available
        {
            get
            {
                Load();
                return _floor.Length > 0 && _wallTop.Length > 0 && _wallFace.Length > 0;
            }
        }

        public static Sprite Floor(System.Random rng) =>
            _decor.Length > 0 && rng.NextDouble() < DecorChance ? Pick(_decor, rng) : Pick(_floor, rng);

        public static Sprite Wall(bool face, System.Random rng)
        {
            if (!face)
                return Pick(_wallTop, rng);
            return _torch != null && rng.NextDouble() < TorchChance ? _torch : Pick(_wallFace, rng);
        }

        private static Sprite Pick(Sprite[] sprites, System.Random rng) => sprites[rng.Next(sprites.Length)];

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _floor = Sequence("Tiles/Floor_");
            _decor = Sequence("Tiles/FloorDecor_");
            _wallTop = Sequence("Tiles/WallTop_");
            _wallFace = Sequence("Tiles/WallFace_");
            _torch = Resources.Load<Sprite>("Tiles/WallFace_Torch");
        }

        private static Sprite[] Sequence(string prefix)
        {
            var list = new List<Sprite>();
            for (var i = 0; ; i++)
            {
                var sprite = Resources.Load<Sprite>(prefix + i);
                if (sprite == null)
                    break;
                list.Add(sprite);
            }
            return list.ToArray();
        }
    }
}
