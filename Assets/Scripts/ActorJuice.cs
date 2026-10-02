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
            // 번쩍이는 사이 다른 코드가 색을 바꿨으면(보스 광폭화 등) 그 색을 존중한다.
            if (_renderer != null && _renderer.color == _flashColor)
                _renderer.color = _baseColor;
            _flashing = false;
            _flashRoutine = null;
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

        public void Bump(Vector2Int direction, float distanceInCells)
        {
            if (_actor == null)
                return;

            if (_bumpRoutine != null)
                StopCoroutine(_bumpRoutine);
            _bumpRoutine = StartCoroutine(BumpRoutine(direction, distanceInCells * GridConstants.CellSize));
        }

        private IEnumerator BumpRoutine(Vector2Int direction, float distance)
        {
            var dir = new Vector3(direction.x, direction.y, 0f).normalized;
            for (var t = 0f; t < BumpDuration; t += Time.deltaTime)
            {
                var k = Mathf.Sin(t / BumpDuration * Mathf.PI);
                transform.position = _actor.GridWorldPosition + dir * (distance * k);
                yield return null;
            }
            _actor.SyncTransform();
            _bumpRoutine = null;
        }
    }
}
