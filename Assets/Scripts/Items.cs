using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>1회용 아이템 10종 - 종류마다 최대 1개(사용자 결정). 순서 = 인벤토리 칸 순서.</summary>
    public enum ItemType
    {
        Torch,      // 횃불
        Bomb,       // 폭탄
        Decoy,      // 미끼
        Smoke,      // 연막탄
        Cleanse,    // 정화제
        Flash,      // 섬광탄
        Trap,       // 덫
        Reflect,    // 반사 부적
        Weakness,   // 약점 표식
        Barrier,    // 보호막
    }

    public static class ItemInfo
    {
        public const int Count = 10;

        // 수치 - 처음 넣는 값이라 플레이/봇으로 맞춘다.
        public const float TorchRadius = 4f;
        public const int BombRange = 3;
        public const float BombDamageRate = 3f;     // 플레이어 공격력 ×
        public const int DecoyTurns = 3;
        public const int SmokeRadius = 3;           // 체비쇼프(정사각형) 반경
        public const int SmokeStunTurns = 2;
        public const int CleanseImmuneTurns = 3;
        public const int FlashBossStunTurns = 2;
        public const int FlashRadius = 2;
        public const int FlashEnemyStunTurns = 1;
        public const int TrapStunTurns = 3;
        public const int ReflectTurns = 3;
        public const float ReflectBossDamageRate = 0.15f; // 보스 최대 체력 ×
        public const int WeaknessRadius = 3;
        public const int WeaknessTurns = 5;
        public const float WeaknessDamageMultiplier = 1.5f;
        public const float MobDropChance = 0.03f;

        public static string Name(ItemType type) => type switch
        {
            ItemType.Torch => "횃불",
            ItemType.Bomb => "폭탄",
            ItemType.Decoy => "미끼",
            ItemType.Smoke => "연막탄",
            ItemType.Cleanse => "정화제",
            ItemType.Flash => "섬광탄",
            ItemType.Trap => "덫",
            ItemType.Reflect => "반사 부적",
            ItemType.Weakness => "약점 표식",
            _ => "보호막",
        };

        public static string Description(ItemType type) => type switch
        {
            ItemType.Torch => "옆 칸에 설치 - 그 주변이 계속 밝게 보인다",
            ItemType.Bomb => $"던지면 최대 {BombRange}칸 앞에 떨어져 다음 턴 3x3 폭발(공격력 {BombDamageRate:0}배, 벽도 부숨, 방패 무시)",
            ItemType.Decoy => $"옆 칸에 허수아비 - {DecoyTurns}턴 동안 몹들이 허수아비를 노린다",
            ItemType.Smoke => $"주변 {SmokeRadius}칸 몹이 {SmokeStunTurns}턴 동안 플레이어를 못 찾는다(행동 못 함)",
            ItemType.Cleanse => $"거미줄 즉시 해제 + {CleanseImmuneTurns}턴 동안 거미줄 무시",
            ItemType.Flash => $"보스의 예고 공격 취소 + {FlashBossStunTurns}턴 기절, 주변 몹 {FlashEnemyStunTurns}턴 기절",
            ItemType.Trap => $"옆 칸에 설치 - 밟은 몹(보스 포함) {TrapStunTurns}턴 기절",
            ItemType.Reflect => $"{ReflectTurns}턴 안에 맞는 보스 예고 공격 1회를 되돌린다(보스 최대 체력 {ReflectBossDamageRate * 100f:0}%)",
            ItemType.Weakness => $"주변 {WeaknessRadius}칸 몹과 보스가 {WeaknessTurns}턴 동안 받는 피해 +{(WeaknessDamageMultiplier - 1f) * 100f:0}%",
            _ => "다음에 받는 피해 1회 무효",
        };

        /// <summary>방향을 골라야 하는 아이템(대시처럼 키 → 방향키).</summary>
        public static bool NeedsDirection(ItemType type) =>
            type == ItemType.Torch || type == ItemType.Bomb || type == ItemType.Decoy || type == ItemType.Trap;
    }

    /// <summary>인벤토리 - 종류마다 있음/없음(최대 1개) + 퀵슬롯 3칸(아이템 종류를 등록, 1/2/3 키). 사망·중도 포기 시 아이템은
    /// 전부 잃고 퀵슬롯 등록은 남는다(빈 칸으로 보임). PlayerPrefs에 저장해서 로비를 다녀와도 유지.</summary>
    public static class Inventory
    {
        public const int QuickSlotCount = 3;

        private const string OwnedKey = "LoopRogue_Items_Owned";
        private const string QuickKey = "LoopRogue_Items_Quick";

        private static readonly System.Random Rng = new System.Random();
        private static bool _loaded;
        private static readonly bool[] Owned = new bool[ItemInfo.Count];
        private static readonly int[] Quick = { -1, -1, -1 };

        /// <summary>자동 플레이 봇 통계용 - 얻은 횟수/쓴 횟수(종류별).</summary>
        public static readonly int[] ObtainedCount = new int[ItemInfo.Count];
        public static readonly int[] UsedCount = new int[ItemInfo.Count];

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;

            var owned = PlayerPrefs.GetString(OwnedKey, "");
            for (var i = 0; i < ItemInfo.Count; i++)
                Owned[i] = i < owned.Length && owned[i] == '1';

            var quick = PlayerPrefs.GetString(QuickKey, "-1,-1,-1").Split(',');
            for (var i = 0; i < QuickSlotCount; i++)
                Quick[i] = i < quick.Length && int.TryParse(quick[i], out var v) && v >= 0 && v < ItemInfo.Count ? v : -1;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(OwnedKey, new string(Owned.Select(o => o ? '1' : '0').ToArray()));
            PlayerPrefs.SetString(QuickKey, string.Join(",", Quick));
        }

        public static bool Has(ItemType type)
        {
            EnsureLoaded();
            return Owned[(int)type];
        }

        /// <summary>없는 아이템 중 하나를 무작위로 준다. 다 있으면 null.</summary>
        public static ItemType? GiveRandomMissing()
        {
            EnsureLoaded();
            var missing = Enumerable.Range(0, ItemInfo.Count).Where(i => !Owned[i]).ToList();
            if (missing.Count == 0)
                return null;
            var type = (ItemType)missing[Rng.Next(missing.Count)];
            Give(type);
            return type;
        }

        /// <summary>얻으면 비어 있는 퀵슬롯이 있고 아직 어디에도 등록 안 된 종류면 자동 등록.</summary>
        public static void Give(ItemType type)
        {
            EnsureLoaded();
            Owned[(int)type] = true;
            ObtainedCount[(int)type]++;
            if (!Quick.Contains((int)type))
            {
                for (var i = 0; i < QuickSlotCount; i++)
                {
                    if (Quick[i] < 0 || !Owned[Quick[i]])
                    {
                        Quick[i] = (int)type;
                        break;
                    }
                }
            }
            Save();
        }

        public static void Consume(ItemType type)
        {
            EnsureLoaded();
            Owned[(int)type] = false;
            UsedCount[(int)type]++;
            Save();
        }

        public static void LoseAll()
        {
            EnsureLoaded();
            for (var i = 0; i < ItemInfo.Count; i++)
                Owned[i] = false;
            Save();
        }

        /// <summary>퀵슬롯에 등록돼 있는지 - 아이템은 퀵슬롯에 등록된 것만 쓸 수 있다(사용자 결정).</summary>
        public static bool IsInQuickSlot(ItemType type)
        {
            EnsureLoaded();
            return Quick.Contains((int)type);
        }

        public static ItemType? QuickSlot(int slot)
        {
            EnsureLoaded();
            return Quick[slot] >= 0 ? (ItemType)Quick[slot] : (ItemType?)null;
        }

        /// <summary>퀵슬롯 등록 - 같은 종류가 다른 칸에 있으면 그 칸은 비운다(한 종류는 한 칸에만).</summary>
        public static void SetQuickSlot(int slot, ItemType type)
        {
            EnsureLoaded();
            for (var i = 0; i < QuickSlotCount; i++)
                if (Quick[i] == (int)type)
                    Quick[i] = -1;
            Quick[slot] = (int)type;
            Save();
        }

        public static IEnumerable<ItemType> OwnedItems()
        {
            EnsureLoaded();
            for (var i = 0; i < ItemInfo.Count; i++)
                if (Owned[i])
                    yield return (ItemType)i;
        }
    }
}
