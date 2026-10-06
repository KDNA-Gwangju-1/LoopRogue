using System;
using System.Collections;
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
    /// 아예 안 쓰고, 매 프레임 Mouse.current로 좌클릭 여부만 읽어서 RectTransformUtility.
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
            BuildUI();
            RefreshEquipmentDisplay();
            RefreshGachaDisplay();
            RefreshPotionDisplay();
            RefreshCriticalDisplay();
            RefreshSlotDisplay();
        }

        private void Update()
        {
            _goldText.text = $"보유 골드: {GoldWallet.Gold - _slotPendingPayout}";

            HandleMouseClick();

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 숫자키 = 1회, Shift+숫자키 = 10연차
            var shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (keyboard.digit1Key.wasPressedThisFrame)
                DoGacha(ItemSlot.Weapon, shift);

            if (keyboard.digit2Key.wasPressedThisFrame)
                DoGacha(ItemSlot.Armor, shift);

            if (keyboard.digit3Key.wasPressedThisFrame)
                DoGacha(ItemSlot.Accessory, shift);

            if (keyboard.aKey.wasPressedThisFrame)
                DoPotionBuy(PotionType.Attack, "공격력");

            if (keyboard.hKey.wasPressedThisFrame)
                DoPotionBuy(PotionType.Health, "체력");

            if (keyboard.cKey.wasPressedThisFrame)
                DoPotionBuy(PotionType.Critical, "치명타");

            if (keyboard.sKey.wasPressedThisFrame)
                DoSlotSpin(shift);

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                StartRun();

            // Esc = 타이틀로(로비엔 진행 중인 전투가 없어서 확인 없이 바로 이동 - 골드/장비는 전부 영구 저장).
            if (keyboard.escapeKey.wasPressedThisFrame)
                SceneManager.LoadScene("Title");
        }

        private void HandleMouseClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            var screenPos = mouse.position.ReadValue();
            foreach (var (rect, onClick) in _buttons)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, null))
                {
                    onClick();
                    return;
                }
            }
        }

        private void StartRun() => SceneManager.LoadScene(MainSceneName);

        private void DoPotionBuy(PotionType type, string label)
        {
            if (!StatPotionWallet.IsUnlocked(type))
            {
                ShowResult($"{label} 영약은 스테이지 {StatPotionWallet.CriticalUnlockStage}부터 구매할 수 있습니다.", Color.white);
                return;
            }

            var cost = StatPotionWallet.GetNextCost(type);
            if (!StatPotionWallet.TryBuy(type))
            {
                ShowResult($"골드가 모자랍니다! ({label} 영약 비용: {cost})", Color.white);
                return;
            }

            ShowResult($"{label} 영약 구매! (영구 적용, 다음 시작부터)", new Color(0.4f, 1f, 0.5f));
            RefreshPotionDisplay();
            RefreshCriticalDisplay();
        }

        private void RefreshPotionDisplay()
        {
            _potionText.text =
                $"영약 누적 - 공격력+{StatPotionWallet.TotalAttackBonus():0.#}   체력+{StatPotionWallet.TotalHealthBonus():0.#}   |   유물 {Relics.OwnedCount}개";

            _attackButtonText.text =
                $"[A] 공격력 영약 구매 (다음 비용 {StatPotionWallet.GetNextCost(PotionType.Attack)}골드)";
            _healthButtonText.text =
                $"[H] 체력 영약 구매 (다음 비용 {StatPotionWallet.GetNextCost(PotionType.Health)}골드)";
        }

        private void RefreshCriticalDisplay()
        {
            var unlocked = StatPotionWallet.IsUnlocked(PotionType.Critical);
            _criticalButtonImage.color = unlocked ? ButtonColor : ButtonLockedColor;

            _criticalButtonText.text = unlocked
                ? $"[C] 치명타확률+{StatPotionWallet.TotalCriticalChanceBonus() * 100f:0.#}% 구매 (다음 비용 {StatPotionWallet.GetNextCost(PotionType.Critical)}골드)"
                : $"치명타 영약: 스테이지 {StatPotionWallet.CriticalUnlockStage}부터 구매 가능(비공개)";
        }

        private void DoGachaPull(ItemSlot slot)
        {
            var result = GachaSystem.Pull(slot);
            if (result == null)
            {
                ShowResult($"골드가 모자랍니다! (뽑기 비용: {GachaSystem.GetPullCost(slot)})", Color.white);
                return;
            }

            var r = result.Value;
            var name = EquipmentData.DisplayName(r.Slot, r.Grade);
            var color = EquipmentData.Grades[r.Grade].Color;

            var message = r.Equipped ? $"{name} 획득! 장착했습니다." : $"{name}... 이미 더 좋은 장비가 있어 버려짐.";
            var newTier = EquipmentEffects.TierOf(r.Grade);
            if (r.Equipped && newTier > 0)
                message += $"  새 효과: {EquipmentEffects.Name(slot, newTier)} ({EquipmentEffects.Description(slot, newTier)})";
            if (r.ShopLeveledUp)
                message += $"  {EquipmentData.Templates[slot].BaseName} 상점 Lv{GachaSystem.GetShopLevel(slot)} 달성!";

            ShowResult(message, color);
            RefreshEquipmentDisplay();
            RefreshGachaDisplay();
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
                ShowResult($"골드가 모자랍니다! (10연차 비용: {cost})", Color.white);
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
                ? $"10연차 최고: {name} - 장착했습니다!"
                : $"10연차 최고: {name}... 이미 더 좋은 장비가 있음.";
            if (leveledUp)
                message += $"  {EquipmentData.Templates[slot].BaseName} 상점 Lv{GachaSystem.GetShopLevel(slot)} 달성!";

            ShowResult(message, EquipmentData.Grades[best.Grade].Color);
            RefreshEquipmentDisplay();
            RefreshGachaDisplay();
        }

        private void RefreshGachaDisplay()
        {
            foreach (var pair in _gachaButtonTexts)
            {
                var slot = pair.Key;
                var level = GachaSystem.GetShopLevel(slot);
                var next = GachaSystem.GetPullsForNextLevel(slot);
                var progress = next.HasValue ? $"{GachaSystem.GetPullCount(slot)}/{next.Value}회" : "MAX";
                pair.Value.text =
                    $"[{(int)slot + 1}] {EquipmentData.Templates[slot].BaseName} 뽑기 ({GachaSystem.GetPullCost(slot)}G)\n상점 Lv{level} ({progress})";
            }

            foreach (var pair in _gachaMultiButtonTexts)
                pair.Value.text = $"[Shift+{(int)pair.Key + 1}] 10연차 ({GachaSystem.GetMultiPullCost(pair.Key)}G)";

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
                ShowResult($"골드가 모자랍니다! (베팅액: {SlotMachine.GetBet(high)})", Color.white);
                return;
            }

            StartCoroutine(SlotSpinRoutine(result.Value));
        }

        /// <summary>결과는 이미 정해졌고 연출만 한다 - 전부 돌다가 왼쪽 릴부터 하나씩 멈춘다.</summary>
        private IEnumerator SlotSpinRoutine(SlotMachine.SpinResult result)
        {
            _slotSpinning = true;
            _slotPendingPayout = result.Payout;
            _slotReelsText.color = Color.white;

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

                _slotReelsText.text = FormatReels(shown);
                yield return new WaitForSeconds(0.06f);
                elapsed += 0.06f;
            }

            _slotPendingPayout = 0;
            _slotSpinning = false;

            if (result.MatchCount == 3)
            {
                _slotReelsText.color = new Color(1f, 0.85f, 0.3f);
                ShowResult($"잭팟! 3개 일치 - {result.Payout}골드 획득!", new Color(1f, 0.85f, 0.3f));
            }
            else if (result.MatchCount == 2)
            {
                ShowResult($"2개 일치 - {result.Payout}골드 돌려받음 (베팅 {result.Bet})", new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                _slotReelsText.color = new Color(0.6f, 0.6f, 0.6f);
                ShowResult($"꽝... {result.Bet}골드를 잃었습니다.", new Color(1f, 0.5f, 0.5f));
            }
        }

        private static string FormatReels(int[] reels) =>
            $"[ {SlotMachine.Symbols[reels[0]]} | {SlotMachine.Symbols[reels[1]]} | {SlotMachine.Symbols[reels[2]]} ]";

        private void RefreshSlotDisplay()
        {
            _slotButtonText.text = $"[S] 돌리기 ({SlotMachine.GetBet(false)}G)";
            _slotHighButtonText.text = $"[Shift+S] {SlotMachine.HighBetMultiplier}배 베팅 ({SlotMachine.GetBet(true)}G)";
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
                return $"{slot}: (미장착)";

            var atk = EquipmentData.AttackBonus(slot, grade.Value);
            var hp = EquipmentData.HealthBonus(slot, grade.Value);
            var tier = EquipmentEffects.TierOf(grade.Value);
            var effects = tier > 0 ? $"  <color=#FFD966>효과 {tier}/{EquipmentEffects.MaxTier}</color>" : ""; // 효과 설명은 게임 중 Tab 상태창
            return $"{slot}: {EquipmentData.DisplayName(slot, grade.Value)} (공격력+{atk:0.#} 체력+{hp:0.#}){effects}";
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
            canvasGo.AddComponent<GraphicRaycaster>();

            CreateLabel(canvasGo.transform, "Title",
                $"LoopRogue - Stage {StageProgress.CurrentStage}/{StageProgress.MaxStage}",
                30, FontStyle.Bold, Color.white, 240f, 560f);
            _resultText = CreateLabel(canvasGo.transform, "ResultText", string.Empty, 18, FontStyle.Normal, Color.white, 190f, 560f);
            _resultText.gameObject.SetActive(false);

            _weaponText = CreateLabel(canvasGo.transform, "WeaponText", string.Empty, 16, FontStyle.Normal, Color.white, 140f, 560f);
            _armorText = CreateLabel(canvasGo.transform, "ArmorText", string.Empty, 16, FontStyle.Normal, Color.white, 110f, 560f);
            _accessoryText = CreateLabel(canvasGo.transform, "AccessoryText", string.Empty, 16, FontStyle.Normal, Color.white, 80f, 560f);
            _potionText = CreateLabel(canvasGo.transform, "PotionText", string.Empty, 16, FontStyle.Normal,
                new Color(0.6f, 1f, 0.7f), 45f, 560f);

            _goldText = CreateLabel(canvasGo.transform, "GoldText", "보유 골드: 0", 20, FontStyle.Bold,
                new Color(1f, 0.85f, 0.3f), 5f, 560f);

            // 슬롯별 뽑기 버튼 3개를 한 줄로(검/갑옷/반지) - 두 줄 텍스트라 버튼/라벨 높이를 키운다.
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
            {
                var x = ((int)slot - 1) * 200f;
                var captured = slot;
                CreateButton(canvasGo.transform, $"Gacha{slot}Button", ButtonColor, -42f, 190f, 50f,
                    out var label, string.Empty, 14, Color.white, () => DoGachaPull(captured), x);
                label.rectTransform.sizeDelta = new Vector2(180f, 46f);
                _gachaButtonTexts[slot] = label;

                // 바로 아래 10연차 버튼
                CreateButton(canvasGo.transform, $"Gacha{slot}MultiButton", new Color(0.35f, 0.3f, 0.45f), -92f, 190f, 34f,
                    out var multiLabel, string.Empty, 14, Color.white, () => DoGachaMultiPull(captured), x);
                _gachaMultiButtonTexts[slot] = multiLabel;
            }

            CreateButton(canvasGo.transform, "AttackPotionButton", ButtonColor, -137f, 480f, 38f,
                out _attackButtonText, string.Empty, 17, Color.white, () => DoPotionBuy(PotionType.Attack, "공격력"));

            CreateButton(canvasGo.transform, "HealthPotionButton", ButtonColor, -182f, 480f, 38f,
                out _healthButtonText, string.Empty, 17, Color.white, () => DoPotionBuy(PotionType.Health, "체력"));

            var criticalRect = CreateButton(canvasGo.transform, "CriticalPotionButton", ButtonColor, -227f, 480f, 38f,
                out _criticalButtonText, string.Empty, 17, Color.white, () => DoPotionBuy(PotionType.Critical, "치명타"));
            _criticalButtonImage = criticalRect.GetComponent<Image>();

            CreateButton(canvasGo.transform, "StartButton", StartButtonColor, -282f, 480f, 46f,
                out _, $"[Enter / Space] 스테이지 {StageProgress.CurrentStage} 시작", 20, Color.white, StartRun);

            BuildChanceTable(canvasGo.transform);

            // 슬롯머신은 오른쪽 빈 공간에 따로 세운다(가운데 열은 이미 꽉 참).
            const float slotX = 470f;
            var slotTitle = CreateLabel(canvasGo.transform, "SlotTitle", "운명의 슬롯\n3개 일치 10배 / 2개 일치 0.5배",
                16, FontStyle.Bold, new Color(1f, 0.85f, 0.3f), 110f, 300f);
            slotTitle.rectTransform.sizeDelta = new Vector2(300f, 48f);
            slotTitle.rectTransform.anchoredPosition = new Vector2(slotX, 110f);

            _slotReelsText = CreateLabel(canvasGo.transform, "SlotReels", FormatReels(new[] { 0, 1, 2 }),
                24, FontStyle.Bold, Color.white, 55f, 300f);
            _slotReelsText.rectTransform.anchoredPosition = new Vector2(slotX, 55f);

            CreateButton(canvasGo.transform, "SlotButton", new Color(0.45f, 0.3f, 0.15f), 0f, 260f, 40f,
                out _slotButtonText, string.Empty, 16, Color.white, () => DoSlotSpin(false), slotX);
            CreateButton(canvasGo.transform, "SlotHighButton", new Color(0.5f, 0.2f, 0.15f), -48f, 260f, 40f,
                out _slotHighButtonText, string.Empty, 16, Color.white, () => DoSlotSpin(true), slotX);
        }

        /// <summary>등급 확률표 - 슬롯머신 반대편(왼쪽 빈 공간)에 등급 이름 열 + 검/갑옷/반지 열. 값은 RefreshGachaDisplay가 채운다.</summary>
        private void BuildChanceTable(Transform parent)
        {
            const float tableX = -470f;
            const float columnWidth = 66f;
            const float tableY = 30f;
            var grades = (ItemGrade[])Enum.GetValues(typeof(ItemGrade));
            var height = (grades.Length + 2) * 19f;

            var title = CreateLabel(parent, "ChanceTitle", "등급 확률 (상점 레벨별)", 16, FontStyle.Bold,
                new Color(1f, 0.85f, 0.3f), tableY + height / 2f + 16f, 300f);
            title.rectTransform.anchoredPosition = new Vector2(tableX, title.rectTransform.anchoredPosition.y);

            var names = new System.Text.StringBuilder("등급\n");
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
            image.color = bgColor;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            label = CreateLabel(go.transform, goName + "Label", content, fontSize, FontStyle.Normal, textColor, 0f, width - 20f);

            _buttons.Add((rect, onClick));
            return rect;
        }
    }
}
