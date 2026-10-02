using UnityEngine;

namespace LoopRogue
{
    /// <summary>플레이어/적 공통 전투 수치 - HP/공격력만 있는 최소 구성(턴제라 이동속도 개념 자체가
    /// 없다, "1턴에 1칸"이 곧 이동속도).</summary>
    public class CharacterStats
    {
        /// <summary>치명타 기본 배율 150%. 치명타 확률은 100%에서 멈추고, 넘친 확률은 버리지 않고 배율로
        /// 바꾼다 - 초과 1%p당 치명타 피해 +0.5%p(예: 확률 140% → 확정 치명타, 배율 150%+20% = 170%).</summary>
        public const float BaseCriticalDamageMultiplier = 1.5f;
        public const float MaxCriticalChance = 1f;
        public const float ExcessCriticalToDamage = 0.5f;

        public float EffectiveCriticalChance => Mathf.Clamp(CriticalChanceRate, 0f, MaxCriticalChance);
        public float CriticalDamageMultiplier =>
            BaseCriticalDamageMultiplier + Mathf.Max(0f, CriticalChanceRate - MaxCriticalChance) * ExcessCriticalToDamage;

        private static readonly System.Random Rng = new System.Random();

        /// <summary>최대체력/공격력 = 성장 몫(Base: 기본 + 레벨업 카드) + 고정 몫(Fixed: 장비 + 영약). 레벨업 카드의
        /// %증감은 성장 몫에만 걸린다 - 예전엔 합계에 곱해서 장비/영약으로 얻은 스탯까지 카드 손해로 깎였고, 같은 장비도
        /// 카드보다 먼저 샀는지 나중에 샀는지에 따라 실제로 오르는 양이 달랐다. 몹은 고정 몫이 항상 0.</summary>
        public float BaseMaxHealth;
        public float FixedMaxHealth;
        public float BaseAttack;
        public float FixedAttack;
        public float MaxHealth => BaseMaxHealth + FixedMaxHealth;
        public float AttackPower => BaseAttack + FixedAttack;
        public float CurrentHealth;

        /// <summary>0~1 확률 - 기본 0(치명타 없음), 치명타 영약/레벨업 카드로 오른다.</summary>
        public float CriticalChanceRate;

        // ---- 레벨업 카드 전용 스탯(몹은 전부 0이라 영향 없음) ----
        /// <summary>받는 피해 감소 비율 - 음수면 오히려 더 받는다(도박 카드). 적용 시 ±MaxDamageReduction(30%)으로 제한.</summary>
        public const float MaxDamageReduction = 0.3f;
        public float DamageReductionRate;
        public float LifeStealRate;    // 준 피해 대비 회복 비율
        /// <summary>흡혈 상한(사용자 결정 50%) - 봇 100판에서 흡혈 카드가 판당 중앙 11장(88%), 최대 20장(160%)까지 쌓였다.
        /// 상한에 닿으면 흡혈 카드는 더 이상 안 나온다(LevelSystem). 상한 도입 전 저장된 판도 적용은 이 값까지만.</summary>
        public const float MaxLifeSteal = 0.5f;
        public float EffectiveLifeSteal => Mathf.Clamp(LifeStealRate, 0f, MaxLifeSteal);
        public float RegenPerTurnRate; // 매 턴 최대체력 대비 회복 비율
        public float KillHealRate;     // 적 처치 시 최대체력 대비 회복 비율
        public float ExpBonusRate;     // 획득 경험치 +%
        public float GoldBonusRate;    // 획득 골드 +%

        /// <summary>골드/경험치 보너스는 카드 손해로 마이너스까지 갈 수 있어서 실제 적용은 이 하한으로 자른다.</summary>
        public const float MinBonusRate = -0.5f;
        public float EffectiveExpBonus => Mathf.Max(MinBonusRate, ExpBonusRate);
        public float EffectiveGoldBonus => Mathf.Max(MinBonusRate, GoldBonusRate);

        public CharacterStats(float maxHealth, float attackPower)
        {
            BaseMaxHealth = maxHealth;
            BaseAttack = attackPower;
            CurrentHealth = maxHealth;
        }

        public bool IsDead => CurrentHealth <= 0f;

        public void TakeDamage(float amount)
        {
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        }

        /// <summary>공격력 그대로 넘기지 않고 이걸 거치면 크리 확률을 굴려서 필요하면
        /// CriticalDamageMultiplier를 곱한 데미지를 돌려준다 - 몹은 CriticalChanceRate가 항상
        /// 0이라 자연히 크리가 안 뜬다(별도 분기 필요 없음).</summary>
        public float RollAttackDamage(out bool isCritical)
        {
            var chance = EffectiveCriticalChance;
            isCritical = chance > 0f && Rng.NextDouble() < chance;
            return isCritical ? AttackPower * CriticalDamageMultiplier : AttackPower;
        }

        /// <summary>적의 공격처럼 "받는 피해"는 이걸로 - 피해 감소를 적용한 실제 피해량을 돌려준다(팝업 표시용).</summary>
        /// <summary>플레이어가 받은 피해 누적(TakeIncomingDamage는 플레이어만 쓴다) - 봇의 방당 받은 피해 측정용.</summary>
        public static double IncomingDamageTotal;

        /// <summary>보호막 아이템 - 다음 피해 1회 무효.</summary>
        public bool BlockNextHit;

        // ---- 갑옷 고유 효과(플레이어만) - 보호막은 방마다(PlayerActor.OnRoomEntered), 불굴/응급 처치는 시도마다(OnAttemptStarted) 채운다 ----
        /// <summary>보호막 - 피해를 먼저 흡수하는 체력.</summary>
        public float Shield;
        /// <summary>불굴 - 이번 시도에서 아직 안 썼으면 true(죽을 피해를 체력 1로 버팀).</summary>
        public bool UndyingReady;
        /// <summary>응급 처치 - 이번 시도에서 아직 안 썼으면 true.</summary>
        public bool EmergencyHealReady;
        /// <summary>불굴/응급 처치가 발동했을 때 알림(메시지 표시용).</summary>
        public System.Action<string> OnArmorEffect;

        public float TakeIncomingDamage(float rawDamage)
        {
            if (BlockNextHit)
            {
                BlockNextHit = false;
                return 0f;
            }
            var reduction = Mathf.Clamp(DamageReductionRate, -MaxDamageReduction, MaxDamageReduction);
            var damage = rawDamage * (1f - reduction);

            var absorbed = Mathf.Min(Shield, damage);
            Shield -= absorbed;
            var toHealth = damage - absorbed;

            if (UndyingReady && toHealth >= CurrentHealth)
            {
                UndyingReady = false;
                toHealth = Mathf.Max(0f, CurrentHealth - 1f);
                OnArmorEffect?.Invoke("불굴! 체력 1로 버텼다");
            }
            TakeDamage(toHealth);

            if (EmergencyHealReady && !IsDead && CurrentHealth < MaxHealth * EquipmentEffects.EmergencyThreshold)
            {
                EmergencyHealReady = false;
                Heal(MaxHealth * EquipmentEffects.EmergencyHealRate);
                OnArmorEffect?.Invoke("응급 처치! 체력 회복");
            }

            IncomingDamageTotal += damage;
            return damage;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }

        /// <summary>카드의 "최대체력 ±N%"용 - 성장 몫만 배율로 바꾸고(장비/영약 몫은 그대로) 현재 체력이 넘치면 잘라낸다.</summary>
        public void MultiplyMaxHealth(float factor)
        {
            BaseMaxHealth = Mathf.Max(1f, BaseMaxHealth * factor);
            CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        }

        /// <summary>"현재 체력 -N%" 카드 손해용 - 이걸로는 절대 죽지 않게 최소 1은 남긴다.</summary>
        public void LoseCurrentHealthPercent(float percent)
        {
            CurrentHealth = Mathf.Max(1f, CurrentHealth * (1f - percent));
        }

        public void FullHeal()
        {
            CurrentHealth = MaxHealth;
        }

        /// <summary>레벨업 업그레이드 카드 전용 - 최대체력을 올리고, healToo면 그만큼 즉시 회복도 준다.</summary>
        public void IncreaseMaxHealth(float amount, bool healToo)
        {
            BaseMaxHealth += amount;
            if (healToo)
                CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        }
    }
}
