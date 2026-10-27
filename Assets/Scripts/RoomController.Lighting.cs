using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>벽 그림자 + 가짜 빛(10-27, 테스터 "벽과 바닥 명도 차이가 작아 부딪힌 뒤에야 벽인 걸 안다" → 사용자가 미리보기 3번 선택).
    /// 실제 2D 라이트(URP)는 안 쓴다 - 파이프라인을 바꾸면 모든 그림의 재질을 바꿔야 해서, 그림을 겹쳐 흉내 낸다.
    /// 그림자: 빛이 위왼쪽에서 온다고 보고 벽 바로 아래·오른쪽 바닥 칸에 반투명 띠(Tiles/Shadow_*, wall_shadow.py).
    /// 빛: 방 전체를 어둡게 덮는 그림 한 장(RoomLighting)에 플레이어 흰빛·벽 횃불·놓은 횃불 주황빛 자리를 3단 계단으로 밝게 뚫는다.
    /// 그리는 순서: 바닥 -10 · 그림자 -9 · 벽 -8 · 어둠 -8(벽보다 카메라 쪽) · 기믹 소품 -7~ · 예고 칸 -5~-4 · 액터 0~ · 안개 8 -
    /// 예고 칸·몹·소품은 어둠 위라 늘 또렷하다. 봇 실행 중(타일 없음)엔 아무것도 만들지 않는다.</summary>
    public partial class RoomController
    {
        private readonly Dictionary<Vector2Int, GameObject> _wallShadows = new Dictionary<Vector2Int, GameObject>();
        private readonly List<Vector2Int> _torchWalls = new List<Vector2Int>(); // 벽돌 횃불 그림(WallFace_Torch)이 깔린 벽
        private static Sprite[] _shadowSprites;
        private RoomLighting _lighting; // 지금 방의 빛(없으면 null - 퍼즐 방·봇)

        /// <summary>푸른 빛을 하나 더 켠다 - 제단에 불이 붙는 순간(LightAltar).</summary>
        private void AddCoolLight(Vector2Int cell)
        {
            if (_lighting != null) // 유니티 null 비교 - 지난 방의 지워진 빛은 null로 친다(?.는 그걸 못 걸러 낸다)
                _lighting.AddCoolLight(new Vector2(cell.x, cell.y - 0.3f));
        }

        private static Sprite ShadowSprite(int i)
        {
            if (_shadowSprites == null)
                _shadowSprites = new[]
                {
                    Resources.Load<Sprite>("Tiles/Shadow_Top"), Resources.Load<Sprite>("Tiles/Shadow_Left"),
                    Resources.Load<Sprite>("Tiles/Shadow_TopLeft"), Resources.Load<Sprite>("Tiles/Shadow_Corner"),
                };
            return _shadowSprites[i];
        }

        /// <summary>그림자를 드리우는 칸 - 방 밖(둘레 장식 벽)이거나 진짜 벽(이름 "Wall"). 수정·바위·구멍 같은 기믹 소품은 아니다.</summary>
        private bool CastsShadow(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= _tileLayout.Width || cell.y >= _tileLayout.Height)
                return true;
            return _wallObjects.TryGetValue(cell, out var go) && go != null && go.name == "Wall";
        }

        /// <summary>바닥 칸 하나의 그림자를 다시 깐다(벽이 생기거나 부서졌을 때도).</summary>
        private void RefreshWallShadow(Vector2Int cell)
        {
            if (_wallShadows.TryGetValue(cell, out var old))
            {
                if (old != null)
                    Destroy(old);
                _wallShadows.Remove(cell);
            }
            if (_floorRoot == null || CastsShadow(cell))
                return;
            var above = CastsShadow(cell + Vector2Int.up);
            var left = CastsShadow(cell + Vector2Int.left);
            var index = above && left ? 2 : above ? 0 : left ? 1 : CastsShadow(cell + new Vector2Int(-1, 1)) ? 3 : -1;
            if (index < 0)
                return;
            var sprite = ShadowSprite(index);
            if (sprite == null)
                return;
            var renderer = CreateTile(_floorRoot, cell, sprite, -9, Color.white);
            renderer.gameObject.name = "WallShadow";
            renderer.transform.position += new Vector3(0f, 0f, 0.05f); // 같은 -9인 퍼즐 발판보다 뒤(발판이 그림자에 덮이지 않게)
            _wallShadows[cell] = renderer.gameObject;
        }

        private void BuildWallShadows(RoomLayout layout)
        {
            _wallShadows.Clear();
            for (var x = 0; x < layout.Width; x++)
            for (var y = 0; y < layout.Height; y++)
                RefreshWallShadow(new Vector2Int(x, y));
        }

        /// <summary>벽이 부서졌을 때(OnWallRemoved) - 그 칸과 그림자를 받던 오른쪽·아래 칸을 다시.</summary>
        private void RefreshShadowsAround(Vector2Int cell)
        {
            if (!_tiledRoom || _tileLayout == null)
                return;
            RefreshWallShadow(cell);
            RefreshWallShadow(cell + Vector2Int.right);
            RefreshWallShadow(cell + Vector2Int.down);
            RefreshWallShadow(cell + new Vector2Int(1, -1));
        }

        /// <summary>일반·보스·테스트 방을 깐 뒤(LoadRoom) - 퍼즐 방은 판을 읽어야 해서 어둡게 하지 않는다.</summary>
        /// <param name="coolLights">푸른 빛(칸 좌표) - 벽화의 방 양옆 불 붙은 제단.</param>
        private void CreateRoomLighting(RoomLayout layout, List<Vector2> coolLights = null)
        {
            if (!_tiledRoom || DamagePopup.Suppressed)
                return;
            var go = new GameObject("RoomLighting");
            go.transform.SetParent(transform, false);
            _lighting = go.AddComponent<RoomLighting>();
            _lighting.Init(this, _player, layout, _torchWalls, coolLights);
            _roomObjects.Add(go);
        }

        /// <summary>방을 덮는 어둠 한 장 - 한 칸 = Res 텍셀(4px 도트 계단). 플레이어가 움직이거나(텍셀 단위) 놓은 횃불 수가 바뀔 때만 다시 그린다.</summary>
        private sealed class RoomLighting : MonoBehaviour
        {
            private const int Res = 8;               // 한 칸(32px)당 텍셀 - 4px 도트
            private const float Ambient = 0.55f;     // 빛이 없는 곳의 밝기
            private const float PlayerRadius = 4.5f; // 칸 - 시야(FogOfWar.VisionRadius)와 같게
            private const float TorchRadius = 3f;
            private const float TorchPower = 0.8f;
            private const float WarmTint = 0.14f;    // 횃불 빛의 주황 기운
            private static readonly Color WarmColor = new Color(0.45f, 0.22f, 0.05f);
            private static readonly Color CoolColor = new Color(0.12f, 0.28f, 0.6f); // 제단의 푸른 불

            private RoomController _room;
            private PlayerActor _player;
            private Vector2Int _origin; // 텍스처 왼쪽 아래 칸(방 둘레 한 칸 바깥)
            private Texture2D _texture;
            private Color32[] _pixels;
            private Color32[] _basePixels;      // 바탕 어둠 + 횃불 빛(플레이어 빛 제외)
            private float[] _baseBright, _baseWarm, _baseCool;
            private List<Vector2> _torches;
            private List<Vector2> _coolLights;
            private Vector2Int _lastKey = new Vector2Int(int.MinValue, 0);
            private int _lastPlaced = -1;

            public void Init(RoomController room, PlayerActor player, RoomLayout layout, List<Vector2Int> torchWalls, List<Vector2> coolLights)
            {
                _coolLights = coolLights ?? new List<Vector2>();
                _room = room;
                _player = player;
                _origin = new Vector2Int(-1, -1);
                var w = (layout.Width + 2) * Res;
                var h = (layout.Height + 2) * Res;
                _texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _pixels = new Color32[w * h];
                _torches = new List<Vector2>();
                foreach (var t in torchWalls)
                    _torches.Add(new Vector2(t.x, t.y - 0.6f)); // 벽돌 앞면에 걸린 횃불 - 불꽃 자리 조금 아래로
                var cs = GridConstants.CellSize;
                var sprite = Sprite.Create(_texture, new Rect(0, 0, w, h), Vector2.zero, Res / cs);
                var renderer = gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = -8;
                transform.position = new Vector3((_origin.x - 0.5f) * cs, (_origin.y - 0.5f) * cs, -0.05f); // 벽(-8)보다 카메라 쪽
                Redraw();
            }

            private void LateUpdate()
            {
                if (_player == null)
                    return;
                var p = PlayerCell();
                var key = new Vector2Int(Mathf.RoundToInt(p.x * Res), Mathf.RoundToInt(p.y * Res));
                var placed = _room.Torches.Count;
                if (key == _lastKey && placed == _lastPlaced)
                    return;
                Redraw();
            }

            private Vector2 PlayerCell() => _player.transform.position / GridConstants.CellSize;

            public void AddCoolLight(Vector2 cell)
            {
                _coolLights.Add(cell);
                _basePixels = null; // 바탕부터 다시
                Redraw();
            }

            /// <summary>횃불 빛과 바탕 어둠(플레이어 빛 제외) - 처음과 놓은 횃불 수가 바뀔 때만 다시 계산해 둔다.</summary>
            private void RebuildBase()
            {
                _lastPlaced = _room.Torches.Count;
                var warmLights = new List<Vector2>(_torches);
                foreach (var t in _room.Torches)
                    warmLights.Add(t); // 아이템으로 놓은 횃불도 빛난다
                var w = _texture.width;
                var h = _texture.height;
                _baseBright = new float[w * h];
                _baseWarm = new float[w * h];
                _baseCool = new float[w * h];
                for (var i = 0; i < _baseBright.Length; i++)
                    _baseBright[i] = Ambient;
                foreach (var t in warmLights)
                {
                    // 그 빛이 닿는 사각형 안만 계산한다
                    ForEachTexelNear(t, TorchRadius, (i, d) =>
                    {
                        var s = Step(d / TorchRadius) * TorchPower;
                        if (s <= 0f)
                            return;
                        _baseBright[i] = Mathf.Max(_baseBright[i], Ambient + (1f - Ambient) * s);
                        _baseWarm[i] = Mathf.Max(_baseWarm[i], s);
                    });
                }
                foreach (var t in _coolLights)
                {
                    ForEachTexelNear(t, TorchRadius, (i, d) =>
                    {
                        var s = Step(d / TorchRadius) * TorchPower;
                        if (s <= 0f)
                            return;
                        _baseBright[i] = Mathf.Max(_baseBright[i], Ambient + (1f - Ambient) * s);
                        _baseCool[i] = Mathf.Max(_baseCool[i], s);
                    });
                }
                for (var i = 0; i < _basePixels.Length; i++)
                    _basePixels[i] = Shade(_baseBright[i], _baseWarm[i], _baseCool[i]);
            }

            /// <summary>바탕을 복사하고 플레이어 빛이 닿는 사각형만 다시 칠한다 - 걸을 때마다 방 전체를 계산하지 않게.</summary>
            private void Redraw()
            {
                if (_basePixels == null || _room.Torches.Count != _lastPlaced)
                {
                    _basePixels = new Color32[_pixels.Length];
                    RebuildBase();
                }
                var p = PlayerCell();
                _lastKey = new Vector2Int(Mathf.RoundToInt(p.x * Res), Mathf.RoundToInt(p.y * Res));
                System.Array.Copy(_basePixels, _pixels, _pixels.Length);
                ForEachTexelNear(p, PlayerRadius, (i, d) =>
                {
                    var s = Step(d / PlayerRadius);
                    if (s <= 0f)
                        return;
                    var bright = Ambient + (1f - Ambient) * s;
                    if (bright > _baseBright[i])
                        _pixels[i] = Shade(bright, _baseWarm[i], _baseCool[i]);
                });
                _texture.SetPixels32(_pixels);
                _texture.Apply(false);
            }

            /// <summary>center(칸 좌표)에서 radius칸 안쪽 사각형의 텍셀마다 (텍셀 번호, 칸 단위 거리).</summary>
            private void ForEachTexelNear(Vector2 center, float radius, System.Action<int, float> visit)
            {
                var w = _texture.width;
                var h = _texture.height;
                var x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - _origin.x + 0.5f) * Res));
                var x1 = Mathf.Min(w - 1, Mathf.CeilToInt((center.x + radius - _origin.x + 0.5f) * Res));
                var y0 = Mathf.Max(0, Mathf.FloorToInt((center.y - radius - _origin.y + 0.5f) * Res));
                var y1 = Mathf.Min(h - 1, Mathf.CeilToInt((center.y + radius - _origin.y + 0.5f) * Res));
                for (var ty = y0; ty <= y1; ty++)
                {
                    var cy = _origin.y - 0.5f + (ty + 0.5f) / Res;
                    for (var tx = x0; tx <= x1; tx++)
                    {
                        var cx = _origin.x - 0.5f + (tx + 0.5f) / Res;
                        var dx = cx - center.x;
                        var dy = cy - center.y;
                        visit(ty * w + tx, Mathf.Sqrt(dx * dx + dy * dy));
                    }
                }
            }

            /// <summary>밝기·빛 기운 → 어둠 픽셀(검정을 1-밝기만큼 덮고, 횃불 곁은 주황·제단 곁은 푸른 기운을 조금).</summary>
            private static Color32 Shade(float bright, float warm, float cool)
            {
                var tint = Mathf.Max(warm, cool);
                var a = Mathf.Max(1f - bright, WarmTint * tint);
                var c = tint > 0f
                    ? (WarmColor * warm + CoolColor * cool) / (warm + cool) * (WarmTint * tint / Mathf.Max(0.001f, a))
                    : Color.black;
                return new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a * 255f));
            }

            /// <summary>빛 세기 3단 계단(도트 느낌) - 반경 안쪽 45%까지 1, 72%까지 0.66, 끝까지 0.33.</summary>
            private static float Step(float d) => d < 0.45f ? 1f : d < 0.72f ? 0.66f : d < 1f ? 0.33f : 0f;

            private void OnDestroy()
            {
                if (_texture != null)
                    Destroy(_texture);
            }
        }
    }
}
