using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Turns wildly different source PNG dimensions into a consistent on-screen size.
    /// Scaling is always uniform, so the aspect ratio of the drawing is never distorted.
    /// </summary>
    public static class FishSizeNormalizer {
        /// <summary> World-space size a sprite occupies before any transform scaling. </summary>
        public static Vector2 CalculateSpriteWorldSize(int pixelWidth, int pixelHeight, float pixelsPerUnit) {
            if (pixelsPerUnit <= 0f) {
                Debug.LogError("FishSizeNormalizer: pixelsPerUnit must be greater than zero.");
                return Vector2.zero;
            }

            return new Vector2(pixelWidth / pixelsPerUnit, pixelHeight / pixelsPerUnit);
        }

        /// <summary> Uniform scale that makes the longest side of the sprite match targetLongestSide world units. </summary>
        public static float CalculateUniformScale(int pixelWidth, int pixelHeight, float pixelsPerUnit, float targetLongestSide) {
            Vector2 worldSize = CalculateSpriteWorldSize(pixelWidth, pixelHeight, pixelsPerUnit);
            float longestSide = Mathf.Max(worldSize.x, worldSize.y);

            if (longestSide <= Mathf.Epsilon) {
                Debug.LogError("FishSizeNormalizer: source sprite has no measurable size, falling back to scale 1.");
                return 1f;
            }

            return targetLongestSide / longestSide;
        }

        /// <summary>
        /// Where a drawing sits between the smallest and largest on-screen fish, from the mask area Python
        /// measured. Returns 0..1, for the caller to map through a response curve and onto a size range.
        ///
        /// Mask area rather than texture dimensions on purpose: the capture station pads and crops to its
        /// own rules, so two drawings of very different sizes can arrive as the same-sized PNG. Sizing from
        /// foreground_area is what keeps a fish drawn small on the paper small in the tank.
        ///
        /// Returns -1 when the payload carried no usable area, which is the caller's signal to fall back to
        /// the deterministic identity roll rather than to silently pick a middling size for everything.
        /// </summary>
        public static float NormaliseForegroundArea(int foregroundArea, int areaAtMinimumSize, int areaAtMaximumSize) {
            if (foregroundArea <= 0) {
                return -1f;
            }

            int lower = Mathf.Min(areaAtMinimumSize, areaAtMaximumSize);
            int upper = Mathf.Max(areaAtMinimumSize, areaAtMaximumSize);

            if (upper - lower <= 0) {
                return -1f;
            }

            return Mathf.Clamp01((foregroundArea - lower) / (float)(upper - lower));
        }

        /// <summary> Final world-space footprint of a fish once the normalising scale is applied. </summary>
        public static Vector2 CalculateNormalisedWorldSize(int pixelWidth, int pixelHeight, float pixelsPerUnit, float targetLongestSide) {
            float scale = CalculateUniformScale(pixelWidth, pixelHeight, pixelsPerUnit, targetLongestSide);
            return CalculateSpriteWorldSize(pixelWidth, pixelHeight, pixelsPerUnit) * scale;
        }
    }
}
