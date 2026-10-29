using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// The water's own movement: slow currents that drift the whole shoal across the tank, plus a
    /// prevailing direction that turns over minutes. Design points 17 and 18.
    ///
    /// A pure function of position and time, with no state of its own. That is worth having: it means
    /// every fish can sample the current without anyone owning a grid, nothing has to be rebuilt each
    /// frame, and the same position and time always give the same answer - so a current cannot be the
    /// reason two runs of the same drawing diverge.
    ///
    /// Built from Perlin noise rather than a physical simulation. What matters here is that the motion
    /// looks unhurried and has no visible seams or repeats; a real fluid solve would cost far more and
    /// read no better behind a tank of drawings.
    /// </summary>
    public static class AquariumFlow {
        /// <summary> Offsets keep the two noise lookups from being correlated, which would collapse the flow onto a diagonal. </summary>
        private const float HorizontalNoiseOffset = 0f;
        private const float VerticalNoiseOffset = 137.31f;

        /// <summary> How far apart the two samples of the migration drift are taken. Small, so the drift turns slowly. </summary>
        private const float MigrationNoiseOffset = 51.7f;

        /// <summary>
        /// Current at a point, as a direction times a strength. Sampling is cheap enough to do per fish
        /// per frame: four Perlin lookups and no allocation.
        ///
        /// scale is in world units - larger means broader, slower eddies. turbulence adds a faster, tighter
        /// component, and is what a storm turns up.
        /// </summary>
        public static Vector2 Sample(Vector2 position, float time, float scale, float strength, float turbulence) {
            if (strength <= 0f && turbulence <= 0f) {
                return Vector2.zero;
            }

            float safeScale = Mathf.Max(0.01f, scale);
            float x = position.x / safeScale;
            float y = position.y / safeScale;

            // Perlin returns 0..1, so each sample is recentred to -1..1.
            float horizontal = (Mathf.PerlinNoise(x + HorizontalNoiseOffset, y + (time * 0.05f)) * 2f) - 1f;
            float vertical = (Mathf.PerlinNoise(x + VerticalNoiseOffset, y + (time * 0.05f)) * 2f) - 1f;

            Vector2 flow = new Vector2(horizontal, vertical) * strength;

            if (turbulence > 0f) {
                // Four times the spatial frequency and six times the temporal, so a storm feels choppy
                // against the calm current underneath rather than simply stronger.
                float gustX = (Mathf.PerlinNoise((x * 4f) + 11.3f, (y * 4f) + (time * 0.3f)) * 2f) - 1f;
                float gustY = (Mathf.PerlinNoise((x * 4f) + 71.9f, (y * 4f) + (time * 0.3f)) * 2f) - 1f;
                flow += new Vector2(gustX, gustY) * turbulence;
            }

            // The vertical component is halved: a current that pushed fish up and down as hard as it
            // pushes them along would fight the depth bands every fish is trying to hold.
            flow.y *= 0.5f;
            return flow;
        }

        /// <summary>
        /// The prevailing drift, a single direction shared by the whole tank that turns over minutes.
        /// This is the migration of design point 18: on its own the eddies above just mill about, and it
        /// is the slow bias underneath them that carries the shoal from one end to the other.
        /// </summary>
        public static Vector2 SampleMigration(float time, float periodSeconds, float strength) {
            if (strength <= 0f) {
                return Vector2.zero;
            }

            float t = time / Mathf.Max(1f, periodSeconds);

            float horizontal = (Mathf.PerlinNoise(t, 0f) * 2f) - 1f;
            float vertical = (Mathf.PerlinNoise(t + MigrationNoiseOffset, 0f) * 2f) - 1f;

            // Mostly sideways. A tank is wider than it is tall, and a vertical migration would just press
            // the whole shoal against the glass at the top or the bottom.
            return new Vector2(horizontal, vertical * 0.25f) * strength;
        }
    }
}
