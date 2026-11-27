using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>던전 바닥·벽 도트 타일(Resources/Tiles, 32x32 = 한 칸) - 위에서 비스듬히 내려다보는 시점이라 아래 칸이 바닥인 벽은
    /// 벽돌 앞면(WallFace), 아니면 덮개돌 윗면(WallTop). 바닥은 기본 돌판 10종에 가끔 장식 12종(금·이끼·뼈·물웅덩이·풀·쇠창살·핏자국·거미줄·항아리·버섯·촛농 등).
    /// 그림이 없거나 봇 실행 중이면 RoomController가 예전 단색 바닥/벽을 쓴다(봇 방 생성 속도 유지).</summary>
    public static class DungeonTiles
    {
        private const float DecorChance = 0.12f; // 장식 12종(10-25 6 → 12)이라 조금 더 자주
        private const float TorchChance = 0.12f;

        // 오류방(10-19) - 깨진 타일(Tiles/Glitch*)을 일반 타일과 섞어 깐다(같은 깨진 칸이 줄 맞춰 반복돼 보이지 않게). RoomController가 오류방을 깔 때만 켠다.
        private const float GlitchFloorChance = 0.65f;
        private const float GlitchWallChance = 0.7f;
        public static bool Glitch;

        private static bool _loaded;
        private static GameSettings.ArtStyle _loadedArt;
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

        /// <summary>벽돌 횃불 그림인지 - 가짜 빛(RoomController.Lighting)이 그 자리를 주황빛으로 밝힌다.</summary>
        public static bool IsTorch(Sprite sprite) => sprite != null && sprite == _torch;

        private static Sprite Pick(Sprite[] sprites, System.Random rng) => sprites[rng.Next(sprites.Length)];

        /// <summary>11-28 고퀄 타일(Resources/TilesHQ, 52칸 = 한 칸, 용사와 같은 도트 크기) - 고퀄 그림체이고 그 폴더가 있으면 거기서,
        /// 아니면 클래식(Resources/Tiles, 32칸). 이름은 두 폴더가 같다.</summary>
        private static string Folder => GameSettings.Art == GameSettings.ArtStyle.HighQuality && Resources.Load<Sprite>("TilesHQ/Floor_0") != null
            ? "TilesHQ/" : "Tiles/";

        /// <summary>타일 한 장(Shadow_Top, ExitDoor, WallCracked 등) - 그림체에 맞는 폴더에서, 고퀄에 없으면 클래식으로.</summary>
        public static Sprite Load(string name)
        {
            var folder = Folder;
            var sprite = Resources.Load<Sprite>(folder + name);
            return sprite != null || folder == "Tiles/" ? sprite : Resources.Load<Sprite>("Tiles/" + name);
        }

        private static void Load()
        {
            if (_loaded && _loadedArt == GameSettings.Art)
                return;
            _loaded = true;
            _loadedArt = GameSettings.Art; // 옵션에서 그림체를 바꾸면 다음 방부터 다시 읽음
            var f = Folder;
            _floor = Sequence(f + "Floor_");
            _decor = Sequence(f + "FloorDecor_");
            _wallTop = Sequence(f + "WallTop_");
            _wallFace = Sequence(f + "WallFace_");
            _torch = Resources.Load<Sprite>(f + "WallFace_Torch");
            _glitchFloor = Sequence(f + "GlitchFloor_");
            _glitchTop = Sequence(f + "GlitchWallTop_");
            _glitchFace = Sequence(f + "GlitchWallFace_");
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
