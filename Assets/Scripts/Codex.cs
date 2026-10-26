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
            AddMonster(list, "Sentinel", Loc.T("반격병"), EnemyActor.NewKindVisual(EnemyKind.Sentinel).Set, EnemyActor.NewKindVisual(EnemyKind.Sentinel).Color,
                Loc.T("두 턴에 한 번 방패를 세워 반격 자세를 잡는다(몸에 방패 빛). 그때 때리면 피해를 거의 막고 공격력 2배로 되받아친다 - 일반 공격·대시·회전 베기 모두. 자세를 잡은 턴엔 한 칸 물러나거나 기다렸다가, 자세를 푼 틈에 때리자. 4층부터."));
            AddMonster(list, "Caster", Loc.T("주문술사"), EnemyActor.NewKindVisual(EnemyKind.Caster).Set, EnemyActor.NewKindVisual(EnemyKind.Caster).Color,
                Loc.F("플레이어를 본 순간부터 머리 위 숫자를 세며 주문을 외운다({0}턴). 1이 되면 내 둘레 3x3을 예고하고, 0이 되면 그 칸이 폭발한다 - 맞으면 즉사급. 몸이 약하니 숫자가 끝나기 전에 뛰어가 잡거나, 대시로 빠지거나 보호막으로 막자. 6층부터, 방에 한 마리까지.", EnemyActor.CasterChantTurns));
            AddMonster(list, "Mimic", Loc.T("미믹"), "Mimic", new Color(0.65f, 0.45f, 0.25f),
                Loc.T("보물상자인 척하는 상자 괴물. 밟으면 튀어나온다. 튼튼하고 아프게 물지만, 잡으면 보물상자보다 훨씬 많은 골드와 아이템을 뱉는다."));
            // 기억하는 기사·층 보스 설명은 공략 대신 스토리 떡밥(10-26 사용자 - 기믹 규칙은 직접 부딪혀 알아내게, 남기는 건 기믹 게이지 UI뿐)
            AddMonster(list, "Knight", MemoryKnight.Name, MemoryKnight.SpriteSet, MemoryKnight.TintColor,
                Loc.T("몬스터 가운데 혼자만 고리를 기억하는 기사. 그가 지키는 것이 문인지 기억인지는 아무도 모른다. 투구 틈의 빛은 벽화 속 꺼져 가는 등불과 같은 색이다.\n<i>\"다른 놈들은 잊는다. 나는 잊지 못한다.\"</i>"),
                Loc.T("어딘가에 말을 하는 몬스터가 있다고 한다"));

            AddBoss(list, 1, new Color(0.5f, 0.33f, 0.2f),
                Loc.T("고리의 첫 문을 지키는 짐승. 앞만 보고 내달리는 버릇은 누구에게서 배웠을까. 쓰러진 자리에 부서진 돌기둥 조각이 남아 있다 - 어느 집 대들보처럼 보인다.\n<i>\"앞만 보고 달렸다. 그래서 늦었다.\"</i>"));
            AddBoss(list, 2, new Color(0.48f, 0.6f, 0.33f),
                Loc.T("바닥을 내려칠 때마다 아래에서 무언가 무너지는 소리가 난다. 오우거는 부서진 것을 메우는 법을 모른다. 그래서 계속 부순다.\n<i>\"무너진 날은 다시 메울 수 있다고 믿었다.\"</i>"));
            AddBoss(list, 3, new Color(0.5f, 0.5f, 0.55f),
                Loc.T("가슴에 박힌 수정은 누군가의 눈물이 굳은 것이라 한다. 골렘은 그 빛을 지키려고 스스로 돌이 되었다.\n<i>\"울지 않으려고 돌이 되었다. 그래도 금은 간다.\"</i>"));
            AddBoss(list, 4, new Color(0.84f, 0.23f, 0.3f),
                Loc.T("왕관만은 녹지 않았다. 갈라지고 또 갈라져도, 킹은 자기가 하나였던 때를 기억하려 애쓴다.\n<i>\"나눠 지면 가벼워질 줄 알았다.\"</i>"));
            AddBoss(list, 5, new Color(0.66f, 0.24f, 0.59f),
                Loc.T("포자를 들이마시면 보고 싶은 것이 보인다고 한다. 대개는 다시 오지 않을 그날의 얼굴이다.\n<i>\"꿈속에서는 늘 제때 도착했다.\"</i>"));
            AddBoss(list, 6, new Color(0.4f, 0.9f, 0.85f),
                Loc.T("한 번도 과녁을 놓친 적 없다는 궁수. 가장 중요했던 단 한 번만 빼고. 꺼진 촛불 밑동마다 이름이 하나씩 새겨져 있다.\n<i>\"그때 쏘았다면.\"</i>"));
            AddBoss(list, 7, new Color(0.23f, 0.19f, 0.32f),
                Loc.T("기사의 몸에서 떨어져 나온 그림자 가운데 가장 빠른 것. 녀석이 세는 박자는 어느 문 앞에서 멎은 심장 소리와 꼭 같다고 한다.\n<i>\"한 걸음만 빨랐다면.\"</i>"));
            AddBoss(list, 8, new Color(0.56f, 0.28f, 0.18f),
                Loc.T("들어온 자가 나가는 길을 잊을수록 미궁은 좁아진다. 소머리 문지기는 벽이 다 닫히기를 기다린다. 그 벽을 쌓은 손은 문지기의 것이 아니다.\n<i>\"벽을 세운 건 나였다.\"</i>"));
            AddBoss(list, 9, new Color(0.4f, 1f, 0.6f),
                Loc.T("죽음을 미뤄 온 마법사. 그의 낙인은 본래 다른 누군가의 몫이었다고 한다. 그 몫을 넘겨받은 자가 누구였는지, 리치는 끝내 말하지 않는다.\n<i>\"끝을 미루는 법은 가르쳐 주었다. 끝내는 법은 나도 모른다.\"</i>"));
            AddBoss(list, 10, new Color(0.75f, 0.8f, 0.9f),
                Loc.T("고리를 만든 자. 깨진 거울 가면에는 지금껏 쓰러뜨린 문지기들의 얼굴이 차례로 비친다. 가면 아래 얼굴을 본 자는 없다 - 아직은.\n<i>\"낯익은 얼굴이군. 거울 속에서 매일 보던 얼굴이다.\"</i>"));

            return list.ToArray();
        }

        private static void AddBoss(List<CodexEntry> list, int stage, Color color, string description) =>
            list.Add(new CodexEntry
            {
                Key = BossKey(stage),
                Category = CodexCategory.Boss,
                Name = BossBrain.BossName(stage),
                Description = () => Loc.F("{0}\n<color=#9AA0AA>{1}층 보스</color>", description, stage),
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
