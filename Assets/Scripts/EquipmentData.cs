using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>StoryRPG 장비 시스템(EquipmentItemDataSO/ItemGradeDataSO)을 이 프로토타입 크기에
    /// 맞게 확 줄인 버전 - 내구도/강화/재료/에디터 툴로 등급별 에셋을 미리 굽는 절차 전부 생략하고,
    /// "슬롯 3개 + 등급 9단계 + 등급별 배율"만 남겼다. 아이템도 ScriptableObject 에셋 대신 코드
    /// 테이블 하나로 정의한다(방 시퀀스를 GameBootstrap에 하드코딩한 것과 같은 이유).</summary>
    public enum ItemSlot
    {
        Weapon,
        Armor,
        Accessory,
    }

    /// <summary>StoryRPG의 ItemGrade와 같은 개념 - 등급이 높을수록 Multiplier가 커져서 같은 슬롯이라도
    /// 최종 스탯 보너스가 커진다. 9단계(일반~영원) - 정수값 순서가 곧 등급 높낮이다(EquipmentWallet이
    /// 이 정수로 비교/저장).</summary>
    public enum ItemGrade
    {
        Common,       // 일반
        Uncommon,     // 고급
        Rare,         // 희귀
        Epic,         // 영웅
        Legendary,    // 전설
        Mythic,       // 신화
        Ancient,      // 고대
        Transcendent, // 초월
        Eternal,      // 영원
        Unknown,      // Unknown - 뽑기로는 안 나온다. 히든 NPC 대장장이(Blacksmith)가 벼려 준다
    }

    public static class EquipmentData
    {
        /// <summary>슬롯별 밑바탕 이름 + 기본 스탯(등급 배율 적용 전) - StoryRPG로 치면 base
        /// ItemDataSO 한 장에 해당.</summary>
        public struct SlotTemplate
        {
            public string BaseName;
            public float BaseAttackBonus;
            public float BaseHealthBonus;
        }

        private static readonly LocCache<Dictionary<ItemSlot, SlotTemplate>> TemplatesCache = new LocCache<Dictionary<ItemSlot, SlotTemplate>>(() => new Dictionary<ItemSlot, SlotTemplate>
        {
            { ItemSlot.Weapon, new SlotTemplate { BaseName = Loc.T("검"), BaseAttackBonus = 2f, BaseHealthBonus = 0f } },
            { ItemSlot.Armor, new SlotTemplate { BaseName = Loc.T("방어구"), BaseAttackBonus = 0f, BaseHealthBonus = 10f } },
            { ItemSlot.Accessory, new SlotTemplate { BaseName = Loc.T("반지"), BaseAttackBonus = 1f, BaseHealthBonus = 5f } },
        });

        public static Dictionary<ItemSlot, SlotTemplate> Templates => TemplatesCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        /// <summary>StoryRPG ItemGradeDataSO의 이름/스탯보너스 역할을 하나로 합쳤다 - 여긴 등급마다
        /// "고정값을 더하는" 대신 "배율을 곱하는" 방식(단순 프로토타입이라 계산 하나로 통일). 뽑기 확률은
        /// 상점 레벨마다 달라서 여기가 아니라 GachaSystem.GradeWeightsByLevel에 있다.</summary>
        public struct GradeInfo
        {
            public string Name;
            public float Multiplier;
            public Color Color;
        }

        private static readonly LocCache<Dictionary<ItemGrade, GradeInfo>> GradesCache = new LocCache<Dictionary<ItemGrade, GradeInfo>>(() => new Dictionary<ItemGrade, GradeInfo>
        {
            { ItemGrade.Common, new GradeInfo { Name = Loc.T("일반"), Multiplier = 1f, Color = Color.white } },
            { ItemGrade.Uncommon, new GradeInfo { Name = Loc.T("고급"), Multiplier = 1.5f, Color = new Color(0.4f, 0.9f, 0.4f) } },
            { ItemGrade.Rare, new GradeInfo { Name = Loc.T("희귀"), Multiplier = 2.2f, Color = new Color(0.3f, 0.6f, 1f) } },
            { ItemGrade.Epic, new GradeInfo { Name = Loc.T("영웅"), Multiplier = 3.2f, Color = new Color(0.7f, 0.3f, 1f) } },
            { ItemGrade.Legendary, new GradeInfo { Name = Loc.T("전설"), Multiplier = 4.8f, Color = new Color(1f, 0.65f, 0.1f) } },
            { ItemGrade.Mythic, new GradeInfo { Name = Loc.T("신화"), Multiplier = 7f, Color = new Color(1f, 0.3f, 0.3f) } },
            { ItemGrade.Ancient, new GradeInfo { Name = Loc.T("고대"), Multiplier = 10f, Color = new Color(0.3f, 0.95f, 0.9f) } },
            { ItemGrade.Transcendent, new GradeInfo { Name = Loc.T("초월"), Multiplier = 15f, Color = new Color(120f / 255f, 110f / 255f, 1f) } }, // 11-03 우주 남보라(예전 분홍)
            { ItemGrade.Eternal, new GradeInfo { Name = Loc.T("영원"), Multiplier = 22f, Color = RainbowFallback } }, // 11-03 무지개(GradeColor·RainbowGraphic·Colorize) - 이 색은 멈춘 자리용
            // Unknown(모든 언어에서 영어 그대로, 사용자 결정) - 수치는 영원과 같고(사용자 결정 "가볍게") 세 부위를 다 끼면 세트 효과(EquipmentEffects.HasMemorySet)
            { ItemGrade.Unknown, new GradeInfo { Name = "Unknown", Multiplier = 22f, Color = new Color(0.5f, 0.95f, 0.9f) } },
        });

        public static Dictionary<ItemGrade, GradeInfo> Grades => GradesCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        // ---- 등급 "영원" = 무지개(11-03 사용자 등급 색 A안) ----
        /// <summary>무지개 등급의 멈춘 색(움직이지 않는 곳에 쓰는 대표색). 연출의 반짝이는 이 색을 받으면 알아서 무지개로 바꾼다(GachaReveal.Spawn).</summary>
        public static readonly Color RainbowFallback = new Color(1f, 0.86f, 0.62f);
        public const float RainbowSpeed = 0.35f; // 초당 색 한 바퀴의 몇 분의 1

        public static bool IsRainbow(ItemGrade grade) => grade == ItemGrade.Eternal;

        public static bool IsRainbowColor(Color color) => color == RainbowFallback;

        public static Color Rainbow(float phase) => Color.HSVToRGB(Mathf.Repeat(phase, 1f), 0.5f, 1f);

        /// <summary>지금 이 순간의 등급 색 - 무지개 등급은 시간에 따라 돈다.</summary>
        public static Color GradeColor(ItemGrade grade) => IsRainbow(grade) ? Rainbow(Time.unscaledTime * RainbowSpeed) : Grades[grade].Color;

        /// <summary>리치 텍스트로 등급 색을 입힌다 - 무지개 등급은 글자마다 색이 다르다.</summary>
        public static string Colorize(ItemGrade grade, string text)
        {
            if (!IsRainbow(grade) || string.IsNullOrEmpty(text))
                return $"<color=#{ColorUtility.ToHtmlStringRGB(Grades[grade].Color)}>{text}</color>";
            var sb = new System.Text.StringBuilder();
            var shift = Time.unscaledTime * RainbowSpeed;
            for (var i = 0; i < text.Length; i++)
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(Rainbow(shift + (float)i / Mathf.Max(6, text.Length)))}>{text[i]}</color>");
            return sb.ToString();
        }

        public static string DisplayName(ItemSlot slot, ItemGrade grade) =>
            $"[{Grades[grade].Name}] {Templates[slot].BaseName}";

        public static float AttackBonus(ItemSlot slot, ItemGrade grade) =>
            Templates[slot].BaseAttackBonus * Grades[grade].Multiplier;

        public static float HealthBonus(ItemSlot slot, ItemGrade grade) =>
            Templates[slot].BaseHealthBonus * Grades[grade].Multiplier;
    }
}
