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

        /// <summary> Final world-space footprint of a fish once the normalising scale is applied. </summary>
        public static Vector2 CalculateNormalisedWorldSize(int pixelWidth, int pixelHeight, float pixelsPerUnit, float targetLongestSide) {
            float scale = CalculateUniformScale(pixelWidth, pixelHeight, pixelsPerUnit, targetLongestSide);
            return CalculateSpriteWorldSize(pixelWidth, pixelHeight, pixelsPerUnit) * scale;
        }
    }
}
