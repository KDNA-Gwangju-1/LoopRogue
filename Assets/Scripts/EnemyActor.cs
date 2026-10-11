using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>적 종류 - 근접(기본)/원거리(궁수)/방패병(정면 공격 80% 감소, 삼각형)/거미(거미줄로 1턴 속박)/폭발병,
    /// 5층부터 하나씩: 치유사(아군 회복)/돌진병(일직선 예고 돌진)/분열체(죽으면 둘로 갈라짐)/도적(플레이어 뒤로 순간이동).
    /// 순서는 봇 처치 통계(AutoPlayBot.KillNames)와 맞춰져 있으니 끝에만 추가할 것.</summary>
    public enum EnemyKind
    {
        Melee,
        Ranged,
        Shield,
        Spider,
        Bomber,
        Healer,
        Charger,
        Splitter,
        Assassin,
    }

    /// <summary>일반 몹/보스 공통 - 턴제라 실시간 이동 대신 TurnManager 역할을 하는
    /// RoomController가 불러주는 TakeTurn() 안에서만 행동한다. 근접은 플레이어 상하좌우 네 칸 중 다른 몹이
    /// 안 맡은 칸을 하나 예약하고 BFS 최단 경로로 다가가서 포위한다. 궁수는 같은 줄 사거리 안이면 쏘고(아군은
    /// 통과), 붙으면 물러나고, 아니면 줄을 맞추는 자리로 움직인다.</summary>
    public class EnemyActor : GridActor
    {
        /// <summary>궁수 사거리(같은 가로/세로 줄, 칸 수).</summary>
        public const int RangedAttackRange = 4;

        /// <summary>궁수가 가장 선호하는 플레이어와의 거리(맨해튼) - 너무 붙지도 너무 멀지도 않게.</summary>
        private const int RangedPreferredDistance = 3;

        /// <summary>궁수는 한 번 움직이면 이 턴 수만큼 못 움직인다(사격/근접 공격은 가능) - 플레이어와 속도가 같으면
        /// 영원히 못 잡는다. 처음엔 "물러나기"만 쿨타임을 걸었는데, 벽 사이에서 옆으로 자리를 옮기며 도망 다니는
        /// 경우가 남아서(봇 로그의 벽 뒤 궁수 술래잡기) 이동 자체를 두 턴에 한 번으로 느리게 했다.</summary>
        private const int RetreatCooldownTurns = 2;

        private int _retreatCooldown;

        /// <summary>방패병: 정면(바라보는 칸)에서 들어오는 공격·대시 피해 배율. 회전 베기는 방향 무시.</summary>
        public const float ShieldFrontDamageRate = 0.2f;

        /// <summary>방패병은 두 턴에 한 번만 90도씩 돌 수 있다 - 옆/뒤로 돌아 들어가면 때릴 틈이 생기게.
        /// 도는 턴엔 공격/이동을 안 한다.</summary>
        private const int ShieldTurnCooldown = 1;
        private int _shieldTurnCooldown;

        /// <summary>방패병은 한 번 움직이면 다음 턴은 못 움직인다(공격은 가능) - 옆으로 비켜선 다음 턴에 못 따라와야 옆을 잡을 수 있다.</summary>
        private const int ShieldMoveCooldown = 1;
        private int _shieldMoveCooldown;

        /// <summary>거미: 같은 줄 2~4칸이면 거미줄(공격력 50% + 1턴 속박 = 이동·대시 불가), 쏜 뒤 이 턴 수 동안 다시 못 쏜다.</summary>
        public const int WebRange = 4;
        private const int WebCooldownTurns = 4;
        private const float WebDamageRate = 0.5f;
        private int _webCooldown;

        /// <summary>폭발병: 플레이어 옆에 붙으면 공격 대신 도화선에 불을 붙이고(주변 3x3 예고), 자기 다음 턴에 터진다.
        /// 폭발은 플레이어만 맞는다(공격력 × 이 배율, 다른 몹은 안 맞음). 터지기 전에 잡으면 불발.</summary>
        public const float BlastDamageRate = 3f;
        private bool _fuseLit;

        /// <summary>치유사: HealCooldownTurns마다 사거리 안에서 체력 비율이 가장 낮은 다친 아군(자기 제외, 보스 제외)을 최대 체력의
        /// HealRate만큼 회복시킨다. 궁수처럼 거리를 두고 두 턴에 한 번만 움직인다 - 먼저 쫓아가 잡아야 하는 몹.</summary>
        public const int HealRange = 5;
        public const float HealRate = 0.3f;
        private const int HealCooldownTurns = 3;
        private const int HealerPreferredDistance = 4;
        private int _healCooldown = 1;

        /// <summary>돌진병: 같은 줄 2~ChargeRange칸이고 사이에 벽이 없으면 그 줄을 예고(빨강)하고, 자기 다음 턴에 예고한 줄을 따라
        /// 끝까지 달린다. 달리다 플레이어를 만나면 공격력 × ChargeDamageRate, 몹/벽을 만나면 그 앞에서 멈춘다. 돌진한 뒤엔
        /// ChargeRecoverTurns턴 동안 숨을 고른다(때릴 기회). 그 밖엔 근접 몹처럼 다가간다.</summary>
        public const int ChargeRange = 5;
        public const float ChargeDamageRate = 2f;
        private const int ChargeCooldownTurns = 4;
        private const int ChargeRecoverTurns = 1;
        private int _chargeCooldown = 1;
        private Vector2Int? _chargeDir;
        private List<Vector2Int> _chargeTiles;

        /// <summary>분열체: 죽으면 그 자리 근처에 작은 분열체 SplitCount마리(체력·공격력 비율 Split*Ratio, 더는 안 갈라짐)가 나온다.
        /// 갈라진 조각은 골드/경험치/아이템이 없고, 갓 나온 턴은 굳어서 움직이지 않는다.</summary>
        public const int SplitCount = 2;
        public const float SplitHealthRatio = 0.4f;
        public const float SplitAttackRatio = 0.6f;
        public bool IsSplitChild { get; private set; }
        private int _spawnFreezeTurns;

        /// <summary>도적: 플레이어와 BlinkMinDistance~BlinkMaxDistance칸 떨어져 있고 순간이동이 준비됐으면 플레이어 옆 빈 칸 중
        /// 자기에게서 가장 먼 칸(= 등 뒤)으로 순간이동한다(그 턴은 이동만). 순간이동 직후 첫 공격은 공격력 × BackstabRate.</summary>
        private const int BlinkMinDistance = 3;
        private const int BlinkMaxDistance = 7;
        private const int BlinkCooldownTurns = 5;
        public const float BackstabRate = 1.5f;
        private int _blinkCooldown = 2;
        private bool _backstabReady;

        /// <summary>퍼즐·미니게임(전투 퍼즐·버티기)이 부른 몹 - 보상 없음.</summary>
        public bool IsPuzzleMob { get; private set; }
        public void MarkAsPuzzleMob() => IsPuzzleMob = true;

        /// <summary>골드·경험치·아이템을 안 주는 몹(보스 졸개, 분열 조각, 퍼즐 몹).</summary>
        public bool NoReward => IsMinion || IsSplitChild || IsPuzzleMob;

        /// <summary>이번 턴에 노리는 칸(플레이어 또는 미끼)과 미끼.</summary>
        private Vector2Int _tp;
        private DecoyActor _decoyTarget;

        /// <summary>남은 기절 턴(섬광탄/연막탄/덫) - 그동안 아무 행동도 안 한다.</summary>
        public int StunTurns { get; private set; }
        public void Stun(int turns) => StunTurns = Mathf.Max(StunTurns, turns);

        /// <summary>약점 표식 - 남은 턴 동안 받는 피해 증가.</summary>
        /// <summary>약점 표식 - 사람은 실제 시간(ItemInfo.WeaknessSeconds초), 자동 플레이 봇은 턴(초고속이라 시간 기준이 안 맞음).</summary>
        public int VulnerableTurns { get; private set; }
        private float _vulnerableUntil = -1f;
        public bool IsVulnerable => VulnerableTurns > 0 || Time.time < _vulnerableUntil;

        public void MarkVulnerable()
        {
            if (GameHUD.AutoPlayActive)
                VulnerableTurns = Mathf.Max(VulnerableTurns, ItemInfo.WeaknessTurns);
            else
                _vulnerableUntil = Mathf.Max(_vulnerableUntil, Time.time + ItemInfo.WeaknessSeconds);
        }

        public float DamageTakenMultiplier => IsVulnerable ? ItemInfo.WeaknessDamageMultiplier : 1f;

        /// <summary>섬광탄 - 보스가 예고해둔 공격을 취소한다.</summary>
        public void CancelBossPattern(RoomController room) => _brain?.CancelPending(room);
        public bool MirrorShardsActive => _brain != null && _brain.MirrorShardsActive;

        /// <summary>방패병이 바라보는 방향(상하좌우). 다른 몹은 의미 없음.</summary>
        public Vector2Int Facing { get; private set; } = Vector2Int.left;

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public bool IsBoss { get; private set; }
        public bool IsMinion { get; private set; } // 보스가 소환한 졸개 - 골드/경험치 없음, 보스가 죽으면 같이 사라짐
        public EnemyKind Kind { get; private set; }

        private BossBrain _brain;

        private int _bossStage;
        public int BossStage => _bossStage; // 층 보스면 그 층(도감 키), 아니면 0
        private int _morphStage; // 거울 가면 기사가 지금 비추고 있는 보스 층(0 = 본모습)
        private bool _enraged;

        public bool IsMimic { get; private set; } // 방 이벤트 "미믹" - 잡으면 큰 보상(RoomController)
        public bool IsElite { get; private set; } // 방 이벤트 "도전의 깃발" 정예

        public string DisplayName => IsBoss ? BossDisplayName : IsMinion ? Loc.T("졸개") : IsMimic ? Loc.T("미믹") : IsSplitChild ? Loc.T("분열 조각") : (IsElite ? Loc.T("정예 ") : "") + Kind switch
        {
            EnemyKind.Ranged => Loc.T("궁수"),
            EnemyKind.Shield => Loc.T("방패병"),
            EnemyKind.Spider => Loc.T("거미"),
            EnemyKind.Bomber => Loc.T("폭발병"),
            EnemyKind.Healer => Loc.T("치유사"),
            EnemyKind.Charger => Loc.T("돌진병"),
            EnemyKind.Splitter => Loc.T("분열체"),
            EnemyKind.Assassin => Loc.T("도적"),
            _ => Loc.T("몬스터"),
        };

        /// <summary>폭발 범위 - 자기 칸 포함 주변 3x3 중 방 안이고 벽이 아닌 칸.</summary>
        public List<Vector2Int> BlastTiles()
        {
            var tiles = new List<Vector2Int>();
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                var p = GridPos + new Vector2Int(dx, dy);
                if (Map.IsInBounds(p) && !Map.IsWall(p))
                    tiles.Add(p);
            }
            return tiles;
        }

        /// <summary>attackerPos에서 때리면 방패 정면인지(방패병만).</summary>
        public bool IsShieldFront(Vector2Int attackerPos) => Kind == EnemyKind.Shield && attackerPos == GridPos + Facing;

        /// <summary>방패병 방향을 target 쪽으로 즉시 맞춘다(방에 처음 놓일 때).</summary>
        public void FaceToward(Vector2Int target)
        {
            if (Kind != EnemyKind.Shield)
                return;
            Facing = DirectionToward(target, Facing);
            ApplyFacingVisual();
        }

        /// <summary>target 쪽 상하좌우 방향 - 더 먼 축 기준, 같으면 지금 방향이 그중 하나면 유지.</summary>
        private Vector2Int DirectionToward(Vector2Int target, Vector2Int current)
        {
            var diff = target - GridPos;
            var horizontal = new Vector2Int(System.Math.Sign(diff.x), 0);
            var vertical = new Vector2Int(0, System.Math.Sign(diff.y));
            if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y))
                return horizontal;
            if (Mathf.Abs(diff.y) > Mathf.Abs(diff.x))
                return vertical;
            if (diff == Vector2Int.zero)
                return current;
            return current == vertical ? vertical : horizontal;
        }

        /// <summary>방패병 도트 그림이면 그 방향 그림으로(방패가 정면 쪽에 그려져 있다), 예전 삼각형이면 꼭짓점이
        /// 바라보는 방향을 가리키게 돌린다(삼각형은 위쪽을 가리키게 그려져 있음).</summary>
        private void ApplyFacingVisual()
        {
            if (TryGetComponent<SpriteAnimator>(out var anim))
            {
                anim.Face(Facing);
                return;
            }
            var angle = Mathf.Atan2(Facing.y, Facing.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private string BossDisplayName =>
            _bossStage <= 0 ? Loc.T("보스")
            : _morphStage > 0 ? $"{BossBrain.BossName(_bossStage)}({BossBrain.BossName(_morphStage)})"
            : Loc.F("{0}층 보스 · {1}", _bossStage, BossBrain.BossName(_bossStage));

        /// <summary>보스 전용 - 스테이지별 예고 공격 패턴을 붙이고 그 층 보스 그림(Resources/Sprites/Boss{층})을 입힌다.</summary>
        public void SetupBossPatterns(List<BossPatternType> patterns, int stage)
        {
            _brain = new BossBrain(this, patterns, stage);
            _bossStage = stage;
            ApplyBossSprite(stage);
            if (stage >= BossBrain.MirrorStage)
                Stats.OnDamaged += OnMirrorKnightDamaged;
        }

        /// <summary>보스 도트 그림(4방향 대기·걷기·공격)으로 - 그림이 없으면 예전 검붉은 사각형 그대로.</summary>
        private bool ApplyBossSprite(int stage)
        {
            var setName = $"Boss{stage}";
            var first = SpriteAnimator.FirstFrame(setName);
            if (first == null)
                return false;
            var renderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * BossSpriteScale, sortingOrder: 0);
            var anim = GetComponent<SpriteAnimator>();
            if (anim == null)
                anim = gameObject.AddComponent<SpriteAnimator>();
            anim.Setup(setName, renderer, BossDebrisColor(stage));
            anim.Face(Facing);
            ApplyBossTint(renderer);
            return true;
        }

        /// <summary>64x64 캔버스에 몸이 40px 남짓이라 1.6배 - 일반 몹보다 확실히 크게.</summary>
        private const float BossSpriteScale = 1.6f;

        private static Color BossDebrisColor(int stage) => stage switch
        {
            1 => new Color(0.5f, 0.33f, 0.2f),
            2 => new Color(0.48f, 0.6f, 0.33f),
            3 => new Color(0.5f, 0.5f, 0.55f),
            4 => new Color(0.84f, 0.23f, 0.3f),
            5 => new Color(0.66f, 0.24f, 0.59f),
            6 => new Color(0.4f, 0.9f, 0.85f),
            7 => new Color(0.23f, 0.19f, 0.32f),
            8 => new Color(0.56f, 0.28f, 0.18f),
            9 => new Color(0.4f, 1f, 0.6f),
            _ => new Color(0.75f, 0.8f, 0.9f),
        };

        /// <summary>거울 가면 기사가 비춘 모습은 은빛으로(진짜 그 층 보스와 구분), 광폭화하면 붉게.</summary>
        private void ApplyBossTint(SpriteRenderer renderer)
        {
            if (renderer == null)
                return;
            var sprite = GetComponent<SpriteAnimator>() != null;
            if (_enraged)
                renderer.color = sprite ? new Color(1f, 0.55f, 0.5f) : new Color(1f, 0.25f, 0.2f);
            else
                renderer.color = _morphStage > 0 ? new Color(0.78f, 0.86f, 1f) : Color.white;
        }

        /// <summary>10층 거울 가면 기사 전용 - 그 층 보스 모습으로 바뀐다(다음 변신까지 유지). 그림이 없으면 이름만 바뀐다.</summary>
        public void MorphInto(int stage)
        {
            if (stage == _morphStage)
                return;
            _morphStage = stage;
            ApplyBossSprite(stage);
            ActorJuice.Get(this).Flash(Color.white); // 변신하는 순간 번쩍
            HitFeedback.OnMorph(this);
            _mirrorDamage = 0f; // 새로 비춘 거울은 멀쩡하다
            RefreshMirrorCrack();
        }

        // ---- 거울 깨기(10층) - 다른 보스를 비추는 동안 최대 체력의 MirrorBreakRatio만큼 맞으면 거울이 깨져 본모습 + 기절 ----
        public const float MirrorBreakRatio = 0.08f;
        public const int MirrorBreakStunTurns = 2;
        private float _mirrorDamage;
        private SpriteRenderer _crackIcon;

        /// <summary>지금 비춘 거울이 얼마나 깨졌는지(0~1) - 본모습이면 0.</summary>
        public float MirrorCrack => _morphStage > 0 ? Mathf.Clamp01(_mirrorDamage / (Stats.MaxHealth * MirrorBreakRatio)) : 0f;

        private void OnMirrorKnightDamaged(float amount)
        {
            if (_morphStage == 0 || Stats.IsDead)
                return;
            _mirrorDamage += amount;
            if (_mirrorDamage < Stats.MaxHealth * MirrorBreakRatio)
            {
                RefreshMirrorCrack();
                return;
            }
            // 깨졌다 - 본모습으로 돌아오고, 예고해둔 패턴은 취소되고, 잠깐 기절(섬광탄과 같은 처리)
            var room = _turnRoom != null ? _turnRoom : FindAnyObjectByType<RoomController>();
            RevertForm();
            if (room != null)
                CancelBossPattern(room);
            Stun(MirrorBreakStunTurns);
            HitFeedback.OnMirrorBroken(this);
            room?.ShowMessage(Loc.F("거울이 깨졌다! {0}가 본모습으로 돌아와 {1}턴 동안 비틀거린다", BossBrain.BossName(BossBrain.MirrorStage), MirrorBreakStunTurns));
        }

        /// <summary>머리 위 거울 아이콘 - 금 간 정도(4단계). 본모습이거나 그림이 없거나 봇 중이면 숨긴다.</summary>
        private void RefreshMirrorCrack()
        {
            var frames = DamagePopup.Suppressed ? null : Fx.Frames("mirror_crack");
            var show = _morphStage > 0 && frames != null && frames.Length > 0 && !Stats.IsDead;
            if (!show)
            {
                if (_crackIcon != null)
                    _crackIcon.gameObject.SetActive(false);
                return;
            }
            if (_crackIcon == null)
            {
                var go = new GameObject("MirrorCrack");
                go.transform.SetParent(transform, false);
                _crackIcon = go.AddComponent<SpriteRenderer>();
                _crackIcon.sortingOrder = 9;
            }
            _crackIcon.gameObject.SetActive(true);
            var index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(MirrorCrack * (frames.Length - 1) + 0.0001f));
            _crackIcon.sprite = frames[index];
            // 부모(보스) 크기와 상관없이 0.45칸, 머리 위
            var parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            var size = GridConstants.CellSize * 0.45f / (frames[0].rect.width / frames[0].pixelsPerUnit);
            _crackIcon.transform.localScale = Vector3.one * size / parentScale;
            _crackIcon.transform.localPosition = new Vector3(0f, GridConstants.CellSize * 0.95f / parentScale, 0f);
        }

        /// <summary>거울 가면 기사가 자기 패턴(거울 파편)을 쓸 때 - 비추던 모습을 풀고 본모습으로. 이미 본모습이면 false.</summary>
        public bool RevertForm()
        {
            if (_morphStage == 0)
                return false;
            _morphStage = 0;
            ApplyBossSprite(_bossStage);
            ActorJuice.Get(this).Flash(Color.white);
            HitFeedback.OnMorph(this);
            _mirrorDamage = 0f;
            RefreshMirrorCrack();
            return true;
        }

        /// <summary>보스 광폭화 표시 - 붉게 물들인다(BossBrain이 HP 절반 이하가 되는 순간 한 번 부른다).</summary>
        public void MarkEnraged()
        {
            _enraged = true;
            ApplyBossTint(GetComponent<SpriteRenderer>());
        }

        /// <summary>방 이벤트 "미믹" - 상자 괴물 도트 그림(Resources/Sprites/Mimic, 4방향 대기·걷기·공격)으로. 그림이 없으면 갈색 사각형.</summary>
        public void MarkAsMimic()
        {
            IsMimic = true;
            var first = SpriteAnimator.FirstFrame(MimicSpriteSet);
            if (first == null)
            {
                var renderer = GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.color = new Color(0.6f, 0.4f, 0.2f);
                return;
            }
            var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * MimicSpriteScale, sortingOrder: 0);
            var anim = GetComponent<SpriteAnimator>();
            if (anim == null)
                anim = gameObject.AddComponent<SpriteAnimator>();
            anim.Setup(MimicSpriteSet, spriteRenderer, new Color(0.65f, 0.45f, 0.25f));
            anim.Face(Facing);
        }

        private const string MimicSpriteSet = "Mimic";
        private const float MimicSpriteScale = 1.25f;

        /// <summary>방 이벤트 "도전의 깃발" 정예 - 금빛으로 물들이고 조금 크게.</summary>
        public void MarkAsElite()
        {
            IsElite = true;
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = GetComponent<SpriteAnimator>() != null ? new Color(1f, 0.82f, 0.4f) : new Color(0.95f, 0.7f, 0.2f);
            transform.localScale *= 1.15f;
        }

        /// <summary>보스 소환 졸개로 표시 - 색을 어둡게 해서 일반 몹과 구분.</summary>
        public void MarkAsMinion()
        {
            IsMinion = true;
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null) // 그림(슬라임)은 곱해지는 색이라 덜 어둡게
                renderer.color = GetComponent<SpriteAnimator>() != null ? new Color(1f, 0.5f, 0.5f) : new Color(0.55f, 0.25f, 0.25f);
        }

        private const string SlimeSpriteSet = "Slime";
        private const string SpiderSpriteSet = "Spider";
        private const string ArcherSpriteSet = "SkeletonArcher";
        private const string ShieldSpriteSet = "ShieldSoldier";
        private const float ShieldSpriteScale = 1.35f;
        private const string BomberSpriteSet = "Bomber";
        private const float BomberSpriteScale = 1.3f;
        private const float NewEnemySpriteScale = 1.3f; // 새 몹 4종도 48x48 캔버스(폭발병과 같은 크기)
        private const float SplitChildScale = 0.7f;

        /// <summary>5층부터 나오는 새 몹 4종의 그림 세트(Resources/Sprites/{세트})와 처치 파편 색 - 그림이 없으면 같은 색 사각형.</summary>
        public static (string Set, Color Color) NewKindVisual(EnemyKind kind) => kind switch
        {
            EnemyKind.Healer => ("Shaman", new Color(0.35f, 0.9f, 0.55f)),
            EnemyKind.Charger => ("OrcCharger", new Color(0.8f, 0.4f, 0.25f)),
            EnemyKind.Splitter => ("Jelly", new Color(0.35f, 0.6f, 1f)),
            _ => ("Rogue", new Color(0.5f, 0.4f, 0.7f)),
        };

        /// <summary>분열체가 죽어 갈라진 조각으로 표시 - 작게, 더는 안 갈라지고 갓 나온 턴은 굳어 있다.</summary>
        public void MarkAsSplitChild()
        {
            IsSplitChild = true;
            _spawnFreezeTurns = 1;
            transform.localScale *= SplitChildScale;
        }

        public void Initialize(float maxHealth, float attackPower, bool isBoss, EnemyKind kind = EnemyKind.Melee)
        {
            IsBoss = isBoss;
            StatusIcons.AttachToEnemy(this); // 기절·약점 표식 표시
            Kind = isBoss ? EnemyKind.Melee : kind;
            Stats = new CharacterStats(maxHealth, attackPower);

            Color color;
            float scale;
            if (isBoss)
            {
                color = new Color(0.55f, 0.05f, 0.05f);
                scale = GridConstants.CellSize * 1.1f;
            }
            else if (Kind == EnemyKind.Ranged)
            {
                // 해골궁수 도트 그림(대기 4방향만 - 쏠 땐 플레이어 쪽을 바라봄) - 그림 파일이 없으면 예전 초록 사각형.
                var archer = SpriteAnimator.FirstFrame(ArcherSpriteSet);
                if (archer != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, archer, GridConstants.CellSize, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(ArcherSpriteSet, spriteRenderer, new Color(0.85f, 0.85f, 0.8f));
                    return;
                }
                color = new Color(0.35f, 0.8f, 0.35f); // 궁수는 초록색 - 한눈에 구분되게
                scale = GridConstants.CellSize * 0.5f;
            }
            else if (Kind == EnemyKind.Shield)
            {
                // 해골 방패병 도트 그림(4방향 대기·걷기 + 방패 밀치기 공격) - 그림 방향은 움직임이 아니라 방패 정면(Facing)을 따른다.
                var soldier = SpriteAnimator.FirstFrame(ShieldSpriteSet);
                if (soldier != null)
                {
                    // 48x48 캔버스에 몸이 작게 그려져 있어 다른 몹보다 크게(사용자 요청 "크기를 좀 더")
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, soldier, GridConstants.CellSize * ShieldSpriteScale, sortingOrder: 0);
                    var anim = gameObject.AddComponent<SpriteAnimator>();
                    anim.Setup(ShieldSpriteSet, spriteRenderer, new Color(0.55f, 0.65f, 0.85f));
                    anim.LockedToFacing = true;
                    ApplyFacingVisual();
                    return;
                }
                // 그림이 없으면 예전 삼각형(사용자 요청) - 꼭짓점이 방패 정면.
                VisualUtil.CreateTriangleVisual(gameObject, new Color(0.55f, 0.65f, 0.85f), GridConstants.CellSize * 0.75f, sortingOrder: 0);
                ApplyFacingVisual();
                return;
            }
            else if (Kind == EnemyKind.Spider)
            {
                // 거미 도트 그림(대기 4방향만 - 공격 모션은 없어서 대기 그대로) - 그림 파일이 없으면 예전 보라 사각형.
                var spider = SpriteAnimator.FirstFrame(SpiderSpriteSet);
                if (spider != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, spider, GridConstants.CellSize, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(SpiderSpriteSet, spriteRenderer, new Color(0.6f, 0.3f, 0.85f));
                    return;
                }
                color = new Color(0.6f, 0.3f, 0.85f); // 거미는 보라색
                scale = GridConstants.CellSize * 0.5f;
            }
            else if (Kind >= EnemyKind.Healer)
            {
                // 5층부터 나오는 새 몹 4종(고블린 주술사·오크 돌격병·분열 젤리·그림자 도적) - 대기·걷기·공격 4방향.
                var (set, kindColor) = NewKindVisual(Kind);
                var first = SpriteAnimator.FirstFrame(set);
                if (first != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * NewEnemySpriteScale, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(set, spriteRenderer, kindColor);
                    return;
                }
                color = kindColor;
                scale = GridConstants.CellSize * 0.55f;
            }
            else if (Kind == EnemyKind.Bomber)
            {
                // 고블린 폭탄병 도트 그림(대기·걷기 4방향 + 불붙음) - 그림 파일이 없으면 예전 주황 사각형.
                var bomber = SpriteAnimator.FirstFrame(BomberSpriteSet);
                if (bomber != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, bomber, GridConstants.CellSize * BomberSpriteScale, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(BomberSpriteSet, spriteRenderer, new Color(1f, 0.55f, 0.1f));
                    return;
                }
                color = new Color(1f, 0.55f, 0.1f); // 폭발병은 주황색(불 붙으면 노랗게)
                scale = GridConstants.CellSize * 0.55f;
            }
            else
            {
                // 기본 근접 몹 = 슬라임(사용자가 만든 도트 그림) - 그림 파일이 없으면 예전 빨간 사각형.
                var slime = SpriteAnimator.FirstFrame(SlimeSpriteSet);
                if (slime != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, slime, GridConstants.CellSize, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(SlimeSpriteSet, spriteRenderer, new Color(0.45f, 0.8f, 0.3f));
                    return;
                }
                color = new Color(0.8f, 0.2f, 0.2f);
                scale = GridConstants.CellSize * 0.6f;
            }
            VisualUtil.CreateSquareVisual(gameObject, color, scale, sortingOrder: 0);
        }

        public bool IsAdjacentTo(Vector2Int pos) => Distance(GridPos, pos) == 1;

        /// <param name="claimedSlots">이번 턴에 이미 다른 근접 몹이 예약한 "플레이어 옆 칸" - 한 칸에 두 마리가
        /// 몰리지 않게 RoomController가 한 턴 동안 공유해서 넘겨준다.</param>
        public void TakeTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (Stats.IsDead)
                return;
            _turnRoom = room;

            if (VulnerableTurns > 0)
                VulnerableTurns--;
            if (_spawnFreezeTurns > 0)
            {
                _spawnFreezeTurns--; // 갓 갈라진 분열 조각 - 한 턴 굳어 있음
                return;
            }
            if (StunTurns > 0)
            {
                StunTurns--; // 기절(섬광탄/연막탄/덫) - 아무것도 안 함
                if (MirrorShardsActive)
                    _brain.TakePatternTurn(player, room); // 단, 이미 깔린 거울 파편은 기절해도 순서대로 계속 터진다
                return;
            }

            // 미끼가 있으면 플레이어 대신 미끼를 노린다(보스 예고 패턴만은 플레이어 기준 그대로).
            _decoyTarget = room.ActiveDecoy;
            _tp = _decoyTarget != null ? _decoyTarget.GridPos : player.GridPos;

            // 보스는 예고/발동/소환 턴이면 그걸로 끝, 아니면 일반 근접 행동.
            if (_brain != null && _brain.TakePatternTurn(player, room))
                return;

            if (Kind == EnemyKind.Ranged)
                TakeRangedTurn(player);
            else if (Kind == EnemyKind.Shield)
                TakeShieldTurn(player, claimedSlots);
            else if (Kind == EnemyKind.Spider)
                TakeSpiderTurn(player, claimedSlots);
            else if (Kind == EnemyKind.Bomber)
                TakeBomberTurn(player, claimedSlots, room);
            else if (Kind == EnemyKind.Healer)
                TakeHealerTurn(player, room);
            else if (Kind == EnemyKind.Charger)
                TakeChargerTurn(player, claimedSlots, room);
            else if (Kind == EnemyKind.Assassin)
                TakeAssassinTurn(player, claimedSlots, room);
            else
                TakeMeleeTurn(player, claimedSlots); // 분열체도 걷는 건 근접 몹과 같다
        }

        /// <summary>봇 통계용 - 치유사 회복 횟수, 돌진 횟수와 그중 플레이어 적중, 도적 순간이동 횟수.</summary>
        public static int HealCount, ChargeCount, ChargeHitCount, BlinkCount;

        /// <summary>치유사: 1) 회복이 준비됐고 사거리 안에 다친 아군이 있으면 회복 2) 붙어 있으면 물러나기(못 물러나면 근접 공격)
        /// 3) 아니면 선호 거리 쪽으로 한 칸(두 턴에 한 번). 회복할 아군이 없으면 회복은 아껴둔다.</summary>
        private void TakeHealerTurn(PlayerActor player, RoomController room)
        {
            if (_healCooldown > 0)
                _healCooldown--;
            if (_retreatCooldown > 0)
                _retreatCooldown--;

            if (_healCooldown == 0)
            {
                EnemyActor best = null;
                var bestRate = HealThreshold;
                foreach (var e in room.Enemies)
                {
                    if (e == null || e == this || e.IsBoss || e.Stats.IsDead || Distance(GridPos, e.GridPos) > HealRange)
                        continue;
                    var rate = e.Stats.CurrentHealth / Mathf.Max(1f, e.Stats.MaxHealth);
                    if (rate < bestRate)
                    {
                        bestRate = rate;
                        best = e;
                    }
                }
                if (best != null)
                {
                    _healCooldown = HealCooldownTurns;
                    var amount = best.Stats.MaxHealth * HealRate;
                    best.Stats.Heal(amount);
                    HealCount++;
                    if (TryGetComponent<SpriteAnimator>(out var anim))
                        anim.PlayAttack(best.GridPos - GridPos);
                    DamagePopup.Spawn(best.transform.position, amount, new Color(0.4f, 1f, 0.5f));
                    Fx.Play("heal", best.transform.position, 1.1f, fps: 14f, sortingOrder: 8);
                    return;
                }
            }

            if (Distance(GridPos, _tp) == 1)
            {
                if (!TryMoveToSpot(mustIncreaseDistance: true, HealerPreferredDistance, requireLine: false))
                    HitTarget(player);
                return;
            }
            TryMoveToSpot(mustIncreaseDistance: false, HealerPreferredDistance, requireLine: false);
        }

        /// <summary>체력 비율이 이보다 낮은 아군만 회복한다(거의 멀쩡한 몹에 회복을 낭비하지 않게).</summary>
        private const float HealThreshold = 0.9f;

        /// <summary>돌진병: 예고해둔 돌진이 있으면 달린다. 돌진 직후엔 숨 고르기. 아니면 같은 줄에서 돌진을 예고하거나, 근접 몹처럼 다가간다.</summary>
        private void TakeChargerTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (_chargeDir.HasValue)
            {
                ResolveCharge(player, room);
                return;
            }
            if (_chargeCooldown > 0)
            {
                _chargeCooldown--;
                if (_chargeCooldown >= ChargeCooldownTurns - ChargeRecoverTurns)
                    return; // 돌진 직후 숨 고르기
            }

            if (_chargeCooldown == 0 && !IsAdjacentTo(_tp) && CanShoot(_tp, ChargeRange))
            {
                var dir = new Vector2Int(System.Math.Sign(_tp.x - GridPos.x), System.Math.Sign(_tp.y - GridPos.y));
                var tiles = new List<Vector2Int>();
                for (var p = GridPos + dir; tiles.Count < ChargeRange && Map.IsInBounds(p) && !Map.IsWall(p); p += dir)
                    tiles.Add(p);
                _chargeDir = dir;
                _chargeTiles = tiles;
                if (TryGetComponent<SpriteAnimator>(out var anim))
                    anim.Face(dir);
                room.ShowEnemyTelegraph(this, tiles, RoomController.ChargeTelegraphColor);
                HitFeedback.OnTelegraph(); // 보스 예고와 같은 경고음
                return;
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>예고한 줄을 따라 달린다 - 플레이어(또는 미끼)를 만나면 들이받고 그 앞에서, 다른 몹/벽이면 그 앞에서 멈춘다.</summary>
        private void ResolveCharge(PlayerActor player, RoomController room)
        {
            var dir = _chargeDir.Value;
            var tiles = _chargeTiles;
            _chargeDir = null;
            _chargeTiles = null;
            _chargeCooldown = ChargeCooldownTurns;
            room.ClearEnemyTelegraph(this);
            ChargeCount++;

            var stop = GridPos;
            GridActor hit = null;
            foreach (var p in tiles)
            {
                var actor = Map.GetActorAt(p);
                if (actor != null)
                {
                    hit = actor;
                    break;
                }
                if (!Map.IsWalkable(p))
                    break;
                stop = p;
            }
            if (stop != GridPos)
                Map.MoveActor(this, stop);

            if (TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(dir);
            if (hit != null && hit == _decoyTarget)
            {
                _decoyTarget.TakeHit(this, Stats.AttackPower * ChargeDamageRate);
                return;
            }
            if (!(hit is PlayerActor))
                return;
            ChargeHitCount++;
            var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower * ChargeDamageRate);
            room.ApplyThorns(this, dealt);
            DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f), isCritical: true);
            HitFeedback.OnPlayerHurt(player, this);
        }

        /// <summary>도적: 붙어 있으면 공격(순간이동 직후 첫 공격은 기습). 떨어져 있고 순간이동이 준비됐으면 플레이어 등 뒤로. 아니면 다가간다.</summary>
        private void TakeAssassinTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (_blinkCooldown > 0)
                _blinkCooldown--;

            var dist = Distance(GridPos, _tp);
            if (dist == 1)
            {
                HitTarget(player, melee: true, rate: _backstabReady ? BackstabRate : 1f);
                _backstabReady = false;
                return;
            }

            if (_blinkCooldown == 0 && dist >= BlinkMinDistance && dist <= BlinkMaxDistance)
            {
                Vector2Int? best = null;
                var bestScore = int.MinValue;
                foreach (var d in Directions)
                {
                    var c = _tp + d;
                    if (!Map.IsWalkable(c) || claimedSlots.Contains(c) || room.IsDangerTile(c))
                        continue;
                    var score = Distance(c, GridPos);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                if (best.HasValue)
                {
                    var from = transform.position;
                    claimedSlots.Add(best.Value);
                    Map.MoveActor(this, best.Value);
                    if (TryGetComponent<SpriteAnimator>(out var anim))
                    {
                        anim.SnapToGrid(); // 걸어가는 모션 대신 순간이동
                        anim.Face(_tp - GridPos);
                    }
                    _blinkCooldown = BlinkCooldownTurns;
                    _backstabReady = true;
                    BlinkCount++;
                    var smoke = new Color(0.6f, 0.5f, 0.8f);
                    Fx.Play("death_smoke", from, 1f, tint: smoke);
                    Fx.Play("death_smoke", transform.position, 1f, tint: smoke);
                    return;
                }
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>폭발병: 불이 붙어 있으면 터진다(그 자리에서 - 플레이어가 도망갔어도). 붙어 있으면 불을 붙인다(공격 안 함).
        /// 아니면 근접 몹처럼 다가간다.</summary>
        private void TakeBomberTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (_fuseLit)
            {
                room.ResolveExplosion(this, Stats.AttackPower * BlastDamageRate);
                return;
            }

            if (IsAdjacentTo(_tp))
            {
                _fuseLit = true;
                // 그림이면 플레이어 쪽을 보며 부들부들 + 폭탄이 달아오르는 그림으로, 아니면 예전처럼 노랗게
                if (TryGetComponent<SpriteAnimator>(out var anim) && anim.SetLit(true))
                    anim.Face(_tp - GridPos);
                else
                {
                    var renderer = GetComponent<SpriteRenderer>();
                    if (renderer != null)
                        renderer.color = new Color(1f, 0.95f, 0.3f);
                }
                room.ShowBombTelegraph(this, BlastTiles());
                HitFeedback.OnFuseLit(this);
                return;
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>방패병: 플레이어와 붙어 있는 동안은 방향을 못 돌리고 그냥 때린다 - 옆/뒤로 돌아 붙으면 계속 옆을 때릴 수 있다
        /// (매 턴 돌 수 있게 했다면 플레이어가 옆 칸에 도착하자마자 돌아서 영원히 정면만 때리게 된다).
        /// 떨어져 있을 땐 플레이어 쪽을 안 보고 있고 돌 수 있으면(두 턴에 한 번) 90도 돈다(그 턴은 끝). 아니면 근접 몹처럼 다가간다.</summary>
        private void TakeShieldTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots)
        {
            var canTurn = _shieldTurnCooldown == 0;
            if (_shieldTurnCooldown > 0)
                _shieldTurnCooldown--;

            var desired = DirectionToward(_tp, Facing);
            if (canTurn && desired != Facing && !IsAdjacentTo(_tp))
            {
                // 반대편이면 90도만(시계 방향) - 한 번에 뒤돌지 못한다.
                Facing = desired == -Facing ? new Vector2Int(Facing.y, -Facing.x) : desired;
                _shieldTurnCooldown = ShieldTurnCooldown;
                ApplyFacingVisual();
                return;
            }

            if (IsAdjacentTo(_tp))
            {
                HitTarget(player); // 공격은 매 턴
                return;
            }

            // 이동은 두 턴에 한 번(무거운 방패) - 매 턴 따라오면 플레이어가 옆으로 비킬 때마다 방향은 그대로 둔 채
            // 옆으로 따라붙어서 다시 정면이 됐다(봇 로그: 비켜섰다 돌아오기를 반복하다 뒤의 궁수에게 맞아 죽음).
            if (_shieldMoveCooldown > 0)
            {
                _shieldMoveCooldown--;
                return;
            }
            _shieldMoveCooldown = ShieldMoveCooldown;
            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>거미: 붙어 있으면 근접 공격, 같은 줄 2~4칸이고 거미줄이 준비됐으면 거미줄, 아니면 근접 몹처럼 다가간다.</summary>
        private void TakeSpiderTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots)
        {
            if (_webCooldown > 0)
                _webCooldown--;

            if (!IsAdjacentTo(_tp) && _webCooldown == 0 && CanShoot(_tp, WebRange))
            {
                _webCooldown = WebCooldownTurns;
                if (TryGetComponent<SpriteAnimator>(out var anim)) // 거미줄 쏜 쪽을 바라보게
                    anim.PlayAttack(_tp - GridPos);
                if (!Fx.Projectile("web_shot", transform.position, TargetWorld(player), 0.45f, 0.15f))
                    SpawnLineVisual(TargetWorld(player), new Color(0.95f, 0.95f, 1f, 0.9f), 0.12f, 0.25f);
                if (_decoyTarget != null)
                {
                    _decoyTarget.TakeHit(this, Stats.AttackPower * WebDamageRate);
                    return;
                }
                var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower * WebDamageRate);
                DamagePopup.Spawn(player.transform.position, dealt, new Color(0.85f, 0.7f, 1f));
                player.ApplyRoot(1);
                HitFeedback.OnPlayerWebbed(player);
                return;
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>근접 포위: 붙어 있으면 공격. 아니면 플레이어 상하좌우 중 비어 있고 아직 예약 안 된 칸들 가운데
        /// BFS로 제일 가까운 칸을 예약하고 그 경로의 첫 걸음을 간다 - 여러 마리가 한 줄로 따라오지 않고 옆/뒤로
        /// 돌아 들어온다. 갈 칸이 없으면(다 찼거나 막힘) 플레이어 쪽으로 그리디하게 한 칸.</summary>
        private void TakeMeleeTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots)
        {
            if (IsAdjacentTo(_tp))
            {
                HitTarget(player);
                return;
            }

            var slots = SlotBuffer;
            slots.Clear();
            foreach (var d in Directions)
            {
                var slot = _tp + d;
                if (Map.IsWalkable(slot) && !claimedSlots.Contains(slot))
                    slots.Add(slot);
            }

            if (slots.Count > 0 && TryFindPathToAny(slots, out var reachedSlot, out var firstStep))
            {
                claimedSlots.Add(reachedSlot);
                Map.MoveActor(this, firstStep);
                return;
            }

            StepGreedilyToward(_tp);
        }

        /// <summary>현재 칸에서 빈 칸만 밟는 BFS로 targets 중 가장 가까운 칸을 찾는다. 찾으면 그 칸과 첫 걸음을 돌려준다.</summary>
        // BFS 버퍼 재사용 - 몹마다 매 턴 새로 만들던 걸(봇 기준 초당 수만 번) 비우고 다시 쓴다. 턴은 한 번에 한 몹씩이라 공유해도 안전.
        private static readonly HashSet<Vector2Int> SlotBuffer = new HashSet<Vector2Int>();
        private static readonly Dictionary<Vector2Int, Vector2Int> FirstStepBuffer = new Dictionary<Vector2Int, Vector2Int>();
        private static readonly Queue<Vector2Int> QueueBuffer = new Queue<Vector2Int>();

        private bool TryFindPathToAny(HashSet<Vector2Int> targets, out Vector2Int reached, out Vector2Int firstStep)
        {
            reached = default;
            firstStep = default;

            var first = FirstStepBuffer;
            var queue = QueueBuffer;
            first.Clear();
            queue.Clear();
            foreach (var d in Directions)
            {
                var next = GridPos + d;
                if (!Map.IsWalkable(next))
                    continue;
                first[next] = next;
                queue.Enqueue(next);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (targets.Contains(cur))
                {
                    reached = cur;
                    firstStep = first[cur];
                    return true;
                }

                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next == GridPos || first.ContainsKey(next) || !Map.IsWalkable(next))
                        continue;
                    first[next] = first[cur];
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        /// <summary>예전 방식 - 더 멀리 떨어진 축으로 한 칸, 막히면 다른 축으로 한 칸, 그것도 막히면 대기.</summary>
        private void StepGreedilyToward(Vector2Int target)
        {
            var diff = target - GridPos;
            var primary = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                ? new Vector2Int(System.Math.Sign(diff.x), 0)
                : new Vector2Int(0, System.Math.Sign(diff.y));
            var secondary = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                ? new Vector2Int(0, System.Math.Sign(diff.y))
                : new Vector2Int(System.Math.Sign(diff.x), 0);

            foreach (var step in new[] { primary, secondary })
            {
                if (step == Vector2Int.zero)
                    continue;
                var next = GridPos + step;
                if (Map.IsWalkable(next))
                {
                    Map.MoveActor(this, next);
                    return;
                }
            }
        }

        /// <summary>궁수: 1) 같은 줄 사거리 안 + 사이가 비었으면 사격 2) 바로 옆이면 물러나기(못 물러나면 근접 공격)
        /// 3) 그 외엔 "줄이 맞고 선호 거리에 가까운" 칸으로 한 칸 이동. 이동(물러나기 포함)은 RetreatCooldownTurns마다 한 번만.</summary>
        private void TakeRangedTurn(PlayerActor player)
        {
            var diff = _tp - GridPos;
            var dist = Mathf.Abs(diff.x) + Mathf.Abs(diff.y);
            if (_retreatCooldown > 0)
                _retreatCooldown--;

            if (dist == 1)
            {
                // 물러날 수 있으면(쿨타임 끝) 한 칸 물러나고, 아니면 그 자리에서 근접 공격.
                if (!TryMoveToSpot(mustIncreaseDistance: true, RangedPreferredDistance, requireLine: true))
                    HitTarget(player);
                return;
            }

            if (CanShoot(_tp, RangedAttackRange))
            {
                SpawnArrowVisual(TargetWorld(player));
                HitTarget(player, melee: false);
                return;
            }

            TryMoveToSpot(mustIncreaseDistance: false, RangedPreferredDistance, requireLine: true);
        }

        private bool CanShoot(Vector2Int target, int range)
        {
            var diff = target - GridPos;
            if (diff.x != 0 && diff.y != 0)
                return false;

            var dist = Mathf.Abs(diff.x) + Mathf.Abs(diff.y);
            if (dist < 2 || dist > range)
                return false;

            // 화살은 아군(다른 몹)은 통과하고 플레이어만 맞히지만, 벽에는 막힌다.
            var step = new Vector2Int(System.Math.Sign(diff.x), System.Math.Sign(diff.y));
            for (var p = GridPos + step; p != target; p += step)
            {
                if (Map.IsWall(p))
                    return false;
            }
            return true;
        }

        /// <summary>이웃 빈 칸 중 점수가 제일 좋은 곳으로 한 칸 - 지금 자리보다 나을 때만 움직인다.
        /// 점수: 줄 어긋남(작을수록 좋음, 궁수만)이 최우선, 플레이어에게 붙는 칸(거리 1)은 금지, 그다음 선호 거리와의 차이.
        /// 궁수·치유사 공통(둘 다 두 턴에 한 번만 이동).</summary>
        private bool TryMoveToSpot(bool mustIncreaseDistance, int preferredDistance, bool requireLine)
        {
            var currentDist = Distance(GridPos, _tp);
            if (_retreatCooldown > 0)
                return false; // 두 턴에 한 번만 이동

            var bestScore = mustIncreaseDistance ? int.MaxValue : SpotScore(GridPos, _tp, preferredDistance, requireLine);
            Vector2Int? best = null;

            foreach (var d in Directions)
            {
                var p = GridPos + d;
                if (!Map.IsWalkable(p))
                    continue;
                var newDist = Distance(p, _tp);
                if (mustIncreaseDistance && newDist <= currentDist)
                    continue;

                var score = SpotScore(p, _tp, preferredDistance, requireLine);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            if (!best.HasValue)
                return false;

            _retreatCooldown = RetreatCooldownTurns; // 이동했으면 다음 턴은 못 움직임
            Map.MoveActor(this, best.Value);
            return true;
        }

        private static int SpotScore(Vector2Int pos, Vector2Int playerPos, int preferredDistance, bool requireLine)
        {
            var dx = Mathf.Abs(playerPos.x - pos.x);
            var dy = Mathf.Abs(playerPos.y - pos.y);
            var dist = dx + dy;
            var tooClose = dist <= 1 ? 1000 : 0;
            if (!requireLine)
                return tooClose + Mathf.Abs(dist - preferredDistance);
            var misalign = Mathf.Min(dx, dy);
            var outOfRange = Mathf.Max(dx, dy) > RangedAttackRange ? 5 : 0;
            return tooClose + misalign * 10 + outOfRange + Mathf.Abs(dist - preferredDistance);
        }

        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private Vector3 TargetWorld(PlayerActor player) => _decoyTarget != null ? _decoyTarget.transform.position : player.transform.position;

        /// <summary>노리는 대상을 때린다 - 미끼면 미끼가 맞고, 아니면 플레이어.</summary>
        private RoomController _turnRoom;

        /// <param name="rate">공격력 배율(도적 기습 1.5배 등)</param>
        private void HitTarget(PlayerActor player, bool melee = true, float rate = 1f)
        {
            if (TryGetComponent<SpriteAnimator>(out var anim)) // 때린 쪽을 바라보고 공격 모션(그림에 공격 프레임이 있을 때만)
                anim.PlayAttack((_decoyTarget != null ? _decoyTarget.GridPos : player.GridPos) - GridPos);
            if (_decoyTarget != null)
            {
                _decoyTarget.TakeHit(this, Stats.AttackPower * rate);
                return;
            }
            HitPlayer(player, melee, rate);
        }

        private void HitPlayer(PlayerActor player, bool melee, float rate)
        {
            var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower * rate);
            if (melee)
                _turnRoom?.ApplyThorns(this, dealt); // 갑옷 "가시"
            DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f), isCritical: rate > 1f);
            HitFeedback.OnPlayerHurt(player, this);
        }

        /// <summary>궁수 → 플레이어로 화살이 날아간다(연출만 - 피해는 바로 들어감). 화살 그림이 없으면 가는 노란 선.</summary>
        private void SpawnArrowVisual(Vector3 targetWorld)
        {
            if (!Fx.Projectile("arrow", transform.position, targetWorld, 0.55f, 0.1f))
                SpawnLineVisual(targetWorld, new Color(1f, 0.9f, 0.3f, 0.9f), 0.08f, 0.15f);
        }

        /// <summary>이 몹 → targetWorld로 가는 선을 잠깐 보여준다(화살/거미줄 - 투사체 이동은 없음).</summary>
        private void SpawnLineVisual(Vector3 targetWorld, Color color, float thickness, float lifetime)
        {
            if (DamagePopup.Suppressed)
                return; // 봇 초고속 실행 중엔 생략

            var from = transform.position;
            var go = new GameObject("Line");
            var renderer = VisualUtil.CreateSquareVisual(go, color, 1f, sortingOrder: 5);
            var delta = targetWorld - from;
            go.transform.position = (from + targetWorld) * 0.5f + new Vector3(0f, 0f, -0.5f);
            go.transform.localScale = new Vector3(Mathf.Max(0.05f, delta.magnitude), thickness, 1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            renderer.color = color;
            Destroy(go, lifetime);
        }
    }
}
