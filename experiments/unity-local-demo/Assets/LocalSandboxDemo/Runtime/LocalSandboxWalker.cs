using UnityEngine;

namespace Code2Create.UnitySandbox.LocalDemo
{
    internal sealed class LocalSandboxWalker : MonoBehaviour
    {
        private CharacterController body;
        private Vector3 spawn;
        private float pitch;
        private float verticalSpeed;
        internal Camera View { get; private set; }

        internal void Initialize(Vector3Int origin)
        {
            // transform.positionを足の基準点にする。
            spawn = new Vector3(origin.x, 63.04f, origin.z - 14);
            transform.position = spawn;
            body = gameObject.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = .28f;
            body.center = Vector3.up * .9f;
            body.skinWidth = .02f;
            body.stepOffset = .3f;
            var eye = new GameObject("Demo camera");
            eye.transform.SetParent(transform, false);
            eye.transform.localPosition = Vector3.up * 1.6f;
            View = eye.AddComponent<Camera>();
            View.nearClipPlane = .05f;
            View.farClipPlane = 600;
            View.fieldOfView = 65;
            View.clearFlags = CameraClearFlags.SolidColor;
            View.backgroundColor = new Color(.57f, .76f, .89f);
            ResetPose();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) Unlock();
            else if (Input.GetMouseButtonDown(0)) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Input.GetKeyDown(KeyCode.R)) ResetPose();
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                transform.Rotate(0, Input.GetAxisRaw("Mouse X") * 2, 0);
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2, -85, 85);
                View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            var horizontal = Input.GetAxisRaw("Horizontal");
            var forward = Input.GetAxisRaw("Vertical");
            var movement = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(horizontal, 0, forward), 1)) * 5;
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            movement.y = verticalSpeed;
            body.Move(movement * Time.deltaTime);
            if (transform.position.y < 40 || Mathf.Abs(transform.position.x) > 200 || Mathf.Abs(transform.position.z) > 200) ResetPose();
#endif
        }

        private void ResetPose()
        {
            body.enabled = false;
            transform.SetPositionAndRotation(spawn, Quaternion.identity);
            body.enabled = true;
            verticalSpeed = 0;
            pitch = 8;
            View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) Unlock(); }
        private void OnDisable() => Unlock();
        private static void Unlock() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
