using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    public enum TitleEffect
    {
        Gold,    // 골드 획득 +% (Value = 비율)
        Exp,     // 경험치 획득 +%
        Attack,  // 공격력 +(고정, 영약처럼 영구 몫)
        Health,  // 최대 체력 +(고정)
        Crit,    // 치명타 확률 +%p
    }

    /// <summary>업적 하나 - 달성하면 칭호가 열리고 보상(슬롯 이용권/뽑기권)을 준다. SteamId는 Steamworks 파트너 사이트에 등록할 업적 API 이름과
    /// 똑같아야 한다(SteamBridge). Condition이 null이면 이벤트로만 달성(잭팟, 10층 클리어 등 - Achievements.Unlock을 직접 부른다).</summary>
    public sealed class AchievementDef
    {
        public string SteamId;
        public string Name;
        public string Description;
        public string Title;
        public TitleEffect Effect;
        public float Value;
        public int SlotTickets;
        public int GachaTickets;
        public Func<bool> Condition;

        public string EffectText => Effect switch
        {
            TitleEffect.Gold => Loc.F("골드 +{0:0}%", Value * 100f),
            TitleEffect.Exp => Loc.F("경험치 +{0:0}%", Value * 100f),
            TitleEffect.Attack => Loc.F("공격력 +{0:0}", Value),
            TitleEffect.Health => Loc.F("최대 체력 +{0:0}", Value),
            _ => Loc.F("치명타 +{0:0}%p", Value * 100f),
        };

        public string RewardText =>
            string.Join(", ", new[]
            {
                SlotTickets > 0 ? Loc.F("슬롯 이용권 {0}장", SlotTickets) : null,
                GachaTickets > 0 ? Loc.F("뽑기권 {0}장", GachaTickets) : null,
            }.Where(s => s != null));
    }

    /// <summary>업적·칭호 - 달성 기록과 장착 칭호를 영구 저장(PlayerPrefs)한다. 대부분의 업적은 Check()가 누적 기록(사망 수, 처치 수, 층,
    /// 장비, 유물, 상인 구매)을 보고 판정하고, 그 기록이 바뀌는 곳(LoopRecord/LobbyQuests/StageProgress/GachaSystem/Relics/NpcActor)이 Check를 부른다.
    /// 칭호는 하나만 장착하고(로비 업적 창) 그 칭호의 작은 효과가 붙는다 - 공격력/체력/치명타는 RunProgress의 영구 몫에, 골드/경험치는 배율에 더한다.
    /// 달성하면 SteamBridge로 스팀 업적도 같이 연다(스팀이 없으면 아무 일도 안 함).</summary>
    public static class Achievements
    {
        private const string UnlockedKey = "LoopRogue_Achievements";
        private const string TitleKey = "LoopRogue_Title";
        private const string MerchantBuysKey = "LoopRogue_MerchantBuys";

        private static readonly LocCache<AchievementDef[]> AllCache = new LocCache<AchievementDef[]>(() => new AchievementDef[]

        {
            new AchievementDef { SteamId = "ACH_DEATH_1", Name = Loc.T("첫 번째 귀환"), Description = Loc.T("처음으로 죽고 돌아온다"),
                Title = Loc.T("갓 돌아온 자"), Effect = TitleEffect.Exp, Value = 0.03f, SlotTickets = 3,
                Condition = () => LoopRecord.TotalDeaths >= 1 },
            new AchievementDef { SteamId = "ACH_DEATH_10", Name = Loc.T("고리의 손님"), Description = Loc.T("10번 죽고 돌아온다"),
                Title = Loc.T("단골 귀환자"), Effect = TitleEffect.Gold, Value = 0.03f, GachaTickets = 1,
                Condition = () => LoopRecord.TotalDeaths >= 10 },
            new AchievementDef { SteamId = "ACH_DEATH_50", Name = Loc.T("세는 걸 멈추지 마"), Description = Loc.T("50번 죽고 돌아온다"),
                Title = Loc.T("고리의 산증인"), Effect = TitleEffect.Health, Value = 15f, GachaTickets = 2,
                Condition = () => LoopRecord.TotalDeaths >= 50 },
            new AchievementDef { SteamId = "ACH_DEATH_100", Name = Loc.T("백 번째 문"), Description = Loc.T("100번 죽고 돌아온다"),
                Title = Loc.T("불굴의 귀환자"), Effect = TitleEffect.Attack, Value = 4f, GachaTickets = 3,
                Condition = () => LoopRecord.TotalDeaths >= 100 },
            new AchievementDef { SteamId = "ACH_STAGE_2", Name = Loc.T("첫 문지기"), Description = Loc.T("1층 보스를 쓰러뜨린다"),
                Title = Loc.T("문지기 사냥꾼"), Effect = TitleEffect.Attack, Value = 2f, SlotTickets = 5,
                Condition = () => StageProgress.CurrentStage >= 2 },
            new AchievementDef { SteamId = "ACH_STAGE_5", Name = Loc.T("고리의 절반"), Description = Loc.T("5층에 도달한다"),
                Title = Loc.T("중층의 방랑자"), Effect = TitleEffect.Health, Value = 10f, GachaTickets = 2,
                Condition = () => StageProgress.CurrentStage >= 5 },
            new AchievementDef { SteamId = "ACH_STAGE_10", Name = Loc.T("심연의 입구"), Description = Loc.T("10층에 도달한다"),
                Title = Loc.T("심연의 도전자"), Effect = TitleEffect.Crit, Value = 0.02f, GachaTickets = 3,
                Condition = () => StageProgress.CurrentStage >= StageProgress.MaxStage },
            new AchievementDef { SteamId = "ACH_CLEAR_10", Name = Loc.T("고리를 끊은 자"), Description = Loc.T("10층 보스를 쓰러뜨린다"),
                Title = Loc.T("고리를 끊은 자"), Effect = TitleEffect.Attack, Value = 6f, GachaTickets = 5 },
            new AchievementDef { SteamId = "ACH_KILL_100", Name = Loc.T("첫 사냥"), Description = Loc.T("몬스터를 누적 100마리 처치한다"),
                Title = Loc.T("풋내기 사냥꾼"), Effect = TitleEffect.Exp, Value = 0.02f, SlotTickets = 3,
                Condition = () => LobbyQuests.LifetimeKills >= 100 },
            new AchievementDef { SteamId = "ACH_KILL_500", Name = Loc.T("사냥꾼의 길"), Description = Loc.T("몬스터를 누적 500마리 처치한다"),
                Title = Loc.T("사냥꾼"), Effect = TitleEffect.Gold, Value = 0.04f, GachaTickets = 1,
                Condition = () => LobbyQuests.LifetimeKills >= 500 },
            new AchievementDef { SteamId = "ACH_KILL_3000", Name = Loc.T("학살자"), Description = Loc.T("몬스터를 누적 3000마리 처치한다"),
                Title = Loc.T("학살자"), Effect = TitleEffect.Attack, Value = 3f, GachaTickets = 3,
                Condition = () => LobbyQuests.LifetimeKills >= 3000 },
            new AchievementDef { SteamId = "ACH_ETERNAL", Name = Loc.T("영원을 손에"), Description = Loc.T("'영원' 등급 장비를 장착한다"),
                Title = Loc.T("영원의 소유자"), Effect = TitleEffect.Crit, Value = 0.02f, SlotTickets = 10,
                Condition = () => Enum.GetValues(typeof(ItemSlot)).Cast<ItemSlot>().Any(s => EquipmentWallet.GetEquipped(s) == ItemGrade.Eternal) },
            new AchievementDef { SteamId = "ACH_JACKPOT", Name = Loc.T("잭팟!"), Description = Loc.T("운명의 슬롯에서 3개를 맞춘다"),
                Title = Loc.T("행운아"), Effect = TitleEffect.Gold, Value = 0.05f, SlotTickets = 10 },
            new AchievementDef { SteamId = "ACH_MERCHANT_10", Name = Loc.T("단골손님"), Description = Loc.T("떠돌이 상인에게 물건을 10번 산다"),
                Title = Loc.T("상인의 친구"), Effect = TitleEffect.Gold, Value = 0.03f, GachaTickets = 1,
                Condition = () => MerchantBuys >= 10 },
            new AchievementDef { SteamId = "ACH_WANDERER", Name = Loc.T("약속을 지킨 자"), Description = Loc.T("길 잃은 모험가의 의뢰를 완료한다"),
                Title = Loc.T("모험가의 은인"), Effect = TitleEffect.Exp, Value = 0.05f, GachaTickets = 3 },
            new AchievementDef { SteamId = "ACH_RELIC_5", Name = Loc.T("유물 수집가"), Description = Loc.T("유물을 5개 모은다"),
                Title = Loc.T("유물 수집가"), Effect = TitleEffect.Exp, Value = 0.04f, SlotTickets = 5,
                Condition = () => Relics.OwnedCount >= 5 },
            new AchievementDef { SteamId = "ACH_CODEX_HALF", Name = Loc.T("기록하는 자"), Description = Loc.T("도감을 절반 채운다"),
                Title = Loc.T("기록관"), Effect = TitleEffect.Exp, Value = 0.03f, SlotTickets = 5,
                Condition = () => Codex.FoundCount() * 2 >= Codex.TotalCount() },
            new AchievementDef { SteamId = "ACH_CODEX_ALL", Name = Loc.T("모든 것을 본 자"), Description = Loc.T("도감을 전부 채운다"),
                Title = Loc.T("고리의 사서"), Effect = TitleEffect.Gold, Value = 0.05f, GachaTickets = 5,
                Condition = () => Codex.FoundCount() >= Codex.TotalCount() },
        });

        public static AchievementDef[] All => AllCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        /// <summary>업적을 달성할 때마다 - 지금 씬의 UI(GameHUD 배너, 로비 결과 줄)가 구독해서 알린다.</summary>
        public static event Action<AchievementDef> Unlocked;

        private static bool _loaded;
        private static readonly HashSet<string> UnlockedIds = new HashSet<string>();
        private static string _title;
        private static int _merchantBuys;

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            UnlockedIds.Clear();
            foreach (var id in PlayerPrefs.GetString(UnlockedKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                UnlockedIds.Add(id);
            _title = PlayerPrefs.GetString(TitleKey, "");
            _merchantBuys = PlayerPrefs.GetInt(MerchantBuysKey, 0);
        }

        public static bool IsUnlocked(AchievementDef def)
        {
            EnsureLoaded();
            return UnlockedIds.Contains(def.SteamId);
        }

        public static int UnlockedCount
        {
            get
            {
                EnsureLoaded();
                return All.Count(IsUnlocked);
            }
        }

        public static IEnumerable<string> UnlockedSteamIds
        {
            get
            {
                EnsureLoaded();
                return UnlockedIds.ToList();
            }
        }

        public static int MerchantBuys
        {
            get
            {
                EnsureLoaded();
                return _merchantBuys;
            }
        }

        /// <summary>떠돌이 상인에게서 하나 살 때마다(NpcActor).</summary>
        public static void AddMerchantBuy()
        {
            EnsureLoaded();
            _merchantBuys++;
            PlayerPrefs.SetInt(MerchantBuysKey, _merchantBuys);
            Check();
        }

        /// <summary>조건이 있는 업적을 전부 다시 본다 - 누적 기록이 바뀌는 곳에서 부른다(가벼워서 자주 불러도 된다).</summary>
        public static void Check()
        {
            EnsureLoaded();
            foreach (var def in All)
                if (def.Condition != null && !UnlockedIds.Contains(def.SteamId) && def.Condition())
                    Unlock(def.SteamId);
        }

        /// <summary>업적 달성 - 저장하고 보상을 준 뒤 알리고 스팀에도 연다. 이미 달성했으면 아무것도 안 한다.</summary>
        public static void Unlock(string steamId)
        {
            EnsureLoaded();
            var def = All.FirstOrDefault(d => d.SteamId == steamId);
            if (def == null || !UnlockedIds.Add(steamId))
                return;
            Save();
            if (def.SlotTickets > 0)
                SlotMachine.AddTickets(def.SlotTickets);
            if (def.GachaTickets > 0)
                GachaSystem.AddTickets(def.GachaTickets);
            SteamBridge.UnlockAchievement(steamId);
            Unlocked?.Invoke(def);
        }

        /// <summary>스팀에는 있는데 여기 기록이 없는 업적(저장 초기화 후 등)을 되살린다 - 보상은 이미 받았던 것이라 다시 주지 않는다.</summary>
        public static void RestoreFromSteam(string steamId)
        {
            EnsureLoaded();
            if (All.Any(d => d.SteamId == steamId) && UnlockedIds.Add(steamId))
                Save();
        }

        private static void Save()
        {
            PlayerPrefs.SetString(UnlockedKey, string.Join(",", UnlockedIds));
            PlayerPrefs.Save();
        }

        // ===================== 칭호 =====================

        /// <summary>장착한 칭호의 업적 - 없거나 아직 못 연 칭호면 null.</summary>
        public static AchievementDef EquippedTitle
        {
            get
            {
                EnsureLoaded();
                var def = All.FirstOrDefault(d => d.SteamId == _title);
                return def != null && UnlockedIds.Contains(def.SteamId) ? def : null;
            }
        }

        /// <summary>칭호 장착(null이면 해제) - 로비에서만 바꾼다(공격력/체력 몫은 다음 Main 입장 때 RunProgress가 차이만큼 반영).</summary>
        public static void EquipTitle(AchievementDef def)
        {
            EnsureLoaded();
            _title = def != null && IsUnlocked(def) ? def.SteamId : "";
            PlayerPrefs.SetString(TitleKey, _title);
            PlayerPrefs.Save();
        }

        private static float TitleValue(TitleEffect effect)
        {
            var title = EquippedTitle;
            return title != null && title.Effect == effect ? title.Value : 0f;
        }

        public static float TitleGoldBonus => TitleValue(TitleEffect.Gold);
        public static float TitleExpBonus => TitleValue(TitleEffect.Exp);
        public static float TitleAttack => TitleValue(TitleEffect.Attack);
        public static float TitleHealth => TitleValue(TitleEffect.Health);
        public static float TitleCrit => TitleValue(TitleEffect.Crit);
    }
}
