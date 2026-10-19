using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>그림 산산조각(10-19 사용자 - 10층 거울상이 깨질 때 "그 모습 그대로 산산조각 난 것처럼").
    /// 깨지는 순간의 그림을 작은 조각(같은 텍스처의 일부 영역)으로 잘라 제자리에 깔고, 잠깐 은빛으로 금이 간 듯 번쩍인 뒤
    /// 가운데에서 바깥으로 튀며 돌고 떨어지며 사라진다. 조각은 거울처럼 은빛이 섞이고 몇 개는 반짝인다. 봇 실행 중엔 아무것도 안 한다.</summary>
    public class SpriteShatter : MonoBehaviour
    {
        private const float HoldSeconds = 0.09f;  // 금 간 채 멈춰 있는 시간
        private const float FlySeconds = 0.75f;
        private const float Gravity = 9f;          // 칸/초² (GridConstants.CellSize 배)
        private static readonly Color Silver = new Color(0.82f, 0.9f, 1f);

        private sealed class Piece
        {
            public SpriteRenderer Renderer;
            public Vector3 Velocity;
            public float Spin;
            public Color Color;
            public bool Glint;
        }

        private readonly List<Piece> _pieces = new List<Piece>();
        private float _age;

        /// <param name="source">깨질 그림(그 순간의 sprite·flipX·크기·위치를 베낀다)</param>
        /// <param name="pieceCells">조각 하나의 크기(칸 단위) - 작을수록 잘게 깨진다</param>
        /// <param name="fraction">떨어져 나갈 조각 비율(1 = 전부, 금만 갈 때는 일부만)</param>
        /// <param name="force">튀어 나가는 세기(칸/초)</param>
        public static void Spawn(SpriteRenderer source, float pieceCells = 0.28f, float fraction = 1f, float force = 4.5f, float silver = 0.4f)
        {
            if (DamagePopup.Suppressed || source == null || source.sprite == null || source.sprite.texture == null)
                return;
            var sprite = source.sprite;
            var ppu = sprite.pixelsPerUnit;
            var rect = sprite.rect;
            var pivot = sprite.pivot; // rect 안 픽셀 좌표
            var scale = source.transform.lossyScale;
            var piecePx = Mathf.Max(2, Mathf.RoundToInt(pieceCells * GridConstants.CellSize / Mathf.Max(0.0001f, Mathf.Abs(scale.x)) * ppu));

            var go = new GameObject("SpriteShatter");
            var shatter = go.AddComponent<SpriteShatter>();
            var center = source.bounds.center;
            var rng = new System.Random(source.GetInstanceID() ^ Time.frameCount);
            for (var px = 0; px < rect.width; px += piecePx)
            for (var py = 0; py < rect.height; py += piecePx)
            {
                if (fraction < 1f && rng.NextDouble() > fraction)
                    continue;
                var w = Mathf.Min(piecePx, rect.width - px);
                var h = Mathf.Min(piecePx, rect.height - py);
                var sub = new Rect(rect.x + px, rect.y + py, w, h);
                var pieceSprite = Sprite.Create(sprite.texture, sub, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
                var local = new Vector3((px + w * 0.5f - pivot.x) / ppu, (py + h * 0.5f - pivot.y) / ppu, 0f);
                if (source.flipX)
                    local.x = -local.x;
                var world = source.transform.TransformPoint(local);

                var pieceGo = new GameObject("Shard");
                pieceGo.transform.SetParent(go.transform, false);
                pieceGo.transform.position = world;
                pieceGo.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), 1f);
                var r = pieceGo.AddComponent<SpriteRenderer>();
                r.sprite = pieceSprite;
                r.flipX = source.flipX;
                r.sortingOrder = source.sortingOrder + 1;
                RoomClip.Apply(r);
                var baseColor = Color.Lerp(source.color, Silver, silver);
                baseColor.a = source.color.a;
                r.color = Color.Lerp(baseColor, Color.white, 0.6f); // 금 간 순간 번쩍

                var away = world - center;
                away.z = 0f;
                if (away.sqrMagnitude < 0.0001f)
                    away = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), 0f);
                var dir = away.normalized;
                var speed = force * (0.55f + (float)rng.NextDouble() * 0.7f) * GridConstants.CellSize;
                shatter._pieces.Add(new Piece
                {
                    Renderer = r,
                    Velocity = dir * speed + Vector3.up * GridConstants.CellSize * (1.5f + (float)rng.NextDouble() * 1.5f),
                    Spin = ((float)rng.NextDouble() - 0.5f) * 900f,
                    Color = baseColor,
                    Glint = rng.NextDouble() < 0.18,
                });
            }
            if (shatter._pieces.Count == 0)
                Destroy(go);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age < HoldSeconds)
                return;
            var t = (_age - HoldSeconds) / FlySeconds;
            if (t >= 1f)
            {
                foreach (var p in _pieces)
                    if (p.Renderer != null)
                        Destroy(p.Renderer.sprite);
                Destroy(gameObject);
                return;
            }
            var dt = Time.deltaTime;
            foreach (var p in _pieces)
            {
                if (p.Renderer == null)
                    continue;
                p.Velocity += Vector3.down * Gravity * GridConstants.CellSize * dt;
                p.Velocity *= 1f - 1.5f * dt; // 공기 저항 - 너무 멀리 안 날아가게
                var tr = p.Renderer.transform;
                tr.position += p.Velocity * dt;
                tr.Rotate(0f, 0f, p.Spin * dt);
                tr.localScale *= 1f - 0.6f * dt;
                var c = p.Glint && Mathf.Repeat(_age * 12f, 1f) < 0.5f ? Color.white : p.Color;
                c.a = p.Color.a * (1f - t * t);
                p.Renderer.color = c;
            }
        }
    }
}
