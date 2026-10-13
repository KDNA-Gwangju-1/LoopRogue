using UnityEngine;

namespace LoopRogue
{
    /// <summary>플레이어 공격 종류 - 기억하는 기사의 "간파"가 같은 공격을 연달아 쓰는지 센다. Other(유물 추가타 등)는 세지도 피하지도 않는다.</summary>
    public enum AttackStyle
    {
        Other,
        Normal,
        Dash,
        Spin,
    }

    /// <summary>히든 몹 "기억하는 기사" - 몬스터 중 혼자만 고리를 기억하는 기사(노인의 기억 조각 "몬스터들도 고리에 갇혀 있다"에서).
    /// MinStage층부터 일반 전투 방을 깔 때 RoomChance 확률로 정해지고, 그 방을 비우는 순간 나타난다. 한 층에 한 번만(잡든 못 잡든, 대장장이 의뢰 중엔 예외).
    /// 말을 하고(ShowMessage), 머리를 쓴다:
    ///  - 간파: 같은 종류 공격(일반·대시·회전 베기)을 연달아 맞을수록 ReadChance만큼 피한다. 다른 공격으로 바꾸면 처음부터.
    ///  - 강타: 붙어 있으면 플레이어 칸과 그 양옆(3칸)을 예고하고 다음 턴에 공격력 SlashDamageRate배로 벤다. 뒤로 물러나면 피한다.
    ///  - 기억 재현: EchoCooldownTurns마다, 플레이어가 마지막으로 쓴 스킬을 따라 한다 - 회전 베기면 자기 주변 8칸, 대시면 같은 줄 최대 EchoDashRange칸을
    ///    청록으로 예고하고 다음 턴에 공격력 EchoDamageRate배(대시는 그 줄을 달려 들이받는다). 스킬을 안 썼으면 안 쓴다.
    ///  - 후퇴: 체력이 RetreatThreshold 아래로 처음 떨어지면 RetreatTurns턴 동안 물러나며 숨을 고른다(턴마다 최대 체력 RetreatHealRate 회복).
    /// 잡으면 골드(방 클리어 골드 × GoldRooms), RelicMinStage층 이상에서 잡으면 히든 유물 "기사의 기억"(RelicType.KnightMemory, 한 번). 행동은 EnemyActor.TakeKnightTurn.</summary>
    public static class MemoryKnight
    {
        public const int MinStage = 3;
        public const float RoomChance = 0.01f; // 3% → 1%(10-13 사용자 - 봇 기준 한 판 11번은 히든치고 너무 잦았다)
        /// <summary>세기(사용자 결정 A안, 10-13): 체력은 그 층 보스급(GameBootstrap.BossStats). 예고 없는 일반 공격은 그 방 일반 몹 × NormalAttackRatio로 약하게,
        /// 예고하는 패턴(강타·기억 재현)만 "패턴 위력" = 보스 공격력 × BossPatternRate 기준으로 세게 - 보스처럼 예고를 보고 피하면 이기는 싸움.
        /// (처음엔 공격력 자체를 보스의 75%로 했더니 10층에서 일반 공격 한 대에 죽는 계산이 나왔다.)</summary>
        public const float BossHealthRate = 1f;
        public const float NormalAttackRatio = 1.5f;
        public const float BossPatternRate = 0.75f;
        public const string SpriteSet = "MemoryKnight";
        public const float SpriteScale = 1.4f;
        public static readonly Color TintColor = new Color(0.3f, 0.75f, 0.72f);

        /// <summary>간파 확률 - [같은 공격을 연달아 몇 번째 맞는지(0 = 처음)], 마지막 값에서 멈춘다.</summary>
        public static readonly float[] ReadChance = { 0f, 0.25f, 0.4f, 0.5f };
        public const float RetreatThreshold = 0.35f;
        public const int RetreatTurns = 3;
        public const float RetreatHealRate = 0.06f;
        public const int SlashCooldownTurns = 3;
        public const float SlashDamageRate = 1.5f; // × 패턴 위력(보스 패턴 배율 BossBrain.PatternDamageMultiplier와 같게)
        public const int GoldRooms = 15;

        /// <summary>히든 유물 "기사의 기억"은 이 층 이상에서 잡아야 준다(사용자 결정 10-13 - 3~4층에서 얻으면 게임 전체가 너무 쉬워졌다:
        /// 봇 100판 전부 획득, 판당 872번 피해 무효). 그 아래에선 골드만. 에디터 테스트 모드는 층 상관없이.</summary>
        public const int RelicMinStage = 8;

        public static bool CanDropRelic(int stage) => stage >= RelicMinStage || TestMode;
        public const int EchoCooldownTurns = 5;
        public const int EchoFirstCooldown = 2;
        public const int EchoDashRange = 4;
        public const float EchoDamageRate = 1f; // × 패턴 위력
        public static readonly Color EchoTelegraphColor = new Color(0.3f, 0.95f, 0.88f, 0.45f);

        private const string DefeatedKey = "LoopRogue_KnightDefeated"; // 한 번이라도 잡았는지(히든 유물은 처음 한 번)
        /// <summary>기사가 나타난 층(저장) - 잡든 못 잡든 그 층에선 다시 안 나온다(사용자 결정 10-13). 대장장이 의뢰 중엔 예외.</summary>
        private const string SeenStageKey = "LoopRogue_KnightSeenStage";

        /// <summary>에디터 메뉴 "기억하는 기사 테스트 모드"(EditorPrefs) - 켜면 일반 전투 방마다 기사가 나온다.</summary>
        public const string TestModePrefKey = "LoopRogue_KnightTestMode";

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

        /// <summary>이 전투 방에 기사를 섞을지 - 방을 깔 때 한 번 굴린다.</summary>
        public static bool ShouldSpawn(RoomDefinition def, int stage, System.Random rng)
        {
            if (def == null || def.IsBossRoom || def.IsTestRoom || def.IsTutorialRoom || TestMap.Active)
                return false;
            if (TestMode)
                return true;
            if (stage < MinStage)
                return false;
            if (!Blacksmith.QuestActive && PlayerPrefs.GetInt(SeenStageKey, 0) == stage)
                return false; // 이 층에선 이미 한 번 나왔다
            return rng.NextDouble() < (Blacksmith.QuestActive ? Blacksmith.QuestKnightChance : RoomChance);
        }

        /// <summary>저장 초기화(SaveReset) 직후 - 이번 실행에서 잡은 층 기록도 비운다.</summary>
        public static void Reload()
        {
            // 저장은 SaveReset이 통째로 지운다 - 메모리에 들고 있는 값이 없어서 할 일 없음(호출부 호환용)
        }

        /// <summary>기사가 실제로 나타났다(방을 비운 순간) - 이 층 기회는 끝.</summary>
        public static void MarkAppeared(int stage)
        {
            PlayerPrefs.SetInt(SeenStageKey, stage);
            PlayerPrefs.Save();
        }

        /// <summary>대장장이 의뢰를 받은 직후 - 이 층에서 이미 잡았어도 다시 나올 수 있게.</summary>
        public static void AllowAgainThisStage()
        {
            PlayerPrefs.DeleteKey(SeenStageKey);
            PlayerPrefs.Save();
        }

        public static bool EverDefeated => PlayerPrefs.GetInt(DefeatedKey, 0) == 1;

        /// <summary>기사를 잡았다 - 처음이면 true(히든 유물).</summary>
        public static bool MarkDefeated(int stage)
        {
            if (EverDefeated)
                return false;
            PlayerPrefs.SetInt(DefeatedKey, 1);
            PlayerPrefs.Save();
            return true;
        }

        public static string Name => Loc.T("기억하는 기사");

        private static readonly System.Random Rng = new System.Random();

        private static string Pick(params string[] lines) => lines[Rng.Next(lines.Length)];

        /// <summary>기사의 말 한 줄(메시지 창용).</summary>
        public static string Say(string line) => Loc.F("<color=#7FE0D8>{0}</color>: \"{1}\"", Name, line);

        public static string AppearLine() => EverDefeated
            ? Pick(Loc.T("...또 너로군. 지난 고리에서도 내 검을 꺾었지."),
                Loc.T("기억한다. 네 검 끝이 어디로 가는지도."),
                Loc.T("이번 고리에서도 만났군. 피할 수 없는 사이인가 보다."))
            : Pick(Loc.T("...너도 기억하는 자인가. 그렇다면 칼을 뽑아라."),
                Loc.T("몇 번째 고리인지 세다가 잊었다. 너는 몇 번째냐?"),
                Loc.T("다른 놈들은 잊는다. 나는 잊지 못한다. 그래서 여기 서 있지."));

        public static string ReadLine(AttackStyle style) => style switch
        {
            AttackStyle.Dash => Pick(Loc.T("또 그 돌진인가. 보인다."), Loc.T("같은 길로 두 번 달려오는 자는 없다. 너 말고는.")),
            AttackStyle.Spin => Pick(Loc.T("빙글빙글 도는 칼은 한 번 보면 충분하다."), Loc.T("회전이 느려졌군.")),
            _ => Pick(Loc.T("같은 칼질이다. 읽었다."), Loc.T("단조롭군. 고리를 너무 오래 돌았나?")),
        };

        public static string SlashLine() => Pick(Loc.T("물러나라. 아니면 받아라."), Loc.T("이 일격은 몇 번이고 연습했다."), Loc.T("거기 서 있으면 베인다."));

        public static string EchoLine(AttackStyle style) => style == AttackStyle.Dash
            ? Pick(Loc.T("네 돌진, 기억해 뒀다. 이번엔 내 차례다."), Loc.T("그렇게 달려들었지. 이렇게."))
            : Pick(Loc.T("네 회전 베기다. 돌려주마."), Loc.T("빙글빙글... 이렇게 하는 거였나?"));

        public static string RetreatLine() => Pick(Loc.T("...아직이다. 숨을 고르마."), Loc.T("이대로 쓰러질 순 없다. 잠시 물러난다."));

        public static string DeathLine(bool first) => first
            ? Loc.T("...드디어 끝인가. 이 기억을... 네가 가져가라.")
            : Pick(Loc.T("다음 고리에서... 다시 보자."), Loc.T("또 졌군. 그래도 잊지는 않겠다."));
    }
}
