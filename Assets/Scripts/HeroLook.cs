using UnityEngine;

namespace LoopRogue
{
    /// <summary>장비 등급별 용사 그림(11-03 사용자) - 몸 겹 Resources/Sprites/HeroBody{방어구 등급}(투구+갑옷, 투구는 방어구를 따라감)
    /// + 무기 겹 HeroWeapon{무기 등급}을 같은 프레임 번호로 겹친다(SpriteAnimator.SetOverlay). 반지는 그림에 안 나온다. 미장착 = 일반.
    /// 무기가 몸 뒤로 가는 프레임은 WeaponBehind 표(그림 스크립트 hero_gear3.py가 뽑은 weapon_behind.txt 그대로).
    /// 그림이 없으면 예전 "Player" 세트 하나로. 장비가 바뀌면 Version이 올라가고 HeroLookBinder가 다시 입힌다.</summary>
    public static class HeroLook
    {
        public const string LegacySet = "Player";
        private static readonly Color DebrisColor = new Color(0.4f, 0.5f, 0.75f);

        /// <summary>장비가 바뀔 때마다 +1(EquipmentWallet) - 붙어 있는 용사 그림이 다음 프레임에 다시 입는다.</summary>
        public static int Version { get; private set; }

        public static void Bump() => Version++;

        public static bool Available => SpriteAnimator.FirstFrame("HeroBody0") != null && SpriteAnimator.FirstFrame("HeroWeapon0") != null;

        /// <summary>11-04 그림체 옵션(art-style-option 브랜치): 고퀄 용사(Codex 그림 -> Tools/SpriteGen/hq_export.py).
        /// 몸은 칼 없이 방어구 등급별 세트(HeroHQ = 일반, HeroHQ{등급}, 없으면 일반), 칼은 등급별 각도 그림(HeroHQSword{등급}/a_0~63)을
        /// 몸 세트의 손 위치 표(weapon_anchor.txt)대로 매 그림마다 붙인다 - 방어구×칼 조합이 늘어도 그림 수가 안 늘게(11-05 사용자 결정).</summary>
        public const string HqSet = "HeroHQ";
        public static bool IsHq => UseHq;
        private static bool UseHq => GameSettings.Art == GameSettings.ArtStyle.HighQuality && SpriteAnimator.FirstFrame(HqSet) != null;

        public static string HqBodySet
        {
            get
            {
                var g = (int)Appearance.Resolved(Appearance.Part.Armor);
                var set = g == 0 ? HqSet : HqSet + g;
                return g != 0 && SpriteAnimator.FirstFrame(set) == null ? HqSet : set;
            }
        }

        public static string HqSwordSet => $"HeroHQSword{(int)Appearance.Resolved(Appearance.Part.Weapon)}";

        // 11-03 재봉사(Appearance) - 방어구 외형·무기 외형·이펙트를 장착 장비와 따로 고를 수 있다(안 골랐으면 장비 따라감, 미장착 = 일반)
        public static string BodySet => UseHq ? HqBodySet : $"HeroBody{(int)Appearance.Resolved(Appearance.Part.Armor)}";
        public static string WeaponSet => $"HeroWeapon{(int)Appearance.Resolved(Appearance.Part.Weapon)}";

        /// <summary>이펙트 등급(베기·회전·대시·잔상 색) - 재봉사에서 고른 이펙트, 안 골랐으면 장착 무기.</summary>
        public static int WeaponGrade => (int)Appearance.Resolved(Appearance.Part.Effect);

        // kind 0 대기 / 1 걷기 / 2 공격, dir 0 남 / 1 동 / 2 서 / 3 북(SpriteAnimator와 같은 순서), 프레임 8장
        private static readonly string[,] Behind =
        {
            { "00000000", "00000000", "00000000", "00000000" },
            { "00000000", "00000000", "00000000", "00000000" },
            { "01000000", "11000000", "11000000", "00111100" },
        };
        /// <summary>회전 베기 클립(spin_south_0~7, kind 3) - 등을 보이는 5번째 장(4)만 칼이 몸 뒤.</summary>
        private const string SpinBehind = "00001000";

        public static bool WeaponBehind(int kind, int dir, int index)
        {
            if (index < 0 || index >= 8)
                return false;
            if (kind == 3)
                return SpinBehind[index] == '1';
            return kind >= 0 && kind < 3 && dir >= 0 && dir < 4 && Behind[kind, dir][index] == '1';
        }

        /// <summary>회전 베기 용사 스프라이트(HeroBody·HeroWeapon의 spin_south_0~7) 재생 - 그림이 없으면 false(예전처럼 그림 통째 회전).</summary>
        public static bool PlaySpin(SpriteAnimator anim) => anim != null && anim.PlayClip("spin", SpinClipSeconds, 0.02f);

        /// <summary>11-04 대쉬 클립(dash_{방향}_0~7) 재생 시간 - 8장.</summary>
        public const float DashClipSeconds = 0.32f;

        /// <summary>회전 한 바퀴 시간 - 8장. 예전 통째 회전(0.18초)보다 조금 길게 해야 자세가 읽힌다.</summary>
        public const float SpinClipSeconds = 0.3f;

        /// <summary>용사 그림을 붙인다 - renderer는 몸 겹(이미 있는 것), 무기 겹은 자식으로 만든다. 등급 그림이 없으면 예전 세트.</summary>
        public static void Attach(GameObject owner, SpriteAnimator anim, SpriteRenderer renderer)
        {
            if (!Available)
            {
                anim.Setup(LegacySet, renderer, DebrisColor);
                return;
            }
            var binder = owner.GetComponent<HeroLookBinder>() ?? owner.AddComponent<HeroLookBinder>();
            binder.Bind(anim, renderer);
        }

        /// <summary>첫 프레임(그림 만들 때 크기 잡기용) - 등급 그림이 있으면 그 몸 겹, 없으면 예전 세트.</summary>
        public static Sprite FirstFrame() => Available ? SpriteAnimator.FirstFrame(BodySet) : SpriteAnimator.FirstFrame(LegacySet);

        // ---- 11-05 고퀄 칼: 몸 세트의 weapon_anchor.txt 한 줄 = "그림이름 x y 각도번호 뒤?"
        // (x, y = 64칸 그림에서 칼막이 자리(왼쪽 위 기준 도트), 각도번호 0~63 = 위쪽에서 시계 방향 5.625도씩, 뒤? 1이면 몸 뒤)
        private struct SwordAnchor
        {
            public Vector2 Pos;
            public int Angle;
            public bool Behind;
        }

        private const int SwordAngles = 64; // hq_export.py ANGLES와 같게
        private static readonly string[] KindNames = { "idle", "walk", "attack" };
        private static readonly string[] DirNames = { "south", "east", "west", "north" };
        private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, SwordAnchor>> Anchors =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, SwordAnchor>>();
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> Swords = new System.Collections.Generic.Dictionary<string, Sprite>();

        private static System.Collections.Generic.Dictionary<string, SwordAnchor> AnchorTable(string set)
        {
            if (Anchors.TryGetValue(set, out var table))
                return table;
            table = new System.Collections.Generic.Dictionary<string, SwordAnchor>();
            var text = Resources.Load<TextAsset>($"Sprites/{set}/weapon_anchor");
            if (text != null)
                foreach (var line in text.text.Split('\n'))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length < 5)
                        continue;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    table[p[0]] = new SwordAnchor
                    {
                        Pos = new Vector2(float.Parse(p[1], inv), float.Parse(p[2], inv)),
                        Angle = int.Parse(p[3]),
                        Behind = p[4] == "1",
                    };
                }
            Anchors[set] = table;
            return table;
        }

        /// <summary>a = 칼날+칼막이(몸 앞/뒤 표대로), g = 손잡이·폼멜(늘 몸 뒤 - 주먹 사이로만 보인다, 11-06 손과 칼이 떨어져 보이던 것).</summary>
        private static Sprite Sword(string part, int angle)
        {
            var path = $"Sprites/{HqSwordSet}/{part}_{angle}";
            if (!Swords.TryGetValue(path, out var sprite))
                Swords[path] = sprite = Resources.Load<Sprite>(path);
            return sprite;
        }

        /// <summary>칼 겹 아래 손잡이 겹(없으면 만든다) - 칼 겹 자식이라 위치를 따라간다.</summary>
        private static SpriteRenderer Grip(SpriteRenderer weapon)
        {
            var t = weapon.transform.Find("HeroGrip");
            if (t != null)
                return t.GetComponent<SpriteRenderer>();
            var go = new GameObject("HeroGrip");
            go.transform.SetParent(weapon.transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>지금 몸 그림에 맞춰 칼을 붙인다(SpriteAnimator.Shown) - 표에 없는 그림이면 칼을 숨긴다.</summary>
        private static void UpdateHqSword(SpriteAnimator anim, SpriteRenderer body, SpriteRenderer weapon)
        {
            var grip = Grip(weapon);
            weapon.sprite = null;
            grip.sprite = null;
            var kind = anim.ShownKind;
            var kindName = kind >= 0 && kind < 3 ? KindNames[kind] : kind == 3 ? anim.ClipName : null;
            if (kindName == null || !AnchorTable(anim.SetName).TryGetValue($"{kindName}_{DirNames[kindName == "spin" ? 0 : anim.ShownDir]}_{anim.ShownIndex}", out var a))
                return;
            var x = a.Pos.x;
            var angle = a.Angle;
            if (body.flipX)
            {
                x = 64f - x;
                angle = (SwordAngles - angle) % SwordAngles;
            }
            weapon.sprite = Sword("a", angle);
            weapon.transform.localPosition = new Vector3((x - 32f) / 64f, (32f - a.Pos.y) / 64f, 0f); // 몸 그림 가운데 기준, PPU 64
            weapon.flipX = false;
            weapon.color = body.color;
            weapon.enabled = body.enabled;
            weapon.sortingLayerID = body.sortingLayerID;
            weapon.sortingOrder = body.sortingOrder + (a.Behind ? -1 : 1);
            grip.sprite = Sword("g", angle);
            grip.color = body.color;
            grip.enabled = body.enabled;
            grip.sortingLayerID = body.sortingLayerID;
            grip.sortingOrder = body.sortingOrder - 1;
        }

        internal static void Apply(SpriteAnimator anim, SpriteRenderer body, SpriteRenderer weapon)
        {
            anim.Setup(BodySet, body, DebrisColor);
            // 11-06 고퀄 대기 = 16장 숨쉬기·깜빡임(한 바퀴 2.4초). 부드럽게 재생은 장 수와 상관없이 한 바퀴 0.6초라 4배 느리게
            anim.IdleSlow = UseHq ? 4f : 1f;
            anim.StepSlow = UseHq ? 1.3f : 1f; // 11-06 사용자: 인게임 걷기 조금 더 천천히(한 칸 0.18 -> 0.234초)
            if (UseHq)
            {
                anim.SetOverlay(null, null, null); // 칼은 Shown에서 직접
                anim.Shown = () => UpdateHqSword(anim, body, weapon);
                anim.Shown();
            }
            else
            {
                anim.Shown = null;
                weapon.transform.localPosition = Vector3.zero;
                var grip = weapon.transform.Find("HeroGrip");
                if (grip != null)
                    grip.GetComponent<SpriteRenderer>().sprite = null;
                anim.SetOverlay(WeaponSet, weapon, WeaponBehind);
            }
        }
    }

    /// <summary>용사 오브젝트에 붙어 장비가 바뀌면(HeroLook.Version) 몸·무기 겹을 다시 입힌다.</summary>
    public class HeroLookBinder : MonoBehaviour
    {
        private SpriteAnimator _anim;
        private SpriteRenderer _body;
        private SpriteRenderer _weapon;
        private int _version = -1;

        public void Bind(SpriteAnimator anim, SpriteRenderer body)
        {
            _anim = anim;
            _body = body;
            if (_weapon == null)
            {
                var go = new GameObject("HeroWeapon");
                go.transform.SetParent(body.transform, false); // 몸 그림과 같은 크기·위치(부모 배율을 그대로 받는다)
                _weapon = go.AddComponent<SpriteRenderer>();
            }
            Refresh();
        }

        private void Refresh()
        {
            _version = HeroLook.Version;
            HeroLook.Apply(_anim, _body, _weapon);
        }

        private void Update()
        {
            if (_anim != null && _version != HeroLook.Version)
                Refresh();
        }
    }
}
