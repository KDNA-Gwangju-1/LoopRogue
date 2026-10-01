using UnityEngine;

namespace LoopRogue
{
    public enum Sfx
    {
        Hit,
        Crit,
        Kill,
        BossDown,
        Hurt,
        Slam,
        Warning,
        Dash,
        Spin,
    }

    /// <summary>효과음 - 음원 파일 없이 사인파/사각파/노이즈를 코드로 합성해 AudioClip을 만든다(처음 쓸 때 한 번).
    /// 씬마다 자기 오브젝트를 새로 만들고, Main 씬 카메라엔 AudioListener가 없어서 없으면 붙인다.
    /// 같은 소리가 연달아 나도 덜 단조롭게 재생할 때마다 피치를 조금씩 흔든다.</summary>
    public class SfxPlayer : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const int VoiceCount = 6;
        public const float MasterVolume = 0.45f;

        private static SfxPlayer _instance;
        private static AudioClip[] _clips;

        private AudioSource[] _voices;
        private int _nextVoice;

        public static void Play(Sfx sfx)
        {
            if (DamagePopup.Suppressed)
                return;

            if (_instance == null)
                _instance = Create();

            var clip = GetClip(sfx);
            var voice = _instance._voices[_instance._nextVoice];
            _instance._nextVoice = (_instance._nextVoice + 1) % VoiceCount;
            voice.pitch = Random.Range(0.92f, 1.08f);
            voice.PlayOneShot(clip, MasterVolume);
        }

        private static SfxPlayer Create()
        {
            if (FindAnyObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main;
                if (cam != null)
                    cam.gameObject.AddComponent<AudioListener>();
            }

            var go = new GameObject("SfxPlayer");
            var player = go.AddComponent<SfxPlayer>();
            player._voices = new AudioSource[VoiceCount];
            for (var i = 0; i < VoiceCount; i++)
            {
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                player._voices[i] = source;
            }
            return player;
        }

        private static AudioClip GetClip(Sfx sfx)
        {
            _clips ??= new AudioClip[System.Enum.GetValues(typeof(Sfx)).Length];
            var index = (int)sfx;
            if (_clips[index] == null) // 런타임에 만든 클립은 씬 정리 때 언로드될 수 있어서 매번 확인
                _clips[index] = Synthesize(sfx);
            return _clips[index];
        }

        private static AudioClip Synthesize(Sfx sfx)
        {
            // 주파수가 변하는 소리라 위상을 누적하는 발진기를 쓴다(sin(f(t)·t)로 하면 내려가는 음이 중간에 뒤집힌다).
            var a = new Osc();
            var b = new Osc();
            return sfx switch
            {
                // 짧은 "퍽" - 낮은 사인 + 노이즈, 빨리 꺼짐
                Sfx.Hit => Build("sfx_hit", 0.07f, p => (a.Sine(170f * (1f - p * 0.4f)) * 0.7f + Noise() * 0.5f) * Decay(p, 6f)),
                // 치명타 - 더 높고 길게, 금속성 배음
                Sfx.Crit => Build("sfx_crit", 0.14f, p => (a.Sine(320f * (1f - p * 0.5f)) * 0.6f + b.Square(640f) * 0.15f + Noise() * 0.45f) * Decay(p, 4.5f)),
                // 처치 - 위에서 아래로 떨어지는 사각파
                Sfx.Kill => Build("sfx_kill", 0.16f, p => (a.Square(Mathf.Lerp(620f, 140f, p)) * 0.35f + Noise() * 0.25f * (1f - p)) * Decay(p, 3f)),
                // 보스 처치 - 길게 무너지는 저음 + 노이즈
                Sfx.BossDown => Build("sfx_bossdown", 0.6f, p => (a.Sine(Mathf.Lerp(140f, 40f, p)) * 0.8f + b.Square(Mathf.Lerp(300f, 60f, p)) * 0.2f + Noise() * 0.5f * (1f - p)) * Decay(p, 2.5f)),
                // 플레이어 피격 - 둔탁한 저음
                Sfx.Hurt => Build("sfx_hurt", 0.13f, p => (a.Sine(Mathf.Lerp(130f, 70f, p)) * 0.8f + Noise() * 0.35f) * Decay(p, 4f)),
                // 보스 예고 공격 발동 - 묵직한 쿵
                Sfx.Slam => Build("sfx_slam", 0.3f, p => (a.Sine(Mathf.Lerp(90f, 45f, p)) * 0.9f + Noise() * 0.6f * Decay(p, 10f)) * Decay(p, 3f)),
                // 예고 경고음 - 삑삑 두 번
                Sfx.Warning => Build("sfx_warning", 0.2f, p => a.Square(880f) * 0.18f * (p < 0.4f || (p > 0.55f && p < 0.95f) ? 1f : 0f)),
                // 대시 - 바람 가르는 "쉭"(노이즈가 점점 커졌다 빠짐)
                Sfx.Dash => Build("sfx_dash", 0.14f, p => Noise() * 0.5f * Mathf.Sin(p * Mathf.PI) + a.Sine(Mathf.Lerp(300f, 700f, p)) * 0.12f * (1f - p)),
                // 회전 베기 - 올라갔다 내려오는 휘두름
                Sfx.Spin => Build("sfx_spin", 0.2f, p => (Noise() * 0.4f + a.Sine(500f + 300f * Mathf.Sin(p * Mathf.PI)) * 0.25f) * Mathf.Sin(p * Mathf.PI)),
                _ => Build("sfx_none", 0.01f, p => 0f),
            };
        }

        /// <summary>wave(진행률 0~1) → 샘플, 샘플 순서대로 한 번씩 부른다. 시작/끝 2ms는 클릭 소리 방지로 살짝 페이드.</summary>
        private static AudioClip Build(string name, float seconds, System.Func<float, float> wave)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];
            var fade = Mathf.Min(count / 2, SampleRate / 500);
            for (var i = 0; i < count; i++)
            {
                var p = (float)i / count;
                var edge = Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / (float)Mathf.Max(1, fade));
                data[i] = Mathf.Clamp(wave(p) * edge, -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>샘플마다 한 번 불러 위상을 한 칸 전진시키는 발진기.</summary>
        private class Osc
        {
            private float _phase;

            private float Advance(float freq)
            {
                _phase += freq / SampleRate;
                _phase -= Mathf.Floor(_phase);
                return _phase;
            }

            public float Sine(float freq) => Mathf.Sin(2f * Mathf.PI * Advance(freq));
            public float Square(float freq) => Advance(freq) < 0.5f ? 1f : -1f;
        }

        private static float Noise() => Random.Range(-1f, 1f);
        private static float Decay(float p, float rate) => Mathf.Exp(-rate * p);
    }
}
