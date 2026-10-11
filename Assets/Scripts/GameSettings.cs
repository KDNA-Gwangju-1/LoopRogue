using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>설정(옵션 메뉴) - 볼륨·화면·연출. PlayerPrefs에 저장하지만 게임 저장과는 별개라 데이터 초기화·엔딩 후 처음으로·봇이
    /// 저장을 지워도 남는다(SaveReset이 Preserve로 감싼다). 게임 시작 때 한 번 적용한다.</summary>
    public static class GameSettings
    {
        private const string Prefix = "LoopRogue_Opt_";
        private const string MasterKey = Prefix + "Master";
        private const string SfxKey = Prefix + "Sfx";
        private const string BgmKey = Prefix + "Bgm";
        private const string ScreenModeKey = Prefix + "ScreenMode";
        private const string ResWidthKey = Prefix + "ResWidth";
        private const string ResHeightKey = Prefix + "ResHeight";
        private const string VSyncKey = Prefix + "VSync";
        private const string ShakeKey = Prefix + "Shake";
        private const string DamageNumbersKey = Prefix + "DamageNumbers";

        private static readonly string[] IntKeys =
            { MasterKey, SfxKey, BgmKey, ScreenModeKey, ResWidthKey, ResHeightKey, VSyncKey, ShakeKey, DamageNumbersKey, Loc.PrefKey };

        public const int VolumeStep = 10;

        /// <summary>볼륨은 0~100(10 단위).</summary>
        public static int Master { get; private set; } = 100;
        public static int Sfx { get; private set; } = 100;
        public static int Bgm { get; private set; } = 70;
        public static ScreenMode Mode { get; private set; } = ScreenMode.Fullscreen;
        public static Vector2Int Resolution { get; private set; }
        public static bool VSync { get; private set; } = true;
        public static bool ScreenShake { get; private set; } = true;
        public static bool DamageNumbers { get; private set; } = true;

        public enum ScreenMode { Fullscreen, Borderless, Windowed }

        /// <summary>효과음 실제 볼륨 배율(전체 × 효과음) - 전체 볼륨은 AudioListener에도 걸지만 배경음악이 따로 생기면 여기서 나눈다.</summary>
        public static float SfxVolume => Sfx / 100f;
        public static float BgmVolume => Bgm / 100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAndApply()
        {
            Load();
            ApplyAudio();
#if !UNITY_EDITOR
            if (!Application.isMobilePlatform)
                ApplyScreen(); // 에디터 Game 창에선 해상도/전체 화면이 안 먹는다, 폰은 항상 전체 화면
#endif
            ApplyVSync();
        }

        private static void Load()
        {
            Master = Mathf.Clamp(PlayerPrefs.GetInt(MasterKey, 100), 0, 100);
            Sfx = Mathf.Clamp(PlayerPrefs.GetInt(SfxKey, 100), 0, 100);
            Bgm = Mathf.Clamp(PlayerPrefs.GetInt(BgmKey, 70), 0, 100);
            Mode = (ScreenMode)Mathf.Clamp(PlayerPrefs.GetInt(ScreenModeKey, (int)FromUnity(Screen.fullScreenMode)), 0, 2);
            var w = PlayerPrefs.GetInt(ResWidthKey, 0);
            var h = PlayerPrefs.GetInt(ResHeightKey, 0);
            Resolution = w > 0 && h > 0 ? new Vector2Int(w, h) : new Vector2Int(Screen.width, Screen.height);
            VSync = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
            ScreenShake = PlayerPrefs.GetInt(ShakeKey, 1) == 1;
            DamageNumbers = PlayerPrefs.GetInt(DamageNumbersKey, 1) == 1;
        }

        private static void Save()
        {
            PlayerPrefs.SetInt(MasterKey, Master);
            PlayerPrefs.SetInt(SfxKey, Sfx);
            PlayerPrefs.SetInt(BgmKey, Bgm);
            PlayerPrefs.SetInt(ScreenModeKey, (int)Mode);
            PlayerPrefs.SetInt(ResWidthKey, Resolution.x);
            PlayerPrefs.SetInt(ResHeightKey, Resolution.y);
            PlayerPrefs.SetInt(VSyncKey, VSync ? 1 : 0);
            PlayerPrefs.SetInt(ShakeKey, ScreenShake ? 1 : 0);
            PlayerPrefs.SetInt(DamageNumbersKey, DamageNumbers ? 1 : 0);
            PlayerPrefs.Save();
        }

        // ---- 바꾸기(옵션 메뉴) - 바꾸는 즉시 적용하고 저장 ----
        public static void SetMaster(int v) { Master = Mathf.Clamp(v, 0, 100); ApplyAudio(); Save(); }
        public static void SetSfx(int v) { Sfx = Mathf.Clamp(v, 0, 100); Save(); }
        public static void SetBgm(int v) { Bgm = Mathf.Clamp(v, 0, 100); Save(); }
        public static void SetMode(ScreenMode m) { Mode = m; ApplyScreen(); Save(); }
        public static void SetResolution(Vector2Int r) { Resolution = r; ApplyScreen(); Save(); }
        public static void SetVSync(bool on) { VSync = on; ApplyVSync(); Save(); }
        public static void SetScreenShake(bool on) { ScreenShake = on; Save(); }
        public static void SetDamageNumbers(bool on) { DamageNumbers = on; Save(); }

        public static void ResetToDefaults()
        {
            Master = 100;
            Sfx = 100;
            Bgm = 70;
            ScreenShake = true;
            DamageNumbers = true;
            VSync = true;
            ApplyAudio();
            ApplyVSync();
            Save(); // 화면 모드·해상도는 그대로(기본값으로 바꾸면 갑자기 화면이 바뀌어서)
        }

        /// <summary>고를 수 있는 해상도(모니터가 지원하는 것, 가로세로 중복 없이, 작은 것부터).</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height))
                .Where(r => r.x >= 1024 && r.y >= 576).Distinct().OrderBy(r => r.x).ThenBy(r => r.y).ToList();
            if (!list.Contains(Resolution))
                list.Add(Resolution);
            return list.OrderBy(r => r.x).ThenBy(r => r.y).ToList();
        }

        private static void ApplyAudio() => AudioListener.volume = Master / 100f;

        private static void ApplyVSync()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : 120;
        }

        private static void ApplyScreen()
        {
            var mode = Mode switch
            {
                ScreenMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
                ScreenMode.Borderless => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed,
            };
            if (Resolution.x > 0 && Resolution.y > 0)
                Screen.SetResolution(Resolution.x, Resolution.y, mode);
            else
                Screen.fullScreenMode = mode;
        }

        private static ScreenMode FromUnity(FullScreenMode mode) => mode switch
        {
            FullScreenMode.ExclusiveFullScreen => ScreenMode.Fullscreen,
            FullScreenMode.FullScreenWindow => ScreenMode.Borderless,
            _ => ScreenMode.Windowed,
        };

        /// <summary>저장을 통째로 지우는 일(데이터 초기화 등) 앞뒤로 감싸서 설정만 살려둔다.</summary>
        public static void Preserve(Action wipe)
        {
            var saved = IntKeys.Where(PlayerPrefs.HasKey).ToDictionary(k => k, PlayerPrefs.GetInt);
            wipe();
            foreach (var pair in saved)
                PlayerPrefs.SetInt(pair.Key, pair.Value);
            PlayerPrefs.Save();
        }
    }
}
