using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Drives one fish around the aquarium: picks destinations, steers towards them with smooth
    /// low frequency wander, keeps clear of the walls and of other fish, holds the depth its personality
    /// prefers, reacts to the visitor, and mirrors the sprite so the drawing always faces the way it is
    /// swimming.
    ///
    /// Every reaction to the visitor arrives through AquariumManager.Pointer, which is an IPointerSource.
    /// Nothing here knows the visitor is a mouse, and that is the point: swapping in a hand tracker later
    /// must not touch this file.
    /// </summary>
    public sealed class FishMovement : MonoBehaviour {
        private const float DirectionEpsilon = 0.0001f;

        /// <summary> Attempts to find a swim target outside a click's danger zone before giving up and using it anyway. </summary>
        private const int DangerZoneRetryLimit = 4;

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
        private float affection;
        private float startleTimer;
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

        /// <summary>
        /// How much this fish has come to trust the visitor, 0..1. Earned by the pointer lingering close
        /// and slow, lost slowly while it is away. Persisted across restarts once M5 lands, which is why
        /// the decay rate is authored rather than tuned to a single session.
        /// </summary>
        public float Affection {
            get { return affection; }
        }

        /// <summary> True while the fish is bolting from a sudden pointer movement. </summary>
        public bool IsStartled {
            get { return startleTimer > 0f; }
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
            currentTarget = ChooseTargetAvoidingDanger(area, Position);

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

            UpdatePointerState(position, deltaTime);
            UpdateTargetSelection(area, position, deltaTime);

            Vector2 steer = BuildSteeringVector(area, position);
            float desiredHeading = FishSteering.HeadingDegrees(steer);

            noiseTime += deltaTime;
            desiredHeading += FishSteering.SampleNoiseDegrees(data.NoiseSeed, noiseTime, config.DirectionNoiseFrequency, config.DirectionNoiseDegrees);

            headingDegrees = FishSteering.StepHeading(headingDegrees, desiredHeading, data.TurnSpeed, deltaTime);

            // A bolting fish is faster than it has any business being, and gets there quickly. Without the
            // acceleration bump the extra speed arrives after the moment that caused it has passed.
            float startleBoost = startleTimer > 0f ? config.StartleSpeedMultiplier : 1f;
            float goalSpeed = isIdle ? 0f : data.Speed * depthSpeedMultiplier * startleBoost;
            currentSpeed = Mathf.MoveTowards(currentSpeed, goalSpeed, data.Acceleration * startleBoost * deltaTime);

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
            currentTarget = ChooseTargetAvoidingDanger(area, Position);
            isIdle = false;
        }

        /// <summary>
        /// Advances everything the visitor drives: trust earned or lost this frame, and whether a sudden
        /// pointer movement has just startled this fish. Runs whether or not the pointer is available, so
        /// affection still decays and a startle still expires while the visitor's hand is off the mouse.
        /// </summary>
        private void UpdatePointerState(Vector2 position, float deltaTime) {
            if (startleTimer > 0f) {
                startleTimer -= deltaTime;
            }

            if (aquariumManager == null || !config.PointerInteractionEnabled) {
                return;
            }

            IPointerSource pointer = aquariumManager.Pointer;
            bool available = pointer != null && pointer.IsAvailable;
            float distance = available ? Vector2.Distance(position, pointer.WorldPosition) : float.MaxValue;
            float pointerSpeed = available ? pointer.Speed : 0f;

            if (config.AffectionEnabled) {
                float delta = PointerInfluence.AffectionDelta(distance, pointerSpeed, available, config.AffectionProximityRadius,
                    config.AffectionPointerSpeedLimit, config.AffectionGainPerSecond, config.AffectionDecayPerSecond, deltaTime);
                affection = Mathf.Clamp01(affection + delta);
            }

            if (!available) {
                return;
            }

            if (!PointerInfluence.ShouldStartle(pointerSpeed, distance, config.PointerInfluenceRadius, data.Traits.Fear, config.StartleSpeedThreshold, config.StartleFearlessMultiplier)) {
                return;
            }

            startleTimer = config.StartleDurationSeconds;

            // A fish that was resting has to wake up, or the bolt is invisible until its idle runs out.
            isIdle = false;
            idleTimer = 0f;
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

            currentTarget = ChooseTargetAvoidingDanger(area, position);
        }

        /// <summary>
        /// Picks a swim destination that is not inside a click's danger zone. Retries a few times and then
        /// accepts whatever it has: a fish that refused to move because every candidate was unlucky would
        /// be a worse bug than one that briefly swims through a fading ripple.
        /// </summary>
        private Vector2 ChooseTargetAvoidingDanger(Rect area, Vector2 position) {
            Rect swimArea = ResolveTargetArea(area);
            Vector2 candidate = FishSteering.ChooseTarget(swimArea, position, config.MinimumTargetTravelDistance, UnityEngine.Random.value, UnityEngine.Random.value);

            if (aquariumManager == null || !config.PointerInteractionEnabled) {
                return candidate;
            }

            RippleField ripples = aquariumManager.Ripples;
            if (ripples == null || ripples.ActiveCount == 0) {
                return candidate;
            }

            for (int attempt = 0; attempt < DangerZoneRetryLimit && ripples.IsInDangerZone(candidate); attempt++) {
                candidate = FishSteering.ChooseTarget(swimArea, position, config.MinimumTargetTravelDistance, UnityEngine.Random.value, UnityEngine.Random.value);
            }

            return candidate;
        }

        /// <summary>
        /// Where this fish is willing to pick its next destination: the whole tank horizontally, but only
        /// its preferred depth band vertically.
        ///
        /// Target selection only. Movement, wall avoidance and clamping all still use the full aquarium,
        /// because a fish fleeing the pointer has to be free to leave its band - the band is a habit, not
        /// a fence.
        /// </summary>
        private Rect ResolveTargetArea(Rect area) {
            if (!config.PreferredDepthEnabled) {
                return area;
            }

            return FishDepthProfile.EvaluatePreferredBand(area, data.PreferredDepth, config.PreferredDepthBandHeight);
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

            Vector2 pointerForce = BuildPointerForce(position);
            Vector2 depthPull = BuildPreferredDepthPull(area, position);

            Vector2 steer = desired + avoidance + separation + pointerForce + depthPull;
            if (steer.sqrMagnitude <= DirectionEpsilon) {
                return desired;
            }

            return steer;
        }

        /// <summary>
        /// Everything the visitor contributes to this fish's steering: the standing pull or push of the
        /// pointer, the outward shove of any decaying click ripples, and an extra burst away while
        /// startled. Curiosity and fear are weighed against each other inside PointerInfluence.
        /// </summary>
        private Vector2 BuildPointerForce(Vector2 position) {
            if (aquariumManager == null || !config.PointerInteractionEnabled) {
                return Vector2.zero;
            }

            RippleField ripples = aquariumManager.Ripples;
            Vector2 force = ripples != null ? ripples.Evaluate(position) : Vector2.zero;

            IPointerSource pointer = aquariumManager.Pointer;
            if (pointer == null || !pointer.IsAvailable) {
                return force;
            }

            float comfort = config.AffectionEnabled ? PointerInfluence.Comfort(affection, config.AffectionTrustThreshold) : 0f;

            force += PointerInfluence.EvaluateSteering(position, pointer.WorldPosition, data.Traits.Curiosity, data.Traits.Fear, comfort,
                config.PointerInfluenceRadius, config.PointerAttractionStrength, config.PointerRepulsionStrength, config.PointerPersonalSpace);

            if (startleTimer <= 0f) {
                return force;
            }

            Vector2 away = position - pointer.WorldPosition;
            if (away.sqrMagnitude > DirectionEpsilon) {
                float remaining = Mathf.Clamp01(startleTimer / Mathf.Max(0.01f, config.StartleDurationSeconds));
                force += away.normalized * config.StartleStrength * remaining;
            }

            return force;
        }

        /// <summary>
        /// Vertical pull back towards the band this fish's personality prefers. Zero inside the band, so a
        /// fish roams freely within it and only feels the pull once it has drifted out.
        /// </summary>
        private Vector2 BuildPreferredDepthPull(Rect area, Vector2 position) {
            if (!config.PreferredDepthEnabled || config.PreferredDepthHomingStrength <= 0f) {
                return Vector2.zero;
            }

            float pull = FishDepthProfile.EvaluatePreferredDepthPull(position.y, data.PreferredDepth, area, config.PreferredDepthBandHeight);
            if (pull == 0f) {
                return Vector2.zero;
            }

            return new Vector2(0f, pull * config.PreferredDepthHomingStrength);
        }

        private void ApplyOrientation(Vector2 direction, float deltaTime) {
            if (Mathf.Abs(direction.x) > config.FacingFlipThreshold) {
                facingPositiveX = direction.x > 0f;
            }

            if (spriteRenderer != null) {
                spriteRenderer.flipX = FishSteering.ShouldFlipHorizontally(facingPositiveX, config.ArtworkFacesRight);
            }

            float targetPitch = FishSteering.VisualAngle(headingDegrees, facingPositiveX, config.MaxPitchAngle);

            // Grace shows up here as much as in the turn rate: a graceful fish changes attitude slowly and
            // reads as gliding, a clumsy one snaps to its heading and reads as twitchy.
            float dampedSmoothing = Mathf.Max(0.01f, config.PitchSmoothing * data.SteeringDamping);
            float smoothing = 1f - Mathf.Exp(-dampedSmoothing * deltaTime);
            currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, smoothing);

            cachedTransform.localRotation = Quaternion.Euler(0f, 0f, currentPitch);
        }
    }
}
