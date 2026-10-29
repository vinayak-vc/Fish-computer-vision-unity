using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Builds the water gradient and gravel floor from procedurally generated sprites and keeps them
    /// filling the camera on any aspect ratio. Deliberately plain, so the drawings stay the focus.
    /// </summary>
    public sealed class AquariumBackground : MonoBehaviour {
        private const int WaterSortingOrder = -100;
        private const int WaterHighlightSortingOrder = -95;
        private const int FloorSortingOrder = -90;
        private const int GradientResolution = 256;

        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional. Supplies the time of day, the weather and the tank's mood, which together tint the water. Leave empty for a fixed, unchanging scene.")]
        [SerializeField] private AquariumManager aquariumManager;
        [SerializeField] private Color deepWaterColour = new Color(0.03f, 0.15f, 0.24f, 1f);
        [SerializeField] private Color shallowWaterColour = new Color(0.09f, 0.42f, 0.52f, 1f);
        [SerializeField] private Color floorColour = new Color(0.08f, 0.12f, 0.14f, 1f);
        [Range(0f, 0.6f)]
        [SerializeField] private float floorHeightFraction = 0.16f;

        private SpriteRenderer waterRenderer;
        private SpriteRenderer waterHighlightRenderer;
        private SpriteRenderer floorRenderer;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Awake() {
            ResolveCamera();
            BuildLayers();
        }

        private void Start() {
            FitToCamera();
        }

        private void Update() {
            ApplyAmbientTint();

            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) {
                return;
            }

            FitToCamera();
        }

        /// <summary>
        /// Tints the water for the time of day, the weather and the tank's mood. Design points 11, 14
        /// and 15 all land here, because they are all the same thing to a viewer: how the water looks.
        ///
        /// Brightness is the daylight curve lifted a little by energy, so a busy tank reads as brighter
        /// than an idle one at the same hour. A storm additionally pulls the colour towards a flat grey,
        /// which is what makes weather legible without a single new renderer.
        /// </summary>
        private void ApplyAmbientTint() {
            if (aquariumManager == null || waterRenderer == null) {
                return;
            }

            AquariumConfig config = aquariumManager.Config;
            AquariumCycle cycle = aquariumManager.Cycle;

            if (config == null || cycle == null) {
                return;
            }

            float energy = aquariumManager.Energy != null ? aquariumManager.Energy.Current : 0.5f;

            // Night never goes fully black: an unlit tank is just an off screen.
            float brightness = Mathf.Lerp(1f - config.NightDarkening, 1f, cycle.Daylight);
            brightness *= Mathf.Lerp(0.92f, 1.08f, energy);

            float storm = cycle.StormIntensity;

            waterRenderer.color = BuildTint(deepWaterColour, brightness, storm, config.StormWaterTint);

            if (waterHighlightRenderer != null) {
                waterHighlightRenderer.color = BuildTint(shallowWaterColour, brightness, storm, config.StormWaterTint);
            }

            if (floorRenderer != null) {
                floorRenderer.color = BuildTint(floorColour, brightness, storm * 0.5f, config.StormWaterTint);
            }
        }

        private static Color BuildTint(Color baseColour, float brightness, float storm, Color stormTint) {
            Color lit = new Color(baseColour.r * brightness, baseColour.g * brightness, baseColour.b * brightness, baseColour.a);
            Color stormed = Color.Lerp(lit, stormTint, Mathf.Clamp01(storm));
            stormed.a = baseColour.a;
            return stormed;
        }

        private void ResolveCamera() {
            if (targetCamera == null) {
                targetCamera = Camera.main;
            }
        }

        private void BuildLayers() {
            waterRenderer = CreateLayer("Water", ProceduralSpriteLibrary.GetSolid(), deepWaterColour, WaterSortingOrder);
            waterHighlightRenderer = CreateLayer("WaterHighlight", ProceduralSpriteLibrary.GetVerticalFade(GradientResolution), shallowWaterColour, WaterHighlightSortingOrder);
            floorRenderer = CreateLayer("Floor", ProceduralSpriteLibrary.GetSolid(), floorColour, FloorSortingOrder);
        }

        private SpriteRenderer CreateLayer(string layerName, Sprite sprite, Color colour, int sortingOrder) {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(transform, false);

            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private void FitToCamera() {
            ResolveCamera();

            if (targetCamera == null) {
                Debug.LogError("AquariumBackground: no camera available to fit the background to.");
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float height = targetCamera.orthographicSize * 2f;
            float width = height * targetCamera.aspect;
            Vector3 centre = targetCamera.transform.position;

            StretchTo(waterRenderer, new Vector2(centre.x, centre.y), new Vector2(width, height));
            StretchTo(waterHighlightRenderer, new Vector2(centre.x, centre.y), new Vector2(width, height));

            float floorHeight = height * floorHeightFraction;
            float floorCentreY = centre.y - (height * 0.5f) + (floorHeight * 0.5f);
            StretchTo(floorRenderer, new Vector2(centre.x, floorCentreY), new Vector2(width, floorHeight));
        }

        /// <summary> Scales a unit sprite so it covers an exact world-space rectangle. </summary>
        private static void StretchTo(SpriteRenderer renderer, Vector2 centre, Vector2 size) {
            if (renderer == null || renderer.sprite == null) {
                return;
            }

            Vector2 spriteSize = renderer.sprite.bounds.size;
            if (spriteSize.x <= Mathf.Epsilon || spriteSize.y <= Mathf.Epsilon) {
                return;
            }

            renderer.transform.position = new Vector3(centre.x, centre.y, 0f);
            renderer.transform.localScale = new Vector3(size.x / spriteSize.x, size.y / spriteSize.y, 1f);
        }
    }
}
