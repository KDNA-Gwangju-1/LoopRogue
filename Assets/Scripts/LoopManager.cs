using System.Collections.Generic;

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

        public void Initialize(RoomController roomController, PlayerActor player, GameHUD hud,
            List<RoomDefinition> rooms, int stage)
        {
            _roomController = roomController;
            _player = player;
            _hud = hud;
            _rooms = rooms;
            _stage = stage;
            _roomIndex = 0;

            _roomController.Initialize(player, this, stage);
            _roomController.LoadRoom(_rooms[_roomIndex]);
            RefreshHudProgress();

            // Esc 메뉴 - 사망/스테이지 클리어(입력 잠금 중)나 레벨업 카드 선택 중에는 안 열린다.
            // 로비 이동은 사망/클리어 때와 같은 경로(풀피 + 런 저장 후 이동).
            _hud.gameObject.AddComponent<PauseMenu>().Setup(SaveProgressAndLoadLobby,
                () => !_roomController.IsInputLocked && !_player.Levels.IsChoosingUpgrade && !_player.Stats.IsDead);
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
            _hud.ShowDeathChoice(ContinueAfterDeath, ReturnToLobby);
        }

        private void ContinueAfterDeath()
        {
            LoopCount++;
            _roomIndex = 0;
            _player.Stats.FullHeal();
            _roomController.LoadRoom(_rooms[_roomIndex]); // IsInputLocked를 다시 false로 풀어준다.
            RefreshHudProgress();
            _hud.ShowLoopResetBanner();
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
            _hud.ShowStageClear(_stage, SaveProgressAndLoadLobby);
        }

        private void SaveProgressAndLoadLobby()
        {
            // 씬이 넘어가면 플레이어 오브젝트 자체가 통째로 사라지므로, 사라지기 직전에 지금
            // 레벨/경험치/스탯을 RunProgress에 스냅샷으로 남겨서 Main이 다시 뜰 때 복원한다.
            // FullHeal도 같이 - 안 그러면 방금 죽은(혹은 보스전 막판 깎인) 체력이 그대로 저장된다.
            _player.Stats.FullHeal();
            RunProgress.Save(_player.Levels, _player.Stats);
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }

        private void RefreshHudProgress() =>
            _hud.RefreshProgress(_stage, _roomIndex + 1, _rooms.Count, LoopCount);
    }
}
