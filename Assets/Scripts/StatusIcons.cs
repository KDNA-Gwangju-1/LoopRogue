using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>지속 상태 표시 - 조건이 켜져 있는 동안 액터에 반복 이펙트를 붙여둔다(꺼지면 Fx.KeepAlive로 알아서 사라짐).
    /// 플레이어: 보호막(다음 피해 1회 무효) / 반사 부적 / 거미줄 면역(정화제 뒤). 몹: 기절(연막·섬광탄) / 약점 표식.
    /// 자동 플레이 봇 중엔 아무것도 안 한다.</summary>
    public class StatusIcons : MonoBehaviour
    {
        private sealed class Entry
        {
            public string Fx;
            public Func<bool> IsOn;
            public float OffsetCells; // 위로 얼마나(칸 단위) - 머리 위 표시는 양수, 몸·발밑은 0 이하
            public float SizeCells;
            public int SortingOrder;
            public Fx Instance;
            public bool Missing; // 그림 파일이 없음 - 다시 시도하지 않는다
        }

        private readonly List<Entry> _entries = new List<Entry>();

        private void Add(string fx, Func<bool> isOn, float offsetCells, float sizeCells, int sortingOrder = 8) =>
            _entries.Add(new Entry { Fx = fx, IsOn = isOn, OffsetCells = offsetCells, SizeCells = sizeCells, SortingOrder = sortingOrder });

        public static void AttachToPlayer(PlayerActor player)
        {
            var icons = player.gameObject.AddComponent<StatusIcons>();
            icons.Add("status_shield", () => player.Stats != null && player.Stats.BlockNextHit, 0f, 1.25f);
            icons.Add("status_reflect", () => player.ReflectTurns > 0, 0f, 1.4f);
            icons.Add("status_immune", () => player.WebImmuneTurns > 0, -0.42f, 1.2f, sortingOrder: 0);
        }

        public static void AttachToEnemy(EnemyActor enemy)
        {
            var icons = enemy.gameObject.AddComponent<StatusIcons>();
            var head = enemy.IsBoss ? 0.85f : 0.55f;
            icons.Add("status_stun", () => enemy.StunTurns > 0 && !enemy.Stats.IsDead, head, enemy.IsBoss ? 1f : 0.7f);
            icons.Add("status_weak", () => enemy.IsVulnerable && !enemy.Stats.IsDead, head + 0.3f, 0.45f);
        }

        private void LateUpdate()
        {
            if (DamagePopup.Suppressed)
                return;
            foreach (var e in _entries)
            {
                if (e.Instance != null || e.Missing || !e.IsOn())
                    continue;
                var pos = transform.position + Vector3.up * GridConstants.CellSize * e.OffsetCells;
                e.Instance = LoopRogue.Fx.Play(e.Fx, pos, e.SizeCells, fps: 8f, loop: true, sortingOrder: e.SortingOrder, parent: transform);
                if (e.Instance != null)
                    e.Instance.KeepAlive = e.IsOn;
                else
                    e.Missing = LoopRogue.Fx.Frames(e.Fx).Length == 0;
            }
        }
    }
}
