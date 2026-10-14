using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LoopRogue
{
    public enum CodexCategory
    {
        Monster,
        Relic,
        Boss, // 층 보스 10종 - 처음 쓰러뜨리면 기록
    }

    /// <summary>도감 항목 하나 - 발견 전엔 실루엣 + "???" + 해금 힌트, 발견하면 그림·이름·설명이 보인다.</summary>
    public sealed class CodexEntry
    {
        public string Key;            // 저장용 고유 이름(분류 접두어 포함)
        public CodexCategory Category;
        public string Name;
        public Func<string> Description;
        public string Hint;           // 발견 전에 보이는 해금 방법
        public Func<Sprite> Image;    // 없으면 FallbackColor 사각형
        public Color FallbackColor = Color.gray;
        public Func<string> Note;     // 발견 후 덧붙이는 상태(유물 보유 여부 등) - 없으면 null
    }

    /// <summary>도감 - 몬스터 6 / 유물 22 / 보스 10. 발견 기록은 영구 저장(PlayerPrefs). 몬스터는 처음 처치할 때, 유물은 보스 보상
    /// 후보로 처음 보거나 얻을 때 기록된다(각 기록 지점이 Discover를 부른다). 새로 기록되면 Discovered 이벤트로 지금 씬 UI가 알린다.
    /// 그림은 몬스터 = 각 도트 그림 첫 프레임, 유물 = Resources/UI/Relics.</summary>
    public static class Codex
    {
        private const string Key = "LoopRogue_Codex";

        public static event Action<CodexEntry> Discovered;

        private static readonly LocCache<CodexEntry[]> AllCache = new LocCache<CodexEntry[]>(() => Build());

        public static CodexEntry[] All => AllCache.Value; // 언어가 바뀌면 다시 만든다(LocCache)

        private static bool _loaded;
        private static readonly HashSet<string> Found = new HashSet<string>();

        private static CodexEntry[] Build()
        {
            var list = new List<CodexEntry>();

            foreach (RelicType relic in Enum.GetValues(typeof(RelicType)))
            {
                var r = relic;
                var boss = Relics.IsBossRelic(r);
                var hidden = Relics.IsHiddenRelic(r);
                list.Add(new CodexEntry
                {
                    Key = RelicKey(r),
                    Category = CodexCategory.Relic,
                    Name = Relics.Name(r),
                    Description = () => $"{Relics.Description(r)}\n<color=#9AA0AA>{(boss ? Loc.F("{0}층 보스 전용 유물", (int)r + 1) : hidden ? Loc.T("히든 유물 - 보스 보상으로는 나오지 않는다") : Loc.T("일반 유물 - 어느 층 보스 보상에서든 나온다"))}</color>",
                    Hint = boss ? Loc.F("{0}층 보스를 처음 쓰러뜨리면 보상 후보로 나타난다", (int)r + 1)
                        : hidden ? Loc.T("깊은 층 어딘가에 고리를 기억하는 자가 있다고 한다")
                        : Loc.T("층 보스를 처음 쓰러뜨리면 보상 후보로 나타날 수 있다"),
                    Image = () => PixelUi.Get($"UI/Relics/Relic_{r}"),
                    FallbackColor = new Color(0.7f, 0.5f, 0.9f),
                    Note = () => Relics.Has(r) ? Loc.T("<color=#8CE08C>보유 중</color>") : Loc.T("<color=#888888>미보유</color>"),
                });
            }

            AddMonster(list, "Slime", Loc.T("슬라임"), "Slime", new Color(0.45f, 0.8f, 0.3f),
                Loc.T("가장 흔한 근접 몹. 붙어서 때린다. 보스가 부르는 졸개도 이 녀석이다."));
            AddMonster(list, "Ranged", Loc.T("해골 궁수"), "SkeletonArcher", new Color(0.85f, 0.85f, 0.8f),
                Loc.F("같은 줄 {0}칸 안에서 화살을 쏜다. 벽 뒤에선 못 쏘니 대각선으로 다가가자. 몸은 약하다.", EnemyActor.RangedAttackRange));
            AddMonster(list, "Shield", Loc.T("해골 방패병"), "ShieldSoldier", new Color(0.55f, 0.65f, 0.85f),
                Loc.T("방패 정면에서 맞는 피해를 크게 줄인다. 옆이나 뒤로 돌아가거나 회전 베기로 방패를 무시하자."));
            AddMonster(list, "Spider", Loc.T("거미"), "Spider", new Color(0.6f, 0.3f, 0.85f),
                Loc.T("거미줄을 쏴서 몇 턴 동안 이동과 대시를 막는다. 공격과 회전 베기는 된다. 정화제로 바로 풀린다."));
            AddMonster(list, "Bomber", Loc.T("고블린 폭탄병"), "Bomber", new Color(1f, 0.55f, 0.1f),
                Loc.T("심지에 불을 붙이면 다음 턴에 주변 3x3이 터진다(주황 칸). 터지기 전에 잡으면 불발."));
            AddMonster(list, "Healer", Loc.T("고블린 주술사"), EnemyActor.NewKindVisual(EnemyKind.Healer).Set, EnemyActor.NewKindVisual(EnemyKind.Healer).Color,
                Loc.F("멀찍이 떨어져서 {0}칸 안의 다친 아군을 최대 체력의 {1}%만큼 고친다(3턴마다). 몸이 약하니 제일 먼저 쫓아가 잡자. 5층부터.", EnemyActor.HealRange, Mathf.RoundToInt(EnemyActor.HealRate * 100f)));
            AddMonster(list, "Charger", Loc.T("오크 돌격병"), EnemyActor.NewKindVisual(EnemyKind.Charger).Set, EnemyActor.NewKindVisual(EnemyKind.Charger).Color,
                Loc.F("같은 줄 {0}칸 안에 서면 돌진할 줄을 빨갛게 예고하고, 다음 턴에 그 줄을 끝까지 달려 들이받는다(공격력 2배). 줄에서 비키거나 다른 몹 뒤에 서자. 돌진한 뒤엔 한 턴 숨을 고른다. 6층부터.", EnemyActor.ChargeRange));
            AddMonster(list, "Splitter", Loc.T("분열 젤리"), EnemyActor.NewKindVisual(EnemyKind.Splitter).Set, EnemyActor.NewKindVisual(EnemyKind.Splitter).Color,
                Loc.T("튼튼한 젤리. 쓰러뜨리면 작은 조각 둘로 갈라진다. 조각은 약하고 갓 갈라진 턴엔 굳어 있지만 골드와 경험치를 주지 않는다. 회전 베기로 한꺼번에 치우자. 7층부터."));
            AddMonster(list, "Assassin", Loc.T("그림자 도적"), EnemyActor.NewKindVisual(EnemyKind.Assassin).Set, EnemyActor.NewKindVisual(EnemyKind.Assassin).Color,
                Loc.T("조금 떨어져 있으면 연기와 함께 플레이어 등 뒤로 순간이동한다. 순간이동 직후 첫 공격은 1.5배로 아프다. 나타나면 바로 때려서 기습 전에 잡자. 8층부터."));
            AddMonster(list, "Mimic", Loc.T("미믹"), "Mimic", new Color(0.65f, 0.45f, 0.25f),
                Loc.T("보물상자인 척하는 상자 괴물. 밟으면 튀어나온다. 튼튼하고 아프게 물지만, 잡으면 보물상자보다 훨씬 많은 골드와 아이템을 뱉는다."));
            AddMonster(list, "Knight", MemoryKnight.Name, MemoryKnight.SpriteSet, MemoryKnight.TintColor,
                Loc.T("몬스터 중 혼자만 고리를 기억하는 기사. 같은 공격을 연달아 쓰면 읽고 피한다(간파) - 일반 공격·대시·회전 베기를 섞자. ") +
                Loc.T("붙어 있으면 앞 3칸을 예고하고 강하게 베니 한 칸 물러나자. 체력이 낮아지면 한 번 물러나 숨을 고른다. 3층부터 아주 드물게.") + " " +
                Loc.T("마지막으로 쓴 스킬을 기억했다가 따라 한다(기억 재현) - 회전 베기면 주변 8칸, 대시면 같은 줄을 청록으로 예고한다."),
                Loc.T("어딘가에 말을 하는 몬스터가 있다고 한다"));

            AddBoss(list, 1, new Color(0.5f, 0.33f, 0.2f),
                Loc.T("엄니를 앞세워 플레이어 쪽 일직선으로 최대 5칸 돌진한다. 돌진할 줄에서 옆으로 비키자. 돌진한 뒤엔 2턴 동안 비틀거린다 - 때릴 기회.") + " " +
                Loc.T("체력이 절반 아래로 떨어지면 돌기둥 4개를 세우고 8칸까지 돌진한다. 기둥이 서 있는 동안엔 거의 다치지 않으니, 기둥 뒤에 서서 돌진을 기둥에 박게 하자(박으면 3턴 기절).") + " " +
                Loc.F("{0}턴 안에 기둥을 다 부수지 못하면 멧돼지가 광폭해져 즉사한다.", RoomController.BoarPillarTurnLimit));
            AddBoss(list, 2, new Color(0.48f, 0.6f, 0.33f),
                Loc.T("몽둥이로 자기 주변 3x3을 내려찍는다. 예고가 뜨면 한 칸 더 떨어지자.") + (BossBrain.OgreCracksEnabled ? " " +
                Loc.T("체력이 절반 아래로 떨어지면 강타가 바닥에 균열을 남긴다. 오우거가 밟으면 빠져서 2턴 기절하고, 내가 밟으면 1턴 묶인다. 균열 너머에 서서 오우거를 걸어오게 하자.") : "")
                + (BossBrain.OgreHolesEnabled ? " " + Loc.F("체력이 절반 아래로 떨어지면 강타마다 던전이 흔들려 바닥에 구멍이 뚫린다. 구멍에 붙어 그쪽으로 움직이면 한 턴에 메울 수 있다. 구멍이 {0}개가 되면 바닥이 무너져 즉사한다.", RoomController.OgreHoleCollapseCount) : ""));
            AddBoss(list, 3, new Color(0.5f, 0.5f, 0.55f),
                Loc.T("땅을 쳐서 자기 가로줄과 세로줄 전체를 가른다. 골렘과 같은 줄에 서지 말고 대각선 쪽에 붙자.") + " " +
                Loc.F("체력이 80·60·40·20%가 될 때마다 수정이 1·2·3·4개 솟고 천장에서 바위가 떨어진다. 수정은 {0}턴 뒤 폭발해 수정에서 보이는 칸을 전부 날려 버린다(즉사) - 붙어서 깨거나 바위 뒤에 숨자. 폭발을 받아 낸 바위는 깨진다. 40%부터는 십자 베기도 바위를 남긴다.", RoomController.GolemCrystalFuseTurns));
            AddBoss(list, 4, new Color(0.84f, 0.23f, 0.3f),
                Loc.T("예고 없이 붉은 슬라임 졸개를 부르고(최대 2마리), 번갈아 주변 3x3을 짓누른다. 졸개부터 정리하자.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 멀리서 졸개를 불러 킹에게 기어가게 한다. 킹에게 붙은 졸개는 흡수돼 킹을 회복시키고, {0}번 흡수되면 즉사한다. 졸개를 먼저 잡거나 짓누르기에 휘말리게 하면 터지며 킹이 다친다.", RoomController.SlimeAbsorbLimit));
            AddBoss(list, 5, new Color(0.66f, 0.24f, 0.59f),
                Loc.T("포자 파동이 군주에게서 1칸, 3칸 떨어진 고리를 덮친다. 딱 2칸 떨어진 고리가 안전하다. 박자가 빠르다.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 파동에 맞거나 시간이 지날 때마다 감염된다. 감염 {0}이면 즉사 - 파동 뒤에 피는 빛버섯을 밟으면 해독된다.", RoomController.InfectionLimit));
            AddBoss(list, 6, new Color(0.4f, 0.9f, 0.85f),
                Loc.T("두 발 연속 저격. 1발은 플레이어 자리와 대각선 4칸(X자) - 상하좌우로 한 칸 움직여 피한다. 2발은 그 자리의 상하좌우 4칸 - 제자리에서 대기하면 피한다.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 영혼 촛불을 켜고 무적이 된다. 촛불 옆에 서서 저격이 촛불 칸을 덮게 하면 꺼진다. {0}턴 안에 다 못 끄면 즉사.", RoomController.GimmickTurnLimit));
            AddBoss(list, 7, new Color(0.23f, 0.19f, 0.32f),
                Loc.T("쌍단검으로 플레이어 자리를 기준으로 대각선 4줄을 벤다. 상하좌우로 비켜서 대각선에서 벗어나자.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 칼이 안 통하는 그림자 분신을 만들고 무적이 된다. 분신과 대각선 줄에 서면 X자 베기가 분신을 벤다. {0}턴 안에 다 못 지우면 즉사.", RoomController.GimmickTurnLimit));
            AddBoss(list, 8, new Color(0.56f, 0.28f, 0.18f),
                Loc.T("돌진과 도끼 내려찍기(주변 3x3)를 번갈아 쓴다. 돌진 뒤 비틀거릴 때가 기회.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 도끼 내려찍기가 용암 균열을 남긴다. 균열 뒤에 서서 돌진을 유도하면 빠져서 크게 다치고 기절한다. 균열이 {0}개 쌓이면 분출해 즉사.", RoomController.LavaEruptCount));
            AddBoss(list, 9, new Color(0.4f, 1f, 0.6f),
                Loc.T("십자 마법(가로줄·세로줄 전체)과 두 발 저격을 번갈아 쓴다. 같은 줄을 피하고, 저격은 움직이고 → 대기.") + " " +
                Loc.F("체력이 절반 아래로 떨어지면 영혼 항아리에 영혼을 숨기고 무적이 된다. 리치와 같은 줄, 항아리 뒤에 서면 십자 마법이 항아리를 깬다. {0}턴 안에 다 못 깨면 즉사.", RoomController.GimmickTurnLimit));
            AddBoss(list, 10, new Color(0.75f, 0.8f, 0.9f),
                Loc.T("깨진 거울 가면에 다른 보스를 비춰 그 모습으로 변신하고 그 보스의 패턴을 쓴다(멧돼지·오우거·골렘·슬라임 킹·버섯 군주·") +
                Loc.T("망령 궁수·암살자 중 무작위, 같은 패턴은 연달아 안 씀). 변신한 모습을 보고 다음 패턴을 읽자.") + " " +
                Loc.T("본모습일 때는 거울 파편을 쓴다 - 주변에 4번의 공격 칸을 한꺼번에 보여주고(칸의 숫자 = 몇 번째에 터지는지) 순서대로 터뜨린다. 매번 서 있는 칸이 포함되니 숫자를 읽고 길을 짜서 움직이자.") + " " +
                Loc.T("다른 보스를 비추는 동안 많이 때리면 머리 위 거울에 금이 가다가 깨진다 - 깨지면 본모습으로 돌아오고, 예고한 패턴이 취소되고, 2턴 동안 비틀거린다."));

            return list.ToArray();
        }

        private static void AddBoss(List<CodexEntry> list, int stage, Color color, string description) =>
            list.Add(new CodexEntry
            {
                Key = BossKey(stage),
                Category = CodexCategory.Boss,
                Name = BossBrain.BossName(stage),
                Description = () => Loc.F("{0}\n<color=#9AA0AA>{1}층 보스 · 패턴: ", description, stage) +
                                    $"{string.Join(", ", BossBrain.StagePatterns(stage).Select(BossBrain.PatternName))}</color>",
                Hint = Loc.F("{0}층 보스를 처음 쓰러뜨리면 기록된다", stage),
                Image = () => SpriteAnimator.FirstFrame($"Boss{stage}"),
                FallbackColor = color,
            });

        public static string BossKey(int stage) => "B:" + stage;

        private static void AddMonster(List<CodexEntry> list, string key, string name, string spriteSet, Color color, string description, string hint = null) =>
            list.Add(new CodexEntry
            {
                Key = "M:" + key,
                Category = CodexCategory.Monster,
                Name = name,
                Description = () => description,
                Hint = hint ?? Loc.T("처음 처치하면 기록된다"),
                Image = () => SpriteAnimator.FirstFrame(spriteSet),
                FallbackColor = color,
            });

        public static string RelicKey(RelicType r) => "R:" + r;

        /// <summary>처치한 몹의 도감 키 - 졸개(= 슬라임 모습)도 슬라임으로, 층 보스는 보스 도감(변신 중인 10층 보스도 10층으로).</summary>
        public static string MonsterKey(EnemyActor e) =>
            e.IsBoss ? BossKey(e.BossStage) : e.IsMimic ? "M:Mimic" : e.IsKnight ? "M:Knight" : e.IsMinion || e.Kind == EnemyKind.Melee ? "M:Slime" : "M:" + e.Kind;

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            Found.Clear();
            foreach (var k in PlayerPrefs.GetString(Key, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                Found.Add(k);
        }

        public static bool IsFound(CodexEntry entry)
        {
            EnsureLoaded();
            return Found.Contains(entry.Key);
        }

        public static int FoundCount(CodexCategory? category = null)
        {
            EnsureLoaded();
            return All.Count(e => (category == null || e.Category == category) && Found.Contains(e.Key));
        }

        public static int TotalCount(CodexCategory? category = null) => All.Count(e => category == null || e.Category == category);

        /// <summary>처음 보면 기록하고 알린다. 이미 기록됐으면 아무것도 안 한다.</summary>
        public static void Discover(string key)
        {
            EnsureLoaded();
            var entry = All.FirstOrDefault(e => e.Key == key);
            if (entry == null || !Found.Add(key))
                return;
            PlayerPrefs.SetString(Key, string.Join(",", Found));
            Discovered?.Invoke(entry);
            Achievements.Check(); // 도감 업적
        }

        /// <summary>도감이 생기기 전 저장 - 이미 가진 유물은 조용히(알림 없이) 기록한다.</summary>
        public static void SyncOwned()
        {
            EnsureLoaded();
            var changed = false;
            foreach (var r in Relics.OwnedRelics())
                changed |= Found.Add(RelicKey(r));
            // 보스 도감이 생기기 전에 이미 넘어간 층의 보스(지금 층보다 앞 층 = 그 층 보스를 잡았다)
            for (var s = 1; s < StageProgress.CurrentStage && s <= StageProgress.MaxStage; s++)
                changed |= Found.Add(BossKey(s));
            if (changed)
                PlayerPrefs.SetString(Key, string.Join(",", Found));
        }
    }
}
