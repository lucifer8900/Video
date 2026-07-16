using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class SanctuaryFlyCamera : MonoBehaviour
    {
        private float _yaw;
        private float _pitch;
        private bool _cinematic = true;
        private Vector3 _cinematicStart;
        private float _cinematicClock;

        private void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
            _cinematicStart = transform.position;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                _cinematic = !_cinematic;
                _cinematicClock = 0f;
                _cinematicStart = transform.position;
            }

            bool hasMovement = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
                               Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f ||
                               Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E) ||
                               Input.GetMouseButton(1);
            if (hasMovement) _cinematic = false;

            if (_cinematic)
            {
                _cinematicClock += Time.unscaledDeltaTime;
                float t = _cinematicClock;
                transform.position = _cinematicStart + new Vector3(Mathf.Sin(t * 0.11f) * 1.6f, Mathf.Sin(t * 0.17f) * 0.35f, t * 0.55f);
                Vector3 target = new Vector3(-7f, 10f, 67f);
                Quaternion look = Quaternion.LookRotation((target - transform.position).normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.unscaledDeltaTime * 0.35f);
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                return;
            }

            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 2.4f;
                _pitch -= Input.GetAxis("Mouse Y") * 2.1f;
                _pitch = Mathf.Clamp(_pitch, -80f, 80f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 direction = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            direction = Vector3.ClampMagnitude(direction, 1f);
            float vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            float speed = Input.GetKey(KeyCode.LeftShift) ? 16f : 7f;
            Vector3 motion = (transform.right * direction.x + transform.forward * direction.z + Vector3.up * vertical) * speed * Time.unscaledDeltaTime;
            transform.position += motion;
            transform.position = new Vector3(Mathf.Clamp(transform.position.x, -55f, 55f), Mathf.Clamp(transform.position.y, 1.5f, 55f), Mathf.Clamp(transform.position.z, -88f, 86f));
        }
    }
}
