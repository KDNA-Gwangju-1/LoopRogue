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
            SfxPlayer.Play(Sfx.Appear); // 남은 할 일은 안내 상자가 바로 바꿔 보여 준다
        }

        /// <summary>출구를 밟았는데 아직 안 써 본 아이템이 있으면 막고 true.</summary>
        public static bool BlocksExit(RoomController room)
        {
            if (Active && Tutorial.BlocksAct(Tutorial.Act.Exit, out var why))
            {
                room.ShowMessage(why); // 기본 조작 안내를 다 해 봐야 나간다
                return true;
            }
            if (!Active || PendingItems.Count == 0)
                return false;
            room.ShowMessage(Loc.F("<color=#FFB060>아직 안 써 본 아이템이 있어요:</color> {0}", PendingList()));
            return true;
        }
        // ---- 안내에 없는 행동 막기(10-20 사용자) - 방마다 배우는 것까지만: ①② 이동·공격·대기 ③ + 대시·회전 베기 ④ + 폭탄 ⑤~⑦ + 그 방 아이템 3개 ----

        /// <summary>튜토리얼 스테이지의 지금 방 번호(1~7), 아니면 0 - 안내 상자(Tutorial)가 방 제목·할 일을 띄우는 데 쓴다.</summary>
        public static int StepOf(RoomController room) => Step(room);

        /// <summary>안내 상자 제목 - "튜토리얼 3/7  스킬".</summary>
        public static string BoxTitle(int step) => Loc.F("<color=#FFD966>튜토리얼 {0}/{1}</color>  {2}", step, RoomCount, RoomName(step));

        private static string RoomName(int step) => step switch
        {
            1 => Loc.T("첫걸음"),
            2 => Loc.T("공격"),
            3 => Loc.T("스킬"),
            4 => Loc.T("위험 피하기"),
            5 => Loc.T("아이템 ① 설치"),
            6 => Loc.T("아이템 ② 전투 보조"),
            _ => Loc.T("아이템 ③ 위기 대처"),
        };

        /// <summary>기본 조작 안내가 다 끝난 뒤 상자 본문 - 그 방에서 할 일. 아이템 방은 남은 아이템과 다음 아이템 설명.</summary>
        public static string BoxBody(RoomController room)
        {
            var step = Step(room);
            if (RoomItems.ContainsKey(step))
            {
                if (PendingItems.Count == 0)
                    return Loc.T("<color=#8CE08C>아이템을 다 써 봤어요!</color> 몹을 정리하고 출구로 가세요.");
                var next = PendingItems[0];
                return Loc.F("남은 아이템: {0}  (숫자 키, 놓거나 던지는 건 그다음 방향키)\n<color=#FFD966>{1}</color> - {2}",
                    PendingList(), ItemInfo.Name(next), ItemInfo.Description(next));
            }
            return step switch
            {
                1 => Loc.T("출구(문)까지 걸어가 보세요."),
                2 => Loc.T("슬라임 쪽으로 이동 키를 누르면 공격해요. 몹을 다 잡으면 출구가 열려요."),
                3 => Loc.T("대시와 회전 베기로 몹을 정리하고 출구로 가세요."),
                4 => PendingItems.Count > 0
                    ? Loc.T("빨간 칸은 다음 턴에 공격이 오는 곳 - 비켜서 피하세요.\n숫자 키로 <color=#FFD966>폭탄</color>을 고른 뒤 방향키로 던져 보세요.")
                    : Loc.T("빨간 칸은 다음 턴에 공격이 오는 곳 - 비켜서 피하세요. 몹을 정리하고 출구로."),
                _ => "",
            };
        }

        private static int Step(RoomController room) =>
            Active && room != null && room.CurrentDefinition != null && room.CurrentDefinition.IsTutorialRoom ? room.CurrentDefinition.TutorialStep : 0;

        /// <summary>대시·회전 베기를 아직 안 배운 방(①②)이면 false - 기본 조작 안내 상자도 이걸 보고 대시·회전 베기 단계를 미룬다.</summary>
        public static bool SkillsAllowed(RoomController room)
        {
            var step = Step(room);
            return step == 0 || step >= 3;
        }

        /// <summary>행동 하나를 하려 한다 - 지금 안내(기본 조작 단계 → 방 안내)에 없는 행동이면 안내를 띄우고 true.
        /// 1) 기본 조작 안내가 떠 있으면 그 행동만(Tutorial.BlocksAct) 2) 방마다 아직 안 배운 것(①② 스킬, 아이템은 그 방 것만).</summary>
        public static bool Blocks(Tutorial.Act act, RoomController room, ItemType item = default)
        {
            if (!Active || room == null)
                return false;
            if (Tutorial.BlocksAct(act, out var why))
            {
                room.ShowMessage(why);
                return true;
            }
            // ④~⑦ - 안내된 아이템(폭탄·그 방 3개)을 다 써 보기 전엔 공격·스킬로 몹을 먼저 잡지 않게(이동·대기는 된다 - 빨간 칸 피하기)
            if (PendingItems.Count > 0 && (act == Tutorial.Act.Attack || act == Tutorial.Act.Dash || act == Tutorial.Act.Spin))
            {
                room.ShowMessage(Loc.F("<color=#FFB060>먼저 아이템을 써 봐요:</color> {0}", PendingList()));
                return true;
            }
            return act switch
            {
                Tutorial.Act.Dash or Tutorial.Act.Spin => BlocksSkill(room),
                Tutorial.Act.Item => BlocksItem(item, room),
                _ => false,
            };
        }

        private static bool BlocksSkill(RoomController room)
        {
            if (SkillsAllowed(room))
                return false;
            room.ShowMessage(Step(room) == 1
                ? Loc.T("<color=#FFB060>지금은 움직여 보는 방이에요</color> - 방향키로 걸어서 출구(문)로")
                : Loc.T("<color=#FFB060>지금은 공격을 배우는 방이에요</color> - 슬라임 쪽으로 이동 키를 눌러 부딪혀 보세요. 스킬은 다음 방에서"));
            return true;
        }

        /// <summary>아이템을 고르려 했다 - 이 방 안내에 없는 아이템이면 안내를 띄우고 true(④는 폭탄만, ⑤~⑦은 그 방 3개만, ①~③은 없음).</summary>
        private static bool BlocksItem(ItemType type, RoomController room)
        {
            var step = Step(room);
            if (step == 0)
                return false;
            if (step == 4)
            {
                if (type == ItemType.Bomb)
                    return false;
                room.ShowMessage(Loc.T("<color=#FFB060>이 방에선 폭탄만 써 봐요</color> - 다른 아이템은 다음 방들에서 하나씩"));
                return true;
            }
            if (RoomItems.TryGetValue(step, out var items))
            {
                if (System.Array.IndexOf(items, type) >= 0)
                    return false;
                room.ShowMessage(Loc.F("<color=#FFB060>이 방에선 이 아이템들을 써 봐요:</color> {0}",
                    string.Join(", ", System.Array.ConvertAll(items, ItemInfo.Name))));
                return true;
            }
            room.ShowMessage(Loc.T("<color=#FFB060>아이템은 조금 뒤 방에서 배워요</color> - 지금은 안내대로 해 보세요"));
            return true;
        }

        private const float EnemyHealth = 1f; // 튜토리얼 몹은 한 대에 쓰러진다(10-20 사용자 - 체력 1 고정, RoomController.SpawnEnemy도 종류 배율 무시)
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
            Tutorial.ResetAll(); // 기본 조작 안내를 처음부터 - 예전에 끝냈거나 건너뛴 기록이 남아 있으면 안내 단계 잠금이 아예 안 걸렸다
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>트레일러 촬영 모드 - 첫 Main은 튜토리얼 7방으로(안내 상자 포함). 끝나면 로비로 가고, 로비에서 던전에 들어가면 보스 러시.</summary>
        public static bool TryBeginForTrailer()
        {
            if (!BossRush.Trailer || BossRush.TrailerTutorialStarted)
                return false;
            BossRush.TrailerTutorialStarted = true;
            _fromTitle = false;
            Active = true;
            Tutorial.ResetAll();
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
            if (!BossRush.Trailer)
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
            if (BossRush.Trailer)
                UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby"); // 트레일러 - 튜토리얼 다음은 로비, 던전에 들어가면 보스 러시
            else if (_fromTitle)
                UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
            else
                GameBootstrap.RebuildInPlace(); // 같은 Main 씬에서 진짜 1층을 새로 짓는다
        }

        /// <summary>방 4는 폭탄, 방 5~7은 아이템 3개를 쥐여 주고 시작(퀵슬롯 1·2·3). 아이템 방은 쓰는 법을 한 줄씩 안내.</summary>
        public static void OnRoomEntered(RoomDefinition def, RoomController room)
        {
            PendingItems.Clear();
            if (def.TutorialStep == 4)
            {
                if (!Inventory.Has(ItemType.Bomb))
                    Inventory.Give(ItemType.Bomb);
                Inventory.SetQuickSlot(0, ItemType.Bomb); // 안내대로 1번에
                PendingItems.Add(ItemType.Bomb); // 폭탄도 던져 봐야 출구로(안내 상자가 남은 할 일을 보여 준다)
            }
            if (!RoomItems.TryGetValue(def.TutorialStep, out var items))
                return;
            for (var i = 0; i < items.Length; i++)
            {
                Inventory.Give(items[i]);
                Inventory.SetQuickSlot(i, items[i]); // 원래 퀵슬롯 자리와 상관없이 1·2·3에
                PendingItems.Add(items[i]);
            }
            // 아이템 설명은 아래 안내 상자에(위쪽 줄로 따로 띄우면 안내가 두 군데서 나왔다)
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
            rooms.Add(r3);

            // ④ 위험 피하기 - 같은 줄 멀리 오크 돌격병(돌진할 줄을 빨갛게 예고) + 폭탄 아이템
            var l4 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l4.EnemyPositions.Add(new Vector2Int(6, 4));
            for (var y = 0; y < 9; y++)
                if (y < 2 || y > 6)
                    l4.Walls.Add(new Vector2Int(8, y)); // 뒤쪽 기둥 - 돌격병이 너무 멀리 도망 다니지 않게
            var r4 = Room(4, l4, EnemyKind.Charger);
            rooms.Add(r4);

            // ⑤ 아이템 - 설치(횃불·덫·미끼): 멀리서 다가오는 슬라임 둘 - 덫을 밟게 하고, 미끼로 시선을 돌린다
            var l5 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l5.EnemyPositions.Add(new Vector2Int(8, 2));
            l5.EnemyPositions.Add(new Vector2Int(8, 6));
            var r5 = Room(5, l5, EnemyKind.Melee, EnemyKind.Melee);
            rooms.Add(r5);

            // ⑥ 아이템 - 전투 보조(연막탄·약점 표식·보호막): 가까이 모인 슬라임 셋
            var l6 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(2, 4) };
            l6.EnemyPositions.Add(new Vector2Int(5, 3));
            l6.EnemyPositions.Add(new Vector2Int(5, 5));
            l6.EnemyPositions.Add(new Vector2Int(6, 4));
            var r6 = Room(6, l6, EnemyKind.Melee, EnemyKind.Melee, EnemyKind.Melee);
            rooms.Add(r6);

            // ⑦ 아이템 - 위기 대처(정화제·섬광탄·반사 부적): 거미줄을 쏘는 거미(정화제) + 슬라임
            var l7 = new RoomLayout { Width = 11, Height = 9, PlayerStart = new Vector2Int(1, 4) };
            l7.EnemyPositions.Add(new Vector2Int(5, 4));
            l7.EnemyPositions.Add(new Vector2Int(8, 6));
            var r7 = Room(7, l7, EnemyKind.Spider, EnemyKind.Melee);
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
