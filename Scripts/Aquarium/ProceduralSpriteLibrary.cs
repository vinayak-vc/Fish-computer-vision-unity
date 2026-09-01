using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Generates the aquarium decoration sprites at runtime and caches them. The scene therefore ships
    /// without art dependencies, and every decoration texture is a handful of kilobytes.
    /// </summary>
    public static class ProceduralSpriteLibrary {
        private const float DefaultPixelsPerUnit = 100f;

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static int CachedSpriteCount {
            get { return cache.Count; }
        }

        /// <summary> Flat white square. Used for gradients and solid bands that are tinted per renderer. </summary>
        public static Sprite GetSolid() {
            Sprite cached;
            if (cache.TryGetValue("solid", out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(4, 4, "AquariumSolid");
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 4; x++) {
                    texture.SetPixel(x, y, Color.white);
                }
            }

            texture.Apply(false, false);
            return Store("solid", texture);
        }

        /// <summary> Vertical white-to-transparent ramp, tinted by the renderer to build water and gravel bands. </summary>
        public static Sprite GetVerticalFade(int height) {
            string key = "fade_" + height;

            Sprite cached;
            if (cache.TryGetValue(key, out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(4, height, "AquariumFade");
            for (int y = 0; y < height; y++) {
                float normalised = (float)y / Mathf.Max(1, height - 1);
                Color colour = new Color(1f, 1f, 1f, normalised);

                for (int x = 0; x < 4; x++) {
                    texture.SetPixel(x, y, colour);
                }
            }

            texture.Apply(false, false);
            return Store(key, texture);
        }

        /// <summary> Soft bubble: bright rim, hollow centre and a small highlight. </summary>
        public static Sprite GetBubble(int size) {
            string key = "bubble_" + size;

            Sprite cached;
            if (cache.TryGetValue(key, out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(size, size, "AquariumBubble");
            float half = size * 0.5f;
            Vector2 highlight = new Vector2(half * 0.65f, half * 1.35f);
            float highlightRadius = size * 0.13f;

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;

                    float alpha = 0f;
                    if (distance <= 1f) {
                        float rim = SmoothThreshold(0.62f, 0.94f, distance);
                        float outerFade = 1f - SmoothThreshold(0.94f, 1f, distance);
                        alpha = rim * outerFade * 0.75f;
                        alpha += (1f - SmoothThreshold(0f, 0.62f, distance)) * 0.12f;
                    }

                    float highlightDistance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), highlight);
                    if (highlightDistance < highlightRadius) {
                        alpha = Mathf.Max(alpha, 1f - (highlightDistance / highlightRadius));
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                }
            }

            texture.Apply(false, false);
            return Store(key, texture);
        }

        /// <summary> Tapered leaf with a rounded base and a pointed tip, pivoted at the bottom so it can sway from the root. </summary>
        public static Sprite GetLeaf(int width, int height) {
            string key = "leaf_" + width + "x" + height;

            Sprite cached;
            if (cache.TryGetValue(key, out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(width, height, "AquariumLeaf");
            float halfWidth = width * 0.5f;

            for (int y = 0; y < height; y++) {
                float normalisedHeight = (float)y / Mathf.Max(1, height - 1);
                float taper = Mathf.Sin(Mathf.Clamp01(1f - normalisedHeight) * Mathf.PI * 0.5f);
                float rowHalfWidth = Mathf.Max(0.5f, halfWidth * taper);

                for (int x = 0; x < width; x++) {
                    float offset = Mathf.Abs((x + 0.5f) - halfWidth);
                    float edge = 1f - SmoothThreshold(rowHalfWidth - 1.5f, rowHalfWidth, offset);
                    float shading = Mathf.Lerp(0.65f, 1f, 1f - (offset / Mathf.Max(0.5f, rowHalfWidth)));

                    texture.SetPixel(x, y, new Color(shading, shading, shading, Mathf.Clamp01(edge)));
                }
            }

            texture.Apply(false, false);
            return Store(key, texture, new Vector2(0.5f, 0f));
        }

        /// <summary> Irregular rounded pebble. The seed varies the silhouette so a row of rocks is not repetitive. </summary>
        public static Sprite GetRock(int size, int seed) {
            string key = "rock_" + size + "_" + seed;

            Sprite cached;
            if (cache.TryGetValue(key, out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(size, size, "AquariumRock");
            float half = size * 0.5f;
            float noiseOffset = seed * 13.37f;

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    Vector2 point = new Vector2((x + 0.5f) - half, (y + 0.5f) - half);
                    float angle = Mathf.Atan2(point.y, point.x);
                    float wobble = Mathf.PerlinNoise(noiseOffset + (Mathf.Cos(angle) * 1.6f), noiseOffset + (Mathf.Sin(angle) * 1.6f));
                    float radius = half * Mathf.Lerp(0.72f, 0.98f, wobble);

                    float distance = point.magnitude;
                    float alpha = 1f - SmoothThreshold(radius - 1.5f, radius, distance);
                    float shading = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((point.y + half) / size));

                    texture.SetPixel(x, y, new Color(shading, shading, shading, Mathf.Clamp01(alpha)));
                }
            }

            texture.Apply(false, false);
            return Store(key, texture, new Vector2(0.5f, 0f));
        }

        /// <summary> Soft light beam: bright along the centre line, fading out sideways and towards the bottom. </summary>
        public static Sprite GetLightRay(int width, int height) {
            string key = "ray_" + width + "x" + height;

            Sprite cached;
            if (cache.TryGetValue(key, out cached) && cached != null) {
                return cached;
            }

            Texture2D texture = CreateTexture(width, height, "AquariumLightRay");
            float halfWidth = width * 0.5f;

            for (int y = 0; y < height; y++) {
                float verticalFade = Mathf.Pow((float)y / Mathf.Max(1, height - 1), 1.6f);

                for (int x = 0; x < width; x++) {
                    float horizontal = 1f - Mathf.Clamp01(Mathf.Abs((x + 0.5f) - halfWidth) / halfWidth);
                    float alpha = Mathf.Pow(horizontal, 2.2f) * verticalFade;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                }
            }

            texture.Apply(false, false);
            return Store(key, texture, new Vector2(0.5f, 1f));
        }

        public static void Clear() {
            foreach (KeyValuePair<string, Sprite> pair in cache) {
                Sprite sprite = pair.Value;
                if (sprite == null) {
                    continue;
                }

                Texture2D texture = sprite.texture;
                Object.Destroy(sprite);

                if (texture != null) {
                    Object.Destroy(texture);
                }
            }

            cache.Clear();
        }

        /// <summary>
        /// Shader-style smoothstep. Mathf.SmoothStep interpolates between two values by a 0..1 parameter,
        /// it does not smoothly threshold a value against two edges, so shape edges need this instead.
        /// </summary>
        private static float SmoothThreshold(float edge0, float edge1, float value) {
            if (Mathf.Abs(edge1 - edge0) <= Mathf.Epsilon) {
                return value >= edge1 ? 1f : 0f;
            }

            float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - (2f * t));
        }

        private static Texture2D CreateTexture(int width, int height, string name) {
            Texture2D texture = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static Sprite Store(string key, Texture2D texture) {
            return Store(key, texture, new Vector2(0.5f, 0.5f));
        }

        private static Sprite Store(string key, Texture2D texture, Vector2 pivot) {
            Rect rect = new Rect(0f, 0f, texture.width, texture.height);
            Sprite sprite = Sprite.Create(texture, rect, pivot, DefaultPixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            cache[key] = sprite;
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() {
            cache.Clear();
        }
    }
}
