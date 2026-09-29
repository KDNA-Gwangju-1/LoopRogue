using UnityEngine;

namespace LoopRogue
{
    /// <summary>Lobby↔Main 씬을 오갈 때 "지금 진행 중이던 런"의 레벨/경험치/스탯 스냅샷 - GoldWallet/
    /// EquipmentWallet과 같은 이유로 PlayerPrefs에 저장한다(사용자가 "Play 모드 껐다 켜도 유지되게
    /// 해달라"고 요청 - 처음엔 씬 전환만 버티면 된다고 판단해서 메모리에만 뒀었는데, 그걸로는
    /// Play를 멈추면 사라져서 영구 저장으로 바꿈).</summary>
    public static class RunProgress
    {
        private const string HasSavedKey = "LoopRogue_Run_HasSaved";
        private const string LevelKey = "LoopRogue_Run_Level";
        private const string ExpKey = "LoopRogue_Run_Exp";
        private const string ExpToNextKey = "LoopRogue_Run_ExpToNext";
        private const string AttackPowerKey = "LoopRogue_Run_AttackPower";
        private const string MaxHealthKey = "LoopRogue_Run_MaxHealth";
        private const string CurrentHealthKey = "LoopRogue_Run_CurrentHealth";
        private const string CriticalChanceKey = "LoopRogue_Run_CriticalChance";
        private const string PermAttackKey = "LoopRogue_Run_PermAttack";
        private const string PermHealthKey = "LoopRogue_Run_PermHealth";
        private const string PermCriticalKey = "LoopRogue_Run_PermCritical";
        private const string DamageReductionKey = "LoopRogue_Run_DamageReduction";
        private const string LifeStealKey = "LoopRogue_Run_LifeSteal";
        private const string RegenKey = "LoopRogue_Run_Regen";
        private const string KillHealKey = "LoopRogue_Run_KillHeal";
        private const string ExpBonusKey = "LoopRogue_Run_ExpBonus";
        private const string GoldBonusKey = "LoopRogue_Run_GoldBonus";

        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static bool HasSavedRun { get; private set; }
        public static int Level { get; private set; }
        public static int Exp { get; private set; }
        public static int ExpToNext { get; private set; }
        public static float AttackPower { get; private set; }
        public static float MaxHealth { get; private set; }
        public static float CurrentHealth { get; private set; }
        public static float CriticalChanceRate { get; private set; }

        // 레벨업 카드로만 오르는 스탯들 - 장비/영약과 무관해서 스냅샷 값 그대로 복원한다.
        public static float DamageReductionRate { get; private set; }
        public static float LifeStealRate { get; private set; }
        public static float RegenPerTurnRate { get; private set; }
        public static float KillHealRate { get; private set; }
        public static float ExpBonusRate { get; private set; }
        public static float GoldBonusRate { get; private set; }

        // 스냅샷을 뜰 때의 영구 보너스(장비+영약) 합계 - 로비에서 영약/장비를 새로 사면 복원할 때
        // "지금 합계 - 이 값"만큼만 더해준다. 이게 없으면 스냅샷이 로비 구매분을 영영 덮어써버린다.
        public static float PermanentAttackAtSave { get; private set; }
        public static float PermanentHealthAtSave { get; private set; }
        public static float PermanentCriticalAtSave { get; private set; }

        public static float CurrentPermanentAttack() =>
            EquipmentWallet.TotalAttackBonus() + StatPotionWallet.TotalAttackBonus();

        public static float CurrentPermanentHealth() =>
            EquipmentWallet.TotalHealthBonus() + StatPotionWallet.TotalHealthBonus();

        public static float CurrentPermanentCritical() => StatPotionWallet.TotalCriticalChanceBonus();

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            HasSavedRun = PlayerPrefs.GetInt(HasSavedKey, 0) == 1;
            Level = PlayerPrefs.GetInt(LevelKey, 1);
            Exp = PlayerPrefs.GetInt(ExpKey, 0);
            ExpToNext = PlayerPrefs.GetInt(ExpToNextKey, 10);
            AttackPower = PlayerPrefs.GetFloat(AttackPowerKey, 0f);
            MaxHealth = PlayerPrefs.GetFloat(MaxHealthKey, 0f);
            CurrentHealth = PlayerPrefs.GetFloat(CurrentHealthKey, 0f);
            CriticalChanceRate = PlayerPrefs.GetFloat(CriticalChanceKey, 0f);
            DamageReductionRate = PlayerPrefs.GetFloat(DamageReductionKey, 0f);
            LifeStealRate = PlayerPrefs.GetFloat(LifeStealKey, 0f);
            RegenPerTurnRate = PlayerPrefs.GetFloat(RegenKey, 0f);
            KillHealRate = PlayerPrefs.GetFloat(KillHealKey, 0f);
            ExpBonusRate = PlayerPrefs.GetFloat(ExpBonusKey, 0f);
            GoldBonusRate = PlayerPrefs.GetFloat(GoldBonusKey, 0f);
            // 이 키들이 생기기 전에 저장된 스냅샷이면 기본값을 "지금 합계"로 둬서(=차이 0) 중복 적용을 막는다.
            PermanentAttackAtSave = PlayerPrefs.GetFloat(PermAttackKey, CurrentPermanentAttack());
            PermanentHealthAtSave = PlayerPrefs.GetFloat(PermHealthKey, CurrentPermanentHealth());
            PermanentCriticalAtSave = PlayerPrefs.GetFloat(PermCriticalKey, CurrentPermanentCritical());
            _loaded = true;
        }

        public static void Save(LevelSystem levels, CharacterStats stats)
        {
            HasSavedRun = true;
            Level = levels.Level;
            Exp = levels.Exp;
            ExpToNext = levels.ExpToNext;
            AttackPower = stats.AttackPower;
            MaxHealth = stats.MaxHealth;
            CurrentHealth = stats.CurrentHealth;
            CriticalChanceRate = stats.CriticalChanceRate;
            DamageReductionRate = stats.DamageReductionRate;
            LifeStealRate = stats.LifeStealRate;
            RegenPerTurnRate = stats.RegenPerTurnRate;
            KillHealRate = stats.KillHealRate;
            ExpBonusRate = stats.ExpBonusRate;
            GoldBonusRate = stats.GoldBonusRate;
            PermanentAttackAtSave = CurrentPermanentAttack();
            PermanentHealthAtSave = CurrentPermanentHealth();
            PermanentCriticalAtSave = CurrentPermanentCritical();
            _loaded = true;

            PlayerPrefs.SetInt(HasSavedKey, 1);
            PlayerPrefs.SetInt(LevelKey, Level);
            PlayerPrefs.SetInt(ExpKey, Exp);
            PlayerPrefs.SetInt(ExpToNextKey, ExpToNext);
            PlayerPrefs.SetFloat(AttackPowerKey, AttackPower);
            PlayerPrefs.SetFloat(MaxHealthKey, MaxHealth);
            PlayerPrefs.SetFloat(CurrentHealthKey, CurrentHealth);
            PlayerPrefs.SetFloat(CriticalChanceKey, CriticalChanceRate);
            PlayerPrefs.SetFloat(DamageReductionKey, DamageReductionRate);
            PlayerPrefs.SetFloat(LifeStealKey, LifeStealRate);
            PlayerPrefs.SetFloat(RegenKey, RegenPerTurnRate);
            PlayerPrefs.SetFloat(KillHealKey, KillHealRate);
            PlayerPrefs.SetFloat(ExpBonusKey, ExpBonusRate);
            PlayerPrefs.SetFloat(GoldBonusKey, GoldBonusRate);
            PlayerPrefs.SetFloat(PermAttackKey, PermanentAttackAtSave);
            PlayerPrefs.SetFloat(PermHealthKey, PermanentHealthAtSave);
            PlayerPrefs.SetFloat(PermCriticalKey, PermanentCriticalAtSave);
            PlayerPrefs.Save();
        }

        /// <summary>보스를 격파하면(다음 시도부턴 새 런이어야 하니) 저장된 스냅샷을 지운다 - 지금은
        /// 호출하는 곳이 없지만(승리 화면에 로비 복귀가 아직 없음), 나중에 쓸 수 있게 미리 만들어둔다.</summary>
        public static void Clear()
        {
            HasSavedRun = false;
            PlayerPrefs.SetInt(HasSavedKey, 0);
            PlayerPrefs.Save();
        }
    }
}
