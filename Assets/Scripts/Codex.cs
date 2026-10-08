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
        Boss, // 아직 항목 없음 - 로비 도감 메뉴에 "준비 중"으로만 보인다
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

    /// <summary>도감 - 몬스터 5 / 유물 22(보스 도감은 준비 중). 발견 기록은 영구 저장(PlayerPrefs). 몬스터는 처음 처치할 때, 유물은 보스 보상
    /// 후보로 처음 보거나 얻을 때 기록된다(각 기록 지점이 Discover를 부른다). 새로 기록되면 Discovered 이벤트로 지금 씬 UI가 알린다.
    /// 그림은 몬스터 = 각 도트 그림 첫 프레임, 유물 = Resources/UI/Relics.</summary>
    public static class Codex
    {
        private const string Key = "LoopRogue_Codex";

        public static event Action<CodexEntry> Discovered;

        public static readonly CodexEntry[] All = Build();

        private static bool _loaded;
        private static readonly HashSet<string> Found = new HashSet<string>();

        private static CodexEntry[] Build()
        {
            var list = new List<CodexEntry>();

            foreach (RelicType relic in Enum.GetValues(typeof(RelicType)))
            {
                var r = relic;
                var boss = Relics.IsBossRelic(r);
                list.Add(new CodexEntry
                {
                    Key = RelicKey(r),
                    Category = CodexCategory.Relic,
                    Name = Relics.Name(r),
                    Description = () => $"{Relics.Description(r)}\n<color=#9AA0AA>{(boss ? $"{(int)r + 1}층 보스 전용 유물" : "일반 유물 - 어느 층 보스 보상에서든 나온다")}</color>",
                    Hint = boss ? $"{(int)r + 1}층 보스를 처음 쓰러뜨리면 보상 후보로 나타난다" : "층 보스를 처음 쓰러뜨리면 보상 후보로 나타날 수 있다",
                    Image = () => PixelUi.Get($"UI/Relics/Relic_{r}"),
                    FallbackColor = new Color(0.7f, 0.5f, 0.9f),
                    Note = () => Relics.Has(r) ? "<color=#8CE08C>보유 중</color>" : "<color=#888888>미보유</color>",
                });
            }

            AddMonster(list, "Slime", "슬라임", "Slime", new Color(0.45f, 0.8f, 0.3f),
                "가장 흔한 근접 몹. 붙어서 때린다. 보스가 부르는 졸개도 이 녀석이다.");
            AddMonster(list, "Ranged", "해골 궁수", "SkeletonArcher", new Color(0.85f, 0.85f, 0.8f),
                $"같은 줄 {EnemyActor.RangedAttackRange}칸 안에서 화살을 쏜다. 벽 뒤에선 못 쏘니 대각선으로 다가가자. 몸은 약하다.");
            AddMonster(list, "Shield", "해골 방패병", "ShieldSoldier", new Color(0.55f, 0.65f, 0.85f),
                "방패 정면에서 맞는 피해를 크게 줄인다. 옆이나 뒤로 돌아가거나 회전 베기로 방패를 무시하자.");
            AddMonster(list, "Spider", "거미", "Spider", new Color(0.6f, 0.3f, 0.85f),
                "거미줄을 쏴서 몇 턴 동안 이동과 대시를 막는다. 공격과 회전 베기는 된다. 정화제로 바로 풀린다.");
            AddMonster(list, "Bomber", "고블린 폭탄병", "Bomber", new Color(1f, 0.55f, 0.1f),
                "심지에 불을 붙이면 다음 턴에 주변 3x3이 터진다(주황 칸). 터지기 전에 잡으면 불발.");
            // 층 보스는 보스 도감(준비 중)으로 따로 - 그림이 생기면 CodexCategory.Boss 항목으로 추가한다.

            return list.ToArray();
        }

        private static void AddMonster(List<CodexEntry> list, string key, string name, string spriteSet, Color color, string description) =>
            list.Add(new CodexEntry
            {
                Key = "M:" + key,
                Category = CodexCategory.Monster,
                Name = name,
                Description = () => description,
                Hint = "처음 처치하면 기록된다",
                Image = () => SpriteAnimator.FirstFrame(spriteSet),
                FallbackColor = color,
            });

        public static string RelicKey(RelicType r) => "R:" + r;

        /// <summary>처치한 몹의 도감 키 - 졸개(= 슬라임 모습)도 슬라임으로, 보스는 아직 항목이 없어 기록되지 않는다.</summary>
        public static string MonsterKey(EnemyActor e) =>
            e.IsBoss ? "M:Boss" : e.IsMinion || e.Kind == EnemyKind.Melee ? "M:Slime" : "M:" + e.Kind;

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
            if (changed)
                PlayerPrefs.SetString(Key, string.Join(",", Found));
        }
    }
}
