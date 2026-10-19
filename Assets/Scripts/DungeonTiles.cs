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

        // 오류방(10-19) - 깨진 타일(Tiles/Glitch*)을 일반 타일과 섞어 깐다(같은 깨진 칸이 줄 맞춰 반복돼 보이지 않게). RoomController가 오류방을 깔 때만 켠다.
        private const float GlitchFloorChance = 0.65f;
        private const float GlitchWallChance = 0.7f;
        public static bool Glitch;

        private static bool _loaded;
        private static Sprite[] _floor, _decor, _wallTop, _wallFace, _glitchFloor, _glitchTop, _glitchFace;
        private static Sprite _torch;

        public static bool Available
        {
            get
            {
                Load();
                return _floor.Length > 0 && _wallTop.Length > 0 && _wallFace.Length > 0;
            }
        }

        public static Sprite Floor(System.Random rng)
        {
            if (Glitch && _glitchFloor.Length > 0 && rng.NextDouble() < GlitchFloorChance)
                return Pick(_glitchFloor, rng);
            return _decor.Length > 0 && rng.NextDouble() < DecorChance ? Pick(_decor, rng) : Pick(_floor, rng);
        }

        public static Sprite Wall(bool face, System.Random rng)
        {
            if (Glitch && rng.NextDouble() < GlitchWallChance)
            {
                var set = face ? _glitchFace : _glitchTop;
                if (set.Length > 0)
                    return Pick(set, rng);
            }
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
            _glitchFloor = Sequence("Tiles/GlitchFloor_");
            _glitchTop = Sequence("Tiles/GlitchWallTop_");
            _glitchFace = Sequence("Tiles/GlitchWallFace_");
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
