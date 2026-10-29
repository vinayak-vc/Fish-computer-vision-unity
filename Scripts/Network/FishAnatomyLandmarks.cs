using UnityEngine;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Turns Python's anatomy landmarks into positions Unity can draw at. Section 7 of the interaction
    /// contract, and design point 26.
    ///
    /// **The coordinate flip is the whole reason this class exists.** Landmarks are normalised 0..1
    /// against the PNG with the origin at the TOP-LEFT and +y pointing DOWN, which is OpenCV's
    /// convention. Unity's textures and sprites start at the BOTTOM-LEFT. Converting requires
    /// uv.y = 1 - a.y, and the contract says to read that twice for good reason: getting it wrong puts
    /// every eye on the fish's belly, and the symptom looks like a detection failure rather than a
    /// coordinate bug, so it gets investigated in the wrong half of the system.
    ///
    /// **The fallback is not an edge case.** anatomy is null today - Python phase 2 - and the contract
    /// says it may stay null; even once it ships, children's drawings defeat landmark detection
    /// routinely, and a block with confidence below 0.4 or an empty list is to be treated as absent. So
    /// the geometric fallback below is the path that will actually run most of the time, and it has to
    /// look acceptable rather than merely not crash.
    /// </summary>
    public static class FishAnatomyLandmarks {
        /// <summary> Below this, the contract says to treat the whole block as absent. </summary>
        public const float MinimumConfidence = 0.4f;

        /// <summary> Where the eye sits along the body from the nose, as a fraction. Section 7's geometric default. </summary>
        private const float FallbackEyeAlongBody = 0.2f;

        /// <summary> How far off the centre line the fallback eye sits, so it is on the head rather than through it. </summary>
        private const float FallbackEyeRise = 0.12f;

        private const float FallbackEyeRadius = 0.045f;

        /// <summary>
        /// Converts one landmark to a sprite UV. This is the flip, and the only place it should ever be
        /// written.
        /// </summary>
        public static Vector2 ToSpriteUv(float x, float y) {
            return new Vector2(Mathf.Clamp01(x), 1f - Mathf.Clamp01(y));
        }

        /// <summary> Whether a block carries landmarks worth using at all. Section 7's confidence rule. </summary>
        public static bool IsUsable(FishAnatomy anatomy) {
            return anatomy != null && anatomy.confidence >= MinimumConfidence;
        }

        /// <summary>
        /// The eye to draw a highlight on, as a sprite UV plus a radius in UV units.
        ///
        /// Uses Python's landmark when there is a usable one and falls back to geometry otherwise. Always
        /// returns something: a fish with no eye at all looks dead, and the fallback is good enough that
        /// a visitor cannot tell which path produced it.
        /// </summary>
        public static void ResolveEye(FishAnatomy anatomy, float orientationDegrees, out Vector2 uv, out float radius) {
            if (IsUsable(anatomy) && anatomy.eyes != null && anatomy.eyes.Length > 0 && anatomy.eyes[0] != null) {
                FishAnatomyEye eye = anatomy.eyes[0];
                uv = ToSpriteUv(eye.x, eye.y);

                // A radius of zero would be a detected eye with no size; fall back rather than draw nothing.
                radius = eye.r > 0.0001f ? Mathf.Clamp(eye.r, 0.005f, 0.5f) : FallbackEyeRadius;
                return;
            }

            uv = FallbackEyeUv(orientationDegrees);
            radius = FallbackEyeRadius;
        }

        /// <summary>
        /// Where the head is when Python has not said: the leading end of the major axis, per section 7.
        ///
        /// orientation_deg is the major axis against +x, in image coordinates where +y points down, so
        /// the sign of the vertical component is inverted on the way into UV space - the same flip as a
        /// landmark, for the same reason.
        /// </summary>
        public static Vector2 FallbackHeadUv(float orientationDegrees) {
            float radians = orientationDegrees * Mathf.Deg2Rad;

            // Half the extent along the axis from the centre reaches the leading end.
            float alongX = Mathf.Cos(radians) * 0.5f;
            float alongY = Mathf.Sin(radians) * 0.5f;

            return new Vector2(Mathf.Clamp01(0.5f + alongX), Mathf.Clamp01(0.5f - alongY));
        }

        /// <summary>
        /// Where the eye is when Python has not said: a fifth of the way back from the nose along the
        /// major axis, raised slightly off the centre line.
        /// </summary>
        public static Vector2 FallbackEyeUv(float orientationDegrees) {
            Vector2 head = FallbackHeadUv(orientationDegrees);
            Vector2 centre = new Vector2(0.5f, 0.5f);

            Vector2 eye = Vector2.Lerp(head, centre, FallbackEyeAlongBody * 2f);

            // Perpendicular to the body, so the eye sits on the head rather than down its throat.
            float radians = orientationDegrees * Mathf.Deg2Rad;
            eye += new Vector2(-Mathf.Sin(radians), -Mathf.Cos(radians)) * FallbackEyeRise;

            return new Vector2(Mathf.Clamp01(eye.x), Mathf.Clamp01(eye.y));
        }

        /// <summary>
        /// The tail, as a sprite UV. Landmark when available, otherwise the trailing end of the major
        /// axis.
        ///
        /// Resolved and carried now so that pinning the existing tail-beat animation to it is a change in
        /// one place once Python's anatomy block ships. Nothing deforms to it yet: with the block null on
        /// every payload today there is no way to see whether a deformation looks right, and guessing at
        /// it would be work that has to be redone against the first real landmarks.
        /// </summary>
        public static Vector2 ResolveTailUv(FishAnatomy anatomy, float orientationDegrees) {
            if (IsUsable(anatomy) && anatomy.tail != null) {
                return ToSpriteUv(anatomy.tail.x, anatomy.tail.y);
            }

            Vector2 head = FallbackHeadUv(orientationDegrees);

            // Opposite the head about the centre.
            return new Vector2(Mathf.Clamp01(1f - head.x), Mathf.Clamp01(1f - head.y));
        }
    }
}
