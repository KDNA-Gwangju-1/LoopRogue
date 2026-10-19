using UnityEngine;

namespace LoopRogue
{
    /// <summary>배경음악(10-19 사용자 준비 요청) - 장소마다 Resources/Music/{곡 이름}을 반복 재생하고, 곡이 바뀌면 FadeSeconds 동안 겹쳐 넘긴다.
    /// 곡 파일이 없으면 조용히(그 장소는 음악 없음) - 파일만 넣으면 코드 수정 없이 나온다. 이름 목록은 Resources/Music/README.txt.
    /// 볼륨 = 설정 "배경음악" × BaseVolume(전체 볼륨은 AudioListener가 따로 건다). 씬이 바뀌어도 살아 있어서 같은 곡이면 끊기지 않는다.
    /// 자동 플레이 봇 실행 중엔 아무것도 안 튼다.</summary>
    public class MusicPlayer : MonoBehaviour
    {
        // 곡 이름(= Resources/Music 안 파일 이름, 확장자 빼고)
        public const string Lobby = "Lobby";     // 로비(걸어 다니는 허브·연금술사)
        public const string Casino = "Casino";   // 도박장(슬롯)
        public const string Gacha = "Gacha";     // 뽑기 상점
        public const string Dungeon = "Dungeon"; // 일반 방·타이틀(같은 곡)
        public const string Puzzle = "Puzzle";   // 퍼즐 방(퍼즐 5종)
        public const string MiniGame = "MiniGame"; // 미니게임 방(미니게임 5종)
        public const string Mural = "Mural";     // 횃불로 연 잊힌 벽화의 방
        public static string Boss(int stage) => $"Boss{Mathf.Clamp(stage, 1, 10)}"; // 층별 보스방

        public const float FadeSeconds = 1.2f;
        private const float BaseVolume = 0.6f; // 효과음(SfxPlayer.MasterVolume 0.45)과 어울리게

        private static MusicPlayer _instance;
        private AudioSource _a, _b;          // 지금 곡 / 사라지는 곡(겹쳐 넘기기)
        private string _current;
        private float _fade = 1f;            // 0 → 1: 새 곡이 커지고 옛 곡이 작아지는 진행

        /// <summary>곡을 튼다 - 같은 곡이면 그대로, 없는 곡이면(파일 없음) 지금 곡을 서서히 끈다.</summary>
        public static void Play(string track)
        {
            if (GameHUD.AutoPlayActive)
                return;
            Ensure();
            _instance.Switch(track);
        }

        /// <summary>음악을 서서히 끈다(일반 방·퍼즐·보스를 잡은 뒤 등).</summary>
        public static void Stop()
        {
            if (_instance == null)
                return;
            _instance.Switch(null);
        }

        private static void Ensure()
        {
            if (_instance != null)
                return;
            var go = new GameObject("MusicPlayer");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MusicPlayer>();
            _instance._a = _instance.NewSource();
            _instance._b = _instance.NewSource();
        }

        private AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            return s;
        }

        private void Switch(string track)
        {
            var clip = track != null ? Resources.Load<AudioClip>($"Music/{track}") : null;
            var key = clip != null ? track : null;
            if (key == _current)
                return;
            _current = key;
            // 지금 곡을 "사라지는 쪽"으로 보내고, 새 곡을 빈 쪽에서 0부터 키운다
            (_a, _b) = (_b, _a);
            _a.Stop();
            _a.clip = clip;
            _a.volume = 0f;
            if (clip != null)
                _a.Play();
            _fade = 0f;
        }

        private void Update()
        {
            var target = GameSettings.BgmVolume * BaseVolume;
            if (_fade < 1f)
                _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / FadeSeconds);
            _a.volume = _a.clip != null ? target * _fade : 0f;
            _b.volume = target * (1f - _fade);
            if (_fade >= 1f && _b.isPlaying)
                _b.Stop();
        }
    }
}
