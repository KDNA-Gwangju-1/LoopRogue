using UnityEngine;

namespace LoopRogue
{
    /// <summary>메인 카메라 - 플레이어 따라가기(방 밖으로 너무 나가지 않게 방 경계로 제한) + 화면 흔들림.
    /// 기준 위치(_base)를 따로 들고 매 프레임 "기준 + 흔들림"으로 다시 놓는다. 따라갈 대상이 없으면 예전처럼
    /// 지난 프레임에 더한 흔들림만 빼고 새로 더한다. 더 센 흔들림이 들어오면 덮어쓰고, 약한 건 진행 중인 것에 묻힌다.</summary>
    public class CameraShake : MonoBehaviour
    {
        /// <summary>따라가는 속도(클수록 바로 붙음) - 한 칸 이동이 끊겨 보이지 않을 만큼만 부드럽게.</summary>
        private const float FollowSharpness = 12f;

        private Vector3 _applied;
        private float _strength;
        private float _duration;
        private float _elapsed;

        private GridActor _follow;
        private Rect _bounds;
        private Vector3 _base;
        private Camera _camera;

        public static void Shake(float strength, float duration)
        {
            var shake = Get(Camera.main);
            if (shake == null)
                return;

            var remaining = shake._duration > 0f ? shake._strength * (1f - shake._elapsed / shake._duration) : 0f;
            if (strength < remaining)
                return;

            shake._strength = strength;
            shake._duration = duration;
            shake._elapsed = 0f;
        }

        /// <summary>방에 들어갈 때 - target을 따라가고, 카메라가 roomBounds(월드 좌표) 밖을 너무 비추지 않게 제한한다. 곧바로 그 자리로 옮긴다.</summary>
        public static void Follow(Camera cam, GridActor target, Rect roomBounds)
        {
            var shake = Get(cam);
            if (shake == null)
                return;

            shake._follow = target;
            shake._bounds = roomBounds;
            shake._applied = Vector3.zero;
            shake._duration = 0f;
            shake._base = shake.Goal();
            shake.transform.position = shake._base;
        }

        private static CameraShake Get(Camera cam)
        {
            if (cam == null)
                return null;
            var shake = cam.GetComponent<CameraShake>();
            if (shake == null)
            {
                shake = cam.gameObject.AddComponent<CameraShake>();
                shake._base = cam.transform.position;
            }
            shake._camera = cam;
            return shake;
        }

        /// <summary>따라갈 목표 위치 - 대상 칸 위치를 방 경계 안으로(방이 화면보다 좁은 축은 가운데로).</summary>
        private Vector3 Goal()
        {
            var target = _follow.GridWorldPosition;
            var halfHeight = _camera != null ? _camera.orthographicSize : 0f;
            var halfWidth = _camera != null ? halfHeight * _camera.aspect : 0f;
            var x = ClampAxis(target.x, _bounds.xMin, _bounds.xMax, halfWidth);
            var y = ClampAxis(target.y, _bounds.yMin, _bounds.yMax, halfHeight);
            return new Vector3(x, y, -10f);
        }

        private static float ClampAxis(float value, float min, float max, float half)
        {
            if (max - min <= half * 2f)
                return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + half, max - half);
        }

        private void LateUpdate()
        {
            if (_follow != null)
            {
                var t = 1f - Mathf.Exp(-FollowSharpness * Time.unscaledDeltaTime);
                _base = Vector3.Lerp(_base, Goal(), t);
            }
            else
            {
                _base = transform.position - _applied;
            }

            _applied = Vector3.zero;
            if (_duration > 0f)
            {
                _elapsed += Time.unscaledDeltaTime;
                if (_elapsed >= _duration)
                {
                    _duration = 0f;
                }
                else
                {
                    var falloff = 1f - _elapsed / _duration;
                    var offset = Random.insideUnitCircle * (_strength * falloff);
                    _applied = new Vector3(offset.x, offset.y, 0f);
                }
            }

            transform.position = _base + _applied;
        }
    }
}
