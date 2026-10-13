using UnityEngine;

namespace LoopRogue
{
    /// <summary>히든 NPC "이름 없는 대장장이"(NpcType.Blacksmith) - 장비 3개가 전부 뽑기 최고 등급(영원)이 되면 일반 방을 비울 때 SpawnChance로 나타난다.
    /// 의뢰: 기억하는 기사(MemoryKnight)를 쓰러뜨려 그 검 조각을 가져오기(의뢰 중엔 기사가 QuestKnightChance로 더 자주 나온다).
    /// 조각을 들고 있으면 다음 일반 방을 비울 때 반드시 나타나 Unknown 등급 장비 세트(검·갑옷·반지, ItemGrade.Unknown)를 벼려 준다.
    /// 진행은 저장(PlayerPrefs) - 죽어도 남고, 엔딩 뒤 새 게임이면 장비와 함께 처음부터.</summary>
    public static class Blacksmith
    {
        public enum Stage
        {
            NotMet,        // 아직 의뢰를 안 받음(조건이 되면 나타날 수 있다)
            QuestAccepted, // 기사의 검 조각을 찾는 중
            HasFragment,   // 조각을 얻음 - 다음 방 클리어 때 대장장이가 나온다
            Done,          // Unknown 세트를 받음 - 다시 안 나온다
        }

        public const float SpawnChance = 0.2f;
        public const float QuestKnightChance = 0.15f;
        private const string StageKey = "LoopRogue_SmithStage";

        /// <summary>에디터 메뉴 "대장장이 테스트 모드"(EditorPrefs) - 켜면 장비 조건 없이 일반 방을 비울 때마다 대장장이가 나온다.</summary>
        public const string TestModePrefKey = "LoopRogue_SmithTestMode";

        public static bool TestMode
        {
            get
            {
#if UNITY_EDITOR
                return !GameHUD.AutoPlayActive && UnityEditor.EditorPrefs.GetBool(TestModePrefKey, false);
#else
                return false;
#endif
            }
        }

        public static Stage Current => (Stage)PlayerPrefs.GetInt(StageKey, 0);

        private static void Set(Stage stage)
        {
            PlayerPrefs.SetInt(StageKey, (int)stage);
            PlayerPrefs.Save();
        }

        /// <summary>장비 3개가 전부 영원 이상인지(출현 조건).</summary>
        public static bool AllEternal
        {
            get
            {
                foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                {
                    var grade = EquipmentWallet.GetEquipped(slot);
                    if (!grade.HasValue || grade.Value < ItemGrade.Eternal)
                        return false;
                }
                return true;
            }
        }

        /// <summary>일반 방을 비운 직후 대장장이를 세울지 - 조각을 들고 있으면 반드시, 아직 안 만났으면 조건 + 확률.</summary>
        public static bool ShouldAppear(System.Random rng)
        {
            var stage = Current;
            if (stage == Stage.HasFragment)
                return true;
            if (stage != Stage.NotMet)
                return false;
            if (TestMode)
                return true;
            return AllEternal && rng.NextDouble() < SpawnChance;
        }

        public static bool QuestActive => Current == Stage.QuestAccepted;

        public static void AcceptQuest()
        {
            if (Current == Stage.NotMet)
                Set(Stage.QuestAccepted);
        }

        /// <summary>의뢰 중에 기사를 쓰러뜨렸다 - 검 조각을 얻는다. 얻었으면 true.</summary>
        public static bool OnKnightDefeated()
        {
            if (Current != Stage.QuestAccepted)
                return false;
            Set(Stage.HasFragment);
            return true;
        }

        /// <summary>조각을 건네고 Unknown 세트를 받는다 - 세 부위를 전부 Unknown 등급으로.</summary>
        public static bool Forge()
        {
            if (Current != Stage.HasFragment)
                return false;
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                EquipmentWallet.TryEquipIfBetter(slot, ItemGrade.Unknown);
            Set(Stage.Done);
            Achievements.Unlock("ACH_UNKNOWN");
            return true;
        }

        public static string Name => Loc.T("이름 없는 대장장이");
        public static string FragmentName => Loc.T("기사의 검 조각");
    }
}
