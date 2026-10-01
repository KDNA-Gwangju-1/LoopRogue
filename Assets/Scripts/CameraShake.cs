using UnityEngine;

namespace LoopRogue
{
    /// <summary>메인 카메라 흔들기 - 매 프레임 "지난 프레임에 더한 흔들림"을 빼고 새로 더하는 방식이라 카메라 기준 위치를
    /// 따로 기억하지 않는다. 방이 바뀌어 RoomController가 카메라를 절대 위치로 옮기면 ResetOffset으로 흔들림을 끊어야
    /// 위치가 어긋나지 않는다. 더 센 흔들림이 들어오면 덮어쓰고, 약한 건 진행 중인 것에 묻힌다.</summary>
    public class CameraShake : MonoBehaviour
    {
        private Vector3 _applied;
        private float _strength;
        private float _duration;
        private float _elapsed;

        public static void Shake(float strength, float duration)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var shake = cam.GetComponent<CameraShake>();
            if (shake == null)
                shake = cam.gameObject.AddComponent<CameraShake>();

            var remaining = shake._duration > 0f ? shake._strength * (1f - shake._elapsed / shake._duration) : 0f;
            if (strength < remaining)
                return;

            shake._strength = strength;
            shake._duration = duration;
            shake._elapsed = 0f;
        }

        /// <summary>카메라를 새 위치로 옮긴 직후 부른다 - 이전 흔들림 값이 새 위치에서 빠지지 않게.</summary>
        public static void ResetOffset(Camera cam)
        {
            if (cam == null)
                return;

            var shake = cam.GetComponent<CameraShake>();
            if (shake == null)
                return;

            shake._applied = Vector3.zero;
            shake._duration = 0f;
        }

        private void LateUpdate()
        {
            transform.position -= _applied;
            _applied = Vector3.zero;

            if (_duration <= 0f)
                return;

            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= _duration)
            {
                _duration = 0f;
                return;
            }

            var falloff = 1f - _elapsed / _duration;
            var offset = Random.insideUnitCircle * (_strength * falloff);
            _applied = new Vector3(offset.x, offset.y, 0f);
            transform.position += _applied;
        }
    }
}
