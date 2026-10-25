using UnityEngine;

namespace LoopRogue
{
    /// <summary>사망 리플레이용 녹화(10-25 사용자 "뭐에 맞아 죽었는지 리플레이로 보여 주고 시간이 감기며 돌아가게") - 메인 카메라가 그리는
    /// 게임 화면(HUD 빼고)을 실제 시간 FrameInterval마다 작은 RenderTexture에 한 장씩, 최근 Capacity장만 고리처럼 돌려 쓴다.
    /// 죽으면 GameHUD가 녹화를 멈추고 이 장면들을 앞으로(느리게) 틀었다가 거꾸로(빠르게) 감는다. 봇 중엔 녹화하지 않는다.</summary>
    public class DeathReplay : MonoBehaviour
    {
        public const int Capacity = 66;            // 30장/초 × 2.2초
        private const float FrameInterval = 1f / 30f;
        private const int Downscale = 3;           // 화면 1/3 해상도(66장 ≈ 45MB, 1080p 기준)

        private readonly RenderTexture[] _frames = new RenderTexture[Capacity];
        private int _head, _count, _width, _height;
        private float _accum;

        /// <summary>false면 녹화를 멈춘다(리플레이 재생 중).</summary>
        public bool Recording { get; set; } = true;
        public int Count => _count;
        /// <summary>녹화할 때 카메라가 화면에서 차지한 영역(0~1) - 재생 그림도 같은 자리에.</summary>
        public Rect ViewportRect { get; private set; } = new Rect(0f, 0f, 1f, 1f);

        /// <summary>i = 0(가장 오래된 장) ~ Count-1(가장 최근 장).</summary>
        public Texture Frame(int i) => _frames[(_head - _count + i + Capacity * 2) % Capacity];

        public void Clear()
        {
            _count = 0;
            _accum = 0f;
        }

        private void LateUpdate()
        {
            if (!Recording || GameHUD.AutoPlayActive || DamagePopup.Suppressed)
                return;
            _accum += Time.unscaledDeltaTime;
            if (_accum < FrameInterval)
                return;
            _accum = Mathf.Min(_accum - FrameInterval, FrameInterval);
            var cam = Camera.main;
            if (cam == null)
                return;
            var pixel = cam.pixelRect;
            var w = Mathf.Max(16, Mathf.RoundToInt(pixel.width) / Downscale);
            var h = Mathf.Max(16, Mathf.RoundToInt(pixel.height) / Downscale);
            if (w != _width || h != _height)
            {
                Release();
                _width = w;
                _height = h;
            }
            var rt = _frames[_head];
            if (rt == null)
            {
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "DeathReplayFrame" };
                _frames[_head] = rt;
            }
            // HUD 띠를 뺀 영역만 그리던 카메라를 잠깐 그림 전체로 - 그림 비율이 원래 영역과 같아서 보이는 범위도 같다
            var rect = cam.rect;
            var target = cam.targetTexture;
            ViewportRect = rect;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = target;
            cam.rect = rect;
            _head = (_head + 1) % Capacity;
            _count = Mathf.Min(_count + 1, Capacity);
        }

        private void Release()
        {
            for (var i = 0; i < Capacity; i++)
            {
                if (_frames[i] != null)
                {
                    _frames[i].Release();
                    Destroy(_frames[i]);
                    _frames[i] = null;
                }
            }
            _count = 0;
            _head = 0;
        }

        private void OnDestroy() => Release();
    }
}
