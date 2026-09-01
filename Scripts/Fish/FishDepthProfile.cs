using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Maps a 0..1 depth value onto the visual and behavioural differences between a foreground
    /// and a background fish. Depth 0 is nearest the viewer, depth 1 is furthest away.
    /// </summary>
    public static class FishDepthProfile {
        public static float EvaluateScaleMultiplier(float depth, float influence) {
            return 1f - (Mathf.Clamp01(depth) * Mathf.Clamp01(influence));
        }

        public static float EvaluateAlpha(float depth, float influence) {
            return 1f - (Mathf.Clamp01(depth) * Mathf.Clamp01(influence));
        }

        public static float EvaluateSpeedMultiplier(float depth, float influence) {
            return 1f - (Mathf.Clamp01(depth) * Mathf.Clamp01(influence));
        }

        /// <summary> Foreground fish get the highest sorting order so they draw over the background shoal. </summary>
        public static int EvaluateSortingOrder(float depth, int foregroundOrder, int backgroundOrder) {
            return Mathf.RoundToInt(Mathf.Lerp(foregroundOrder, backgroundOrder, Mathf.Clamp01(depth)));
        }
    }
}
