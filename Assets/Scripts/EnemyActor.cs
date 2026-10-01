using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>적 종류 - 근접(기본)/원거리(궁수)/방패병(정면 공격 80% 감소, 삼각형)/거미(거미줄로 1턴 속박).</summary>
    public enum EnemyKind
    {
        Melee,
        Ranged,
        Shield,
        Spider,
        Bomber,
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
        public bool IsFuseLit => _fuseLit;

        /// <summary>방패병이 바라보는 방향(상하좌우). 다른 몹은 의미 없음.</summary>
        public Vector2Int Facing { get; private set; } = Vector2Int.left;

        private static readonly Vector2Int[] Directions =
            { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public bool IsBoss { get; private set; }
        public bool IsMinion { get; private set; } // 보스가 소환한 졸개 - 골드/경험치 없음, 보스가 죽으면 같이 사라짐
        public EnemyKind Kind { get; private set; }

        private BossBrain _brain;

        public string DisplayName => IsBoss ? "보스" : IsMinion ? "졸개" : Kind switch
        {
            EnemyKind.Ranged => "궁수",
            EnemyKind.Shield => "방패병",
            EnemyKind.Spider => "거미",
            EnemyKind.Bomber => "폭발병",
            _ => "몬스터",
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

        /// <summary>삼각형 꼭짓점이 바라보는 방향을 가리키게 돌린다(스프라이트는 위쪽을 가리키게 그려져 있음).</summary>
        private void ApplyFacingVisual()
        {
            var angle = Mathf.Atan2(Facing.y, Facing.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>보스 전용 - 스테이지별 예고 공격 패턴을 붙인다.</summary>
        public void SetupBossPatterns(List<BossPatternType> patterns, int stage) => _brain = new BossBrain(this, patterns, stage);

        /// <summary>보스 광폭화 표시 - 붉게 물들인다(BossBrain이 HP 절반 이하가 되는 순간 한 번 부른다).</summary>
        public void MarkEnraged()
        {
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = new Color(1f, 0.25f, 0.2f);
        }

        /// <summary>보스 소환 졸개로 표시 - 색을 어둡게 해서 일반 몹과 구분.</summary>
        public void MarkAsMinion()
        {
            IsMinion = true;
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = new Color(0.55f, 0.25f, 0.25f);
        }

        public void Initialize(float maxHealth, float attackPower, bool isBoss, EnemyKind kind = EnemyKind.Melee)
        {
            IsBoss = isBoss;
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
                color = new Color(0.35f, 0.8f, 0.35f); // 궁수는 초록색 - 한눈에 구분되게
                scale = GridConstants.CellSize * 0.5f;
            }
            else if (Kind == EnemyKind.Shield)
            {
                // 방패병은 삼각형(사용자 요청) - 꼭짓점이 방패 정면.
                VisualUtil.CreateTriangleVisual(gameObject, new Color(0.55f, 0.65f, 0.85f), GridConstants.CellSize * 0.75f, sortingOrder: 0);
                ApplyFacingVisual();
                return;
            }
            else if (Kind == EnemyKind.Spider)
            {
                color = new Color(0.6f, 0.3f, 0.85f); // 거미는 보라색
                scale = GridConstants.CellSize * 0.5f;
            }
            else if (Kind == EnemyKind.Bomber)
            {
                color = new Color(1f, 0.55f, 0.1f); // 폭발병은 주황색(불 붙으면 노랗게)
                scale = GridConstants.CellSize * 0.55f;
            }
            else
            {
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
            else
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

            if (IsAdjacentTo(player.GridPos))
            {
                _fuseLit = true;
                var renderer = GetComponent<SpriteRenderer>();
                if (renderer != null)
                    renderer.color = new Color(1f, 0.95f, 0.3f);
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

            var desired = DirectionToward(player.GridPos, Facing);
            if (canTurn && desired != Facing && !IsAdjacentTo(player.GridPos))
            {
                // 반대편이면 90도만(시계 방향) - 한 번에 뒤돌지 못한다.
                Facing = desired == -Facing ? new Vector2Int(Facing.y, -Facing.x) : desired;
                _shieldTurnCooldown = ShieldTurnCooldown;
                ApplyFacingVisual();
                return;
            }

            if (IsAdjacentTo(player.GridPos))
            {
                HitPlayer(player); // 공격은 매 턴
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

            if (!IsAdjacentTo(player.GridPos) && _webCooldown == 0 && CanShoot(player.GridPos, WebRange))
            {
                _webCooldown = WebCooldownTurns;
                SpawnLineVisual(player.transform.position, new Color(0.95f, 0.95f, 1f, 0.9f), 0.12f, 0.25f);
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
            if (IsAdjacentTo(player.GridPos))
            {
                HitPlayer(player);
                return;
            }

            var slots = new HashSet<Vector2Int>();
            foreach (var d in Directions)
            {
                var slot = player.GridPos + d;
                if (Map.IsWalkable(slot) && !claimedSlots.Contains(slot))
                    slots.Add(slot);
            }

            if (slots.Count > 0 && TryFindPathToAny(slots, out var reachedSlot, out var firstStep))
            {
                claimedSlots.Add(reachedSlot);
                Map.MoveActor(this, firstStep);
                return;
            }

            StepGreedilyToward(player.GridPos);
        }

        /// <summary>현재 칸에서 빈 칸만 밟는 BFS로 targets 중 가장 가까운 칸을 찾는다. 찾으면 그 칸과 첫 걸음을 돌려준다.</summary>
        private bool TryFindPathToAny(HashSet<Vector2Int> targets, out Vector2Int reached, out Vector2Int firstStep)
        {
            reached = default;
            firstStep = default;

            var first = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
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
            var diff = player.GridPos - GridPos;
            var dist = Mathf.Abs(diff.x) + Mathf.Abs(diff.y);
            if (_retreatCooldown > 0)
                _retreatCooldown--;

            if (dist == 1)
            {
                // 물러날 수 있으면(쿨타임 끝) 한 칸 물러나고, 아니면 그 자리에서 근접 공격.
                if (!TryMoveToBestSpot(player, mustIncreaseDistance: true))
                    HitPlayer(player);
                return;
            }

            if (CanShoot(player.GridPos, RangedAttackRange))
            {
                SpawnArrowVisual(player.transform.position);
                HitPlayer(player);
                return;
            }

            TryMoveToBestSpot(player, mustIncreaseDistance: false);
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
        /// 점수: 줄 어긋남(작을수록 좋음)이 최우선, 플레이어에게 붙는 칸(거리 1)은 금지, 그다음 선호 거리와의 차이.</summary>
        private bool TryMoveToBestSpot(PlayerActor player, bool mustIncreaseDistance)
        {
            var currentDist = Distance(GridPos, player.GridPos);
            if (_retreatCooldown > 0)
                return false; // 궁수는 두 턴에 한 번만 이동

            var bestScore = mustIncreaseDistance ? int.MaxValue : SpotScore(GridPos, player.GridPos);
            Vector2Int? best = null;

            foreach (var d in Directions)
            {
                var p = GridPos + d;
                if (!Map.IsWalkable(p))
                    continue;
                var newDist = Distance(p, player.GridPos);
                if (mustIncreaseDistance && newDist <= currentDist)
                    continue;

                var score = SpotScore(p, player.GridPos);
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

        private static int SpotScore(Vector2Int pos, Vector2Int playerPos)
        {
            var dx = Mathf.Abs(playerPos.x - pos.x);
            var dy = Mathf.Abs(playerPos.y - pos.y);
            var dist = dx + dy;
            var misalign = Mathf.Min(dx, dy);
            var tooClose = dist <= 1 ? 1000 : 0;
            var outOfRange = Mathf.Max(dx, dy) > RangedAttackRange ? 5 : 0;
            return tooClose + misalign * 10 + outOfRange + Mathf.Abs(dist - RangedPreferredDistance);
        }

        private static int Distance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        private void HitPlayer(PlayerActor player)
        {
            var dealt = player.Stats.TakeIncomingDamage(Stats.AttackPower);
            DamagePopup.Spawn(player.transform.position, dealt, new Color(1f, 0.35f, 0.35f));
            HitFeedback.OnPlayerHurt(player, this);
        }

        /// <summary>궁수 → 플레이어로 가는 가는 노란 선을 잠깐 보여준다(화살 느낌만, 투사체 이동은 없음).</summary>
        private void SpawnArrowVisual(Vector3 targetWorld) =>
            SpawnLineVisual(targetWorld, new Color(1f, 0.9f, 0.3f, 0.9f), 0.08f, 0.15f);

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
