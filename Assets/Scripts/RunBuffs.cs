using System.Collections.Generic;

namespace LoopRogue
{
    /// <summary>방 이벤트로 얻는 이번 시도 동안의 효과(숫돌·피의 제단 = 주는 피해 증가, 시간의 모래시계 = 부활 1회).
    /// 저주 계약(Curses)처럼 시도 단위 - 죽거나 로비로 나가거나 층을 넘기면 풀린다(LoopManager가 Reset).</summary>
    public static class RunBuffs
    {
        public const float WhetstoneDamage = 1.15f;
        public const float BloodAltarDamage = 1.35f;
        public const float BloodAltarHealthCost = 0.25f; // 최대 체력 대비
        public const float ReviveHealth = 0.5f;

        private static readonly List<string> Names = new List<string>();

        public static float DamageDealtMultiplier { get; private set; } = 1f;
        public static int ReviveCharges { get; private set; }

        public static void Reset()
        {
            DamageDealtMultiplier = 1f;
            ReviveCharges = 0;
            Names.Clear();
        }

        public static void AddDamage(float multiplier, string name)
        {
            DamageDealtMultiplier *= multiplier;
            Names.Add(name);
        }

        public static void AddRevive()
        {
            ReviveCharges++;
            Names.Add(Loc.T("모래시계"));
        }

        /// <summary>부활이 남아 있으면 하나 쓰고 true.</summary>
        public static bool ConsumeRevive()
        {
            if (ReviveCharges <= 0)
                return false;
            ReviveCharges--;
            Names.Remove(Loc.T("모래시계"));
            return true;
        }

        /// <summary>GameHUD 왼쪽 위 의뢰·저주 줄 아래 - 이번 시도 효과 요약. 없으면 null.</summary>
        public static string TrackerText =>
            Names.Count == 0 ? null : Loc.T("<color=#8CE0FF>효과:</color> ") + string.Join(" / ", Names);
    }
}
