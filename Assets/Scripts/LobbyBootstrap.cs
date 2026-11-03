using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoopRogue
{
    /// <summary>로비 씬의 진입점 - 골드/장비 표시 + 슬롯별 가챠 뽑기 + "시작". 예전엔 EventSystem/
    /// InputSystemUIInputModule을 런타임 AddComponent로 붙이면 기본 액션 바인딩이 비어있어 클릭이
    /// 씹힐 수 있다는 함정 때문에 전부 키보드로만 진행했는데, "상점 UI처럼 버튼으로도 구매 가능하게"
    /// 요청이 와서 그 함정 자체를 우회하는 방식으로 버튼 클릭을 추가했다: UGUI Button/EventSystem을
    /// 아예 안 쓰고, 매 프레임 Pointer.current로 좌클릭 여부만 읽어서 RectTransformUtility.
    /// RectangleContainsScreenPoint로 등록된 버튼 사각형들과 직접 히트테스트한다(Screen Space
    /// Overlay 캔버스라 카메라 인자 없이도 정확함) - EventSystem이 전혀 필요 없어서 바인딩 문제가
    /// 생길 여지 자체가 없다. 키보드 단축키는 기존 그대로 유지(버튼과 같은 메서드를 호출).</summary>
    public class LobbyBootstrap : MonoBehaviour
    {
        private const string MainSceneName = "Main";
        private const float ResultBannerDuration = 2.5f;

        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.35f);
        private static readonly Color ButtonLockedColor = new Color(0.18f, 0.18f, 0.18f);

        private readonly List<(RectTransform rect, Action onClick)> _buttons = new List<(RectTransform, Action)>();

        // 로비(허브)에서 상점 3곳으로 들어가 그 안에서만 사고판다 - 한 화면에 다 몰려 있던 걸 나눔.
        // 화면마다 GameObject 하나, 꺼진 화면의 버튼은 클릭·단축키 둘 다 안 받는다.
        private enum LobbyView { Hub, Gacha, Potion, Casino, Items }
        private LobbyView _view;
        private readonly Dictionary<LobbyView, GameObject> _viewRoots = new Dictionary<LobbyView, GameObject>();
        private Text _titleText;
        private GameObject _headerGroup;
        private GameObject _equipmentGroup;
        private GameObject _potionGroup;

        // 도트 그림(Resources/UI/Lobby, Resources/Backgrounds/Lobby_<화면>) - 타이틀과 같은 4배 픽셀. 화면마다 배경이 바뀌고,
        // 버튼·패널은 9-slice 돌판. 그림이 없으면 예전 단색 버튼/검은 배경 그대로.
        private const float PixelScale = 4f;
        private Sprite _buttonSprite;
        private Sprite _buttonHoverSprite;
        private Sprite _panelSprite;
        private Image _background;
        private Image _shade;
        private readonly Dictionary<LobbyView, Sprite> _backgrounds = new Dictionary<LobbyView, Sprite>();
        private readonly List<(RectTransform rect, Image image)> _skinnedButtons = new List<(RectTransform, Image)>();

        private Text _goldText;
        private Text _hubGoldText;  // 로비 방(허브) 오른쪽 위 골드 칸 - 상점 안은 위 제목 줄 골드
        private int _shownHubGold = -1;
        private Text _hubStoneText; // 골드 칸 바로 아래 마석 칸
        private int _shownHubStones = -1;
        private Text _exchangeButtonText;
        private Text _weaponText;
        private Text _armorText;
        private Text _accessoryText;
        private Text _potionText;
        private Text _resultText;
        private readonly Dictionary<ItemSlot, Text> _gachaButtonTexts = new Dictionary<ItemSlot, Text>();
        private readonly Dictionary<ItemSlot, Text> _gachaMultiButtonTexts = new Dictionary<ItemSlot, Text>();
        private readonly Dictionary<ItemSlot, GameObject> _gachaTicketButtons = new Dictionary<ItemSlot, GameObject>(); // 뽑기권 전부(있을 때만 보임)
        private readonly Dictionary<ItemSlot, Text> _gachaTicketButtonTexts = new Dictionary<ItemSlot, Text>();
        private const int TicketRevealMax = 10; // 뽑기권 일괄 - 카드 연출은 좋은 순서로 이만큼까지
        // 왼쪽 빈 공간의 등급 확률표 - 칸마다 글자 하나(등급 이름 열 + 슬롯 3열, 각 열은 등급 수만큼 줄).
        private readonly Dictionary<ItemSlot, Text> _chanceColumns = new Dictionary<ItemSlot, Text>();
        private Text _attackButtonText;
        private Text _healthButtonText;
        private Text _criticalButtonText;
        private Image _criticalButtonImage;
        private Coroutine _resultRoutine;

        // 슬롯머신 - 돌아가는 동안엔 당첨금을 골드 표시에서 빼서 결과를 미리 들키지 않게 한다.
        private const float SlotSpinDuration = 1.2f;
        private const float SlotReelStopInterval = 0.35f;
        private Text _slotReelsText;
        // 도트 슬롯(Resources/UI/Casino) - 틀 + 릴 창 3개에 기호 그림. 그림이 없으면 위 글자 릴로.
        private Image _slotFrameImage;
        private readonly Image[] _reelImages = new Image[3];
        private Sprite[] _symbolSprites;
        private Sprite _slotFrameSprite;
        private Sprite _slotFrameWinSprite;
        private Text _slotButtonText;
        private Text _slotHighButtonText; // 11-02 - 거는 돈 줄(예전 고액 베팅 버튼 자리)
        private Text _slotRiskButtonText;
        private Text _slotTitleText;
        private Text _slotBulkButtonText;
        private Text _slotAllButtonText;
        private RectTransform _slotBulkButton;
        private RectTransform _slotAllButton;
        private const int SlotBulkCount = 10; // [T] 이용권 한 번에 10장씩
        private bool _slotSpinning;
        private int _slotPendingPayout;
        // 연출 스킵 - 켜 두면 릴이 돌지 않고 바로 결과(저장), 꺼져 있어도 도는 중에 다시 누르면 그 판만 바로 멈춘다.
        private const string SlotSkipPrefKey = "LoopRogue_SlotSkipAnim";
        private bool _slotSkipAnim;
        private bool _slotSkipNow;
        private Text _slotSkipButtonText;

        private void Awake()
        {
            GoldWallet.EnsureLoaded();
            EquipmentWallet.EnsureLoaded();
            GachaSystem.EnsureLoaded();
            StatPotionWallet.EnsureLoaded();
            StageProgress.EnsureLoaded();

            // 씬 카메라가 유니티 기본 파란 배경이라 UI 뒤를 검은색으로.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            BuildUI();
            BuildLobbyRoom();
            RefreshEquipmentDisplay();
            RefreshGachaDisplay();
            RefreshPotionDisplay();
            RefreshCriticalDisplay();
            RefreshSlotDisplay();
            ShowView(LobbyView.Hub);
            NpcDialogUI.Create();
            _achievementPanel = AchievementPanel.Create();
            _codexPanel = CodexPanel.Create();
            _traitPanel = TraitPanel.Create();
            _stageSelectPanel = StageSelectPanel.Create();
            Achievements.Unlocked += OnAchievementUnlocked;
            Codex.SyncOwned();    // 도감이 생기기 전 저장으로 이미 가진 유물/아이템
            Achievements.Check(); // 업적이 생기기 전 저장으로 이미 조건을 채운 것들
        }

        private AchievementPanel _achievementPanel;
        private CodexPanel _codexPanel;
        private TraitPanel _traitPanel; // 11-02 특성 창 - 허브에서 T
        private StageSelectPanel _stageSelectPanel; // 11-02 층 선택 - 10층 클리어 뒤 문으로

        private void OnDestroy() => Achievements.Unlocked -= OnAchievementUnlocked;

        private void OnAchievementUnlocked(AchievementDef def)
        {
            var reward = def.RewardText;
            ShowResult(Loc.F("업적 달성! {0} - 칭호 「{1}」{2}", def.Name, def.Title, (reward.Length > 0 ? $" + {reward}" : "")), new Color(1f, 0.85f, 0.4f));
            RefreshGachaDisplay(); // 보상 뽑기권·이용권 장수
            RefreshSlotDisplay();
        }

        private void Update()
        {
            _goldText.text = Loc.F("보유 골드: {0}", GoldWallet.Gold - _slotPendingPayout) + "      " + Loc.F("마석: {0}", ManaStoneWallet.Stones);
            var hubGold = GoldWallet.Gold - _slotPendingPayout;
            if (_shownHubGold != hubGold)
            {
                _shownHubGold = hubGold;
                _hubGoldText.text = hubGold.ToString("N0");
            }
            if (_shownHubStones != ManaStoneWallet.Stones)
            {
                _shownHubStones = ManaStoneWallet.Stones;
                _hubStoneText.text = _shownHubStones.ToString("N0");
            }
            RefreshHubTraitButton();
            UpdateHover();

            // 안내자와 대화 중이거나 업적 창이 열려 있으면(닫은 프레임 포함) 로비 버튼·단축키를 안 받는다 - 그 창들이 Esc/숫자/클릭을 직접 처리한다.
            if (NpcDialogUI.BlocksInput || AchievementPanel.BlocksInput || CodexPanel.BlocksInput || TraitPanel.BlocksInput || StageSelectPanel.BlocksInput || GachaReveal.BlocksInput)
                return;

            // 클릭으로 화면이 바뀐 프레임엔 단축키를 안 읽는다(같은 프레임에 새 화면 키가 먹지 않게).
            if (HandleMouseClick())
                return;


            var shift = GameInput.Held(Key.LeftShift) || GameInput.Held(Key.RightShift);
            switch (_view)
            {
                case LobbyView.Hub:
                    if (GameInput.Down(Key.Digit1))
                        ShowView(LobbyView.Gacha);
                    else if (GameInput.Down(Key.Digit2))
                        ShowView(LobbyView.Potion);
                    else if (GameInput.Down(Key.Digit3))
                        ShowView(LobbyView.Casino);
                    else if (GameInput.Down(Key.Digit4))
                        LobbyGuide.Talk();
                    else if (GameInput.Down(Key.Digit5))
                        _achievementPanel.Open();
                    else if (GameInput.Down(Key.Digit6))
                        _codexPanel.Open();
                    else if (GameInput.Down(Key.Digit7))
                        ShowView(LobbyView.Items);
                    else if (GameInput.Down(Key.Digit8))
                        TalkSkillHelper();
                    else if (GameInput.Down(Key.T))
                        _traitPanel.Open();
                    // Esc = 타이틀로(로비엔 진행 중인 전투가 없어서 확인 없이 바로 이동 - 골드/장비는 전부 영구 저장).
                    else if (GameInput.Down(Key.Escape))
                        SceneManager.LoadScene("Title");
                    else if (GameInput.PointerDown)
                        _room.HandleClick(GameInput.PointerPosition); // UI 버튼에 안 맞은 클릭 = 방 안 클릭
                    else
                        _room.Tick(); // 걷기·말 걸기·문
                    return;

                case LobbyView.Gacha:
                    // 확률표 창이 열려 있으면 P/Esc로 닫기만(뒤 화면 단축키는 안 받음)
                    if (ChanceOpen)
                    {
                        if (GameInput.Down(Key.P) || GameInput.Down(Key.Escape))
                            SetChanceOpen(false);
                        return;
                    }
                    if (GameInput.Down(Key.P))
                    {
                        SetChanceOpen(true);
                        return;
                    }
                    if (GameInput.Down(Key.E))
                        DoManaExchange(shift ? ExchangeBulkCount : 1);
                    // 숫자키 = 1회, Shift+숫자키 = 10연차, Ctrl+숫자키 = 뽑기권 전부(11-03)
                    var ctrl = GameInput.Held(Key.LeftCtrl) || GameInput.Held(Key.RightCtrl);
                    if (GameInput.Down(Key.Digit1))
                        DoGachaKey(ItemSlot.Weapon, shift, ctrl);
                    if (GameInput.Down(Key.Digit2))
                        DoGachaKey(ItemSlot.Armor, shift, ctrl);
                    if (GameInput.Down(Key.Digit3))
                        DoGachaKey(ItemSlot.Accessory, shift, ctrl);
                    break;

                case LobbyView.Items:
                    for (var i = 0; i < ItemInfo.Count; i++)
                        if (GameInput.Down(i == 9 ? Key.Digit0 : Key.Digit1 + i))
                            DoItemBuy((ItemType)i);
                    break;

                case LobbyView.Potion:
                    // 10-28 사용자 - A/H/C 대신 뽑기 상점처럼 숫자 1/2/3
                    if (GameInput.Down(Key.Digit1))
                        DoPotionBuy(PotionType.Attack, Loc.T("공격력"));
                    if (GameInput.Down(Key.Digit2))
                        DoPotionBuy(PotionType.Health, Loc.T("체력"));
                    if (GameInput.Down(Key.Digit3))
                        DoPotionBuy(PotionType.Critical, Loc.T("치명타"));
                    break;

                case LobbyView.Casino:
                    if (GameInput.Down(Key.S))
                        DoSlotSpin();
                    if (GameInput.Down(Key.A) || GameInput.Down(Key.LeftArrow))
                        AdjustSlotBet(-(shift ? SlotMachine.BigBetStep : SlotMachine.BetStep));
                    if (GameInput.Down(Key.D) || GameInput.Down(Key.RightArrow))
                        AdjustSlotBet(shift ? SlotMachine.BigBetStep : SlotMachine.BetStep);
                    if (GameInput.Down(Key.Q))
                        AdjustSlotMultiplier(-SlotMachine.MultiplierStep);
                    if (GameInput.Down(Key.E))
                        AdjustSlotMultiplier(SlotMachine.MultiplierStep);
                    if (GameInput.Down(Key.T))
                        DoSlotTicketBulk(shift ? int.MaxValue : SlotBulkCount);
                    if (GameInput.Down(Key.K))
                        ToggleSlotSkip();
                    break;
            }

            if (GameInput.Down(Key.Escape))
                ShowView(LobbyView.Hub);
        }

        /// <summary>눌린 버튼이 있으면 실행하고 true. 꺼진 화면의 버튼은 건너뛴다.</summary>
        private bool HandleMouseClick()
        {
            var mouse = Pointer.current;
            if (mouse == null || !GameInput.PointerDown)
                return false;

            var screenPos = GameInput.PointerPosition;
            foreach (var (rect, onClick) in _buttons)
            {
                if (ChanceOpen && !rect.IsChildOf(_chanceGroup.transform))
                    continue; // 확률표 창이 덮고 있을 땐 그 창 버튼만
                if (rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null))
                {
                    onClick();
                    return true;
                }
            }
            return false;
        }

        private void ShowView(LobbyView view)
        {
            _view = view;
            MusicPlayer.Play(view switch // 로비·뽑기·도박장 배경음악(파일이 없으면 조용히, 연금술사는 로비 곡)
            {
                LobbyView.Gacha => MusicPlayer.Gacha,
                LobbyView.Casino => MusicPlayer.Casino,
                _ => MusicPlayer.Lobby,
            });
            SetChanceOpen(false);
            foreach (var pair in _viewRoots)
                pair.Value.SetActive(pair.Key == view);
            // 장비 요약은 로비와 뽑기 상점 둘 다에서 보여준다(뽑으면서 지금 장비를 봐야 해서).
            // (허브는 걸어 다니는 방이라 요약 패널이 방을 가리지 않게 상점 안에서만)
            _equipmentGroup.SetActive(view == LobbyView.Gacha);
            _headerGroup.SetActive(view != LobbyView.Hub);
            _potionGroup.SetActive(view == LobbyView.Potion);
            if (view == LobbyView.Items)
                RefreshItemShopDisplay();
            // 허브는 배경 그림 대신 방(LobbyRoom)이 보인다. 상점 안은 배경 그림 + 어둡게(그림이 없으면 방이 어둡게 비침).
            var hub = view == LobbyView.Hub;
            _background.enabled = !hub && _backgrounds.TryGetValue(view, out var background) && background != null;
            if (_background.enabled)
                _background.sprite = _backgrounds[view];
            _shade.color = new Color(0f, 0f, 0f, hub ? 0f : 0.55f);
            _titleText.text = view switch
            {
                LobbyView.Gacha => Loc.T("뽑기 상점"),
                LobbyView.Potion => Loc.T("영약 상점"),
                LobbyView.Casino => Loc.T("도박장"),
                LobbyView.Items => Loc.T("잡화점"),
                _ => $"Stage {StageProgress.CurrentStage}/{StageProgress.MaxStage}",
            };
        }

        private void StartRun() => SceneManager.LoadScene(MainSceneName);

        /// <summary>로비 문 - 10층 보스를 잡고 "그대로 계속"을 골랐으면 층 선택 창부터(11-02 사용자), 아니면 바로 지금 층.</summary>
        private void EnterDoor()
        {
            if (!StageProgress.FinalBossCleared)
            {
                StartRun();
                return;
            }
            _stageSelectPanel.Open(stage =>
            {
                StageProgress.SelectStage(stage);
                StartRun();
            }, () => _room.StepBackFromDoor());
        }

        private void DoPotionBuy(PotionType type, string label)
        {
            if (!StatPotionWallet.IsUnlocked(type))
            {
                ShowResult(Loc.F("{0} 영약은 스테이지 {1}부터 구매할 수 있습니다.", label, StatPotionWallet.CriticalUnlockStage), Color.white);
                return;
            }

            var cost = StatPotionWallet.GetNextCost(type);
            if (!StatPotionWallet.TryBuy(type))
            {
                ShowResult(Loc.F("골드가 모자랍니다! ({0} 영약 비용: {1})", label, cost), Color.white);
                return;
            }

            ShowResult(Loc.F("{0} 영약 구매! (영구 적용, 다음 시작부터)", label), new Color(0.4f, 1f, 0.5f));
            RefreshPotionDisplay();
            RefreshCriticalDisplay();
            OnShopPurchase();
        }

        private void RefreshPotionDisplay()
        {
            _potionText.text =
                Loc.F("영약 누적 - 공격력+{0:0.#}   체력+{1:0.#}   |   유물 {2}개", StatPotionWallet.TotalAttackBonus(), StatPotionWallet.TotalHealthBonus(), Relics.OwnedCount);

            _attackButtonText.text =
                Loc.F("[1] 공격력 영약 구매 (다음 비용 {0}골드)", StatPotionWallet.GetNextCost(PotionType.Attack));
            _healthButtonText.text =
                Loc.F("[2] 체력 영약 구매 (다음 비용 {0}골드)", StatPotionWallet.GetNextCost(PotionType.Health));
        }

        private void RefreshCriticalDisplay()
        {
            var unlocked = StatPotionWallet.IsUnlocked(PotionType.Critical);
            _criticalButtonImage.color = ButtonTint(unlocked ? ButtonColor : ButtonLockedColor);
            if (_criticalIcon != null)
                _criticalIcon.color = unlocked ? Color.white : new Color(0.35f, 0.35f, 0.38f); // 잠겨 있으면 병도 어둡게

            _criticalButtonText.text = unlocked
                ? Loc.F("[3] 치명타확률+{0:0.#}% 구매 (다음 비용 {1}골드)", StatPotionWallet.TotalCriticalChanceBonus() * 100f, StatPotionWallet.GetNextCost(PotionType.Critical))
                : Loc.F("치명타 영약: 스테이지 {0}부터 구매 가능(비공개)", StatPotionWallet.CriticalUnlockStage);
        }

        private void DoGachaPull(ItemSlot slot)
        {
            var result = GachaSystem.Pull(slot);
            if (result == null)
            {
                ShowResult(Loc.F("골드가 모자랍니다! (뽑기 비용: {0})", GachaSystem.GetPullCost(slot)), Color.white);
                return;
            }

            var r = result.Value;
            var name = EquipmentData.DisplayName(r.Slot, r.Grade);
            var color = EquipmentData.GradeColor(r.Grade);

            var message = r.Equipped ? Loc.F("{0} 획득! 장착했습니다.", name)
                : r.EnhanceExp > 0 ? Loc.F("{0}... 이미 더 좋은 장비가 있어 강화 경험치 +{1}", name, r.EnhanceExp)
                : Loc.F("{0}... 이미 더 좋은 장비가 있음(강화 MAX)", name);
            if (r.EnhanceLevelsGained > 0)
                message += "  " + EnhanceLevelMessage(slot);
            var newTier = EquipmentEffects.TierOf(r.Grade);
            if (r.Equipped && newTier > 0)
                message += Loc.F("  새 효과: {0} ({1})", EquipmentEffects.Name(slot, newTier), EquipmentEffects.Description(slot, newTier));
            if (r.ShopLeveledUp)
                message += Loc.F("  {0} 상점 Lv{1} 달성!", EquipmentData.Templates[slot].BaseName, GachaSystem.GetShopLevel(slot));

            RefreshGachaDisplay(); // 가격·뽑기권은 바로, 장비 요약·결과 줄은 연출이 끝난 뒤(미리 안 들키게)
            GachaReveal.Play(slot, new[] { (r.Grade, r.Equipped, r.EnhanceExp) }, () =>
            {
                ShowResult(message, color);
                RefreshEquipmentDisplay();
                OnShopPurchase();
            });
        }

        private void DoGachaKey(ItemSlot slot, bool shift, bool ctrl)
        {
            if (ctrl)
                DoGachaAllTickets(slot);
            else
                DoGacha(slot, shift);
        }

        /// <summary>뽑기권 전부를 그 부위에(11-03 사용자) - 결과 한 줄 요약 + 카드 연출은 좋은 순서로 TicketRevealMax장까지.</summary>
        private void DoGachaAllTickets(ItemSlot slot)
        {
            var results = GachaSystem.PullAllTickets(slot);
            if (results == null)
            {
                ShowResult(Loc.T("뽑기권이 없습니다 - 던전의 떠돌이 상인에게서 살 수 있다"), Color.white);
                return;
            }
            var best = results[0];
            var anyEquipped = false;
            var leveledUp = false;
            var expTotal = 0;
            var enhanceUp = 0;
            foreach (var r in results)
            {
                if (r.Grade > best.Grade)
                    best = r;
                anyEquipped |= r.Equipped;
                leveledUp |= r.ShopLeveledUp;
                expTotal += r.EnhanceExp;
                enhanceUp += r.EnhanceLevelsGained;
            }
            var name = EquipmentData.DisplayName(slot, best.Grade);
            var message = anyEquipped
                ? Loc.F("뽑기권 {0}장 사용 - 최고: {1} - 장착했습니다!", results.Count, name)
                : Loc.F("뽑기권 {0}장 사용 - 최고: {1}... 이미 더 좋은 장비가 있음.", results.Count, name);
            if (expTotal > 0)
                message += Loc.F("  강화 경험치 +{0}", expTotal);
            if (enhanceUp > 0)
                message += "  " + EnhanceLevelMessage(slot);
            if (leveledUp)
                message += Loc.F("  {0} 상점 Lv{1} 달성!", EquipmentData.Templates[slot].BaseName, GachaSystem.GetShopLevel(slot));

            RefreshGachaDisplay();
            var shown = new List<GachaResult>(results);
            if (shown.Count > TicketRevealMax) // 카드는 좋은 등급부터(장착된 것 먼저)
            {
                shown.Sort((a, b) => b.Grade != a.Grade ? b.Grade.CompareTo(a.Grade) : b.Equipped.CompareTo(a.Equipped));
                shown.RemoveRange(TicketRevealMax, shown.Count - TicketRevealMax);
            }
            var reveal = new List<(ItemGrade, bool, int)>();
            foreach (var r in shown)
                reveal.Add((r.Grade, r.Equipped, r.EnhanceExp));
            GachaReveal.Play(slot, reveal, () =>
            {
                ShowResult(message, EquipmentData.GradeColor(best.Grade));
                RefreshEquipmentDisplay();
                OnShopPurchase();
            });
        }

        private void DoGacha(ItemSlot slot, bool multi)
        {
            if (multi)
                DoGachaMultiPull(slot);
            else
                DoGachaPull(slot);
        }

        /// <summary>10연차 결과는 한 줄 요약 - 최고 등급 하나 + 장착 여부 + 상점 레벨업.</summary>
        private void DoGachaMultiPull(ItemSlot slot)
        {
            var cost = GachaSystem.GetMultiPullCost(slot);
            var results = GachaSystem.PullMulti(slot);
            if (results == null)
            {
                ShowResult(Loc.F("골드가 모자랍니다! (10연차 비용: {0})", cost), Color.white);
                return;
            }

            var best = results[0];
            var anyEquipped = false;
            var leveledUp = false;
            var expTotal = 0;
            var enhanceUp = 0;
            foreach (var r in results)
            {
                if (r.Grade > best.Grade)
                    best = r;
                anyEquipped |= r.Equipped;
                leveledUp |= r.ShopLeveledUp;
                expTotal += r.EnhanceExp;
                enhanceUp += r.EnhanceLevelsGained;
            }

            var name = EquipmentData.DisplayName(slot, best.Grade);
            var message = anyEquipped
                ? Loc.F("10연차 최고: {0} - 장착했습니다!", name)
                : Loc.F("10연차 최고: {0}... 이미 더 좋은 장비가 있음.", name);
            if (expTotal > 0)
                message += Loc.F("  강화 경험치 +{0}", expTotal);
            if (enhanceUp > 0)
                message += "  " + EnhanceLevelMessage(slot);
            if (leveledUp)
                message += Loc.F("  {0} 상점 Lv{1} 달성!", EquipmentData.Templates[slot].BaseName, GachaSystem.GetShopLevel(slot));

            RefreshGachaDisplay();
            var reveal = new List<(ItemGrade, bool, int)>();
            foreach (var r in results)
                reveal.Add((r.Grade, r.Equipped, r.EnhanceExp));
            GachaReveal.Play(slot, reveal, () =>
            {
                ShowResult(message, EquipmentData.GradeColor(best.Grade));
                RefreshEquipmentDisplay();
                OnShopPurchase();
            });
        }

        private void RefreshGachaDisplay()
        {
            foreach (var pair in _gachaButtonTexts)
            {
                var slot = pair.Key;
                var level = GachaSystem.GetShopLevel(slot);
                var next = GachaSystem.GetPullsForNextLevel(slot);
                var progress = next.HasValue ? Loc.F("{0}/{1}회", GachaSystem.GetPullCount(slot), next.Value) : "MAX";
                var price = GachaSystem.Tickets > 0 ? Loc.F("뽑기권 {0}장", GachaSystem.Tickets) : $"{GachaSystem.GetPullCost(slot)}G"; // 뽑기권이 있으면 먼저 쓴다
                pair.Value.text =
                    Loc.F("[{0}] {1} 뽑기 ({2})\n상점 Lv{3} ({4})", (int)slot + 1, EquipmentData.Templates[slot].BaseName, price, level, progress);
            }

            foreach (var pair in _gachaMultiButtonTexts)
                pair.Value.text = Loc.F("[Shift+{0}] 10연차 ({1}G)", (int)pair.Key + 1, GachaSystem.GetMultiPullCost(pair.Key));
            foreach (var pair in _gachaTicketButtons)
            {
                pair.Value.SetActive(GachaSystem.Tickets > 0);
                _gachaTicketButtonTexts[pair.Key].text = Loc.F("[Ctrl+{0}] 뽑기권 전부 ({1}장)", (int)pair.Key + 1, GachaSystem.Tickets);
            }

            // 확률표 - 그 슬롯 지금 상점 레벨 기준, 아직 안 열린 등급은 "-".
            foreach (var pair in _chanceColumns)
            {
                var chances = GachaSystem.GetGradeChances(pair.Key);
                var sb = new System.Text.StringBuilder();
                sb.Append($"{EquipmentData.Templates[pair.Key].BaseName}\nLv{GachaSystem.GetShopLevel(pair.Key)}");
                foreach (var c in chances)
                    sb.Append(c > 0f ? $"\n{c:0.#}%" : "\n<color=#666666>-</color>");
                pair.Value.text = sb.ToString();
            }
        }

        private void DoSlotSpin()
        {
            if (_slotSpinning)
            {
                _slotSkipNow = true; // 도는 중에 다시 누르면 바로 멈춤
                return;
            }

            var result = SlotMachine.Spin(useTicket: false);
            if (result == null)
            {
                ShowResult(Loc.F("골드가 모자랍니다! (베팅액: {0})", SlotMachine.Bet), Color.white);
                return;
            }

            RefreshSlotDisplay(); // 이용권 장수
            StartCoroutine(SlotSpinRoutine(result.Value));
        }

        /// <summary>이용권 여러 장을 한 번에 - [T] 10장씩(10장 미만이면 남은 전부), [Shift+T] 전부. 결과는 한꺼번에 정하고,
        /// 릴 연출은 한 번만(그중 제일 좋은 결과의 그림으로) 돌린 뒤 합계를 보여 준다.</summary>
        private void DoSlotTicketBulk(int count)
        {
            if (_slotSpinning)
            {
                _slotSkipNow = true;
                return;
            }
            if (SlotMachine.Tickets <= 0)
                return;

            var results = new List<SlotMachine.SpinResult>();
            while (results.Count < count && SlotMachine.Tickets > 0)
            {
                var result = SlotMachine.Spin(useTicket: true);
                if (result == null)
                    break;
                results.Add(result.Value);
            }
            if (results.Count == 0)
                return;

            RefreshSlotDisplay();
            StartCoroutine(SlotBulkRoutine(results));
        }

        private IEnumerator SlotBulkRoutine(List<SlotMachine.SpinResult> results)
        {
            var best = results[0];
            int total = 0, wins = 0;
            foreach (var r in results)
            {
                total += r.Payout;
                if (r.Payout > 0) wins++;
                if (r.MatchCount > best.MatchCount)
                    best = r;
            }

            _slotPendingPayout = total; // 연출이 끝날 때까지 보유 골드에 안 보이게(한 장 돌릴 때와 같다)
            yield return SlotSpinRoutine(best, showResult: false);
            _slotPendingPayout = 0;

            var summary = Loc.F("이용권 {0}장 사용 ({1}배) - 당첨 {2} / 꽝 {3}  →  총 {4}골드 획득", results.Count, best.Multiplier, wins, results.Count - wins, total);
            ShowResult(summary, wins > 0 ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.5f, 0.5f));
        }

        /// <summary>결과는 이미 정해졌고 연출만 한다 - 전부 돌다가 왼쪽 릴부터 하나씩 멈춘다.</summary>
        private IEnumerator SlotSpinRoutine(SlotMachine.SpinResult result, bool showResult = true)
        {
            _slotSpinning = true;
            _slotPendingPayout = result.Payout;
            SetSlotLook(dim: false, win: false);

            var shown = new int[3];
            var stopped = 0;
            var elapsed = 0f;
            _slotSkipNow = false;
            while (stopped < 3)
            {
                if (_slotSkipAnim || _slotSkipNow)
                    elapsed = SlotSpinDuration + SlotReelStopInterval * 3f; // 연출 스킵 - 남은 릴을 한꺼번에 세운다
                while (stopped < 3 && elapsed >= SlotSpinDuration + SlotReelStopInterval * stopped)
                {
                    shown[stopped] = result.Reels[stopped];
                    stopped++;
                }

                for (var i = stopped; i < 3; i++)
                    shown[i] = UnityEngine.Random.Range(0, SlotMachine.Symbols.Length);

                ShowReels(shown);
                if (stopped >= 3)
                    break; // 다 멈췄으면 기다리지 않는다(스킵이 켜져 있으면 누르는 즉시 결과)
                yield return new WaitForSeconds(0.06f);
                elapsed += 0.06f;
            }

            _slotSkipNow = false;
            _slotPendingPayout = 0;
            _slotSpinning = false;

            if (!showResult)
            {
                SetSlotLook(dim: result.MatchCount == 1, win: result.MatchCount == 3);
                yield break; // 여러 장 한 번에 - 합계는 호출부(SlotBulkRoutine)가
            }

            if (result.MatchCount == 3)
            {
                SetSlotLook(dim: false, win: true);
                ShowResult(Loc.F("당첨! {0}배 - {1}골드 획득!", result.Multiplier, result.Payout), new Color(1f, 0.85f, 0.3f));
            }
            else if (result.MatchCount == 2 && result.Payout > 0) // 대박 배율은 2개 일치가 꽝
            {
                ShowResult(Loc.F("2개 일치 - {0}골드 돌려받음 (베팅 {1})", result.Payout, result.Bet), new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                SetSlotLook(dim: true, win: false);
                ShowResult(result.UsedTicket ? Loc.T("꽝... 이용권 1장을 썼습니다.") : Loc.F("꽝... {0}골드를 잃었습니다.", result.Bet), new Color(1f, 0.5f, 0.5f));
            }
        }

        private void ShowReels(int[] reels)
        {
            if (_reelImages[0] == null)
            {
                _slotReelsText.text = FormatReels(reels);
                return;
            }
            for (var i = 0; i < _reelImages.Length; i++)
                _reelImages[i].sprite = _symbolSprites[reels[i]];
        }

        /// <summary>잭팟 = 틀이 금빛, 꽝 = 기호가 흐리게(글자 릴이면 글자 색으로).</summary>
        private void SetSlotLook(bool dim, bool win)
        {
            if (_reelImages[0] == null)
            {
                _slotReelsText.color = win ? new Color(1f, 0.85f, 0.3f) : dim ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
                return;
            }
            _slotFrameImage.sprite = win ? _slotFrameWinSprite : _slotFrameSprite;
            foreach (var reel in _reelImages)
                reel.color = dim ? new Color(0.5f, 0.5f, 0.5f) : Color.white;
        }

        private static string FormatReels(int[] reels) =>
            $"[ {SlotMachine.Symbols[reels[0]]} | {SlotMachine.Symbols[reels[1]]} | {SlotMachine.Symbols[reels[2]]} ]";

        private void RefreshSlotDisplay()
        {
            var multiplier = SlotMachine.Multiplier;
            _slotTitleText.text = Loc.T("운명의 슬롯") + "\n" + Loc.F("3개 일치 {0}배 · 당첨 확률 {1:0.#}%", multiplier, SlotMachine.WinChance(multiplier) * 100f);
            _slotButtonText.text = Loc.F("[S] 돌리기 ({0}G)", SlotMachine.Bet);
            _slotHighButtonText.text = Loc.F("[A] ◀  거는 돈 {0}G  ▶ [D]   <size=12>(최대 {1}G, Shift = 100씩)</size>", SlotMachine.Bet, SlotMachine.MaxBet);
            _slotRiskButtonText.text = Loc.F("[Q] ◀  배율 {0}배  ▶ [E]", multiplier);

            // 이용권 - 1장 이상이면 [T](10장씩, 모자라면 남은 전부), 전부 쓰기는 10장보다 많을 때만. 이용권 한 장 = {TicketBet}G를 지금 배율로
            var tickets = SlotMachine.Tickets;
            _slotBulkButton.gameObject.SetActive(tickets >= 1);
            _slotAllButton.gameObject.SetActive(tickets > SlotBulkCount);
            _slotBulkButton.anchoredPosition = new Vector2(tickets > SlotBulkCount ? -84f : 0f, _slotBulkButton.anchoredPosition.y); // 혼자면 가운데
            _slotBulkButtonText.text = tickets >= SlotBulkCount
                ? Loc.F("[T] {0}장 쓰기", SlotBulkCount)
                : Loc.F("[T] {0}장 다 쓰기", tickets);
            _slotAllButtonText.text = Loc.F("[Shift+T] 전부 ({0}장)", tickets);
            _slotSkipButtonText.text = _slotSkipAnim ? Loc.T("[K] 연출 스킵: 켬") : Loc.T("[K] 연출 스킵: 끔");
        }

        /// <summary>거는 돈 ± (A/D, Shift면 100씩) - 최소 10, 최대 100 + 층 × 100(11-02 사용자).</summary>
        private void AdjustSlotBet(int delta)
        {
            if (_slotSpinning)
                return;
            SlotMachine.Bet += delta;
            RefreshSlotDisplay();
        }

        /// <summary>배율 ± 0.5(Q/E) - 2~5배, 높을수록 당첨 확률이 낮다(11-02 사용자).</summary>
        private void AdjustSlotMultiplier(float delta)
        {
            if (_slotSpinning)
                return;
            SlotMachine.Multiplier += delta;
            RefreshSlotDisplay();
        }

        private void ToggleSlotSkip()
        {
            _slotSkipAnim = !_slotSkipAnim;
            Prefs.SetInt(SlotSkipPrefKey, _slotSkipAnim ? 1 : 0);
            Prefs.Save();
            if (_slotSpinning && _slotSkipAnim)
                _slotSkipNow = true;
            RefreshSlotDisplay();
        }

        private void RefreshEquipmentDisplay()
        {
            _weaponText.text = FormatSlot(ItemSlot.Weapon);
            _armorText.text = FormatSlot(ItemSlot.Armor);
            _accessoryText.text = FormatSlot(ItemSlot.Accessory);
        }

        private static string FormatSlot(ItemSlot slot)
        {
            var grade = EquipmentWallet.GetEquipped(slot);
            if (!grade.HasValue)
                return Loc.F("{0}: (미장착)", EquipmentData.Templates[slot].BaseName);

            var atk = EquipmentData.AttackBonus(slot, grade.Value) * EquipmentEnhance.Multiplier(slot);
            var hp = EquipmentData.HealthBonus(slot, grade.Value) * EquipmentEnhance.Multiplier(slot);
            var tier = EquipmentEffects.TierOf(grade.Value);
            var effects = tier > 0 ? Loc.F("  <color=#FFD966>효과 {0}/{1}</color>", tier, EquipmentEffects.MaxTier) : ""; // 효과 설명은 게임 중 Tab 상태창
            if (slot == ItemSlot.Accessory && EquipmentEffects.HasMemorySet)
                effects += Loc.F("  <color=#7FE0D8>세트 「{0}」</color>", EquipmentEffects.MemorySetName);
            var enhance = Loc.F("  <color=#FFD27A>강화 +{0} ({1})</color>", EquipmentEnhance.GetLevel(slot), EquipmentEnhance.ProgressText(slot));
            return Loc.F("{0}: {1} (공격력+{2:0.#} 체력+{3:0.#}){4}", EquipmentData.Templates[slot].BaseName, EquipmentData.DisplayName(slot, grade.Value) + EquipmentEnhance.Suffix(slot), atk, hp, effects + enhance);
        }

        private static string EnhanceLevelMessage(ItemSlot slot) =>
            Loc.F("<color=#FFD27A>{0} 강화 +{1} 달성! (장비 스탯 +{2:0}%)</color>", EquipmentData.Templates[slot].BaseName,
                EquipmentEnhance.GetLevel(slot), EquipmentEnhance.GetLevel(slot) * EquipmentEnhance.StatPerLevel * 100f);

        private void ShowResult(string message, Color color)
        {
            _resultText.text = message;
            _resultText.color = color;
            _resultText.gameObject.SetActive(true);

            if (_resultRoutine != null)
                StopCoroutine(_resultRoutine);
            _resultRoutine = StartCoroutine(HideResultAfter(ResultBannerDuration));
        }

        private IEnumerator HideResultAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _resultText.gameObject.SetActive(false);
            _resultRoutine = null;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("LobbyCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // 16:9가 아닌 화면에서도 잘리지 않게
            canvasGo.AddComponent<GraphicRaycaster>();
            var root = canvasGo.transform;

            LoadArt();
            BuildBackground(root);

            // 상점 화면 공통: 제목(상점 이름) / 골드 - 로비 방에선 숨김(스테이지는 문 위에, 골드는 상점에서만 필요 - 10-14 사용자)
            _headerGroup = CreateGroup(root, "Header");
            CreatePanel(_headerGroup.transform, 0f, 282f, 200f, 80f);
            _titleText = CreateLabel(_headerGroup.transform, "Title", string.Empty, 26, FontStyle.Bold, Color.white, 300f, 560f);
            _goldText = CreateLabel(_headerGroup.transform, "GoldText", Loc.T("보유 골드: 0"), 18, FontStyle.Bold,
                new Color(1f, 0.85f, 0.3f), 262f, 560f);
            // 결과 안내 한 줄은 모든 화면 공통
            _resultText = CreateLabel(root, "ResultText", string.Empty, 18, FontStyle.Normal, Color.white, 222f, 1000f);
            _resultText.gameObject.SetActive(false);

            // 로비·뽑기 상점 공통: 지금 장비 / 로비·영약 상점 공통: 영약 누적
            _equipmentGroup = CreateGroup(root, "EquipmentGroup");
            CreatePanel(_equipmentGroup.transform, 0f, 143f, 760f, 104f);
            _weaponText = CreateLabel(_equipmentGroup.transform, "WeaponText", string.Empty, 16, FontStyle.Normal, Color.white, 170f, 700f);
            _armorText = CreateLabel(_equipmentGroup.transform, "ArmorText", string.Empty, 16, FontStyle.Normal, Color.white, 140f, 700f);
            _accessoryText = CreateLabel(_equipmentGroup.transform, "AccessoryText", string.Empty, 16, FontStyle.Normal, Color.white, 110f, 700f);
            _potionGroup = CreateGroup(root, "PotionGroup");
            CreatePanel(_potionGroup.transform, 0f, 73f, 760f, 40f);
            _potionText = CreateLabel(_potionGroup.transform, "PotionText", string.Empty, 16, FontStyle.Normal,
                new Color(0.6f, 1f, 0.7f), 73f, 700f);

            BuildHubView(root);
            BuildGachaView(root);
            BuildPotionView(root);
            BuildCasinoView(root);
            BuildItemShopView(root);
        }

        private void LoadArt()
        {
            _buttonSprite = Resources.Load<Sprite>("UI/Common/Button");
            _buttonHoverSprite = Resources.Load<Sprite>("UI/Common/Button_Hover");
            _panelSprite = Resources.Load<Sprite>("UI/Common/Panel");
            foreach (LobbyView view in Enum.GetValues(typeof(LobbyView)))
                _backgrounds[view] = Resources.Load<Sprite>($"Backgrounds/Lobby_{view}");
        }

        /// <summary>화면 비율이 16:9가 아니어도 빈틈 없이 덮게(넘치는 쪽은 잘림) + 위에 어둠 한 겹(ShowView가 화면마다 진하기 조절).</summary>
        private void BuildBackground(Transform parent)
        {
            var go = new GameObject("Background", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _background = go.AddComponent<Image>();
            _background.enabled = false; // ShowView가 그림을 넣을 때 켠다(그림 없으면 카메라의 검은 배경)
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1280f, 720f);
            var fitter = go.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            var shade = CreateGroup(parent, "Shade");
            _shade = shade.AddComponent<Image>();
            _shade.color = new Color(0f, 0f, 0f, 0.2f);
        }

        /// <summary>돌 테두리 패널(9-slice, 가운데 반투명 어둠) - 글자 묶음 뒤에 깐다. 글자보다 먼저 만들어야 뒤에 그려진다.</summary>
        private void CreatePanel(Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            if (_panelSprite != null)
            {
                image.sprite = _panelSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f / PixelScale;
            }
            else
                image.color = new Color(0f, 0f, 0f, 0.5f);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>돌판 그림 버튼은 원래 단색을 옅은 물빛으로만 남긴다(그림 색이 묻히지 않게).</summary>
        private Color ButtonTint(Color bgColor)
        {
            if (_buttonSprite == null)
                return bgColor;
            var tint = Color.Lerp(Color.white, bgColor * 2.2f, 0.35f);
            tint.a = 1f;
            return tint;
        }

        private void UpdateHover()
        {
            var mouse = Pointer.current;
            if (mouse == null || _buttonHoverSprite == null)
                return;
            var screenPos = GameInput.PointerPosition;
            foreach (var (rect, image) in _skinnedButtons)
            {
                if (!rect.gameObject.activeInHierarchy)
                    continue;
                var sprite = RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null) ? _buttonHoverSprite : _buttonSprite;
                if (image.sprite != sprite)
                    image.sprite = sprite;
            }
        }

        private static GameObject CreateGroup(Transform parent, string goName)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private Transform CreateView(Transform parent, LobbyView view)
        {
            var go = CreateGroup(parent, $"{view}View");
            _viewRoots[view] = go;
            return go.transform;
        }

        /// <summary>상점 안 화면 맨 아래 공통 "로비로" 버튼.</summary>
        private void CreateBackButton(Transform view) =>
            CreateButton(view, "BackButton", ButtonLockedColor, -300f, 220f, 40f,
                out _, Loc.T("[Esc] 로비로"), 17, Color.white, () => ShowView(LobbyView.Hub));

        private void BuildHubView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Hub);
            // 허브 = 걸어 다니는 방(LobbyRoom) - 예전 카드 6개·시작 버튼 대신 NPC와 문. 화면엔 아래 안내 한 줄만.
            CreatePanel(view, 0f, -330f, 1000f, 44f);
            _hubHint = CreateLabel(view, "HubHint", string.Empty, 15, FontStyle.Normal, new Color(0.85f, 0.85f, 0.9f), -330f, 980f);
            _hubHint.rectTransform.sizeDelta = new Vector2(980f, 36f);
            RefreshHubHint();
            // 골드 칸(던전 HUD와 같은 모양) - 방(가로 약 224~1056px) 오른쪽 위 빈 모서리, 아래 안내 줄(~1140px)과도 안 겹친다
            _hubGoldText = HudUi.CreateGoldBox(view, Vector2.one, Vector2.one, new Vector2(-12f, -8f));
            _hubStoneText = HudUi.CreateCurrencyBox(view, Vector2.one, Vector2.one, new Vector2(-12f, -52f), "UI/Icons/ManaStone", new Color(0.79f, 0.63f, 1f));
            // 특성 창 버튼(11-02 - 모바일엔 T키가 없다) - 마석 칸 아래, 남은 포인트가 있으면 초록 숫자
            CreateButton(view, "TraitButton", ButtonColor, 236f, 180f, 44f, out _hubTraitText, string.Empty, 16, Color.white,
                () => _traitPanel.Open(), x: 538f);
            _hubTraitText.supportRichText = true;
            RefreshHubTraitButton();
        }

        private Text _hubTraitText;
        private int _shownTraitPoints = int.MinValue;

        private void RefreshHubTraitButton()
        {
            var free = Traits.FreePoints;
            if (_hubTraitText == null || free == _shownTraitPoints)
                return;
            _shownTraitPoints = free;
            _hubTraitText.text = free > 0 ? Loc.F("특성 [T]  <color=#8CE08C>+{0}</color>", free) : Loc.T("특성 [T]");
        }

        private Text _hubHint;

        /// <summary>아래 안내 줄 - 튜토리얼 뒤 첫 구매 전엔 할 일(뽑기·영약)을 노란 글씨로.</summary>
        private void RefreshHubHint()
        {
            var shopFirst = FirstShopLocked;
            _hubHint.text = shopFirst
                ? Loc.T("받은 골드로 먼저 강해지자!  [1] 보물 상인: 장비 뽑기 · [2] 연금술사: 영약 - 하나라도 사면 문이 열린다")
                : Loc.T("방향키 이동 · NPC에게 부딪히거나 Space로 말 걸기 · 위쪽 문으로 들어가면 시작 · 숫자 1~7 바로 열기 · T 특성 · Esc 타이틀");
            _hubHint.color = shopFirst ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.85f, 0.9f);
        }

        /// <summary>튜토리얼 뒤 첫 구매 전 - 던전 문 잠금. 골드가 뽑기·영약 어느 쪽도 못 살 만큼이면(그럴 일은 없지만) 잠그지 않는다.</summary>
        private static bool FirstShopLocked => TutorialStage.FirstShopPending
            && GoldWallet.Gold >= Mathf.Min(GachaSystem.GetPullCost(ItemSlot.Weapon), StatPotionWallet.GetNextCost(PotionType.Attack));

        /// <summary>뽑기·영약을 샀다 - 첫 구매 잠금이 풀렸으면 알린다.</summary>
        private void OnShopPurchase()
        {
            if (!TutorialStage.CompleteFirstShop())
                return;
            RefreshHubHint();
            // 방금 띄운 구매 결과 뒤에 붙인다(덮으면 뭘 뽑았는지 안 보인다)
            _resultText.text += "   <color=#7CFF8A>" + Loc.T("문이 열렸다! 이제 위쪽 문으로 던전에 들어가자") + "</color>";
        }

        private static string FirstShopBadge() => FirstShopLocked ? "<color=#FFD966>!</color>" : string.Empty;

        /// <summary>로비 방에 NPC 6명을 세운다(예전 허브 카드 6개와 같은 기능·같은 숫자키).</summary>
        private void BuildLobbyRoom()
        {
            _room = LobbyRoom.Create(EnterDoor, StageProgress.FinalBossCleared ? Loc.T("층 선택") : Loc.F("스테이지 {0} 시작", StageProgress.CurrentStage));
            const int left = 1, right = LobbyRoom.Width - 2, top = LobbyRoom.Height - 2, upper = top - 2, lower = top - 5;
            _room.AddNpc("LobbyGacha", new Vector2Int(left, upper), () => Loc.T("[1] 보물 상인"), FirstShopBadge, () => ShowView(LobbyView.Gacha));
            _room.AddNpc("LobbyAlchemist", new Vector2Int(left, lower), () => Loc.T("[2] 연금술사"), FirstShopBadge, () => ShowView(LobbyView.Potion));
            _room.AddNpc("LobbyGambler", new Vector2Int(right, upper), () => Loc.T("[3] 도박꾼"), () => string.Empty, () => ShowView(LobbyView.Casino));
            _room.AddNpc("LobbyTimeKeeper", new Vector2Int(LobbyRoom.DoorCell.x - 3, top), () => $"[4] {LobbyGuide.Name}", GuideBadge, LobbyGuide.Talk);
            _room.AddNpc("LobbyChronicler", new Vector2Int(LobbyRoom.DoorCell.x + 3, top), () => Loc.T("[5] 기록관"),
                () => $"<color=#FFD966>{Achievements.UnlockedCount} / {Achievements.All.Length}</color>", () => _achievementPanel.Open());
            _room.AddNpc("LobbyScholar", new Vector2Int(right, lower), () => Loc.T("[6] 도감 학자"),
                () => $"<color=#B9A0FF>{Codex.FoundCount()} / {Codex.TotalCount()}</color>", () => _codexPanel.Open());
            _room.AddNpc("LobbyGrocer", new Vector2Int(3, 3), () => Loc.T("[7] 잡화상"), () => string.Empty, () => ShowView(LobbyView.Items)); // 11-02 소비 아이템
            _room.AddNpc("LobbySkillHelper", new Vector2Int(7, 3), () => Loc.T("[8] 스킬 도우미"), SkillHelperBadge, TalkSkillHelper); // 11-03 특성 갈래 고르기
            _room.DoorLocked = () => FirstShopLocked;
            _room.OnDoorLocked = () => ShowResult(Loc.T("아직 문이 잠겨 있다 - 보물 상인(1)에서 장비를 뽑거나 연금술사(2)에게 영약을 사서 먼저 강해지자!"), new Color(1f, 0.85f, 0.4f));
            _room.Build();
        }

        /// <summary>[8] 스킬 도우미(11-03 사용자) - 1층 보스를 잡기 전엔 한마디만, 그 뒤 아직 안 고른 갈래가 있으면 고르는 창, 다 골랐으면 그냥 특성 창.</summary>
        private void TalkSkillHelper()
        {
            if (!Traits.ChoiceUnlocked)
            {
                ShowResult(Loc.T("스킬 도우미: \"1층 보스를 쓰러뜨리고 오게. 그때 자네에게 맞는 길을 보여 주지.\""), new Color(1f, 0.85f, 0.4f));
                return;
            }
            if (Traits.PendingChoice.HasValue)
                _traitPanel.OpenChoice();
            else
                _traitPanel.Open();
        }

        /// <summary>스킬 도우미 머리 위 - 고를 갈래가 남았으면 노란 "!".</summary>
        private static string SkillHelperBadge() => Traits.ChoiceUnlocked && Traits.PendingChoice.HasValue ? "<color=#FFD966>!</color>" : string.Empty;

        /// <summary>노인 머리 위 - 받을 의뢰 보상이 있으면 노란 글씨, 회차 특별 대사가 있으면 "!".</summary>
        private static string GuideBadge()
        {
            var pending = LobbyQuests.PendingCount;
            if (pending > 0)
                return $"<color=#FFD966>{Loc.F("보상 {0}개 받기", pending)}</color>";
            return LobbyGuide.HasSomethingToSay ? "<color=#B9A0FF>!</color>" : string.Empty;
        }

        private LobbyRoom _room;

        private void BuildGachaView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Gacha);

            // 슬롯별 뽑기 버튼 3개를 한 줄로(검/갑옷/반지) - 확률표를 창으로 빼면서 아래가 비어 크고 널널하게(사용자 요청).
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var x = ((int)slot - 1) * 265f;
                var captured = slot;
                CreateButton(view, $"Gacha{slot}Button", ButtonColor, 20f, 245f, 84f,
                    out var label, string.Empty, 17, Color.white, () => DoGachaPull(captured), x);
                label.rectTransform.sizeDelta = new Vector2(225f, 74f);
                _gachaButtonTexts[slot] = label;

                // 바로 아래 10연차 버튼
                CreateButton(view, $"Gacha{slot}MultiButton", new Color(0.35f, 0.3f, 0.45f), -56f, 245f, 50f,
                    out var multiLabel, string.Empty, 16, Color.white, () => DoGachaMultiPull(captured), x);
                _gachaMultiButtonTexts[slot] = multiLabel;

                // 그 아래 뽑기권 전부(11-03) - 뽑기권이 있을 때만(RefreshGachaDisplay)
                var ticketButton = CreateButton(view, $"Gacha{slot}TicketButton", new Color(0.25f, 0.36f, 0.3f), -104f, 245f, 38f,
                    out var ticketLabel, string.Empty, 14, Color.white, () => DoGachaAllTickets(captured), x);
                _gachaTicketButtons[slot] = ticketButton.gameObject;
                _gachaTicketButtonTexts[slot] = ticketLabel;
            }

            // 등급 확률표는 바로 보여주지 않고 버튼으로 여는 창(사용자 요청) - 어둡게 덮고 가운데에 표, 닫기 버튼·P·Esc로 닫음
            CreateButton(view, "ChanceButton", new Color(0.3f, 0.3f, 0.36f), -150f, 260f, 44f,
                out _, Loc.T("[P] 등급 확률 보기"), 16, Color.white, () => SetChanceOpen(true));

            // 마석(10-29) - 골드 환전(유물 뽑기는 던전의 떠돌이 마석 상인)
            CreateButton(view, "ManaExchangeButton", new Color(0.36f, 0.16f, 0.2f), -222f, 330f, 52f,
                out _exchangeButtonText, string.Empty, 16, Color.white, () => DoManaExchange(1));
            RefreshManaStoneDisplay();
            CreateBackButton(view);

            _chanceGroup = CreateGroup(view, "ChanceGroup");
            var dim = _chanceGroup.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);
            CreatePanel(_chanceGroup.transform, 0f, 0f, 320f, 290f);
            BuildChanceTable(_chanceGroup.transform, -6f);
            CreateButton(_chanceGroup.transform, "ChanceCloseButton", ButtonLockedColor, -178f, 180f, 38f,
                out _, Loc.T("[P / Esc] 닫기"), 15, Color.white, () => SetChanceOpen(false));
            _chanceGroup.SetActive(false);
        }

        private GameObject _chanceGroup;

        private const int ExchangeBulkCount = 10; // [Shift+E] 10개씩

        private void RefreshManaStoneDisplay()
        {
            _exchangeButtonText.text = Loc.F("[E] 마석 1개로 환전 ({0}골드)\n[Shift+E] 10개", ManaStoneWallet.ExchangeCost);
        }

        private void DoManaExchange(int count)
        {
            var done = 0;
            while (done < count && ManaStoneWallet.TryExchange())
                done++;
            if (done == 0)
                ShowResult(Loc.F("골드가 모자랍니다! (마석 1개: {0}골드)", ManaStoneWallet.ExchangeCost), Color.white);
            else
                ShowResult(Loc.F("골드를 마석 {0}개로 바꿨습니다. (보유 마석 {1})", done, ManaStoneWallet.Stones), new Color(0.79f, 0.63f, 1f));
            RefreshManaStoneDisplay();
        }
        private bool ChanceOpen => _chanceGroup != null && _chanceGroup.activeSelf;

        private void SetChanceOpen(bool open)
        {
            if (_chanceGroup != null)
                _chanceGroup.SetActive(open);
        }

        private void BuildPotionView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Potion);

            // 영약마다 왼쪽에 병 그림(UI/Icons/Potion_*, 32x32 → 2배) + 오른쪽에 설명 - 10-14 영약 그림 추가로 버튼을 키움
            PotionButton(view, "AttackPotionButton", -10f, "Potion_Attack", out _attackButtonText, () => DoPotionBuy(PotionType.Attack, Loc.T("공격력")));
            PotionButton(view, "HealthPotionButton", -95f, "Potion_Health", out _healthButtonText, () => DoPotionBuy(PotionType.Health, Loc.T("체력")));
            var criticalRect = PotionButton(view, "CriticalPotionButton", -180f, "Potion_Critical", out _criticalButtonText,
                () => DoPotionBuy(PotionType.Critical, Loc.T("치명타")), out _criticalIcon);
            _criticalButtonImage = criticalRect.GetComponent<Image>();

            CreateBackButton(view);
        }

        private Image _criticalIcon;

        private RectTransform PotionButton(Transform view, string goName, float y, string icon, out Text label, Action onClick) =>
            PotionButton(view, goName, y, icon, out label, onClick, out _);

        private RectTransform PotionButton(Transform view, string goName, float y, string icon, out Text label, Action onClick, out Image iconImage)
        {
            const float width = 560f, height = 76f;
            var rect = CreateButton(view, goName, ButtonColor, y, width, height, out label, string.Empty, 18, Color.white, onClick);
            iconImage = null;
            var sprite = PixelUi.Icon(icon);
            if (sprite == null)
                return rect; // 그림이 없으면 예전처럼 글자만
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(rect, false);
            iconImage = go.AddComponent<Image>();
            iconImage.sprite = sprite;
            var iconRect = go.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * 2f;
            iconRect.anchoredPosition = new Vector2(-width / 2f + 44f, 0f);
            label.rectTransform.sizeDelta = new Vector2(width - 110f, height - 8f);
            label.rectTransform.anchoredPosition = new Vector2(36f, 0f);
            return rect;
        }

        // ---- 잡화점(11-02 사용자 "로비에서 소비 아이템 구매") - 1회용 아이템 10종, 가격은 던전 떠돌이 상인과 같은 공식(NpcActor.ItemPrice).
        // 종류마다 가질 수 있는 만큼(1개, 연금술사의 주머니면 2개)까지. 숫자 1~9·0 또는 클릭으로 산다. ----
        private readonly Text[] _itemShopTexts = new Text[ItemInfo.Count];
        private readonly Image[] _itemShopImages = new Image[ItemInfo.Count];

        private void BuildItemShopView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Items);
            const float width = 470f, height = 50f, gapY = 56f, top = 60f;
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var type = (ItemType)i;
                var x = i < 5 ? -245f : 245f;
                var y = top - (i % 5) * gapY;
                var rect = CreateButton(view, $"Item{i}", ButtonColor, y, width, height, out _itemShopTexts[i], string.Empty, 15, Color.white,
                    () => DoItemBuy(type), x: x);
                _itemShopImages[i] = rect.GetComponent<Image>();
                _itemShopTexts[i].supportRichText = true;
                _itemShopTexts[i].alignment = TextAnchor.MiddleLeft;
                _itemShopTexts[i].rectTransform.sizeDelta = new Vector2(width - 64f, height - 6f);
                _itemShopTexts[i].rectTransform.anchoredPosition = new Vector2(24f, 0f);
                var sprite = PixelUi.Icon($"Item_{type}");
                if (sprite == null)
                    continue;
                var go = new GameObject("Icon", typeof(RectTransform));
                go.transform.SetParent(rect, false);
                var icon = go.AddComponent<Image>();
                icon.sprite = sprite;
                var iconRect = go.GetComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * 2f;
                iconRect.anchoredPosition = new Vector2(-width / 2f + 26f, 0f);
            }
            CreateBackButton(view);
        }

        /// <summary>잡화점 가격 - 던전 상인과 같은 공식, 특성 "단골"이면 -20%.</summary>
        private static int LobbyItemPrice => Mathf.RoundToInt(NpcActor.ItemPrice(StageProgress.CurrentStage) * (Traits.Has(TraitType.TradeShop) ? Traits.TradeShopDiscount : 1f));

        private void RefreshItemShopDisplay()
        {
            var price = LobbyItemPrice;
            for (var i = 0; i < ItemInfo.Count; i++)
            {
                var type = (ItemType)i;
                var key = i == 9 ? "0" : (i + 1).ToString();
                var canHold = Inventory.CanHold(type);
                var affordable = GoldWallet.Gold >= price;
                var status = !canHold ? Loc.T("<color=#8A8A92>가득 참</color>")
                    : $"<color={(affordable ? "#FFD966" : "#FF8080")}>{price}G</color>";
                _itemShopTexts[i].text = $"[{key}] <b>{ItemInfo.Name(type)}</b>  {status}  " +
                                         Loc.F("<size=12><color=#9AA0AA>보유 {0}/{1}</color></size>", Inventory.Count(type), Relics.ItemCapacity) +
                                         $"\n<size=11><color=#C8CCD6>{ItemInfo.Description(type)}</color></size>";
                _itemShopImages[i].color = ButtonTint(canHold && affordable ? ButtonColor : ButtonLockedColor);
            }
        }

        private void DoItemBuy(ItemType type)
        {
            var price = LobbyItemPrice;
            if (!Inventory.CanHold(type))
            {
                ShowResult(Loc.F("{0}은(는) 더 가질 수 없습니다.", ItemInfo.Name(type)), Color.white);
                return;
            }
            if (!GoldWallet.TrySpend(price))
            {
                ShowResult(Loc.F("골드가 모자랍니다! (가격: {0})", price), Color.white);
                return;
            }
            Inventory.Give(type);
            Prefs.Save();
            PlayLog.Lobby($"잡화점 구매: {ItemInfo.Name(type)} ({price}골드)");
            ShowResult(Loc.F("{0} 구매! ({1}골드)", ItemInfo.Name(type), price), new Color(0.6f, 1f, 0.7f));
            RefreshItemShopDisplay();
        }

        private void BuildCasinoView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Casino);
            CreatePanel(view, 0f, 115f, 440f, 170f);

            _slotTitleText = CreateLabel(view, "SlotTitle", string.Empty,
                18, FontStyle.Bold, new Color(1f, 0.85f, 0.3f), 168f, 400f);
            _slotTitleText.rectTransform.sizeDelta = new Vector2(400f, 52f);

            BuildSlotReels(view, 86f);

            CreateButton(view, "SlotButton", new Color(0.45f, 0.3f, 0.15f), -12f, 300f, 42f,
                out _slotButtonText, string.Empty, 17, Color.white, DoSlotSpin);
            // 거는 돈 줄 - 왼쪽 반을 누르면 -, 오른쪽 반을 누르면 +(11-02, 예전 고액 베팅 자리)
            CreateButton(view, "SlotBetMinus", new Color(0.3f, 0.22f, 0.15f), -62f, 60f, 42f,
                out _, "◀", 17, Color.white, () => AdjustSlotBet(-SlotMachine.BetStep), x: -200f);
            CreateButton(view, "SlotBetPlus", new Color(0.3f, 0.22f, 0.15f), -62f, 60f, 42f,
                out _, "▶", 17, Color.white, () => AdjustSlotBet(SlotMachine.BetStep), x: 200f);
            _slotHighButtonText = CreateLabel(view, "SlotBet", string.Empty, 16, FontStyle.Normal, Color.white, -62f, 330f);
            _slotHighButtonText.supportRichText = true;
            CreateButton(view, "SlotRiskButton", new Color(0.5f, 0.2f, 0.15f), -212f, 300f, 36f,
                out _slotRiskButtonText, string.Empty, 15, Color.white, () => AdjustSlotMultiplier(SlotMachine.Multiplier >= SlotMachine.MaxMultiplier
                    ? SlotMachine.MinMultiplier - SlotMachine.MaxMultiplier : SlotMachine.MultiplierStep)); // 누르면 한 칸 위로, 5배에서 누르면 2배로
            _slotBulkButton = CreateButton(view, "SlotBulkButton", new Color(0.35f, 0.3f, 0.15f), -112f, 160f, 42f,
                out _slotBulkButtonText, string.Empty, 14, Color.white, () => DoSlotTicketBulk(SlotBulkCount), x: -84f);
            _slotAllButton = CreateButton(view, "SlotAllButton", new Color(0.35f, 0.3f, 0.15f), -112f, 160f, 42f,
                out _slotAllButtonText, string.Empty, 14, Color.white, () => DoSlotTicketBulk(int.MaxValue), x: 84f);
            _slotSkipAnim = Prefs.GetInt(SlotSkipPrefKey, 0) == 1;
            CreateButton(view, "SlotSkipButton", new Color(0.25f, 0.28f, 0.35f), -162f, 220f, 36f,
                out _slotSkipButtonText, string.Empty, 14, Color.white, ToggleSlotSkip);

            CreateBackButton(view);
        }

        /// <summary>슬롯 틀(72x26 도트) + 릴 창 3개 위에 기호(18x18 도트) - 둘 다 4배. 그림이 하나라도 없으면 글자 릴.</summary>
        private void BuildSlotReels(Transform view, float y)
        {
            _slotFrameSprite = Resources.Load<Sprite>("UI/Casino/SlotFrame");
            _slotFrameWinSprite = Resources.Load<Sprite>("UI/Casino/SlotFrame_Win");
            _symbolSprites = new Sprite[SlotMachine.Symbols.Length];
            var complete = _slotFrameSprite != null && _slotFrameWinSprite != null;
            for (var i = 0; i < _symbolSprites.Length; i++)
            {
                _symbolSprites[i] = Resources.Load<Sprite>($"UI/Casino/Symbol_{i}");
                complete &= _symbolSprites[i] != null;
            }

            if (!complete)
            {
                _slotReelsText = CreateLabel(view, "SlotReels", FormatReels(new[] { 0, 1, 2 }),
                    32, FontStyle.Bold, Color.white, y, 400f);
                _slotReelsText.rectTransform.sizeDelta = new Vector2(400f, 44f);
                return;
            }

            _slotFrameImage = CreatePixelImage(view, "SlotFrame", _slotFrameSprite, new Vector2(0f, y));
            // 창 가운데: 틀 왼쪽에서 14 + 22*i 픽셀(틀 가운데 36) → -22 / 0 / +22 픽셀
            for (var i = 0; i < _reelImages.Length; i++)
                _reelImages[i] = CreatePixelImage(view, $"Reel{i}", _symbolSprites[i], new Vector2((i - 1) * 22f * PixelScale, y));
        }

        private static Image CreatePixelImage(Transform parent, string goName, Sprite sprite, Vector2 position)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * PixelScale;
            rect.anchoredPosition = position;
            return image;
        }

        /// <summary>등급 확률표 - 뽑기 버튼 아래에 등급 이름 열 + 검/갑옷/반지 열. 값은 RefreshGachaDisplay가 채운다.</summary>
        private void BuildChanceTable(Transform parent, float tableY)
        {
            const float tableX = 0f;
            const float columnWidth = 66f;
            var grades = ((ItemGrade[])Enum.GetValues(typeof(ItemGrade))).Where(g => g <= ItemGrade.Eternal).ToArray(); // Unknown은 뽑기로 안 나온다
            var height = (grades.Length + 2) * 19f;

            var title = CreateLabel(parent, "ChanceTitle", Loc.T("등급 확률 (상점 레벨별)"), 16, FontStyle.Bold,
                new Color(1f, 0.85f, 0.3f), tableY + height / 2f + 16f, 300f);
            title.rectTransform.anchoredPosition = new Vector2(tableX, title.rectTransform.anchoredPosition.y);

            var names = new System.Text.StringBuilder(Loc.T("등급\n"));
            foreach (var g in grades)
            {
                var info = EquipmentData.Grades[g];
                names.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(info.Color)}>{info.Name}</color>");
            }
            var nameColumn = CreateLabel(parent, "ChanceNames", names.ToString(), 14, FontStyle.Normal, Color.white, tableY, columnWidth);
            nameColumn.rectTransform.sizeDelta = new Vector2(columnWidth, height);
            nameColumn.rectTransform.anchoredPosition = new Vector2(tableX - 1.5f * columnWidth, tableY);

            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var column = CreateLabel(parent, $"Chance{slot}", string.Empty, 14, FontStyle.Normal, Color.white, tableY, columnWidth);
                column.rectTransform.sizeDelta = new Vector2(columnWidth, height);
                column.rectTransform.anchoredPosition = new Vector2(tableX + ((int)slot - 0.5f) * columnWidth, tableY);
                _chanceColumns[slot] = column;
            }
        }

        private static Text CreateLabel(Transform parent, string goName, string content, int fontSize,
            FontStyle style, Color color, float y, float width)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.text = content;
            // 그림 배경 위에서도 읽히게
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 32f);
            rect.anchoredPosition = new Vector2(0f, y);
            return text;
        }

        /// <summary>배경 Image + 중앙 정렬 Text로 이루어진 "버튼"을 만들고, 클릭 판정용으로
        /// _buttons 리스트에 사각형+콜백을 등록한다. UGUI Button 컴포넌트를 안 쓰는 이유는 클래스
        /// 주석 참고 - EventSystem 없이 순수 사각형 히트테스트로만 클릭을 판정한다.</summary>
        private RectTransform CreateButton(Transform parent, string goName, Color bgColor, float y,
            float width, float height, out Text label, string content, int fontSize, Color textColor, Action onClick,
            float x = 0f)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = ButtonTint(bgColor);
            if (_buttonSprite != null)
            {
                image.sprite = _buttonSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f / PixelScale;
            }

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            label = CreateLabel(go.transform, goName + "Label", content, fontSize, FontStyle.Normal, textColor, 0f, width - 20f);

            _buttons.Add((rect, onClick));
            if (_buttonSprite != null)
                _skinnedButtons.Add((rect, image));
            return rect;
        }
    }
}
