using UnityEngine;

namespace LoopRogue
{
    /// <summary>지금 도전 중인 스테이지(1~MaxStage) - 보스를 잡아야만 다음 스테이지로 넘어간다
    /// ("스테이지를 10개 만들어서 보스 클리어하면 로비로, 그다음 스테이지로 이동" 요청). 죽는 건
    /// 스테이지 진행에 영향이 없다 - 같은 스테이지 안에서 방1로 리셋될 뿐(레벨/스탯이 유지되는
    /// 것과 같은 원칙, LoopManager.OnPlayerDied 참고). 골드/장비처럼 PlayerPrefs로 영구 저장.</summary>
    public static class StageProgress
    {
        public const int MaxStage = 10;
        private const string StageKey = "LoopRogue_CurrentStage";
        private const string AttemptsKey = "LoopRogue_StageAttempts";
        private const string RoomsClearedKey = "LoopRogue_StageRoomsCleared";

        /// <summary>반복 보상 감소 - 같은 스테이지를 다시 시도할수록 일반 몹/방 클리어 골드·경험치가 이 비율로 줄고
        /// (2번째 90%, 3번째 81%...), RepeatRewardFloor 밑으로는 안 내려간다. 어려운 스테이지에서 수십 번 죽으며
        /// 파밍한 성장으로 다음 스테이지가 공짜가 되던 "쉬운 골짜기"(봇 테스트에서 매번 발생) 대책.
        /// 보스 처치/방 이벤트 보상은 안 줄이고, 다음 스테이지로 가면 초기화된다.</summary>
        public const float RepeatRewardDecay = 0.9f;
        public const float RepeatRewardFloor = 0.5f;

        private static int _attempts;
        private static int _roomsCleared;

        /// <summary>이 층에서 지금까지 깬 일반 방 수(방 1부터 연속) - 시작 방 고르기는 깬 방 바로 다음까지만 열린다
        /// (사용자 결정: "이전 스테이지를 클리어해야 열리게" - 처음 들어간 층은 방 1만, 방 10을 깨면 보스방까지).
        /// 다음 층으로 넘어가면 0. 죽어도 줄지 않는다.</summary>
        public static int RoomsCleared
        {
            get
            {
                EnsureLoaded();
                return _roomsCleared;
            }
        }

        /// <summary>시작 방 고르기에서 고를 수 있는 칸 수(앞에서부터) - 깬 방 수 + 1, 최대 전체 칸 수.</summary>
        public static int UnlockedRoomCount(int roomCount) => Mathf.Clamp(RoomsCleared + 1, 1, roomCount);

        /// <summary>일반 방 roomNumber(1부터)를 깼다 - 처음 깬 방이면 다음 방이 열린다.</summary>
        public static void MarkRoomCleared(int roomNumber)
        {
            EnsureLoaded();
            if (roomNumber <= _roomsCleared)
                return;
            _roomsCleared = roomNumber;
            PlayerPrefs.SetInt(RoomsClearedKey, _roomsCleared);
            PlayerPrefs.Save();
        }

        private static bool _loaded;

        /// <summary>저장 초기화(SaveReset) 직후 메모리에 들고 있던 값을 버리고 PlayerPrefs에서 다시 읽는다.</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }
        private static int _stage;

        public static int CurrentStage
        {
            get
            {
                EnsureLoaded();
                return _stage;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _stage = PlayerPrefs.GetInt(StageKey, 1);
            _attempts = PlayerPrefs.GetInt(AttemptsKey, 0);
            _roomsCleared = PlayerPrefs.GetInt(RoomsClearedKey, 0);
            _loaded = true;
        }

        /// <summary>MaxStage에서 더 안 올라간다 - 마지막 스테이지 보스를 계속 다시 잡아도 그냥
        /// 그 스테이지가 반복될 뿐(2주 프로토타입 범위 - "진짜 엔딩" 연출은 나중에).</summary>
        public static void AdvanceStage()
        {
            EnsureLoaded();
            _stage = Mathf.Min(_stage + 1, MaxStage);
            _attempts = 0;
            _roomsCleared = 0;
            PlayerPrefs.SetInt(StageKey, _stage);
            PlayerPrefs.SetInt(AttemptsKey, _attempts);
            PlayerPrefs.SetInt(RoomsClearedKey, _roomsCleared);
            PlayerPrefs.Save();
        }

        /// <summary>지금 스테이지를 몇 번째 시도 중인지(1부터). 로비를 오가도, Play를 껐다 켜도 유지된다.</summary>
        public static int AttemptsThisStage
        {
            get
            {
                EnsureLoaded();
                return Mathf.Max(1, _attempts);
            }
        }

        /// <summary>새 시도 시작(Main 입장 또는 사망 후 방1부터 재시작) - LoopManager가 부른다.</summary>
        public static void BeginAttempt()
        {
            EnsureLoaded();
            _attempts++;
            PlayerPrefs.SetInt(AttemptsKey, _attempts);
            PlayerPrefs.Save();
        }

        /// <summary>이번 시도의 일반 몹/방 클리어 보상 배율.</summary>
        public static float RepeatRewardMultiplier =>
            Mathf.Max(RepeatRewardFloor, Mathf.Pow(RepeatRewardDecay, AttemptsThisStage - 1));
    }
}
