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

        private static int GradeIndex(ItemSlot slot) => (int)(EquipmentWallet.GetEquipped(slot) ?? ItemGrade.Common);

        public static string BodySet => $"HeroBody{GradeIndex(ItemSlot.Armor)}";
        public static string WeaponSet => $"HeroWeapon{GradeIndex(ItemSlot.Weapon)}";

        /// <summary>그 자리 무기 등급(미장착 = 일반) - 베기 이펙트 고르기용.</summary>
        public static int WeaponGrade => GradeIndex(ItemSlot.Weapon);

        // kind 0 대기 / 1 걷기 / 2 공격, dir 0 남 / 1 동 / 2 서 / 3 북(SpriteAnimator와 같은 순서), 프레임 8장
        private static readonly string[,] Behind =
        {
            { "00000000", "00000000", "00000000", "00000000" },
            { "00000000", "00000000", "00000000", "00000000" },
            { "01000000", "11000000", "11000000", "00111100" },
        };

        public static bool WeaponBehind(int kind, int dir, int index) =>
            kind >= 0 && kind < 3 && dir >= 0 && dir < 4 && index >= 0 && index < 8 && Behind[kind, dir][index] == '1';

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

        internal static void Apply(SpriteAnimator anim, SpriteRenderer body, SpriteRenderer weapon)
        {
            anim.Setup(BodySet, body, DebrisColor);
            anim.SetOverlay(WeaponSet, weapon, WeaponBehind);
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
