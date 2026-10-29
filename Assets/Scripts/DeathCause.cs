using System.Collections.Generic;

namespace LoopRogue
{
    /// <summary>사망 대사(10-27 사용자 "슬라임 점액에 기도가 막혀 죽었다" 식으로, 원인에 맞게) - 플레이어에게 피해를 주거나 기믹으로 즉사시키는
    /// 곳마다 Set으로 원인을 적어 두고, 죽으면(LoopManager.OnPlayerDied) 마지막 원인의 대사를 사망 창 제목과 플레이 기록에 쓴다.
    /// 원인 키가 없거나 모르는 키면 "쓰러져 죽었다".</summary>
    public static class DeathCause
    {
        private static string _key;

        public static void Set(string key) => _key = key;

        /// <summary>몹·보스가 직접 때렸을 때(근접·원거리 평타). 보스는 층별, 일반 몹은 종류별.</summary>
        public static void SetEnemy(EnemyActor enemy, int stage)
        {
            if (enemy == null)
                return;
            _key = enemy.IsBoss ? "boss_" + stage
                : enemy.IsMinion ? "minion"
                : enemy.IsMimic ? "mimic"
                : enemy.IsKnight ? "knight_slash"
                : enemy.IsSplitChild ? "split_child"
                : enemy.IsSlimeTurret ? "slime_turret"
                : enemy.Kind switch
                {
                    EnemyKind.Melee => "slime",
                    EnemyKind.Ranged => "archer",
                    EnemyKind.Shield => "shield",
                    EnemyKind.Spider => "spider",
                    EnemyKind.Bomber => "bomber",
                    EnemyKind.Healer => "shaman",
                    EnemyKind.Charger => "charger",
                    EnemyKind.Splitter => "splitter",
                    EnemyKind.Assassin => "rogue",
                    EnemyKind.Sentinel => "sentinel",
                    EnemyKind.Caster => "caster",
                    _ => null,
                };
        }

        public static void SetPattern(BossPatternType type) => _key = "pattern_" + type;

        /// <summary>지금 원인의 사망 대사(지금 언어).</summary>
        public static string Line => Loc.T(_key != null && Lines.TryGetValue(_key, out var line) ? line : "쓰러져 죽었다");

        public static string Key => _key;

        private static readonly Dictionary<string, string> Lines = new Dictionary<string, string>
        {
            // 일반 몹
            ["slime"] = "슬라임 점액에 기도가 막혀 죽었다",
            ["shield"] = "해골 방패병의 방패가 머리에 꽂혀 죽었다",
            ["splitter"] = "분열체 속에 빨려 들어가 녹아 죽었다",
            ["split_child"] = "작은 조각들에게 조금씩 뜯어먹혀 죽었다",
            ["rogue"] = "등 뒤에 나타난 도적에게 목이 베여 죽었다",
            ["mimic"] = "보물상자인 줄 알고 열었다가 먹혀 죽었다",
            ["shaman"] = "주술사의 지팡이에 정수리를 맞아 죽었다",
            ["sentinel"] = "반격병의 창끝에 꿰뚫려 죽었다",
            ["sentinel_counter"] = "방패를 든 반격병을 때렸다가 되받아쳐져 죽었다",
            ["archer"] = "해골 궁수의 화살이 이마에 꽂혀 죽었다",
            ["spider"] = "거미줄에 칭칭 감긴 채 말라 죽었다",
            ["charger"] = "오크의 돌진에 받혀 벽에 부딪혀 죽었다",
            ["bomber"] = "고블린 폭탄병과 함께 터져 죽었다",
            ["caster"] = "8턴짜리 주문을 끝까지 들어 주다 불타 죽었다",
            ["elite_charge"] = "정예가 자세를 잡는 걸 보고도 그 자리에 서 있다 짓뭉개져 죽었다",
            ["elite_pierce"] = "정예 궁수가 보여 준 화살길 위에 그대로 서 있다 꿰뚫려 죽었다",
            ["shield_charge"] = "정예 방패병의 방패 돌진에 깔려 죽었다",
            ["knight_slash"] = "기사가 기억해 둔 내 버릇 그대로 베여 죽었다",
            ["knight_echo"] = "기사의 잔상이 지나가는 길목에서 베여 죽었다",
            // 보스 평타
            ["boss_1"] = "멧돼지 엄니에 걸려 하늘을 날다 떨어져 죽었다",
            ["boss_2"] = "오우거의 몽둥이에 바닥까지 박혀 죽었다",
            ["boss_3"] = "골렘의 돌주먹에 종잇장처럼 납작해져 죽었다",
            ["boss_4"] = "슬라임 킹에게 깔려 숨이 막혀 죽었다",
            ["boss_5"] = "버섯 군주의 포자를 한가득 들이마셔 죽었다",
            ["boss_6"] = "망령 궁수의 손끝에 영혼이 빠져나가 죽었다",
            ["boss_7"] = "그림자 암살자가 언제 왔는지도 모른 채 베여 죽었다",
            ["boss_8"] = "미노타우로스의 도끼에 두 동강 나 죽었다",
            ["boss_9"] = "리치의 손짓 한 번에 생명이 빨려 죽었다",
            ["boss_10"] = "거울 가면에 비친 내 얼굴을 보다 베여 죽었다",
            // 보스 패턴
            ["pattern_Slam"] = "내려찍기에 바닥과 하나가 되어 죽었다",
            ["pattern_Charge"] = "달려오는 보스를 정면으로 받아 내다 죽었다",
            ["pattern_Cross"] = "십자 베기의 한가운데 서 있다가 베여 죽었다",
            ["pattern_Ring"] = "퍼져 나가는 파동에 휩쓸려 죽었다",
            ["pattern_RingInverse"] = "안전하다고 믿은 칸이 역파동에 터져 죽었다",
            ["pattern_TripleWave"] = "세 번째 파동까지 버티지 못하고 죽었다",
            ["pattern_Snipe"] = "저격 화살이 정확히 내 자리에 떨어져 죽었다",
            ["pattern_SnipeFollowUp"] = "첫 화살은 피했는데 두 번째 화살에 맞아 죽었다",
            ["pattern_Diagonal"] = "X자 칼날이 교차하는 자리에서 베여 죽었다",
            ["pattern_Encircle"] = "사방이 한꺼번에 터져 빠져나가지 못하고 죽었다",
            ["pattern_Zigzag"] = "지그재그 칼날이 꺾이는 자리에서 베여 죽었다",
            ["pattern_AxeWave"] = "갈라지는 바닥과 함께 갈라져 죽었다",
            ["pattern_Checker"] = "체스판의 틀린 색 칸을 밟아 죽었다",
            ["pattern_SoulOrbs"] = "터지는 영혼 구체에 혼이 흩어져 죽었다",
            ["pattern_Jump"] = "하늘에서 떨어진 슬라임 킹에게 깔려 죽었다",
            ["pattern_MirrorShards"] = "쏟아지는 거울 파편에 수없이 찔려 죽었다",
            ["minion"] = "보스의 졸개들에게 둘러싸여 맞아 죽었다",
            ["slime_turret"] = "벽에 붙은 슬라임의 점액 파동에 휩쓸려 죽었다",
            ["doom_orb"] = "낙인의 영혼 구체에 영혼이 꿰뚫려 죽었다",
            // 기믹 즉사
            ["g1_boar"] = "광폭해진 멧돼지에게 짓밟혀 죽었다",
            ["g2_collapse"] = "무너진 바닥과 함께 던전 아래로 떨어져 죽었다",
            ["g3_crystal"] = "공명 수정의 빛에 몸이 부서져 죽었다",
            ["g4_slime"] = "바닥을 뒤덮은 점액에 통째로 삼켜져 죽었다",
            ["g5_infect"] = "온몸에 버섯이 피어 군주의 정원이 되어 죽었다",
            ["g6_candles"] = "쏟아지는 화살비에 고슴도치가 되어 죽었다",
            ["g6_targets"] = "영혼 과녁이 모두 꿰뚫리며 함께 꿰뚫려 죽었다",
            ["g7_clones"] = "한꺼번에 덮친 그림자들에게 찢겨 죽었다",
            ["g7_beat"] = "박자를 놓쳐 그림자 칼날에 갈려 죽었다",
            ["g8_crush"] = "조여 오는 벽 사이에 끼어 납작해져 죽었다",
            ["g8_walls"] = "맞붙은 사방의 벽에 짓눌려 죽었다",
            ["g9_doom"] = "죽음의 낙인이 타올라 재가 되어 죽었다",
            ["g9_dodge"] = "리치의 공격을 끝까지 피하려다 회피 낙인에 영혼이 찢겨 죽었다",
            ["g10_laststand"] = "기사의 마지막 일격을 받아 내지 못하고 죽었다",
        };
    }
}
