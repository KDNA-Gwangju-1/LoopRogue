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
        private Text _hpText;
        private Text _expText;
        private Text _skillText;

        // 상단 중앙 타겟 체력바 - 마지막으로 때린 적 하나만 보여준다. 그 적이 죽어서 Destroy되면
        // (Unity의 파괴된 오브젝트 == null) 자동으로 숨는다(방 전환으로 몹이 전부 지워질 때도 동일).
        private EnemyActor _target;
        private GameObject _targetPanel;
        private RectTransform _targetFill;
        private Image _targetFillImage;
        private Text _targetText;

        // 상태창(스탯 전체 보기) - Tab 키 또는 오른쪽 위 버튼으로 여닫는다. 버튼 클릭은 로비와 같은
        // 수동 사각형 히트테스트(EventSystem 없음). 열려 있어도 게임 진행은 막지 않는다(정보 표시만).
        private RectTransform _statButtonRect;
        private GameObject _statPanel;
        private Text _statText;

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
        private Action _onContinueAfterDeath;
        private Action _onReturnToLobby;

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
            Refresh();
            HandleStatPanelToggle();
            HandleUpgradeSelection();
            HandleDeathChoice();
            HandleStageClearChoice();
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

        public void ChooseDeathContinue()
        {
            var callback = _onContinueAfterDeath;
            if (callback == null)
                return;
            ClearDeathChoice();
            callback();
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

            if (keyboard.enterKey.wasPressedThisFrame)
                ChooseDeathContinue();
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

            _hpText.text = $"HP {stats.CurrentHealth:0}/{stats.MaxHealth:0}";
            _levelText.text = $"Lv.{levels.Level}  (ATK {stats.AttackPower:0})";
            _expText.text = $"EXP {levels.Exp}/{levels.ExpToNext}";
            _skillText.text = _player.IsAimingDash
                ? "<color=#7FD4FF>[Q] 대시 - 방향키로 방향 선택 (Q: 취소)</color>"
                : $"{SkillLabel("Q", "대시", _player.DashCooldown)}    {SkillLabel("E", "회전 베기", _player.SpinCooldown)}";

            RefreshTarget();

            if (_statPanel.activeSelf)
                _statText.text = BuildStatText();
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

            _statPanel.SetActive(!_statPanel.activeSelf);
            if (_statPanel.activeSelf)
                _statText.text = BuildStatText();
        }

        private static string FormatBonus(float rate) =>
            rate >= 0f ? $"+{rate * 100f:0.#}%" : $"<color=#FF7070>{rate * 100f:0.#}%</color>";

        private string BuildStatText()
        {
            var s = _player.Stats;
            var levels = _player.Levels;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("<b><color=#FFD966>[기본]</color></b>");
            sb.AppendLine($"레벨  Lv.{levels.Level}  (EXP {levels.Exp}/{levels.ExpToNext})");
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
            sb.AppendLine($"흡혈  {s.LifeStealRate * 100f:0.#}%");
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

        public void ShowLoopResetBanner() => ShowBanner("보스에게 당했다... 스탯은 그대로! 방 1부터 다시.");

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
        public void ShowDeathChoice(Action onContinue, Action onLobby, int goldPenalty)
        {
            _onContinueAfterDeath = onContinue;
            _onReturnToLobby = onLobby;
            LastDeathGoldPenalty = goldPenalty;
            _deathPenaltyText.text = goldPenalty > 0
                ? $"데스 패널티: 골드 -{goldPenalty} (이번 시도 획득분의 {LoopManager.DeathGoldPenaltyRate * 100f:0}%)"
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

            BuildStatusPanel(canvasGo.transform);
            BuildTargetPanel(canvasGo.transform);
            BuildStatPanel(canvasGo.transform);
            _upgradePanel = BuildOverlayPanel(canvasGo.transform, "UpgradePanel", new Vector2(440f, 320f), active: false);
            BuildBanner(canvasGo.transform);
            BuildVictoryPanel(canvasGo.transform);
            BuildDeathPanel(canvasGo.transform);
        }

        private void BuildStatusPanel(Transform parent)
        {
            var panelGo = new GameObject("StatusPanel", typeof(RectTransform));
            panelGo.transform.SetParent(parent, false);
            var rect = panelGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(400f, 166f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            panelGo.AddComponent<Image>().color = PanelColor;

            // CreateLabel(parent, text, offsetMin, offsetMax) - 상단 고정 앵커라 offsetMin이
            // "더 아래(더 음수)", offsetMax가 "더 위(덜 음수)" 쪽이어야 한다(offsetMax<offsetMin이면
            // 높이가 음수가 되는 실수를 한 번 했었다 - RectTransform 높이 = offsetMax.y - offsetMin.y).
            _levelText = CreateLabel(panelGo.transform, "Lv.1", new Vector2(10f, -28f), new Vector2(-10f, -8f));
            _progressText = CreateLabel(panelGo.transform, "Stage 1/10   Room 1/3   Attempt 1", new Vector2(10f, -52f), new Vector2(-10f, -32f));
            _progressText.fontSize = 13;
            _hpText = CreateLabel(panelGo.transform, "HP 30/30", new Vector2(10f, -84f), new Vector2(-10f, -64f));
            _expText = CreateLabel(panelGo.transform, "EXP 0/10", new Vector2(10f, -108f), new Vector2(-10f, -88f));

            // 조작 안내 한 줄 - 대기 키(스페이스바)를 모르고 지나치지 않게.
            _skillText = CreateLabel(panelGo.transform, "", new Vector2(10f, -134f), new Vector2(-10f, -114f));
            _skillText.fontSize = 14;
            _skillText.supportRichText = true;

            var controls = CreateLabel(panelGo.transform, "이동/공격: 방향키·WASD   대기: Space   스탯: Tab   메뉴: Esc",
                new Vector2(10f, -158f), new Vector2(-10f, -138f));
            controls.fontSize = 12;
            controls.color = new Color(0.7f, 0.72f, 0.78f);
        }

        private static string SkillLabel(string key, string name, int cooldown) =>
            cooldown > 0 ? $"<color=#777777>[{key}] {name} {cooldown}턴</color>" : $"<color=#7FD4FF>[{key}] {name} 준비</color>";

        private void BuildStatPanel(Transform parent)
        {
            // 오른쪽 위 토글 버튼
            var buttonGo = new GameObject("StatButton", typeof(RectTransform));
            buttonGo.transform.SetParent(parent, false);
            _statButtonRect = buttonGo.GetComponent<RectTransform>();
            _statButtonRect.anchorMin = new Vector2(1f, 1f);
            _statButtonRect.anchorMax = new Vector2(1f, 1f);
            _statButtonRect.pivot = new Vector2(1f, 1f);
            _statButtonRect.sizeDelta = new Vector2(120f, 34f);
            _statButtonRect.anchoredPosition = new Vector2(-16f, -16f);
            buttonGo.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.35f, 0.95f);

            var buttonText = CreateLabel(buttonGo.transform, "[Tab] 스탯", Vector2.zero, Vector2.zero);
            buttonText.rectTransform.anchorMin = Vector2.zero;
            buttonText.rectTransform.anchorMax = Vector2.one;
            buttonText.rectTransform.offsetMin = Vector2.zero;
            buttonText.rectTransform.offsetMax = Vector2.zero;
            buttonText.alignment = TextAnchor.MiddleCenter;

            // 버튼 아래 상태창
            _statPanel = new GameObject("StatPanel", typeof(RectTransform));
            _statPanel.transform.SetParent(parent, false);
            var rect = _statPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(320f, 440f);
            rect.anchoredPosition = new Vector2(-16f, -56f);
            _statPanel.AddComponent<Image>().color = PanelColor;

            _statText = CreateLabel(_statPanel.transform, string.Empty, Vector2.zero, Vector2.zero);
            _statText.rectTransform.anchorMin = Vector2.zero;
            _statText.rectTransform.anchorMax = Vector2.one;
            _statText.rectTransform.offsetMin = new Vector2(14f, 10f);
            _statText.rectTransform.offsetMax = new Vector2(-14f, -10f);
            _statText.supportRichText = true;
            _statText.lineSpacing = 1.05f;

            _statPanel.SetActive(false);
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
            rect.anchoredPosition = new Vector2(0f, -16f);
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
            rect.anchoredPosition = new Vector2(0f, -60f); // 상단 타겟 체력바(-16, 높이 34) 아래
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
            _deathPanel = BuildOverlayPanel(parent, "DeathPanel", new Vector2(460f, 200f), active: false);

            var title = CreateLabel(_deathPanel.transform, "사망! (레벨/스탯/장비는 그대로 유지됩니다)",
                new Vector2(10f, -70f), new Vector2(-10f, -15f));
            title.alignment = TextAnchor.MiddleCenter;
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;

            _deathPenaltyText = CreateLabel(_deathPanel.transform, string.Empty, new Vector2(10f, -105f), new Vector2(-10f, -72f));
            _deathPenaltyText.alignment = TextAnchor.MiddleCenter;
            _deathPenaltyText.fontSize = 15;
            _deathPenaltyText.color = new Color(1f, 0.6f, 0.4f);

            var prompt = CreateLabel(_deathPanel.transform, "[Enter] 계속하기 (방1부터 다시)    [L] 로비로 이동",
                new Vector2(10f, -170f), new Vector2(-10f, -110f));
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.fontSize = 16;
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
