using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>5층 버섯 군주 독안개(10-16 사용자) - 감염 기믹이 시작되면 보랏빛 포자 안개가 방 전체를 천천히 덮는다(연출만, 규칙은 그대로).
    /// "다 피했는데 감염됐다"가 안 이상하게 - 12턴마다 공기 중 포자로 감염될 때 안개가 짙게 번쩍이고, 그 턴이 가까울수록 점점 짙어진다.
    /// 봇 실행 중엔 만들지 않는다.</summary>
    public partial class RoomController
    {
        private SporeFog _sporeFog;

        // ---- 맑은 공기(10-16 사용자) - 빛버섯을 먹은 자리 둘레 3x3이 CleanAirTurns턴 동안 안전지대: 그 안에선 독안개로 감염되지 않는다
        // (파동에 맞으면 여전히 감염). 안개가 걷히고 바닥이 옅은 초록으로 빛난다.
        public const int CleanAirTurns = 12;
        private static readonly Color CleanAirColor = new Color(0.55f, 1f, 0.7f, 0.2f);
        private readonly Dictionary<Vector2Int, int> _cleanAir = new Dictionary<Vector2Int, int>(); // 가운데 칸 → 남은 턴
        private readonly Dictionary<Vector2Int, List<GameObject>> _cleanAirTiles = new Dictionary<Vector2Int, List<GameObject>>();

        public IReadOnlyCollection<Vector2Int> CleanAirCenters => _cleanAir.Keys;

        // ---- 환각·혼란(10-16 사용자) - 5층 감염 단계별: 감염 1부터 환각(파동·역파동 예고 고리가 반대 = 홀수↔짝수로 보임),
        // 감염 2부터 혼란(방향키 반대)까지. 빛버섯으로 감염이 0이 되면 둘 다 풀린다. 실제 맞는 칸(봇·위험 판정)은 그대로 - 사람 눈에만 거꾸로.
        // 화면 가장자리가 보랏빛으로 일렁이고, 혼란이면 더 짙게.
        public const int HallucinationInfection = 1;
        public const int ConfusionInfection = 2;
        private bool SporeActive => _gimmickStage == 5 && _gimmickBoss != null && !_gimmickBoss.Stats.IsDead;
        public bool PlayerHallucinating => SporeActive && _infection >= HallucinationInfection;
        public bool PlayerConfused => SporeActive && _infection >= ConfusionInfection;

        private void UpdateHallucination()
        {
            if (!PlayerHallucinating || DamagePopup.Suppressed || Camera.main == null)
                return;
            if (Camera.main.GetComponent<HallucinationVignette>() == null)
                Camera.main.gameObject.AddComponent<HallucinationVignette>().Setup(this);
        }

        /// <summary>환각 중 화면 가장자리 보랏빛 일렁임 - 카메라에 붙어서, 환각이 풀리면 스스로 사라진다.</summary>
        private sealed class HallucinationVignette : MonoBehaviour
        {
            private RoomController _room;
            private Transform _quad;
            private SpriteRenderer _renderer;
            private static Sprite _sprite;
            private float _born;

            public void Setup(RoomController room)
            {
                _room = room;
                _born = Time.time;
                var go = new GameObject("HallucinationVignette");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 5f);
                _quad = go.transform;
                _renderer = go.AddComponent<SpriteRenderer>();
                _renderer.sprite = VignetteSprite();
                _renderer.sortingOrder = 40;
            }

            private void LateUpdate()
            {
                var cam = GetComponent<Camera>();
                if (_room == null || !_room.PlayerHallucinating || cam == null)
                {
                    if (_quad != null)
                        Destroy(_quad.gameObject);
                    Destroy(this);
                    return;
                }
                var h = cam.orthographicSize * 2f;
                var w = h * cam.aspect;
                var t = Time.time - _born;
                var wobble = 1f + Mathf.Sin(t * 2.3f) * 0.04f;
                _quad.localScale = new Vector3(w * 1.08f * wobble, h * 1.08f * (2f - wobble), 1f);
                _quad.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.7f) * 1.5f);
                var a = Mathf.Clamp01(t / 0.6f) * (0.55f + 0.2f * Mathf.Sin(t * 3.1f)) * (_room.PlayerConfused ? 1.35f : 1f); // 혼란이면 더 짙게
                _renderer.color = new Color(0.62f, 0.3f, 0.85f, a);
            }

            /// <summary>가운데는 비고 가장자리로 갈수록 짙어지는 사각 그림(코드로 만든 64x64, 1유닛).</summary>
            private static Sprite VignetteSprite()
            {
                if (_sprite != null)
                    return _sprite;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (var x = 0; x < size; x++)
                for (var y = 0; y < size; y++)
                {
                    var dx = Mathf.Abs(x + 0.5f - size / 2f) / (size / 2f);
                    var dy = Mathf.Abs(y + 0.5f - size / 2f) / (size / 2f);
                    var d = Mathf.Max(dx, dy) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                    var a = Mathf.Clamp01((d - 0.62f) / 0.38f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
                tex.Apply();
                _sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                return _sprite;
            }
        }

        public int InfectionClock => _infectionClock;
        public bool InCleanAir(Vector2Int p) => _cleanAir.Keys.Any(c => Mathf.Max(Mathf.Abs(c.x - p.x), Mathf.Abs(c.y - p.y)) <= 1);

        private void AddCleanAir(Vector2Int center)
        {
            RemoveCleanAirTiles(center);
            _cleanAir[center] = CleanAirTurns;
            if (!DamagePopup.Suppressed)
            {
                var tiles = new List<GameObject>();
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    var p = center + new Vector2Int(dx, dy);
                    if (!Map.IsInBounds(p) || Map.IsWall(p))
                        continue;
                    var go = new GameObject("CleanAir");
                    go.transform.SetParent(transform, false);
                    VisualUtil.CreateSquareVisual(go, CleanAirColor, GridConstants.CellSize * 0.96f, sortingOrder: -6);
                    go.transform.position = new Vector3(p.x * GridConstants.CellSize, p.y * GridConstants.CellSize, 0f);
                    _roomObjects.Add(go);
                    tiles.Add(go);
                }
                _cleanAirTiles[center] = tiles;
                DeathBurst.Spawn(new Vector3(center.x * GridConstants.CellSize, center.y * GridConstants.CellSize, 0f), new Color(0.6f, 1f, 0.75f), 16, GridConstants.CellSize * 2.2f, GridConstants.CellSize * 0.12f);
            }
            _sporeFog?.SetClearZones(_cleanAir.Keys);
        }

        /// <summary>턴마다 - 남은 턴을 줄이고 끝난 곳은 지운다. 끝나 가면(3턴 이하) 바닥 빛이 옅어진다.</summary>
        private void TickCleanAir()
        {
            foreach (var c in _cleanAir.Keys.ToList())
            {
                var left = --_cleanAir[c];
                if (left <= 0)
                {
                    _cleanAir.Remove(c);
                    RemoveCleanAirTiles(c);
                    continue;
                }
                if (left <= 3 && _cleanAirTiles.TryGetValue(c, out var tiles))
                    foreach (var go in tiles)
                        if (go != null && go.TryGetComponent<SpriteRenderer>(out var r))
                            r.color = new Color(CleanAirColor.r, CleanAirColor.g, CleanAirColor.b, CleanAirColor.a * left / 4f);
            }
            _sporeFog?.SetClearZones(_cleanAir.Keys);
        }

        private void RemoveCleanAirTiles(Vector2Int center)
        {
            if (!_cleanAirTiles.TryGetValue(center, out var tiles))
                return;
            foreach (var go in tiles)
                if (go != null)
                    Destroy(go);
            _cleanAirTiles.Remove(center);
        }

        private void SpawnSporeFog()
        {
            if (DamagePopup.Suppressed || _sporeFog != null)
                return;
            var go = new GameObject("SporeFog");
            go.transform.SetParent(transform, false);
            _roomObjects.Add(go);
            _sporeFog = go.AddComponent<SporeFog>();
            _sporeFog.Setup(Map.Width, Map.Height);
        }

        /// <summary>감염 시계(남은 턴)에 맞춰 안개 짙기 - 12턴마다 감염이면 마지막 몇 턴에 짙어진다.</summary>
        private void UpdateSporeFog(bool infectedNow)
        {
            if (_sporeFog == null)
                return;
            if (infectedNow)
                _sporeFog.Pulse();
            _sporeFog.SetThickness(_infectionClock <= 3 ? 1f - (_infectionClock - 1) / 3f : 0f);
        }

        private void ClearSporeFog()
        {
            if (_sporeFog != null)
                Destroy(_sporeFog.gameObject);
            _sporeFog = null;
            foreach (var c in _cleanAirTiles.Keys.ToList())
                RemoveCleanAirTiles(c);
            _cleanAir.Clear();
        }

        private sealed class SporeFog : MonoBehaviour
        {
            private const float FadeInSeconds = 1.6f;
            private const float BaseAlpha = 0.2f;
            private const float ThickAlpha = 0.16f;  // 감염 직전에 더해지는 짙기
            private const float PulseAlpha = 0.28f;  // 감염되는 순간 번쩍
            private static readonly Color FogColor = new Color(0.62f, 0.42f, 0.78f);
            private static Sprite _puffSprite;

            private struct Puff
            {
                public Transform T;
                public SpriteRenderer R;
                public Vector2 Origin, Drift;
                public float Phase, Speed, Size;
            }

            private readonly List<Puff> _puffs = new List<Puff>();
            private float _born;
            private float _thickness, _thicknessShown;
            private float _pulseAt = -10f;

            public void Setup(int width, int height)
            {
                _born = Time.time;
                var cell = GridConstants.CellSize;
                var count = Mathf.Clamp(width * height / 5, 20, 90);
                for (var i = 0; i < count; i++)
                {
                    var go = new GameObject("Puff");
                    go.transform.SetParent(transform, false);
                    var r = go.AddComponent<SpriteRenderer>();
                    r.sprite = PuffSprite();
                    r.sortingOrder = 3; // 몹·플레이어 위, 머리 위 글자(30) 아래
                    var size = Random.Range(3.5f, 6f) * cell;
                    go.transform.localScale = new Vector3(size, size * Random.Range(0.75f, 1f), 1f);
                    _puffs.Add(new Puff
                    {
                        T = go.transform,
                        R = r,
                        Origin = new Vector2(Random.Range(-1f, width) * cell, Random.Range(-1f, height) * cell),
                        Drift = Random.insideUnitCircle * cell * Random.Range(0.6f, 1.4f),
                        Phase = Random.value * 10f,
                        Speed = Random.Range(0.12f, 0.3f),
                        Size = size,
                    });
                }
                Update();
            }

            public void SetThickness(float t) => _thickness = Mathf.Clamp01(t);

            private readonly List<Vector2> _clear = new List<Vector2>();
            private const float ClearRadiusCells = 2.6f;

            /// <summary>맑은 공기 자리(칸) - 그 둘레 연기는 옅어진다.</summary>
            public void SetClearZones(IEnumerable<Vector2Int> centers)
            {
                _clear.Clear();
                foreach (var c in centers)
                    _clear.Add(new Vector2(c.x * GridConstants.CellSize, c.y * GridConstants.CellSize));
            }
            public void Pulse() => _pulseAt = Time.time;

            private void Update()
            {
                var fade = Mathf.Clamp01((Time.time - _born) / FadeInSeconds);
                _thicknessShown = Mathf.MoveTowards(_thicknessShown, _thickness, Time.deltaTime * 1.5f);
                var pulse = Mathf.Clamp01(1f - (Time.time - _pulseAt) / 0.8f);
                var alpha = (BaseAlpha + ThickAlpha * _thicknessShown + PulseAlpha * pulse) * fade;
                foreach (var p in _puffs)
                {
                    if (p.T == null)
                        continue;
                    var t = Time.time * p.Speed + p.Phase;
                    p.T.position = new Vector3(p.Origin.x + Mathf.Sin(t) * p.Drift.x, p.Origin.y + Mathf.Cos(t * 0.8f) * p.Drift.y, 0f);
                    var c = FogColor;
                    var clear = 1f;
                    foreach (var z in _clear) // 맑은 공기 둘레는 걷힌다
                        clear = Mathf.Min(clear, Mathf.Clamp01((Vector2.Distance(z, p.T.position) / GridConstants.CellSize - 0.5f) / ClearRadiusCells));
                    c.a = alpha * (0.65f + 0.35f * Mathf.Sin(t * 1.7f)) * clear;
                    p.R.color = c;
                }
            }

            /// <summary>가장자리가 부드럽게 사라지는 둥근 연기 한 덩이(코드로 만든 64x64 그림, 1유닛 = 1칸 배율 기준).</summary>
            private static Sprite PuffSprite()
            {
                if (_puffSprite != null)
                    return _puffSprite;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (var x = 0; x < size; x++)
                for (var y = 0; y < size; y++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
                tex.Apply();
                _puffSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                return _puffSprite;
            }
        }
    }
}
