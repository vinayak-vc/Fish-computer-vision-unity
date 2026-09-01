using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Drives one fish around the aquarium: picks destinations, steers towards them with smooth
    /// low frequency wander, keeps clear of the walls and of other fish, and mirrors the sprite
    /// so the drawing always faces the way it is swimming.
    /// </summary>
    public sealed class FishMovement : MonoBehaviour {
        private const float DirectionEpsilon = 0.0001f;

        private Transform cachedTransform;
        private SpriteRenderer spriteRenderer;
        private AquariumConfig config;
        private AquariumBounds bounds;
        private AquariumManager aquariumManager;
        private FishController owner;
        private FishData data;

        private Vector2 currentTarget;
        private float headingDegrees;
        private float currentSpeed;
        private float depthSpeedMultiplier = 1f;
        private float idleTimer;
        private float noiseTime;
        private float currentPitch;
        private bool isIdle;
        private bool facingPositiveX = true;
        private bool initialised;

        public Vector2 Position {
            get { return cachedTransform != null ? (Vector2)cachedTransform.position : Vector2.zero; }
        }

        public Vector2 CurrentTarget {
            get { return currentTarget; }
        }

        public float HeadingDegrees {
            get { return headingDegrees; }
        }

        public float CurrentSpeed {
            get { return currentSpeed; }
        }

        public bool IsIdle {
            get { return isIdle; }
        }

        public bool FacingPositiveX {
            get { return facingPositiveX; }
        }

        public void Initialize(FishController fishOwner, FishData fishData, AquariumConfig aquariumConfig, AquariumBounds aquariumBounds, AquariumManager manager, SpriteRenderer renderer, Vector2 initialHeading) {
            owner = fishOwner;
            data = fishData;
            config = aquariumConfig;
            bounds = aquariumBounds;
            aquariumManager = manager;
            spriteRenderer = renderer;
            cachedTransform = transform;

            depthSpeedMultiplier = FishDepthProfile.EvaluateSpeedMultiplier(data.Depth, config.DepthSpeedInfluence);
            noiseTime = data.NoiseSeed;

            Vector2 heading = initialHeading.sqrMagnitude > DirectionEpsilon ? initialHeading.normalized : Vector2.right;
            headingDegrees = FishSteering.HeadingDegrees(heading);
            facingPositiveX = heading.x >= 0f;
            currentSpeed = data.Speed * depthSpeedMultiplier * 0.5f;

            Rect area = bounds.GetPaddedArea(config.BoundsPadding);
            currentTarget = FishSteering.ChooseTarget(area, Position, config.MinimumTargetTravelDistance, UnityEngine.Random.value, UnityEngine.Random.value);

            ApplyOrientation(heading, 1f);
            initialised = true;
        }

        /// <summary> Advanced once per frame by AquariumManager rather than by an Update of its own. </summary>
        public void Tick(float deltaTime) {
            if (!initialised || deltaTime <= 0f) {
                return;
            }

            Rect area = bounds.GetPaddedArea(config.BoundsPadding);
            Vector2 position = Position;

            UpdateTargetSelection(area, position, deltaTime);

            Vector2 steer = BuildSteeringVector(area, position);
            float desiredHeading = FishSteering.HeadingDegrees(steer);

            noiseTime += deltaTime;
            desiredHeading += FishSteering.SampleNoiseDegrees(data.NoiseSeed, noiseTime, config.DirectionNoiseFrequency, config.DirectionNoiseDegrees);

            headingDegrees = FishSteering.StepHeading(headingDegrees, desiredHeading, data.TurnSpeed, deltaTime);

            float goalSpeed = isIdle ? 0f : data.Speed * depthSpeedMultiplier;
            currentSpeed = Mathf.MoveTowards(currentSpeed, goalSpeed, data.Acceleration * deltaTime);

            Vector2 direction = FishSteering.DirectionFromHeading(headingDegrees);
            Vector2 moved = position + (direction * currentSpeed * deltaTime);
            moved = FishSteering.ClampInside(moved, area);

            cachedTransform.position = new Vector3(moved.x, moved.y, cachedTransform.position.z);

            ApplyOrientation(direction, deltaTime);
        }

        /// <summary> Forces a new destination, used when the aquarium is resized under the fish. </summary>
        public void PickNewTarget() {
            if (!initialised) {
                return;
            }

            Rect area = bounds.GetPaddedArea(config.BoundsPadding);
            currentTarget = FishSteering.ChooseTarget(area, Position, config.MinimumTargetTravelDistance, UnityEngine.Random.value, UnityEngine.Random.value);
            isIdle = false;
        }

        private void UpdateTargetSelection(Rect area, Vector2 position, float deltaTime) {
            if (isIdle) {
                idleTimer -= deltaTime;
                if (idleTimer <= 0f) {
                    isIdle = false;
                }

                return;
            }

            if (!FishSteering.HasReachedTarget(position, currentTarget, config.TargetReachDistance)) {
                return;
            }

            if (UnityEngine.Random.value < data.IdleProbability) {
                isIdle = true;
                idleTimer = config.IdleDurationRange.PickRandom();
            }

            currentTarget = FishSteering.ChooseTarget(area, position, config.MinimumTargetTravelDistance, UnityEngine.Random.value, UnityEngine.Random.value);
        }

        private Vector2 BuildSteeringVector(Rect area, Vector2 position) {
            Vector2 toTarget = currentTarget - position;
            Vector2 desired = toTarget.sqrMagnitude > DirectionEpsilon ? toTarget.normalized : FishSteering.DirectionFromHeading(headingDegrees);

            // Damping only the target-seeking part keeps the cruise flat while leaving the fish free to
            // climb or dive at full strength when a wall pushes it away.
            desired.y *= config.VerticalSteeringDamping;
            if (desired.sqrMagnitude > DirectionEpsilon) {
                desired = desired.normalized;
            }

            Vector2 avoidance = FishSteering.BoundsAvoidance(position, area, config.BoundsAvoidanceMargin) * config.BoundsAvoidanceStrength;

            Vector2 separation = Vector2.zero;
            if (config.SeparationEnabled && aquariumManager != null) {
                separation = aquariumManager.CalculateSeparation(owner, position, config.SeparationRadius) * config.SeparationStrength;
            }

            Vector2 steer = desired + avoidance + separation;
            if (steer.sqrMagnitude <= DirectionEpsilon) {
                return desired;
            }

            return steer;
        }

        private void ApplyOrientation(Vector2 direction, float deltaTime) {
            if (Mathf.Abs(direction.x) > config.FacingFlipThreshold) {
                facingPositiveX = direction.x > 0f;
            }

            if (spriteRenderer != null) {
                spriteRenderer.flipX = FishSteering.ShouldFlipHorizontally(facingPositiveX, config.ArtworkFacesRight);
            }

            float targetPitch = FishSteering.VisualAngle(headingDegrees, facingPositiveX, config.MaxPitchAngle);
            float smoothing = 1f - Mathf.Exp(-Mathf.Max(0.01f, config.PitchSmoothing) * deltaTime);
            currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, smoothing);

            cachedTransform.localRotation = Quaternion.Euler(0f, 0f, currentPitch);
        }
    }
}
