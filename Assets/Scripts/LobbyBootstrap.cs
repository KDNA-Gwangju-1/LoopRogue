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
        private static readonly Color StartButtonColor = new Color(0.2f, 0.45f, 0.25f);

        private readonly List<(RectTransform rect, Action onClick)> _buttons = new List<(RectTransform, Action)>();

        // 로비(허브)에서 상점 3곳으로 들어가 그 안에서만 사고판다 - 한 화면에 다 몰려 있던 걸 나눔.
        // 화면마다 GameObject 하나, 꺼진 화면의 버튼은 클릭·단축키 둘 다 안 받는다.
        private enum LobbyView { Hub, Gacha, Potion, Casino }
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
        private Text _weaponText;
        private Text _armorText;
        private Text _accessoryText;
        private Text _potionText;
        private Text _resultText;
        private readonly Dictionary<ItemSlot, Text> _gachaButtonTexts = new Dictionary<ItemSlot, Text>();
        private readonly Dictionary<ItemSlot, Text> _gachaMultiButtonTexts = new Dictionary<ItemSlot, Text>();
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
        private Text _slotHighButtonText;
        private bool _slotSpinning;
        private int _slotPendingPayout;

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
            Achievements.Unlocked += OnAchievementUnlocked;
            Codex.SyncOwned();    // 도감이 생기기 전 저장으로 이미 가진 유물/아이템
            Achievements.Check(); // 업적이 생기기 전 저장으로 이미 조건을 채운 것들
        }

        private AchievementPanel _achievementPanel;
        private CodexPanel _codexPanel;

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
            _goldText.text = Loc.F("보유 골드: {0}", GoldWallet.Gold - _slotPendingPayout);
            UpdateHover();

            // 안내자와 대화 중이거나 업적 창이 열려 있으면(닫은 프레임 포함) 로비 버튼·단축키를 안 받는다 - 그 창들이 Esc/숫자/클릭을 직접 처리한다.
            if (NpcDialogUI.BlocksInput || AchievementPanel.BlocksInput || CodexPanel.BlocksInput || GachaReveal.BlocksInput)
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
                    // 숫자키 = 1회, Shift+숫자키 = 10연차
                    if (GameInput.Down(Key.Digit1))
                        DoGacha(ItemSlot.Weapon, shift);
                    if (GameInput.Down(Key.Digit2))
                        DoGacha(ItemSlot.Armor, shift);
                    if (GameInput.Down(Key.Digit3))
                        DoGacha(ItemSlot.Accessory, shift);
                    break;

                case LobbyView.Potion:
                    if (GameInput.Down(Key.A))
                        DoPotionBuy(PotionType.Attack, Loc.T("공격력"));
                    if (GameInput.Down(Key.H))
                        DoPotionBuy(PotionType.Health, Loc.T("체력"));
                    if (GameInput.Down(Key.C))
                        DoPotionBuy(PotionType.Critical, Loc.T("치명타"));
                    break;

                case LobbyView.Casino:
                    if (GameInput.Down(Key.S))
                        DoSlotSpin(shift);
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
            SetChanceOpen(false);
            foreach (var pair in _viewRoots)
                pair.Value.SetActive(pair.Key == view);
            // 장비 요약은 로비와 뽑기 상점 둘 다에서 보여준다(뽑으면서 지금 장비를 봐야 해서).
            // (허브는 걸어 다니는 방이라 요약 패널이 방을 가리지 않게 상점 안에서만)
            _equipmentGroup.SetActive(view == LobbyView.Gacha);
            _headerGroup.SetActive(view != LobbyView.Hub);
            _potionGroup.SetActive(view == LobbyView.Potion);
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
                _ => $"Stage {StageProgress.CurrentStage}/{StageProgress.MaxStage}",
            };
        }

        private void StartRun() => SceneManager.LoadScene(MainSceneName);

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
        }

        private void RefreshPotionDisplay()
        {
            _potionText.text =
                Loc.F("영약 누적 - 공격력+{0:0.#}   체력+{1:0.#}   |   유물 {2}개", StatPotionWallet.TotalAttackBonus(), StatPotionWallet.TotalHealthBonus(), Relics.OwnedCount);

            _attackButtonText.text =
                Loc.F("[A] 공격력 영약 구매 (다음 비용 {0}골드)", StatPotionWallet.GetNextCost(PotionType.Attack));
            _healthButtonText.text =
                Loc.F("[H] 체력 영약 구매 (다음 비용 {0}골드)", StatPotionWallet.GetNextCost(PotionType.Health));
        }

        private void RefreshCriticalDisplay()
        {
            var unlocked = StatPotionWallet.IsUnlocked(PotionType.Critical);
            _criticalButtonImage.color = ButtonTint(unlocked ? ButtonColor : ButtonLockedColor);
            if (_criticalIcon != null)
                _criticalIcon.color = unlocked ? Color.white : new Color(0.35f, 0.35f, 0.38f); // 잠겨 있으면 병도 어둡게

            _criticalButtonText.text = unlocked
                ? Loc.F("[C] 치명타확률+{0:0.#}% 구매 (다음 비용 {1}골드)", StatPotionWallet.TotalCriticalChanceBonus() * 100f, StatPotionWallet.GetNextCost(PotionType.Critical))
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
            var color = EquipmentData.Grades[r.Grade].Color;

            var message = r.Equipped ? Loc.F("{0} 획득! 장착했습니다.", name) : Loc.F("{0}... 이미 더 좋은 장비가 있어 버려짐.", name);
            var newTier = EquipmentEffects.TierOf(r.Grade);
            if (r.Equipped && newTier > 0)
                message += Loc.F("  새 효과: {0} ({1})", EquipmentEffects.Name(slot, newTier), EquipmentEffects.Description(slot, newTier));
            if (r.ShopLeveledUp)
                message += Loc.F("  {0} 상점 Lv{1} 달성!", EquipmentData.Templates[slot].BaseName, GachaSystem.GetShopLevel(slot));

            RefreshGachaDisplay(); // 가격·뽑기권은 바로, 장비 요약·결과 줄은 연출이 끝난 뒤(미리 안 들키게)
            GachaReveal.Play(slot, new[] { (r.Grade, r.Equipped) }, () =>
            {
                ShowResult(message, color);
                RefreshEquipmentDisplay();
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
            foreach (var r in results)
            {
                if (r.Grade > best.Grade)
                    best = r;
                anyEquipped |= r.Equipped;
                leveledUp |= r.ShopLeveledUp;
            }

            var name = EquipmentData.DisplayName(slot, best.Grade);
            var message = anyEquipped
                ? Loc.F("10연차 최고: {0} - 장착했습니다!", name)
                : Loc.F("10연차 최고: {0}... 이미 더 좋은 장비가 있음.", name);
            if (leveledUp)
                message += Loc.F("  {0} 상점 Lv{1} 달성!", EquipmentData.Templates[slot].BaseName, GachaSystem.GetShopLevel(slot));

            RefreshGachaDisplay();
            var reveal = new List<(ItemGrade, bool)>();
            foreach (var r in results)
                reveal.Add((r.Grade, r.Equipped));
            GachaReveal.Play(slot, reveal, () =>
            {
                ShowResult(message, EquipmentData.Grades[best.Grade].Color);
                RefreshEquipmentDisplay();
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

        private void DoSlotSpin(bool high)
        {
            if (_slotSpinning)
                return;

            var result = SlotMachine.Spin(high);
            if (result == null)
            {
                ShowResult(Loc.F("골드가 모자랍니다! (베팅액: {0})", SlotMachine.GetBet(high)), Color.white);
                return;
            }

            RefreshSlotDisplay(); // 이용권 장수
            StartCoroutine(SlotSpinRoutine(result.Value));
        }

        /// <summary>결과는 이미 정해졌고 연출만 한다 - 전부 돌다가 왼쪽 릴부터 하나씩 멈춘다.</summary>
        private IEnumerator SlotSpinRoutine(SlotMachine.SpinResult result)
        {
            _slotSpinning = true;
            _slotPendingPayout = result.Payout;
            SetSlotLook(dim: false, win: false);

            var shown = new int[3];
            var stopped = 0;
            var elapsed = 0f;
            while (stopped < 3)
            {
                while (stopped < 3 && elapsed >= SlotSpinDuration + SlotReelStopInterval * stopped)
                {
                    shown[stopped] = result.Reels[stopped];
                    stopped++;
                }

                for (var i = stopped; i < 3; i++)
                    shown[i] = UnityEngine.Random.Range(0, SlotMachine.Symbols.Length);

                ShowReels(shown);
                yield return new WaitForSeconds(0.06f);
                elapsed += 0.06f;
            }

            _slotPendingPayout = 0;
            _slotSpinning = false;

            if (result.MatchCount == 3)
            {
                SetSlotLook(dim: false, win: true);
                ShowResult(Loc.F("잭팟! 3개 일치 - {0}골드 획득!", result.Payout), new Color(1f, 0.85f, 0.3f));
            }
            else if (result.MatchCount == 2)
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
            _slotButtonText.text = SlotMachine.Tickets > 0 // 이용권이 있으면 먼저 쓴다
                ? Loc.F("[S] 돌리기 (이용권 {0}장)", SlotMachine.Tickets)
                : Loc.F("[S] 돌리기 ({0}G)", SlotMachine.GetBet(false));
            _slotHighButtonText.text = Loc.F("[Shift+S] {0}배 베팅 ({1}G)", SlotMachine.HighBetMultiplier, SlotMachine.GetBet(true));
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
                return Loc.F("{0}: (미장착)", slot);

            var atk = EquipmentData.AttackBonus(slot, grade.Value);
            var hp = EquipmentData.HealthBonus(slot, grade.Value);
            var tier = EquipmentEffects.TierOf(grade.Value);
            var effects = tier > 0 ? Loc.F("  <color=#FFD966>효과 {0}/{1}</color>", tier, EquipmentEffects.MaxTier) : ""; // 효과 설명은 게임 중 Tab 상태창
            if (slot == ItemSlot.Accessory && EquipmentEffects.HasMemorySet)
                effects += Loc.F("  <color=#7FE0D8>세트 「{0}」</color>", EquipmentEffects.MemorySetName);
            return Loc.F("{0}: {1} (공격력+{2:0.#} 체력+{3:0.#}){4}", slot, EquipmentData.DisplayName(slot, grade.Value), atk, hp, effects);
        }

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
            var hint = CreateLabel(view, "HubHint",
                Loc.T("방향키 이동 · NPC에게 부딪히거나 Space로 말 걸기 · 위쪽 문으로 들어가면 시작 · 숫자 1~6 바로 열기 · Esc 타이틀"),
                15, FontStyle.Normal, new Color(0.85f, 0.85f, 0.9f), -330f, 980f);
            hint.rectTransform.sizeDelta = new Vector2(980f, 36f);
        }

        /// <summary>로비 방에 NPC 6명을 세운다(예전 허브 카드 6개와 같은 기능·같은 숫자키).</summary>
        private void BuildLobbyRoom()
        {
            _room = LobbyRoom.Create(StartRun, Loc.F("스테이지 {0} 시작", StageProgress.CurrentStage));
            const int left = 1, right = LobbyRoom.Width - 2, top = LobbyRoom.Height - 2, upper = top - 2, lower = top - 5;
            _room.AddNpc("LobbyGacha", new Vector2Int(left, upper), () => Loc.T("[1] 보물 상인"), () => string.Empty, () => ShowView(LobbyView.Gacha));
            _room.AddNpc("LobbyAlchemist", new Vector2Int(left, lower), () => Loc.T("[2] 연금술사"), () => string.Empty, () => ShowView(LobbyView.Potion));
            _room.AddNpc("LobbyGambler", new Vector2Int(right, upper), () => Loc.T("[3] 도박꾼"), () => string.Empty, () => ShowView(LobbyView.Casino));
            _room.AddNpc("LobbyTimeKeeper", new Vector2Int(LobbyRoom.DoorCell.x - 3, top), () => $"[4] {LobbyGuide.Name}", GuideBadge, LobbyGuide.Talk);
            _room.AddNpc("LobbyChronicler", new Vector2Int(LobbyRoom.DoorCell.x + 3, top), () => Loc.T("[5] 기록관"),
                () => $"<color=#FFD966>{Achievements.UnlockedCount} / {Achievements.All.Length}</color>", () => _achievementPanel.Open());
            _room.AddNpc("LobbyScholar", new Vector2Int(right, lower), () => Loc.T("[6] 도감 학자"),
                () => $"<color=#B9A0FF>{Codex.FoundCount()} / {Codex.TotalCount()}</color>", () => _codexPanel.Open());
            _room.Build();
        }

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
            }

            // 등급 확률표는 바로 보여주지 않고 버튼으로 여는 창(사용자 요청) - 어둡게 덮고 가운데에 표, 닫기 버튼·P·Esc로 닫음
            CreateButton(view, "ChanceButton", new Color(0.3f, 0.3f, 0.36f), -150f, 260f, 44f,
                out _, Loc.T("[P] 등급 확률 보기"), 16, Color.white, () => SetChanceOpen(true));
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

        private void BuildCasinoView(Transform parent)
        {
            var view = CreateView(parent, LobbyView.Casino);
            CreatePanel(view, 0f, 115f, 440f, 170f);

            var slotTitle = CreateLabel(view, "SlotTitle", Loc.T("운명의 슬롯\n3개 일치 10배 / 2개 일치 0.5배"),
                18, FontStyle.Bold, new Color(1f, 0.85f, 0.3f), 168f, 400f);
            slotTitle.rectTransform.sizeDelta = new Vector2(400f, 52f);

            BuildSlotReels(view, 86f);

            CreateButton(view, "SlotButton", new Color(0.45f, 0.3f, 0.15f), -12f, 300f, 42f,
                out _slotButtonText, string.Empty, 17, Color.white, () => DoSlotSpin(false));
            CreateButton(view, "SlotHighButton", new Color(0.5f, 0.2f, 0.15f), -62f, 300f, 42f,
                out _slotHighButtonText, string.Empty, 17, Color.white, () => DoSlotSpin(true));

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
