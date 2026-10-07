using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>체력/레벨/방-루프 진행도 표시 + 레벨업 업그레이드 카드 + 승리/루프리셋 안내. 전부
    /// 코드로 UGUI를 생성한다(에디터로 씬에 직접 배치할 수 없는 환경이라 - StoryRPG의 CharacterStatUI
    /// 등과 동일한 이유).</summary>
    public class GameHUD : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);

        private PlayerActor _player;

        private Text _levelText;
        private Text _progressText;
        private Text _skillText;

        // 아래 가운데 액션바 - 체력/경험치 막대 + Q/E 스킬 칸(퀵슬롯 1/2/3 칸은 InventoryUI가 같은 줄에 그린다).
        private HudBar _hpBar;
        private HudBar _expBar;
        private HudSlot _dashSlot;
        private HudSlot _spinSlot;
        private int _dashCooldownMax = PlayerActor.DashCooldownTurns;
        private int _spinCooldownMax = PlayerActor.SpinCooldownTurns;
        private int _lastDashCooldown;
        private int _lastSpinCooldown;

        // 상단 중앙 타겟 체력바 - 마지막으로 때린 적 하나만 보여준다. 그 적이 죽어서 Destroy되면
        // (Unity의 파괴된 오브젝트 == null) 자동으로 숨는다(방 전환으로 몹이 전부 지워질 때도 동일).
        private EnemyActor _target;
        private GameObject _targetPanel;
        private RectTransform _targetFill;
        private Image _targetFillImage;
        private Text _targetText;

        // 상태창(스탯 전체 보기) - Tab 키 또는 오른쪽 위 버튼으로 여닫는다. 버튼 클릭은 로비와 같은
        // 수동 사각형 히트테스트(EventSystem 없음). 열려 있어도 게임 진행은 막지 않는다(정보 표시만).
        // 글이 화면보다 길면(장비 효과 줄이 많을 때) 휠/스크롤바로 넘겨 본다.
        private RectTransform _canvasRect;
        private RectTransform _statButtonRect;
        private ScrollTextPanel _statPanel;
        // 가진 유물 - 상태창 왼쪽 별도 패널(상태창 안에 넣으면 장비 효과 줄이 많을 때 아래가 잘려서 안 보였다).
        private ScrollTextPanel _ownedRelicPanel;

        private GameObject _upgradePanel;
        private List<UpgradeOption> _pendingOptions;
        private GameObject _bannerGo;
        private Text _bannerText;
        private Coroutine _bannerRoutine;
        private GameObject _victoryPanel;
        private Text _victoryText;
        private Action _onStageClearContinue;
        private GameObject _deathPanel;
        private Text _deathPenaltyText;
        private Action<int> _onContinueAfterDeath;
        private Action _onReturnToLobby;

        // 시작 방 고르기("방을 자유롭게 들어갈 수 있게", 사용자 결정: 처음부터 11개 전부, 깬 뒤엔 다음 방으로) -
        // 스테이지 입장 창과 사망 창이 "방 지도 한 줄"(RoomMapView) 하나를 같이 쓴다.
        // 봇은 항상 방1부터(밸런스 측정 기준 유지) - 봇이 켜져 있으면 입장 창은 아예 안 뜨고, ChooseDeathContinue는 방1.
        public static bool AutoPlayActive;
        private GameObject _roomSelectPanel;
        private Text _roomSelectTitle;
        private RoomMapView _roomMap;
        private Action<int> _onRoomSelected;
        private int _roomCount;

        public void Initialize(PlayerActor player)
        {
            _player = player;
            BuildUI();

            _player.Levels.OnExpChanged += Refresh;
            _player.Levels.OnLevelUp += _ => Refresh();
            _player.Levels.OnUpgradeChoicesReady += ShowUpgradeChoices;
            _player.OnAttackedEnemy += enemy => _target = enemy;

            Refresh();
        }

        // 체력은 전투 중 실시간으로 바뀌므로 매 프레임 새로고침한다 - LevelSystem 이벤트만으로는
        // HP 변화(공격당한 순간)를 못 잡는다(StoryRPG CharacterStatUI와 같은 이유의 폴링).
        private void Update()
        {
            ApplyCameraViewport();
            Refresh();
            HandleStatPanelToggle();
            HandleStatPanelScroll();
            HandleUpgradeSelection();
            HandleRelicChoice();
            HandleDeathChoice();
            HandleRoomSelect();
            HandleStageClearChoice();
        }

        /// <summary>스테이지 입장 시 시작 방 고르기 - 고르기 전까지는 호출부(LoopManager)가 입력을 잠가둔다.</summary>
        public void ShowRoomSelect(int stage, IReadOnlyList<RoomDefinition> rooms, int defaultRoom, Action<int> onSelected)
        {
            _onRoomSelected = onSelected;
            _roomCount = rooms.Count;
            _roomSelectTitle.text = $"스테이지 {stage} - 어디서 시작할까?";
            _roomMap ??= new RoomMapView(_roomSelectPanel.transform, rooms.Count);
            _roomMap.Show(_roomSelectPanel.transform, new Vector2(0f, 0f), rooms, defaultRoom);
            _roomSelectPanel.SetActive(true);
        }

        private void HandleRoomSelect()
        {
            if (_onRoomSelected == null || !_roomMap.HandleInput())
                return;

            var callback = _onRoomSelected;
            _onRoomSelected = null;
            _roomSelectPanel.SetActive(false);
            callback(_roomMap.Selected);
        }

        /// <summary>스테이지 보스를 잡은 직후 뜨는 안내 - Enter 한 번으로 로비로 이동한다(사망 때와
        /// 달리 "계속하기"가 없다 - 이 스테이지는 이미 클리어돼서 더 볼 일이 없다).</summary>
        private void HandleStageClearChoice()
        {
            if (_onStageClearContinue == null)
                return;

            // 보스 처치 경험치로 레벨업 카드가 같이 떴으면 카드부터 고르게 한다 - 그 사이엔 클리어
            // 창을 숨기고 Enter도 무시(안 그러면 카드를 못 고른 채 로비로 넘어가 레벨업 보상이 날아감).
            _victoryPanel.SetActive(_pendingOptions == null);
            if (_pendingOptions != null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.enterKey.wasPressedThisFrame)
                return;

            ConfirmStageClear();
        }

        // ---- 보스 처치 유물 고르기 - 레벨업 카드가 같이 떴으면 카드부터(숫자키가 겹치므로 그 사이엔 창을 숨긴다) ----
        private GameObject _relicPanel;
        private List<RelicType> _pendingRelics;
        private Action<RelicType> _onRelicPicked;

        public IReadOnlyList<RelicType> PendingRelicOptions => _pendingOptions == null ? _pendingRelics : null;

        public void ShowRelicChoice(List<RelicType> options, Action<RelicType> onPicked)
        {
            _pendingRelics = options;
            _onRelicPicked = onPicked;

            foreach (Transform child in _relicPanel.transform)
                Destroy(child.gameObject);

            var title = CreateLabel(_relicPanel.transform, "보스 처치! 유물을 하나 고르세요 (영구 적용)", new Vector2(10f, -45f), new Vector2(-10f, -10f));
            title.alignment = TextAnchor.MiddleCenter;
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;

            for (var i = 0; i < options.Count; i++)
            {
                var relic = options[i];
                var cardGo = new GameObject("Relic_" + i, typeof(RectTransform));
                cardGo.transform.SetParent(_relicPanel.transform, false);
                var rect = cardGo.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(460f, 70f);
                rect.anchoredPosition = new Vector2(0f, 60f - i * 85f);
                cardGo.AddComponent<Image>().color = Relics.IsBossRelic(relic) ? new Color(0.85f, 0.35f, 0.25f, 0.35f) : new Color(0.7f, 0.4f, 1f, 0.25f);

                var text = CreateLabel(cardGo.transform, string.Empty, Vector2.zero, Vector2.zero);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = Vector2.zero;
                text.rectTransform.offsetMax = Vector2.zero;
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 15;
                text.text = $"[{i + 1}] {Relics.Name(relic)}{(Relics.IsBossRelic(relic) ? "  (보스 전용 - 지금 아니면 못 얻음)" : "")}\n{Relics.Description(relic)}";
            }
        }

        private void HandleRelicChoice()
        {
            if (_pendingRelics == null)
                return;
            _relicPanel.SetActive(_pendingOptions == null);
            if (_pendingOptions != null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            var index = keyboard.digit1Key.wasPressedThisFrame ? 0 : keyboard.digit2Key.wasPressedThisFrame ? 1 : keyboard.digit3Key.wasPressedThisFrame ? 2 : -1;
            ChooseRelic(index);
        }

        public void ChooseRelic(int index)
        {
            if (_pendingRelics == null || _pendingOptions != null || index < 0 || index >= _pendingRelics.Count)
                return;
            var picked = _pendingRelics[index];
            var callback = _onRelicPicked;
            _pendingRelics = null;
            _onRelicPicked = null;
            _relicPanel.SetActive(false);
            callback?.Invoke(picked);
        }

        // ---- 외부(자동 플레이 봇) 조작용 - 키 입력과 같은 동작을 그대로 호출한다 ----
        public IReadOnlyList<UpgradeOption> PendingUpgradeOptions => _pendingOptions;
        public bool IsDeathChoiceOpen => _onContinueAfterDeath != null;
        public bool IsStageClearOpen => _onStageClearContinue != null && _pendingOptions == null;

        public void ConfirmStageClear()
        {
            if (_onStageClearContinue == null || _pendingOptions != null)
                return;

            var callback = _onStageClearContinue;
            _onStageClearContinue = null;
            _victoryPanel.SetActive(false);
            callback();
        }

        /// <summary>봇용 - 항상 방1부터(사람은 Enter로 고른 방부터).</summary>
        public void ChooseDeathContinue() => ChooseDeathContinue(0);

        private void ChooseDeathContinue(int startRoom)
        {
            var callback = _onContinueAfterDeath;
            if (callback == null)
                return;
            ClearDeathChoice();
            callback(startRoom);
        }

        public void ChooseDeathLobby()
        {
            var callback = _onReturnToLobby;
            if (callback == null)
                return;
            ClearDeathChoice();
            callback();
        }

        /// <summary>사망 직후 뜨는 선택지("계속하기" vs "로비로 이동") - 마우스 없이 Enter/L 키로만
        /// 고른다(업그레이드 카드와 같은 컨벤션).</summary>
        private void HandleDeathChoice()
        {
            if (_onContinueAfterDeath == null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (_roomMap.HandleInput())
                ChooseDeathContinue(_roomMap.Selected);
            else if (keyboard.lKey.wasPressedThisFrame)
                ChooseDeathLobby();
        }

        private void ClearDeathChoice()
        {
            _onContinueAfterDeath = null;
            _onReturnToLobby = null;
            _deathPanel.SetActive(false);
        }

        /// <summary>업그레이드 카드는 마우스 클릭이 아니라 숫자키 1/2/3으로 고른다 - 이 프로토타입은
        /// EventSystem/InputSystemUIInputModule을 아예 안 만들어뒀다(런타임에 AddComponent로만
        /// 붙이면 기본 액션 바인딩이 비어있어 클릭이 씹힐 수 있다는 게 알려진 함정 - 이동 입력과
        /// 똑같이 Keyboard.current로 직접 읽는 이 방식이 훨씬 확실하다).</summary>
        private void HandleUpgradeSelection()
        {
            if (_pendingOptions == null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            var index = -1;
            if (keyboard.digit1Key.wasPressedThisFrame) index = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) index = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) index = 2;

            ChooseUpgrade(index);
        }

        public void ChooseUpgrade(int index)
        {
            if (_pendingOptions == null || index < 0 || index >= _pendingOptions.Count)
                return;

            _player.Levels.ChooseUpgrade(_pendingOptions[index]);
            _pendingOptions = null;
            _upgradePanel.SetActive(false);
        }

        private void Refresh()
        {
            if (_player?.Stats == null)
                return;

            var stats = _player.Stats;
            var levels = _player.Levels;

            var maxHp = Mathf.Max(1f, stats.MaxHealth);
            _hpBar.Set(stats.CurrentHealth / maxHp, stats.Shield >= 1f
                ? $"{stats.CurrentHealth:0} / {stats.MaxHealth:0}  <color=#9FD8FF>+보호막 {stats.Shield:0}</color>"
                : $"{stats.CurrentHealth:0} / {stats.MaxHealth:0}");
            _hpBar.SetOverlay(stats.Shield / maxHp);
            var expRatio = levels.IsMaxLevel ? 1f : levels.Exp / (float)Mathf.Max(1, levels.ExpToNext);
            _expBar.Set(expRatio, levels.IsMaxLevel
                ? $"Lv.{levels.Level}   EXP MAX"
                : $"Lv.{levels.Level}   EXP {levels.Exp} / {levels.ExpToNext}  [{expRatio * 100f:0.00}%]");
            _levelText.text = $"Lv.{levels.Level}  (ATK {stats.AttackPower:0})";

            // 상태 안내 한 줄(체력 막대 위) - 방향 고르는 중이거나 묶였을 때만.
            _skillText.text = _player.AimingItem.HasValue
                ? $"<color=#FFD27F>[아이템] {ItemInfo.Name(_player.AimingItem.Value)} - 방향키로 방향 선택 (같은 키: 취소)</color>"
                : _player.RootedTurns > 0
                ? "<color=#D9B3FF>거미줄에 묶임! 이동·대시 불가 (공격·E·Space 가능)</color>"
                : _player.IsAimingDash
                    ? "<color=#7FD4FF>[Q] 대시 - 방향키로 방향 선택 (Q: 취소)</color>"
                    : string.Empty;
            RefreshSkillSlots();

            RefreshTarget();

            if (_statPanel.Root.activeSelf)
                RefreshStatPanels();
        }

        private void HandleStatPanelToggle()
        {
            var toggle = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;

            var mouse = Mouse.current;
            if (!PauseMenu.BlocksInput && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                RectTransformUtility.RectangleContainsScreenPoint(_statButtonRect, mouse.position.ReadValue(), null))
                toggle = true;

            if (!toggle)
                return;

            var open = !_statPanel.Root.activeSelf;
            _statPanel.Root.SetActive(open);
            if (open)
            {
                _statPanel.ResetScroll();
                _ownedRelicPanel.ResetScroll();
                RefreshStatPanels();
            }
            else
                _ownedRelicPanel.Root.SetActive(false);
        }

        private void HandleStatPanelScroll()
        {
            if (!_statPanel.Root.activeSelf || PauseMenu.BlocksInput)
                return;
            var mouse = Mouse.current;
            _statPanel.HandleMouse(mouse);
            if (_ownedRelicPanel.Root.activeSelf)
                _ownedRelicPanel.HandleMouse(mouse);
        }

        private const float StatPanelTop = ActionBarLayout.TopBarHeight + 8f;
        private const float StatPanelBottomMargin = ActionBarLayout.BottomAreaHeight + 8f;

        /// <summary>상태창 글과 유물 패널을 같이 갱신 - 유물 패널은 유물이 있을 때만. 높이는 글 길이에 맞추되
        /// 화면 아래를 넘으면 거기서 멈추고 스크롤바가 생긴다.</summary>
        private void RefreshStatPanels()
        {
            // 캔버스 높이는 화면 비율에 따라 720이 아닐 수 있다(ScaleWithScreenSize, 너비 기준).
            var canvasHeight = _canvasRect.rect.height > 200f ? _canvasRect.rect.height : 720f;
            var maxHeight = canvasHeight - StatPanelTop - StatPanelBottomMargin;

            _statPanel.SetText(BuildStatText(), maxHeight);

            var hasRelics = Relics.OwnedCount > 0;
            if (_ownedRelicPanel.Root.activeSelf != hasRelics)
                _ownedRelicPanel.Root.SetActive(hasRelics);
            if (hasRelics)
                _ownedRelicPanel.SetText(BuildRelicText(), maxHeight);
        }

        private static string BuildRelicText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b><color=#E8A0FF>[유물 {Relics.OwnedCount}개]</color></b>");
            foreach (var r in Relics.OwnedRelics())
            {
                var color = Relics.IsBossRelic(r) ? "#FF9A80" : "#E8C8FF";
                sb.AppendLine($"<color={color}>{Relics.Name(r)}</color>");
                sb.AppendLine($"<size=12>   {Relics.Description(r)}</size>");
            }
            return sb.ToString().TrimEnd();
        }

        private static string FormatBonus(float rate) =>
            rate >= 0f ? $"+{rate * 100f:0.#}%" : $"<color=#FF7070>{rate * 100f:0.#}%</color>";

        private string BuildStatText()
        {
            var s = _player.Stats;
            var levels = _player.Levels;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("<b><color=#FFD966>[기본]</color></b>");
            sb.AppendLine(levels.IsMaxLevel ? $"레벨  Lv.{levels.Level}  (MAX)" : $"레벨  Lv.{levels.Level}  (EXP {levels.Exp}/{levels.ExpToNext})");
            sb.AppendLine($"체력  {s.CurrentHealth:0} / {s.MaxHealth:0}  (장비·영약 {s.FixedMaxHealth:0})");
            sb.AppendLine($"공격력  {s.AttackPower:0.#}  (장비·영약 {s.FixedAttack:0.#})");
            sb.AppendLine($"치명타  {s.EffectiveCriticalChance * 100f:0.#}%  (피해 {s.CriticalDamageMultiplier * 100f:0.#}%)");
            if (s.CriticalChanceRate > CharacterStats.MaxCriticalChance)
                sb.AppendLine($"   초과 치명타 {(s.CriticalChanceRate - CharacterStats.MaxCriticalChance) * 100f:0.#}%p → 피해로 전환");

            sb.AppendLine();
            sb.AppendLine("<b><color=#7FE0A0>[특수]</color></b>");
            var reduction = Mathf.Clamp(s.DamageReductionRate, -CharacterStats.MaxDamageReduction, CharacterStats.MaxDamageReduction);
            sb.AppendLine(reduction >= 0f
                ? $"받는 피해  -{reduction * 100f:0.#}%  (최대 {CharacterStats.MaxDamageReduction * 100f:0}%)"
                : $"받는 피해  <color=#FF7070>+{-reduction * 100f:0.#}%</color>");
            sb.AppendLine($"흡혈  {s.EffectiveLifeSteal * 100f:0.#}%{(s.LifeStealRate >= Relics.LifeStealCap ? " (최대)" : "")}");
            sb.AppendLine($"재생  턴당 {s.RegenPerTurnRate * 100f:0.#}%  (≈{s.MaxHealth * s.RegenPerTurnRate:0.#} HP)");
            sb.AppendLine($"처치 회복  {s.KillHealRate * 100f:0.#}%  (≈{s.MaxHealth * s.KillHealRate:0.#} HP)");

            sb.AppendLine();
            sb.AppendLine("<b><color=#FFE680>[경제]</color></b>");
            sb.AppendLine($"경험치  {FormatBonus(s.EffectiveExpBonus)}");
            sb.AppendLine($"골드  {FormatBonus(s.EffectiveGoldBonus)}");
            sb.AppendLine($"보유 골드  {GoldWallet.Gold}");

            sb.AppendLine();
            sb.AppendLine("<b><color=#8FB8FF>[장비]</color></b>");
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var grade = EquipmentWallet.GetEquipped(slot);
                var baseName = EquipmentData.Templates[slot].BaseName;
                if (!grade.HasValue)
                {
                    sb.AppendLine($"{baseName}  (미장착)");
                    continue;
                }

                var hex = ColorUtility.ToHtmlStringRGB(EquipmentData.Grades[grade.Value].Color);
                sb.AppendLine($"<color=#{hex}>{EquipmentData.DisplayName(slot, grade.Value)}</color>");
                var tier = EquipmentEffects.TierOf(grade.Value);
                for (var t = 1; t <= tier; t++)
                    sb.AppendLine($"   <size=12>{EquipmentEffects.Name(slot, t)}: {EquipmentEffects.Description(slot, t)}</size>");
            }

            return sb.ToString().TrimEnd();
        }

        private void RefreshTarget()
        {
            var visible = _target != null && !_target.Stats.IsDead;
            if (_targetPanel.activeSelf != visible)
                _targetPanel.SetActive(visible);
            if (!visible)
                return;

            var stats = _target.Stats;
            var ratio = stats.MaxHealth > 0f ? Mathf.Clamp01(stats.CurrentHealth / stats.MaxHealth) : 0f;
            _targetFill.anchorMax = new Vector2(ratio, 1f);
            _targetFillImage.color = _target.IsBoss ? new Color(0.85f, 0.35f, 0.1f) : new Color(0.75f, 0.15f, 0.15f);
            _targetText.text = $"{_target.DisplayName}  HP {stats.CurrentHealth:0}/{stats.MaxHealth:0}  (ATK {stats.AttackPower:0})";
        }

        public void RefreshProgress(int stage, int roomNumber, int roomCount, int loopCount)
        {
            // 시도 횟수는 이 스테이지 누적(로비 왕복 포함) - 반복 보상 감소가 이 숫자로 정해진다.
            var reward = StageProgress.RepeatRewardMultiplier;
            var rewardText = reward < 0.999f ? $"   보상 {reward * 100f:0}%" : string.Empty;
            _progressText.text = $"Stage {stage}/{StageProgress.MaxStage}   Room {roomNumber}/{roomCount}   시도 {StageProgress.AttemptsThisStage}{rewardText}";
        }

        /// <summary>화면 위쪽 배너로 짧은 안내(방 이벤트 결과, 보스 소환 등).</summary>
        public void ShowMessage(string message) => ShowBanner(message);

        public void ShowLoopResetBanner(int startRoom) => ShowBanner($"스탯은 그대로! {RoomMapView.RoomLabel(startRoom, _roomCount)}부터 다시.");

        private void ShowBanner(string message)
        {
            _bannerText.text = message;
            _bannerGo.SetActive(true);

            if (_bannerRoutine != null)
                StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(HideBannerAfter(2.5f));
        }

        private IEnumerator HideBannerAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _bannerGo.SetActive(false);
            _bannerRoutine = null;
        }

        /// <summary>LoopManager.OnBossDefeated가 호출 - stage는 "방금 깬" 스테이지 번호(이미
        /// StageProgress는 다음 스테이지로 올라간 뒤라서, 메시지에는 방금 깬 번호를 그대로 보여준다).
        /// Enter를 누르면 onContinue(로비 이동)가 실행된다.</summary>
        public void ShowStageClear(int stage, Action onContinue)
        {
            _onStageClearContinue = onContinue;

            _victoryText.text = stage >= StageProgress.MaxStage
                ? $"스테이지 {stage} 클리어! 마지막 벽까지 뚫었다!\n[Enter] 로비로 이동"
                : $"스테이지 {stage} 클리어!\n[Enter] 로비로 이동 (다음 스테이지 도전 가능)";

            _victoryPanel.SetActive(true);
        }

        /// <summary>LoopManager.OnPlayerDied가 호출 - 선택 전까지는 RoomController.IsInputLocked가
        /// 이미 켜져 있어서 플레이어가 움직일 수 없다.</summary>
        public void ShowDeathChoice(Action<int> onContinue, Action onLobby, int goldPenalty, IReadOnlyList<RoomDefinition> rooms, int defaultRoom)
        {
            _onContinueAfterDeath = onContinue;
            _roomCount = rooms.Count;
            _roomMap ??= new RoomMapView(_deathPanel.transform, rooms.Count);
            _roomMap.Show(_deathPanel.transform, new Vector2(0f, -5f), rooms, defaultRoom);
            _onReturnToLobby = onLobby;
            LastDeathGoldPenalty = goldPenalty;
            _deathPenaltyText.text = goldPenalty > 0
                ? $"데스 패널티: 골드 -{goldPenalty} (이번 시도 획득분의 {Relics.DeathPenaltyRate * 100f:0}%)"
                : "데스 패널티: 이번 시도에 번 골드 없음";
            _deathPanel.SetActive(true);
        }

        /// <summary>가장 최근 사망 때 잃은 골드 - 자동 플레이 봇 통계용.</summary>
        public int LastDeathGoldPenalty { get; private set; }

        private static Color CardColor(UpgradeCategory category) => category switch
        {
            UpgradeCategory.Growth => new Color(0.3f, 0.6f, 1f, 0.25f),
            UpgradeCategory.Special => new Color(0.3f, 0.9f, 0.5f, 0.22f),
            UpgradeCategory.Economy => new Color(1f, 0.85f, 0.3f, 0.22f),
            UpgradeCategory.Gamble => new Color(1f, 0.3f, 0.3f, 0.3f),
            _ => new Color(1f, 1f, 1f, 0.15f),
        };

        private void ShowUpgradeChoices(List<UpgradeOption> options)
        {
            foreach (Transform child in _upgradePanel.transform)
                Destroy(child.gameObject);

            _pendingOptions = options;

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];

                var cardGo = new GameObject("Option_" + i, typeof(RectTransform));
                cardGo.transform.SetParent(_upgradePanel.transform, false);
                var rect = cardGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(400f, 70f);
                rect.anchoredPosition = new Vector2(0f, 100f - i * 90f);

                cardGo.AddComponent<Image>().color = CardColor(option.Category);

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(cardGo.transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                var text = textGo.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 15;
                text.color = Color.white;
                text.text = $"[{i + 1}] {option.Title}\n{option.Description}";
            }

            _upgradePanel.SetActive(true);
        }

        // ===================== UI 생성 =====================

        private void BuildUI()
        {
            var canvasGo = new GameObject("HUDCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = canvasGo.GetComponent<RectTransform>();

            BuildStatusPanel(canvasGo.transform);
            BuildActionBar(canvasGo.transform);
            BuildTargetPanel(canvasGo.transform);
            BuildStatPanel(canvasGo.transform);
            _upgradePanel = BuildOverlayPanel(canvasGo.transform, "UpgradePanel", new Vector2(440f, 320f), active: false);
            BuildBanner(canvasGo.transform);
            BuildVictoryPanel(canvasGo.transform);
            BuildDeathPanel(canvasGo.transform);
            BuildRoomSelectPanel(canvasGo.transform);
            _relicPanel = BuildOverlayPanel(canvasGo.transform, "RelicPanel", new Vector2(500f, 330f), active: false);
            canvasGo.AddComponent<InventoryUI>().Build(canvasGo.transform, _player);
        }

        private static readonly Color BandColor = new Color(0.04f, 0.04f, 0.05f, 1f);

        /// <summary>화면 맨 위 띠 한 줄 - 레벨·공격력 / 스테이지 진행 / 조작 안내 / [Tab] 스탯 버튼(BuildStatPanel).
        /// 카메라 영역이 이 띠 아래에서 시작해서 게임 화면을 가리지 않는다.</summary>
        private void BuildStatusPanel(Transform parent)
        {
            var band = HudUi.CreateImage(parent, "TopBar", BandColor);
            var rect = band.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -ActionBarLayout.TopBarHeight);
            rect.offsetMax = Vector2.zero;

            _levelText = CreateBandText(band.transform, "Level", 15, 14f, 180f, TextAnchor.MiddleLeft);
            _levelText.fontStyle = FontStyle.Bold;
            _progressText = CreateBandText(band.transform, "Progress", 14, 200f, 440f, TextAnchor.MiddleLeft);

            // 조작 안내 - 대기 키(스페이스바)를 모르고 지나치지 않게. 오른쪽 [Tab] 스탯 버튼 바로 왼쪽.
            var controls = CreateBandText(band.transform, "Controls", 12, -520f - 130f, 520f, TextAnchor.MiddleRight);
            controls.text = "이동/공격: 방향키·WASD   대기: Space   스탯: Tab   인벤토리: I   메뉴: Esc";
            controls.color = new Color(0.62f, 0.64f, 0.7f);
            var controlsRect = controls.rectTransform;
            controlsRect.anchorMin = controlsRect.anchorMax = new Vector2(1f, 0.5f);
            controlsRect.pivot = new Vector2(0f, 0.5f);
        }

        /// <summary>위 띠 안 글자 - 왼쪽 끝 기준 x에서 width만큼.</summary>
        private static Text CreateBandText(Transform band, string name, int fontSize, float x, float width, TextAnchor alignment)
        {
            var text = HudUi.CreateText(band, name, fontSize, alignment);
            text.supportRichText = true;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, ActionBarLayout.TopBarHeight);
            return text;
        }

        private float _appliedViewportBottom = -1f;
        private float _appliedViewportHeight = -1f;

        /// <summary>카메라가 위·아래 HUD 띠를 뺀 가운데만 그리게 한다. 캔버스 높이는 화면 비율마다 달라서(너비 기준
        /// 스케일) 매 프레임 비율을 다시 재고, 바뀐 때만 넣는다. 카메라 따라가기는 Camera.aspect·orthographicSize를 그때그때 읽어서 그대로 맞는다.</summary>
        private void ApplyCameraViewport()
        {
            var cam = Camera.main;
            var canvasHeight = _canvasRect.rect.height;
            if (cam == null || canvasHeight < 200f)
                return;
            var bottom = ActionBarLayout.BottomAreaHeight / canvasHeight;
            var height = 1f - (ActionBarLayout.BottomAreaHeight + ActionBarLayout.TopBarHeight) / canvasHeight;
            if (Mathf.Approximately(bottom, _appliedViewportBottom) && Mathf.Approximately(height, _appliedViewportHeight))
                return;
            _appliedViewportBottom = bottom;
            _appliedViewportHeight = height;
            cam.rect = new Rect(0f, bottom, 1f, height);
            // 영역이 줄어든 만큼 보이는 높이도 줄여 칸 크기를 HUD 넣기 전과 같게(방을 새로 불러올 때도 같은 계산).
            cam.orthographicSize = RoomController.CameraOrthoSize(cam);
        }

        /// <summary>Q/E 칸 - 쿨다운 덮개 비율의 분모는 "막 썼을 때의 남은 턴"(반지 효과로 줄어들 수 있어서 상수 대신 관찰값).</summary>
        private void RefreshSkillSlots()
        {
            UpdateSkillSlot(_dashSlot, _player.DashCooldown, ref _dashCooldownMax, ref _lastDashCooldown,
                _player.IsAimingDash, _player.RootedTurns > 0);
            UpdateSkillSlot(_spinSlot, _player.SpinCooldown, ref _spinCooldownMax, ref _lastSpinCooldown, false, false);
        }

        private static void UpdateSkillSlot(HudSlot slot, int cooldown, ref int max, ref int last, bool active, bool blocked)
        {
            if (cooldown > last)
                max = cooldown;
            last = cooldown;
            slot.SetCooldown(cooldown, max);

            var ready = cooldown <= 0 && !blocked;
            slot.Frame.color = active ? HudSlot.FrameActive : ready ? HudSlot.FrameReady : HudSlot.FrameNormal;
            slot.Label.color = ready || active ? Color.white : HudSlot.TextDim;
        }

        private void BuildActionBar(Transform parent)
        {
            var bottom = new Vector2(0.5f, 0f);
            var slotSize = new Vector2(ActionBarLayout.SlotSize, ActionBarLayout.SlotSize);

            // 아래 띠 배경 - 카메라 영역이 이 위에서 시작한다(ApplyCameraViewport).
            var band = HudUi.CreateImage(parent, "BottomBar", BandColor).rectTransform;
            band.anchorMin = Vector2.zero;
            band.anchorMax = new Vector2(1f, 0f);
            band.pivot = new Vector2(0.5f, 0f);
            band.offsetMin = Vector2.zero;
            band.offsetMax = new Vector2(0f, ActionBarLayout.BottomAreaHeight);

            _dashSlot = new HudSlot(parent, "DashSlot", bottom,
                new Vector2(ActionBarLayout.SkillX(0), ActionBarLayout.SlotCenterY), slotSize, "Q");
            _dashSlot.Label.text = "대시";
            _spinSlot = new HudSlot(parent, "SpinSlot", bottom,
                new Vector2(ActionBarLayout.SkillX(1), ActionBarLayout.SlotCenterY), slotSize, "E");
            _spinSlot.Label.text = "회전\n베기";

            _hpBar = new HudBar(parent, "HpBar", new Vector2(ActionBarLayout.HpBarCenterX, ActionBarLayout.HpBarBottom),
                new Vector2(ActionBarLayout.HpBarWidth, ActionBarLayout.HpBarHeight),
                new Color(0.78f, 0.2f, 0.22f), 15, new Color(0.55f, 0.82f, 1f, 0.85f));
            _hpBar.Label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            _expBar = new HudBar(parent, "ExpBar", Vector2.zero, Vector2.zero, new Color(0.85f, 0.7f, 0.25f), 12);
            _expBar.StretchAcrossBottom(ActionBarLayout.ExpBarHeight);
            _expBar.Label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            // 상태 안내 한 줄 - 체력 막대 바로 위, 가운데 정렬.
            _skillText = HudUi.CreateText(parent, "StatusLine", 15, TextAnchor.MiddleCenter);
            _skillText.supportRichText = true;
            var lineRect = _skillText.rectTransform;
            lineRect.anchorMin = bottom;
            lineRect.anchorMax = bottom;
            lineRect.pivot = new Vector2(0.5f, 0f);
            lineRect.anchoredPosition = new Vector2(0f, ActionBarLayout.StatusLineBottom);
            lineRect.sizeDelta = new Vector2(700f, 22f);
            var outline = _skillText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        }

        private void BuildStatPanel(Transform parent)
        {
            // 위 띠 오른쪽 끝 토글 버튼
            var buttonGo = new GameObject("StatButton", typeof(RectTransform));
            buttonGo.transform.SetParent(parent, false);
            _statButtonRect = buttonGo.GetComponent<RectTransform>();
            _statButtonRect.anchorMin = new Vector2(1f, 1f);
            _statButtonRect.anchorMax = new Vector2(1f, 1f);
            _statButtonRect.pivot = new Vector2(1f, 1f);
            _statButtonRect.sizeDelta = new Vector2(110f, ActionBarLayout.TopBarHeight - 8f);
            _statButtonRect.anchoredPosition = new Vector2(-8f, -4f);
            buttonGo.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.35f, 0.95f);

            var buttonText = CreateLabel(buttonGo.transform, "[Tab] 스탯", Vector2.zero, Vector2.zero);
            buttonText.rectTransform.anchorMin = Vector2.zero;
            buttonText.rectTransform.anchorMax = Vector2.one;
            buttonText.rectTransform.offsetMin = Vector2.zero;
            buttonText.rectTransform.offsetMax = Vector2.zero;
            buttonText.alignment = TextAnchor.MiddleCenter;

            // 버튼 아래 상태창, 그 바로 왼쪽에 유물 패널 - 높이는 RefreshStatPanels가 글에 맞춘다.
            const float statWidth = 340f;
            _statPanel = new ScrollTextPanel(parent, "StatPanel", statWidth, new Vector2(-16f, -StatPanelTop));
            _ownedRelicPanel = new ScrollTextPanel(parent, "OwnedRelicPanel", 300f, new Vector2(-16f - statWidth - 8f, -StatPanelTop));
        }

        /// <summary>글이 길면 마우스 휠이나 스크롤바 드래그로 넘겨 보는 글 패널. 이 프로젝트엔 EventSystem이
        /// 없어서(HandleUpgradeSelection 주석 참고) ScrollRect 대신 RectMask2D로 자르고, 휠·드래그는
        /// Mouse.current + 사각형 히트테스트로 직접 읽는다. 패널 높이는 글 길이에 맞추되 maxHeight에서 멈춘다.</summary>
        private sealed class ScrollTextPanel
        {
            private const float Padding = 10f;
            private const float BarWidth = 6f;
            private const float MinHandleHeight = 24f;
            private const float WheelStep = 48f;

            public readonly GameObject Root;
            private readonly RectTransform _rect;
            private readonly Text _text;
            private readonly GameObject _bar;
            private readonly RectTransform _barRect;
            private readonly RectTransform _handle;
            private string _shownText;
            private float _shownMaxHeight;
            private float _contentHeight;
            private float _offset;
            private bool _dragging;

            public ScrollTextPanel(Transform parent, string name, float width, Vector2 anchoredPosition)
            {
                Root = new GameObject(name, typeof(RectTransform));
                Root.transform.SetParent(parent, false);
                _rect = Root.GetComponent<RectTransform>();
                _rect.anchorMin = new Vector2(1f, 1f);
                _rect.anchorMax = new Vector2(1f, 1f);
                _rect.pivot = new Vector2(1f, 1f);
                _rect.sizeDelta = new Vector2(width, 100f);
                _rect.anchoredPosition = anchoredPosition;
                Root.AddComponent<Image>().color = PanelColor;

                // 글이 보이는 창 - 밖으로 나간 부분은 RectMask2D가 자른다. 오른쪽은 스크롤바 자리.
                var viewportGo = new GameObject("Viewport", typeof(RectTransform));
                viewportGo.transform.SetParent(Root.transform, false);
                var viewport = viewportGo.GetComponent<RectTransform>();
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = new Vector2(14f, Padding);
                viewport.offsetMax = new Vector2(-(BarWidth + 12f), -Padding);
                viewportGo.AddComponent<RectMask2D>();

                _text = CreateLabel(viewport, string.Empty, Vector2.zero, Vector2.zero);
                _text.supportRichText = true;
                _text.lineSpacing = 1.05f;
                _text.verticalOverflow = VerticalWrapMode.Overflow;

                var barGo = new GameObject("ScrollBar", typeof(RectTransform));
                barGo.transform.SetParent(Root.transform, false);
                _bar = barGo;
                _barRect = barGo.GetComponent<RectTransform>();
                _barRect.anchorMin = new Vector2(1f, 0f);
                _barRect.anchorMax = new Vector2(1f, 1f);
                _barRect.pivot = new Vector2(1f, 1f);
                _barRect.offsetMin = new Vector2(-6f - BarWidth, Padding);
                _barRect.offsetMax = new Vector2(-6f, -Padding);
                barGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

                var handleGo = new GameObject("Handle", typeof(RectTransform));
                handleGo.transform.SetParent(barGo.transform, false);
                _handle = handleGo.GetComponent<RectTransform>();
                _handle.anchorMin = new Vector2(0f, 1f);
                _handle.anchorMax = new Vector2(1f, 1f);
                _handle.pivot = new Vector2(0.5f, 1f);
                handleGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.55f);

                Root.SetActive(false);
            }

            private float ViewHeight => _rect.sizeDelta.y - Padding * 2f;
            private float MaxOffset => Mathf.Max(0f, _contentHeight - ViewHeight);

            /// <summary>매 프레임 불려도 되게 글이나 최대 높이가 바뀐 때만 다시 잰다.</summary>
            public void SetText(string text, float maxHeight)
            {
                if (text == _shownText && Mathf.Approximately(maxHeight, _shownMaxHeight))
                    return;
                _shownText = text;
                _shownMaxHeight = maxHeight;
                _text.text = text;
                _contentHeight = _text.preferredHeight;
                var height = Mathf.Min(maxHeight, _contentHeight + Padding * 2f);
                _rect.sizeDelta = new Vector2(_rect.sizeDelta.x, height);
                ApplyScroll();
            }

            public void ResetScroll()
            {
                _offset = 0f;
                _dragging = false;
                ApplyScroll();
            }

            public void HandleMouse(Mouse mouse)
            {
                if (mouse == null || MaxOffset <= 0f)
                {
                    _dragging = false;
                    return;
                }

                var pos = mouse.position.ReadValue();
                // 휠 값 크기는 Input System 버전/OS마다 달라서(±1 또는 ±120) 방향만 쓴다.
                var wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f && RectTransformUtility.RectangleContainsScreenPoint(_rect, pos, null))
                {
                    _offset -= Mathf.Sign(wheel) * WheelStep;
                    ApplyScroll();
                }

                if (mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(_barRect, pos, null))
                    _dragging = true;
                if (!mouse.leftButton.isPressed)
                    _dragging = false;

                // 막대 위쪽(피벗)이 0, 아래로 갈수록 음수 - 손잡이 가운데가 마우스를 따라간다.
                if (_dragging && RectTransformUtility.ScreenPointToLocalPointInRectangle(_barRect, pos, null, out var local))
                {
                    var track = _barRect.rect.height - _handle.rect.height;
                    var t = track > 0f ? Mathf.Clamp01((-local.y - _handle.rect.height * 0.5f) / track) : 0f;
                    _offset = t * MaxOffset;
                    ApplyScroll();
                }
            }

            private void ApplyScroll()
            {
                var max = MaxOffset;
                _offset = Mathf.Clamp(_offset, 0f, max);
                _text.rectTransform.sizeDelta = new Vector2(0f, _contentHeight);
                _text.rectTransform.anchoredPosition = new Vector2(0f, _offset);

                var scrollable = max > 0.5f;
                if (_bar.activeSelf != scrollable)
                    _bar.SetActive(scrollable);
                if (!scrollable)
                    return;
                var view = ViewHeight;
                var handleHeight = Mathf.Max(MinHandleHeight, view * view / _contentHeight);
                _handle.sizeDelta = new Vector2(0f, handleHeight);
                _handle.anchoredPosition = new Vector2(0f, -(view - handleHeight) * (_offset / max));
            }
        }

        private void BuildTargetPanel(Transform parent)
        {
            _targetPanel = new GameObject("TargetPanel", typeof(RectTransform));
            _targetPanel.transform.SetParent(parent, false);
            var rect = _targetPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(440f, 34f);
            rect.anchoredPosition = new Vector2(0f, -(ActionBarLayout.TopBarHeight + 8f)); // 위 띠 바로 아래
            _targetPanel.AddComponent<Image>().color = PanelColor;

            // 채움 막대 - 오른쪽 끝(anchorMax.x)을 체력 비율로 줄인다(Image.fillAmount는 스프라이트가
            // 있어야 동작해서, 스프라이트 없는 단색 Image로는 앵커 방식이 확실하다).
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(_targetPanel.transform, false);
            _targetFill = fillGo.GetComponent<RectTransform>();
            _targetFill.anchorMin = Vector2.zero;
            _targetFill.anchorMax = Vector2.one;
            _targetFill.offsetMin = new Vector2(3f, 3f);
            _targetFill.offsetMax = new Vector2(-3f, -3f);
            _targetFillImage = fillGo.AddComponent<Image>();

            _targetText = CreateLabel(_targetPanel.transform, string.Empty, Vector2.zero, Vector2.zero);
            _targetText.rectTransform.anchorMin = Vector2.zero;
            _targetText.rectTransform.anchorMax = Vector2.one;
            _targetText.rectTransform.offsetMin = Vector2.zero;
            _targetText.rectTransform.offsetMax = Vector2.zero;
            _targetText.alignment = TextAnchor.MiddleCenter;

            _targetPanel.SetActive(false);
        }

        /// <summary>화면 중앙을 덮는 반투명 패널 - 업그레이드 카드/승리 화면이 공유하는 뼈대.</summary>
        private GameObject BuildOverlayPanel(Transform parent, string name, Vector2 size, bool active)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            go.SetActive(active);
            return go;
        }

        private void BuildBanner(Transform parent)
        {
            _bannerGo = new GameObject("Banner", typeof(RectTransform));
            _bannerGo.transform.SetParent(parent, false);
            var rect = _bannerGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(560f, 44f);
            rect.anchoredPosition = new Vector2(0f, -(ActionBarLayout.TopBarHeight + 8f + 34f + 10f)); // 상단 타겟 체력바(높이 34) 아래
            _bannerGo.AddComponent<Image>().color = new Color(0.5f, 0.1f, 0.1f, 0.85f);

            _bannerText = CreateLabel(_bannerGo.transform, string.Empty, Vector2.zero, Vector2.zero);
            _bannerText.rectTransform.anchorMin = Vector2.zero;
            _bannerText.rectTransform.anchorMax = Vector2.one;
            _bannerText.rectTransform.offsetMin = Vector2.zero;
            _bannerText.rectTransform.offsetMax = Vector2.zero;
            _bannerText.alignment = TextAnchor.MiddleCenter;

            _bannerGo.SetActive(false);
        }

        private void BuildVictoryPanel(Transform parent)
        {
            _victoryPanel = BuildOverlayPanel(parent, "VictoryPanel", new Vector2(460f, 200f), active: false);

            _victoryText = CreateLabel(_victoryPanel.transform, string.Empty, new Vector2(10f, -170f), new Vector2(-10f, -10f));
            _victoryText.alignment = TextAnchor.MiddleCenter;
            _victoryText.fontSize = 20;
            _victoryText.fontStyle = FontStyle.Bold;
        }

        private void BuildDeathPanel(Transform parent)
        {
            _deathPanel = BuildOverlayPanel(parent, "DeathPanel", new Vector2(700f, 330f), active: false);

            var title = CreateLabel(_deathPanel.transform, "사망! (레벨/스탯/장비는 그대로 유지됩니다)",
                new Vector2(10f, -70f), new Vector2(-10f, -15f));
            title.alignment = TextAnchor.MiddleCenter;
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;

            _deathPenaltyText = CreateLabel(_deathPanel.transform, string.Empty, new Vector2(10f, -105f), new Vector2(-10f, -72f));
            _deathPenaltyText.alignment = TextAnchor.MiddleCenter;
            _deathPenaltyText.fontSize = 15;
            _deathPenaltyText.color = new Color(1f, 0.6f, 0.4f);

            var prompt = CreateLabel(_deathPanel.transform, "←/→ 또는 클릭: 시작 방 고르기   [Enter] 계속하기   [L] 로비로 이동",
                new Vector2(10f, -315f), new Vector2(-10f, -275f));
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.fontSize = 16;
        }

        private void BuildRoomSelectPanel(Transform parent)
        {
            _roomSelectPanel = BuildOverlayPanel(parent, "RoomSelectPanel", new Vector2(700f, 280f), active: false);

            _roomSelectTitle = CreateLabel(_roomSelectPanel.transform, string.Empty, new Vector2(10f, -55f), new Vector2(-10f, -15f));
            _roomSelectTitle.alignment = TextAnchor.MiddleCenter;
            _roomSelectTitle.fontSize = 20;
            _roomSelectTitle.fontStyle = FontStyle.Bold;

            var prompt = CreateLabel(_roomSelectPanel.transform, "←/→ 또는 클릭: 고르기   B: 보스방   [Enter] 시작  (깬 뒤엔 다음 방으로)",
                new Vector2(10f, -265f), new Vector2(-10f, -225f));
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.fontSize = 15;
        }

        /// <summary>anchorMin/Max를 (0,1)/(1,1)로 고정하고 offsetMin/Max로 위치를 잡는 상단-정렬
        /// 텍스트 라벨 헬퍼 - 이 HUD의 모든 텍스트가 같은 규칙을 쓴다.</summary>
        private static Text CreateLabel(Transform parent, string content, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.UpperLeft;
            text.fontSize = 15;
            text.color = Color.white;
            text.text = content;
            return text;
        }
    }
}
