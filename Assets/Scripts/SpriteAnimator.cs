using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>방향별 프레임 애니메이션(대기 반복 + 공격 한 번 + 걷기) - Animator/애니메이션 클립 없이 SpriteRenderer.sprite만 바꾼다
    /// (이 프로젝트는 씬/에셋을 코드로 구성해서 Animator Controller를 만들 수 없다). 프레임은
    /// Resources/Sprites/{세트}/{idle|attack|walk}_{south|east|west|north}_{번호}.png에서 읽어 세트별로 한 번만 캐싱한다.
    /// walk 그림이 있는 세트는 한 칸 이동을 순간이동 대신 짧게 미끄러지며 걷는 모습으로 보여준다(없으면 예전처럼 툭).
    /// 방향은 칸이 바뀐 쪽(움직인 방향)이나 공격한 쪽을 따른다. 봇 실행 중(DamagePopup.Suppressed)엔 아무것도 안 한다.</summary>
    public class SpriteAnimator : MonoBehaviour
    {
        private const float IdleFrameSeconds = 0.15f;
        private const float AttackFrameSeconds = 0.07f;
        private const float WalkFrameSeconds = 0.05f;
        // 걷기 그림이 있는 세트만: 칸이 바뀌면 순간이동 대신 이 시간 동안 이전 칸 → 새 칸으로 미끄러지며 걷는다.
        private const float StepSeconds = 0.18f;

        private static readonly string[] DirNames = { "south", "east", "west", "north" };

        private class FrameSet
        {
            public Sprite[][] Idle = new Sprite[4][];
            public Sprite[][] Attack = new Sprite[4][];
            public Sprite[][] Walk = new Sprite[4][];
            public Sprite[][] Lit = new Sprite[4][]; // 폭발병 심지에 불붙은 대기(lit_)
            public Sprite[][] Jump = new Sprite[4][]; // 4층 슬라임 킹 점프 짓누르기(jump_ 0 움츠림·1 깊이 움츠림·2 도약·3 공중·4 낙하·5 착지)
        }

        // ---- 점프(jump_ 그림이 있는 세트만) ----
        private bool _crouch;
        private float _jumpStart = -1f, _jumpUntil = -1f, _jumpFlight;
        private const float JumpLandHold = 0.14f;

        // ---- 한 번 재생하는 연출 클립(Resources/Sprites/{세트}/{이름}_south_{번호}, 4층 슬라임 킹 분열 split 등) - 마지막 장에서 hold초 멈춘다 ----
        private Sprite[] _clip;
        private Sprite[] _overlayClip; // 겹(무기)도 같은 이름의 클립이 있으면 같은 번호로(11-03 회전 베기)
        private float _clipStart, _clipSeconds, _clipUntil = -1f;

        /// <summary>연출 클립을 재생 중인지.</summary>
        public bool IsPlayingClip => _clip != null && Time.time < _clipUntil;

        /// <summary>마지막으로 튼 연출 클립 이름(spin, dash 등) - 겹 앞뒤 표가 클립마다 다를 때(고퀄 용사 HeroLook).</summary>
        public string ClipName { get; private set; }
        /// <summary>클립을 seconds초에 걸쳐 한 번 재생하고 마지막 장을 hold초 보여 준다. 그림이 없으면 false.</summary>
        public bool PlayClip(string name, float seconds, float hold = 0.2f)
        {
            if (_set == null || DamagePopup.Suppressed)
                return false;
            var frames = LoadSequence($"Sprites/{_setName}/{name}_south_");
            if (frames.Length == 0)
                return false;
            _clip = frames;
            ClipName = name;
            _overlayClip = string.IsNullOrEmpty(_overlayName) ? null : LoadSequence($"Sprites/{_overlayName}/{name}_south_");
            _attackUntil = -1f; // 클립이 공격 모션을 덮는다(끝난 뒤 남은 공격 장면이 이어 나오지 않게)
            _clipStart = Time.time;
            _clipSeconds = seconds;
            _clipUntil = Time.time + seconds + hold;
            return true;
        }

        /// <summary>방향별 연출 클립({이름}_{south|east|west|north}_{번호}) - 그 방향 그림이 없으면 남쪽, 그것도 없으면 false.
        /// 11-04 GPT 용사 대쉬(dash_*)용. 바라보는 방향도 그쪽으로 돌린다.</summary>
        public bool PlayDirClip(string name, Vector2Int direction, float seconds, float hold = 0.02f)
        {
            if (_set == null || DamagePopup.Suppressed)
                return false;
            var d = DirIndex(direction, _dir);
            var frames = LoadSequence($"Sprites/{_setName}/{name}_{DirNames[d]}_");
            if (frames.Length == 0)
                frames = LoadSequence($"Sprites/{_setName}/{name}_south_");
            if (frames.Length == 0)
                return false;
            if (!LockedToFacing)
                _dir = d;
            _clip = frames;
            ClipName = name;
            // 11-05 고퀄 용사: 겹(무기)도 같은 방향 클립이 있으면 같은 번호로(없으면 겹 없이 - 클래식 무기엔 대쉬 그림이 없다)
            _overlayClip = null;
            if (!string.IsNullOrEmpty(_overlayName))
            {
                _overlayClip = LoadSequence($"Sprites/{_overlayName}/{name}_{DirNames[d]}_");
                if (_overlayClip.Length == 0)
                    _overlayClip = LoadSequence($"Sprites/{_overlayName}/{name}_south_");
            }
            _attackUntil = -1f;
            _clipStart = Time.time;
            _clipSeconds = seconds;
            _clipUntil = Time.time + seconds + hold;
            return true;
        }

        /// <summary>점프 그림이 있는지 - 없으면 호출부가 예전처럼 늘이고 누르는 효과만.</summary>
        public bool HasJump => _set != null && Frames(_set.Jump).Length >= 6;

        /// <summary>점프 예고 턴 - 움츠린 그림으로 기다린다(false면 대기로).</summary>
        public void SetJumpCrouch(bool crouch) => _crouch = crouch && HasJump;

        /// <summary>뛰어올라 날아가는 동안(flight초) 도약·공중·낙하, 내려앉으면 착지 그림을 잠깐.</summary>
        public void PlayJump(float flight)
        {
            _crouch = false;
            if (!HasJump)
                return;
            _jumpStart = Time.time;
            _jumpFlight = flight;
            _jumpUntil = Time.time + flight + JumpLandHold;
            _attackUntil = -1f; // 공격 모션보다 점프가 먼저
        }

        private const float LitFrameSeconds = 0.08f;

        /// <summary>true면 대기 대신 lit_ 그림을 빠르게 돌린다(폭발병 불붙음) - 그 그림이 없으면 false를 돌려줘 호출부가 예전 색 표시로.</summary>
        public bool SetLit(bool lit)
        {
            if (_set == null || Frames(_set.Lit).Length == 0)
                return false;
            _lit = lit;
            return true;
        }

        private static readonly Dictionary<string, FrameSet> Cache = new Dictionary<string, FrameSet>();

        /// <summary>처치 파편 색 - 스프라이트는 틴트가 흰색이라 렌더러 색 대신 이걸 쓴다(ActorJuice.BaseColorOf).</summary>
        public Color DebrisColor = Color.white;

        /// <summary>true면 움직인 쪽·공격한 쪽을 따라 돌지 않고 Face()로 정한 방향만 본다 - 방패병은 그림 방향 = 방패 정면이어야
        /// "정면은 막힌다"가 눈에 보인다(옆으로 붙어 때려도 방패는 원래 쪽 그대로).</summary>
        public bool LockedToFacing;

        public void Face(Vector2Int direction) => _dir = DirIndex(direction, _dir);

        /// <summary>공격 모션 길이 배율 - 보스는 2배로 느리게(10-15 사용자 "보스 일반 공격이 잘 안 보인다").</summary>
        public float AttackDurationScale = 1f;

        private FrameSet _set;
        private SpriteRenderer _renderer;
        private GridActor _actor;
        private Vector2Int _lastPos;
        private bool _posKnown;
        private int _dir; // DirNames 인덱스
        private float _time;
        private float _attackUntil = -1f;
        private float _attackStart;
        private float _stepStart = -1f;
        private Vector3 _stepFrom;
        private bool _lit;
        private int _stepParity; // 8장 걷기 - 한 칸마다 앞 4장(왼발)·뒤 4장(오른발)을 번갈아

        /// <summary>8장짜리 세트는 프레임당 시간을 줄여 한 바퀴 시간을 4장 때와 같게(10-13 4→8프레임 작업). 슬라임·거미도
        /// 10-14에 새로 그려 다른 몹과 같은 규칙(예전 외부 그림의 8장 대기만 예외였음).</summary>
        private bool _smooth = true;

        /// <summary>대기 프레임 시간 배율(11-04 GPT 용사 - 망토가 크게 펄럭여서 같은 박자도 슬라임보다 빨라 보임). 기본 1.</summary>
        public float IdleSlow { get; set; } = 1f;
        /// <summary>11-06 한 칸 걷는 시간 배율(고퀄 용사는 걷기 모션이 빨라 보여 조금 느리게). 칸 이동 판정과는 무관, 그림·미끄러짐만.</summary>
        public float StepSlow { get; set; } = 1f;
        private float StepTime => StepSeconds * StepSlow;

        private float FrameTime(float baseSeconds, int length) =>
            _smooth && length >= 8 ? baseSeconds * 4f / length : baseSeconds;

        /// <summary>세트의 대기(남쪽) 첫 프레임 - 없으면 null(그림 파일이 아직 없으면 호출부가 예전 사각형으로).</summary>
        /// <summary>그림 묶음을 미리 읽어 둔다(처음 쓸 때 한 프레임에 백 장 가까이 읽느라 멈추지 않게 - 10층 기사 변신).</summary>
        public static void Preload(string setName) => Load(setName);

        /// <summary>Preload의 나눠 읽기판 - 한 장씩 Resources.LoadAsync로 읽어 프레임을 멈추지 않는다(AssetWarmup이 타이틀·로비에서 돌린다).
        /// 도중에 누가 Load로 먼저 읽었으면 그쪽을 그대로 쓴다.</summary>
        public static IEnumerator WarmupAsync(string setName)
        {
            if (Cache.ContainsKey(setName))
                yield break;
            var set = new FrameSet();
            var kinds = new[] { ("idle", set.Idle), ("attack", set.Attack), ("walk", set.Walk), ("lit", set.Lit), ("jump", set.Jump) };
            for (var d = 0; d < DirNames.Length; d++)
            {
                foreach (var (kind, target) in kinds)
                {
                    var list = new List<Sprite>();
                    for (var i = 0; ; i++)
                    {
                        var req = Resources.LoadAsync<Sprite>($"Sprites/{setName}/{kind}_{DirNames[d]}_{i}");
                        yield return req;
                        if (!(req.asset is Sprite sprite))
                            break;
                        list.Add(sprite);
                    }
                    target[d] = list.ToArray();
                    if (Cache.ContainsKey(setName))
                        yield break;
                }
            }
            Cache[setName] = set;
        }

        public static Sprite FirstFrame(string setName)
        {
            var set = Load(setName);
            return set.Idle[0] != null && set.Idle[0].Length > 0 ? set.Idle[0][0] : null;
        }

        private string _setName;
        public string SetName => _setName;

        // ---- 겹(11-03 장비 등급별 용사) - 몸 그림과 같은 프레임 번호로 다른 세트(무기)를 자식 그림에 그린다 ----
        private FrameSet _overlay;
        private SpriteRenderer _overlayRenderer;
        private System.Func<int, int, int, bool> _overlayBehind; // (종류 0 대기·1 걷기·2 공격, 방향, 번호) → 몸 뒤?

        /// <summary>겹 세트를 붙인다(null이면 뗀다). behind가 true인 프레임은 몸 그림 뒤에.</summary>
        private string _overlayName;

        public void SetOverlay(string setName, SpriteRenderer renderer, System.Func<int, int, int, bool> behind)
        {
            _overlayName = setName;
            _overlay = string.IsNullOrEmpty(setName) ? null : Load(setName);
            _overlayRenderer = renderer;
            _overlayBehind = behind;
            if (_overlayRenderer != null && _overlay == null)
                _overlayRenderer.sprite = null;
        }

        /// <summary>몸 그림을 바꾸고 겹도 같은 프레임으로 맞춘다. kind -1 = 겹 없는 그림(연출 클립·점프 등).</summary>
        /// <summary>지금 보이는 그림(종류 0 대기·1 걷기·2 공격·3 클립·-1 겹 없음, 번호, 방향) - 11-05 고퀄 용사 칼은
        /// 이걸 보고 몸 세트의 손 위치 표에서 칼을 골라 붙인다(HeroLook.UpdateHqSword). Shown은 그림을 바꿀 때마다 불린다.</summary>
        public int ShownKind { get; private set; } = -1;
        public int ShownIndex { get; private set; }
        public int ShownDir { get; private set; }
        public System.Action Shown;

        private void Show(Sprite sprite, int kind, int index)
        {
            _renderer.sprite = sprite;
            ShownKind = kind;
            ShownIndex = index;
            ShownDir = kind == 3 || byDirHasOwn(kind) ? _dir : 0;
            Shown?.Invoke();
            if (_overlayRenderer == null)
                return;
            Sprite over = null;
            if (kind == 3) // 연출 클립 - 겹 클립의 같은 번호
            {
                if (_overlayClip != null && index >= 0 && index < _overlayClip.Length)
                    over = _overlayClip[index];
            }
            else if (_overlay != null && kind >= 0)
            {
                var byDir = kind == 0 ? _overlay.Idle : kind == 1 ? _overlay.Walk : _overlay.Attack;
                var frames = Frames(byDir);
                if (index >= 0 && index < frames.Length)
                    over = frames[index];
            }
            _overlayRenderer.sprite = over;
            _overlayRenderer.color = _renderer.color;
            _overlayRenderer.flipX = _renderer.flipX;
            _overlayRenderer.enabled = _renderer.enabled;
            var dir = ShownDir; // 클립은 지금 방향(클래식 회전 표는 방향을 안 본다)
            _overlayRenderer.sortingLayerID = _renderer.sortingLayerID;
            _overlayRenderer.sortingOrder = _renderer.sortingOrder + (_overlayBehind != null && _overlayBehind(kind, dir, index) ? -1 : 1);
        }

        /// <summary>그 종류에 지금 방향 그림이 있는지(없으면 Frames가 남쪽 그림을 쓴다 - 뒤/앞 표도 남쪽 것으로).</summary>
        private bool byDirHasOwn(int kind)
        {
            if (_set == null || kind < 0 || kind > 2)
                return false;
            var byDir = kind == 0 ? _set.Idle : kind == 1 ? _set.Walk : _set.Attack;
            return byDir[_dir] != null && byDir[_dir].Length > 0;
        }

        public void Setup(string setName, SpriteRenderer renderer, Color debrisColor)
        {
            _setName = setName;
            _set = Load(setName);
            // 세트가 바뀌면 진행 중이던 점프·움츠림·연출 클립은 버린다 - 예전 세트 기준이라 새 세트엔 그 그림이 없을 수 있다
            // (10-28 IndexOutOfRange: 10층 기사가 슬라임 킹 점프 중에 본모습으로 돌아오며 jump[5]를 찾았다)
            _jumpUntil = -1f;
            _crouch = false;
            _clip = null;
            _smooth = true;
            _renderer = renderer;
            _actor = GetComponent<GridActor>();
            DebrisColor = debrisColor;
            _time = Random.value * 10f; // 몹끼리 숨쉬기 박자가 딱 맞지 않게
        }

        /// <summary>방을 새로 깔아 순간이동했을 때 - 옛 칸에서 새 칸으로 걸어가는 모션을 넣지 않게 위치 기억을 버린다.</summary>
        public void SnapToGrid()
        {
            _posKnown = false;
            _stepStart = -1f;
        }

        /// <summary>그 방향으로 공격 모션을 한 번 재생한다.</summary>
        public void PlayAttack(Vector2Int direction)
        {
            if (DamagePopup.Suppressed || _set == null)
                return;
            if (!LockedToFacing)
                _dir = DirIndex(direction, _dir);
            _attackStart = Time.time;
            var length = Frames(_set.Attack).Length;
            _attackUntil = Time.time + FrameTime(AttackFrameSeconds * AttackDurationScale, length) * length;
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
                var delta = _actor.GridPos - _lastPos;
                if (!LockedToFacing)
                    _dir = DirIndex(delta, _dir);
                // 옆 칸으로 한 걸음일 때만 걸어서(밀려나기·순간이동은 그대로 툭)
                if (Frames(_set.Walk).Length > 0 && Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1)
                {
                    _stepFrom = new Vector3(_lastPos.x * GridConstants.CellSize, _lastPos.y * GridConstants.CellSize, 0f) + _actor.VisualOffset;
                    _stepStart = Time.time;
                    _stepParity ^= 1;
                }
                _lastPos = _actor.GridPos;
            }

            var stepping = _stepStart >= 0f && Time.time < _stepStart + StepTime;
            if (_stepStart >= 0f)
            {
                var t = stepping ? (Time.time - _stepStart) / StepTime : 1f;
                transform.position = Vector3.Lerp(_stepFrom, _actor.GridWorldPosition, t);
                if (!stepping)
                    _stepStart = -1f;
            }

            if (_clip != null && Time.time < _clipUntil)
            {
                var k = Mathf.Min(_clip.Length - 1, (int)((Time.time - _clipStart) / Mathf.Max(0.01f, _clipSeconds) * _clip.Length));
                Show(_clip[k], 3, k);
                return;
            }
            if (Time.time < _jumpUntil)
            {
                var jump = Frames(_set.Jump);
                if (jump.Length < 6)
                {
                    _jumpUntil = -1f; // 점프 도중 그림 세트가 바뀌었다(10층 기사가 슬라임 킹 모습에서 돌아옴 등) - 점프 그림 없이 평소대로
                    return;
                }
                var t = Time.time - _jumpStart;
                Show(t >= _jumpFlight ? jump[5] : jump[2 + Mathf.Min(2, (int)(t / _jumpFlight * 3f))], -1, 0);
                return;
            }
            if (_crouch)
            {
                var jump = Frames(_set.Jump);
                if (jump.Length < 2)
                {
                    _crouch = false;
                    return;
                }
                _time += Time.deltaTime;
                Show(_time % 0.6f < 0.12f ? jump[0] : jump[1], -1, 0); // 깊이 움츠린 채 가끔 들썩
                return;
            }

            if (Time.time < _attackUntil)
            {
                var attack = Frames(_set.Attack);
                if (attack.Length > 0)
                {
                    var i = Mathf.Min(attack.Length - 1, (int)((Time.time - _attackStart) / FrameTime(AttackFrameSeconds * AttackDurationScale, attack.Length)));
                    Show(attack[i], 2, i);
                    return;
                }
            }

            if (stepping)
            {
                var walk = Frames(_set.Walk);
                if (_smooth && walk.Length >= 8)
                {
                    // 한 칸 = 반 바퀴(한쪽 발) - 걸음마다 번갈아 앞·뒤 절반을 한 칸 걷는 시간에 맞춰 돌린다
                    var half = walk.Length / 2;
                    var k = Mathf.Min(half - 1, (int)((Time.time - _stepStart) / (StepTime / half)));
                    Show(walk[_stepParity * half + k], 1, _stepParity * half + k);
                }
                else
                {
                    var wi = (int)((Time.time - _stepStart) / WalkFrameSeconds) % walk.Length;
                    Show(walk[wi], 1, wi);
                }
                return;
            }

            _time += Time.deltaTime;
            if (_lit)
            {
                var lit = Frames(_set.Lit);
                Show(lit[(int)(_time / FrameTime(LitFrameSeconds, lit.Length)) % lit.Length], -1, 0);
                return;
            }
            var idle = Frames(_set.Idle);
            if (idle.Length > 0)
            {
                var ii = (int)(_time / (FrameTime(IdleFrameSeconds, idle.Length) * IdleSlow)) % idle.Length;
                Show(idle[ii], 0, ii);
            }
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
                set.Walk[d] = LoadSequence($"Sprites/{setName}/walk_{DirNames[d]}_");
                set.Lit[d] = LoadSequence($"Sprites/{setName}/lit_{DirNames[d]}_");
                set.Jump[d] = LoadSequence($"Sprites/{setName}/jump_{DirNames[d]}_");
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

    /// <summary>그림 미리 읽기(10-27 사용자 "처음 던전 입장·주문술사 때 렉") - 게임을 켜면 뒤에서 몹 그림 세트·이펙트·바닥 타일을
    /// Resources.LoadAsync로 한 장씩(세트 4개씩 나란히) 읽어 캐시에 넣어 둔다. 타이틀·로비에 있는 동안 끝나서, 던전에 들어가거나
    /// 주문술사가 마법진을 펼칠 때 그 자리에서 백 장 가까이 한꺼번에 읽느라 멈추던 게 사라진다. 다 못 읽었어도 남은 건 예전처럼 쓸 때 읽는다.
    /// 자동 플레이 봇(그림을 안 씀)이 켜지면 멈춘다.</summary>
    public class AssetWarmup : MonoBehaviour
    {
        private const int Parallel = 4;
        private static readonly string[] Sets =
        {
            "Player", "Slime", "SkeletonArcher", "ShieldSoldier", "Spider", "Bomber", "Shaman", "OrcCharger", "Jelly", "Rogue",
            "Sentinel", "Chanter", "Mimic", "GoldGoblin", "MemoryKnight",
            "Boss1", "Boss2", "Boss3", "Boss4", "Boss5", "Boss6", "Boss7", "Boss8", "Boss9", "Boss10",
        };
        private static readonly string[] Effects = { "altar_ignite", "arrow", "axe_wave", "boss_charge", "boss_cross", "boss_diag", "boss_ring", "boss_slam", "boss_snipe", "bull_charge", "candle_light", "candle_out", "chant_blast", "chant_circle", "chant_fireball", "clone_vanish", "coin", "crush_dust", "crystal_float", "crystal_rise", "crystal_shatter", "dash", "death_smoke", "doom_mark", "doom_summon", "earth_might", "event_AdventurerRemains", "event_Anvil", "event_BlessingAltar", "event_BloodAltar", "event_ChallengeFlag", "event_CursedChest", "event_ElixirSpring", "event_FateDice", "event_HealingSpring", "event_Hourglass", "event_TreasureChest", "event_Whetstone", "event_WishingWell", "explosion", "ghost_volley", "heal", "hit_spark", "hole_fill", "item_cleanse", "item_flash", "item_place", "item_ring", "item_smoke", "knight_morph", "knight_smash", "lava_burst", "levelup", "lich_cross", "lich_wave", "mirror_crack", "morph", "mushroom_sprout", "npc_alert", "obj_bomb", "obj_decoy", "obj_torch", "obj_trap", "pillar_break", "pillar_rise", "rubble_break", "rubble_fall", "shadow_split", "slash", "slash_crit", "slime_splash", "slime_wave", "soul_orb", "soul_summon", "soul_target_break", "soul_target_return", "soul_target_rise", "soul_wisp", "spin", "spore_burst", "status_immune", "status_infect", "status_infect2", "status_might", "status_reflect", "status_shield", "status_stun", "status_weak", "success", "summon", "tel_axe", "tel_beat", "tel_beat_next", "tel_charge", "tel_checker", "tel_cross", "tel_crush", "tel_diag", "tel_encircle", "tel_jump", "tel_mirror", "tel_orb", "tel_ring", "tel_ring_inv", "tel_slam", "tel_slime", "tel_snipe", "tel_snipe2", "tel_wave", "tel_zigzag", "telegraph", "web_bind", "web_shot", };
        private static readonly string[] TilePrefixes = { "Tiles/Floor_", "Tiles/FloorDecor_", "Tiles/WallTop_", "Tiles/WallFace_" };
        private static readonly string[] TileSingles = { "Tiles/WallFace_Torch", "Tiles/Shadow_Top", "Tiles/Shadow_Left", "Tiles/Shadow_TopLeft", "Tiles/Shadow_Corner", "Backgrounds/Dungeon" };
        private static readonly List<Object> Keep = new List<Object>(); // 타일은 DungeonTiles가 나중에 읽을 때까지 내려가지 않게 붙잡아 둔다

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (Application.isBatchMode)
                return;
            var go = new GameObject("AssetWarmup");
            DontDestroyOnLoad(go);
            go.AddComponent<AssetWarmup>();
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1f); // 타이틀이 다 뜬 다음부터
            var jobs = new List<IEnumerator>();
            foreach (var path in TileSingles)
                jobs.Add(Single(path));
            foreach (var prefix in TilePrefixes)
                jobs.Add(Sequence(prefix));
            foreach (var set in Sets)
                jobs.Add(SpriteAnimator.WarmupAsync(set));
            foreach (var fx in Effects)
                jobs.Add(Fx.WarmupAsync(fx));
            var next = 0;
            var running = 0;
            for (var i = 0; i < Parallel; i++)
                StartCoroutine(Worker());
            while (next < jobs.Count || running > 0)
                yield return null;
            Destroy(gameObject);

            IEnumerator Worker()
            {
                running++;
                while (next < jobs.Count)
                {
                    if (DamagePopup.Suppressed)
                        break; // 봇 - 그림을 안 쓴다
                    yield return StartCoroutine(jobs[next++]);
                }
                running--;
            }
        }

        private static IEnumerator Single(string path)
        {
            var req = Resources.LoadAsync<Sprite>(path);
            yield return req;
            if (req.asset != null)
                Keep.Add(req.asset);
        }

        private static IEnumerator Sequence(string prefix)
        {
            for (var i = 0; ; i++)
            {
                var req = Resources.LoadAsync<Sprite>(prefix + i);
                yield return req;
                if (req.asset == null)
                    yield break;
                Keep.Add(req.asset);
            }
        }
    }
}
