using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Pure steering maths shared by FishMovement. Kept free of Unity component state so the
    /// movement rules can be unit tested without a scene.
    /// </summary>
    public static class FishSteering {
        public static float HeadingDegrees(Vector2 direction) {
            if (direction.sqrMagnitude <= Mathf.Epsilon) {
                return 0f;
            }

            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        public static Vector2 DirectionFromHeading(float headingDegrees) {
            float radians = headingDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary> Rotates the current heading towards the desired one at a capped angular rate, taking the shortest way round. </summary>
        public static float StepHeading(float currentDegrees, float desiredDegrees, float turnSpeedDegreesPerSecond, float deltaTime) {
            float maximumStep = Mathf.Max(0f, turnSpeedDegreesPerSecond) * Mathf.Max(0f, deltaTime);
            return Mathf.MoveTowardsAngle(currentDegrees, desiredDegrees, maximumStep);
        }

        public static bool HasReachedTarget(Vector2 position, Vector2 target, float reachDistance) {
            return (target - position).sqrMagnitude <= (reachDistance * reachDistance);
        }

        /// <summary>
        /// Picks the next swim destination inside the aquarium. Randomness is supplied by the caller so the
        /// selection is deterministic under test. Targets closer than minimumTravelDistance are pushed outwards
        /// to stop a fish twitching between neighbouring points.
        /// </summary>
        public static Vector2 ChooseTarget(Rect area, Vector2 currentPosition, float minimumTravelDistance, float random01X, float random01Y) {
            Vector2 candidate = new Vector2(Mathf.Lerp(area.xMin, area.xMax, Mathf.Clamp01(random01X)), Mathf.Lerp(area.yMin, area.yMax, Mathf.Clamp01(random01Y)));

            Vector2 delta = candidate - currentPosition;
            float distance = delta.magnitude;
            if (distance >= minimumTravelDistance) {
                return candidate;
            }

            Vector2 direction = distance > 0.0001f ? (delta / distance) : new Vector2(1f, 0f);
            return ClampInside(currentPosition + (direction * minimumTravelDistance), area);
        }

        /// <summary> Low frequency Perlin wander in degrees. Smooth by construction, so the fish never jitters frame to frame. </summary>
        public static float SampleNoiseDegrees(float noiseSeed, float time, float frequency, float amplitudeDegrees) {
            float sample = Mathf.PerlinNoise(noiseSeed, time * frequency);
            return ((sample * 2f) - 1f) * amplitudeDegrees;
        }

        /// <summary> Push away from one neighbour. Falls off linearly to zero at the separation radius. </summary>
        public static Vector2 SeparationContribution(Vector2 self, Vector2 other, float radius) {
            if (radius <= Mathf.Epsilon) {
                return Vector2.zero;
            }

            Vector2 away = self - other;
            float distance = away.magnitude;
            if (distance >= radius) {
                return Vector2.zero;
            }

            if (distance <= 0.0001f) {
                return new Vector2(0f, 1f);
            }

            float falloff = 1f - (distance / radius);
            return (away / distance) * falloff;
        }

        /// <summary> Inward push that grows as the fish approaches an aquarium edge, so it turns before it hits the wall. </summary>
        public static Vector2 BoundsAvoidance(Vector2 position, Rect area, float margin) {
            if (margin <= Mathf.Epsilon) {
                return Vector2.zero;
            }

            Vector2 avoidance = Vector2.zero;

            float distanceToLeft = position.x - area.xMin;
            if (distanceToLeft < margin) {
                avoidance.x += 1f - Mathf.Clamp01(distanceToLeft / margin);
            }

            float distanceToRight = area.xMax - position.x;
            if (distanceToRight < margin) {
                avoidance.x -= 1f - Mathf.Clamp01(distanceToRight / margin);
            }

            float distanceToBottom = position.y - area.yMin;
            if (distanceToBottom < margin) {
                avoidance.y += 1f - Mathf.Clamp01(distanceToBottom / margin);
            }

            float distanceToTop = area.yMax - position.y;
            if (distanceToTop < margin) {
                avoidance.y -= 1f - Mathf.Clamp01(distanceToTop / margin);
            }

            return avoidance;
        }

        public static Vector2 ClampInside(Vector2 position, Rect area) {
            return new Vector2(Mathf.Clamp(position.x, area.xMin, area.xMax), Mathf.Clamp(position.y, area.yMin, area.yMax));
        }

        /// <summary>
        /// Whether the sprite has to be mirrored for the fish to face the way it is swimming.
        /// artworkFacesRight describes the source drawing: true when its nose points right in the PNG.
        /// </summary>
        public static bool ShouldFlipHorizontally(bool movingPositiveX, bool artworkFacesRight) {
            return artworkFacesRight ? !movingPositiveX : movingPositiveX;
        }

        /// <summary>
        /// Z rotation for the sprite. The fish tilts nose-up or nose-down towards its heading but is never
        /// rolled upside down; when the sprite is mirrored the tilt is mirrored with it.
        /// </summary>
        public static float VisualAngle(float headingDegrees, bool visualFacesPositiveX, float maxPitchAngle) {
            Vector2 direction = DirectionFromHeading(headingDegrees);
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float clamped = Mathf.Clamp(pitch, -maxPitchAngle, maxPitchAngle);
            return visualFacesPositiveX ? clamped : -clamped;
        }
    }
}
