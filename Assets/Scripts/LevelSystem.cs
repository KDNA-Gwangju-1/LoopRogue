using System;
using System.Collections.Generic;

namespace LoopRogue
{
    /// <summary>카드 분류 - 등장 가중치와 카드 배경색에 쓴다.</summary>
    public enum UpgradeCategory
    {
        Basic,   // 고정값 기본 카드
        Growth,  // % 성장 카드
        Special, // 흡혈/피해감소/재생/처치회복
        Economy, // 경험치/골드
        Gamble,  // 얻는 만큼 잃는 도박 카드
    }

    /// <summary>레벨업 업그레이드 카드 하나 - 제목/설명 + 실제 스탯 적용 델리게이트.</summary>
    public class UpgradeOption
    {
        public string Title;
        public string Description;
        public UpgradeCategory Category;
        public Action<CharacterStats> Apply;
        /// <summary>지금 이 카드를 보기에 넣어도 되는지 - 없으면 항상 가능(흡혈은 상한에 닿으면 빠진다).</summary>
        public Func<CharacterStats, bool> IsAvailable;

        /// <summary>뽑힐 상대 가중치 - 기본 카드가 제일 흔하고 도박 카드가 제일 드물다.</summary>
        public int Weight => Category switch
        {
            UpgradeCategory.Basic => 10,
            UpgradeCategory.Growth => 8,
            UpgradeCategory.Special => 6,
            UpgradeCategory.Economy => 6,
            _ => 4,
        };
    }

    /// <summary>적 처치 → 경험치 → 레벨업 → "3개 중 1개 선택" 업그레이드 카드. 선택 대기 중엔
    /// IsChoosingUpgrade가 true라 PlayerActor가 입력을 안 받는다(전투 중 갑자기 카드가 안 뜨게).</summary>
    public class LevelSystem
    {
        /// <summary>레벨업에 필요한 경험치도 스테이지 몹 스탯처럼 지수적으로 늘어난다("경험치도
        /// 가면갈수록 레벨업이 힘들게" 요청) - 이전엔 `10 + Level*6`(레벨당 +6 고정, 선형)이라
        /// 레벨이 아무리 올라도 다음 레벨까지 필요한 증가폭이 항상 똑같았다. 이제 레벨당
        /// ExpGrowthRate(1.13)배씩 곱해져서 초반엔 완만하다가 레벨이 쌓일수록 요구량이 확 가팔라진다.</summary>
        private const float ExpGrowthRate = 1.13f;
        private const int BaseExpToNext = 10;

        /// <summary>만렙(사용자 결정 100). Lv70까지는 위 지수 곡선 그대로, Lv70부터는 레벨당 +1,000씩만 늘어난다
        /// (사용자 결정 B안: 70→71 41,000 … 99→100 70,000, 구간 합계 약 45만/55만/65만) - 지수 그대로면 Lv79→100에만
        /// 약 1,277만이 들어서(스테이지 10 몹 약 10만 마리) 봇 100판 최고가 Lv79에서 멈췄다.</summary>
        public const int MaxLevel = 100;
        private const int LinearFromLevel = 70;
        private const int LinearStartExp = 41000;
        private const int LinearStepExp = 1000;

        /// <summary>level → level+1에 필요한 경험치.</summary>
        public static int ExpToNextFor(int level) => level >= LinearFromLevel
            ? LinearStartExp + LinearStepExp * (level - LinearFromLevel)
            : (int)Math.Round(BaseExpToNext * Math.Pow(ExpGrowthRate, level - 1));

        public bool IsMaxLevel => Level >= MaxLevel;

        public int Level { get; private set; } = 1;
        public int Exp { get; private set; }
        public int ExpToNext { get; private set; } = BaseExpToNext;
        public bool IsChoosingUpgrade { get; private set; }

        public event Action<int> OnLevelUp;
        public event Action OnExpChanged;
        public event Action<List<UpgradeOption>> OnUpgradeChoicesReady;

        private readonly CharacterStats _stats;
        private static readonly Random Rng = new Random();

        /// <summary>모든 카드가 "얻는 것 + 잃는 것" 구조("모든 선택지를 도박처럼" 요청). 얻는 쪽을 잃는 쪽보다
        /// 크게 잡아서 순이익은 항상 플러스. 손해는 항상 가지고 있는 스탯(공격력/최대체력/현재체력/받는 피해/
        /// 골드·경험치 보너스)에서만 깎는다 - 처음엔 0인 치명타/흡혈 등을 깎으면 사실상 손해 없는 카드가 된다.
        /// 도박 분류는 "크게 얻고 크게 잃는" 카드로 남겨 일반 카드와 구분한다.
        /// 최대체력 %감소는 장비/영약으로 쌓은 체력 전체를 깎아서(봇 한 판 기준 카드만으로 최대체력 약 -52%) 광폭화(도박)와
        /// 공격력 강화(-3%로 절반)에만 남기고 나머지는 다른 스탯으로 옮겼다.
        /// 공격력/최대체력의 %증감과 체력 강화(+15%)는 성장 몫(기본 + 카드)에만 걸린다 - 장비/영약 몫은 고정(CharacterStats).</summary>
        private static readonly LocCache<List<UpgradeOption>> PoolCache = new LocCache<List<UpgradeOption>>(() => new List<UpgradeOption>
        {
            // 기본
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = Loc.T("균형 성장"), Description = Loc.T("최대체력 +12, 공격력 +3 / 현재 체력 -20%"),
                Apply = s => { s.IncreaseMaxHealth(12f, true); s.BaseAttack += 3f; s.LoseCurrentHealthPercent(0.2f); } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = Loc.T("힘"), Description = Loc.T("공격력 +6 / 받는 피해 +2%"),
                Apply = s => { s.BaseAttack += 6f; s.DamageReductionRate -= 0.02f; } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = Loc.T("체력"), Description = Loc.T("최대체력 +25 / 공격력 -3%"),
                Apply = s => { s.IncreaseMaxHealth(25f, true); s.BaseAttack *= 0.97f; } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = Loc.T("완전 회복"), Description = Loc.T("체력 전부 회복 / 공격력 -2%"),
                Apply = s => { s.FullHeal(); s.BaseAttack *= 0.98f; } },

            // 성장
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = Loc.T("공격력 강화"), Description = Loc.T("공격력 +12% / 최대체력 -3%"),
                Apply = s => { s.BaseAttack *= 1.12f; s.MultiplyMaxHealth(0.97f); } },
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = Loc.T("체력 강화"), Description = Loc.T("최대체력 +15% / 공격력 -5%"),
                Apply = s => { s.IncreaseMaxHealth(s.BaseMaxHealth * 0.15f, true); s.BaseAttack *= 0.95f; } },
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = Loc.T("날카로움"), Description = Loc.T("치명타 +5%p / 공격력 -4%"),
                Apply = s => { s.CriticalChanceRate += 0.05f; s.BaseAttack *= 0.96f; } },

            // 특수
            new UpgradeOption { Category = UpgradeCategory.Special, Title = Loc.T("흡혈"), Description = Loc.T("흡혈 +8% (일반 공격만, 최대 50%) / 공격력 -3%"),
                Apply = s => { s.LifeStealRate = Math.Min(Relics.LifeStealCap, s.LifeStealRate + 0.08f); s.BaseAttack *= 0.97f; },
                IsAvailable = s => s.LifeStealRate < Relics.LifeStealCap },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = Loc.T("방어"), Description = Loc.T("받는 피해 -2% (최대 30%) / 골드 -5%"),
                Apply = s => { s.DamageReductionRate += 0.02f; s.GoldBonusRate -= 0.05f; } },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = Loc.T("재생"), Description = Loc.T("매 턴 최대체력 1.5% 회복 / 받는 피해 +2%"),
                Apply = s => { s.RegenPerTurnRate += 0.015f; s.DamageReductionRate -= 0.02f; } },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = Loc.T("처치 회복"), Description = Loc.T("처치 시 최대체력 12% 회복 / 공격력 -3%"),
                Apply = s => { s.KillHealRate += 0.12f; s.BaseAttack *= 0.97f; } },

            // 경제
            new UpgradeOption { Category = UpgradeCategory.Economy, Title = Loc.T("학습"), Description = Loc.T("경험치 +25% / 받는 피해 +3%"),
                Apply = s => { s.ExpBonusRate += 0.25f; s.DamageReductionRate -= 0.03f; } },
            new UpgradeOption { Category = UpgradeCategory.Economy, Title = Loc.T("수집"), Description = Loc.T("골드 +20% / 경험치 -10%"),
                Apply = s => { s.GoldBonusRate += 0.2f; s.ExpBonusRate -= 0.1f; } },

            // 도박 - 크게 얻고 크게 잃는다.
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = Loc.T("[도박] 광폭화"), Description = Loc.T("공격력 +20% / 최대체력 -10%"),
                Apply = s => { s.BaseAttack *= 1.2f; s.MultiplyMaxHealth(0.9f); } },
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = Loc.T("[도박] 철갑"), Description = Loc.T("받는 피해 -3% / 공격력 -5%"),
                Apply = s => { s.DamageReductionRate += 0.03f; s.BaseAttack *= 0.95f; } },
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = Loc.T("[도박] 황금 욕심"), Description = Loc.T("골드 +30% / 받는 피해 +8%"),
                Apply = s => { s.GoldBonusRate += 0.3f; s.DamageReductionRate -= 0.08f; } },
        });
        private static List<UpgradeOption> Pool => PoolCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        public LevelSystem(CharacterStats stats)
        {
            _stats = stats;
        }

        /// <summary>RunProgress에서 씬 전환 전 스냅샷을 그대로 되돌릴 때만 쓴다 - 정상적인 레벨업
        /// 흐름(AddExp)을 우회해서 Level/Exp/ExpToNext를 직접 덮어쓴다.</summary>
        public void RestoreProgress(int level, int exp, int expToNext)
        {
            Level = Math.Min(level, MaxLevel);
            ExpToNext = ExpToNextFor(Level); // 곡선이 바뀌기 전에 저장된 판도 지금 곡선으로(옛 Lv70+ 요구량은 훨씬 컸다)
            Exp = Math.Min(exp, ExpToNext - 1);
        }

        public void AddExp(int amount)
        {
            if (amount <= 0 || IsMaxLevel)
                return;

            Exp += amount;
            // 카드를 고르는 중이면 경험치만 쌓아두고 레벨업은 고른 뒤에(ChooseUpgrade) - 예전엔 회전 베기로 여러 마리를 잡으면
            // 첫 마리로 레벨업한 뒤 나머지 경험치가 통째로 버려졌다.
            if (IsChoosingUpgrade || !TryLevelUp())
                OnExpChanged?.Invoke();
        }

        /// <summary>경험치가 찼으면 한 레벨 올리고 카드를 보여준다(한 번에 한 레벨 - 남으면 카드를 고른 뒤 또).</summary>
        private bool TryLevelUp()
        {
            if (IsMaxLevel || Exp < ExpToNext)
                return false;
            Exp -= ExpToNext;
            Level++;
            ExpToNext = ExpToNextFor(Level);
            if (IsMaxLevel)
                Exp = 0;
            OnLevelUp?.Invoke(Level);
            PresentUpgradeChoices();
            return true;
        }

        /// <summary>고르는 중에 받은 보너스 카드(축복 제단·도전의 깃발·운명의 주사위 등) - 지금 카드를 고른 뒤 이어서 보여준다.</summary>
        private int _pendingBonusUpgrades;

        /// <summary>레벨업 없이 카드 한 번 고르기(축복 제단 등). 이미 고르는 중이면 미뤄뒀다가 그다음에.</summary>
        public void GrantBonusUpgrade()
        {
            if (IsChoosingUpgrade)
                _pendingBonusUpgrades++;
            else
                PresentUpgradeChoices();
        }

        private void PresentUpgradeChoices()
        {
            IsChoosingUpgrade = true;

            // 가중치 랜덤으로 서로 다른 3장 - 뽑힌 카드는 후보에서 빼고 다시 굴린다.
            var pool = Pool.FindAll(o => o.IsAvailable == null || o.IsAvailable(_stats));
            var picks = new List<UpgradeOption>();
            for (var i = 0; i < 3 && pool.Count > 0; i++)
            {
                var total = 0;
                foreach (var option in pool)
                    total += option.Weight;

                var roll = Rng.Next(total);
                var idx = 0;
                while (roll >= pool[idx].Weight)
                {
                    roll -= pool[idx].Weight;
                    idx++;
                }

                picks.Add(pool[idx]);
                pool.RemoveAt(idx);
            }

            OnUpgradeChoicesReady?.Invoke(picks);
        }

        /// <summary>마지막으로 카드를 고른 프레임 - 같은 숫자키가 퀵슬롯으로 한 번 더 읽히는 걸 막는다(PlayerActor).</summary>
        public int LastChoiceFrame { get; private set; } = -1;

        public void ChooseUpgrade(UpgradeOption option)
        {
            LastChoiceFrame = UnityEngine.Time.frameCount;
            option.Apply(_stats);
            IsChoosingUpgrade = false;
            // 고르는 사이 쌓인 경험치로 또 레벨업하거나, 미뤄둔 보너스 카드가 있으면 이어서 보여준다.
            if (TryLevelUp())
                return;
            if (_pendingBonusUpgrades > 0)
            {
                _pendingBonusUpgrades--;
                PresentUpgradeChoices();
                return;
            }
            OnExpChanged?.Invoke();
        }
    }
}
