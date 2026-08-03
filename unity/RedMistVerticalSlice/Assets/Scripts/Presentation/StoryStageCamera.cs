using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class StoryStageCamera : MonoBehaviour
    {
        private Vector3 _targetPosition;
        private Vector3 _lookAt;
        private float _targetFov = 48f;
        private Vector3 _velocity;
        private Camera _camera;
        private float _shotStarted;
        private bool _initialized;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public void SetShot(Vector3 position, Vector3 lookAt, float fov, bool immediate = false)
        {
            _targetPosition = position;
            _lookAt = lookAt;
            _targetFov = fov;
            _shotStarted = Time.unscaledTime;
            if (!_initialized || immediate)
            {
                transform.position = position;
                transform.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
                if (_camera != null) _camera.fieldOfView = fov;
                _velocity = Vector3.zero;
            }
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            float settle = Mathf.Clamp01((Time.unscaledTime - _shotStarted) / 1.8f);
            float smooth = Mathf.Lerp(0.42f, 0.18f, settle);
            transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _velocity, smooth, 55f, Time.unscaledDeltaTime);

            Vector3 direction = (_lookAt - transform.position).normalized;
            Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desired, 1f - Mathf.Exp(-3.8f * Time.unscaledDeltaTime));
            if (_camera != null) _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _targetFov, 1f - Mathf.Exp(-3.2f * Time.unscaledDeltaTime));

            // Subtle breathing motion keeps the frame alive without the nausea of a handheld camera.
            float drift = Mathf.Sin(Time.unscaledTime * 0.31f) * 0.018f;
            transform.position += transform.up * drift * Time.unscaledDeltaTime;
        }
    }
}
