using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방 순서 진행 + 루프 리셋의 핵심 - 사망 시 방 인덱스만 0으로 되돌리고, 레벨/스탯/
    /// 경험치(LevelSystem, CharacterStats)는 절대 건드리지 않는다. "환생 시스템과 다르게 스탯은
    /// 유지한 채 위치만 초기화"라는 원래 요청이 그대로 이 메서드 하나(OnPlayerDied)에 들어있다.
    /// 스테이지(StageProgress) 진행은 이것과 완전히 별개 축이다 - 죽어서 도는 루프는 지금 스테이지
    /// 안에서만 반복되고, 그 스테이지의 보스를 잡아야만(OnBossDefeated) 다음 스테이지로 넘어간다.</summary>
    public class LoopManager
    {
        private List<RoomDefinition> _rooms;
        private int _roomIndex;
        private int _stage;
        public int LoopCount { get; private set; } = 1;

        private RoomController _roomController;
        private PlayerActor _player;
        private GameHUD _hud;

        /// <summary>데스 패널티 - 죽거나(Esc로) 중도 포기하면 "이번 시도에서 번 골드"의 이 비율을 잃는다. 이전에
        /// 모아둔 골드/레벨/스탯/장비는 그대로. 봇 테스트에서 죽을 때마다 번 골드를 전부 챙겨 가 매 판 영원 장비
        /// 풀세트가 되고, 많이 죽은 다음 스테이지가 쉬워지는 문제가 있어서 넣었다.</summary>
        public const float DeathGoldPenaltyRate = 0.4f;

        private int _attemptStartGold; // 이번 시도(Main 입장 또는 방1 재시작) 시작 시점의 골드

        /// <summary>방 이벤트는 시도마다 스테이지당 한 방 - 방 3~9 중 랜덤(방 1~2는 아직 몸풀기, 방 10은 보스 직전).</summary>
        private const int FirstEventRoomIndex = 2; // 방 3
        private const int LastEventRoomIndex = 8;  // 방 9
        private static readonly System.Random Rng = new System.Random();

        public void Initialize(RoomController roomController, PlayerActor player, GameHUD hud,
            List<RoomDefinition> rooms, int stage)
        {
            _roomController = roomController;
            _player = player;
            _hud = hud;
            _rooms = rooms;
            for (var i = 0; i < rooms.Count; i++)
                rooms[i].RoomNumber = i + 1;
            _stage = stage;
            _roomIndex = 0;
            RollRoomEvent();
            StageProgress.BeginAttempt();
            Curses.Reset(); // 저주 계약은 시도 단위
            RunBuffs.Reset(); // 방 이벤트 효과(숫돌·피의 제단·모래시계)도
            Codex.SyncOwned();    // 도감이 생기기 전 저장으로 이미 가진 유물/아이템
            Achievements.Check(); // 업적이 생기기 전 저장으로 이미 조건을 채운 것들

            _roomController.Initialize(player, this, stage);
            _player.OnAttemptStarted();
            _roomController.LoadRoom(_rooms[_roomIndex]);
            RefreshHudProgress();
            _attemptStartGold = GoldWallet.Gold;

            // 시작 방 고르기(사람만) - 방1은 이미 깔아둔 채로 입력만 잠그고, 다른 방을 고르면 그 방으로 바꿔 로드.
            if (!GameHUD.AutoPlayActive && !TestMap.Active)
            {
                _roomController.IsInputLocked = true;
                _hud.ShowRoomSelect(stage, _rooms, 0, StartFromSelectedRoom);
            }

            // Esc 메뉴 - 사망/스테이지 클리어(입력 잠금 중)나 레벨업 카드 선택 중에는 안 열린다.
            // 중도 포기도 사망과 같은 데스 패널티(안 그러면 죽기 직전에 Esc로 빠져나가 패널티를 피할 수 있다).
            _hud.gameObject.AddComponent<PauseMenu>().Setup(RetreatToLobby,
                () => !_roomController.IsInputLocked && !_player.Levels.IsChoosingUpgrade && !_player.Stats.IsDead && !InventoryUI.IsOpen
                      && !NpcDialogUI.BlocksInput, // 대화창을 Esc로 닫은 그 프레임에 메뉴가 바로 열리지 않게
                lobbyLabel: Loc.F("로비로 이동 (이번 시도 골드 {0:0}% 손실)  [L]", Relics.DeathPenaltyRate * 100f),
                goToTitle: RetreatToTitle);
        }

        private void StartFromSelectedRoom(int index)
        {
            if (index == _roomIndex)
            {
                _roomController.IsInputLocked = false;
                return;
            }
            _roomIndex = index;
            _roomController.LoadRoom(_rooms[_roomIndex]); // IsInputLocked를 다시 false로 풀어준다.
            RefreshHudProgress();
        }

        public void AdvanceToNextRoom()
        {
            _roomIndex++;
            _roomController.LoadRoom(_rooms[_roomIndex]);
            RefreshHudProgress();
        }

        /// <summary>죽는 순간 바로 리셋하지 않고 먼저 선택지를 띄운다("계속하기" vs "로비로 이동") -
        /// 로비에서 그동안 번 골드로 가챠를 뽑고 싶을 수 있으니, 죽었을 때 바로 다음 시도로 넘어가지
        /// 않고 판단할 틈을 준다. 입력은 그 선택 전까지 잠가둔다.</summary>
        public void OnPlayerDied()
        {
            _roomController.IsInputLocked = true;
            LoopRecord.AddDeath(); // 누적 회차 - NPC 대사가 바뀐다
            Inventory.LoseAll(); // 죽으면 들고 있던 아이템 전부 잃음(사용자 결정)
            var penalty = ApplyDeathGoldPenalty();
            _hud.ShowDeathChoice(ContinueAfterDeath, ReturnToLobby, penalty, _rooms, _roomIndex, // 기본값 = 죽은 방
                bossRoom: _rooms[_roomIndex].IsBossRoom); // 보스방이면 노인의 한마디도 보스 조언 위주로
        }

        /// <summary>이번 시도에서 번 골드의 DeathGoldPenaltyRate만큼 뺏고, 뺏은 양을 돌려준다(표시용).</summary>
        private int ApplyDeathGoldPenalty()
        {
            var earned = Mathf.Max(0, GoldWallet.Gold - _attemptStartGold);
            var penalty = Mathf.RoundToInt(earned * Relics.DeathPenaltyRate); // 유물 "탐욕"이면 60%
            GoldWallet.TrySpend(penalty);
            _attemptStartGold = GoldWallet.Gold;
            return penalty;
        }

        private void RetreatToLobby()
        {
            ApplyDeathGoldPenalty();
            Inventory.LoseAll(); // 중도 포기도 사망과 같다
            SaveProgressAndLoadLobby();
        }

        /// <summary>Esc 메뉴 → 타이틀. 중도 포기라 로비 이동과 같은 데스 패널티, 런 진행은 저장해서 다시 시작하면 이어진다.</summary>
        private void RetreatToTitle()
        {
            ApplyDeathGoldPenalty();
            Inventory.LoseAll();
            SaveProgress();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
        }

        private void ContinueAfterDeath(int startRoom)
        {
            LoopCount++;
            _player.OnAttemptStarted();
            _roomIndex = startRoom;
            RollRoomEvent();
            StageProgress.BeginAttempt();
            Curses.Reset(); // 죽으면 저주도 풀린다
            RunBuffs.Reset();
            _player.Stats.FullHeal();
            _roomController.LoadRoom(_rooms[_roomIndex]); // IsInputLocked를 다시 false로 풀어준다.
            _attemptStartGold = GoldWallet.Gold;
            RefreshHudProgress();
            _hud.ShowLoopResetBanner(startRoom);
        }

        private void ReturnToLobby() => SaveProgressAndLoadLobby();

        /// <summary>이 스테이지의 보스를 잡으면 - 다음 스테이지로 승격시켜두고(StageProgress),
        /// 로비로 돌아가서 그동안 번 골드로 장비를 맞춘 뒤 "시작"을 누르면 그 다음(이미 올라간)
        /// 스테이지로 들어가게 된다. 이 스테이지 자체는 클리어됐으니 더 볼 일이 없어서, 사망 때와
        /// 달리 "계속하기" 선택지 없이 로비 이동 하나뿐이다.</summary>
        public void OnBossDefeated()
        {
            _roomController.IsInputLocked = true;
            StageProgress.AdvanceStage();
            if (_stage >= StageProgress.MaxStage)
                Achievements.Unlock("ACH_CLEAR_10");
            Achievements.Check(); // 층 도달 업적
            // 보스를 처음 잡았으면 유물부터 고른다(보스 전용 1 + 일반 2) → 고른 뒤 스테이지 클리어 창.
            var offer = Relics.MakeOffer(_stage);
            if (offer != null)
                _hud.ShowRelicChoice(offer, picked =>
                {
                    Relics.Acquire(picked, _player);
                    Achievements.Check(); // 유물 개수 업적
                    ShowStageClear();
                });
            else
                ShowStageClear();
        }

        private void ShowStageClear()
        {
            // 마지막 층 - 엔딩을 보여주고 처음으로(도감·업적·칭호만 남김). 자동 플레이 봇은 예전처럼 클리어 창으로(봇이 판 종료로 센다).
            if (_stage >= StageProgress.MaxStage && !GameHUD.AutoPlayActive)
            {
                var record = new EndingScreen.Record
                {
                    Clears = SaveReset.GameClears + 1,
                    TotalDeaths = LoopRecord.TotalDeaths,
                    Level = _player.Levels.Level,
                    CodexFound = Codex.FoundCount(),
                    CodexTotal = Codex.TotalCount(),
                };
                EndingScreen.Show(record, () => !_player.Levels.IsChoosingUpgrade, () =>
                {
                    SaveReset.ResetForNewGame();
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
                });
                return;
            }
            _hud.ShowStageClear(_stage, SkipLobbyAfterStageClear ? SaveProgressAndReloadMain : (System.Action)SaveProgressAndLoadLobby);
        }

        /// <summary>자동 플레이 봇 전용 - 스테이지 클리어 후 로비 씬을 거치지 않고 Main을 그 자리에서 다시 짓는다(쇼핑은 봇이 같은 프레임에
        /// 지갑만 건드려서 처리 - 다시 짓기는 다음 프레임이므로 저장 → 쇼핑 → 새 Main 초기화 순서가 로비를 거칠 때와 같다).</summary>
        public static bool SkipLobbyAfterStageClear;

        private void SaveProgressAndReloadMain()
        {
            SaveProgress();
            GameBootstrap.RebuildInPlace();
        }

        private void SaveProgressAndLoadLobby()
        {
            // 씬이 넘어가면 플레이어 오브젝트 자체가 통째로 사라지므로, 사라지기 직전에 지금
            // 레벨/경험치/스탯을 RunProgress에 스냅샷으로 남겨서 Main이 다시 뜰 때 복원한다.
            // FullHeal도 같이 - 안 그러면 방금 죽은(혹은 보스전 막판 깎인) 체력이 그대로 저장된다.
            SaveProgress();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }

        private void SaveProgress()
        {
            _player.Stats.FullHeal();
            RunProgress.Save(_player.Levels, _player.Stats);
        }

        /// <summary>시도마다 방 이벤트를 새로 뽑는다(사용자 결정): 보물상자(아이템)는 층마다 반드시 1개, 그 밖의 이벤트(회복 샘/축복 제단/
        /// 저주받은 상자)는 OtherEventChance 확률로 1개 - 둘은 서로 다른 방(방 3~9 중).</summary>
        private const float OtherEventChance = 0.75f;

        private void RollRoomEvent()
        {
            foreach (var room in _rooms)
                room.HasEvent = false;

            var last = Mathf.Min(LastEventRoomIndex, _rooms.Count - 2); // 보스방 제외
            if (last < FirstEventRoomIndex)
                return;

            var chestIndex = Rng.Next(FirstEventRoomIndex, last + 1);
            _rooms[chestIndex].HasEvent = true;
            _rooms[chestIndex].EventType = RoomEventType.TreasureChest;

            if (Rng.NextDouble() >= OtherEventChance || last == FirstEventRoomIndex)
                return;
            int otherIndex;
            do
                otherIndex = Rng.Next(FirstEventRoomIndex, last + 1);
            while (otherIndex == chestIndex);
            _rooms[otherIndex].HasEvent = true;
            _rooms[otherIndex].EventType = RoomEventActor.RollNonChestType();
        }

        public void ShowMessage(string message) => _hud.ShowMessage(message);

        private void RefreshHudProgress() =>
            _hud.RefreshProgress(_stage, _roomIndex + 1, _rooms.Count, LoopCount);
    }
}
