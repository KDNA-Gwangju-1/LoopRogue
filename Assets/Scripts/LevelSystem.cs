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
        private static readonly List<UpgradeOption> Pool = new List<UpgradeOption>
        {
            // 기본
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = "균형 성장", Description = "최대체력 +12, 공격력 +3 / 현재 체력 -20%",
                Apply = s => { s.IncreaseMaxHealth(12f, true); s.BaseAttack += 3f; s.LoseCurrentHealthPercent(0.2f); } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = "힘", Description = "공격력 +6 / 받는 피해 +2%",
                Apply = s => { s.BaseAttack += 6f; s.DamageReductionRate -= 0.02f; } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = "체력", Description = "최대체력 +25 / 공격력 -3%",
                Apply = s => { s.IncreaseMaxHealth(25f, true); s.BaseAttack *= 0.97f; } },
            new UpgradeOption { Category = UpgradeCategory.Basic, Title = "완전 회복", Description = "체력 전부 회복 / 공격력 -2%",
                Apply = s => { s.FullHeal(); s.BaseAttack *= 0.98f; } },

            // 성장
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = "공격력 강화", Description = "공격력 +12% / 최대체력 -3%",
                Apply = s => { s.BaseAttack *= 1.12f; s.MultiplyMaxHealth(0.97f); } },
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = "체력 강화", Description = "최대체력 +15% / 공격력 -5%",
                Apply = s => { s.IncreaseMaxHealth(s.BaseMaxHealth * 0.15f, true); s.BaseAttack *= 0.95f; } },
            new UpgradeOption { Category = UpgradeCategory.Growth, Title = "날카로움", Description = "치명타 +5%p / 공격력 -4%",
                Apply = s => { s.CriticalChanceRate += 0.05f; s.BaseAttack *= 0.96f; } },

            // 특수
            new UpgradeOption { Category = UpgradeCategory.Special, Title = "흡혈", Description = "흡혈 +8% (일반 공격만, 최대 50%) / 공격력 -3%",
                Apply = s => { s.LifeStealRate = Math.Min(CharacterStats.MaxLifeSteal, s.LifeStealRate + 0.08f); s.BaseAttack *= 0.97f; },
                IsAvailable = s => s.LifeStealRate < CharacterStats.MaxLifeSteal },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = "방어", Description = "받는 피해 -2% (최대 30%) / 골드 -5%",
                Apply = s => { s.DamageReductionRate += 0.02f; s.GoldBonusRate -= 0.05f; } },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = "재생", Description = "매 턴 최대체력 1.5% 회복 / 받는 피해 +2%",
                Apply = s => { s.RegenPerTurnRate += 0.015f; s.DamageReductionRate -= 0.02f; } },
            new UpgradeOption { Category = UpgradeCategory.Special, Title = "처치 회복", Description = "처치 시 최대체력 12% 회복 / 공격력 -3%",
                Apply = s => { s.KillHealRate += 0.12f; s.BaseAttack *= 0.97f; } },

            // 경제
            new UpgradeOption { Category = UpgradeCategory.Economy, Title = "학습", Description = "경험치 +25% / 받는 피해 +3%",
                Apply = s => { s.ExpBonusRate += 0.25f; s.DamageReductionRate -= 0.03f; } },
            new UpgradeOption { Category = UpgradeCategory.Economy, Title = "수집", Description = "골드 +20% / 경험치 -10%",
                Apply = s => { s.GoldBonusRate += 0.2f; s.ExpBonusRate -= 0.1f; } },

            // 도박 - 크게 얻고 크게 잃는다.
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = "[도박] 광폭화", Description = "공격력 +20% / 최대체력 -10%",
                Apply = s => { s.BaseAttack *= 1.2f; s.MultiplyMaxHealth(0.9f); } },
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = "[도박] 철갑", Description = "받는 피해 -3% / 공격력 -5%",
                Apply = s => { s.DamageReductionRate += 0.03f; s.BaseAttack *= 0.95f; } },
            new UpgradeOption { Category = UpgradeCategory.Gamble, Title = "[도박] 황금 욕심", Description = "골드 +30% / 받는 피해 +8%",
                Apply = s => { s.GoldBonusRate += 0.3f; s.DamageReductionRate -= 0.08f; } },
        };

        public LevelSystem(CharacterStats stats)
        {
            _stats = stats;
        }

        /// <summary>RunProgress에서 씬 전환 전 스냅샷을 그대로 되돌릴 때만 쓴다 - 정상적인 레벨업
        /// 흐름(AddExp)을 우회해서 Level/Exp/ExpToNext를 직접 덮어쓴다.</summary>
        public void RestoreProgress(int level, int exp, int expToNext)
        {
            Level = level;
            Exp = exp;
            ExpToNext = expToNext;
        }

        public void AddExp(int amount)
        {
            if (amount <= 0 || IsChoosingUpgrade)
                return;

            Exp += amount;
            if (Exp >= ExpToNext)
            {
                Exp -= ExpToNext;
                Level++;
                ExpToNext = (int)Math.Round(BaseExpToNext * Math.Pow(ExpGrowthRate, Level - 1));
                OnLevelUp?.Invoke(Level);
                PresentUpgradeChoices();
                return; // 남은 exp는 다음 처치 때 이어서 - 한 킬로 여러 레벨 오르는 케이스는 범위 밖.
            }

            OnExpChanged?.Invoke();
        }

        /// <summary>레벨업 없이 카드 한 번 고르기(축복 제단 이벤트). 이미 고르는 중이면 무시.</summary>
        public void GrantBonusUpgrade()
        {
            if (!IsChoosingUpgrade)
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

        public void ChooseUpgrade(UpgradeOption option)
        {
            option.Apply(_stats);
            IsChoosingUpgrade = false;
            OnExpChanged?.Invoke();
        }
    }
}
