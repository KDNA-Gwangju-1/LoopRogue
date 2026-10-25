using System.Collections;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>사망 리플레이용 녹화(10-25 사용자 "뭐에 맞아 죽었는지 리플레이로 보여 주고 시간이 감기며 돌아가게") - 다 그린 화면을
    /// 실제 시간 FrameInterval마다 작은 RenderTexture로 복사해, 최근 Capacity장만 고리처럼 돌려 쓴다.
    /// 죽으면 GameHUD가 녹화를 멈추고 이 장면들을 앞으로(느리게) 틀었다가 거꾸로(빠르게) 감는다. 봇 중엔 녹화하지 않는다.
    /// 10-25 렉 점검: 처음엔 카메라를 한 번 더 그려서(cam.Render) 초당 30번 장면 전체를 두 번 그렸다 → 카메라 CommandBuffer 복사는
    /// 이 환경에서 뒤집히고 캐릭터가 빠졌다(사용자) → 지금은 프레임 끝(WaitForEndOfFrame)에 완성된 화면을 GPU에서 통째로 복사
    /// (ScreenCapture.CaptureScreenshotIntoRenderTexture)해 줄이기만 한다 - 그리기 비용 없음. 그 복사는 D3D·Metal·Vulkan
    /// (graphicsUVStartsAtTop)에서 위아래가 뒤집혀 나와서 줄일 때 바로 세운다. 복사본은 화면 전체라 재생할 땐 ViewportRect만큼 잘라 쓴다.</summary>
    public class DeathReplay : MonoBehaviour
    {
        public const int Capacity = 66;            // 30장/초 × 2.2초
        private const float FrameInterval = 1f / 30f;
        private const int MaxWidth = 640;          // 복사본 가로 최대(4K에서도 66장 ≈ 60MB)

        private readonly RenderTexture[] _frames = new RenderTexture[Capacity];
        private RenderTexture _full;               // 화면 크기 그대로 받는 한 장(다시 씀)
        private int _head, _count, _width, _height;
        private float _accum;
        private readonly WaitForEndOfFrame _endOfFrame = new WaitForEndOfFrame();

        /// <summary>false면 녹화를 멈춘다(리플레이 재생 중).</summary>
        public bool Recording { get; set; } = true;
        public int Count => _count;
        /// <summary>녹화할 때 카메라가 화면에서 차지한 영역(0~1) - 재생할 때 복사본에서 이 부분만 잘라 같은 자리에.</summary>
        public Rect ViewportRect { get; private set; } = new Rect(0f, 0f, 1f, 1f);

        /// <summary>i = 0(가장 오래된 장) ~ Count-1(가장 최근 장).</summary>
        public Texture Frame(int i) => _frames[(_head - _count + i + Capacity * 2) % Capacity];

        public void Clear()
        {
            _count = 0;
            _accum = 0f;
        }

        private void OnEnable() => StartCoroutine(CaptureLoop());

        private IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return _endOfFrame;
                if (!Recording || GameHUD.AutoPlayActive || DamagePopup.Suppressed)
                    continue;
                _accum += Time.unscaledDeltaTime;
                if (_accum < FrameInterval)
                    continue;
                _accum = Mathf.Min(_accum - FrameInterval, FrameInterval);
                var cam = Camera.main;
                if (cam == null || Screen.width < 16 || Screen.height < 16)
                    continue;
                EnsureTextures();
                ViewportRect = cam.rect;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
                if (SystemInfo.graphicsUVStartsAtTop)
                    Graphics.Blit(_full, _frames[_head], new Vector2(1f, -1f), new Vector2(0f, 1f)); // 뒤집혀 온 것을 바로 세운다
                else
                    Graphics.Blit(_full, _frames[_head]);
                _head = (_head + 1) % Capacity;
                _count = Mathf.Min(_count + 1, Capacity);
            }
        }

        /// <summary>화면 크기 한 장 + 줄인 복사본 Capacity장 - 처음(또는 화면 크기가 바뀌었을 때) 한꺼번에.</summary>
        private void EnsureTextures()
        {
            var w = Mathf.Min(MaxWidth, Mathf.Max(16, Screen.width / 3));
            var h = Mathf.Max(16, Mathf.RoundToInt(w * (float)Screen.height / Screen.width));
            if (_full != null && _full.width == Screen.width && _full.height == Screen.height && w == _width && h == _height)
                return;
            Release();
            _full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { name = "DeathReplayFull" };
            _full.Create();
            _width = w;
            _height = h;
            for (var i = 0; i < Capacity; i++)
            {
                _frames[i] = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "DeathReplayFrame" };
                _frames[i].Create();
            }
        }

        private void Release()
        {
            if (_full != null)
            {
                _full.Release();
                Destroy(_full);
                _full = null;
            }
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
