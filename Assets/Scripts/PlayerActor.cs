using System;
using System.Collections;
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
                // 레벨/경험치/스탯을 그대로 복원한다. 스냅샷 자체가 "장비/영약 포함 최종 수치"라
                // 장비/영약을 통째로 다시 더하면 중복이고, 스냅샷 이후 로비에서 새로 산 만큼(차이)만 더한다.
                var addedAttack = RunProgress.CurrentPermanentAttack() - RunProgress.PermanentAttackAtSave;
                var addedHealth = RunProgress.CurrentPermanentHealth() - RunProgress.PermanentHealthAtSave;
                var addedCritical = RunProgress.CurrentPermanentCritical() - RunProgress.PermanentCriticalAtSave;
                Stats = new CharacterStats(RunProgress.MaxHealth + addedHealth, RunProgress.AttackPower + addedAttack)
                {
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
                // 첫 시작(또는 로비를 거치지 않은 평범한 루프 리셋) - 장비 + 영약 보너스를 기본
                // 스탯 위에 얹는다. 셋 다 GoldWallet처럼 영구 저장이라 매 런 시작마다 다시 적용된다.
                var baseHealth = 30f + EquipmentWallet.TotalHealthBonus() + StatPotionWallet.TotalHealthBonus();
                var baseAttack = 6f + EquipmentWallet.TotalAttackBonus() + StatPotionWallet.TotalAttackBonus();
                Stats = new CharacterStats(maxHealth: baseHealth, attackPower: baseAttack)
                {
                    CriticalChanceRate = StatPotionWallet.TotalCriticalChanceBonus(),
                };
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

            if (occupant is EnemyActor enemy)
            {
                PlayAttackFlash();
                var damage = Stats.RollAttackDamage(out var isCritical);
                enemy.Stats.TakeDamage(damage);
                DamagePopup.Spawn(enemy.transform.position, damage, Color.white, isCritical);
                OnAttackedEnemy?.Invoke(enemy);
                Stats.Heal(damage * Stats.LifeStealRate); // 흡혈 카드

                if (enemy.Stats.IsDead)
                {
                    Map.RemoveActor(enemy);
                    Destroy(enemy.gameObject);
                    Stats.Heal(Stats.MaxHealth * Stats.KillHealRate); // 처치 회복 카드

                    // 경험치도 골드처럼 StageScaling 배율을 곱한다(몹은 스테이지마다 세지는데 경험치만
                    // 고정이면 뒤로 갈수록 레벨이 안 오름). 보스 처치 경험치는 스테이지 클리어 화면보다
                    // 먼저 들어가야 해서 NotifyEnemyDefeated(보스면 곧바로 클리어 처리)보다 앞에서 준다.
                    var baseExp = enemy.IsMinion ? 0 : enemy.IsBoss ? ExpPerBossKill : ExpPerKill; // 졸개는 경험치 없음
                    var repeat = enemy.IsBoss ? 1f : StageProgress.RepeatRewardMultiplier; // 반복 보상 감소(보스는 제외)
                    Levels.AddExp(Mathf.RoundToInt(baseExp * StageScaling.RewardMultiplier(_room.Stage) * (1f + Stats.EffectiveExpBonus) * repeat));

                    var roomCleared = _room.NotifyEnemyDefeated(enemy);

                    if (roomCleared)
                        return; // 다음 방/승리 전환은 이미 끝났다 - 새 방 몹은 이번 턴엔 안 움직인다.
                }
            }
            else if (occupant is RoomEventActor ev)
            {
                // 이벤트 칸 - 발동시키고 그 칸으로 이동(한 턴 소모).
                _room.TriggerEvent(ev);
                Map.MoveActor(this, targetPos);
            }
            else if (occupant == null)
            {
                Map.MoveActor(this, targetPos);
            }
            else
            {
                return; // 다른 종류의 점유자는 없다(플레이어는 하나뿐) - 안전망.
            }

            EndTurn();
        }

        /// <summary>제자리 대기 - 아무것도 안 하고 한 턴 넘긴다(보스 저격 2발 같은 "움직이면 맞는" 공격 피하기용).</summary>
        private void Wait() => EndTurn();

        /// <summary>자동 플레이 봇용 - 대기 키를 누른 것과 같다.</summary>
        public void BotWait()
        {
            if (CanAct)
                Wait();
        }

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
