using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>튜토리얼 스테이지 - 죽을 일 없는 작은 방 4개(① 이동·출구 ② 공격 ③ 대시·회전 베기 ④ 빨간 예고 칸·아이템).
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
        public const int RoomCount = 4;
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
            1 => Loc.T("<color=#FFD966>튜토리얼 1/4</color>  첫걸음 - 움직여 보고 출구(계단)로"),
            2 => Loc.T("<color=#FFD966>튜토리얼 2/4</color>  공격 - 슬라임에게 부딪혀 보세요"),
            3 => Loc.T("<color=#FFD966>튜토리얼 3/4</color>  스킬 - 대시와 회전 베기"),
            _ => Loc.T("<color=#FFD966>튜토리얼 4/4</color>  위험 피하기 - 빨간 칸에서 비키고, 아이템도 써 보세요"),
        };

        /// <summary>방 4는 아이템을 하나 쥐여 주고 시작(폭탄 - 퀵슬롯 1에 들어간다).</summary>
        public static void OnRoomEntered(RoomDefinition def)
        {
            if (def.TutorialStep == 4 && !Inventory.Has(ItemType.Bomb))
                Inventory.Give(ItemType.Bomb);
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
