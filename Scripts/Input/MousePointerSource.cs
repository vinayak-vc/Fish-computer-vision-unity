using System;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// The mouse, presented as an IPointerSource. The only implementation today, and deliberately the only
    /// file in the project that reads UnityEngine.Input for pointer state.
    ///
    /// Screen pixels are converted to world units once, here, so a fish never has to know the resolution.
    /// Velocity is smoothed because a raw frame-to-frame delta spikes hard on a dropped frame, and an
    /// unsmoothed spike would startle the whole tank for no reason a visitor could see.
    /// </summary>
    public sealed class MousePointerSource : MonoBehaviour, IPointerSource {
        [Tooltip("Camera the pointer is projected through. Falls back to Camera.main.")]
        [SerializeField] private Camera pointerCamera;
        [Tooltip("Seconds of smoothing on the reported speed. Larger is calmer and slower to react.")]
        [SerializeField] private float velocitySmoothingSeconds = 0.08f;
        [Tooltip("Treat the pointer as absent while it is outside the game window.")]
        [SerializeField] private bool requirePointerInsideWindow = true;

        private Vector2 worldPosition;
        private Vector2 smoothedVelocity;
        private bool hasPreviousPosition;

        public event Action<Vector2> Pressed;

        public bool IsAvailable { get; private set; }

        public Vector2 WorldPosition {
            get { return worldPosition; }
        }

        public Vector2 WorldVelocity {
            get { return smoothedVelocity; }
        }

        public float Speed {
            get { return smoothedVelocity.magnitude; }
        }

        private void Awake() {
            if (pointerCamera == null) {
                pointerCamera = Camera.main;
            }

            if (pointerCamera == null) {
                Debug.LogError("MousePointerSource: no camera assigned and no Camera.main in the scene; the pointer will report as unavailable.");
            }
        }

        private void Update() {
            float deltaTime = Time.deltaTime;

            Vector2 sampled;
            if (!TryReadWorldPosition(out sampled)) {
                IsAvailable = false;
                hasPreviousPosition = false;
                smoothedVelocity = Vector2.zero;
                return;
            }

            if (hasPreviousPosition && deltaTime > 0f) {
                Vector2 instantVelocity = (sampled - worldPosition) / deltaTime;
                smoothedVelocity = SmoothVelocity(smoothedVelocity, instantVelocity, deltaTime);
            } else {
                // First frame after the pointer reappears: it teleported, and reporting that as speed
                // would fire every startle in the tank at once.
                smoothedVelocity = Vector2.zero;
            }

            worldPosition = sampled;
            hasPreviousPosition = true;
            IsAvailable = true;

            if (UnityEngine.Input.GetMouseButtonDown(0)) {
                RaisePressed(worldPosition);
            }
        }

        private Vector2 SmoothVelocity(Vector2 current, Vector2 target, float deltaTime) {
            if (velocitySmoothingSeconds <= 0f) {
                return target;
            }

            // Frame-rate independent exponential smoothing: the same feel at 30 fps and at 144 fps.
            float blend = 1f - Mathf.Exp(-deltaTime / velocitySmoothingSeconds);
            return Vector2.Lerp(current, target, blend);
        }

        private bool TryReadWorldPosition(out Vector2 result) {
            result = Vector2.zero;

            if (pointerCamera == null) {
                pointerCamera = Camera.main;
            }

            if (pointerCamera == null) {
                return false;
            }

            Vector3 screenPosition = UnityEngine.Input.mousePosition;

            if (requirePointerInsideWindow && !IsInsideWindow(screenPosition)) {
                return false;
            }

            // The z distance is what the camera projects onto. For an orthographic camera any positive
            // value gives the same x and y, but nearClipPlane is correct for a perspective one too.
            screenPosition.z = Mathf.Abs(pointerCamera.transform.position.z) + pointerCamera.nearClipPlane;

            Vector3 world = pointerCamera.ScreenToWorldPoint(screenPosition);
            result = new Vector2(world.x, world.y);
            return true;
        }

        private static bool IsInsideWindow(Vector3 screenPosition) {
            return screenPosition.x >= 0f && screenPosition.x <= Screen.width && screenPosition.y >= 0f && screenPosition.y <= Screen.height;
        }

        private void RaisePressed(Vector2 position) {
            Action<Vector2> handler = Pressed;
            if (handler != null) {
                handler(position);
            }
        }
    }
}
