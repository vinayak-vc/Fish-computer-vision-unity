using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Subtle transform-only swimming motion applied to the fish visual child. It never touches the
    /// texture, so the drawing itself is left exactly as the user made it. A mesh deformation pass can
    /// replace this later by driving the same visual transform.
    /// </summary>
    public sealed class FishAnimator : MonoBehaviour {
        private const float RollFrequencyRatio = 0.8f;
        private const float BreathingFrequencyRatio = 1.3f;
        private const float MinimumMotionScale = 0.35f;

        private Transform cachedTransform;
        private AquariumConfig config;
        private FishData data;
        private Vector3 baseLocalPosition;
        private float animationTime;
        private bool initialised;

        public void Initialize(FishData fishData, AquariumConfig aquariumConfig) {
            data = fishData;
            config = aquariumConfig;
            cachedTransform = transform;
            baseLocalPosition = cachedTransform.localPosition;
            animationTime = data.AnimationPhase;
            initialised = true;
        }

        /// <summary>
        /// Advanced by FishController. speedNormalised is the current speed as a fraction of the
        /// top speed of the fish, so a resting fish still breathes but stops sculling.
        /// </summary>
        public void Tick(float deltaTime, float speedNormalised) {
            if (!initialised || deltaTime <= 0f) {
                return;
            }

            animationTime += deltaTime * data.SwimFrequency * Mathf.PI * 2f;

            float motionScale = Mathf.Lerp(MinimumMotionScale, 1f, Mathf.Clamp01(speedNormalised));

            float bob = Mathf.Sin(animationTime) * data.SwimAmplitude * motionScale;
            float roll = Mathf.Sin(animationTime * RollFrequencyRatio) * config.BodyRollDegrees * motionScale;
            float breathing = 1f + (Mathf.Sin(animationTime * BreathingFrequencyRatio) * config.BreathingScaleAmount);

            cachedTransform.localPosition = new Vector3(baseLocalPosition.x, baseLocalPosition.y + bob, baseLocalPosition.z);
            cachedTransform.localRotation = Quaternion.Euler(0f, 0f, roll);
            cachedTransform.localScale = new Vector3(breathing, breathing, 1f);
        }
    }
}
