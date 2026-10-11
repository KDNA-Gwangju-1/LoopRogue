using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    public enum GameLanguage { Korean, English, Japanese, ChineseSimplified }

    /// <summary>다국어 - 코드에 쓴 한국어 문장 자체가 열쇠다. Loc.T("한국어")는 지금 언어의 번역을, Loc.F("골드 +{0}", g)는 번역한 서식에
    /// 값을 끼운 문장을 돌려준다. 번역표는 Resources/Localization/{en|ja|zh}.txt(한 줄 = "한국어 TAB 번역", 줄바꿈은 \n으로 적음).
    /// 표에 없는 문장은 한국어 그대로. 처음 실행하면 컴퓨터 언어를 따르고, 바꾼 언어는 설정과 같이 저장된다(GameSettings.Preserve).</summary>
    public static class Loc
    {
        public const string PrefKey = "LoopRogue_Opt_Language";

        public static GameLanguage Current { get; private set; } = GameLanguage.Korean;

        /// <summary>언어가 바뀔 때마다 1씩 오른다 - 번역한 문장을 들고 있는 정적 표(LocCache)가 다시 만들어지게.</summary>
        public static int Version { get; private set; }

        public static event Action Changed;

        /// <summary>자동 플레이 봇 중엔 항상 한국어 - 봇이 카드 이름 등 한국어 문장으로 판단하고 로그도 한국어로 남긴다.</summary>
        public static bool ForceKorean
        {
            get => _forceKorean;
            set
            {
                if (_forceKorean == value)
                    return;
                _forceKorean = value;
                Version++; // 이미 만든 번역 표(LocCache)를 다시 만들게
            }
        }

        private static bool _forceKorean;

        private static Dictionary<string, string> _table;

        public static string DisplayName(GameLanguage language) => language switch
        {
            GameLanguage.English => "English",
            GameLanguage.Japanese => "日本語",
            GameLanguage.ChineseSimplified => "简体中文",
            _ => "한국어",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Current = PlayerPrefs.HasKey(PrefKey)
                ? (GameLanguage)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey), 0, 3)
                : FromSystem(Application.systemLanguage);
            _table = null;
        }

        private static GameLanguage FromSystem(SystemLanguage language) => language switch
        {
            SystemLanguage.Korean => GameLanguage.Korean,
            SystemLanguage.Japanese => GameLanguage.Japanese,
            SystemLanguage.Chinese => GameLanguage.ChineseSimplified,
            SystemLanguage.ChineseSimplified => GameLanguage.ChineseSimplified,
            SystemLanguage.ChineseTraditional => GameLanguage.ChineseSimplified,
            _ => GameLanguage.English,
        };

        public static void SetLanguage(GameLanguage language)
        {
            PlayerPrefs.SetInt(PrefKey, (int)language);
            PlayerPrefs.Save();
            if (language == Current)
                return;
            Current = language;
            _table = null;
            Version++;
            Changed?.Invoke();
        }

        public static string T(string korean)
        {
            if (_forceKorean || Current == GameLanguage.Korean || string.IsNullOrEmpty(korean))
                return korean;
            EnsureTable();
            return _table.TryGetValue(korean, out var translated) && translated.Length > 0 ? translated : korean;
        }

        public static string F(string koreanFormat, params object[] args)
        {
            var format = T(koreanFormat);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return string.Format(koreanFormat, args); // 번역 서식이 깨졌으면 한국어로라도
            }
        }

        private static void EnsureTable()
        {
            if (_table != null)
                return;
            _table = new Dictionary<string, string>();
            var code = Current switch
            {
                GameLanguage.English => "en",
                GameLanguage.Japanese => "ja",
                _ => "zh",
            };
            var asset = Resources.Load<TextAsset>($"Localization/{code}");
            if (asset == null)
                return;
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var tab = line.IndexOf('\t');
                if (tab <= 0)
                    continue;
                _table[Unescape(line.Substring(0, tab))] = Unescape(line.Substring(tab + 1));
            }
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");
    }

    /// <summary>번역한 문장을 들고 있는 정적 표 - 언어가 바뀌면(Loc.Version) 다음에 읽을 때 다시 만든다.</summary>
    public sealed class LocCache<T>
    {
        private readonly Func<T> _build;
        private T _value;
        private int _version = -1;

        public LocCache(Func<T> build) => _build = build;

        public T Value
        {
            get
            {
                if (_version != Loc.Version)
                {
                    _value = _build();
                    _version = Loc.Version;
                }
                return _value;
            }
        }
    }
}
