using System.Collections;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>특성 "노다지" 연출(11-02 사용자 "금화가 비처럼") - 금화(Effects/coin)가 플레이어 둘레 3x3 위에서 0.8초에 걸쳐 쏟아져 떨어지고,
    /// 바닥에 닿으면 반짝이며 작게 튀었다 사라진다. 떨어지는 동안 옅은 금빛 꼬리. 시안 coinrain.gif(scratchpad coinrain_preview.py)와 같은 수치.</summary>
    public class CoinRain : MonoBehaviour
    {
        private const int CoinCount = 28;
        private const float SpawnSeconds = 0.8f;
        private const float SpreadX = 1.4f;       // 칸
        private const float SpreadY = 1.2f;
        private const float DropHeight = 3f;      // 착지점 위 이만큼에서
        private const float MinSpeed = 9f, MaxSpeed = 13f; // 칸/초
        private const float AnimFps = 18f;
        private const float LandSeconds = 0.35f;
        private const int TrailCount = 2;

        public static void Play(Vector3 center)
        {
            var frames = Fx.Frames("coin");
            if (frames == null || frames.Length == 0)
                return;
            var go = new GameObject("CoinRain");
            go.transform.position = center;
            go.AddComponent<CoinRain>().StartCoroutine(Run(go, center, frames));
        }

        private static IEnumerator Run(GameObject owner, Vector3 center, Sprite[] frames)
        {
            var cell = GridConstants.CellSize;
            var alive = 0;
            var runner = owner.GetComponent<CoinRain>();
            var spawnTimes = new float[CoinCount];
            for (var i = 0; i < CoinCount; i++)
                spawnTimes[i] = Random.Range(0f, SpawnSeconds);
            System.Array.Sort(spawnTimes);
            var t = 0f;
            var next = 0;
            while (next < CoinCount)
            {
                while (next < CoinCount && spawnTimes[next] <= t)
                {
                    var target = center + new Vector3(Random.Range(-SpreadX, SpreadX), Random.Range(-SpreadY, SpreadY), 0f) * cell;
                    alive++;
                    runner.StartCoroutine(Coin(owner.transform, target, frames, Random.Range(MinSpeed, MaxSpeed) * cell,
                        Random.Range(0, frames.Length), Random.Range(0.3f, 0.45f) * cell, () => alive--));
                    next++;
                }
                t += Time.deltaTime;
                yield return null;
            }
            while (alive > 0)
                yield return null;
            Destroy(owner);
        }

        private static IEnumerator Coin(Transform parent, Vector3 target, Sprite[] frames, float speed, int phase, float size, System.Action done)
        {
            var renderers = new SpriteRenderer[TrailCount + 1];
            for (var k = 0; k <= TrailCount; k++)
            {
                var go = new GameObject(k == 0 ? "Coin" : "CoinTrail");
                go.transform.SetParent(parent, false);
                var r = VisualUtil.CreateSpriteVisual(go, frames[phase], size * (1f - k * 0.25f), sortingOrder: 9);
                r.color = new Color(1f, 1f, 1f, k == 0 ? 1f : 0.25f / k);
                RoomClip.Apply(r);
                renderers[k] = r;
            }
            var fall = DropHeight * GridConstants.CellSize / speed;
            var lt = 0f;
            while (true)
            {
                var frame = frames[(phase + (int)(lt * AnimFps)) % frames.Length];
                if (lt < fall)
                {
                    var y = target.y + (fall - lt) * speed;
                    for (var k = 0; k <= TrailCount; k++)
                    {
                        renderers[k].sprite = frame;
                        renderers[k].transform.position = new Vector3(target.x, y + k * size * 0.5f, -0.6f);
                    }
                }
                else
                {
                    var after = lt - fall;
                    if (after >= LandSeconds)
                        break;
                    if (renderers[1] != null)
                    {
                        for (var k = 1; k <= TrailCount; k++)
                            Destroy(renderers[k].gameObject);
                        renderers[1] = null;
                        Fx.Play("hit_spark", target, 0.45f, tint: new Color(1f, 0.95f, 0.7f), fps: 24f, sortingOrder: 10); // 닿는 순간 반짝
                    }
                    var hop = Mathf.Sin(Mathf.Min(1f, after / 0.25f) * Mathf.PI) * GridConstants.CellSize * 0.18f;
                    renderers[0].sprite = frame;
                    renderers[0].transform.position = new Vector3(target.x, target.y + hop, -0.6f);
                    renderers[0].color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Max(0f, after - 0.2f) / 0.15f));
                }
                lt += Time.deltaTime;
                yield return null;
            }
            foreach (var r in renderers)
                if (r != null)
                    Destroy(r.gameObject);
            done();
        }
    }
}
