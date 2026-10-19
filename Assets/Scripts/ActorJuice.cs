using System.Collections;
using UnityEngine;

namespace LoopRogue
{
    /// <summary>액터 하나의 타격 모션 - 번쩍임(색 잠깐 바꾸기), 찌그러짐(크기 펀치), 들이받기(공격 방향으로 살짝 나갔다
    /// 돌아오기). HitFeedback이 필요할 때 처음 붙인다. 들이받기는 매 프레임 GridPos 기준 위치에 오프셋을 더하므로 그 사이에
    /// 칸 이동이 일어나도 새 칸 기준으로 자연스럽게 돌아온다.</summary>
    public class ActorJuice : MonoBehaviour
    {
        private const float FlashDuration = 0.09f;
        private const float SquishDuration = 0.14f;
        private const float BumpDuration = 0.12f;
        private const float SpinDuration = 0.18f;

        private GridActor _actor;
        private SpriteRenderer _renderer;
        private Vector3 _baseScale;
        private Color _baseColor;
        private Color _flashColor;
        private bool _flashing;
        private Coroutine _flashRoutine;
        private Coroutine _squishRoutine;
        private Coroutine _bumpRoutine;
        private Coroutine _spinRoutine;

        public static ActorJuice Get(GridActor actor)
        {
            var juice = actor.GetComponent<ActorJuice>();
            if (juice == null)
            {
                juice = actor.gameObject.AddComponent<ActorJuice>();
                juice._actor = actor;
                juice._renderer = actor.GetComponent<SpriteRenderer>();
                juice._baseScale = actor.transform.localScale;
            }
            return juice;
        }

        /// <summary>몸 크기가 바뀌었을 때(4층 슬라임 킹 분열) - 지금 크기를 새 기준으로.</summary>
        public void RebaseScale() => _baseScale = transform.localScale;

        /// <summary>번쩍이는 중이면 원래 색을, 아니면 지금 색을 돌려준다(처치 파편 색용).</summary>
        public static Color BaseColorOf(GridActor actor, SpriteRenderer renderer)
        {
            var anim = actor.GetComponent<SpriteAnimator>();
            if (anim != null)
                return anim.DebrisColor; // 그림은 틴트가 흰색이라 지정해둔 파편 색
            var juice = actor.GetComponent<ActorJuice>();
            if (juice != null && juice._flashing)
                return juice._baseColor;
            return renderer != null ? renderer.color : Color.white;
        }

        public void Flash(Color color)
        {
            if (_renderer == null)
                return;

            if (!_flashing)
                _baseColor = _renderer.color;
            _flashColor = color;
            _flashing = true;
            _renderer.color = color;

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            yield return new WaitForSeconds(FlashDuration);
            // 번쩍이는 사이 다른 코드가 색을 바꿨으면(보스 광폭화 등) 그 색을 존중한다. 투명도는 빼고 비교·복원 -
            // 연막 중엔 PlayerActor가 매 프레임 반투명을 다시 입혀서, 투명도까지 비교하면 번쩍임 색(빨강)이 그대로 남았다(10-19 사용자).
            if (_renderer != null && SameRgb(_renderer.color, _flashColor))
            {
                var restored = _baseColor;
                restored.a = _renderer.color.a;
                _renderer.color = restored;
            }
            _flashing = false;
            _flashRoutine = null;
        }

        private static bool SameRgb(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);

        // ---- 준비 동작(보스가 다음 턴에 일반 공격을 할 수 있는 상태) - 붉게 숨쉬듯 깜빡이며 살짝 부풀었다 줄었다 ----
        private static readonly Color WindupTint = new Color(1f, 0.45f, 0.35f);
        private const float WindupPulseSpeed = 7f;
        private bool _windup;
        private Color _windupBase;

        public void SetWindup(bool on)
        {
            if (_renderer == null || on == _windup)
                return;
            _windup = on;
            if (on)
                _windupBase = _flashing ? _baseColor : _renderer.color;
            else
            {
                if (!_flashing)
                    _renderer.color = _windupBase;
                if (_squishRoutine == null)
                    transform.localScale = _baseScale;
            }
        }

        private void LateUpdate()
        {
            if (!_windup || _renderer == null)
                return;
            var pulse = (Mathf.Sin(Time.time * WindupPulseSpeed) + 1f) * 0.5f;
            if (!_flashing)
                _renderer.color = Color.Lerp(_windupBase, WindupTint, pulse * 0.6f);
            if (_squishRoutine == null)
                transform.localScale = _baseScale * (1f + 0.04f * pulse);
        }

        public void Squish(float amount)
        {
            if (_squishRoutine != null)
                StopCoroutine(_squishRoutine);
            _squishRoutine = StartCoroutine(SquishRoutine(amount));
        }

        private IEnumerator SquishRoutine(float amount)
        {
            for (var t = 0f; t < SquishDuration; t += Time.deltaTime)
            {
                var k = Mathf.Sin(t / SquishDuration * Mathf.PI) * amount;
                transform.localScale = new Vector3(_baseScale.x * (1f + k), _baseScale.y * (1f - k * 0.6f), _baseScale.z);
                yield return null;
            }
            transform.localScale = _baseScale;
            _squishRoutine = null;
        }

        private Coroutine _hopRoutine;

        /// <summary>점프 짓누르기(4층 슬라임 킹) - 예고 칸을 향해 포물선으로 뛰어올라 내려찍는다. 시작·착지에 찌그러짐.
        /// 격자 위치는 이미 바뀐 뒤라 from(뛰기 전 그림 자리) → 지금 격자 자리로 그림만 날아간다.</summary>
        /// <param name="stretch">공중에서 위아래로 늘이는 정도 - 점프 그림이 있으면 그림이 늘어나 있으니 약하게.</param>
        public void Hop(Vector3 from, float heightInCells, float duration, float stretch = 0.18f, float landSquish = 0.45f)
        {
            if (_actor == null)
                return;
            if (_hopRoutine != null)
                StopCoroutine(_hopRoutine);
            _hopRoutine = StartCoroutine(HopRoutine(from, heightInCells * GridConstants.CellSize, duration, stretch, landSquish));
        }

        private IEnumerator HopRoutine(Vector3 from, float height, float duration, float stretchAmount, float landSquish)
        {
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = t / duration;
                var to = _actor.GridWorldPosition;
                transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (height * 4f * k * (1f - k));
                // 뛰는 동안 위아래로 늘어났다가 떨어질 때 원래대로
                var stretch = Mathf.Sin(k * Mathf.PI) * stretchAmount;
                transform.localScale = new Vector3(_baseScale.x * (1f - stretch * 0.5f), _baseScale.y * (1f + stretch), _baseScale.z);
                yield return null;
            }
            transform.position = _actor.GridWorldPosition;
            transform.localScale = _baseScale;
            _hopRoutine = null;
            if (landSquish > 0f)
                Squish(landSquish); // 착지 - 납작하게
        }

        /// <summary>한 바퀴 빙글(회전 베기).</summary>
        public void Spin()
        {
            if (_spinRoutine != null)
                StopCoroutine(_spinRoutine);
            _spinRoutine = StartCoroutine(SpinRoutine());
        }

        private IEnumerator SpinRoutine()
        {
            for (var t = 0f; t < SpinDuration; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Euler(0f, 0f, -360f * (t / SpinDuration));
                yield return null;
            }
            transform.rotation = Quaternion.identity;
            _spinRoutine = null;
        }

        /// <param name="duration">들이받았다 돌아오는 시간 - 없으면 BumpDuration(보스 일반 공격은 길게).</param>
        public void Bump(Vector2Int direction, float distanceInCells, float duration = 0f)
        {
            if (_actor == null)
                return;

            if (_bumpRoutine != null)
                StopCoroutine(_bumpRoutine);
            _bumpRoutine = StartCoroutine(BumpRoutine(direction, distanceInCells * GridConstants.CellSize, duration > 0f ? duration : BumpDuration));
        }

        private IEnumerator BumpRoutine(Vector2Int direction, float distance, float duration)
        {
            var dir = new Vector3(direction.x, direction.y, 0f).normalized;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                var k = Mathf.Sin(t / duration * Mathf.PI);
                transform.position = _actor.GridWorldPosition + dir * (distance * k);
                yield return null;
            }
            _actor.SyncTransform();
            _bumpRoutine = null;
        }
    }
}
