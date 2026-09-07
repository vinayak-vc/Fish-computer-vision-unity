using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Draws the food that FoodField simulates. Pooled the same way BubbleSystem is: every renderer is
    /// created once at startup and then shown, moved or hidden, so an installation that runs for days
    /// never instantiates or destroys a food GameObject.
    ///
    /// Purely presentational - it owns no state of its own and never writes to the field it reads. The
    /// simulation is deliberately separate so it can be tested without a scene, and so that turning the
    /// visuals off could never change how the fish behave.
    ///
    /// It exists because feeding has to be legible: "dropping food pulls a crowd" is not something a
    /// visitor can work out if the food itself is invisible.
    /// </summary>
    public sealed class FoodRenderer : MonoBehaviour {
        private const int PelletTextureSize = 24;

        /// <summary> Fraction of its life over which a flake fades out, so it does not blink out of existence. </summary>
        private const float FadeOutFraction = 0.18f;

        [SerializeField] private AquariumManager aquariumManager;

        private Transform[] pelletTransforms;
        private SpriteRenderer[] pelletRenderers;
        private Color pelletColour = Color.white;

        private void Start() {
            if (aquariumManager == null) {
                aquariumManager = FindObjectOfType<AquariumManager>();
            }

            if (aquariumManager == null) {
                Debug.LogError("FoodRenderer: no AquariumManager assigned; food will be simulated but never drawn.");
                return;
            }

            BuildPool();
        }

        private void LateUpdate() {
            if (pelletRenderers == null || aquariumManager == null) {
                return;
            }

            FoodField food = aquariumManager.Food;
            if (food == null) {
                return;
            }

            for (int i = 0; i < pelletRenderers.Length; i++) {
                Vector2 position;
                float normalisedAge;

                if (!food.TryGetParticle(i, out position, out normalisedAge)) {
                    if (pelletRenderers[i].gameObject.activeSelf) {
                        pelletRenderers[i].gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!pelletRenderers[i].gameObject.activeSelf) {
                    pelletRenderers[i].gameObject.SetActive(true);
                }

                pelletTransforms[i].position = new Vector3(position.x, position.y, 0f);

                Color colour = pelletColour;
                colour.a = pelletColour.a * (1f - Mathf.InverseLerp(1f - FadeOutFraction, 1f, normalisedAge));
                pelletRenderers[i].color = colour;
            }
        }

        private void BuildPool() {
            AquariumConfig config = aquariumManager.Config;
            FoodField food = aquariumManager.Food;

            if (config == null || food == null) {
                Debug.LogError("FoodRenderer: the aquarium has no config or no food field, so the pool cannot be sized.");
                return;
            }

            pelletColour = config.FoodColour;

            // Sized to the field's capacity rather than to the config, so the two can never disagree.
            int size = food.Capacity;
            pelletTransforms = new Transform[size];
            pelletRenderers = new SpriteRenderer[size];

            Sprite sprite = ProceduralSpriteLibrary.GetPellet(PelletTextureSize);
            float spriteHeight = sprite != null ? sprite.bounds.size.y : 1f;
            float scale = spriteHeight > Mathf.Epsilon ? config.FoodWorldSize / spriteHeight : 1f;

            for (int i = 0; i < size; i++) {
                GameObject pellet = new GameObject("Food_" + i);
                pellet.transform.SetParent(transform, false);
                pellet.transform.localScale = new Vector3(scale, scale, 1f);

                SpriteRenderer renderer = pellet.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = pelletColour;
                renderer.sortingOrder = config.FoodSortingOrder;
                pellet.SetActive(false);

                pelletRenderers[i] = renderer;
                pelletTransforms[i] = pellet.transform;
            }
        }
    }
}
