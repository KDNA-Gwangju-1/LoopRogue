using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LoopRogue
{
    /// <summary>방향키 1회 입력 = 1턴. 빈 칸이면 이동, 적이 있는 칸이면 이동 대신 그 적을
    /// 공격(bump-to-attack) - 공격 자체를 직접 조작하지 않는다, 어디로 움직일지만 정하면 나머지는
    /// 자동이다. 플레이어 행동이 끝나면 곧바로 방의 몹 전체가 한 번씩 행동한다(RoomController.
    /// RunEnemyTurns) - 이 왕복 하나가 "턴 1회".</summary>
    public class PlayerActor : GridActor
    {
        private static readonly Color PlayerColor = new Color(0.25f, 0.55f, 1f);
        private const int ExpPerKill = 8;       // 스테이지 배율 곱하기 전 기본값
        private const int ExpPerBossKill = 80;  // 골드처럼 일반 몹의 10배

        // ---- 스킬(처음부터 보유, 턴 쿨타임) - Q 대시, E 회전 베기 ----
        public static int DashRange => Relics.Has(RelicType.ChargeHorn) ? Relics.ChargeHornDashRange : 3; // 유물 "돌진의 뿔"
        public const int DashCooldownTurns = 5;
        public const int SpinCooldownTurns = 6;
        public static float SpinDamageRate => Relics.Has(RelicType.SpinningBlade) ? Relics.SpinningBladeDamageRate : 0.8f; // 유물 "회전 칼날"

        private static readonly Vector2Int[] SpinOffsetsBase =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        private static readonly Vector2Int[] SpinOffsetsCross =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(0, -2), new Vector2Int(0, 2), new Vector2Int(-2, 0), new Vector2Int(2, 0),
        };

        /// <summary>회전 베기 범위 - 유물 "십자 문장"이면 상하좌우 2칸까지.</summary>
        private static Vector2Int[] SpinOffsets => Relics.Has(RelicType.CrossCrest) ? SpinOffsetsCross : SpinOffsetsBase;

        private static readonly Vector2Int[] Around8 =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        private static readonly Vector2Int[] Around4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        /// <summary>남은 쿨타임(행동 횟수). 0이면 사용 가능. 스킬을 쓰면 N이 되고 이후 행동(이동/공격/대기/스킬) 1번마다 1씩 준다.</summary>
        public int DashCooldown { get; private set; }
        public int SpinCooldown { get; private set; }
        /// <summary>Q를 눌러 대시 방향을 고르는 중(다음 방향키 = 대시, Q 다시 = 취소).</summary>
        public bool IsAimingDash { get; private set; }

        /// <summary>거미줄 속박 - 0보다 크면 이동·대시 불가(공격/회전 베기/대기는 가능). 행동 1번마다 1씩 준다.</summary>
        public int RootedTurns { get; private set; }

        /// <summary>방패병 정면을 때려서 피해가 줄어든 누적 횟수(봇 통계용).</summary>
        public static int ShieldBlockCount;

        /// <summary>거미줄에 맞았을 때(EnemyActor 거미가 부른다). 정화제 효과 중엔 무시.</summary>
        public void ApplyRoot(int turns)
        {
            if (WebImmuneTurns > 0)
                return;
            RootedTurns = Mathf.Max(RootedTurns, turns);
            IsAimingDash = false;
            AimingItem = null;
        }

        // ---- 아이템 ----
        /// <summary>방향을 고르는 중인 아이템(퀵슬롯/인벤토리에서 고른 횃불·폭탄·미끼·덫). 다음 방향키 = 사용, 같은 키 = 취소.</summary>
        public ItemType? AimingItem { get; private set; }
        /// <summary>정화제 - 남은 턴 동안 거미줄 무시.</summary>
        public int WebImmuneTurns { get; private set; }
        /// <summary>반사 부적 - 남은 턴 안에 맞는 보스 예고 공격 1회를 되돌린다.</summary>
        public int ReflectTurns { get; private set; }

        private static readonly System.Random ItemRng = new System.Random();

        /// <summary>보스 예고 공격에 맞는 순간 BossBrain이 부른다 - 반사 부적이 켜져 있으면 써버리고 true.</summary>
        public bool TryConsumeReflect()
        {
            if (ReflectTurns <= 0)
                return false;
            ReflectTurns = 0;
            return true;
        }

        /// <summary>아이템 쓰기 - 방향이 필요한 아이템은 dir가 있어야 한다. 쓸 수 없으면 false(턴 소모 없음, 아이템 그대로).</summary>
        public bool TryUseItem(ItemType type, Vector2Int? dir)
        {
            if (!Inventory.Has(type) || !Inventory.IsInQuickSlot(type))
                return false; // 퀵슬롯에 등록된 것만 쓸 수 있다
            if (ItemInfo.NeedsDirection(type) && !dir.HasValue)
                return false;

            Vector2Int target = default;
            if (type == ItemType.Bomb)
            {
                if (!PreviewBomb(dir.Value, out target))
                    return false;
            }
            else if (ItemInfo.NeedsDirection(type))
            {
                target = GridPos + dir.Value;
                if (!_room.CanPlaceAt(target))
                    return false;
            }
            else if (type == ItemType.Cleanse && RootedTurns <= 0 && WebImmuneTurns > 0)
            {
                return false;
            }

            BeginAction();
            if (EquipmentEffects.Has(ItemSlot.Accessory, 2) && ItemRng.NextDouble() < EquipmentEffects.ItemSaveChance)
                _room.ShowMessage(Loc.F("절약! {0}이(가) 남았다", ItemInfo.Name(type))); // 반지 "절약"
            else
                Inventory.Consume(type);
            ApplyItem(type, target);
            HitFeedback.OnItemUsed(this, type, target);
            EndTurn();
            return true;
        }

        private void ApplyItem(ItemType type, Vector2Int target)
        {
            switch (type)
            {
                case ItemType.Torch:
                    _room.PlaceTorch(target);
                    _room.ShowMessage(Loc.T("횃불을 놓았다"));
                    break;
                case ItemType.Bomb:
                    _room.ThrowBomb(target);
                    _room.ShowMessage(Loc.T("폭탄을 던졌다! 다음 턴에 터진다"));
                    break;
                case ItemType.Decoy:
                    _room.PlaceDecoy(target);
                    _room.ShowMessage(Loc.F("미끼! {0}턴 동안 몹들이 허수아비를 노린다", ItemInfo.DecoyTurns));
                    break;
                case ItemType.Trap:
                    _room.PlaceTrap(target);
                    _room.ShowMessage(Loc.T("덫을 놓았다"));
                    break;
                case ItemType.Smoke:
                    foreach (var e in _room.EnemiesWithin(GridPos, ItemInfo.SmokeRadius))
                        e.Stun(ItemInfo.SmokeStunTurns);
                    _room.ShowMessage(Loc.F("연막! 주변 몹이 {0}턴 동안 플레이어를 못 찾는다", ItemInfo.SmokeStunTurns));
                    break;
                case ItemType.Cleanse:
                    RootedTurns = 0;
                    WebImmuneTurns = ItemInfo.CleanseImmuneTurns;
                    _room.ShowMessage(Loc.T("정화제! 거미줄이 풀렸다"));
                    break;
                case ItemType.Flash:
                {
                    var mirrorKept = false;
                    foreach (var e in _room.Enemies.Where(b => b != null && b.IsBoss && !b.Stats.IsDead))
                    {
                        // 10층 거울 파편(4연격)은 섬광탄으로 못 막는다 - 기절은 걸리지만 깔린 파편은 그대로 터진다.
                        if (e.MirrorShardsActive)
                            mirrorKept = true;
                        else
                            e.CancelBossPattern(_room);
                        e.Stun(ItemInfo.FlashBossStunTurns);
                    }
                    foreach (var e in _room.EnemiesWithin(GridPos, ItemInfo.FlashRadius).Where(m => !m.IsBoss))
                        e.Stun(ItemInfo.FlashEnemyStunTurns);
                    _room.ShowMessage(mirrorKept ? Loc.T("섬광탄! 보스는 비틀거리지만 거울 파편은 멈추지 않는다") : Loc.T("섬광탄! 보스의 공격이 취소됐다"));
                    break;
                }
                case ItemType.Reflect:
                    ReflectTurns = ItemInfo.ReflectTurns;
                    _room.ShowMessage(Loc.F("반사 부적! {0}턴 안에 맞는 보스 공격을 되돌린다", ItemInfo.ReflectTurns));
                    break;
                case ItemType.Weakness:
                    foreach (var e in _room.EnemiesWithin(GridPos, ItemInfo.WeaknessRadius))
                        e.MarkVulnerable();
                    foreach (var e in _room.Enemies.Where(b => b != null && b.IsBoss && !b.Stats.IsDead))
                        e.MarkVulnerable();
                    _room.ShowMessage(Loc.T("약점 표식! 받는 피해가 늘어난다"));
                    break;
                case ItemType.Barrier:
                    Stats.BlockNextHit = true;
                    _room.ShowMessage(Loc.T("보호막! 다음 피해 1회 무효"));
                    break;
            }
        }

        /// <summary>폭탄이 떨어질 칸 - 최대 BombRange칸 앞, 벽/몹 앞에서 멈춘다(몹 바로 앞에 떨어짐). 한 칸도 못 가면 막힌 옆 칸
        /// 자체에 터뜨린다(벽 부수기용). 방 밖이면 false. 봇도 같은 계산을 쓴다.</summary>
        public bool PreviewBomb(Vector2Int dir, out Vector2Int target)
        {
            target = GridPos;
            for (var i = 0; i < ItemInfo.BombRange; i++)
            {
                var next = target + dir;
                if (!Map.IsInBounds(next) || Map.IsWall(next) || Map.GetActorAt(next) != null)
                {
                    if (target == GridPos && Map.IsInBounds(next))
                        target = next;
                    break;
                }
                target = next;
            }
            return target != GridPos;
        }

        /// <summary>퀵슬롯/인벤토리에서 아이템을 골랐을 때 - 방향이 필요하면 고르기 모드, 아니면 바로 쓴다.</summary>
        public void SelectItem(ItemType type)
        {
            if (!CanAct || !Inventory.Has(type))
                return;
            IsAimingDash = false;
            if (ItemInfo.NeedsDirection(type))
            {
                AimingItem = AimingItem == type ? (ItemType?)null : type;
                return;
            }
            AimingItem = null;
            if (!TryUseItem(type, null))
                _room.ShowMessage(Loc.T("지금은 쓸 수 없다"));
        }

        /// <summary>자동 플레이 봇용.</summary>
        public bool BotUseItem(ItemType type, Vector2Int? dir = null) => CanAct && TryUseItem(type, dir);

        public LevelSystem Levels { get; private set; }

        /// <summary>적을 때릴 때마다(데미지 적용 직후) 그 적을 알린다 - GameHUD가 상단 타겟 체력바에 쓴다.</summary>
        public event Action<EnemyActor> OnAttackedEnemy;

        private RoomController _room;
        private SpriteRenderer _renderer;
        private SpriteAnimator _anim; // 전용 도트 그림(Resources/Sprites/Player)이 있으면 4방향 대기·걷기·공격

        private const string PlayerSpriteSet = "Player";
        private const float PlayerSpriteScale = 1.25f; // 64x64 캔버스에 몸이 40px 남짓 - 예전 그림(48px, 0.9칸)과 비슷한 키

        public void Initialize(RoomController room)
        {
            _room = room;
            RunProgress.EnsureLoaded();

            if (RunProgress.HasSavedRun)
            {
                // 로비를 들렀다가 돌아온 경우 - 씬이 새로 뜨면서 이 오브젝트 자체가 완전히 새로
                // 만들어졌으니(코드로 씬을 구성하는 이 프로젝트 특성상), 넘어가기 직전에 저장해둔
                // 레벨/경험치/스탯을 그대로 복원한다. 스냅샷은 "장비/영약 포함 합계"라 저장 시점의 장비/영약 몫을 빼서
                // 성장 몫을 되살리고, 고정 몫은 지금 장비/영약으로 새로 채운다(그 사이 로비에서 산 것도 자연히 포함).
                var addedHealth = RunProgress.CurrentPermanentHealth() - RunProgress.PermanentHealthAtSave;
                var addedCritical = RunProgress.CurrentPermanentCritical() - RunProgress.PermanentCriticalAtSave;
                Stats = new CharacterStats(
                    Mathf.Max(1f, RunProgress.MaxHealth - RunProgress.PermanentHealthAtSave),
                    Mathf.Max(1f, RunProgress.AttackPower - RunProgress.PermanentAttackAtSave))
                {
                    FixedMaxHealth = RunProgress.CurrentPermanentHealth(),
                    FixedAttack = RunProgress.CurrentPermanentAttack(),
                    CurrentHealth = RunProgress.CurrentHealth + addedHealth,
                    CriticalChanceRate = RunProgress.CriticalChanceRate + addedCritical,
                    DamageReductionRate = RunProgress.DamageReductionRate,
                    LifeStealRate = RunProgress.LifeStealRate,
                    RegenPerTurnRate = RunProgress.RegenPerTurnRate,
                    KillHealRate = RunProgress.KillHealRate,
                    ExpBonusRate = RunProgress.ExpBonusRate,
                    GoldBonusRate = RunProgress.GoldBonusRate,
                };
                Levels = new LevelSystem(Stats);
                Levels.RestoreProgress(RunProgress.Level, RunProgress.Exp, RunProgress.ExpToNext);
            }
            else
            {
                // 첫 시작(또는 로비를 거치지 않은 평범한 루프 리셋) - 기본 스탯(성장 몫) + 장비/영약(고정 몫).
                // 장비/영약은 GoldWallet처럼 영구 저장이라 매 런 시작마다 다시 채운다.
                Stats = new CharacterStats(maxHealth: 30f, attackPower: 6f)
                {
                    FixedMaxHealth = RunProgress.CurrentPermanentHealth(),
                    FixedAttack = RunProgress.CurrentPermanentAttack(),
                    CriticalChanceRate = RunProgress.CurrentPermanentCritical(), // 영약 + 칭호
                };
                Stats.FullHeal();
                Levels = new LevelSystem(Stats);
            }
            Levels.OnLevelUp += _ => HitFeedback.OnLevelUp(this); // 금빛 기둥

            // 전용 도트 그림(노란 머리 대검 용사, 4방향 대기·걷기·공격 - Resources/Sprites/Player). 없으면 파란 사각형.
            // (예전에 빌려 쓰던 StoryRPG 어쌔신 그림은 출처가 불확실해서 스팀 출시 전에 지웠다.)
            var playerSprite = SpriteAnimator.FirstFrame(PlayerSpriteSet);
            if (playerSprite != null)
            {
                _renderer = VisualUtil.CreateSpriteVisual(gameObject, playerSprite, GridConstants.CellSize * PlayerSpriteScale, sortingOrder: 1);
                _anim = gameObject.AddComponent<SpriteAnimator>();
                _anim.Setup(PlayerSpriteSet, _renderer, new Color(0.4f, 0.5f, 0.75f));
            }
            else
                _renderer = VisualUtil.CreateSquareVisual(gameObject, PlayerColor, GridConstants.CellSize * 0.65f, sortingOrder: 1);
            StatusIcons.AttachToPlayer(this); // 보호막·반사 부적·거미줄 면역 표시

            Stats.OnArmorEffect = message => _room.ShowMessage(message);
            Stats.OnRevived = MoveToSafeTile;
        }

        /// <summary>두 번째 숨으로 부활하면 몹에게서 먼 안전한 칸으로 옮긴다 - 제자리면 같은 몹 턴에 남은 몹들이
        /// 계속 때려서 부활하자마자 다시 죽었다(사용자 결정: 안전지역으로 이동).</summary>
        private void MoveToSafeTile()
        {
            if (_room == null)
                return;
            var safe = _room.FindSafeTile(GridPos);
            if (safe == GridPos)
                return;
            var from = transform.position;
            Map.MoveActor(this, safe);
            HitFeedback.OnDash(this, from);
        }

        /// <summary>방을 깔 때마다(RoomController.LoadRoom) - 갑옷 "보호막"을 다시 채운다.</summary>
        public void OnRoomEntered()
        {
            Stats.Shield = EquipmentEffects.Has(ItemSlot.Armor, 1) ? Stats.MaxHealth * EquipmentEffects.RoomShieldRate : 0f;
            _firstStrikeReady = Relics.Has(RelicType.FirstStrike); // 유물 "첫 일격"

            if (Relics.Has(RelicType.PhantomBanner)) // 유물 "망령의 깃발" - 옆 빈 칸에 허수아비
            {
                foreach (var d in Around4)
                {
                    if (_room.CanPlaceAt(GridPos + d))
                    {
                        _room.PlaceDecoy(GridPos + d);
                        break;
                    }
                }
            }
        }

        private bool _firstStrikeReady;

        /// <summary>새 시도(스테이지 입장, 사망 후 계속하기)마다 - 갑옷 "불굴"/"응급 처치"를 한 번씩 다시 채운다. 처음엔 방마다였는데
        /// 봇 측정에서 보스전 사망이 목표의 10~25%로 떨어져서 시도마다로 줄였다(사용자 결정 C안).</summary>
        public void OnAttemptStarted()
        {
            // 죽거나 로비를 다녀와 새 시도 - 지난 시도에 걸어둔 보호막·반사 부적·거미줄·면역·쿨타임·조준이 넘어오지 않게.
            Stats.BlockNextHit = false;
            ReflectTurns = 0;
            RootedTurns = 0;
            WebImmuneTurns = 0;
            DashCooldown = 0;
            SpinCooldown = 0;
            IsAimingDash = false;
            AimingItem = null;
            _comboCount = 0;
            Stats.UndyingReady = EquipmentEffects.Has(ItemSlot.Armor, 3);
            Stats.EmergencyHealReady = EquipmentEffects.Has(ItemSlot.Armor, 4);
            Stats.ReviveReady = Relics.Has(RelicType.SecondWind); // 유물 "두 번째 숨"
        }

        // 검 "연격" - 일반 공격(방향키로 때리기) 횟수. 스킬은 안 센다.
        private int _comboCount;

        /// <summary>검 고유 효과로 붙는 피해 배율(1 + 보너스 합). countCombo면 이번 타격을 연격 횟수에 센다.</summary>
        private float WeaponEffectMultiplier(EnemyActor enemy, bool countCombo)
        {
            var tier = EquipmentEffects.Tier(ItemSlot.Weapon);
            if (tier == 0)
                return 1f;

            var bonus = 0f;
            if (countCombo)
            {
                _comboCount++;
                var every = tier >= 5 ? EquipmentEffects.ComboEveryUpgraded : EquipmentEffects.ComboEvery;
                if (_comboCount % every == 0)
                    bonus += tier >= 5 ? EquipmentEffects.ComboBonusUpgraded : EquipmentEffects.ComboBonus;
            }
            var hpRate = enemy.Stats.CurrentHealth / Mathf.Max(1f, enemy.Stats.MaxHealth);
            if (tier >= 2 && hpRate <= EquipmentEffects.ExecuteThreshold)
                bonus += EquipmentEffects.ExecuteBonus;
            if (tier >= 3 && enemy.IsBoss)
                bonus += EquipmentEffects.BossHunterBonus;
            if (tier >= 4 && enemy.Stats.CurrentHealth >= enemy.Stats.MaxHealth)
                bonus += EquipmentEffects.FirstStrikeBonus;
            return 1f + bonus;
        }

        /// <summary>지금 이동/공격 입력을 받을 수 있는 상태인지(키 입력과 자동 플레이 봇이 같은 조건을 쓴다).</summary>
        public bool CanAct => _room != null && !_room.IsInputLocked && !Levels.IsChoosingUpgrade && !Stats.IsDead && !PauseMenu.BlocksInput && !InventoryUI.IsOpen
                              && !NpcDialogUI.BlocksInput; // 대화창을 닫은 키(Space/숫자)가 대기/퀵슬롯으로 또 읽히지 않게 닫힌 프레임까지

        /// <summary>자동 플레이 봇용 - 방향키 한 번 누른 것과 똑같이 한 턴 행동한다.</summary>
        public void BotAct(Vector2Int direction)
        {
            if (CanAct)
                TryAct(direction);
        }

        private void Update()
        {
            if (!CanAct)
                return;


            Vector2Int? direction = null;
            if (GameInput.Down(Key.W) || GameInput.Down(Key.UpArrow))
                direction = Vector2Int.up;
            else if (GameInput.Down(Key.S) || GameInput.Down(Key.DownArrow))
                direction = Vector2Int.down;
            else if (GameInput.Down(Key.A) || GameInput.Down(Key.LeftArrow))
                direction = Vector2Int.left;
            else if (GameInput.Down(Key.D) || GameInput.Down(Key.RightArrow))
                direction = Vector2Int.right;

            if (InventoryUI.LastCloseFrame == Time.frameCount)
                return; // 인벤토리를 닫은 키가 이동/대기로 또 읽히지 않게

            // 레벨업 카드를 숫자키로 고른 바로 그 프레임엔 같은 키가 퀵슬롯으로도 읽히지 않게.
            var choseCardThisFrame = Levels.LastChoiceFrame == Time.frameCount;
            for (var slot = 0; slot < Inventory.QuickSlotCount && !choseCardThisFrame; slot++)
            {
                var key = slot == 0 ? Key.Digit1 : slot == 1 ? Key.Digit2 : Key.Digit3;
                if (!GameInput.Down(key))
                    continue;
                var item = Inventory.QuickSlot(slot);
                if (item.HasValue && Inventory.Has(item.Value))
                    SelectItem(item.Value);
                else
                    _room.ShowMessage(Loc.F("퀵슬롯 {0}이 비어 있다 (I: 인벤토리)", slot + 1));
                return;
            }

            if (GameInput.Down(Key.F))
            {
                TryTalk();
                return;
            }

            if (AimingItem.HasValue)
            {
                if (GameInput.Down(Key.Q) || GameInput.Down(Key.E))
                    AimingItem = null; // 스킬 키를 누르면 아이템 고르기 취소(아래에서 스킬 처리)
                else if (direction.HasValue)
                {
                    var item = AimingItem.Value;
                    AimingItem = null;
                    if (!TryUseItem(item, direction.Value))
                        _room.ShowMessage(Loc.T("그쪽으로는 쓸 수 없다"));
                    return;
                }
                else
                    return; // 방향 고르는 중
            }

            if (GameInput.Down(Key.Q))
            {
                if (IsAimingDash)
                    IsAimingDash = false;
                else if (RootedTurns > 0)
                    _room.ShowMessage(Loc.T("거미줄에 묶여 대시할 수 없다!"));
                else if (DashCooldown > 0)
                    _room.ShowMessage(Loc.F("대시는 {0}턴 뒤에 쓸 수 있다", DashCooldown));
                else
                    IsAimingDash = true;
                return;
            }

            if (GameInput.Down(Key.E))
            {
                IsAimingDash = false;
                if (SpinCooldown > 0)
                    _room.ShowMessage(Loc.F("회전 베기는 {0}턴 뒤에 쓸 수 있다", SpinCooldown));
                else if (!TrySpin())
                    _room.ShowMessage(Loc.T("주변에 적이 없다"));
                return;
            }

            if (IsAimingDash)
            {
                if (direction.HasValue)
                {
                    IsAimingDash = false;
                    if (!TryDash(direction.Value))
                        _room.ShowMessage(Loc.T("그쪽으로는 대시할 수 없다"));
                }
                return; // 방향 고르는 중엔 대기 키도 무시
            }

            if (direction.HasValue)
                TryAct(direction.Value);
            else if (GameInput.Down(Key.Space))
                Wait(); // 스페이스바 = 제자리 대기(한 턴 넘기기)
        }

        /// <summary>F - 주변 8칸의 NPC와 대화(턴 소모 없음). 방향 고르기 중이면 취소하고 연다.</summary>
        private void TryTalk()
        {
            var npc = _room.FindNpcNear(GridPos);
            if (npc == null)
            {
                _room.ShowMessage(Loc.T("주변에 말을 걸 상대가 없다"));
                return;
            }
            IsAimingDash = false;
            AimingItem = null;
            npc.Talk(this);
        }

        private void TryAct(Vector2Int direction)
        {
            var targetPos = GridPos + direction;

            if (!Map.IsInBounds(targetPos) || Map.IsWall(targetPos))
                return; // 방 끝/벽 방향으로 헛눌러도 턴 소모 없음 - 로그라이크 관례.

            var occupant = Map.GetActorAt(targetPos);

            if (occupant is NpcActor npc)
            {
                _room.ShowMessage(Loc.F("{0} - F 키로 대화", npc.DisplayName)); // 부딪혀도 턴 소모 없음
                return;
            }

            if (RootedTurns > 0 && !(occupant is EnemyActor))
            {
                _room.ShowMessage(Loc.T("거미줄에 묶여 움직일 수 없다! (공격·회전 베기·대기는 가능)"));
                return; // 턴 소모 없음
            }

            if (occupant is EnemyActor enemy)
            {
                BeginAction();
                PlayAttackFlash(direction);
                if (StrikeEnemy(enemy, direction, 1f))
                    return; // 다음 방/승리 전환은 이미 끝났다 - 새 방 몹은 이번 턴엔 안 움직인다.
                if (NormalAttackRelicHits(targetPos, direction))
                    return;
            }
            else if (occupant is RoomEventActor ev)
            {
                // 이벤트 칸 - 발동시키고 그 칸으로 이동(한 턴 소모). 피의 제단처럼 고르는 창이 뜨면 칸이 남아 있고 턴도 안 쓴다
                // (예전엔 창이 떠 있는 동안 몹이 한 번 움직였다).
                if (!_room.TriggerEvent(ev))
                    return;
                BeginAction();
                Map.MoveActor(this, targetPos);
            }
            else if (occupant == null)
            {
                BeginAction();
                Map.MoveActor(this, targetPos);
                if (_room.TryUseExit(targetPos))
                    return; // 다음 방으로 넘어감 - 새 방 몹은 이번 턴에 안 움직인다
            }
            else
            {
                return; // 다른 종류의 점유자는 없다(플레이어는 하나뿐) - 안전망.
            }

            EndTurn();
        }

        /// <summary>몹 하나를 공격력 × damageRate로 때린다(치명타/흡혈/처치 보상 포함). 그 처치로 방이 넘어갔으면 true -
        /// 호출부는 곧바로 끝내야 한다(새 방 몹이 이번 턴에 움직이면 안 됨).</summary>
        /// 방패병 정면(지금 서 있는 칸이 방패가 보는 칸)이면 피해가 ShieldFrontDamageRate배 - 회전 베기만 ignoreShield로 무시한다.
        /// 흡혈은 일반 공격(방향키로 때리기)에만 - 스킬(대시/회전 베기)은 흡혈 없음(사용자 결정: 회전 베기가 몹마다 흡혈해서
        /// 일반 방에서 맞는 만큼 회복해 버렸다).
        private bool StrikeEnemy(EnemyActor enemy, Vector2Int direction, float damageRate, bool ignoreShield = false, bool isSkill = false)
        {
            var raw = Stats.RollAttackDamage(out var isCritical);
            if (_firstStrikeReady) // 유물 "첫 일격" - 방마다 첫 타격은 치명타 확정
            {
                _firstStrikeReady = false;
                if (!isCritical)
                {
                    isCritical = true;
                    raw = Stats.AttackPower * Stats.CriticalDamageMultiplier;
                }
            }
            if (isCritical && Relics.Has(RelicType.SniperEye)) // 유물 "저격수의 눈" - 치명타 피해 +50%p
                raw *= (Stats.CriticalDamageMultiplier + Relics.SniperCritDamageBonus) / Stats.CriticalDamageMultiplier;
            var berserk = Relics.Has(RelicType.Berserker) && Stats.CurrentHealth <= Stats.MaxHealth * Relics.BerserkerThreshold
                ? 1f + Relics.BerserkerBonus : 1f; // 유물 "광전사"
            var damage = raw * damageRate * enemy.DamageTakenMultiplier * berserk
                * WeaponEffectMultiplier(enemy, countCombo: !isSkill)
                * Curses.DamageDealtMultiplier // 저주 계약(분노·메마름)
                * RunBuffs.DamageDealtMultiplier; // 방 이벤트(숫돌·피의 제단)
            var blocked = !ignoreShield && enemy.IsShieldFront(GridPos);
            if (blocked)
            {
                damage *= EnemyActor.ShieldFrontDamageRate;
                ShieldBlockCount++;
            }
            if (Relics.Has(RelicType.Executioner) && !enemy.IsBoss &&
                enemy.Stats.CurrentHealth <= enemy.Stats.MaxHealth * Relics.ExecutionerThreshold)
                damage = Mathf.Max(damage, enemy.Stats.CurrentHealth); // 유물 "사형 집행자"
            enemy.Stats.TakeDamage(damage);
            DamagePopup.Spawn(enemy.transform.position, damage, blocked ? new Color(0.6f, 0.65f, 0.75f) : Color.white, isCritical && !blocked);
            HitFeedback.OnPlayerHitEnemy(this, enemy, direction, isCritical, blocked);
            OnAttackedEnemy?.Invoke(enemy);
            if (!isSkill)
                Stats.Heal(damage * Stats.EffectiveLifeSteal); // 흡혈 카드(최대 50%)

            if (!enemy.Stats.IsDead)
                return false;

            return ClaimKill(enemy);
        }

        /// <summary>죽은 몹을 플레이어가 잡은 것으로 처리(제거 + 처치 회복 + 경험치 + 골드/방 전환 + 가끔 아이템). 직접 때린 경우와
        /// 폭탄/반사 부적 공통. 방이 넘어갔으면 true.</summary>
        public bool ClaimKill(EnemyActor enemy)
        {
            HitFeedback.OnEnemyKilled(enemy);
            var blastTargets = CollectChainBlastTargets(enemy);
            if (!enemy.IsBoss && !enemy.IsMinion && ItemRng.NextDouble() < EquipmentEffects.MobDropChance + Relics.DropBonus)
            {
                var drop = Inventory.GiveRandomMissing();
                if (drop.HasValue)
                    _room.ShowMessage(Loc.F("{0}이(가) {1}을(를) 떨어뜨렸다!", enemy.DisplayName, ItemInfo.Name(drop.Value)));
            }
            Map.RemoveActor(enemy);
            Destroy(enemy.gameObject);
            Stats.Heal(Stats.MaxHealth * Stats.KillHealRate); // 처치 회복 카드

            // 경험치도 골드처럼 StageScaling 배율을 곱한다(몹은 스테이지마다 세지는데 경험치만
            // 고정이면 뒤로 갈수록 레벨이 안 오름). 보스 처치 경험치는 스테이지 클리어 화면보다
            // 먼저 들어가야 해서 NotifyEnemyDefeated(보스면 곧바로 클리어 처리)보다 앞에서 준다.
            var baseExp = enemy.IsMinion ? 0 : enemy.IsBoss ? ExpPerBossKill : ExpPerKill; // 졸개는 경험치 없음
            var repeat = enemy.IsBoss ? 1f : StageProgress.RepeatRewardMultiplier; // 반복 보상 감소(보스는 제외)
            Levels.AddExp(Mathf.RoundToInt(baseExp * StageScaling.RewardMultiplier(_room.Stage) * (1f + Stats.EffectiveExpBonus + Achievements.TitleExpBonus) * repeat)); // + 칭호

            if (!enemy.IsMinion)
                LobbyQuests.AddKill(); // 로비 안내자의 토벌 의뢰(누적 처치)
            Codex.Discover(Codex.MonsterKey(enemy));

            if (_room.NotifyEnemyDefeated(enemy))
                return true;
            return ResolveChainBlast(blastTargets);
        }

        // ---- 유물 추가타 ----
        private bool _chainBlasting;

        /// <summary>유물 "연쇄 폭발" - 일반 몹이 죽은 자리 주변 8칸의 살아있는 몹(제거되기 전에 모아둔다). 폭발로 죽은 몹은 다시 터지지 않는다.</summary>
        private List<EnemyActor> CollectChainBlastTargets(EnemyActor dead)
        {
            if (_chainBlasting || dead.IsBoss || !Relics.Has(RelicType.ChainBlast))
                return null;
            var list = new List<EnemyActor>();
            foreach (var d in Around8)
                if (Map.GetActorAt(dead.GridPos + d) is EnemyActor e && e != dead && !e.Stats.IsDead)
                    list.Add(e);
            return list;
        }

        private bool ResolveChainBlast(List<EnemyActor> targets)
        {
            if (targets == null || targets.Count == 0)
                return false;
            _chainBlasting = true;
            try
            {
                foreach (var e in targets)
                {
                    if (e == null || e.Stats.IsDead)
                        continue;
                    var damage = Stats.AttackPower * Relics.ChainBlastRate;
                    e.Stats.TakeDamage(damage);
                    DamagePopup.Spawn(e.transform.position, damage, new Color(1f, 0.6f, 0.2f));
                    if (e.Stats.IsDead && ClaimKill(e))
                        return true;
                }
                return false;
            }
            finally
            {
                _chainBlasting = false;
            }
        }

        /// <summary>일반 공격(방향키로 때리기) 직후 - 유물 "대지의 망치"(대상 상하좌우)와 "관통의 인장"(대상 뒤 1칸). 방이 넘어갔으면 true.</summary>
        private bool NormalAttackRelicHits(Vector2Int targetPos, Vector2Int direction)
        {
            if (Relics.Has(RelicType.EarthHammer))
            {
                foreach (var d in Around4)
                {
                    var p = targetPos + d;
                    if (p == GridPos || !(Map.GetActorAt(p) is EnemyActor e) || e.Stats.IsDead)
                        continue;
                    if (StrikeEnemy(e, d, Relics.EarthHammerSplashRate, ignoreShield: true, isSkill: true))
                        return true;
                }
            }
            if (Relics.Has(RelicType.PiercingSeal) && Map.GetActorAt(targetPos + direction) is EnemyActor behind && !behind.Stats.IsDead)
            {
                if (StrikeEnemy(behind, direction, Relics.PiercingRate, ignoreShield: true, isSkill: true))
                    return true;
            }
            return false;
        }

        /// <summary>대시 미리보기 - dir 방향으로 최대 DashRange칸, 벽/방 끝/이벤트 칸 앞에서 멈추고, 몹을 만나면 그 앞에서
        /// 멈춰 그 몹을 때린다. 한 칸도 못 가고 때릴 몹도 없으면 false(봇도 같은 계산을 쓴다).</summary>
        public bool PreviewDash(Vector2Int dir, out Vector2Int landing, out EnemyActor hit)
        {
            landing = GridPos;
            hit = null;
            for (var i = 0; i < DashRange; i++)
            {
                var next = landing + dir;
                if (!Map.IsInBounds(next) || Map.IsWall(next))
                    break;
                var occupant = Map.GetActorAt(next);
                if (occupant is EnemyActor e && !e.Stats.IsDead)
                {
                    hit = e;
                    break;
                }
                if (occupant != null)
                    break;
                landing = next;
            }
            return landing != GridPos || hit != null;
        }

        /// <summary>Q 대시 - 한 턴에 최대 3칸 이동, 가는 길에 몹이 있으면 그 앞까지 가서 한 대. 쓸 수 없으면 false(턴 소모 없음).</summary>
        private bool TryDash(Vector2Int dir)
        {
            if (DashCooldown > 0 || RootedTurns > 0 || !PreviewDash(dir, out var landing, out var hit))
                return false;

            BeginAction();
            DashCooldown = Mathf.Max(1, DashCooldownTurns - EquipmentEffects.SkillCooldownReduction // 반지 "민첩"
                - (Relics.Has(RelicType.Gale) ? Relics.GaleDashCooldownReduction : 0)); // 유물 "질풍"
            var from = transform.position;
            if (landing != GridPos)
                Map.MoveActor(this, landing);
            HitFeedback.OnDash(this, from);
            if (hit == null && _room.TryUseExit(landing))
                return true;

            if (hit != null)
            {
                PlayAttackFlash(dir);
                var dashRate = Relics.Has(RelicType.ChargeHorn) ? Relics.ChargeHornDashDamage : 1f; // 유물 "돌진의 뿔"
                if (StrikeEnemy(hit, dir, dashRate, isSkill: true))
                    return true;
            }

            if (Relics.Has(RelicType.TyrantPlate)) // 유물 "폭군의 갑주" - 내려선 자리 주변 8칸
            {
                foreach (var d in Around8)
                {
                    if (!(Map.GetActorAt(GridPos + d) is EnemyActor e) || e.Stats.IsDead || e == hit)
                        continue;
                    if (StrikeEnemy(e, d, Relics.TyrantLandingRate, ignoreShield: true, isSkill: true))
                        return true;
                }
            }

            EndTurn();
            return true;
        }

        /// <summary>회전 베기 대상 수(주변 8칸의 살아있는 몹) - 봇 판단용.</summary>
        public int CountSpinTargets()
        {
            var count = 0;
            foreach (var offset in SpinOffsets)
                if (Map.GetActorAt(GridPos + offset) is EnemyActor e && !e.Stats.IsDead)
                    count++;
            return count;
        }

        /// <summary>E 회전 베기 - 주변 8칸 몹 전체를 공격력의 80%로(치명타는 몹마다 따로). 주변에 몹이 없으면 false(턴 소모 없음).</summary>
        private bool TrySpin()
        {
            if (SpinCooldown > 0)
                return false;

            var targets = new List<(EnemyActor Enemy, Vector2Int Offset)>();
            foreach (var offset in SpinOffsets)
                if (Map.GetActorAt(GridPos + offset) is EnemyActor e && !e.Stats.IsDead)
                    targets.Add((e, offset));
            if (targets.Count == 0)
                return false;

            BeginAction();
            SpinCooldown = SpinCooldownTurns - EquipmentEffects.SkillCooldownReduction;
            PlayAttackFlash();
            HitFeedback.OnSpin(this);
            // 보스를 맨 뒤에 - 보스가 먼저 죽으면 남은 졸개가 같이 치워져서 그 뒤 타격이 의미 없어진다.
            foreach (var (enemy, offset) in targets.OrderBy(t => t.Enemy.IsBoss))
            {
                if (enemy == null || enemy.Stats.IsDead)
                    continue;
                if (StrikeEnemy(enemy, offset, SpinDamageRate, ignoreShield: true, isSkill: true))
                    return true; // 방 전환 - 남은 대상은 이미 정리됐다
            }
            if (Relics.Has(RelicType.PulseCore)) // 유물 "파동의 핵" - 맞고 살아남은 몹 1턴 기절
            {
                foreach (var (enemy, _) in targets)
                    if (enemy != null && !enemy.Stats.IsDead)
                        enemy.Stun(1);
            }

            EndTurn();
            return true;
        }

        /// <summary>행동 하나(이동/공격/대기/스킬)가 확정될 때 - 스킬 쿨타임과 거미줄 속박을 1 줄인다.</summary>
        private void BeginAction()
        {
            if (RootedTurns > 0)
                RootedTurns--;
            if (WebImmuneTurns > 0)
                WebImmuneTurns--;
            if (ReflectTurns > 0)
                ReflectTurns--;
            if (DashCooldown > 0)
                DashCooldown--;
            if (SpinCooldown > 0)
                SpinCooldown--;
        }

        /// <summary>제자리 대기 - 아무것도 안 하고 한 턴 넘긴다(보스 저격 2발 같은 "움직이면 맞는" 공격 피하기용).</summary>
        private void Wait()
        {
            BeginAction();
            EndTurn();
        }

        /// <summary>자동 플레이 봇용 - 대기 키를 누른 것과 같다.</summary>
        public void BotWait()
        {
            if (CanAct)
                Wait();
        }

        /// <summary>자동 플레이 봇용 - Q+방향키와 같다. 못 쓰면 false.</summary>
        public bool BotDash(Vector2Int direction) => CanAct && TryDash(direction);

        /// <summary>자동 플레이 봇용 - E와 같다. 못 쓰면 false.</summary>
        public bool BotSpin() => CanAct && TrySpin();

        private void EndTurn()
        {
            Stats.Heal(Stats.MaxHealth * Stats.RegenPerTurnRate); // 재생 카드 - 행동 1회(대기 포함) = 1턴
            _room.RunEnemyTurns();
        }

        /// <summary>공격 모션 한 번(도트 그림이 없어 사각형이면 생략).</summary>
        /// <param name="direction">때린 쪽 - 회전 베기처럼 없으면 지금 보는 쪽</param>
        private void PlayAttackFlash(Vector2Int direction = default) => _anim?.PlayAttack(direction);
    }
}
