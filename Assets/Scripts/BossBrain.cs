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

        private int _stunTurns;
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

        /// <summary>층 보스 이름 - 그림은 Resources/Sprites/Boss{층}.</summary>
        public static string BossName(int stage) => stage switch
        {
            1 => "거대 멧돼지",
            2 => "오우거",
            3 => "돌 골렘",
            4 => "슬라임 킹",
            5 => "버섯 군주",
            6 => "망령 궁수",
            7 => "그림자 암살자",
            8 => "미노타우로스",
            9 => "리치",
            _ => "거울 가면 기사",
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
            5 => new List<BossPatternType> { BossPatternType.Ring },
            6 => new List<BossPatternType> { BossPatternType.Snipe },
            7 => new List<BossPatternType> { BossPatternType.Diagonal },
            8 => new List<BossPatternType> { BossPatternType.Charge, BossPatternType.Slam },
            9 => new List<BossPatternType> { BossPatternType.Cross, BossPatternType.Snipe },
            _ => new List<BossPatternType>
            {
                BossPatternType.Slam, BossPatternType.Charge, BossPatternType.Cross, BossPatternType.Summon,
                BossPatternType.Ring, BossPatternType.Snipe, BossPatternType.Diagonal,
            },
        };

        public static string PatternName(BossPatternType type) => type switch
        {
            BossPatternType.Slam => "강타",
            BossPatternType.Charge => "돌진",
            BossPatternType.Cross => "십자 베기",
            BossPatternType.Summon => "소환",
            BossPatternType.Ring => "파동",
            BossPatternType.Snipe => "저격",
            BossPatternType.SnipeFollowUp => "저격(2발)",
            _ => "X자 베기",
        };

        /// <summary>이번 턴 보스 행동. 예고/발동/소환을 했으면 true, 일반 행동을 해야 하면 false.</summary>
        /// <summary>섬광탄 - 예고해둔 공격(저격 후속 포함)을 없던 일로.</summary>
        public void CancelPending(RoomController room)
        {
            _pending = null;
            _pendingTiles.Clear();
            room.ClearTelegraph();
        }

        public bool TakePatternTurn(PlayerActor player, RoomController room)
        {
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

            if (!_enraged && _boss.Stats.CurrentHealth <= _boss.Stats.MaxHealth * EnrageHealthRatio)
            {
                _enraged = true;
                EnrageCount++;
                _boss.MarkEnraged();
                room.ShowMessage("보스가 광폭화했다! 공격이 잦아진다!");
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

            if (type == BossPatternType.Summon)
            {
                room.SpawnMinions(_boss, MaxMinions);
                room.ShowMessage(TryMorph(type, out var morph) ? $"{morph} ({PatternName(type)})" : $"보스의 {PatternName(type)}!");
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
            var target = MorphStage(type);
            if (_stage < MirrorStage || target == 0)
                return false;
            _boss.MorphInto(target);
            message = $"{BossName(MirrorStage)}가 {BossName(target)}의 모습을 비췄다!";
            return true;
        }

        private void Resolve(PlayerActor player, RoomController room)
        {
            var type = _pending.Value;
            _pending = null;
            room.ClearTelegraph();

            // 패턴이 터지는 순간 공격 모션(그림이 있을 때만) - 돌진은 돌진한 방향, 나머지는 플레이어 쪽.
            if (_boss.TryGetComponent<SpriteAnimator>(out var anim))
                anim.PlayAttack(type == BossPatternType.Charge ? _chargeDirection : player.GridPos - _boss.GridPos);

            if (type == BossPatternType.Charge)
            {
                DoChargeMove(room.Map);
                _stunTurns = ChargeStunTurns;
                room.ShowMessage("보스가 돌진 후 비틀거린다!");
            }

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
                    room.ShowMessage("반사 부적! 보스의 공격을 되돌렸다");
                    room.HandleBossDamagedByItem(_boss);
                }
                else
                {
                    var ironWall = EquipmentEffects.Has(ItemSlot.Armor, 5) ? 1f - EquipmentEffects.BossPatternReduction : 1f; // 갑옷 "철벽"
                    var dealt = player.Stats.TakeIncomingDamage(_boss.Stats.AttackPower * PatternDamageMultiplier * ironWall);
                    DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.2f, 0.2f), isCritical: true);
                }
            }
            _pendingTiles.Clear();

            // 저격은 2연속 - 1발(X자)이 끝나면 곧바로 그 순간 플레이어 자리 기준 마름모를 예고한다.
            // "상하좌우로 움직여 1발을 피하고 → 대기해서 2발을 피하는" 패턴.
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

        private void ComputeTiles(BossPatternType type, PlayerActor player, GridMap map)
        {
            _pendingTiles.Clear();
            var origin = _boss.GridPos;

            switch (type)
            {
                case BossPatternType.Slam:
                    AddSquare(map, origin, 1);
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
                    for (var dx = -3; dx <= 3; dx++)
                    for (var dy = -3; dy <= 3; dy++)
                    {
                        var r = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                        if (r == 1 || r == 3)
                            AddTile(map, origin + new Vector2Int(dx, dy));
                    }
                    break;
                case BossPatternType.Cross:
                    foreach (var d in Directions)
                        AddRay(map, origin, d);
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
                    AddRay(map, origin, _chargeDirection, MaxChargeDistance);
                    break;
                }
            }
        }

        /// <summary>돌진 방향으로 벽/다른 캐릭터에 막히기 직전 칸까지 이동(플레이어가 길 위에 있으면 그 앞까지).</summary>
        private void DoChargeMove(GridMap map)
        {
            var pos = _boss.GridPos;
            for (var i = 0; i < MaxChargeDistance; i++)
            {
                var next = pos + _chargeDirection;
                if (!map.IsWalkable(next))
                    break;
                pos = next;
            }
            if (pos != _boss.GridPos)
                map.MoveActor(_boss, pos);
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
            for (var p = origin + d; map.IsInBounds(p) && !map.IsWall(p) && length < maxLength; p += d, length++)
                _pendingTiles.Add(p);
        }

        private void AddTile(GridMap map, Vector2Int p)
        {
            if (map.IsInBounds(p) && !map.IsWall(p) && p != _boss.GridPos)
                _pendingTiles.Add(p);
        }
    }
}
