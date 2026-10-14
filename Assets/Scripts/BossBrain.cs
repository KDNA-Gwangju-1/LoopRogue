using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    public enum BossPatternType
    {
        Slam,     // 강타 - 보스 주변 3x3
        Charge,   // 돌진 - 플레이어 쪽 일직선으로 돌진
        Cross,    // 십자 - 보스의 가로줄 + 세로줄
        Summon,   // 소환 - 졸개 소환(예고 없음)
        Ring,     // 파동 - 보스 주변 거리 1과 3(거리 2는 안전)
        Snipe,    // 저격 1발 - 플레이어 자리 + 대각선 4칸(X자, 상하좌우로 한 칸 움직이면 피함) → 바로 2발로 이어짐
        SnipeFollowUp, // 저격 2발 - 그때 플레이어 자리의 상하좌우 4칸(마름모, 가운데는 안전 → 대기하면 피함)
        Diagonal, // X자 - 플레이어 자리 기준 대각선 4줄(저격처럼 조준)
        MirrorShards, // 거울 파편(10층 거울 가면 기사 본모습 전용) - 4번의 공격 칸을 한꺼번에 예고(칸마다 몇 번째인지 숫자)하고 보스 턴마다 하나씩 터뜨림
        RingInverse, // 역파동(5층 전용) - 파동과 반대로 거리 2·4·6…(짝수)을 친다. 바로 옆과 홀수 거리가 안전
    }

    /// <summary>스테이지 보스의 예고 공격 - 몇 턴마다 공격할 칸을 빨갛게 예고하고(그 턴은 안 움직임), 다음 턴에
    /// 그 칸에 있으면 강한 피해(공격력 × PatternDamageMultiplier). 그 사이 피하면 안 맞는다. 나머지 턴에는 일반
    /// 근접 몹처럼 포위 이동/공격. 스테이지마다 쓰는 패턴 목록이 다르다(StagePatterns).</summary>
    public class BossBrain
    {
        /// <summary>예고 공격 사이 일반 턴 수(최소~최대, 매 사이클 랜덤) - 예전엔 모든 보스가 "일반 2턴 → 예고 → 발동"으로
        /// 고정이라 몇 번 해보면 박자를 외울 수 있었다. 보스마다 범위를 다르게 두고 매번 그 안에서 굴린다.</summary>
        private static (int Min, int Max) NormalTurnRange(int stage) => stage switch
        {
            1 => (2, 3), // 입문 - 느리고 거의 일정
            2 => (2, 3),
            3 => (1, 3),
            4 => (2, 4), // 소환 + 강타 - 졸개가 있어서 느리게
            5 => (1, 2), // 파동 - 빠른 박자
            6 => (2, 3), // 저격은 2연속이라 사이를 넉넉히
            7 => (1, 3),
            8 => (1, 3),
            9 => (1, 2),
            _ => (1, 3), // 10 - 전부
        };

        /// <summary>광폭화 - 보스 HP가 이 비율 이하가 되면 일반 턴 수가 1 줄어든다(최소 1, 예고 없는 연속 패턴은 안 됨).</summary>
        public const float EnrageHealthRatio = 0.5f;
        public const float PatternDamageMultiplier = 1.5f;
        public const int MaxMinions = 2;
        public const float MinionHealthRatio = 0.06f;
        public const float MinionAttackRatio = 0.35f;

        /// <summary>자동 플레이 봇 통계용 - 예고 공격이 발동한 횟수 / 그중 플레이어가 맞은 횟수.</summary>
        public static int PatternResolveCount;
        public static int PatternHitCount;
        public static int EnrageCount;

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private readonly EnemyActor _boss;
        private readonly List<BossPatternType> _patterns;
        private int _patternIndex;
        private int _normalTurns;
        private int _normalTurnsThisCycle;
        private readonly int _stage;
        private bool _enraged;
        private static readonly System.Random Rng = new System.Random();

        /// <summary>돌진 후 기절 턴 수 - 없으면 돌진으로 방 반대편에 갔다가 걸어 돌아오는 동안 다음 예고가 떠서
        /// 때릴 틈이 전혀 없었다(봇 30판 중 15판이 스테이지 2 보스에서 서로 한 대도 못 치고 1500턴 반복).</summary>
        public const int ChargeStunTurns = 2;

        /// <summary>돌진 최대 칸 수 - 방 끝까지 가로지르면 걸어 돌아오는 사이 기절이 풀려 여전히 때릴 틈이 없었다.</summary>
        public const int MaxChargeDistance = 5;

        // ---- 1층 거대 멧돼지 기믹(사용자 구상 10-13) ----
        // 체력이 BoarPillarHealthRatio 이하가 되면 주변에 돌기둥 4개(RoomController.SpawnBoarPillars) + 돌진 거리 BoarPhaseChargeDistance.
        // 기둥이 하나라도 서 있으면 받는 피해 × BoarArmorMultiplier. 돌진하다 기둥에 막히면 그 기둥이 부서지고 BoarCrashStunTurns 기절.
        // 4개를 전부 부수면 단단함이 풀린다(폭탄으로 부숴도 된다).
        public const float BoarPillarHealthRatio = 0.5f;
        public const int BoarPhaseChargeDistance = 8;
        public const float BoarArmorMultiplier = 0.1f;
        public const int BoarCrashStunTurns = 3;
        private bool _pillarPhase;

        private bool IsBoar => _stage == 1;

        // ---- 2층 오우거 기믹(사용자 구상 10-13 A안 - 균열은 양쪽 모두에게 위험) ----
        // 체력이 OgreCrackHealthRatio 이하가 되면 강타가 내려찍은 테두리 칸 중 OgreCracksPerSlam칸에 균열을 남긴다(RoomController.AddOgreCracks).
        // 오우거가 밟으면 빠져서 기절, 플레이어가 밟으면 묶임 - 판정은 RoomController.CheckOgreCrack / CheckPlayerCrack.
        public const float OgreCrackHealthRatio = 0.5f;
        /// <summary>10-14 봇: 균열 덕에 2층 보스방 사망 3.1→0.7(목표 3)로 너무 쉬워져서 일단 끔(사용자). 코드는 그대로 - true면 다시 켜짐.</summary>
        public const bool OgreCracksEnabled = false;
        // 10-14 새 기믹(사용자): 체력 OgreHoleHealthRatio 이하부터 강타가 던전을 흔들어 바닥에 구멍 OgreHolesMin~Max개(RoomController.OpenOgreHoles).
        // 플레이어가 붙어서 메운다(한 턴). 열린 구멍이 RoomController.OgreHoleCollapseCount개면 즉사.
        public const bool OgreHolesEnabled = true;
        public const float OgreHoleHealthRatio = 0.5f;
        // 10-14 사용자 "구멍 메우면서 잡는 게 가능한가, 두 개씩은 너무 많다" - 강타(4~5턴마다)에 2~3개면 메우러 다니는 턴이 모자라서 1개로.
        public const int OgreHolesMin = 1, OgreHolesMax = 1;
        private bool _holePhase;
        public const int OgreCracksPerSlam = 4;
        private bool _crackPhase;
        private bool IsOgre => _stage == 2;

        // ---- 3층 돌 골렘 기믹(사용자 구상 10-13) ----
        // 10-14 사용자 설계: 체력 80/60/40/20%마다 수정 1/2/3/4개가 솟고(RoomController.SpawnGolemCrystals - 수정마다 12턴 뒤 방 전체 폭발 즉사,
        // 붙어서 깨거나 바위 뒤에 숨는다) 바위가 (수정 수 + 1)개 떨어진다. 바위는 폭발에 맞으면 깨진다(한 번 숨겨 주면 사라짐).
        // 3번째 물결(40%)부터는 십자 베기가 지나간 줄에도 바위 GolemRubblePerCross개.
        private static readonly float[] GolemWaveHealth = { 0.8f, 0.6f, 0.4f, 0.2f };
        public const int GolemRubblePerCross = 3;
        private const int GolemCrossRubbleWave = 3;
        private int _golemPhase; // 지나간 물결 수(0~4)
        private bool _collectCrystals;
        private readonly List<Vector2Int> _resonating = new List<Vector2Int>();
        private RoomController _room;
        private bool IsGolem => _stage == 3;

        /// <summary>봇 통계용 - 1층 멧돼지가 돌진하다 기둥에 박은 횟수.</summary>
        public static int BoarCrashCount;

        private int ChargeDistance => IsBoar && _pillarPhase ? BoarPhaseChargeDistance : MaxChargeDistance;

        /// <summary>보스가 받는 피해 배율 - 멧돼지 기둥이 서 있으면 BoarArmorMultiplier(EnemyActor.DamageTakenMultiplier).</summary>
        public float ArmorMultiplier(RoomController room) =>
            room != null && room.BossInvulnerable ? 0f // 6·7·9층 기믹 - 촛불·분신·항아리가 남아 있는 동안 무적
            : IsBoar && _pillarPhase && room != null && room.BoarPillarCount > 0 ? BoarArmorMultiplier : 1f;

        // ---- 4~9층 기믹(10-14 사용자) - 체력 StageGimmickHealthRatio 이하에서 한 번 시작(RoomController.BeginStageGimmick)
        public const float StageGimmickHealthRatio = 0.5f;
        private bool _stageGimmickStarted;
        private bool _collectJars;                                     // 9층 - 십자 마법 줄 끝에 닿은 항아리를 모은다(AddRay)
        private readonly List<Vector2Int> _jarHits = new List<Vector2Int>();

        private int _stunTurns;
        /// <summary>돌진 뒤 비틀거림·기둥/용암 충돌 등 패턴 쪽 기절 중인지(머리 위 기절 별 - StatusIcons).</summary>
        public bool IsStunned => _stunTurns > 0;
        private BossPatternType? _pending;
        private readonly HashSet<Vector2Int> _pendingTiles = new HashSet<Vector2Int>();
        private Vector2Int _chargeDirection;

        public BossBrain(EnemyActor boss, List<BossPatternType> patterns, int stage)
        {
            _boss = boss;
            _patterns = patterns != null && patterns.Count > 0 ? patterns : new List<BossPatternType> { BossPatternType.Slam };
            _stage = stage;
            RollNormalTurns();
        }

        private void RollNormalTurns()
        {
            var (min, max) = NormalTurnRange(_stage);
            if (_enraged)
            {
                min = Mathf.Max(1, min - 1);
                max = Mathf.Max(1, max - 1);
            }
            _normalTurnsThisCycle = Rng.Next(min, max + 1);
        }

        /// <summary>10층 = 거울 가면 기사(다른 보스로 변신하며 그 보스의 패턴을 쓴다).</summary>
        public const int MirrorStage = 10;

        /// <summary>거울 파편 - 몇 번 연달아 터지는지 / 플레이어 중심 범위(체비쇼프) / 웨이브마다 범위 칸을 덮는 비율 / 한 번 맞을 때 피해 배율(패턴 피해 기준).
        /// 예전엔 체스판 2연속이라 상하좌우로 계속 걷기만 하면 다 피했다(사용자 지적) - 4번을 미리 보여주고 숫자 순서대로 피하게.</summary>
        public const int MirrorWaveCount = 4;
        public const int MirrorWaveRadius = 3;
        public const float MirrorWaveDensity = 0.55f;
        public const float MirrorWaveDamageRate = 0.75f;

        /// <summary>층 보스 이름 - 그림은 Resources/Sprites/Boss{층}.</summary>
        public static string BossName(int stage) => stage switch
        {
            1 => Loc.T("거대 멧돼지"),
            2 => Loc.T("오우거"),
            3 => Loc.T("돌 골렘"),
            4 => Loc.T("슬라임 킹"),
            5 => Loc.T("버섯 군주"),
            6 => Loc.T("망령 궁수"),
            7 => Loc.T("그림자 암살자"),
            8 => Loc.T("미노타우로스"),
            9 => Loc.T("리치"),
            _ => Loc.T("거울 가면 기사"),
        };

        /// <summary>거울 가면 기사가 이 패턴을 쓸 때 변신하는 보스(그 패턴의 원래 주인 층) - 0이면 변신 안 함.</summary>
        public static int MorphStage(BossPatternType type) => type switch
        {
            BossPatternType.Charge => 1,
            BossPatternType.Slam => 2,
            BossPatternType.Cross => 3,
            BossPatternType.Summon => 4,
            BossPatternType.Ring => 5,
            BossPatternType.Snipe => 6,
            BossPatternType.Diagonal => 7,
            _ => 0,
        };

        /// <summary>스테이지별 패턴 목록 - 1~7은 하나씩, 8~9는 두 개를 번갈아, 10은 전부 무작위(NextPattern).</summary>
        public static List<BossPatternType> StagePatterns(int stage) => stage switch
        {
            1 => new List<BossPatternType> { BossPatternType.Charge }, // 1/2 서로 바꿈(사용자 요청) - 돌진이 봇 테스트에서 가장 쉬운 패턴이라 입문용
            2 => new List<BossPatternType> { BossPatternType.Slam },
            3 => new List<BossPatternType> { BossPatternType.Cross },
            4 => new List<BossPatternType> { BossPatternType.Summon, BossPatternType.Slam },
            5 => new List<BossPatternType> { BossPatternType.Ring, BossPatternType.RingInverse }, // 10-14 사용자 "패턴을 여러 개로, 범위도 크게" - 번갈아
            6 => new List<BossPatternType> { BossPatternType.Snipe },
            7 => new List<BossPatternType> { BossPatternType.Diagonal },
            8 => new List<BossPatternType> { BossPatternType.Charge, BossPatternType.Slam },
            9 => new List<BossPatternType> { BossPatternType.Cross, BossPatternType.Snipe },
            _ => new List<BossPatternType>
            {
                BossPatternType.Slam, BossPatternType.Charge, BossPatternType.Cross, BossPatternType.Summon,
                BossPatternType.Ring, BossPatternType.Snipe, BossPatternType.Diagonal,
                BossPatternType.MirrorShards, // 자기 패턴 - 쓸 땐 변신을 풀고 본모습으로
            },
        };

        public static string PatternName(BossPatternType type) => type switch
        {
            BossPatternType.Slam => Loc.T("강타"),
            BossPatternType.Charge => Loc.T("돌진"),
            BossPatternType.Cross => Loc.T("십자 베기"),
            BossPatternType.Summon => Loc.T("소환"),
            BossPatternType.Ring => Loc.T("파동"),
            BossPatternType.RingInverse => Loc.T("역파동"),
            BossPatternType.Snipe => Loc.T("저격"),
            BossPatternType.SnipeFollowUp => Loc.T("저격(2발)"),
            BossPatternType.MirrorShards => Loc.T("거울 파편"),
            _ => Loc.T("X자 베기"),
        };

        /// <summary>이번 턴 보스 행동. 예고/발동/소환을 했으면 true, 일반 행동을 해야 하면 false.</summary>
        /// <summary>섬광탄 - 예고해둔 공격(저격 후속 포함)을 없던 일로.</summary>
        public void CancelPending(RoomController room)
        {
            _cancelled = true; // Resolve 도중(반사 → 거울 깨기)이면 후속 패턴을 걸지 않게
            _pending = null;
            _pendingTiles.Clear();
            _mirrorWaves.Clear();
            room.ClearTelegraph();
        }

        /// <summary>거울 파편(4연격)이 진행 중인지 - 섬광탄으로도 안 멈추고, 보스가 기절해 있어도 계속 터진다(사용자 요청).</summary>
        public bool MirrorShardsActive => _pending == BossPatternType.MirrorShards;

        /// <summary>체력 조건을 지난 기믹 시작(컷신) - 시작했으면 true(그 턴은 끝). 보스 러시는 입장하자마자 이걸 한 번 부른다.</summary>
        public bool TryStartGimmick(RoomController room)
        {
            _room = room;
            if (IsBoar && !_pillarPhase && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * BoarPillarHealthRatio)
            {
                _pillarPhase = true;
                CancelPending(room); // 예고해 둔 돌진은 취소 - 컷신 뒤 처음부터
                room.StartBoarPillarPhase(_boss); // 짧은 컷신: 멧돼지는 방 중앙, 플레이어는 그 2칸 아래 → 기둥이 솟는다(10-14 사용자)
                return true;
            }

            // 3층 골렘 - 체력 80/60/40/20%를 지날 때마다 수정 n개 + 바위 n+1개(한 번에 여러 칸 깎였으면 지난 물결을 모두)
            if (IsGolem && _golemPhase < GolemWaveHealth.Length && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * GolemWaveHealth[_golemPhase])
            {
                // 지난 물결을 모두 합쳐 컷신 한 번(바위가 하나씩 떨어지고 수정이 하나씩 솟는다 - RoomController.StartGolemWave)
                int crystals = 0, rubble = 0;
                var first = _golemPhase == 0;
                var crossStarts = false;
                while (_golemPhase < GolemWaveHealth.Length && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * GolemWaveHealth[_golemPhase])
                {
                    _golemPhase++;
                    crystals += _golemPhase;
                    rubble += _golemPhase + 1; // 폭발을 막아 줄 바위(안전지대) - 폭발에 맞으면 깨진다
                    crossStarts |= _golemPhase == GolemCrossRubbleWave;
                }
                room.StartGolemWave(_boss, crystals, rubble, first, crossStarts);
                return true;
            }

            if (IsOgre && OgreHolesEnabled && !_holePhase && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * OgreHoleHealthRatio)
            {
                _holePhase = true;
                room.StartOgreHolePhase(_boss); // 컷신 - 첫 구멍 1개
                return true;
            }

            if (!_stageGimmickStarted && _stage >= 4 && _stage <= 9 && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * StageGimmickHealthRatio)
            {
                _stageGimmickStarted = true;
                room.BeginStageGimmick(_stage, _boss); // 컷신 - 소품·분신이 하나씩
                return true;
            }
            return false;
        }

        /// <summary>그 층 기믹이 처음 시작하는 체력 비율 - 보스 러시는 보스를 이 체력으로 내보낸다. 기믹이 없으면 1.</summary>
        public static float GimmickStartHealthRatio(int stage) => stage switch
        {
            1 => BoarPillarHealthRatio,
            2 => OgreHoleHealthRatio,
            3 => GolemWaveHealth[0],
            >= 4 and <= 9 => StageGimmickHealthRatio,
            _ => 1f,
        };

        public bool TakePatternTurn(PlayerActor player, RoomController room)
        {
            _room = room;
            if (_pending.HasValue)
            {
                Resolve(player, room);
                return true;
            }

            if (_stunTurns > 0)
            {
                _stunTurns--;
                return true; // 기절 중 - 아무것도 안 함(공격 기회)
            }

            if (TryStartGimmick(room))
                return true;

            if (IsOgre && OgreCracksEnabled && !_crackPhase && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * OgreCrackHealthRatio)
            {
                _crackPhase = true;
                room.ShowMessage(Loc.T("<color=#FFB060>오우거가 분노해 바닥을 부술 기세로 몽둥이를 쥔다!</color> 강타가 바닥에 균열을 남긴다"));
                room.ShowMessage(Loc.T("균열은 오우거도 나도 빠뜨린다 - 균열 너머로 유도하되, 내가 밟지는 말자"));
            }

            if (!_enraged && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * EnrageHealthRatio)
            {
                _enraged = true;
                EnrageCount++;
                _boss.MarkEnraged();
                room.ShowMessage(Loc.T("보스가 광폭화했다! 공격이 잦아진다!"));
                _normalTurnsThisCycle = Mathf.Min(_normalTurnsThisCycle, Mathf.Max(1, NormalTurnRange(_stage).Max - 1));
            }

            if (_normalTurns < _normalTurnsThisCycle)
            {
                _normalTurns++;
                return false;
            }

            _normalTurns = 0;
            RollNormalTurns();
            var type = NextPattern();

            if (type == BossPatternType.MirrorShards)
            {
                BuildMirrorWaves(player, room.Map);
                if (_mirrorWaves.Count == 0)
                    return false;
                _pending = type;
                _mirrorWaveNumber = 1;
                room.ShowTelegraphWaves(_mirrorWaves, _mirrorWaveNumber);
                TryMorph(type, out var mirrorMessage);
                room.ShowMessage(mirrorMessage + Loc.T(" - 숫자 순서대로 터진다!"));
                return true;
            }

            if (type == BossPatternType.Summon)
            {
                room.SpawnMinions(_boss, MaxMinions);
                room.ShowMessage(TryMorph(type, out var morph) ? $"{morph} ({PatternName(type)})" : Loc.F("보스의 {0}!", PatternName(type)));
                return true;
            }

            ComputeTiles(type, player, room.Map);
            if (_pendingTiles.Count == 0)
                return false; // 쓸 칸이 없으면(구석 등) 그냥 일반 행동

            _pending = type;
            room.ShowTelegraph(_pendingTiles);
            if (TryMorph(type, out var message))
                room.ShowMessage(message);
            return true;
        }

        /// <summary>다음 패턴 - 1~9층은 목록 순서대로, 10층은 무작위(같은 패턴 연속은 안 나옴).</summary>
        private BossPatternType NextPattern()
        {
            if (_stage < MirrorStage || _patterns.Count < 2)
            {
                var next = _patterns[_patternIndex];
                _patternIndex = (_patternIndex + 1) % _patterns.Count;
                return next;
            }
            BossPatternType pick;
            do
                pick = _patterns[Rng.Next(_patterns.Count)];
            while (pick == _lastRandom);
            _lastRandom = pick;
            return pick;
        }

        private BossPatternType? _lastRandom;

        /// <summary>10층 거울 가면 기사 - 쓰는 패턴의 원래 주인(1~7층 보스)으로 변신하고, 다음 변신까지 그 모습으로 싸운다.</summary>
        private bool TryMorph(BossPatternType type, out string message)
        {
            message = null;
            if (_stage >= MirrorStage && type == BossPatternType.MirrorShards)
            {
                // 자기 패턴 - 비추던 모습을 깨고 본모습으로 돌아온다.
                message = _boss.RevertForm()
                    ? Loc.F("{0}가 본모습을 드러냈다! ({1})", BossName(MirrorStage), PatternName(type))
                    : Loc.F("보스의 {0}!", PatternName(type));
                return true;
            }
            var target = MorphStage(type);
            if (_stage < MirrorStage || target == 0)
                return false;
            _boss.MorphInto(target);
            message = Loc.F("{0}가 {1}의 모습을 비췄다!", BossName(MirrorStage), BossName(target));
            return true;
        }

        private bool _cancelled;

        private void Resolve(PlayerActor player, RoomController room)
        {
            var type = _pending.Value;
            _pending = null;
            _cancelled = false;
            room.ClearTelegraph();
            if (type == BossPatternType.MirrorShards && _mirrorWaves.Count > 0)
            {
                _pendingTiles.Clear();
                _pendingTiles.UnionWith(_mirrorWaves[0]); // 이번에 터질 웨이브
                _mirrorWaves.RemoveAt(0);
            }

            // 패턴이 터지는 순간 공격 모션(그림이 있을 때만) - 돌진은 돌진한 방향, 나머지는 플레이어 쪽.
            if (_boss.TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(type == BossPatternType.Charge ? _chargeDirection : player.GridPos - _boss.GridPos);

            if (type == BossPatternType.Charge)
            {
                var crashed = DoChargeMove(room);
                _stunTurns = crashed ? BoarCrashStunTurns : ChargeStunTurns;
                if (crashed && IsBoar)
                    BoarCrashCount++;
                if (!crashed)
                    room.ShowMessage(Loc.T("보스가 돌진 후 비틀거린다!"));
                else if (!IsBoar)
                {
                    // 8층 용암 - 메시지는 RoomController.TryLavaFall이
                }
                else if (room.BoarPillarCount > 0)
                    room.ShowMessage(Loc.F("<color=#FFB060>멧돼지가 기둥에 박았다!</color> 크게 비틀거린다 (남은 기둥 {0})", room.BoarPillarCount));
                else
                    room.ShowMessage(Loc.T("<color=#FFB060>멧돼지가 마지막 기둥에 박았다!</color> 크게 비틀거린다")); // "모두 무너졌다"는 RoomController가
            }

            if (type == BossPatternType.Slam && IsOgre && _crackPhase)
                room.AddOgreCracks(_pendingTiles, OgreCracksPerSlam);
            if (type == BossPatternType.Slam && IsOgre && _holePhase)
                room.OpenOgreHoles(_boss, UnityEngine.Random.Range(OgreHolesMin, OgreHolesMax + 1)); // 붕괴 즉사면 몹 턴 루프가 사망 처리

            PatternResolveCount++;
            HitFeedback.OnBossPatternResolved(player, _pendingTiles.Contains(player.GridPos), type, _pendingTiles);
            if (_pendingTiles.Contains(player.GridPos))
            {
                PatternHitCount++;
                if (player.TryConsumeReflect())
                {
                    // 반사 부적 - 플레이어는 안 맞고 보스가 최대 체력의 일정 비율을 맞는다.
                    var reflected = _boss.Stats.MaxHealth * ItemInfo.ReflectBossDamageRate;
                    _boss.Stats.TakeDamage(reflected);
                    DamagePopup.Spawn(_boss.transform.position, reflected, new Color(0.6f, 0.9f, 1f), isCritical: true);
                    room.ShowMessage(Loc.T("반사 부적! 보스의 공격을 되돌렸다"));
                    room.HandleBossDamagedByItem(_boss);
                    if (_boss.Stats.IsDead)
                    {
                        // 반사로 보스가 죽었다 - 저격 2발·남은 거울 파편을 다시 깔지 않는다(클리어한 방에 예고가 남던 문제).
                        _pendingTiles.Clear();
                        _mirrorWaves.Clear();
                        return;
                    }
                }
                else
                {
                    var ironWall = EquipmentEffects.Has(ItemSlot.Armor, 5) ? 1f - EquipmentEffects.BossPatternReduction : 1f; // 갑옷 "철벽"
                    var waveRate = type == BossPatternType.MirrorShards ? MirrorWaveDamageRate : 1f; // 거울 파편은 4번이라 한 번은 약하게
                    var dealt = player.Stats.TakeIncomingDamage(_boss.Stats.AttackPower * PatternDamageMultiplier * ironWall * waveRate);
                    DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.2f, 0.2f), isCritical: true);
                }
            }
            if (_stageGimmickStarted && room.GimmickStage == _stage)
            {
                if (_stage == 4 && type == BossPatternType.Slam)
                    room.OnSlimeSlam(_pendingTiles);
                else if (_stage == 5 && (type == BossPatternType.Ring || type == BossPatternType.RingInverse))
                    room.OnSporeRing(_pendingTiles, _pendingTiles.Contains(player.GridPos)); // 감염으로 죽으면 몹 턴 루프가 사망 처리
                else if (_stage == 6 && (type == BossPatternType.Snipe || type == BossPatternType.SnipeFollowUp))
                    room.OnSnipeTiles(_pendingTiles);
                else if (_stage == 7 && type == BossPatternType.Diagonal)
                    room.OnDiagonalTiles(_pendingTiles);
                else if (_stage == 8 && type == BossPatternType.Slam)
                    room.AddLava(_pendingTiles);
                else if (_stage == 9 && type == BossPatternType.Cross)
                    room.BreakLichJars(_jarHits);
            }
            if (type == BossPatternType.Cross && IsGolem)
            {
                if (_golemPhase >= GolemCrossRubbleWave)
                    room.DropGolemRubble(_pendingTiles, GolemRubblePerCross);
            }
            _pendingTiles.Clear();
            if (_cancelled)
            {
                // 이번 발동 중에 거울이 깨져 취소됐다 - 후속 패턴 없음, 돌진 기절도 거울 깨기 기절과 겹치지 않게.
                _stunTurns = 0;
                _mirrorWaves.Clear();
                return;
            }

            // 저격은 2연속 - 1발(X자)이 끝나면 곧바로 그 순간 플레이어 자리 기준 마름모를 예고한다.
            // "상하좌우로 움직여 1발을 피하고 → 대기해서 2발을 피하는" 패턴.
            // 거울 파편 - 남은 웨이브가 있으면 다음 보스 턴에 또 터진다(남은 숫자를 다시 보여줌).
            if (type == BossPatternType.MirrorShards)
            {
                if (_mirrorWaves.Count > 0 && !player.Stats.IsDead)
                {
                    _mirrorWaveNumber++;
                    _pending = BossPatternType.MirrorShards;
                    room.ShowTelegraphWaves(_mirrorWaves, _mirrorWaveNumber);
                }
                else
                    _mirrorWaves.Clear();
            }

            if (type == BossPatternType.Snipe && !player.Stats.IsDead)
            {
                ComputeTiles(BossPatternType.SnipeFollowUp, player, room.Map);
                if (_pendingTiles.Count > 0)
                {
                    _pending = BossPatternType.SnipeFollowUp;
                    room.ShowTelegraph(_pendingTiles);
                }
            }
        }

        /// <summary>보스 몸 가장자리에서 거리 1~maxR 중 홀수(odd) 또는 짝수 거리 고리 칸.</summary>
        private void AddRings(GridMap map, Vector2Int origin, int body, int maxR, bool odd)
        {
            var reach = Mathf.Min(maxR, Mathf.Max(map.Width, map.Height)) + body;
            for (var dx = -reach; dx <= reach; dx++)
            for (var dy = -reach; dy <= reach; dy++)
            {
                var r = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) - body; // 몸 가장자리 기준 거리
                if (r >= 1 && r <= maxR && (r % 2 == 1) == odd)
                    AddTile(map, origin + new Vector2Int(dx, dy));
            }
        }

        private void ComputeTiles(BossPatternType type, PlayerActor player, GridMap map)
        {
            _pendingTiles.Clear();
            _resonating.Clear();
            _jarHits.Clear();
            var origin = _boss.GridPos;
            var body = _boss.FootprintRadius; // 3x3 보스면 1 - 보스 기준 범위는 몸 가장자리에서 잰다

            switch (type)
            {
                case BossPatternType.Slam:
                    AddSquare(map, origin, 1 + body); // 몸 둘레 한 줄
                    break;
                case BossPatternType.Snipe:
                    // 3x3이면 한 턴에 한 칸만 움직이는 플레이어가 절대 못 빠져나가서, 플레이어 자리 + 대각선만 친다.
                    AddTile(map, player.GridPos);
                    foreach (var d in new[] { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) })
                        AddTile(map, player.GridPos + d);
                    break;
                case BossPatternType.SnipeFollowUp:
                    // 마름모 - 상하좌우 4칸만, 가운데(플레이어 자리)는 비워서 대기 키로 피할 수 있게.
                    foreach (var d in Directions)
                        AddTile(map, player.GridPos + d);
                    break;
                case BossPatternType.Ring:
                    // 거리 1과 3을 치고 그 사이 거리 2는 안전 - 보스 옆에서 때리다 예고가 뜨면 한 칸 물러나서 피한다.
                    // 예전(거리 2~3, 바로 옆 안전)은 옆에 붙어 계속 때리면 절대 안 맞아서 패턴이 보스의 공격 턴만 버리게
                    // 만들었다(봇 100판: 5층 81판 무사망). 1~2칸 전부 치면 예고 후 한 칸 이동으로는 못 빠져나간다.
                    // 5층 버섯 군주는 방 끝까지 홀수 거리 전부(1·3·5…) - 멀리 도망가서 피하는 길을 막는다(10-14 사용자 "범위를 키우자").
                    // 10층 거울 가면 기사가 빌려 쓸 땐 예전처럼 1과 3만.
                    AddRings(map, origin, body, _stage == 5 ? 99 : 3, odd: true);
                    break;
                case BossPatternType.RingInverse:
                    // 역파동 - 짝수 거리(2·4·6…). 파동을 피하던 거리 2가 이번엔 위험, 보스 바로 옆이 안전
                    AddRings(map, origin, body, 99, odd: false);
                    break;
                case BossPatternType.Cross:
                    // 몸 폭만큼의 띠(3x3이면 3줄) - 가운데 한 줄만 치면 몸 옆 가장자리 칸에 붙어 계속 때려도 절대 안 맞았다
                    // (봇 100판 2026-10-12: 십자 보스 3층 사망 0.0~0.2, 9층 1.3~1.7 - 보스 세기를 50% 올려도 그대로).
                    // 1칸 보스 때처럼 "몸에 붙은 칸은 전부 위험, 모서리 대각선으로 비키면 안전"이 되게.
                    _collectCrystals = false; // 3층 골렘 공명(줄 끝 수정이 터짐) - 10-14 삭제(사용자), 수정은 시간 안에 깨는 폭탄으로 바뀜
                    _collectJars = _stage == 9; // 9층 리치 - 줄 끝 항아리가 깨진다
                    foreach (var d in Directions)
                    {
                        var side = new Vector2Int(d.y, d.x); // d에 수직
                        for (var k = -body; k <= body; k++)
                            AddRay(map, origin + side * k, d);
                    }
                    _collectJars = false;
                    _collectCrystals = false;
                    foreach (var c in _resonating) // 터질 수정 주변 3x3도 함께 예고(나도 맞는다)
                        for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            var t = c + new Vector2Int(dx, dy);
                            if (map.IsInBounds(t) && !map.IsWall(t) && !GridMap.Occupies(_boss, t))
                                _pendingTiles.Add(t);
                        }
                    break;
                case BossPatternType.Diagonal:
                    // 저격처럼 플레이어 자리를 조준 - 플레이어 자리 + 거기서 뻗는 대각선 4줄(상하좌우로 한 칸 움직이면 피함).
                    // 예전엔 보스 기준 대각선이라 보스 상하좌우(공격하는 자리)가 항상 안전했다(봇 100판: 7층 사망 2.4회).
                    AddTile(map, player.GridPos);
                    foreach (var d in new[] { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) })
                        AddRay(map, player.GridPos, d);
                    break;
                case BossPatternType.Charge:
                {
                    var diff = player.GridPos - origin;
                    _chargeDirection = Mathf.Abs(diff.x) >= Mathf.Abs(diff.y)
                        ? new Vector2Int(System.Math.Sign(diff.x), 0)
                        : new Vector2Int(0, System.Math.Sign(diff.y));
                    if (_chargeDirection == Vector2Int.zero)
                        _chargeDirection = Vector2Int.left;
                    if (body == 0)
                    {
                        AddRay(map, origin, _chargeDirection, ChargeDistance);
                        break;
                    }
                    // 큰 몸은 몸 폭만큼의 띠를 쓸고 간다 - 몸이 벽에 닿기 직전까지(캐릭터는 예고만 통과, 실제 돌진은 DoChargeMove가 막힘 확인)
                    for (var k = 1; k <= ChargeDistance; k++)
                    {
                        var center = origin + _chargeDirection * k;
                        var fits = true;
                        foreach (var p in GridMap.Cells(center, body))
                            fits &= map.IsInBounds(p) && !map.IsWall(p);
                        if (!fits)
                            break;
                        foreach (var p in GridMap.Cells(center, body))
                            AddTile(map, p);
                    }
                    break;
                }
            }
        }

        /// <summary>돌진 방향으로 벽/다른 캐릭터에 막히기 직전 칸까지 이동(플레이어가 길 위에 있으면 그 앞까지).
        /// 멧돼지 기둥에 막혔으면 그 기둥을 부수고 true(남은 거리를 다 달리기 전에 막혀도).</summary>
        private bool DoChargeMove(RoomController room)
        {
            var map = room.Map;
            var pos = _boss.GridPos;
            var blockedAt = (Vector2Int?)null;
            for (var i = 0; i < ChargeDistance; i++)
            {
                var next = pos + _chargeDirection;
                if (!map.CanOccupy(_boss, next)) // 몸 전체가 들어가야 전진
                {
                    blockedAt = next;
                    break;
                }
                pos = next;
            }
            if (pos != _boss.GridPos)
                map.MoveActor(_boss, pos);
            if (!blockedAt.HasValue)
                return false;
            var front = GridMap.Cells(blockedAt.Value, _boss.FootprintRadius);
            if (room.BoarPillarCount > 0 && room.BreakBoarPillars(front))
                return true;
            return room.TryLavaFall(front, _boss); // 8층 미노타우로스 - 용암 균열에 빠짐(피해·기절은 RoomController가)
        }

        private void AddSquare(GridMap map, Vector2Int center, int radius)
        {
            for (var dx = -radius; dx <= radius; dx++)
            for (var dy = -radius; dy <= radius; dy++)
                AddTile(map, center + new Vector2Int(dx, dy));
        }

        /// <summary>origin에서 d 방향으로 벽이나 방 끝(또는 maxLength칸)을 만날 때까지(캐릭터는 통과).</summary>
        private void AddRay(GridMap map, Vector2Int origin, Vector2Int d, int maxLength = int.MaxValue)
        {
            var length = 0;
            // 보스 몸 안은 건너뛰고 몸 가장자리 바깥부터 센다(3x3 보스)
            var start = GridMap.Occupies(_boss, origin) ? origin + d * (_boss.FootprintRadius + 1) : origin + d;
            var p = start;
            for (; map.IsInBounds(p) && !map.IsWall(p) && length < maxLength; p += d, length++)
                _pendingTiles.Add(p);
            if (_collectCrystals && length < maxLength && _room != null && _room.IsGolemCrystal(p) && !_resonating.Contains(p))
                _resonating.Add(p); // 줄이 공명 수정에서 멈췄다 - 발동 때 터진다
            if (_collectJars && length < maxLength && _room != null && _room.IsLichJar(p) && !_jarHits.Contains(p))
                _jarHits.Add(p); // 9층 - 줄이 영혼 항아리에서 멈췄다 - 발동 때 깨진다
        }

        private readonly List<HashSet<Vector2Int>> _mirrorWaves = new List<HashSet<Vector2Int>>();
        private int _mirrorWaveNumber;

        /// <summary>거울 파편 웨이브 4개 - 먼저 플레이어가 한 칸씩 움직여 갈 안전한 길(p1~p4)을 뽑고, 웨이브 k는 범위 칸을 무작위로 덮되
        /// p_k는 비우고 직전 자리(p_k-1)는 꼭 덮는다. 그래서 제자리면 맞고, 숫자를 읽어 길을 짜면 항상 다 피할 수 있다(직진만으론 안 됨).</summary>
        private void BuildMirrorWaves(PlayerActor player, GridMap map)
        {
            _mirrorWaves.Clear();
            var start = player.GridPos;
            var area = new List<Vector2Int>();
            for (var dx = -MirrorWaveRadius; dx <= MirrorWaveRadius; dx++)
            for (var dy = -MirrorWaveRadius; dy <= MirrorWaveRadius; dy++)
            {
                var t = start + new Vector2Int(dx, dy);
                if (map.IsInBounds(t) && !map.IsWall(t) && !GridMap.Occupies(_boss, t))
                    area.Add(t);
            }
            var areaSet = new HashSet<Vector2Int>(area);

            var cur = start;
            var prev = start;
            for (var k = 0; k < MirrorWaveCount; k++)
            {
                var moves = new List<Vector2Int>();
                foreach (var d in Directions)
                {
                    var p = cur + d;
                    if (areaSet.Contains(p) && (map.IsWalkable(p) || p == start))
                        moves.Add(p);
                }
                if (moves.Count > 1)
                    moves.Remove(prev); // 되돌아가기는 다른 길이 없을 때만
                var next = moves.Count > 0 ? moves[Rng.Next(moves.Count)] : cur;

                var wave = new HashSet<Vector2Int>();
                foreach (var t in area)
                    if (t != next && (t == cur || Rng.NextDouble() < MirrorWaveDensity))
                        wave.Add(t);
                _mirrorWaves.Add(wave);
                prev = cur;
                cur = next;
            }
        }

        private void AddTile(GridMap map, Vector2Int p)
        {
            if (map.IsInBounds(p) && !map.IsWall(p) && !GridMap.Occupies(_boss, p))
                _pendingTiles.Add(p);
        }
    }
}
