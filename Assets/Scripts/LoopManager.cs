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
            _hud.Room = roomController;
            _rooms = rooms;
            for (var i = 0; i < rooms.Count; i++)
                rooms[i].RoomNumber = i + 1;
            _stage = stage;
            _roomIndex = 0;
            RollRoomEvent();
            if (!TutorialStage.Active)
                StageProgress.BeginAttempt(); // 튜토리얼은 그 층 시도 횟수(반복 보상 감소)에 안 센다
            Curses.Reset(); // 저주 계약은 시도 단위
            RunBuffs.Reset(); // 방 이벤트 효과(숫돌·피의 제단·모래시계)도
            Codex.SyncOwned();    // 도감이 생기기 전 저장으로 이미 가진 유물/아이템
            Achievements.Check(); // 업적이 생기기 전 저장으로 이미 조건을 채운 것들

            _roomController.Initialize(player, this, stage);
            _player.OnAttemptStarted();
            PlayLog.AttemptStarted(stage, _player, TutorialStage.Active ? "튜토리얼" : BossRush.Active ? "보스 러시" : "던전 입장");
            if (GameHUD.AutoPlayActive || TutorialStage.Active)
                EnterRoom(); // 봇은 시작 방 고르기 창이 없어서 첫 방도 여기서 퍼즐 확률을 굴린다(튜토리얼도 고르기 없이 방 1부터)
            else
            {
                PlayLog.SkipNextRoomEntry = true; // 자리 표시용 방은 기록에 "입장"으로 안 남긴다
                _roomController.LoadRoom(_rooms[_roomIndex]); // 사람은 고르기 창 뒤 자리 표시용 - 고른 뒤 EnterRoom
                PlayLog.SkipNextRoomEntry = false;
            }
            RefreshHudProgress();
            _attemptStartGold = GoldWallet.Gold;

            // 시작 방 고르기(사람만) - 방1은 이미 깔아둔 채로 입력만 잠그고, 다른 방을 고르면 그 방으로 바꿔 로드.
            if (ConsumeBossDirect())
            {
                _roomIndex = _rooms.Count - 1; // 에디터 메뉴 "기준 스펙으로 N층 보스방" - 고르기 없이 보스방으로
                EnterRoom();
            }
            else if (PuzzleTrial.Active)
                _roomController.LoadPuzzle(_rooms[_roomIndex], PuzzleTrial.Type.Value); // 에디터 메뉴 "퍼즐·미니게임 체험" - 고르기 없이 바로
            else if (MuralTrial.Active)
            {
                MuralTrial.GiveTorch(); // 에디터 메뉴 "벽화의 방 체험" - 몹 없는 방 1 + 제단 + 횃불
                _roomController.LoadClearedRoom(_rooms[_roomIndex]);
            }
            else if (!GameHUD.AutoPlayActive && !TestMap.Active && !TutorialStage.Active && !BossRush.Active && !MuralTrial.Active)
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

            Tutorial.Attach(_roomController, _player, _hud); // 첫 판 안내(봇·테스트 맵에선 안 만든다)
        }

        /// <summary>에디터 테스트 - 다음 Main 진입을 그 층 보스방에서 바로 시작(한 번만).</summary>
        public const string BossDirectPrefKey = "LoopRogue_Test_BossDirect";

        /// <summary>봇 "N층 보스방 반복" 모드 - 켜져 있는 동안 Main에 들어갈 때마다 보스방에서 시작한다(AutoPlayBot이 켜고 끈다).</summary>
        public static bool BotStartAtBoss;

        private static bool ConsumeBossDirect()
        {
            if (BotStartAtBoss)
                return true;
#if UNITY_EDITOR
            if (GameHUD.AutoPlayActive || !UnityEditor.EditorPrefs.GetBool(BossDirectPrefKey, false))
                return false;
            UnityEditor.EditorPrefs.SetBool(BossDirectPrefKey, false);
            return true;
#else
            return false;
#endif
        }

        private void StartFromSelectedRoom(int index)
        {
            _roomIndex = index;
            EnterRoom(); // 방 1을 골랐어도 다시 깐다 - 퍼즐방 확률을 여기서 굴린다(고르기 창 뒤에 깔아둔 방은 자리 표시용)
        }

        /// <summary>지금 방이 이 층(보스 러시면 러시 전체)의 마지막 방인지 - 트레일러 F9 건너뛰기가 끝을 넘지 않게.</summary>
        public bool IsLastRoom => _roomIndex >= _rooms.Count - 1;

        public void AdvanceToNextRoom()
        {
            if (TutorialStage.Active && _roomIndex + 1 >= _rooms.Count)
            {
                _roomController.IsInputLocked = true;
                TutorialStage.Complete(_roomController); // 완료 알림 + 첫 보상 골드 → 잠시 뒤 진짜 1층(또는 타이틀)로
                SaveProgress();
                return;
            }
            _roomIndex++;
            EnterRoom();
        }

        /// <summary>지금 방(_roomIndex)에 들어간다 - 일반 방이면 PuzzleBase.Chance 확률로 전투 대신 퍼즐·미니게임 10종 중 하나
        /// (사용자 결정). 보스방·이벤트 칸이 있는 방(층마다 보물상자 1개 보장)·테스트 맵은 항상 전투. 자동 플레이 봇은 퍼즐을 풀 줄 몰라서
        /// 퍼즐방이 걸리면 성공으로 치고(카드 1장) 곧바로 다음 방으로 넘어간다(사용자 결정 - 사람과 같은 확률로 방이 빠지게).</summary>
        private void EnterRoom()
        {
            var def = _rooms[_roomIndex];
            var puzzleAllowed = !def.IsBossRoom && !def.IsTestRoom && !def.IsTutorialRoom && !def.HasEvent && !TestMap.Active && !BossRush.Active; // 트레일러 잡몹 방엔 퍼즐 없음
            if (puzzleAllowed && !GameHUD.AutoPlayActive && PuzzleBase.TestModeNext(out var testType))
                _roomController.LoadPuzzle(def, testType);
            else if (puzzleAllowed && RollPuzzleOnce(def.RoomNumber))
            {
                if (GameHUD.AutoPlayActive)
                {
                    _roomController.SkipPuzzleAsCleared(def);
                    _roomIndex++; // 보스방은 퍼즐이 안 나오므로 언젠가 멈춘다
                    EnterRoom();
                    return;
                }
                _roomController.LoadPuzzle(def, (PuzzleType)Rng.Next(PuzzleBase.TypeCount));
            }
            else
                _roomController.LoadRoom(def); // IsInputLocked를 다시 false로 풀어준다.
            RefreshHudProgress();
            if (def.IsTutorialRoom)
            {
                TutorialStage.OnRoomEntered(def, _roomController); // 방 제목·할 일은 아래 안내 상자(Tutorial)가
            }
        }

        /// <summary>이 층에서 이 방을 처음 들어갈 때만 퍼즐 확률을 굴린다 - 두 번째부터는 걸렸든 아니든 항상 전투(StageProgress.PuzzleRolled).</summary>
        private static bool RollPuzzleOnce(int roomNumber)
        {
            if (StageProgress.PuzzleRolled(roomNumber))
                return false;
            StageProgress.MarkPuzzleRolled(roomNumber);
            return Rng.NextDouble() < PuzzleBase.Chance;
        }

        /// <summary>퍼즐 실패 창 "다시 도전" - 같은 종류를 배치만 새로 뽑아 다시.</summary>
        public void RetryPuzzle(PuzzleType type)
        {
            PuzzleHud.SkipRulesNext = true; // 방금 읽은 설명 창은 건너뛴다
            PlayLog.PuzzleChoice(type, retry: true);
            _roomController.LoadPuzzle(_rooms[_roomIndex], type);
        }

        public bool CanReturnToPreviousRoom => _roomIndex > 0;

        /// <summary>퍼즐 실패 창 두 번째 선택지 - 이전 방(이미 깬 방, 출구만 열림)으로 돌아간다. 출구로 다시 오면 퍼즐 확률을 새로 굴린다.
        /// 방 1이라 이전 방이 없으면 평소 전투 방으로.</summary>
        public void LeavePuzzle()
        {
            PlayLog.Note("퍼즐 포기 → 이전 방으로");
            if (!CanReturnToPreviousRoom)
            {
                _roomController.LoadRoom(_rooms[_roomIndex]);
                return;
            }
            _roomIndex--;
            _roomController.LoadClearedRoom(_rooms[_roomIndex]);
            RefreshHudProgress();
        }

        /// <summary>죽는 순간 바로 리셋하지 않고 먼저 선택지를 띄운다("계속하기" vs "로비로 이동") -
        /// 로비에서 그동안 번 골드로 가챠를 뽑고 싶을 수 있으니, 죽었을 때 바로 다음 시도로 넘어가지
        /// 않고 판단할 틈을 준다. 입력은 그 선택 전까지 잠가둔다.</summary>
        public void OnPlayerDied()
        {
            if (BossRush.TrailerInvincible && !_player.Stats.IsDead)
                return; // 트레일러 촬영 - 기믹 즉사(수정 폭발 등)는 죽이지 않으니 사망 처리도 없이 그대로 다음 장면으로(10-26 사용자)
            _roomController.IsInputLocked = true;
            LoopRecord.AddDeath(); // 누적 회차 - NPC 대사가 바뀐다
            StageProgress.AddStageDeath(); // 유물 "되풀이의 각인"
            var penalty = ApplyDeathGoldPenalty();
            PlayLog.Death(_player, _roomController, penalty); // 잃는 아이템까지 적으려고 LoseAll 전에
            Inventory.LoseAll(); // 죽으면 들고 있던 아이템 전부 잃음(사용자 결정)
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
            PlayLog.Retreat("로비로");
            ApplyDeathGoldPenalty();
            Inventory.LoseAll(); // 중도 포기도 사망과 같다
            SaveProgressAndLoadLobby();
        }

        /// <summary>Esc 메뉴 → 타이틀. 중도 포기라 로비 이동과 같은 데스 패널티, 런 진행은 저장해서 다시 시작하면 이어진다.</summary>
        private void RetreatToTitle()
        {
            PlayLog.Retreat("타이틀로");
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
            PlayLog.DeathChoice(false, startRoom);
            PlayLog.AttemptStarted(_stage, _player, "사망 후 계속하기");
            Curses.Reset(); // 죽으면 저주도 풀린다
            RunBuffs.Reset();
            _player.Stats.FullHeal();
            EnterRoom(); // IsInputLocked를 다시 false로 풀어준다.
            _attemptStartGold = GoldWallet.Gold;
            RefreshHudProgress();
            _hud.ShowLoopResetBanner(startRoom);
        }

        private void ReturnToLobby()
        {
            PlayLog.DeathChoice(true, 0);
            SaveProgressAndLoadLobby();
        }

        /// <summary>이 스테이지의 보스를 잡으면 - 다음 스테이지로 승격시켜두고(StageProgress),
        /// 로비로 돌아가서 그동안 번 골드로 장비를 맞춘 뒤 "시작"을 누르면 그 다음(이미 올라간)
        /// 스테이지로 들어가게 된다. 이 스테이지 자체는 클리어됐으니 더 볼 일이 없어서, 사망 때와
        /// 달리 "계속하기" 선택지 없이 로비 이동 하나뿐이다.</summary>
        public void OnBossDefeated()
        {
            _roomController.IsInputLocked = true;
            PlayLog.BossDefeated(_stage, _player);
            StageProgress.AdvanceStage();
            if (_stage >= StageProgress.MaxStage)
                Achievements.Unlock("ACH_CLEAR_10");
            if (_roomController.NoDamageThisRoom && !BossRush.Active && !TestMap.Active)
                Achievements.Unlock("ACH_NOHIT_" + _stage); // 층 보스 무피격
            Achievements.Check(); // 층 도달 업적
            // 보스를 처음 잡았으면 유물부터 고른다(보스 전용 1 + 일반 2) → 고른 뒤 스테이지 클리어 창.
            var offer = Relics.MakeOffer(_stage);
            if (offer != null)
                _hud.ShowRelicChoice(offer, picked =>
                {
                    PlayLog.RelicChosen(offer, picked);
                    Relics.Acquire(picked, _player);
                    Achievements.Check(); // 유물 개수 업적
                    ShowStageClear();
                });
            else
                ShowStageClear();
        }

        private void ShowStageClear()
        {
            // 마지막 층 - 엔딩을 보여주고 처음으로(도감·이용권·고른 유물 하나만 남김). 자동 플레이 봇은 예전처럼 클리어 창으로(봇이 판 종료로 센다).
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
                PlayLog.Ending(record.Clears);
                EndingScreen.Show(record, () => !_player.Levels.IsChoosingUpgrade, carried =>
                {
                    if (carried.HasValue)
                        PlayLog.Note($"엔딩 - 다음 회차로 가져간 유물: {Relics.Name(carried.Value)}");
                    SaveReset.ResetForNewGame(carried);
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

        /// <summary>시도마다 방 이벤트를 새로 뽑는다 - 방 3~9를 두 방씩 묶어(3·4 / 5·6 / 7·8 / 9) 묶음마다 이벤트 1개(묶음 안 방은 무작위).
        /// 그중 한 묶음은 보물상자(아이템) 확정, 나머지는 무작위 종류(10-19 사용자 "두 방마다 1개, 종류는 랜덤" - 예전엔 보물상자 1 + 75%로 1개).</summary>

        private void RollRoomEvent()
        {
            foreach (var room in _rooms)
                room.HasEvent = false;
            if (TutorialStage.Active || BossRush.Active)
                return; // 튜토리얼·보스 러시 방엔 이벤트 없음

            var last = Mathf.Min(LastEventRoomIndex, _rooms.Count - 2); // 보스방 제외
            if (last < FirstEventRoomIndex)
                return;

            var slots = new List<int>();
            for (var first = FirstEventRoomIndex; first <= last; first += 2)
                slots.Add(Mathf.Min(last, first + Rng.Next(2)));
            var chestSlot = Rng.Next(slots.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                _rooms[slots[i]].HasEvent = true;
                _rooms[slots[i]].EventType = i == chestSlot ? RoomEventType.TreasureChest : RoomEventActor.RollNonChestType();
            }
        }

        public void ShowMessage(string message) => _hud.ShowMessage(message);

        private void RefreshHudProgress() =>
            _hud.RefreshProgress(_stage, _roomIndex + 1, _rooms.Count, LoopCount);
    }
}
