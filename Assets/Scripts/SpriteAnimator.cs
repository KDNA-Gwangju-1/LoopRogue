using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방향별 프레임 애니메이션(대기 반복 + 공격 한 번) - Animator/애니메이션 클립 없이 SpriteRenderer.sprite만 바꾼다
    /// (이 프로젝트는 씬/에셋을 코드로 구성해서 Animator Controller를 만들 수 없다). 프레임은
    /// Resources/Sprites/{세트}/{idle|attack}_{south|east|west|north}_{번호}.png에서 읽어 세트별로 한 번만 캐싱한다.
    /// 방향은 칸이 바뀐 쪽(움직인 방향)이나 공격한 쪽을 따른다. 봇 실행 중(DamagePopup.Suppressed)엔 아무것도 안 한다.</summary>
    public class SpriteAnimator : MonoBehaviour
    {
        private const float IdleFrameSeconds = 0.15f;
        private const float AttackFrameSeconds = 0.07f;

        private static readonly string[] DirNames = { "south", "east", "west", "north" };

        private class FrameSet
        {
            public Sprite[][] Idle = new Sprite[4][];
            public Sprite[][] Attack = new Sprite[4][];
        }

        private static readonly Dictionary<string, FrameSet> Cache = new Dictionary<string, FrameSet>();

        /// <summary>처치 파편 색 - 스프라이트는 틴트가 흰색이라 렌더러 색 대신 이걸 쓴다(ActorJuice.BaseColorOf).</summary>
        public Color DebrisColor = Color.white;

        private FrameSet _set;
        private SpriteRenderer _renderer;
        private GridActor _actor;
        private Vector2Int _lastPos;
        private bool _posKnown;
        private int _dir; // DirNames 인덱스
        private float _time;
        private float _attackUntil = -1f;
        private float _attackStart;

        /// <summary>세트의 대기(남쪽) 첫 프레임 - 없으면 null(그림 파일이 아직 없으면 호출부가 예전 사각형으로).</summary>
        public static Sprite FirstFrame(string setName)
        {
            var set = Load(setName);
            return set.Idle[0] != null && set.Idle[0].Length > 0 ? set.Idle[0][0] : null;
        }

        public void Setup(string setName, SpriteRenderer renderer, Color debrisColor)
        {
            _set = Load(setName);
            _renderer = renderer;
            _actor = GetComponent<GridActor>();
            DebrisColor = debrisColor;
            _time = Random.value * 10f; // 몹끼리 숨쉬기 박자가 딱 맞지 않게
        }

        /// <summary>그 방향으로 공격 모션을 한 번 재생한다.</summary>
        public void PlayAttack(Vector2Int direction)
        {
            if (DamagePopup.Suppressed || _set == null)
                return;
            _dir = DirIndex(direction, _dir);
            _attackStart = Time.time;
            _attackUntil = Time.time + AttackFrameSeconds * Frames(_set.Attack).Length;
        }

        private void LateUpdate()
        {
            if (_set == null || _renderer == null || DamagePopup.Suppressed)
                return;

            if (_actor != null && !_posKnown)
            {
                _lastPos = _actor.GridPos; // Initialize 때는 아직 칸에 안 놓였다 - 첫 프레임 위치부터 센다
                _posKnown = true;
            }
            if (_actor != null && _actor.GridPos != _lastPos)
            {
                _dir = DirIndex(_actor.GridPos - _lastPos, _dir);
                _lastPos = _actor.GridPos;
            }

            if (Time.time < _attackUntil)
            {
                var attack = Frames(_set.Attack);
                if (attack.Length > 0)
                {
                    var i = Mathf.Min(attack.Length - 1, (int)((Time.time - _attackStart) / AttackFrameSeconds));
                    _renderer.sprite = attack[i];
                    return;
                }
            }

            _time += Time.deltaTime;
            var idle = Frames(_set.Idle);
            if (idle.Length > 0)
                _renderer.sprite = idle[(int)(_time / IdleFrameSeconds) % idle.Length];
        }

        /// <summary>지금 방향의 프레임 - 그 방향 그림이 없으면 남쪽 것.</summary>
        private Sprite[] Frames(Sprite[][] byDir) => byDir[_dir] != null && byDir[_dir].Length > 0 ? byDir[_dir] : byDir[0] ?? new Sprite[0];

        private static int DirIndex(Vector2Int d, int fallback)
        {
            if (d == Vector2Int.zero)
                return fallback;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
                return d.x > 0 ? 1 : 2;
            return d.y > 0 ? 3 : 0;
        }

        private static FrameSet Load(string setName)
        {
            if (Cache.TryGetValue(setName, out var set))
                return set;
            set = new FrameSet();
            for (var d = 0; d < DirNames.Length; d++)
            {
                set.Idle[d] = LoadSequence($"Sprites/{setName}/idle_{DirNames[d]}_");
                set.Attack[d] = LoadSequence($"Sprites/{setName}/attack_{DirNames[d]}_");
            }
            Cache[setName] = set;
            return set;
        }

        private static Sprite[] LoadSequence(string prefix)
        {
            var list = new List<Sprite>();
            for (var i = 0; ; i++)
            {
                var sprite = Resources.Load<Sprite>(prefix + i);
                if (sprite == null)
                    break;
                list.Add(sprite);
            }
            return list.ToArray();
        }
    }
}
