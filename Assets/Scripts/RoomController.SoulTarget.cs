using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>6층 망령 궁수 두 번째 기믹 "영혼 과녁"(10-23 사용자) - 촛불 뒤 체력 SoulTargetHealthRatio 아래에서 한 번.
    /// 플레이어의 영혼이 과녁 SoulTargetCount개로 흩어져 방 곳곳에 박힌다. SoulTargetTurns턴 동안 궁수는 단단해지고(받는 피해 90% 감소)
    /// 저격(X자 → 마름모)만 쏜다 - 저격이 덮은 과녁은 부서지고, 다 부서지면 즉사. 버티면 궁수가 기절하고
    /// 남은 과녁 하나당 궁수가 받는 피해 +SoulTargetBonusPerTarget(방 끝까지). 저격은 나를 노리니 과녁에서 떨어져 서야 지킨다.</summary>
    public partial class RoomController
    {
        public const int SoulTargetCount = 5;
        public const int SoulTargetTurns = 30;
        public const float SoulTargetBonusPerTarget = 0.2f;
        private static readonly Color SoulTargetColor = new Color(0.55f, 0.95f, 1f);

        private readonly Dictionary<Vector2Int, GameObject> _soulTargets = new Dictionary<Vector2Int, GameObject>();
        private bool _soulTargetActive;
        private int _soulTargetTurnsLeft;
        private float _soulTargetBonus;

        public bool SoulTargetActive => _gimmickStage == 6 && _soulTargetActive;
        public IReadOnlyCollection<Vector2Int> SoulTargets => _soulTargets.Keys;
        /// <summary>버틴 뒤 궁수가 받는 피해 추가 비율(남은 과녁 × 20%) - BossBrain.ArmorMultiplier.</summary>
        public float SoulTargetBonus => _gimmickStage == 6 ? _soulTargetBonus : 0f;

        /// <summary>BossBrain이 6층 체력 25% 아래에서 한 번 부른다 - 촛불 기믹(_gimmickStage 6)은 그대로 이어 쓴다.</summary>
        public void BeginSoulTarget(EnemyActor boss)
        {
            _gimmickStage = 6;
            _gimmickBoss = boss;
            _gimmickJustStarted = true;
            _soulTargetActive = true;
            _soulTargetTurnsLeft = SoulTargetTurns;
            _soulTargetBonus = 0f;
            var c = new GimmickCutscene();
            for (var i = 0; i < SoulTargetCount; i++)
                c.Steps.Add(PropStep(_soulTargets, () =>
                {
                    var before = _soulTargets.Count;
                    SpawnFloorProps(_soulTargets, before + 1, "Soul_Target", SoulTargetColor, boss, 2);
                    return _soulTargets.Count > before;
                }, "soul_target_rise"));
            c.Finish = UpdateGimmickLabel; // 시작 안내는 없음(10-21 사용자 - 힌트 없이 직접 깨기, 규칙은 도감에)
            UpdateGimmickLabel();
            PlayGimmickCutscene(c);
        }

        /// <summary>저격이 덮은 과녁을 부순다 - 다 부서지면 즉사(사망 처리는 몹 턴 루프가).</summary>
        private void SnipeSoulTargets(IEnumerable<Vector2Int> tiles)
        {
            var hit = tiles.Where(_soulTargets.ContainsKey).ToList();
            if (hit.Count == 0)
                return;
            foreach (var p in hit)
            {
                if (HitFeedback.Enabled && _soulTargets[p] != null)
                    Fx.Play("soul_target_break", _soulTargets[p].transform.position, 1.2f, fps: 14f, sortingOrder: 6);
                RemoveFloorProp(_soulTargets, p, SoulTargetColor);
            }
            if (HitFeedback.Enabled && _player != null)
                ActorJuice.Get(_player).Flash(SoulTargetColor);
            if (_soulTargets.Count > 0)
            {
                UpdateGimmickLabel();
                return;
            }
            DeathCause.Set("g6_targets"); // 사망 대사
            ShowMessage(Loc.T("<color=#FF4040>영혼 과녁이 모두 꿰뚫렸다!</color>"));
            _soulTargetActive = false;
            GimmickKills[6]++;
            GimmickDeathFx();
            KillPlayerInstantly();
        }

        /// <summary>몹 턴 끝마다 - 다 버티면 궁수 기절 + 남은 과녁만큼 받는 피해 증가.</summary>
        private void TickSoulTarget()
        {
            if (--_soulTargetTurnsLeft > 0)
            {
                return;
            }
            _soulTargetActive = false;
            var left = _soulTargets.Count;
            _soulTargetBonus = left * SoulTargetBonusPerTarget;
            foreach (var p in _soulTargets.Keys.ToList())
            {
                if (HitFeedback.Enabled && _soulTargets[p] != null)
                    Fx.Play("soul_target_return", _soulTargets[p].transform.position, 1.2f, fps: 14f, sortingOrder: 6);
                RemoveFloorProp(_soulTargets, p, SoulTargetColor);
            }
            OnGimmickBroken();
        }

        private void ClearSoulTarget()
        {
            foreach (var go in _soulTargets.Values)
                if (go != null)
                    Destroy(go);
            _soulTargets.Clear();
            _soulTargetActive = false;
            _soulTargetTurnsLeft = 0;
            _soulTargetBonus = 0f;
        }

        private string SoulTargetLabelText(out bool danger)
        {
            danger = _soulTargets.Count <= 1;
            return Loc.F("과녁 {0} · {1}턴", _soulTargets.Count, _soulTargetTurnsLeft);
        }
    }
}
