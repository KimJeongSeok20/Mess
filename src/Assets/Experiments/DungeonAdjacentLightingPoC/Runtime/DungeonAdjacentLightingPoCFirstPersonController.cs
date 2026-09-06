using UnityEngine;

namespace DungeonAdjacentLightingPoC
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class DungeonAdjacentLightingPoCFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float runSpeed = 6.5f;
        [SerializeField] private float mouseSensitivity = 2f;

        private CharacterController characterController;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float pitch;
        private float verticalVelocity;

        public void Configure(Camera camera)
        {
            playerCamera = camera;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>(true);
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            pitch = playerCamera != null ? NormalizeAngle(playerCamera.transform.localEulerAngles.x) : 0f;
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (Cursor.lockState == CursorLockMode.Locked && playerCamera != null)
            {
                transform.Rotate(0f, Input.GetAxisRaw("Mouse X") * mouseSensitivity, 0f);
                pitch = Mathf.Clamp(
                    pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity,
                    -80f,
                    80f);
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            Vector2 input = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
            input = Vector2.ClampMagnitude(input, 1f);

            float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
            Vector3 planar = (transform.right * input.x + transform.forward * input.y) * speed;
            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            else
                verticalVelocity -= 18f * Time.deltaTime;

            characterController.Move(
                (planar + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.R))
            {
                characterController.enabled = false;
                transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                if (playerCamera != null)
                    playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                verticalVelocity = 0f;
                characterController.enabled = true;
            }
        }

        private static float NormalizeAngle(float degrees)
        {
            return degrees > 180f ? degrees - 360f : degrees;
        }
    }
}
