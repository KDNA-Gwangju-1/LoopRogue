using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>튜토리얼 스테이지 - 죽을 일 없는 작은 방 7개(① 이동·출구 ② 공격 ③ 대시·회전 베기 ④ 빨간 예고 칸·폭탄
    /// ⑤~⑦ 아이템 3개씩 - 10종을 전부 한 번씩 써 봐야 출구로 나갈 수 있다(10-14 사용자)).
    /// (아래 설명은 4방 시절 그대로)
    /// 처음 시작한 사람(저장된 런·사망 기록 없음)이 던전에 들어가면 자동으로 한 번, 타이틀 [튜토리얼] 버튼으로는 언제든.
    /// 화면 아래 안내 상자(Tutorial)가 방마다 할 일을 알려 준다. 플레이어 체력은 1 밑으로 안 떨어지고(CharacterStats),
    /// 몹은 골드·경험치를 안 주며, 층 진행·방 클리어 기록·이벤트·퍼즐·오류방·히든 몹과 무관하다. 마지막 방 출구로 나가면
    /// 처음 온 사람은 그대로 진짜 1층으로, 타이틀에서 다시 본 사람은 타이틀로.</summary>
    public static class TutorialStage
    {
        public const string DoneKey = "LoopRogue_TutorialStageDone";
        public const string RewardKey = "LoopRogue_TutorialRewardGiven"; // 완료 보상은 한 번만(타이틀에서 다시 해도 또 안 줌)
        public const int RewardGold = 100;
        private const float CompleteDelaySeconds = 2.8f;
        public const int RoomCount = 7;

        // ⑤~⑦ 아이템 방 - 방에 들어가면 이 3개를 퀵슬롯 1·2·3에 넣어 주고, 다 써 봐야 출구가 통과된다(RoomController.TryUseExit).
        private static readonly Dictionary<int, ItemType[]> RoomItems = new Dictionary<int, ItemType[]>
        {
            { 5, new[] { ItemType.Torch, ItemType.Trap, ItemType.Decoy } },
            { 6, new[] { ItemType.Smoke, ItemType.Weakness, ItemType.Barrier } },
            { 7, new[] { ItemType.Cleanse, ItemType.Flash, ItemType.Reflect } },
        };
        private static readonly List<ItemType> PendingItems = new List<ItemType>();

        private static string PendingList() => string.Join(", ", PendingItems.ConvertAll(ItemInfo.Name));

        /// <summary>아이템을 썼다(PlayerActor.TryUseItem) - 이 방에서 써 봐야 할 것이면 지우고 남은 것을 알린다.</summary>
        public static void NotifyItemUsed(ItemType type, RoomController room)
        {
            if (!Active || !PendingItems.Remove(type))
                return;
            room.ShowMessage(PendingItems.Count > 0
                ? Loc.F("좋아요! 남은 아이템: {0}", PendingList())
                : Loc.T("<color=#8CE08C>아이템을 다 써 봤어요!</color> 몹을 정리하고 출구로"));
        }

        /// <summary>출구를 밟았는데 아직 안 써 본 아이템이 있으면 막고 true.</summary>
        public static bool BlocksExit(RoomController room)
        {
            if (!Active || PendingItems.Count == 0)
                return false;
            room.ShowMessage(Loc.F("<color=#FFB060>아직 안 써 본 아이템이 있어요:</color> {0}", PendingList()));
            return true;
        }
        private const float EnemyHealth = 6f;
        private const float EnemyAttack = 1f;

        public static bool Active { get; private set; }
        private static bool _requestedFromTitle;
        private static bool _fromTitle;

        public static bool Done => PlayerPrefs.GetInt(DoneKey, 0) == 1;

        /// <summary>타이틀 [튜토리얼] - 다음 Main 진입을 튜토리얼로(안내 상자도 처음부터).</summary>
        public static void RequestFromTitle()
        {
            _requestedFromTitle = true;
            Tutorial.ResetAll();
        }

        /// <summary>GameBootstrap.Build - 이번 Main을 튜토리얼로 지을지 정한다(정하면 바로 "본 것"으로 기록 - 중간에 나가도 다시 강제하지 않는다).</summary>
        public static bool TryBegin()
        {
            Active = false;
            if (GameHUD.AutoPlayActive || TestMap.Active || PuzzleTrial.Active)
            {
                _requestedFromTitle = false;
                return false;
            }
            var firstTime = !Done && !RunProgress.HasSavedRun && LoopRecord.TotalDeaths == 0;
            if (!_requestedFromTitle && !firstTime)
                return false;
            _fromTitle = _requestedFromTitle;
            _requestedFromTitle = false;
            Active = true;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>마지막 방 출구를 밟았다(LoopManager.AdvanceToNextRoom) - "튜토리얼 완료" 알림 + 처음이면 골드, 잠깐 보여준 뒤 넘어간다.</summary>
        public static void Complete(RoomController room)
        {
            var firstReward = PlayerPrefs.GetInt(RewardKey, 0) == 0;
            if (firstReward)
            {
                GoldWallet.Add(RewardGold);
                PlayerPrefs.SetInt(RewardKey, 1);
                PlayerPrefs.Save();
            }
            room.ShowMessage(firstReward
                ? Loc.F("<color=#FFD966>튜토리얼 완료!</color>  골드 +{0}", RewardGold)
                : Loc.T("<color=#FFD966>튜토리얼 완료!</color>"));
            room.ShowMessage(_fromTitle ? Loc.T("잠시 후 타이틀로 돌아갑니다") : Loc.T("이제 진짜 던전으로 내려갑니다..."));
            if (room.Player != null)
                HitFeedback.OnBonusReward(room.Player);
            room.StartCoroutine(FinishAfterDelay());
        }

        private static IEnumerator FinishAfterDelay()
        {
            yield return new WaitForSecondsRealtime(CompleteDelaySeconds);
            Finish();
        }

        private static void Finish()
        {
            Active = false;
            Inventory.LoseAll();
            if (_fromTitle)
                UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            else
                GameBootstrap.RebuildInPlace(); // 같은 Main 씬에서 진짜 1층을 새로 짓는다
        }

        /// <summary>방 입장 때 위쪽 안내 줄에 띄우는 제목.</summary>
        public static string Caption(int step) => step switch
        {
            1 => Loc.T("<color=#FFD966>튜토리얼 1/7</color>  첫걸음 - 움직여 보고 출구(문)로"),
            2 => Loc.T("<color=#FFD966>튜토리얼 2/7</color>  공격 - 슬라임에게 부딪혀 보세요"),
            3 => Loc.T("<color=#FFD966>튜토리얼 3/7</color>  스킬 - 대시와 회전 베기"),
            4 => Loc.T("<color=#FFD966>튜토리얼 4/7</color>  위험 피하기 - 빨간 칸에서 비키고, 폭탄도 던져 보세요"),
            5 => Loc.T("<color=#FFD966>튜토리얼 5/7</color>  아이템 ① 설치 - 횃불·덫·미끼를 놓아 보세요"),
            6 => Loc.T("<color=#FFD966>튜토리얼 6/7</color>  아이템 ② 전투 보조 - 연막탄·약점 표식·보호막"),
            _ => Loc.T("<color=#FFD966>튜토리얼 7/7</color>  아이템 ③ 위기 대처 - 정화제·섬광탄·반사 부적"),
        };

        /// <summary>방 4는 폭탄, 방 5~7은 아이템 3개를 쥐여 주고 시작(퀵슬롯 1·2·3). 아이템 방은 쓰는 법을 한 줄씩 안내.</summary>
        public static void OnRoomEntered(RoomDefinition def, RoomController room)
        {
            PendingItems.Clear();
            if (def.TutorialStep == 4 && !Inventory.Has(ItemType.Bomb))
                Inventory.Give(ItemType.Bomb);
            if (!RoomItems.TryGetValue(def.TutorialStep, out var items))
                return;
            for (var i = 0; i < items.Length; i++)
            {
                Inventory.Give(items[i]);
                Inventory.SetQuickSlot(i, items[i]); // 원래 퀵슬롯 자리와 상관없이 1·2·3에
                PendingItems.Add(items[i]);
                room.ShowMessage($"[{i + 1}] <color=#FFD966>{ItemInfo.Name(items[i])}</color> - {ItemInfo.Description(items[i])}");
            }
            room.ShowMessage(Loc.T("숫자 키로 아이템을 써요 - 놓거나 던지는 아이템은 숫자를 누른 뒤 방향키로. 세 개를 다 써 봐야 출구로 나갈 수 있어요"));
        }

        public static List<RoomDefinition> BuildRooms()
        {
            var rooms = new List<RoomDefinition>();

            // ① 첫걸음 - 몹 없음, 들어가자마자 출구가 열린다(이동·대기·능력치 창 안내)
            var l1 = new RoomLayout { Width = 9, Height = 7, PlayerStart = new Vector2Int(1, 3) };
            rooms.Add(Room(1, l1));

            // ② 공격 - 슬라임 한 마리
            var l2 = new RoomLayout { Width = 9, Height = 7, PlayerStart = new Vector2Int(1, 3) };
            l2.EnemyPositions.Add(new Vector2Int(6, 3));
            rooms.Add(Room(2, l2, EnemyKind.Melee));

            // ③ 스킬 - 가까이 둘러싼 셋(회전 베기) + 같은 줄 멀리 하나(대시). 조금 튼튼하게(스킬로 정리하는 맛)
            var l3 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(5, 4) };
            l3.EnemyPositions.Add(new Vector2Int(4, 5));
            l3.EnemyPositions.Add(new Vector2Int(6, 5));
            l3.EnemyPositions.Add(new Vector2Int(5, 6));
            l3.EnemyPositions.Add(new Vector2Int(5, 0));
            var r3 = Room(3, l3, EnemyKind.Melee, EnemyKind.Melee, EnemyKind.Melee, EnemyKind.Melee);
            r3.EnemyMaxHealth = EnemyHealth * 2f;
            rooms.Add(r3);

            // ④ 위험 피하기 - 같은 줄 멀리 오크 돌격병(돌진할 줄을 빨갛게 예고) + 폭탄 아이템
            var l4 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l4.EnemyPositions.Add(new Vector2Int(6, 4));
            for (var y = 0; y < 9; y++)
                if (y < 2 || y > 6)
                    l4.Walls.Add(new Vector2Int(8, y)); // 뒤쪽 기둥 - 돌격병이 너무 멀리 도망 다니지 않게
            var r4 = Room(4, l4, EnemyKind.Charger);
            r4.EnemyMaxHealth = EnemyHealth * 4f;
            rooms.Add(r4);

            // ⑤ 아이템 - 설치(횃불·덫·미끼): 멀리서 다가오는 슬라임 둘 - 덫을 밟게 하고, 미끼로 시선을 돌린다
            var l5 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l5.EnemyPositions.Add(new Vector2Int(8, 2));
            l5.EnemyPositions.Add(new Vector2Int(8, 6));
            var r5 = Room(5, l5, EnemyKind.Melee, EnemyKind.Melee);
            r5.EnemyMaxHealth = EnemyHealth * 2f;
            rooms.Add(r5);

            // ⑥ 아이템 - 전투 보조(연막탄·약점 표식·보호막): 가까이 모인 슬라임 셋
            var l6 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(2, 4) };
            l6.EnemyPositions.Add(new Vector2Int(5, 3));
            l6.EnemyPositions.Add(new Vector2Int(5, 5));
            l6.EnemyPositions.Add(new Vector2Int(6, 4));
            var r6 = Room(6, l6, EnemyKind.Melee, EnemyKind.Melee, EnemyKind.Melee);
            r6.EnemyMaxHealth = EnemyHealth * 3f;
            rooms.Add(r6);

            // ⑦ 아이템 - 위기 대처(정화제·섬광탄·반사 부적): 거미줄을 쏘는 거미(정화제) + 슬라임
            var l7 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l7.EnemyPositions.Add(new Vector2Int(5, 4));
            l7.EnemyPositions.Add(new Vector2Int(8, 6));
            var r7 = Room(7, l7, EnemyKind.Spider, EnemyKind.Melee);
            r7.EnemyMaxHealth = EnemyHealth * 3f;
            rooms.Add(r7);

            return rooms;
        }

        private static RoomDefinition Room(int step, RoomLayout layout, params EnemyKind[] kinds) => new RoomDefinition
        {
            RoomName = $"Tutorial {step}",
            Width = layout.Width,
            Height = layout.Height,
            EnemyCount = kinds.Length,
            EnemyKinds = new List<EnemyKind>(kinds),
            EnemyMaxHealth = EnemyHealth,
            EnemyAttackPower = EnemyAttack,
            IsTutorialRoom = true,
            TutorialStep = step,
            TutorialLayout = layout,
        };
    }
}
