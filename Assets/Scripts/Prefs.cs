using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>세이브 슬롯(11-02 사용자 "타이틀에서 3칸 선택") - 게임 코드는 PlayerPrefs 대신 이걸 쓴다. "LoopRogue_X" 키를 지금 슬롯의 키로 바꿔 끼운다:
    /// 1번 슬롯은 예전 키 그대로(슬롯이 생기기 전 저장이 1번 슬롯), 2·3번은 "LoopRogue_S2_X"·"LoopRogue_S3_X". 설정(LoopRogue_Opt_*)·빌드 표시·
    /// 슬롯 정보 키는 슬롯과 상관없이 공용. PlayerPrefs는 키 목록을 못 꺼내서, 슬롯마다 쓴 키를 목록(LoopRogue_SaveSlotKeys_N)으로 남겨
    /// 그 슬롯만 지울 때(DeleteAll) 쓴다. 에디터 테스트 메뉴(Assets/Editor)는 예전처럼 PlayerPrefs를 바로 쓴다 - 1번 슬롯 기준.</summary>
    public static class Prefs
    {
        public const int SlotCount = 3;
        private const string Root = "LoopRogue_";
        private const string CurrentSlotKey = "LoopRogue_SaveSlot";
        private const string IndexKeyPrefix = "LoopRogue_SaveSlotKeys_";
        private const string BuildStampKey = "LoopRogue_BuildStamp";

        private static int _slot;

        /// <summary>지금 슬롯(1~SlotCount).</summary>
        public static int Slot
        {
            get
            {
                if (_slot == 0)
                    _slot = Mathf.Clamp(PlayerPrefs.GetInt(CurrentSlotKey, 1), 1, SlotCount);
                return _slot;
            }
        }

        /// <summary>슬롯을 바꾸고 저장 클래스들이 그 슬롯을 다시 읽게 한다(타이틀 슬롯 선택).</summary>
        public static void SelectSlot(int slot)
        {
            Playtest.FlushPlayTime(); // 지금 슬롯에 쌓인 플레이 시간을 먼저 적어 둔다
            _slot = Mathf.Clamp(slot, 1, SlotCount);
            PlayerPrefs.SetInt(CurrentSlotKey, _slot);
            PlayerPrefs.Save();
            SaveReset.ReloadAll();
        }

        private static bool IsGlobal(string key) =>
            !key.StartsWith(Root) || key.StartsWith("LoopRogue_Opt_") || key == BuildStampKey || key.StartsWith(CurrentSlotKey);

        /// <summary>slot 슬롯에서 key가 실제로 저장되는 키.</summary>
        public static string Map(string key, int slot) =>
            IsGlobal(key) || slot == 1 ? key : $"{Root}S{slot}_{key.Substring(Root.Length)}";

        private static string Map(string key) => Map(key, Slot);

        // ---- 슬롯마다 쓴 키 목록(키 → 'i'/'f'/'s') ----
        private static readonly Dictionary<int, Dictionary<string, char>> Index = new Dictionary<int, Dictionary<string, char>>();

        private static Dictionary<string, char> IndexOf(int slot)
        {
            if (Index.TryGetValue(slot, out var index))
                return index;
            index = new Dictionary<string, char>();
            foreach (var entry in PlayerPrefs.GetString(IndexKeyPrefix + slot, "").Split('\n'))
                if (entry.Length > 2 && entry[1] == ':')
                    index[entry.Substring(2)] = entry[0];
            Index[slot] = index;
            return index;
        }

        private static void Track(string key, char type)
        {
            if (IsGlobal(key))
                return;
            var index = IndexOf(Slot);
            if (index.TryGetValue(key, out var old) && old == type)
                return;
            index[key] = type;
            SaveIndex(Slot);
        }

        private static void SaveIndex(int slot) =>
            PlayerPrefs.SetString(IndexKeyPrefix + slot, string.Join("\n", IndexOf(slot).Select(p => $"{p.Value}:{p.Key}")));

        // ---- PlayerPrefs와 같은 모양 ----
        public static int GetInt(string key) => PlayerPrefs.GetInt(Map(key));
        public static int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(Map(key), defaultValue);
        public static float GetFloat(string key) => PlayerPrefs.GetFloat(Map(key));
        public static float GetFloat(string key, float defaultValue) => PlayerPrefs.GetFloat(Map(key), defaultValue);
        public static string GetString(string key) => PlayerPrefs.GetString(Map(key));
        public static string GetString(string key, string defaultValue) => PlayerPrefs.GetString(Map(key), defaultValue);
        public static bool HasKey(string key) => PlayerPrefs.HasKey(Map(key));
        public static void Save() => PlayerPrefs.Save();

        public static void SetInt(string key, int value)
        {
            Track(key, 'i');
            PlayerPrefs.SetInt(Map(key), value);
        }

        public static void SetFloat(string key, float value)
        {
            Track(key, 'f');
            PlayerPrefs.SetFloat(Map(key), value);
        }

        public static void SetString(string key, string value)
        {
            Track(key, 's');
            PlayerPrefs.SetString(Map(key), value);
        }

        public static void DeleteKey(string key)
        {
            PlayerPrefs.DeleteKey(Map(key));
            if (!IsGlobal(key) && IndexOf(Slot).Remove(key))
                SaveIndex(Slot);
        }

        /// <summary>지금 슬롯만 지운다(설정은 GameSettings.Preserve로 감싸서 부른다). 1번 슬롯은 예전 키(목록에 없는 것도 있다)라 PlayerPrefs를 통째로 지우고
        /// 다른 슬롯·공용 키만 다시 써 넣는다.</summary>
        public static void DeleteAll()
        {
            var slot = Slot;
            if (slot != 1)
            {
                foreach (var key in IndexOf(slot).Keys)
                    PlayerPrefs.DeleteKey(Map(key, slot));
                IndexOf(slot).Clear();
                PlayerPrefs.DeleteKey(IndexKeyPrefix + slot);
                return;
            }

            var keep = new List<(string Key, char Type, object Value)>();
            void KeepRaw(string key, char type)
            {
                if (!PlayerPrefs.HasKey(key))
                    return;
                keep.Add((key, type, type == 'i' ? PlayerPrefs.GetInt(key) : type == 'f' ? (object)PlayerPrefs.GetFloat(key) : PlayerPrefs.GetString(key)));
            }
            KeepRaw(CurrentSlotKey, 'i');
            KeepRaw(BuildStampKey, 's');
            for (var s = 2; s <= SlotCount; s++)
            {
                KeepRaw(IndexKeyPrefix + s, 's');
                foreach (var pair in IndexOf(s))
                    KeepRaw(Map(pair.Key, s), pair.Value);
            }
            PlayerPrefs.DeleteAll();
            foreach (var (key, type, value) in keep)
            {
                if (type == 'i')
                    PlayerPrefs.SetInt(key, (int)value);
                else if (type == 'f')
                    PlayerPrefs.SetFloat(key, (float)value);
                else
                    PlayerPrefs.SetString(key, (string)value);
            }
            IndexOf(1).Clear();
        }

        /// <summary>새 빌드 - 모든 슬롯을 지운다(설정·빌드 표시는 호출부가 살린다).</summary>
        public static void DeleteEverySlot()
        {
            PlayerPrefs.DeleteAll();
            Index.Clear();
            _slot = 1;
        }

        // ---- 타이틀 슬롯 카드 - 다른 슬롯의 값을 슬롯을 바꾸지 않고 읽는다 ----
        public static int PeekInt(int slot, string key, int defaultValue) => PlayerPrefs.GetInt(Map(key, slot), defaultValue);
        public static float PeekFloat(int slot, string key, float defaultValue) => PlayerPrefs.GetFloat(Map(key, slot), defaultValue);
        public static bool PeekHasKey(int slot, string key) => PlayerPrefs.HasKey(Map(key, slot));
    }
}
