using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// A big shape that crosses the tank now and then and scatters everything. Design point 20's rare
    /// procedural predator.
    ///
    /// Procedural, meaning it is not somebody's drawing - it is generated, it has no capture id, it is
    /// never archived and it never persists. It exists to break the tank's rhythm: two minutes of
    /// watching needs something to happen, and a predator is the cheapest event that changes what every
    /// fish is doing at once.
    ///
    /// Deliberately rare. The default interval is minutes, because a threat that arrives often stops
    /// being a threat and just becomes the reason the fish are always at the edges.
    ///
    /// A plain class, clock-free, like RippleField and FoodField - so the whole appear-cross-leave cycle
    /// can be tested without a scene, and so AquariumManager keeps owning the only Update.
    /// </summary>
    public sealed class AquariumPredator {
        private readonly float appearIntervalSeconds;
        private readonly float crossingSeconds;
        private readonly float fadeSeconds;

        private float sinceLastVisit;
        private float crossingElapsed;
        private Vector2 entryPoint;
        private Vector2 exitPoint;
        private bool present;

        public AquariumPredator(float appearInterval, float crossingDuration, float fadeDuration) {
            appearIntervalSeconds = Mathf.Max(1f, appearInterval);
            crossingSeconds = Mathf.Max(0.5f, crossingDuration);
            fadeSeconds = Mathf.Clamp(fadeDuration, 0.01f, Mathf.Max(0.02f, crossingSeconds * 0.4f));
        }

        public bool IsPresent {
            get { return present; }
        }

        public Vector2 Position { get; private set; }

        /// <summary> Direction of travel. Used to face the silhouette the way it is swimming. </summary>
        public Vector2 Heading { get; private set; }

        /// <summary>
        /// How frightening it is right now, 0..1, fading in as it arrives and out as it leaves. Fish scale
        /// their panic by this, so nothing snaps into a full-tank scatter the instant it appears.
        /// </summary>
        public float Threat {
            get {
                if (!present) {
                    return 0f;
                }

                float fadeIn = Mathf.Clamp01(crossingElapsed / fadeSeconds);
                float fadeOut = Mathf.Clamp01((crossingSeconds - crossingElapsed) / fadeSeconds);
                return Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));
            }
        }

        /// <summary> Visits so far. Surfaced in the debug overlay, since one may not appear while anybody is watching. </summary>
        public int VisitCount { get; private set; }

        public void Tick(float deltaTime, Rect area) {
            if (deltaTime <= 0f) {
                return;
            }

            if (present) {
                crossingElapsed += deltaTime;

                if (crossingElapsed >= crossingSeconds) {
                    present = false;
                    crossingElapsed = 0f;
                    sinceLastVisit = 0f;
                    return;
                }

                float progress = crossingElapsed / crossingSeconds;
                Vector2 previous = Position;

                Position = Vector2.Lerp(entryPoint, exitPoint, progress);

                Vector2 travelled = Position - previous;
                if (travelled.sqrMagnitude > 0.000001f) {
                    Heading = travelled.normalized;
                }

                return;
            }

            sinceLastVisit += deltaTime;

            if (sinceLastVisit >= appearIntervalSeconds) {
                Appear(area);
            }
        }

        /// <summary>
        /// Sends it across now. Exposed so the debug overlay can trigger a visit rather than waiting out
        /// the interval, which would make the effect impossible to tune.
        /// </summary>
        public void Appear(Rect area) {
            // Enters from one side and leaves by the other, at a height somewhere in the middle band -
            // never along the very top or bottom, where it would be half off-screen.
            bool fromLeft = UnityEngine.Random.value < 0.5f;
            float entryHeight = Mathf.Lerp(area.yMin, area.yMax, UnityEngine.Random.Range(0.25f, 0.75f));
            float exitHeight = Mathf.Lerp(area.yMin, area.yMax, UnityEngine.Random.Range(0.25f, 0.75f));

            // Starts and ends beyond the glass, so it swims in and out rather than appearing from nothing.
            float overshoot = area.width * 0.2f;

            entryPoint = new Vector2(fromLeft ? area.xMin - overshoot : area.xMax + overshoot, entryHeight);
            exitPoint = new Vector2(fromLeft ? area.xMax + overshoot : area.xMin - overshoot, exitHeight);

            Position = entryPoint;
            Heading = (exitPoint - entryPoint).normalized;

            present = true;
            crossingElapsed = 0f;
            VisitCount++;
        }

        public void Reset() {
            present = false;
            crossingElapsed = 0f;
            sinceLastVisit = 0f;
        }
    }
}
