using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Two unrelated depth axes, kept in one place precisely so the difference is hard to miss.
    ///
    /// PARALLAX (Evaluate* below): how far into the scene a fish is drawn. 0 is nearest the viewer, 1 is
    /// furthest away. Drives scale, opacity, sorting order and a small speed multiplier. Derived from the
    /// identity hash, not from anything Python sends.
    ///
    /// PREFERRED DEPTH (EvaluatePreferred* below): where in the water column a fish likes to swim. 0 is the
    /// surface, 1 is the floor. Comes straight from personality.preferred_depth. Drives a vertical home
    /// band in world space and nothing visual at all.
    ///
    /// Mapping one onto the other would put every surface-dwelling fish in front of the glass, which reads
    /// as a bug to anyone watching.
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

        /// <summary>
        /// World-space height a fish with this preferred depth calls home. preferredDepth 0 is the top of
        /// the swimming area and 1 is the bottom, which is the inverse of a Unity y axis, hence the Lerp
        /// running from yMax to yMin.
        /// </summary>
        public static float EvaluatePreferredHeight(float preferredDepth, Rect area) {
            return Mathf.Lerp(area.yMax, area.yMin, Mathf.Clamp01(preferredDepth));
        }

        /// <summary>
        /// The slice of the aquarium a fish with this preferred depth calls home: full width, but only the
        /// band's height. Swim targets are chosen inside this rather than anywhere in the tank.
        ///
        /// Without it the homing pull below is fighting target selection rather than shaping it - a fish
        /// told to swim to the surface every few seconds settles at a mid-water compromise, however hard
        /// it is pulled down, and preferred_depth reads as though it does nothing.
        ///
        /// A band that would fall outside the tank is slid back in rather than squashed, so a surface or
        /// floor dweller keeps a full band to roam in instead of a sliver pressed against the glass.
        /// </summary>
        public static Rect EvaluatePreferredBand(Rect area, float preferredDepth, float bandHeightFraction) {
            float bandHeight = Mathf.Clamp(area.height * Mathf.Clamp01(bandHeightFraction), 0.01f, area.height);
            float home = EvaluatePreferredHeight(preferredDepth, area);

            float minY = home - (bandHeight * 0.5f);
            float maxY = home + (bandHeight * 0.5f);

            if (minY < area.yMin) {
                maxY += area.yMin - minY;
                minY = area.yMin;
            }

            if (maxY > area.yMax) {
                minY -= maxY - area.yMax;
                maxY = area.yMax;
            }

            minY = Mathf.Max(minY, area.yMin);
            maxY = Mathf.Min(maxY, area.yMax);

            return new Rect(area.xMin, minY, area.width, Mathf.Max(0.01f, maxY - minY));
        }

        /// <summary>
        /// Vertical push back towards the fish's home band, as a -1..1 value: positive is upward.
        ///
        /// Zero inside the band, so a fish is free to roam within it and only feels the pull once it drifts
        /// out. The pull then grows with distance and saturates at the band height again, which keeps a
        /// fish that starts far from home from being yanked rather than swimming there.
        /// </summary>
        public static float EvaluatePreferredDepthPull(float currentHeight, float preferredDepth, Rect area, float bandHeightFraction) {
            float bandHeight = Mathf.Max(0.01f, area.height * Mathf.Clamp01(bandHeightFraction));
            float halfBand = bandHeight * 0.5f;

            float homeHeight = EvaluatePreferredHeight(preferredDepth, area);
            float offset = homeHeight - currentHeight;
            float distanceOutsideBand = Mathf.Abs(offset) - halfBand;

            if (distanceOutsideBand <= 0f) {
                return 0f;
            }

            return Mathf.Sign(offset) * Mathf.Clamp01(distanceOutsideBand / bandHeight);
        }
    }
}
