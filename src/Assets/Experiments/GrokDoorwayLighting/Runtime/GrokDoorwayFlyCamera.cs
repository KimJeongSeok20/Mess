using UnityEngine;

namespace GrokDoorwayLighting
{
    [DisallowMultipleComponent]
    public sealed class GrokDoorwayFlyCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float lookSpeed = 2.1f;

        private float _yaw;
        private float _pitch;
        private bool _looking;

        private void Start()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetMouseButtonUp(1))
            {
                _looking = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (_looking)
            {
                _yaw += Input.GetAxisRaw("Mouse X") * lookSpeed;
                _pitch -= Input.GetAxisRaw("Mouse Y") * lookSpeed;
                _pitch = Mathf.Clamp(_pitch, -85f, 85f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 move = new Vector3(
                KeyAxis(KeyCode.D, KeyCode.A),
                KeyAxis(KeyCode.E, KeyCode.Q),
                KeyAxis(KeyCode.W, KeyCode.S));
            float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * 2.4f : moveSpeed;
            transform.position += transform.TransformDirection(move) * (speed * Time.deltaTime);
        }

        private static float KeyAxis(KeyCode positive, KeyCode negative)
        {
            float value = 0f;
            if (Input.GetKey(positive))
                value += 1f;
            if (Input.GetKey(negative))
                value -= 1f;
            return value;
        }
    }
}
