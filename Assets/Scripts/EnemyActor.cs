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
        Sentinel, // 반격병(10-26) - 두 턴에 한 번 반격 자세, 그때 때리면 막고 강하게 되받아친다
        Caster,   // 주문술사(10-26) - 플레이어를 본 순간부터 8턴 주문, 0이 되면 플레이어 둘레 3x3 폭발
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
        public bool NoReward => IsMinion || IsSplitChild || IsPuzzleMob || IsGlitchRevived || IsTutorialMob;

        /// <summary>튜토리얼 스테이지 몹 - 보상 없음.</summary>
        public bool IsTutorialMob { get; private set; }
        public void MarkAsTutorialMob() => IsTutorialMob = true;

        /// <summary>오류방에서 한 번 되살아난 몹 - 보상 없음, 다시는 안 살아남, 보랏빛.</summary>
        public bool IsGlitchRevived { get; private set; }

        public void MarkGlitchRevived()
        {
            IsGlitchRevived = true;
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = new Color(0.8f, 0.55f, 1f);
        }

        /// <summary>이번 턴에 노리는 칸(플레이어 또는 미끼)과 미끼.</summary>
        private Vector2Int _tp;
        private DecoyActor _decoyTarget;

        /// <summary>남은 기절 턴(섬광탄/연막탄/덫) - 그동안 아무 행동도 안 한다.</summary>
        public int StunTurns { get; private set; }
        public void Stun(int turns) => StunTurns = Mathf.Max(StunTurns, turns);
        /// <summary>10층 발악 시작 - 남은 기절을 없앤다(발악은 바로 시작).</summary>
        public void ClearStun() => StunTurns = 0;

        /// <summary>덫에 걸려 기절 - 유물 "사냥꾼의 올가미"가 그 기절 동안 피해를 늘린다.</summary>
        public void Snare(int turns)
        {
            Stun(turns);
            _snareTurns = Mathf.Max(_snareTurns, turns);
        }

        private int _snareTurns; // 덫 기절 남은 턴 - 섬광탄·파동의 핵 기절은 올가미 대상이 아니다
        public bool IsSnared => _snareTurns > 0 && StunTurns > 0;
        /// <summary>섬광탄·덫 같은 몹 기절 + 보스 패턴 기절(돌진 뒤 비틀거림, 기둥·용암 충돌) - 머리 위 기절 별 표시용.</summary>
        public bool IsStunned => StunTurns > 0 || (_brain != null && _brain.IsStunned);

        /// <summary>연막 속 - 플레이어가 공격할 때까지(PlayerActor.BreakSmoke) 플레이어를 못 찾아 아무것도 안 한다.</summary>
        public bool InSmoke { get; private set; }
        public void SetInSmoke(bool value) => InSmoke = value;

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

        public float DamageTakenMultiplier => IsBladeProof ? 0f : (IsVulnerable ? ItemInfo.WeaknessDamageMultiplier : 1f)
                                             * (_brain != null ? _brain.ArmorMultiplier(_turnRoom) : 1f); // 1층 멧돼지 기둥 - 단단함

        /// <summary>섬광탄 - 보스가 예고해둔 공격을 취소한다.</summary>
        public void CancelBossPattern(RoomController room) => _brain?.CancelPending(room);
        public void RefreshHallucinatedTelegraph(RoomController room) => _brain?.RefreshHallucinatedTelegraph(room);
        public bool MirrorShardsActive => _brain != null && _brain.MirrorShardsActive;

        /// <summary>방패병이 바라보는 방향(상하좌우). 다른 몹은 의미 없음.</summary>
        public Vector2Int Facing { get; private set; } = Vector2Int.left;

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public bool IsBoss { get; private set; }

        /// <summary>보스는 3x3 칸을 차지한다(사용자 결정) - GridPos가 가운데 칸. 어느 칸을 때려도 맞고, 몸 전체가 들어갈 자리로만 움직인다.</summary>
        public const int BossFootprintRadius = 1;
        public override int FootprintRadius => IsBoss || IsMirrorFake ? BossFootprintRadius : 0;
        /// <summary>4층 슬라임 킹이 분열한 뒤 - 2x2(GridPos가 왼쪽 아래 칸).</summary>
        private bool _slimeSmall;
        public override int FootprintMin => _slimeSmall ? 0 : -FootprintRadius;
        public override int FootprintMax => _slimeSmall ? 1 : FootprintRadius;
        public bool IsSlimeSmall => _slimeSmall;

        /// <summary>4층 분열 - 3x3 몸 안의 2x2(왼쪽 아래)로 줄어든다. 그림도 작게.</summary>
        public void ShrinkToSlimeHalf()
        {
            if (_slimeSmall || Map == null)
                return;
            Map.RemoveActor(this);
            _slimeSmall = true;
            Map.PlaceActor(this, GridPos + new Vector2Int(-1, -1) + new Vector2Int(UnityEngine.Random.Range(0, 2), UnityEngine.Random.Range(0, 2)));
            if (TryGetComponent<SpriteAnimator>(out var anim))
                anim.SnapToGrid();
            ApplyBossSprite(_bossStage);
            ActorJuice.Get(this).RebaseScale();
            SyncTransform();
        }

        /// <summary>4층 분열체(벽 포탑) - 방 둘레 장식 벽 위를 타고 다니며 점액 파동만 쏜다(RoomController.SlimeTurretTurn). 칼이 안 통하고,
        /// 지도 칸을 차지하지 않는다(10-25 사용자 - 예전엔 방 안 벽가 2x2를 점유). 그림만 킹을 2x2 크기로.</summary>
        public bool IsSlimeTurret { get; private set; }
        /// <summary>벽 포탑 그림 위치 - 벽 위 두 칸 사이 가운데(RoomController.SeatSlimeTurret이 정한다). 없으면 평소대로.</summary>
        public Vector3? WallVisualOffset { get; set; }
        public override Vector3 VisualOffset => WallVisualOffset ?? FootprintVisualOffset;

        public void MarkAsSlimeTurret(EnemyActor king)
        {
            IsSlimeTurret = true;
            var first = SpriteAnimator.FirstFrame("Boss4");
            if (first == null)
                return;
            var ownAnim = GetComponent<SpriteAnimator>();
            if (ownAnim != null)
                Destroy(ownAnim);
            var renderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * BossSpriteScale(4) * 2.1f / BossBodyCells, sortingOrder: 0);
            gameObject.AddComponent<SpriteAnimator>().Setup("Boss4", renderer, BossDebrisColor(4));
        }
        public bool IsMinion { get; private set; } // 보스가 소환한 졸개 - 골드/경험치 없음, 보스가 죽으면 같이 사라짐
        /// <summary>7층 그림자 분신 - 칼이 안 통하고(받는 피해 0), 암살자의 X자 베기에만 사라진다(RoomController.OnDiagonalTiles).</summary>
        public bool IsShadowClone { get; private set; }

        /// <summary>10층 거울 가면 기사의 가짜(거울상) - 진짜와 똑같은 3x3 몸·그림인데 좌우만 뒤집혀 있다. 칼이 안 통하고, 진짜의 예고 공격에
        /// 맞으면 깨진다(RoomController.OnMirrorPatternTiles). 10-15 사용자 "진짜 찾기".</summary>
        public bool IsMirrorFake { get; private set; }
        private EnemyActor _mirrorOf;

        /// <summary>칼(일반 공격·스킬·폭탄)이 안 통하는 몹 - 7층 그림자 분신, 10층 거울상.</summary>
        public bool IsBladeProof => IsShadowClone || IsMirrorFake || IsSlimeTurret;

        public void MarkAsMirrorFake(EnemyActor original)
        {
            IsMirrorFake = true;
            _mirrorOf = original;
            var source = original.GetComponent<SpriteRenderer>();
            if (source == null)
                return;
            if (TryGetComponent<SpriteAnimator>(out var ownAnim))
                Destroy(ownAnim); // 일반 몹 그림 대신 진짜 그림을 베낀다(MirrorImage)
            var renderer = VisualUtil.CreateSpriteVisual(gameObject, source.sprite, original.transform.localScale.x, sortingOrder: 0);
            renderer.flipX = true;
            gameObject.AddComponent<MirrorImage>().Setup(this, original, renderer);
        }

        public void MarkAsShadowClone()
        {
            IsShadowClone = true;
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = new Color(0.35f, 0.25f, 0.5f, 0.7f); // 어두운 반투명 그림자
        }
        public EnemyKind Kind { get; private set; }

        private BossBrain _brain;

        private int _bossStage;
        public int BossStage => _bossStage; // 층 보스면 그 층(도감 키), 아니면 0
        private int _morphStage; // 거울 가면 기사가 지금 비추고 있는 보스 층(0 = 본모습)
        private bool _enraged;

        public bool IsMimic { get; private set; } // 방 이벤트 "미믹" - 잡으면 큰 보상(RoomController)
        public bool IsElite { get; private set; } // 방 이벤트 "도전의 깃발" 정예
        public bool IsKnight { get; private set; } // 히든 몹 "기억하는 기사"(MemoryKnight)

        /// <summary>표시 이름 - 오류방에선 깨진 글자(몹마다 늘 같은 모양).</summary>
        public string DisplayName => GlitchRoom.IsActive && !IsBoss ? GlitchRoom.Corrupt(PlainName, GetInstanceID()) : PlainName;

        private string PlainName => IsMirrorFake && _mirrorOf != null ? _mirrorOf.PlainName : IsBoss ? BossDisplayName : IsMinion ? Loc.T("졸개") : IsMimic ? Loc.T("미믹") : IsKnight ? MemoryKnight.Name : IsSplitChild ? Loc.T("분열 조각") : (IsElite ? Loc.T("정예 ") : "") + Kind switch
        {
            EnemyKind.Ranged => Loc.T("궁수"),
            EnemyKind.Shield => Loc.T("방패병"),
            EnemyKind.Spider => Loc.T("거미"),
            EnemyKind.Bomber => Loc.T("폭발병"),
            EnemyKind.Healer => Loc.T("치유사"),
            EnemyKind.Charger => Loc.T("돌진병"),
            EnemyKind.Splitter => Loc.T("분열체"),
            EnemyKind.Assassin => Loc.T("도적"),
            EnemyKind.Sentinel => Loc.T("반격병"),
            EnemyKind.Caster => Loc.T("주문술사"),
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
        /// <summary>보스 러시 - 입장하자마자 기믹 시작(체력은 이미 기믹 체력으로 깎아 둠).</summary>
        public void StartGimmickNow(RoomController room)
        {
            _turnRoom = room; // 보스 러시 - 한 턴도 안 움직였어도 무적·단단함 배율(DamageTakenMultiplier)이 방을 알게
            _brain?.TryStartGimmick(room);
        }

        public void SetupBossPatterns(List<BossPatternType> patterns, int stage)
        {
            _brain = new BossBrain(this, patterns, stage);
            _bossStage = stage;
            ApplyBossSprite(stage);
            if (stage >= BossBrain.MirrorStage)
            {
                Stats.OnDamaged += OnMirrorKnightDamaged;
                Stats.HealthFloor = 1f; // 발악(0% 기믹)을 버텨야 쓰러진다
                if (isActiveAndEnabled)
                    StartCoroutine(PreloadMirrorForms());
            }
        }

        /// <summary>보스 도트 그림(4방향 대기·걷기·공격)으로 - 그림이 없으면 예전 검붉은 사각형 그대로.</summary>
        private bool ApplyBossSprite(int stage)
        {
            var setName = $"Boss{stage}";
            var first = SpriteAnimator.FirstFrame(setName);
            if (first == null)
                return false;
            var renderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * BossSpriteScale(stage) * (_slimeSmall ? 2.3f / BossBodyCells : 1f), sortingOrder: 0);
            var anim = GetComponent<SpriteAnimator>();
            if (anim == null)
                anim = gameObject.AddComponent<SpriteAnimator>();
            anim.Setup(setName, renderer, BossDebrisColor(stage));
            anim.AttackDurationScale = 2f; // 보스 공격 모션은 2배 길게 - 일반 공격이 잘 보이게
            anim.Face(Facing);
            ApplyBossTint(renderer);
            return true;
        }

        /// <summary>보스 그림 배율 - 정면 대기 그림의 몸 긴 변(BossBodyPixels, 64x64 캔버스 기준)이 3x3 판정보다 살짝 큰 BossBodyCells칸이 되게.
        /// 위아래 위치는 그림 meta의 피벗을 몸통 중심으로 맞춰 둬서(boss_fit.py) 몸이 판정 칸 한가운데에 선다.
        /// (예전엔 모든 보스에 같은 배율 + 캔버스 가운데 피벗이라 몸이 판정보다 한 칸쯤 아래로 처지고 보스마다 크기가 2.5~3.9칸으로 제각각이었다.)</summary>
        private const float BossBodyCells = 3.2f;
        private static readonly int[] BossBodyPixels = { 36, 48, 46, 41, 46, 53, 45, 54, 57, 55 }; // 1~10층

        private static float BossSpriteScale(int stage) =>
            BossBodyCells * 64f / BossBodyPixels[Mathf.Clamp(stage, 1, BossBodyPixels.Length) - 1];

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
            SwapForm(stage);
            _mirrorDamage = 0f; // 새로 비춘 거울은 멀쩡하다
            RefreshMirrorCrack();
        }

        /// <summary>10층 기사가 비출 보스 그림(1~9층, 보스마다 96~125장)과 변신·패턴 이펙트를 한 프레임에 한 묶음씩 미리 읽는다.
        /// 예전엔 처음 보는 모습으로 변신하는 순간 그 그림을 한꺼번에 읽느라 화면이 멈췄다(10-19 사용자 "변신할 때 렉").</summary>
        private System.Collections.IEnumerator PreloadMirrorForms()
        {
            yield return null; // 방이 다 깔린 다음 프레임부터
            for (var s = 1; s < BossBrain.MirrorStage; s++)
            {
                SpriteAnimator.Preload($"Boss{s}");
                yield return null;
            }
            foreach (var fx in MirrorPreloadFx)
            {
                Fx.Frames(fx);
                yield return null;
            }
        }

        private static readonly string[] MirrorPreloadFx =
        {
            "knight_morph", "morph", "mirror_crack", "crystal_shatter", "boss_slam", "boss_charge", "boss_cross", "boss_ring",
            "boss_snipe", "boss_diag", "slime_splash", "axe_wave", "lich_wave", "soul_orb",
        };

        // ---- 10층 변신 연출(10-19 사용자) - 거울 조각이 모여 거울판이 서는 사이 기사가 가로로 접혔다가 새 모습으로 펴진다(Effects/knight_morph).
        // 봇·연출 꺼짐이면 바로 바꾼다. 판정·패턴은 그 자리에서 이미 바뀌고 그림만 0.2초쯤 늦게 따라온다.
        // 10-19 사용자 "렉 걸리는 느낌" - 0.08 + 0.17×2(약 0.42초) → 0.03 + 0.08×2(약 0.19초), 그림도 20 → 40fps
        private const float MorphFoldDelay = 0.03f; // 조각이 모여드는 사이
        private const float MorphFoldHalf = 0.08f;  // 접히는 시간 = 펴지는 시간
        private Coroutine _morphRoutine;

        private void SwapForm(int stage)
        {
            if (_morphRoutine != null)
            {
                StopCoroutine(_morphRoutine);
                _morphRoutine = null;
            }
            if (!HitFeedback.Enabled || !isActiveAndEnabled || GetComponent<SpriteAnimator>() == null)
            {
                ApplyBossSprite(stage);
                ActorJuice.Get(this).RebaseScale();
                ActorJuice.Get(this).Flash(Color.white); // 변신하는 순간 번쩍
                HitFeedback.OnMorph(this);
                return;
            }
            _morphRoutine = StartCoroutine(MirrorFold(stage));
        }

        private System.Collections.IEnumerator MirrorFold(int stage)
        {
            HitFeedback.OnMorph(this);
            var juice = ActorJuice.Get(this);
            juice.SetWindup(false);
            var from = transform.localScale;
            yield return new WaitForSeconds(MorphFoldDelay);
            for (var t = 0f; t < MorphFoldHalf; t += Time.deltaTime)
            {
                transform.localScale = new Vector3(from.x * (1f - t / MorphFoldHalf), from.y, from.z);
                yield return null;
            }
            ApplyBossSprite(stage);
            var to = transform.localScale;
            for (var t = 0f; t < MorphFoldHalf; t += Time.deltaTime)
            {
                transform.localScale = new Vector3(to.x * (t / MorphFoldHalf), to.y, to.z);
                yield return null;
            }
            transform.localScale = to;
            juice.RebaseScale();
            juice.Flash(Color.white);
            _morphRoutine = null;
        }

        // ---- 거울 깨기(10층) - 다른 보스를 비추는 동안 최대 체력의 MirrorBreakRatio만큼 맞으면 거울이 깨져 본모습 + 기절 ----
        public const float MirrorBreakRatio = 0.08f;
        public const int MirrorBreakStunTurns = 2;
        /// <summary>거울 방어막 지름(칸) - 보스 몸(BossBodyCells 3.2칸)을 넉넉히 감싼다.</summary>
        private const float MirrorShieldCells = 4f;
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
        }

        /// <summary>몸을 감싼 거울 방어막 - 금 간 정도(4단계). 본모습이거나 그림이 없거나 봇 중이면 숨긴다.</summary>
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
            // 몸 전체를 감싸는 방어막(10-23 사용자, 예전엔 머리 위 0.45칸 아이콘) - 부모(보스) 크기와 상관없이 MirrorShieldCells칸, 몸 가운데(그림 피벗 = 몸통 중심)
            var parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            var size = GridConstants.CellSize * MirrorShieldCells / (frames[0].rect.width / frames[0].pixelsPerUnit);
            _crackIcon.transform.localScale = Vector3.one * size / parentScale;
            _crackIcon.transform.localPosition = Vector3.zero;
        }

        /// <summary>거울 가면 기사가 자기 패턴(거울 파편)을 쓸 때 - 비추던 모습을 풀고 본모습으로. 이미 본모습이면 false.</summary>
        public bool RevertForm()
        {
            if (_morphStage == 0)
                return false;
            _morphStage = 0;
            SwapForm(_bossStage);
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

        public const string MimicSpriteSet = "Mimic";

        /// <summary>히든 몹 "기억하는 기사" - 녹슨 기사 도트 그림(Resources/Sprites/MemoryKnight, 4방향 대기·걷기·공격). 그림이 없으면 청록 사각형.</summary>
        /// <param name="patternPower">강타·기억 재현의 기준 공격력(보스 공격력 × MemoryKnight.BossPatternRate) - 일반 공격은 Stats.AttackPower(약함).</param>
        public void MarkAsKnight(float patternPower)
        {
            IsKnight = true;
            _knightPatternPower = patternPower;
            _knightSlashCooldown = 1; // 붙자마자 강타하지 않게
            var first = SpriteAnimator.FirstFrame(MemoryKnight.SpriteSet);
            if (first == null)
            {
                var renderer = GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.color = MemoryKnight.TintColor;
                return;
            }
            var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, first, GridConstants.CellSize * MemoryKnight.SpriteScale, sortingOrder: 0);
            var anim = GetComponent<SpriteAnimator>();
            if (anim == null)
                anim = gameObject.AddComponent<SpriteAnimator>();
            anim.Setup(MemoryKnight.SpriteSet, spriteRenderer, MemoryKnight.TintColor);
            anim.Face(Facing);
        }
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
        private const float BasicEnemySpriteScale = 1.3f; // 슬라임·거미·해골 궁수도 48x48 캔버스(10-14 새로 그림, 새 몹 4종과 같은 크기)
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
            EnemyKind.Sentinel => ("Sentinel", new Color(0.45f, 0.5f, 0.6f)), // 강철 갑옷 + 해 문양 방패(sentinel2.py, lit_ = 반격 자세)
            EnemyKind.Caster => ("Chanter", new Color(0.5f, 0.25f, 0.55f)),    // 자줏빛 두건 로브 + 떠 있는 마도서(chanter2.py)
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
                scale = GridConstants.CellSize * (BossFootprintRadius * 2 + 1) * 0.95f; // 차지한 3x3 칸만큼
            }
            else if (Kind == EnemyKind.Ranged)
            {
                // 해골 궁수 도트 그림(대기·걷기·활 쏘기 4방향) - 그림 파일이 없으면 예전 초록 사각형.
                var archer = SpriteAnimator.FirstFrame(ArcherSpriteSet);
                if (archer != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, archer, GridConstants.CellSize * BasicEnemySpriteScale, sortingOrder: 0);
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
                // 거미 도트 그림(대기·걷기·물기 4방향, 거미줄 쏠 때도 물기 모션) - 그림 파일이 없으면 예전 보라 사각형.
                var spider = SpriteAnimator.FirstFrame(SpiderSpriteSet);
                if (spider != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, spider, GridConstants.CellSize * BasicEnemySpriteScale, sortingOrder: 0);
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
                // 기본 근접 몹 = 슬라임(대기·깡충 걷기·박치기 4방향) - 그림 파일이 없으면 예전 빨간 사각형.
                var slime = SpriteAnimator.FirstFrame(SlimeSpriteSet);
                if (slime != null)
                {
                    var spriteRenderer = VisualUtil.CreateSpriteVisual(gameObject, slime, GridConstants.CellSize * BasicEnemySpriteScale, sortingOrder: 0);
                    gameObject.AddComponent<SpriteAnimator>().Setup(SlimeSpriteSet, spriteRenderer, new Color(0.45f, 0.8f, 0.3f));
                    return;
                }
                color = new Color(0.8f, 0.2f, 0.2f);
                scale = GridConstants.CellSize * 0.6f;
            }
            VisualUtil.CreateSquareVisual(gameObject, color, scale, sortingOrder: 0);
        }

        /// <summary>pos가 몸에 상하좌우로 붙은 칸인지(3x3 보스면 몸 둘레 칸, 대각선 모서리는 아님).</summary>
        public bool IsAdjacentTo(Vector2Int pos) => GridMap.FootprintDistance(this, pos) == 1;

        /// <summary>보스가 다음 턴에 붙어 있는 대상을 일반 공격할 수 있는 상태인지 - 준비 동작 표시용. TakeMeleeTurn의 "안 때리는" 조건과 맞춘다.</summary>
        public bool BossMeleeReadyNextTurn(RoomController room) =>
            IsBoss && !Stats.IsDead && _brain != null && _brain.NextTurnIsNormal && StunTurns == 0 && !InSmoke
            && !(room != null && room.BossInvulnerable);

        /// <param name="claimedSlots">이번 턴에 이미 다른 근접 몹이 예약한 "플레이어 옆 칸" - 한 칸에 두 마리가
        /// 몰리지 않게 RoomController가 한 턴 동안 공유해서 넘겨준다.</param>
        public void TakeTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (Stats.IsDead)
                return;
            _turnRoom = room;
            // 반격 자세는 한 몹 턴만 - 기절·연막으로 이번 턴을 못 움직여도 자세는 풀린다(봇 100판: 연막 속에서 자세를 영영 안 풀어
            // 봇이 끝없이 기다렸다)
            _guardedLastTurn = IsGuarding;
            if (IsGuarding)
                SetGuard(false);

            if (VulnerableTurns > 0)
                VulnerableTurns--;
            if (_spawnFreezeTurns > 0)
            {
                _spawnFreezeTurns--; // 갓 갈라진 분열 조각 - 한 턴 굳어 있음
                return;
            }
            if ((InSmoke || (player != null && player.InSmoke)) && !(IsBoss && room != null && room.MirrorLastStand))
                return; // 연막 속 - 플레이어가 공격하기 전까지 아무것도 안 함(약점 표식 턴은 위에서 그대로 줄어든다). 연막 뒤에 나온 몹도.
                        // 10층 발악 중인 기사에겐 안 통한다(10-19 사용자 - 연막으로 발악을 공짜로 넘기던 구멍)
            if (StunTurns > 0 && IsBoss && room != null && room.MirrorLastStand)
                StunTurns = 0; // 10층 발악 중인 기사는 기절도 안 통한다(섬광탄·덫·파동의 핵으로 발악 턴을 공짜로 넘기던 구멍 - 연막과 같은 이유)
            if (StunTurns > 0)
            {
                StunTurns--; // 기절(섬광탄/덫) - 아무것도 안 함
                if (_snareTurns > 0)
                    _snareTurns--;
                if (MirrorShardsActive)
                    _brain.TakePatternTurn(player, room); // 단, 이미 깔린 거울 파편은 기절해도 순서대로 계속 터진다
                return;
            }

            if (IsSlimeTurret)
            {
                room.SlimeTurretTurn(this, player); // 4층 벽 포탑 - 둘레 벽을 타고 따라다니며 점액 파동만
                return;
            }

            // 미끼가 있으면 플레이어 대신 미끼를 노린다(보스 예고 패턴만은 플레이어 기준 그대로).
            _decoyTarget = room.ActiveDecoy;
            _tp = _decoyTarget != null ? _decoyTarget.GridPos : player.GridPos;
            if (IsMinion && room.MinionTarget(this) is Vector2Int king)
                _tp = king; // 4층 흡수 단계 - 졸개는 킹에게 기어간다

            // 보스는 예고/발동/소환 턴이면 그걸로 끝, 아니면 일반 근접 행동.
            if (_brain != null && _brain.TakePatternTurn(player, room))
                return;

            if (IsKnight)
            {
                TakeKnightTurn(player, claimedSlots, room);
                return;
            }
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
            else if (Kind == EnemyKind.Sentinel)
                TakeSentinelTurn(claimedSlots, player);
            else if (Kind == EnemyKind.Caster)
                TakeCasterTurn(player, room);
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
            PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} 돌진", dealt, player.Stats);
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

        // ---- 반격병(Sentinel, 10-26 사용자) - 스킬 쿨타임을 기다리며 일반 공격만 누르는 걸 막는다 ----
        public const float SentinelGuardDamageRate = 0.1f; // 반격 자세일 때 받는 피해
        public const float SentinelCounterRate = 2f;       // 반격 = 공격력 × 이 값
        private const int SentinelGuardRange = 2;          // 이 거리 안에 들어와야 자세를 잡는다(멀면 그냥 다가온다)

        /// <summary>반격 자세 - 몹 턴에 잡고 다음 몹 턴에 푼다. 그 사이(플레이어 턴)에 때리면 막고 되받아친다.</summary>
        public bool IsGuarding { get; private set; }

        /// <summary>반격 자세 그림(lit_ - 방패를 세우고 금빛)이 있어 몸으로 보여 주는지 - 없으면 StatusIcons가 방패 빛을 대신 띄운다.</summary>
        public bool ShowsGuardPose { get; private set; }

        private void SetGuard(bool on)
        {
            IsGuarding = on;
            ShowsGuardPose = TryGetComponent<SpriteAnimator>(out var anim) && anim.SetLit(on);
        }

        /// <summary>봇 통계용 - 반격 횟수, 주문 폭발 횟수와 그중 플레이어 적중.</summary>
        public static int SentinelCounterCount, CasterBlastCount, CasterBlastHitCount;

        /// <summary>반격병: 자세를 잡고 있었으면 풀고 평소처럼 다가가 때린다. 아니면 가까울 때 자세를 잡고 그 턴은 끝(두 턴에 한 번).</summary>
        private bool _guardedLastTurn;

        private void TakeSentinelTurn(HashSet<Vector2Int> claimedSlots, PlayerActor player)
        {
            if (_guardedLastTurn)
            {
                TakeMeleeTurn(player, claimedSlots); // 자세를 푼 턴 - 다가와 때린다(자세는 TakeTurn 맨 앞에서 풀림)
                return;
            }
            if (Distance(GridPos, _tp) <= SentinelGuardRange)
            {
                if (TryGetComponent<SpriteAnimator>(out var anim))
                    anim.Face(_tp - GridPos);
                SetGuard(true);
                return;
            }
            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>반격 자세에 맞았다(PlayerActor.StrikeEnemy) - 공격한 플레이어를 강하게 되받아친다. 거리와 상관없이(대시로 와서 때려도).</summary>
        public void CounterAttack(PlayerActor player, RoomController room)
        {
            if (Stats.IsDead || player == null || player.Stats.IsDead)
                return;
            _turnRoom = room;
            SentinelCounterCount++;
            DamagePopup.SpawnText(transform.position, Loc.T("반격!"), new Color(1f, 0.8f, 0.35f));
            if (TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(player.GridPos - GridPos);
            HitPlayer(player, melee: true, rate: SentinelCounterRate);
        }

        // ---- 주문술사(Caster, 10-26 사용자) - "3턴 안에 잡을 것인가, 막아낼 것인가" ----
        public const int CasterChantTurns = 8;   // 플레이어를 본 순간부터 이만큼 몹 턴 뒤 폭발(사용자 8턴)
        /// <summary>폭발 피해 = 플레이어 최대 체력 × 이 값 - 즉사급(10-26 사용자). 받는 피해 감소(최대 30%)로도 못 버티고,
        /// 보호막 아이템·반사 부적·대시로 피하기·불굴 같은 "막는 수단"으로만 산다.</summary>
        public const float CasterBlastMaxHealthRate = 1.5f;
        private int _chant = -1;                 // 남은 주문 턴 - -1이면 아직 플레이어를 못 봤다

        /// <summary>봇용 - 주문을 외우는 중인지(플레이어를 본 뒤), 폭발 예고 칸이 이 칸을 덮는지.</summary>
        public bool IsChanting => Kind == EnemyKind.Caster && _chant >= 0 && !Stats.IsDead;
        public bool CasterBlastCovers(Vector2Int pos) => _casterBlastTiles != null && _casterBlastTiles.Contains(pos);
        private List<Vector2Int> _casterBlastTiles;
        private TextMesh _chantLabel;
        private ChantCircle _chantCircle;

        /// <summary>주문술사: 플레이어가 시야(플레이어 기준 시야 반경 + 벽에 안 가림)에 들어온 순간 주문을 시작해 그 자리에서 외운다.
        /// 남은 턴이 1이 되면 그때 플레이어 둘레 3x3을 예고하고, 0이 되면 그 칸에 폭발. 그 뒤 다시 처음부터 외운다.</summary>
        private void TakeCasterTurn(PlayerActor player, RoomController room)
        {
            if (_chant < 0)
            {
                if (!CasterSeesPlayer(room, player))
                    return;
                _chant = CasterChantTurns;
                UpdateChantLabel();
                return;
            }
            _chant--;
            if (_chant == 1)
            {
                _casterBlastTiles = CasterBlastArea(room.Map, player.GridPos);
                room.ShowEnemyTelegraph(this, _casterBlastTiles, new Color(0.85f, 0.25f, 0.8f, 0.55f));
                HitFeedback.OnTelegraph();
                LaunchFireball(player.GridPos);
            }
            else if (_chant <= 0)
            {
                ResolveCasterBlast(player, room);
                _chant = CasterChantTurns;
            }
            if (TryGetComponent<SpriteAnimator>(out var anim))
                anim.Face(player.GridPos - GridPos);
            UpdateChantLabel();
        }

        private bool CasterSeesPlayer(RoomController room, PlayerActor player)
        {
            var d = player.GridPos - GridPos;
            var radius = Relics.VisionRadius;
            return d.x * d.x + d.y * d.y <= radius * radius && FogOfWar.HasLineOfSight(room.Map, player.GridPos, GridPos);
        }

        private static List<Vector2Int> CasterBlastArea(GridMap map, Vector2Int center)
        {
            var tiles = new List<Vector2Int>();
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                var c = center + new Vector2Int(dx, dy);
                if (map.IsInBounds(c) && !map.IsWall(c))
                    tiles.Add(c);
            }
            return tiles;
        }

        private void ResolveCasterBlast(PlayerActor player, RoomController room)
        {
            var tiles = _casterBlastTiles ?? CasterBlastArea(room.Map, player.GridPos);
            _casterBlastTiles = null;
            room.ClearEnemyTelegraph(this);
            CasterBlastCount++;
            if (TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(player.GridPos - GridPos);
            if (HitFeedback.Enabled)
            {
                // 떠 있던 불덩이가 예고 칸 한가운데로 내리꽂히며 자홍빛 폭발(떠 있던 불덩이는 _casterBlastTiles가 비면서 KeepAlive로 사라진다)
                var center = _fireballTarget;
                Fx.Play("chant_fireball", center + Vector3.up * GridConstants.CellSize * 0.6f, 1.6f, angle: -90f, fps: 20f);
                Fx.Play("chant_circle", center, 3.6f, tint: new Color(1f, 0.45f, 0.95f), fps: 14f, sortingOrder: -2); // 바닥에 큰 마법진이 번쩍
                Fx.Play("chant_blast", center + Vector3.up * GridConstants.CellSize * 0.3f, 3.8f, fps: 14f, sortingOrder: 8);
                DeathBurst.Spawn(center, new Color(0.95f, 0.35f, 0.85f), count: 24, speed: 9f, size: 0.25f);
                DeathBurst.Spawn(center, new Color(1f, 0.9f, 0.6f), count: 12, speed: 12f, size: 0.12f);
                ScreenFlash.Play(new Color(0.95f, 0.35f, 0.9f), 0.35f, 0.45f);
                CameraShake.Shake(0.5f, 0.55f);
                SfxPlayer.Play(Sfx.Explosion);
            }
            if (!tiles.Contains(player.GridPos))
                return;
            CasterBlastHitCount++;
            if (player.ReflectInto(this, room))
                return; // 반사 부적 - 막고 되돌린다
            var dealt = player.Stats.TakeIncomingDamage(player.Stats.MaxHealth * CasterBlastMaxHealthRate);
            PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} 주문 폭발", dealt, player.Stats);
            DamagePopup.Spawn(player.transform.position, dealt, new Color(0.95f, 0.4f, 0.85f), isCritical: true);
            HitFeedback.OnPlayerHurt(player, this);
        }

        private Vector3 _fireballTarget;
        private const float FireballFlightSeconds = 0.45f;

        /// <summary>날아가는 불덩이 뒤로 작은 불꽃이 남는다 - 불덩이와 같은 길을 따라 짧게 타고 사라지는 작은 불덩이.</summary>
        private System.Collections.IEnumerator FireballTrail(Vector3 from, Vector3 to, float seconds)
        {
            for (var t = 0f; t < seconds; t += 0.04f)
            {
                var p = Vector3.Lerp(from, to, t / seconds);
                var delta = to - from;
                Fx.Play("chant_fireball", p, 0.6f, angle: Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, fps: 30f, sortingOrder: 5,
                    tint: new Color(1f, 0.6f, 0.95f, 0.7f));
                yield return new WaitForSeconds(0.04f);
            }
        }

        /// <summary>마법진이 완성되는 턴(남은 1) - 마법진이 번쩍이고 불덩이가 날아가 예고 칸 위에 떠서 이글거린다(10-26 사용자 "플레이어 쪽으로 불덩이").
        /// 폭발 턴에 날리면 그 한 방에 죽을 때 사망 연출(슬로모션 → 정지)에 불덩이가 공중에 멈춘 채 찍혀서, 한 턴 먼저 날려 띄워 둔다.</summary>
        private void LaunchFireball(Vector2Int target)
        {
            _fireballTarget = new Vector3(target.x * GridConstants.CellSize, target.y * GridConstants.CellSize, 0f);
            if (!HitFeedback.Enabled)
                return;
            _chantCircle?.Flash();
            var from = _chantCircle != null ? _chantCircle.transform.position : transform.position + Vector3.up * GridConstants.CellSize;
            var hover = _fireballTarget + Vector3.up * GridConstants.CellSize * 1.4f;
            SfxPlayer.Play(Sfx.Slam);
            DeathBurst.Spawn(from, new Color(1f, 0.55f, 0.95f), count: 16, speed: 6f, size: 0.14f); // 완성된 마법진에서 룬 파편이 흩날림
            StartCoroutine(FireballTrail(from, hover, FireballFlightSeconds));
            Fx.Projectile("chant_fireball", from, hover, 1.3f, FireballFlightSeconds, () =>
            {
                if (_casterBlastTiles == null || Stats.IsDead)
                    return; // 그새 주문술사가 쓰러졌다
                var fx = Fx.Play("chant_fireball", hover, 1.3f, angle: -90f, fps: 12f, loop: true, sortingOrder: 7);
                if (fx != null)
                    fx.KeepAlive = () => this != null && _casterBlastTiles != null && !Stats.IsDead;
            });
        }

        /// <summary>머리 위 남은 주문 턴 숫자(3 이하면 빨갛게) - 봇 실행 중엔 안 만든다.</summary>
        private void UpdateChantLabel()
        {
            if (DamagePopup.Suppressed)
                return;
            if (_chantLabel == null)
            {
                var go = new GameObject("ChantLabel");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, GridConstants.CellSize * 1.5f, -1f); // 머리 위 마법진보다 위
                _chantLabel = go.AddComponent<TextMesh>();
                _chantLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _chantLabel.fontSize = 48;
                _chantLabel.characterSize = 0.08f;
                _chantLabel.anchor = TextAnchor.LowerCenter;
                _chantLabel.alignment = TextAlignment.Center;
                _chantLabel.fontStyle = FontStyle.Bold;
                var meshRenderer = go.GetComponent<MeshRenderer>();
                meshRenderer.material = _chantLabel.font.material;
                meshRenderer.sortingOrder = 30;
            }
            _chantLabel.text = _chant.ToString();
            _chantLabel.color = _chant <= 3 ? new Color(1f, 0.3f, 0.3f) : new Color(0.95f, 0.75f, 1f);

            // 머리 위 마법진(10-26 사용자 "머리 위에서") - 인식한 턴(8)엔 작고 희미하게, 턴마다 커지고 또렷해지다 마지막 2턴은 붉게 맥박
            if (_chantCircle == null)
            {
                var go = new GameObject("ChantCircle");
                go.transform.SetParent(transform, false);
                _chantCircle = go.AddComponent<ChantCircle>();
            }
            var progress = 1f - (_chant - 1) / (float)(CasterChantTurns - 1); // 8 → 0, 1 → 1
            _chantCircle.SetTarget(Mathf.Lerp(0.5f, 1.5f, progress), Mathf.Lerp(0.15f, 0.95f, progress), danger: _chant <= 2);
        }

        /// <summary>주문술사 머리 위 마법진(Effects/chant_circle) - 크기·투명도를 목표값으로 부드럽게 옮기며 천천히 돈다. 위험(마지막 2턴)이면 붉게 맥박.</summary>
        private sealed class ChantCircle : MonoBehaviour
        {
            private static readonly Color Purple = new Color(0.92f, 0.43f, 0.9f);
            private static readonly Color Red = new Color(1f, 0.27f, 0.27f);
            private SpriteRenderer _renderer;
            private Sprite[] _frames;
            private float _size = 0.5f, _alpha, _targetSize = 0.5f, _targetAlpha;
            private bool _danger;

            /// <summary>마법진 완성 - 하얗게 번쩍이며 한껏 부풀었다가 목표 크기로 돌아온다.</summary>
            public void Flash() => _flash = 1f;

            private float _flash;

            public void SetTarget(float sizeCells, float alpha, bool danger)
            {
                _targetSize = sizeCells;
                _targetAlpha = alpha;
                _danger = danger;
            }

            private void Awake()
            {
                _frames = Fx.Frames("chant_circle");
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sortingOrder = 6; // 몸 위(머리 위 후광처럼), 숫자(30) 아래
                transform.localPosition = new Vector3(0f, GridConstants.CellSize * 0.95f, -0.5f);
                if (_frames.Length == 0)
                    enabled = false;
            }

            private void Update()
            {
                var dt = Time.deltaTime;
                _size = Mathf.MoveTowards(_size, _targetSize, dt * 2f);
                _alpha = Mathf.MoveTowards(_alpha, _targetAlpha, dt * 1.5f);
                _renderer.sprite = _frames[(int)(Time.time * 8f) % _frames.Length];
                var pulse = _danger ? 0.8f + 0.2f * Mathf.Sin(Time.time * 9f) : 1f;
                var c = Color.Lerp(_danger ? Red : Purple, Color.white, _flash);
                c.a = Mathf.Max(_alpha * pulse, _flash);
                _renderer.color = c;
                var world = GridConstants.CellSize * _size * (1f + 0.35f * _flash) / Mathf.Max(0.01f, _renderer.sprite.bounds.size.x);
                _flash = Mathf.Max(0f, _flash - dt * 2.5f);
                var parent = transform.parent != null ? transform.parent.lossyScale.x : 1f;
                transform.localScale = Vector3.one * world / Mathf.Max(0.01f, parent);
            }
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
                PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} 거미줄", dealt, player.Stats);
                DamagePopup.Spawn(player.transform.position, dealt, new Color(0.85f, 0.7f, 1f));
                player.ApplyRoot(1);
                HitFeedback.OnPlayerWebbed(player);
                return;
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        // ---- 기억하는 기사(MemoryKnight) ----
        private AttackStyle _knightReadStyle;
        private int _knightReadStreak = -1;
        private bool _knightReadSpoken; // 간파 대사는 한 싸움에 한 번만(매번 말하면 메시지 창이 기사 말로 덮인다)
        private int _knightSlashCooldown;
        private List<Vector2Int> _knightSlashTiles;
        private bool _knightRetreated;
        private int _knightRetreatTurns;
        private float _knightPatternPower;

        /// <summary>기사의 강타·기억 재현이 플레이어에게 한 번이라도 맞았는지 - 히든 업적 "완벽한 간파".</summary>
        public bool KnightPatternLanded { get; private set; }
        private int _knightEchoCooldown = MemoryKnight.EchoFirstCooldown;
        private AttackStyle _knightEchoStyle;
        private List<Vector2Int> _knightEchoTiles;
        private Vector2Int _knightEchoDir;
        private static readonly System.Random KnightRng = new System.Random();

        /// <summary>봇 통계용 - 기사가 간파한 횟수, 강타 예고 횟수와 그중 플레이어 적중.</summary>
        public static int KnightReadCount, KnightSlashCount, KnightSlashHitCount, KnightEchoCount, KnightEchoHitCount;

        /// <summary>기사의 간파 - 플레이어가 style로 때릴 때 부른다. 피했으면 true(피해 없음). 같은 공격을 연달아 맞을수록 잘 피한다.</summary>
        public bool TryReadAttack(AttackStyle style, RoomController room)
        {
            if (!IsKnight || style == AttackStyle.Other || StunTurns > 0 || InSmoke)
                return false;
            _knightReadStreak = style == _knightReadStyle ? _knightReadStreak + 1 : 0;
            _knightReadStyle = style;
            var chance = MemoryKnight.ReadChance[Mathf.Clamp(_knightReadStreak, 0, MemoryKnight.ReadChance.Length - 1)];
            if (KnightRng.NextDouble() >= chance)
                return false;
            KnightReadCount++;
            DamagePopup.SpawnText(transform.position, Loc.T("간파!"), new Color(0.5f, 0.95f, 0.9f));
            if (!_knightReadSpoken)
            {
                _knightReadSpoken = true;
                room.ShowMessage(MemoryKnight.Say(MemoryKnight.ReadLine(style)));
            }
            return true;
        }

        /// <summary>기사: 1) 예고한 강타가 있으면 벤다 2) 숨 고르는 중이면 물러나며 회복 3) 체력이 처음 낮아지면 숨 고르기 시작
        /// 4) 붙어 있고 강타가 준비됐으면 예고 5) 아니면 근접 몹처럼 다가가 때린다.</summary>
        private void TakeKnightTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots, RoomController room)
        {
            if (_knightEchoTiles != null)
            {
                ResolveKnightEcho(player, room);
                return;
            }
            if (_knightSlashTiles != null)
            {
                ResolveKnightSlash(player, room);
                return;
            }
            if (_knightSlashCooldown > 0)
                _knightSlashCooldown--;
            if (_knightEchoCooldown > 0)
                _knightEchoCooldown--;

            if (!_knightRetreated && Stats.CurrentHealth < Stats.MaxHealth * MemoryKnight.RetreatThreshold)
            {
                _knightRetreated = true;
                _knightRetreatTurns = MemoryKnight.RetreatTurns;
                room.ShowMessage(MemoryKnight.Say(MemoryKnight.RetreatLine()));
            }
            if (_knightRetreatTurns > 0)
            {
                _knightRetreatTurns--;
                _retreatCooldown = 0; // 궁수와 같은 이동 도우미를 쓰지만 기사는 매 턴 움직인다
                TryMoveToSpot(mustIncreaseDistance: true, preferredDistance: 6, requireLine: false);
                _retreatCooldown = 0;
                Stats.Heal(Stats.MaxHealth * MemoryKnight.RetreatHealRate);
                DamagePopup.SpawnText(transform.position, Loc.T("숨 고르기"), new Color(0.5f, 0.95f, 0.6f));
                return;
            }

            if (_knightEchoCooldown == 0 && TryStartKnightEcho(player, room))
                return;

            if (_knightSlashCooldown == 0 && IsAdjacentTo(_tp))
            {
                var dir = _tp - GridPos;
                var side = new Vector2Int(dir.y, dir.x);
                var tiles = new List<Vector2Int>();
                foreach (var p in new[] { _tp, _tp + side, _tp - side })
                    if (Map.IsInBounds(p) && !Map.IsWall(p))
                        tiles.Add(p);
                _knightSlashTiles = tiles;
                _knightSlashCooldown = MemoryKnight.SlashCooldownTurns;
                KnightSlashCount++;
                if (TryGetComponent<SpriteAnimator>(out var anim))
                    anim.Face(dir);
                room.ShowEnemyTelegraph(this, tiles, RoomController.ChargeTelegraphColor);
                HitFeedback.OnTelegraph();
                WarnKnightPattern(Loc.T("강타!"), new Color(1f, 0.4f, 0.35f));
                if (KnightSlashCount % 3 == 1)
                    room.ShowMessage(MemoryKnight.Say(MemoryKnight.SlashLine()));
                room.ShowMessage(Loc.T("<color=#FF7A70>기사의 강타!</color>"));
                return;
            }

            TakeMeleeTurn(player, claimedSlots);
        }

        /// <summary>예고 패턴 경고 - 기사 머리 위에 큰 글자가 떠오르고 화면 가장자리가 그 색으로 살짝 번쩍인다(봇 중엔 둘 다 생략).</summary>
        private void WarnKnightPattern(string text, Color color)
        {
            DamagePopup.SpawnText(transform.position + Vector3.up * GridConstants.CellSize * 0.35f, text, color, big: true);
            ScreenFlash.Play(color, 0.12f, 0.35f);
        }

        /// <summary>기억 재현 - 플레이어가 마지막으로 쓴 스킬을 따라 할 수 있으면 예고한다. 회전 베기: 플레이어가 주변 8칸 안.
        /// 대시: 같은 줄 EchoDashRange칸 안(벽에 안 막힘). 예고했으면 true.</summary>
        private bool TryStartKnightEcho(PlayerActor player, RoomController room)
        {
            var style = player.LastSkill;
            var tiles = new List<Vector2Int>();
            if (style == AttackStyle.Spin)
            {
                var d = _tp - GridPos;
                if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) != 1)
                    return false;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    var p = GridPos + new Vector2Int(dx, dy);
                    if ((dx != 0 || dy != 0) && Map.IsInBounds(p) && !Map.IsWall(p))
                        tiles.Add(p);
                }
            }
            else if (style == AttackStyle.Dash)
            {
                if (!CanShoot(_tp, MemoryKnight.EchoDashRange))
                    return false;
                var dir = new Vector2Int(System.Math.Sign(_tp.x - GridPos.x), System.Math.Sign(_tp.y - GridPos.y));
                for (var p = GridPos + dir; tiles.Count < MemoryKnight.EchoDashRange && Map.IsInBounds(p) && !Map.IsWall(p); p += dir)
                    tiles.Add(p);
                _knightEchoDir = dir;
                if (TryGetComponent<SpriteAnimator>(out var anim))
                    anim.Face(dir);
            }
            else
                return false;

            _knightEchoStyle = style;
            _knightEchoTiles = tiles;
            _knightEchoCooldown = MemoryKnight.EchoCooldownTurns;
            KnightEchoCount++;
            room.ShowEnemyTelegraph(this, tiles, MemoryKnight.EchoTelegraphColor);
            HitFeedback.OnTelegraph();
            WarnKnightPattern(Loc.T("기억 재현!"), new Color(0.45f, 0.95f, 0.88f));
            room.ShowMessage(MemoryKnight.Say(MemoryKnight.EchoLine(style)));
            room.ShowMessage(style == AttackStyle.Spin
                ? Loc.T("<color=#7FE0D8>기억 재현 - 회전 베기!</color>")
                : Loc.T("<color=#7FE0D8>기억 재현 - 돌진!</color>"));
            return true;
        }

        /// <summary>예고한 기억 재현을 쓴다 - 회전 베기는 주변 칸 전부, 대시는 줄을 따라 달려 처음 만난 플레이어(미끼)를 들이받고 그 앞에서 멈춘다.</summary>
        private void ResolveKnightEcho(PlayerActor player, RoomController room)
        {
            var tiles = _knightEchoTiles;
            var style = _knightEchoStyle;
            _knightEchoTiles = null;
            room.ClearEnemyTelegraph(this);
            var damage = _knightPatternPower * MemoryKnight.EchoDamageRate;

            GridActor hit = null;
            if (style == AttackStyle.Dash)
            {
                var stop = GridPos;
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
                    anim.PlayAttack(_knightEchoDir);
            }
            else
            {
                if (TryGetComponent<SpriteAnimator>(out var anim))
                    anim.PlayAttack(_tp - GridPos);
                if (_decoyTarget != null && tiles.Contains(_decoyTarget.GridPos))
                    hit = _decoyTarget;
                else if (tiles.Contains(player.GridPos))
                    hit = player;
            }

            if (hit != null && hit == _decoyTarget)
            {
                _decoyTarget.TakeHit(this, damage);
                return;
            }
            if (!(hit is PlayerActor))
                return;
            KnightEchoHitCount++;
            KnightPatternLanded = true;
            var dealt = player.Stats.TakeIncomingDamage(damage);
            PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} 잔상", dealt, player.Stats);
            room.ApplyThorns(this, dealt);
            DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f), isCritical: true);
            HitFeedback.OnPlayerHurt(player, this);
        }

        /// <summary>예고한 3칸을 벤다 - 거기 남아 있는 플레이어(또는 미끼)가 공격력 SlashDamageRate배로 맞는다.</summary>
        private void ResolveKnightSlash(PlayerActor player, RoomController room)
        {
            var tiles = _knightSlashTiles;
            _knightSlashTiles = null;
            room.ClearEnemyTelegraph(this);
            if (TryGetComponent<SpriteAnimator>(out var anim) && tiles.Count > 0)
                anim.PlayAttack(tiles[0] - GridPos);
            if (HitFeedback.Enabled) // 강타 칸마다 청록 충격(Effects/knight_smash, 10-23) - 그림이 없으면 조용히
                foreach (var t in tiles)
                    Fx.Play("knight_smash", new Vector3(t.x * GridConstants.CellSize, t.y * GridConstants.CellSize, 0f), 1.4f, fps: 16f, sortingOrder: 4);
            if (_decoyTarget != null && tiles.Contains(_decoyTarget.GridPos))
            {
                _decoyTarget.TakeHit(this, _knightPatternPower * MemoryKnight.SlashDamageRate);
                return;
            }
            if (!tiles.Contains(player.GridPos))
                return;
            KnightSlashHitCount++;
            KnightPatternLanded = true;
            var dealt = player.Stats.TakeIncomingDamage(_knightPatternPower * MemoryKnight.SlashDamageRate);
            PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} 베기", dealt, player.Stats);
            room.ApplyThorns(this, dealt);
            DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f), isCritical: true);
            HitFeedback.OnPlayerHurt(player, this);
        }

        /// <summary>근접 포위: 붙어 있으면 공격. 아니면 플레이어 상하좌우 중 비어 있고 아직 예약 안 된 칸들 가운데
        /// BFS로 제일 가까운 칸을 예약하고 그 경로의 첫 걸음을 간다 - 여러 마리가 한 줄로 따라오지 않고 옆/뒤로
        /// 돌아 들어온다. 갈 칸이 없으면(다 찼거나 막힘) 플레이어 쪽으로 그리디하게 한 칸.</summary>
        private void TakeMeleeTurn(PlayerActor player, HashSet<Vector2Int> claimedSlots)
        {
            if (IsAdjacentTo(_tp))
            {
                if (IsMirrorFake)
                {
                    // 10층 거울상 - 때리는 시늉만(들이받는 동작), 피해는 없다(10-15 사용자: 거울상 옆이 제일 아픈 자리가 되던 문제)
                    if (HitFeedback.Enabled)
                        ActorJuice.Get(this).Bump(_tp - GridPos, 0.4f, 0.3f);
                    return;
                }
                if (IsBoss && _turnRoom != null && _turnRoom.BossInvulnerable)
                    return; // 6·7·9층 무적 단계 - 따라다니며 패턴만 쓰고, 붙어서 때리지는 않는다(10-14 사용자)
                // 5층 버섯 군주는 10-14에 일반 공격을 뺐다가(파동·역파동만) 봇 보스방 사망 0.0 - 붙어서 공짜로 때리는 시간만 남아서 10-15에 되살림(사용자)
                HitTarget(player);
                return;
            }
            if (IsLarge)
            {
                MoveBigBodyToward(_tp);
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

        /// <summary>3x3 보스 이동 - 몸 전체가 들어가는 가운데 칸만 밟는 BFS로 "몸이 target에 붙는" 자리를 찾아 첫 걸음.
        /// 길이 없으면(기둥 사이 등) 몸이 들어가는 쪽으로 그리디하게 한 칸.</summary>
        private void MoveBigBodyToward(Vector2Int target)
        {
            var first = FirstStepBuffer;
            var queue = QueueBuffer;
            first.Clear();
            queue.Clear();
            foreach (var d in Directions)
            {
                var next = GridPos + d;
                if (!Map.CanOccupy(this, next))
                    continue;
                first[next] = next;
                queue.Enqueue(next);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (GridMap.FootprintDistance(cur, FootprintMin, FootprintMax, target) == 1)
                {
                    Map.MoveActor(this, first[cur]);
                    return;
                }
                foreach (var d in Directions)
                {
                    var next = cur + d;
                    if (next == GridPos || first.ContainsKey(next) || !Map.CanOccupy(this, next))
                        continue;
                    first[next] = first[cur];
                    queue.Enqueue(next);
                }
            }

            var diff = target - GridPos;
            var primary = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                ? new Vector2Int(System.Math.Sign(diff.x), 0)
                : new Vector2Int(0, System.Math.Sign(diff.y));
            var secondary = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                ? new Vector2Int(0, System.Math.Sign(diff.y))
                : new Vector2Int(System.Math.Sign(diff.x), 0);
            foreach (var step in new[] { primary, secondary })
            {
                if (step != Vector2Int.zero && Map.CanOccupy(this, GridPos + step))
                {
                    Map.MoveActor(this, GridPos + step);
                    return;
                }
            }
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
            if (IsBoss && player.ReflectInto(this, _turnRoom))
                return; // 반사 부적 - 보스 평타도 막고 되돌린다(10-16 사용자)
            var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower * rate);
            PlayLog.PlayerHit($"{PlayLog.EnemyLabel(this)} {(melee ? "근접" : "원거리")}", dealt, player.Stats);
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

        /// <summary>10층 거울상 - 진짜 보스의 지금 그림(변신·걷기·공격 프레임)·색·크기를 매 프레임 그대로 베끼고 좌우만 뒤집는다.
        /// 한 칸 움직이면 진짜처럼 미끄러지듯 걷는다(순간이동하면 가짜라는 티가 나서). 진짜가 죽거나 사라지면 마지막 모습 그대로.</summary>
        [DefaultExecutionOrder(100)] // SpriteAnimator(진짜 그림 갱신)보다 뒤에
        private sealed class MirrorImage : MonoBehaviour
        {
            private const float StepSeconds = 0.18f;
            private EnemyActor _self;
            private EnemyActor _original;
            private SpriteRenderer _renderer;
            private SpriteRenderer _source;
            private Vector2Int _lastPos;
            private Vector3 _stepFrom;
            private float _stepStart = -1f;

            public void Setup(EnemyActor self, EnemyActor original, SpriteRenderer renderer)
            {
                _self = self;
                _original = original;
                _renderer = renderer;
                _source = original.GetComponent<SpriteRenderer>();
                _lastPos = self.GridPos;
            }

            private void LateUpdate()
            {
                if (_renderer == null || _self == null)
                    return;
                if (_original != null && _source != null)
                {
                    _renderer.sprite = _source.sprite;
                    _renderer.color = _source.color;
                    _renderer.flipX = !_source.flipX;
                    transform.localScale = _original.transform.localScale;
                }
                if (_self.GridPos != _lastPos)
                {
                    var delta = _self.GridPos - _lastPos;
                    if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1)
                    {
                        _stepFrom = new Vector3(_lastPos.x * GridConstants.CellSize, _lastPos.y * GridConstants.CellSize, 0f);
                        _stepStart = Time.time;
                    }
                    _lastPos = _self.GridPos;
                }
                if (_stepStart >= 0f)
                {
                    var t = Mathf.Clamp01((Time.time - _stepStart) / StepSeconds);
                    transform.position = Vector3.Lerp(_stepFrom, _self.GridWorldPosition, t);
                    if (t >= 1f)
                        _stepStart = -1f;
                }
            }
        }
    }
}
