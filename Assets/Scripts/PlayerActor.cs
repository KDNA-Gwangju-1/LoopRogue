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

        /// <summary>마지막으로 쓴 스킬(대시·회전 베기, 없으면 Other) - 기억하는 기사의 "기억 재현"이 따라 한다. 방에 들어갈 때 지운다.</summary>
        public AttackStyle LastSkill { get; private set; }
        /// <summary>Q를 눌러 대시 방향을 고르는 중(다음 방향키 = 대시, Q 다시 = 취소).</summary>
        public bool IsAimingDash { get; private set; }

        /// <summary>거미줄 속박 - 0보다 크면 이동·대시 불가(공격/회전 베기/대기는 가능). 행동 1번마다 1씩 준다.</summary>
        public int RootedTurns { get; private set; }

        /// <summary>방패병 정면을 때려서 피해가 줄어든 누적 횟수(봇 통계용).</summary>
        public static int ShieldBlockCount;

        /// <summary>플레이어가 몹을 잡은 순간(봇 통계용) - 직접 타격·폭탄·반사 등 ClaimKill 공통 경로에서 부른다.</summary>
        public static event Action<EnemyActor> EnemyKilled;

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
        /// <summary>연막 - 쓴 순간 주변 몹이 "연막 속"이 되고, 플레이어가 공격하기 전까지(또는 다음 방까지) 유지된다(사용자 결정:
        /// 사람은 봇처럼 늘 최선수를 두지 않으니 괜찮다). 그동안 캐릭터는 반투명.</summary>
        public bool InSmoke { get; private set; }
        private const float SmokeAlpha = 0.35f;
        private readonly List<EnemyActor> _smokedEnemies = new List<EnemyActor>();

        /// <summary>공격하는 순간(몹을 때림 / 폭탄 던짐) 연막이 걷힌다 - 걸려 있던 몹이 다시 움직인다.</summary>
        private void BreakSmoke()
        {
            if (!InSmoke)
                return;
            InSmoke = false;
            foreach (var e in _smokedEnemies)
                if (e != null)
                    e.SetInSmoke(false);
            _smokedEnemies.Clear();
            if (HitFeedback.Enabled)
                DeathBurst.Spawn(transform.position, new Color(0.62f, 0.64f, 0.7f), count: 12, speed: 3f, size: 0.25f);
        }

        private static readonly System.Random ItemRng = new System.Random();

        /// <summary>보스 예고 공격에 맞는 순간 BossBrain이 부른다 - 반사 부적이 켜져 있으면 써버리고 true.</summary>
        /// <summary>반사 부적(10-16 사용자) - 켜져 있으면 이번 보스 공격(예고 공격·평타·4층 포탑 파동)을 막고, 내 평타 한 대만큼(치명타는 따로 굴림)
        /// target에게 되돌린다. 무적이면 0(받는 피해 배율). 한 번 막으면 꺼진다(5턴이 지나도 꺼짐). 막았으면 true - 호출부는 피해를 주지 않는다.</summary>
        public bool ReflectInto(EnemyActor target, RoomController room)
        {
            if (!TryConsumeReflect())
                return false;
            if (target == null || target.Stats.IsDead)
                return true;
            var damage = Stats.RollAttackDamage(out var isCritical) * target.DamageTakenMultiplier;
            target.Stats.TakeDamage(damage);
            DamagePopup.Spawn(target.transform.position, damage, new Color(0.6f, 0.9f, 1f), isCritical);
            if (HitFeedback.Enabled)
            {
                Fx.Play("hit_spark", target.transform.position, 1.5f, tint: new Color(0.6f, 0.9f, 1f));
                ActorJuice.Get(target).Flash(new Color(0.6f, 0.9f, 1f));
            }
            room?.ShowMessage(isCritical ? Loc.T("반사 부적! 보스의 공격을 치명타로 되돌렸다") : Loc.T("반사 부적! 보스의 공격을 되돌렸다"));
            room?.HandleBossDamagedByItem(target); // 그걸로 죽었으면 처치 처리
            return true;
        }

        /// <summary>5층 환각(감염 1부터, 예고가 거꾸로 보임) / 혼란(감염 2부터, 방향키 반대) - HUD 표시용.</summary>
        public int Infection => _room != null ? _room.PlayerInfection : 0;
        public bool IsHallucinating => _room != null && _room.PlayerHallucinating;
        public bool IsConfused => _room != null && _room.PlayerConfused;

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
                if (type == ItemType.Torch && _room.IsAltar(target))
                {
                    // 불 꺼진 제단 쪽으로 횃불 - 벽화의 방으로(10-19). 몹이 남아 있으면 안 쓰고 안내만.
                    if (!_room.AltarReady(out var why))
                    {
                        _room.ShowMessage(why);
                        return false;
                    }
                }
                else if (!_room.CanPlaceAt(target))
                    return false;
            }
            else if (type == ItemType.Cleanse && RootedTurns <= 0 && WebImmuneTurns > 0 && _room.CleanseGimmick(GridPos, dryRun: true).Count == 0)
            {
                return false;
            }

            // 아이템은 턴을 쓰지 않는다(10-14 사용자) - 써도 몹이 행동하지 않고 쿨타임·거미줄·반사 남은 턴도 그대로. 같은 턴에 이어서 움직이거나 때릴 수 있다.
            if (EquipmentEffects.Has(ItemSlot.Accessory, 2) && ItemRng.NextDouble() < EquipmentEffects.ItemSaveChance)
            {
                _room.ShowMessage(Loc.F("절약! {0}이(가) 남았다", ItemInfo.Name(type))); // 반지 "절약"
                PlayLog.ItemUsed(type, saved: true);
            }
            else
            {
                Inventory.Consume(type);
                PlayLog.ItemUsed(type, saved: false);
            }
            ApplyItem(type, target);
            HitFeedback.OnItemUsed(this, type, target);
            TutorialStage.NotifyItemUsed(type, _room); // 튜토리얼 아이템 방 - 써 본 아이템 체크
            return true;
        }

        private void ApplyItem(ItemType type, Vector2Int target)
        {
            switch (type)
            {
                case ItemType.Torch:
                    if (_room.IsAltar(target))
                    {
                        _room.LightAltar();
                        break;
                    }
                    _room.PlaceTorch(target);
                    _room.ShowMessage(Loc.T("횃불을 놓았다"));
                    break;
                case ItemType.Bomb:
                    BreakSmoke(); // 폭탄도 공격
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
                    // 방 안 몹 전부(보스 포함, 10-19 사용자 - 멀리 있던 몹이 계속 때리는 게 이상했다). 나중에 나온 몹은 EnemyActor.TakeTurn이 막는다.
                    // 연막 속에서도 턴은 그대로 흐른다(기믹 제한 턴·남은 턴은 몹 턴 끝 RoomController가 센다).
                    foreach (var e in _room.Enemies)
                    {
                        if (e == null || e.Stats.IsDead)
                            continue;
                        e.SetInSmoke(true);
                        if (!_smokedEnemies.Contains(e))
                            _smokedEnemies.Add(e);
                    }
                    InSmoke = true;
                    if (HitFeedback.Enabled)
                        DeathBurst.Spawn(transform.position, new Color(0.62f, 0.64f, 0.7f), count: 30, speed: 4f, size: 0.4f);
                    _room.ShowMessage(Loc.T("연막! 공격하기 전까지 몹들이 플레이어를 못 찾는다"));
                    break;
                case ItemType.Cleanse:
                {
                    RootedTurns = 0;
                    WebImmuneTurns = ItemInfo.CleanseImmuneTurns;
                    var cleansed = _room.CleanseGimmick(GridPos); // 4층 점액 씻기·5층 감염 -1
                    _room.ShowMessage(cleansed.Count > 0 ? Loc.F("정화제! {0}", string.Join(", ", cleansed)) : Loc.T("정화제! 거미줄이 풀렸다"));
                    if (HitFeedback.Enabled)
                    {
                        // 초록 기운이 몸을 감쌌다가 퍼지며 풀려나간다
                        var cleanse = new Color(0.45f, 1f, 0.55f);
                        ActorJuice.Get(this).Flash(cleanse);
                        AuraEffect.Spawn(transform, new Color(0.45f, 1f, 0.55f, 0.9f));
                        DeathBurst.Spawn(transform.position, new Color(0.55f, 1f, 0.6f), count: 16, speed: 5f, size: 0.14f);
                    }
                    break;
                }
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
                    _room.ShowMessage(Loc.F("반사 부적! {0}턴 안에 맞는 보스 공격 한 번을 막고 되돌린다", ItemInfo.ReflectTurns));
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
            if (_room.Puzzle != null && !_room.Puzzle.AllowsTools)
            {
                _room.ShowMessage(Loc.T("여기선 스킬·아이템을 쓸 수 없다")); // 인벤토리 창에서 고른 경우
                return;
            }
            if (TutorialStage.Blocks(Tutorial.Act.Item, _room, type))
            {
                AimingItem = null;
                return; // 튜토리얼 - 이 방 안내에 없는 아이템
            }
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
                    RoomShieldCardRate = RunProgress.RoomShieldCardRate,
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

        /// <summary>에디터 보스 러시 - 저장(RunProgress)을 바꿔 끼운 뒤 지금 플레이어의 스탯·레벨을 그 값으로 다시 맞추고 체력을 채운다.
        /// Stats/Levels 객체는 HUD가 붙들고 있어서 새로 만들지 않고 값만 덮어쓴다(Initialize의 저장 복원과 같은 계산).</summary>
        public void ReloadStatsFromSave()
        {
            RunProgress.EnsureLoaded();
            if (!RunProgress.HasSavedRun)
                return;
            Stats.BaseMaxHealth = Mathf.Max(1f, RunProgress.MaxHealth - RunProgress.PermanentHealthAtSave);
            Stats.BaseAttack = Mathf.Max(1f, RunProgress.AttackPower - RunProgress.PermanentAttackAtSave);
            Stats.FixedMaxHealth = RunProgress.CurrentPermanentHealth();
            Stats.FixedAttack = RunProgress.CurrentPermanentAttack();
            Stats.CriticalChanceRate = RunProgress.CriticalChanceRate + (RunProgress.CurrentPermanentCritical() - RunProgress.PermanentCriticalAtSave);
            Stats.DamageReductionRate = RunProgress.DamageReductionRate;
            Stats.LifeStealRate = RunProgress.LifeStealRate;
            Stats.RoomShieldCardRate = RunProgress.RoomShieldCardRate;
            Stats.KillHealRate = RunProgress.KillHealRate;
            Stats.ExpBonusRate = RunProgress.ExpBonusRate;
            Stats.GoldBonusRate = RunProgress.GoldBonusRate;
            Stats.FullHeal();
            Levels.RestoreProgress(RunProgress.Level, RunProgress.Exp, RunProgress.ExpToNext);
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
            // 연막은 그 방에서만 - 새 방에 들어가면 걷힌다(몹 목록은 이미 새 방 것)
            InSmoke = false;
            _smokedEnemies.Clear();
            LastSkill = AttackStyle.Other;

            var shieldRate = (EquipmentEffects.Has(ItemSlot.Armor, 1) ? EquipmentEffects.RoomShieldRate : 0f) + Stats.RoomShieldCardRate; // 갑옷 효과 + 수호막 카드
            Stats.Shield = Stats.MaxHealth * shieldRate;
            _firstStrikeReady = Relics.Has(RelicType.FirstStrike); // 유물 "첫 일격"
            if (EquipmentEffects.HasMemorySet)
                Stats.BlockNextHit = true; // Unknown 세트 "기억의 벼림" - 방마다 처음 받는 피해 1번 무효

            _bannerReady = Relics.Has(RelicType.PhantomBanner); // 유물 "망령의 깃발" - 방마다 1번(TryPhantomBanner)
        }

        private bool _bannerReady;

        /// <summary>유물 "망령의 깃발" - 몹 턴이 끝났을 때(RoomController.EndOfEnemyTurns) 체력이 30% 아래면 방마다 1번,
        /// 내 둘레 8칸 중 몹에 가장 가까운 빈칸에 허수아비(7턴). 예전엔 방에 들어갈 때마다 옆에 생겨 길만 막았다(10-27 테스터 "선택할 이유가 없다").</summary>
        public void TryPhantomBanner()
        {
            if (!_bannerReady || Stats.IsDead || Stats.CurrentHealth > Stats.MaxHealth * Relics.PhantomBannerHealthRatio
                || _room.ActiveDecoy != null)
                return;
            var enemies = _room.Enemies.Where(e => e != null && !e.Stats.IsDead).ToList();
            if (enemies.Count == 0)
                return;
            var best = (Vector2Int?)null;
            var bestDist = int.MaxValue;
            foreach (var d in Around8)
            {
                var p = GridPos + d;
                if (!_room.CanPlaceAt(p))
                    continue;
                var dist = enemies.Min(e => GridMap.FootprintDistance(e, p));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            if (!best.HasValue)
                return;
            _bannerReady = false;
            _room.PlaceDecoy(best.Value, Relics.PhantomBannerTurns);
            _room.ShowMessage(Loc.F("망령의 깃발! 허수아비가 {0}턴 동안 공격을 받아 낸다", Relics.PhantomBannerTurns));
        }

        private bool _firstStrikeReady;
        private int _echoCount; // 유물 "메아리 칼날" - 일반 공격 횟수
        private bool _echoReady; // 3번째 공격으로 대상이 죽어 아직 못 쓴 추가타 - 다음 일반 공격에

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

        // 5층 감염 - 몸이 보랏빛으로 물들어 천천히 숨쉬듯(1단계 옅게, 2단계 짙게). 맞아서 번쩍이는 중(다른 색)이면 건드리지 않는다.
        private static readonly Color InfectTint1 = new Color(0.86f, 0.66f, 1f);
        private static readonly Color InfectTint2 = new Color(0.74f, 0.52f, 1f);

        /// <summary>연막 중이면 반투명 - 맞아서 번쩍이는 색(ActorJuice)이 덮어써도 투명도만은 매 프레임 다시 맞춘다.</summary>
        private void LateUpdate()
        {
            if (_renderer == null)
                return;
            var color = _renderer.color;
            if (!ActorJuice.IsFlashingNow(this))
            {
                var infection = Infection;
                var tint = infection <= 0 ? Color.white
                    : Color.Lerp(Color.white, infection == 1 ? InfectTint1 : InfectTint2, 0.8f + 0.2f * Mathf.Sin(Time.time * 3f));
                if (color.r != tint.r || color.g != tint.g || color.b != tint.b)
                {
                    color = new Color(tint.r, tint.g, tint.b, color.a);
                    _renderer.color = color;
                }
            }
            var alpha = InSmoke ? SmokeAlpha : 1f;
            if (!Mathf.Approximately(color.a, alpha))
            {
                color.a = alpha;
                _renderer.color = color;
            }
        }

        // ---- 10층 발악(RoomController.MirrorLastStand) - 사람은 누르는 즉시 움직이고(10-23 사용자 - 예전엔 0.6초 박자까지 모아 두었다가
        // 실행해 최대 0.6초 늦게 움직였다), LastStandTurnSeconds 동안 아무것도 안 하면 시간이 대기 턴을 넘긴다. 행동할 때마다 시계는 처음부터.
        // 봇은 원래대로 턴마다 바로 움직인다.
        private float _realtimeClock;

        private bool HandleRealtimeTurn(Vector2Int? direction)
        {
            if (_room == null || !_room.MirrorLastStand || GameHUD.AutoPlayActive)
            {
                _realtimeClock = 0f;
                return false;
            }
            if (_room.TickLastStandClock(Time.deltaTime))
            {
                _realtimeClock = 0f;
                Wait(); // 발악 시간이 다 됐다 - 한 턴 넘겨 기사가 바로 쓰러지게
                return true;
            }
            if (GameInput.Down(Key.Q))
            {
                if (RootedTurns > 0)
                    _room.ShowMessage(Loc.T("거미줄에 묶여 대시할 수 없다!"));
                else if (DashCooldown > 0)
                    _room.ShowMessage(Loc.F("대시는 {0}턴 뒤에 쓸 수 있다", DashCooldown));
                else
                    IsAimingDash = !IsAimingDash;
            }
            if (direction.HasValue)
            {
                var dash = IsAimingDash;
                IsAimingDash = false;
                if (dash && TryDash(direction.Value))
                {
                    _realtimeClock = 0f;
                    return true;
                }
                if (RootedTurns > 0) // 묶였으면 TryAct가 턴을 안 써서 시간이 멈춘다 → 제자리 대기로 한 턴
                {
                    _realtimeClock = 0f;
                    Wait();
                    return true;
                }
                if (Map.IsWalkable(GridPos + direction.Value))
                {
                    _realtimeClock = 0f;
                    TryAct(direction.Value);
                    return true;
                }
                // 벽·기사 쪽은 무시(턴을 안 쓴다) - 칼은 통하지 않고, 잘못 누른 키로 한 턴을 날리지 않게
            }
            else if (GameInput.Down(Key.Space))
            {
                _realtimeClock = 0f;
                Wait();
                return true;
            }

            _realtimeClock += Time.deltaTime;
            if (_realtimeClock < RoomController.LastStandTurnSeconds)
                return true;
            _realtimeClock = 0f;
            Wait(); // 아무것도 안 했으면 제자리 - 시간은 기다려 주지 않는다
            return true;
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
            if (direction.HasValue && GlitchRoom.Active == GlitchRoom.Rule.ReverseControls)
                direction = -direction.Value; // 오류방 - 방향이 뒤집혔다
            if (direction.HasValue && IsConfused)
                direction = -direction.Value; // 5층 혼란 - 감염 2부터 방향키가 뒤집힌다(10-16 사용자)

            if (InventoryUI.LastCloseFrame == Time.frameCount || RoomMapView.LastConfirmFrame == Time.frameCount)
                return; // 인벤토리를 닫은 키·시작 방을 확정한 스페이스가 이동/대기로 또 읽히지 않게

            if (HandleRealtimeTurn(direction))
                return; // 10층 발악 - 이동·대시·대기만, 시간이 턴을 넘긴다

            // 퍼즐·미니게임 방 - G로 포기, 몹과 싸우는 퍼즐이 아니면 스킬·아이템 금지
            var puzzle = _room.Puzzle;
            if (puzzle != null)
            {
                if (GameInput.Down(Key.G))
                {
                    puzzle.GiveUp();
                    return;
                }
                if (GameInput.Down(Key.H))
                {
                    puzzle.ShowRulesAgain(); // 설명 창 다시 보기
                    return;
                }
                if (puzzle.HandleKeys())
                    return; // 상자 밀기 [Z] 되돌리기 등
                if (!puzzle.AllowsTools && (GameInput.Down(Key.Q) || GameInput.Down(Key.E) ||
                                            GameInput.Down(Key.Digit1) || GameInput.Down(Key.Digit2) || GameInput.Down(Key.Digit3)))
                {
                    _room.ShowMessage(Loc.T("여기선 스킬·아이템을 쓸 수 없다"));
                    return;
                }
            }

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

            if (GameInput.Down(Key.Q) && !IsAimingDash && TutorialStage.Blocks(Tutorial.Act.Dash, _room))
                return; // 튜토리얼 - 안내에 없는 행동(대시 조준을 끄는 건 된다)
            if (GameInput.Down(Key.E) && TutorialStage.Blocks(Tutorial.Act.Spin, _room))
                return;

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
            else if (GameInput.Down(Key.Space) && !TutorialStage.Blocks(Tutorial.Act.Wait, _room))
                Wait(); // 스페이스바 = 제자리 대기(한 턴 넘기기)
        }

        /// <summary>F - 주변 8칸의 NPC와 대화(턴 소모 없음). 방향 고르기 중이면 취소하고 연다.</summary>
        private void TryTalk()
        {
            if (_room.TryViewMural(GridPos))
            {
                IsAimingDash = false;
                AimingItem = null;
                return; // 벽화의 방 - 벽화 큰 그림(10-19)
            }
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

        /// <summary>퍼즐이 직접 처리한 행동도 한 턴으로 친다(쿨타임·재생·몹 턴·퍼즐 턴 끝).</summary>
        public void ConsumeTurn()
        {
            BeginAction();
            EndTurn();
        }

        private void TryAct(Vector2Int direction)
        {
            if (_room.Puzzle != null && _room.Puzzle.HandleAct(direction))
                return; // 얼음 미끄럼·상자 밀기처럼 퍼즐이 이동을 직접 처리

            var targetPos = GridPos + direction;

            if (_room.IsGolemCrystal(targetPos))
            {
                BeginAction(); // 이것도 한 턴 - 쿨타임·속박·면역이 같이 줄어야 한다(퍼즐 ConsumeTurn과 같게)
                _room.BreakGolemCrystal(targetPos); // 3층 골렘 수정 - 붙어서 그쪽으로 움직이면 한 번에 깬다(안 깨면 폭발)
                PlayLog.Act("기믹조작");
                PlayAttackFlash(direction);
                EndTurn();
                return;
            }

            if (_room.IsLightMirror(targetPos))
            {
                BeginAction(); // 이것도 한 턴 - 쿨타임·속박·면역이 같이 줄어야 한다(퍼즐 ConsumeTurn과 같게)
                _room.RotateLightMirror(targetPos); // 10층 빛 단계 거울 - 붙어서 그쪽으로 움직이면 /·\가 바뀐다
                PlayLog.Act("기믹조작");
                PlayAttackFlash(direction);
                EndTurn();
                return;
            }

            if (_room.IsOgreHole(targetPos))
            {
                BeginAction(); // 이것도 한 턴 - 쿨타임·속박·면역이 같이 줄어야 한다(퍼즐 ConsumeTurn과 같게)
                _room.FillOgreHole(targetPos); // 2층 오우거 바닥 구멍 - 붙어서 그쪽으로 움직이면 한 턴 써서 메운다
                PlayLog.Act("기믹조작");
                PlayAttackFlash(direction);
                EndTurn();
                return;
            }

            if (!Map.IsInBounds(targetPos) || Map.IsWall(targetPos))
                return; // 방 끝/벽 방향으로 헛눌러도 턴 소모 없음 - 로그라이크 관례.

            var occupant = Map.GetActorAt(targetPos);

            if (occupant is NpcActor npc)
            {
                _room.ShowMessage(Loc.F("{0} - F 키로 대화", npc.DisplayName)); // 부딪혀도 턴 소모 없음
                return;
            }

            if (!(occupant is EnemyActor) && TutorialStage.Blocks(Tutorial.Act.Move, _room))
                return; // 튜토리얼 - 지금 안내는 이동이 아니다(턴 소모 없음)

            if (RootedTurns > 0 && !(occupant is EnemyActor))
            {
                _room.ShowMessage(Loc.T("거미줄에 묶여 움직일 수 없다! (공격·회전 베기·대기는 가능)"));
                return; // 턴 소모 없음
            }

            if (occupant is EnemyActor enemy)
            {
                if (TutorialStage.Blocks(Tutorial.Act.Attack, _room))
                    return; // 튜토리얼 - 아직 공격 차례가 아니다(턴 소모 없음)
                BeginAction();
                PlayLog.Act("공격");
                PlayAttackFlash(direction);
                if (StrikeEnemy(enemy, direction, 1f, style: AttackStyle.Normal))
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
            else if (occupant == null || occupant is DecoyActor)
            {
                BeginAction();
                PlayLog.Act("이동");
                if (occupant is DecoyActor decoy) // 내 허수아비(미끼·망령의 깃발)는 자리를 바꿔 지나간다(10-27 사용자)
                {
                    var from = GridPos;
                    Map.RemoveActor(decoy);
                    Map.MoveActor(this, targetPos);
                    Map.PlaceActor(decoy, from);
                }
                else
                    Map.MoveActor(this, targetPos);
                if (_room.TryUseExit(targetPos))
                    return; // 다음 방으로 넘어감 - 새 방 몹은 이번 턴에 안 움직인다
                _room.CheckPlayerCrack(); // 2층 오우거 균열 - 밟으면 묶인다
                _room.CheckSlimePuddle(); // 4층 슬라임 킹 점액 - 밟으면 한 턴 묶인다
                _room.Puzzle?.PlayerMoved(targetPos); // 룬·보물 찾기처럼 밟은 칸에 반응하는 퍼즐
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
        private bool StrikeEnemy(EnemyActor enemy, Vector2Int direction, float damageRate, bool ignoreShield = false, bool isSkill = false,
            AttackStyle style = AttackStyle.Other)
        {
            if (enemy.TryReadAttack(style, _room)) // 기억하는 기사의 간파 - 같은 공격을 연달아 쓰면 피한다
            {
                BreakSmoke();
                return false;
            }
            var raw = Stats.RollAttackDamage(out var isCritical);
            var firstStrike = 1f;
            if (_firstStrikeReady) // 유물 "첫 일격" - 방마다 첫 타격 피해 1.5배(치명타와 따로 곱한다)
            {
                _firstStrikeReady = false;
                firstStrike = Relics.FirstStrikeDamage;
            }
            if (isCritical && Relics.Has(RelicType.SniperEye)) // 유물 "저격수의 눈" - 치명타 피해 +50%p
                raw *= (Stats.CriticalDamageMultiplier + Relics.SniperCritDamageBonus) / Stats.CriticalDamageMultiplier;
            var berserk = Relics.Has(RelicType.Berserker) && Stats.CurrentHealth <= Stats.MaxHealth * Relics.BerserkerThreshold
                ? 1f + Relics.BerserkerBonus : 1f; // 유물 "광전사"
            var executioner = Relics.Has(RelicType.Executioner) && !enemy.IsBoss ? Relics.ExecutionerDamage : 1f; // 유물 "사형 집행자" - 일반 몹 피해 증가
            var eliteHunter = Relics.Has(RelicType.EliteHunter) && (enemy.IsElite || enemy.IsMimic) ? Relics.EliteHunterDamage : 1f; // 유물 "정예 사냥꾼"
            var snare = Relics.Has(RelicType.SnareHunter) && enemy.IsSnared ? Relics.SnareHunterDamage : 1f; // 유물 "사냥꾼의 올가미"
            var damage = raw * damageRate * enemy.DamageTakenMultiplier * berserk * executioner * firstStrike * eliteHunter * snare
                * Relics.StoppedWatchMultiplier // 유물 "멈춘 회중시계"
                * WeaponEffectMultiplier(enemy, countCombo: !isSkill)
                * Curses.DamageDealtMultiplier // 저주 계약(분노·메마름)
                * RunBuffs.DamageDealtMultiplier; // 방 이벤트(숫돌·피의 제단)
            var blocked = !ignoreShield && enemy.IsShieldFront(GridPos);
            if (blocked)
            {
                damage *= EnemyActor.ShieldFrontDamageRate;
                ShieldBlockCount++;
            }
            var countered = enemy.IsGuarding; // 반격병 반격 자세 - 일반 공격·대시·회전 베기 모두 막고 되받아친다
            if (countered)
            {
                damage *= EnemyActor.SentinelGuardDamageRate;
                blocked = true;
            }
            enemy.Stats.TakeDamage(damage);
            DamagePopup.Spawn(enemy.transform.position, damage, blocked ? new Color(0.6f, 0.65f, 0.75f) : Color.white, isCritical && !blocked);
            HitFeedback.OnPlayerHitEnemy(this, enemy, direction, isCritical, blocked);
            OnAttackedEnemy?.Invoke(enemy);
            BreakSmoke(); // 때리는 순간 연막이 걷힌다(일반 공격·대시·회전 베기 공통)
            if (!isSkill)
                Stats.Heal(damage * Stats.EffectiveLifeSteal); // 흡혈 카드(최대 50%)

            if (countered)
                enemy.CounterAttack(this, _room);
            if (!enemy.Stats.IsDead)
                return false;

            return ClaimKill(enemy);
        }

        /// <summary>죽은 몹을 플레이어가 잡은 것으로 처리(제거 + 처치 회복 + 경험치 + 골드/방 전환 + 가끔 아이템). 직접 때린 경우와
        /// 폭탄/반사 부적 공통. 방이 넘어갔으면 true.</summary>
        public bool ClaimKill(EnemyActor enemy)
        {
            EnemyKilled?.Invoke(enemy);
            HitFeedback.OnEnemyKilled(enemy);
            var blastTargets = CollectChainBlastTargets(enemy);
            if (!enemy.IsBoss && !enemy.NoReward && ItemRng.NextDouble() < EquipmentEffects.MobDropChance + Relics.DropBonus)
            {
                var drop = Inventory.GiveRandomMissing();
                if (drop.HasValue)
                    _room.ShowMessage(Loc.F("{0}이(가) {1}을(를) 떨어뜨렸다!", enemy.DisplayName, ItemInfo.Name(drop.Value)));
            }
            Map.RemoveActor(enemy);
            if (enemy.Kind == EnemyKind.Splitter && !enemy.IsSplitChild)
                _room.SpawnSplitChildren(enemy); // 분열체는 죽으면 둘로 갈라진다(처치 처리 전에 - 방이 빈 걸로 안 치게)
            if (GlitchRoom.Active == GlitchRoom.Rule.Revive && !enemy.IsBoss && !enemy.IsGlitchRevived && !enemy.IsPuzzleMob)
                _room.SpawnGlitchRevive(enemy); // 오류방 - 한 번 되살아난다(처치 처리 전에)
            Destroy(enemy.gameObject);
            Stats.Heal(Stats.MaxHealth * Stats.EffectiveKillHeal); // 처치 회복 카드(상한 36%)
            if (Relics.Has(RelicType.BloodHunter) && !enemy.NoReward) // 유물 "피의 사냥꾼" - 졸개·분열 조각은 제외
                Stats.Heal(Stats.MaxHealth * Relics.BloodHunterHealRate);

            // 경험치도 골드처럼 StageScaling 배율을 곱한다(몹은 스테이지마다 세지는데 경험치만
            // 고정이면 뒤로 갈수록 레벨이 안 오름). 보스 처치 경험치는 스테이지 클리어 화면보다
            // 먼저 들어가야 해서 NotifyEnemyDefeated(보스면 곧바로 클리어 처리)보다 앞에서 준다.
            var baseExp = enemy.NoReward ? 0 : enemy.IsBoss ? ExpPerBossKill : ExpPerKill; // 졸개·분열 조각은 경험치 없음
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
                if (Map.GetActorAt(dead.GridPos + d) is EnemyActor e && e != dead && !e.Stats.IsDead && !list.Contains(e))
                    list.Add(e); // 3x3 보스는 여러 칸이 걸쳐도 한 번만
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
                    // 칼이 안 통하는 몹(분신·거울상·포탑)과 보스 무적·단단함도 지킨다 - 다른 공격과 같은 배율
                    var damage = Stats.AttackPower * Relics.ChainBlastRate * e.DamageTakenMultiplier;
                    if (damage <= 0f)
                        continue;
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
            // 방금 때린 몹(3x3 보스면 그 몸 칸들이 주변에 걸쳐 있다) - 같은 몹을 추가타로 다시 때리지 않는다
            var primary = Map.GetActorAt(targetPos) as EnemyActor;
            if (Relics.Has(RelicType.EchoBlade))
            {
                // 유물 "메아리 칼날" - 일반 공격 3번째마다 한 번 더(스킬 취급이라 흡혈 없음). 죽인 공격도 센다(10-27 사용자 - 예전엔 살아남은
                // 공격만 세서 몹이 잘 죽는 일반 방에선 거의 안 터졌다). 3번째 공격으로 대상이 죽었으면 추가타를 다음 일반 공격에 붙인다.
                if (++_echoCount % Relics.EchoBladeEvery == 0)
                    _echoReady = true;
                if (_echoReady && primary != null && !primary.Stats.IsDead)
                {
                    _echoReady = false;
                    if (StrikeEnemy(primary, direction, Relics.EchoBladeRate, isSkill: true))
                        return true;
                }
            }
            if (Relics.Has(RelicType.EarthHammer))
            {
                var struck = new HashSet<EnemyActor>();
                if (primary != null)
                    struck.Add(primary);
                foreach (var d in Around4)
                {
                    var p = targetPos + d;
                    if (p == GridPos || !(Map.GetActorAt(p) is EnemyActor e) || e.Stats.IsDead || !struck.Add(e))
                        continue;
                    if (StrikeEnemy(e, d, Relics.EarthHammerSplashRate, ignoreShield: true, isSkill: true))
                        return true;
                }
            }
            if (Relics.Has(RelicType.PiercingSeal))
            {
                // 대상 몸 바로 뒤 칸(3x3 보스면 몸을 지나 그 너머)
                var behindPos = targetPos + direction;
                while (primary != null && GridMap.Occupies(primary, behindPos))
                    behindPos += direction;
                if (Map.GetActorAt(behindPos) is EnemyActor behind && behind != primary && !behind.Stats.IsDead &&
                    StrikeEnemy(behind, direction, Relics.PiercingRate, ignoreShield: true, isSkill: true))
                    return true;
            }
            return false;
        }

        /// <summary>대시 미리보기 - dir 방향으로 최대 DashRange칸, 벽/방 끝/이벤트 칸 앞에서 멈추고, 몹을 만나면 그 앞에서
        /// 멈춰 그 몹을 때린다. 한 칸도 못 가고 때릴 몹도 없으면 false(봇도 같은 계산을 쓴다).</summary>
        public bool PreviewDash(Vector2Int dir, out Vector2Int landing, out EnemyActor hit) =>
            PreviewDash(dir, out landing, out hit, out _);

        /// <summary>passed = 유물 "그림자 걸음"일 때 뚫고 지나가며 벨 몹들(가는 길 순서, 첫 몹 = hit). 그림자 걸음이면 몹을 지나 방 끝/벽/다른 칸 앞까지
        /// 가서 마지막 빈칸에 내려선다(몹 칸엔 안 내려섬).</summary>
        public bool PreviewDash(Vector2Int dir, out Vector2Int landing, out EnemyActor hit, out List<EnemyActor> passed)
        {
            passed = null;
            if (Relics.Has(RelicType.ShadowStep))
            {
                landing = GridPos;
                hit = null;
                passed = new List<EnemyActor>();
                var at = GridPos;
                for (var i = 0; i < DashRange; i++)
                {
                    var next = at + dir;
                    if (!Map.IsInBounds(next) || Map.IsWall(next))
                        break;
                    var occupant = Map.GetActorAt(next);
                    if (occupant is EnemyActor e)
                    {
                        if (!e.Stats.IsDead && !passed.Contains(e))
                            passed.Add(e);
                    }
                    else if (occupant != null && !(occupant is DecoyActor))
                        break;
                    at = next;
                    if (occupant == null)
                        landing = next;
                }
                hit = passed.Count > 0 ? passed[0] : null;
                return landing != GridPos || hit != null;
            }
            landing = GridPos;
            hit = null;
            var cursor = GridPos;
            for (var i = 0; i < DashRange; i++)
            {
                var next = cursor + dir;
                if (!Map.IsInBounds(next) || Map.IsWall(next))
                    break;
                var occupant = Map.GetActorAt(next);
                if (occupant is EnemyActor e && !e.Stats.IsDead)
                {
                    if (cursor == landing) // 허수아비 바로 너머의 몹은 못 친다(내려설 칸이 몹 옆이 아니다)
                        hit = e;
                    break;
                }
                cursor = next;
                if (occupant is DecoyActor)
                    continue; // 허수아비는 뛰어넘는다 - 그 칸엔 내려서지 않는다
                if (occupant != null)
                    break;
                landing = next;
            }
            return landing != GridPos || hit != null;
        }

        /// <summary>대시를 쓰면 걸리는 쿨타임(장비·유물 감소 포함) - 6층 포위 폭발 간격도 이걸 기준으로 한다(BossBrain).</summary>
        public static int DashCooldownMax => Mathf.Max(1, DashCooldownTurns - EquipmentEffects.SkillCooldownReduction // 반지 "민첩"
            - (Relics.Has(RelicType.Gale) ? Relics.GaleDashCooldownReduction : 0)); // 유물 "질풍"

        /// <summary>Q 대시 - 한 턴에 최대 3칸 이동, 가는 길에 몹이 있으면 그 앞까지 가서 한 대. 쓸 수 없으면 false(턴 소모 없음).</summary>
        private bool TryDash(Vector2Int dir)
        {
            if (DashCooldown > 0 || RootedTurns > 0 || !PreviewDash(dir, out var landing, out var hit, out var passed))
                return false;

            BeginAction();
            PlayLog.Act("대시");
            DashCooldown = DashCooldownMax;
            var from = transform.position;
            var startPos = GridPos;
            LastSkill = AttackStyle.Dash;
            if (landing != GridPos)
                Map.MoveActor(this, landing);
            HitFeedback.OnDash(this, from);
            if (hit == null && _room.TryUseExit(landing))
                return true;
            _room.CheckPlayerCrack(); // 대시로 내려선 칸이 균열이면 묶인다
            _room.CheckSlimePuddle(); // 4층 슬라임 킹 점액 - 걸어서 밟을 때와 같게
            if (landing != startPos)
                _room.Puzzle?.PlayerMoved(landing); // 룬·보물 찾기처럼 밟은 칸에 반응하는 퍼즐

            if (hit != null)
            {
                PlayAttackFlash(dir);
                var dashRate = Relics.Has(RelicType.ChargeHorn) ? Relics.ChargeHornDashDamage : 1f; // 유물 "돌진의 뿔"
                foreach (var target in passed ?? new List<EnemyActor> { hit }) // 유물 "그림자 걸음"이면 지나친 몹 전부
                {
                    if (target == null || target.Stats.IsDead)
                        continue;
                    if (StrikeEnemy(target, dir, dashRate, isSkill: true, style: AttackStyle.Dash))
                        return true;
                }
            }

            if (Relics.Has(RelicType.TyrantPlate)) // 유물 "폭군의 갑주" - 내려선 자리 주변 8칸(같은 몹은 한 번만)
            {
                var struck = new HashSet<EnemyActor>();
                if (hit != null)
                    struck.Add(hit);
                foreach (var d in Around8)
                {
                    if (!(Map.GetActorAt(GridPos + d) is EnemyActor e) || e.Stats.IsDead || !struck.Add(e))
                        continue;
                    if (StrikeEnemy(e, d, Relics.TyrantLandingRate, ignoreShield: true, isSkill: true))
                        return true;
                }
            }

            EndTurn();
            return true;
        }

        /// <summary>회전 베기 대상 수(주변 8칸의 살아있는 몹) - 봇 판단용.</summary>
        /// <summary>봇용 - 회전 베기 범위(주변 8칸)에 반격 자세인 반격병이 있는지(맞히면 되받아친다).</summary>
        public bool SpinHitsGuard()
        {
            foreach (var offset in SpinOffsets)
                if (Map.GetActorAt(GridPos + offset) is EnemyActor e && !e.Stats.IsDead && e.IsGuarding)
                    return true;
            return false;
        }

        public int CountSpinTargets()
        {
            var seen = new HashSet<EnemyActor>(); // 3x3 보스는 여러 칸이 걸쳐도 한 마리
            foreach (var offset in SpinOffsets)
                if (Map.GetActorAt(GridPos + offset) is EnemyActor e && !e.Stats.IsDead)
                    seen.Add(e);
            return seen.Count;
        }

        /// <summary>E 회전 베기 - 주변 8칸 몹 전체를 공격력의 80%로(치명타는 몹마다 따로). 주변에 몹이 없으면 false(턴 소모 없음).</summary>
        private bool TrySpin()
        {
            if (SpinCooldown > 0)
                return false;

            var targets = new List<(EnemyActor Enemy, Vector2Int Offset)>();
            foreach (var offset in SpinOffsets)
                if (Map.GetActorAt(GridPos + offset) is EnemyActor e && !e.Stats.IsDead && !targets.Any(t => t.Enemy == e))
                    targets.Add((e, offset)); // 3x3 보스는 처음 걸린 칸 방향으로 한 번만
            if (targets.Count == 0)
                return false;

            BeginAction();
            PlayLog.Act("회전베기");
            SpinCooldown = SpinCooldownTurns - EquipmentEffects.SkillCooldownReduction;
            LastSkill = AttackStyle.Spin;
            PlayAttackFlash();
            HitFeedback.OnSpin(this);
            // 보스를 맨 뒤에 - 보스가 먼저 죽으면 남은 졸개가 같이 치워져서 그 뒤 타격이 의미 없어진다.
            foreach (var (enemy, offset) in targets.OrderBy(t => t.Enemy.IsBoss))
            {
                if (enemy == null || enemy.Stats.IsDead)
                    continue;
                if (StrikeEnemy(enemy, offset, SpinDamageRate, ignoreShield: true, isSkill: true, style: AttackStyle.Spin))
                    return true; // 방 전환 - 남은 대상은 이미 정리됐다
            }
            _whirlwindWait = 0f;
            if (Relics.Has(RelicType.Whirlwind) && WhirlwindPush(targets))
                return true;
            if (Relics.Has(RelicType.PulseCore)) // 유물 "파동의 핵" - 맞고 살아남은 몹 기절(10-19 사용자 1 → 2턴)
            {
                foreach (var (enemy, _) in targets)
                    if (enemy != null && !enemy.Stats.IsDead)
                        enemy.Stun(Relics.PulseCoreStunTurns);
            }

            if (_whirlwindWait > 0f && HitFeedback.Enabled)
                StartCoroutine(EndTurnAfter(_whirlwindWait)); // 유물 "소용돌이" - 밀려난 몹 그림이 다 보이고 나서 몹 턴
            else
                EndTurn();
            return true;
        }

        /// <summary>유물 "소용돌이" - 회전 베기에 맞고 살아남은 몹을 나에게서 먼 쪽으로 2칸까지 민다(10-27 사용자 1 → 2). 가다가 벽·다른 몹·방 끝에 막히면 거기서 멈추고 공격력 30% 추가 피해.
        /// 큰 몹(보스)은 안 밀리고 추가 피해도 없다. 방이 넘어갔으면 true.</summary>
        private const float WhirlwindSlideSeconds = 0.2f; // 튕겨 날아가는 시간
        private const float KnockClipSeconds = 0.45f;     // 밀려남 그림 4장
        private const float CrashClipSeconds = 0.8f;      // 충돌 그림 6장
        private float _whirlwindWait;                     // 이번 회전 베기 뒤 몹 턴을 미룰 시간(그림이 다 보이게)

        private static System.Collections.IEnumerator PlayClipLater(SpriteAnimator anim, string clip, float seconds, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (anim != null)
                anim.PlayClip(clip, seconds, 0.1f);
        }

        /// <summary>몹 턴을 seconds초 뒤에 - 그동안 입력을 잠근다(밀려남·충돌 그림이 몹이 다시 걸어오기 전에 다 보이게).</summary>
        private System.Collections.IEnumerator EndTurnAfter(float seconds)
        {
            _room.IsInputLocked = true;
            yield return new WaitForSeconds(seconds);
            _room.IsInputLocked = false;
            EndTurn();
        }

        private bool WhirlwindPush(List<(EnemyActor Enemy, Vector2Int Offset)> targets)
        {
            int pushed = 0, crashed = 0;
            try
            {
                // 바깥쪽(십자 문장의 2칸 거리) 몹부터 - 안쪽 몹이 밀려날 자리를 먼저 비워 준다
                foreach (var (enemy, offset) in targets.OrderByDescending(t => Mathf.Abs(t.Offset.x) + Mathf.Abs(t.Offset.y)))
                {
                    if (enemy == null || enemy.Stats.IsDead || enemy.IsLarge || enemy.IsBoss || enemy.IsBladeProof)
                        continue; // 큰 몹·보스·기믹 몹(7층 분신·10층 거울상·4층 포탑)은 안 밀린다
                    var dir = new Vector2Int(System.Math.Sign(offset.x), System.Math.Sign(offset.y));
                    // 최대 WhirlwindPushCells칸 - 가다가 벽·다른 몹·방 끝·출구에 막히면 거기서 멈추고 부딪힌 피해(한 칸도 못 갔어도)
                    var to = enemy.GridPos;
                    var pushedFrom = enemy.GridPos;
                    var blocked = false;
                    for (var i = 0; i < Relics.WhirlwindPushCells; i++)
                    {
                        var next = to + dir;
                        if (!Map.IsWalkable(next) || next == _room.ExitPosition)
                        {
                            blocked = true;
                            break;
                        }
                        to = next;
                    }
                    if (to != enemy.GridPos)
                    {
                        var from = enemy.transform.position;
                        Map.MoveActor(enemy, to);
                        pushed++;
                        if (HitFeedback.Enabled)
                        {
                            // 걷기 모션(뒤돌아 한 걸음)이 아니라 튕겨 나가는 모습으로 - 그림 방향은 그대로, 낮게 미끄러지듯 날아가 착지
                            if (enemy.TryGetComponent<SpriteAnimator>(out var anim))
                                anim.SnapToGrid();
                            ActorJuice.Get(enemy).Hop(from, 0.15f, WhirlwindSlideSeconds, stretch: 0f, landSquish: 0.35f);
                            Fx.Play("crush_dust", enemy.GridWorldPosition, 0.9f, fps: 18f);
                        }
                    }
                    // 밀려남·부딪힘 그림(Sprites/{세트}/knock_south_·crash_south_ - 지금은 슬라임만, 없으면 튕겨 나가는 연출만)
                    if (HitFeedback.Enabled && enemy.TryGetComponent<SpriteAnimator>(out var reaction))
                    {
                        // 잘 보이게 길게(10-27 사용자 "너무 짧아 안 보인다") - 몹 턴은 그동안 기다린다(TrySpin → EndTurnAfter)
                        if (blocked)
                        {
                            var delay = to != enemy.GridPos || pushedFrom != enemy.GridPos ? WhirlwindSlideSeconds : 0f; // 날아가다 막혔으면 닿은 뒤에 철퍽
                            StartCoroutine(PlayClipLater(reaction, "crash", CrashClipSeconds, delay));
                            _whirlwindWait = Mathf.Max(_whirlwindWait, delay + CrashClipSeconds * 0.75f);
                        }
                        else
                        {
                            reaction.PlayClip("knock", KnockClipSeconds, 0.1f);
                            _whirlwindWait = Mathf.Max(_whirlwindWait, KnockClipSeconds);
                        }
                    }
                    if (!blocked)
                        continue;
                    crashed++;
                    if (StrikeEnemy(enemy, dir, Relics.WhirlwindWallRate, ignoreShield: true, isSkill: true))
                        return true;
                }
                return false;
            }
            finally
            {
                if (pushed + crashed > 0)
                    PlayLog.Note($"소용돌이: {pushed}마리 밀어냄, {crashed}마리 막혀서 추가 피해");
            }
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
            PlayLog.Act("대기");
            Tutorial.NotifyWaited(); // 첫 판 안내 "대기" 단계
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
            PlayLog.Turn();
            _room.RunEnemyTurns();
        }

        /// <summary>공격 모션 한 번(도트 그림이 없어 사각형이면 생략).</summary>
        /// <param name="direction">때린 쪽 - 회전 베기처럼 없으면 지금 보는 쪽</param>
        private void PlayAttackFlash(Vector2Int direction = default) => _anim?.PlayAttack(direction);
    }
}
