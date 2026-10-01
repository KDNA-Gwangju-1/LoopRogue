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
        private const float AttackFlashDuration = 0.18f;
        private const int ExpPerKill = 8;       // 스테이지 배율 곱하기 전 기본값
        private const int ExpPerBossKill = 80;  // 골드처럼 일반 몹의 10배

        // ---- 스킬(처음부터 보유, 턴 쿨타임) - Q 대시, E 회전 베기 ----
        public const int DashRange = 3;
        public const int DashCooldownTurns = 5;
        public const int SpinCooldownTurns = 6;
        public const float SpinDamageRate = 0.8f;

        private static readonly Vector2Int[] SpinOffsets =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(-1, 0), new Vector2Int(1, 0),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        /// <summary>남은 쿨타임(행동 횟수). 0이면 사용 가능. 스킬을 쓰면 N이 되고 이후 행동(이동/공격/대기/스킬) 1번마다 1씩 준다.</summary>
        public int DashCooldown { get; private set; }
        public int SpinCooldown { get; private set; }
        /// <summary>Q를 눌러 대시 방향을 고르는 중(다음 방향키 = 대시, Q 다시 = 취소).</summary>
        public bool IsAimingDash { get; private set; }

        /// <summary>거미줄 속박 - 0보다 크면 이동·대시 불가(공격/회전 베기/대기는 가능). 행동 1번마다 1씩 준다.</summary>
        public int RootedTurns { get; private set; }

        /// <summary>방패병 정면을 때려서 피해가 줄어든 누적 횟수(봇 통계용).</summary>
        public static int ShieldBlockCount;

        /// <summary>거미줄에 맞았을 때(EnemyActor 거미가 부른다).</summary>
        public void ApplyRoot(int turns)
        {
            RootedTurns = Mathf.Max(RootedTurns, turns);
            IsAimingDash = false;
        }

        public LevelSystem Levels { get; private set; }

        /// <summary>적을 때릴 때마다(데미지 적용 직후) 그 적을 알린다 - GameHUD가 상단 타겟 체력바에 쓴다.</summary>
        public event Action<EnemyActor> OnAttackedEnemy;

        private RoomController _room;
        private SpriteRenderer _renderer;
        private Sprite _idleSprite;
        private Sprite _attackSprite;
        private Coroutine _attackFlashRoutine;

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
                    CriticalChanceRate = StatPotionWallet.TotalCriticalChanceBonus(),
                };
                Stats.FullHeal();
                Levels = new LevelSystem(Stats);
            }

            // StoryRPG의 로그(Rogue) 캐릭터 스프라이트(전용 아트가 아직 없어 어쌔신 스프라이트로
            // 폴백 중인 그 그림) - Assets/Resources/PlayerSprite.png(평소)+PlayerAttackSprite.png
            // (공격 순간만 잠깐)로 가져와뒀다. 못 찾으면 예전처럼 단색 사각형으로 안전하게 폴백한다
            // (이 경우 공격 모션도 같이 생략 - 바꿀 스프라이트 자체가 없으므로).
            _idleSprite = Resources.Load<Sprite>("PlayerSprite");
            _attackSprite = Resources.Load<Sprite>("PlayerAttackSprite");

            _renderer = _idleSprite != null
                ? VisualUtil.CreateSpriteVisual(gameObject, _idleSprite, GridConstants.CellSize * 0.9f, sortingOrder: 1)
                : VisualUtil.CreateSquareVisual(gameObject, PlayerColor, GridConstants.CellSize * 0.65f, sortingOrder: 1);
        }

        /// <summary>지금 이동/공격 입력을 받을 수 있는 상태인지(키 입력과 자동 플레이 봇이 같은 조건을 쓴다).</summary>
        public bool CanAct => _room != null && !_room.IsInputLocked && !Levels.IsChoosingUpgrade && !Stats.IsDead && !PauseMenu.BlocksInput;

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

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Vector2Int? direction = null;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
                direction = Vector2Int.up;
            else if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
                direction = Vector2Int.down;
            else if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                direction = Vector2Int.left;
            else if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                direction = Vector2Int.right;

            if (keyboard.qKey.wasPressedThisFrame)
            {
                if (IsAimingDash)
                    IsAimingDash = false;
                else if (RootedTurns > 0)
                    _room.ShowMessage("거미줄에 묶여 대시할 수 없다!");
                else if (DashCooldown > 0)
                    _room.ShowMessage($"대시는 {DashCooldown}턴 뒤에 쓸 수 있다");
                else
                    IsAimingDash = true;
                return;
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                IsAimingDash = false;
                if (SpinCooldown > 0)
                    _room.ShowMessage($"회전 베기는 {SpinCooldown}턴 뒤에 쓸 수 있다");
                else if (!TrySpin())
                    _room.ShowMessage("주변에 적이 없다");
                return;
            }

            if (IsAimingDash)
            {
                if (direction.HasValue)
                {
                    IsAimingDash = false;
                    if (!TryDash(direction.Value))
                        _room.ShowMessage("그쪽으로는 대시할 수 없다");
                }
                return; // 방향 고르는 중엔 대기 키도 무시
            }

            if (direction.HasValue)
                TryAct(direction.Value);
            else if (keyboard.spaceKey.wasPressedThisFrame)
                Wait(); // 스페이스바 = 제자리 대기(한 턴 넘기기)
        }

        private void TryAct(Vector2Int direction)
        {
            var targetPos = GridPos + direction;

            if (!Map.IsInBounds(targetPos) || Map.IsWall(targetPos))
                return; // 방 끝/벽 방향으로 헛눌러도 턴 소모 없음 - 로그라이크 관례.

            var occupant = Map.GetActorAt(targetPos);

            if (RootedTurns > 0 && !(occupant is EnemyActor))
            {
                _room.ShowMessage("거미줄에 묶여 움직일 수 없다! (공격·회전 베기·대기는 가능)");
                return; // 턴 소모 없음
            }

            if (occupant is EnemyActor enemy)
            {
                BeginAction();
                PlayAttackFlash();
                if (StrikeEnemy(enemy, direction, 1f))
                    return; // 다음 방/승리 전환은 이미 끝났다 - 새 방 몹은 이번 턴엔 안 움직인다.
            }
            else if (occupant is RoomEventActor ev)
            {
                // 이벤트 칸 - 발동시키고 그 칸으로 이동(한 턴 소모).
                BeginAction();
                _room.TriggerEvent(ev);
                Map.MoveActor(this, targetPos);
            }
            else if (occupant == null)
            {
                BeginAction();
                Map.MoveActor(this, targetPos);
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
            var damage = Stats.RollAttackDamage(out var isCritical) * damageRate;
            var blocked = !ignoreShield && enemy.IsShieldFront(GridPos);
            if (blocked)
            {
                damage *= EnemyActor.ShieldFrontDamageRate;
                ShieldBlockCount++;
            }
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

        /// <summary>죽은 몹을 플레이어가 잡은 것으로 처리(제거 + 처치 회복 + 경험치 + 골드/방 전환). 방이 넘어갔으면 true.</summary>
        private bool ClaimKill(EnemyActor enemy)
        {
            HitFeedback.OnEnemyKilled(enemy);
            Map.RemoveActor(enemy);
            Destroy(enemy.gameObject);
            Stats.Heal(Stats.MaxHealth * Stats.KillHealRate); // 처치 회복 카드

            // 경험치도 골드처럼 StageScaling 배율을 곱한다(몹은 스테이지마다 세지는데 경험치만
            // 고정이면 뒤로 갈수록 레벨이 안 오름). 보스 처치 경험치는 스테이지 클리어 화면보다
            // 먼저 들어가야 해서 NotifyEnemyDefeated(보스면 곧바로 클리어 처리)보다 앞에서 준다.
            var baseExp = enemy.IsMinion ? 0 : enemy.IsBoss ? ExpPerBossKill : ExpPerKill; // 졸개는 경험치 없음
            var repeat = enemy.IsBoss ? 1f : StageProgress.RepeatRewardMultiplier; // 반복 보상 감소(보스는 제외)
            Levels.AddExp(Mathf.RoundToInt(baseExp * StageScaling.RewardMultiplier(_room.Stage) * (1f + Stats.EffectiveExpBonus) * repeat));

            return _room.NotifyEnemyDefeated(enemy);
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
            DashCooldown = DashCooldownTurns;
            var from = transform.position;
            if (landing != GridPos)
                Map.MoveActor(this, landing);
            HitFeedback.OnDash(this, from);

            if (hit != null)
            {
                PlayAttackFlash();
                if (StrikeEnemy(hit, dir, 1f, isSkill: true))
                    return true;
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
            SpinCooldown = SpinCooldownTurns;
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

            EndTurn();
            return true;
        }

        /// <summary>행동 하나(이동/공격/대기/스킬)가 확정될 때 - 스킬 쿨타임과 거미줄 속박을 1 줄인다.</summary>
        private void BeginAction()
        {
            if (RootedTurns > 0)
                RootedTurns--;
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

        /// <summary>공격 순간만 잠깐 공격 포즈 스프라이트로 바꿨다가 원래 대기 포즈로 되돌린다 -
        /// 애니메이션 클립 없이 프레임 하나만 있어도 "공격했다"는 느낌은 충분히 준다. 연속 공격 시
        /// 이전 되돌리기 코루틴이 남아있으면 새로 시작(StoryRPG SpiritSummonEffect의 알파 되돌리기와
        /// 같은 패턴).</summary>
        private void PlayAttackFlash()
        {
            if (_attackSprite == null || _idleSprite == null)
                return; // 폴백(단색 사각형) 중이면 바꿀 스프라이트 자체가 없다.

            if (_attackFlashRoutine != null)
                StopCoroutine(_attackFlashRoutine);
            _attackFlashRoutine = StartCoroutine(AttackFlashRoutine());
        }

        private IEnumerator AttackFlashRoutine()
        {
            _renderer.sprite = _attackSprite;
            yield return new WaitForSeconds(AttackFlashDuration);
            _renderer.sprite = _idleSprite;
            _attackFlashRoutine = null;
        }
    }
}
