using UnityEngine;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Draws the eggs that EggField incubates. Pooled and read-only, the same shape as FoodRenderer.
    ///
    /// Eggs need to be visible for design point 5 to read at all: without them, a pair simply produces a
    /// baby out of nowhere and the courtship that led to it is invisible. An egg that swells as it ripens
    /// is the only cue a visitor gets that something is coming.
    /// </summary>
    public sealed class EggRenderer : MonoBehaviour {
        private const int EggTextureSize = 32;

        /// <summary> How much larger an egg is when it is about to hatch. </summary>
        private const float RipeSwell = 1.35f;

        [SerializeField] private AquariumManager aquariumManager;

        private Transform[] eggTransforms;
        private SpriteRenderer[] eggRenderers;
        private float baseScale = 1f;

        private void Start() {
            if (aquariumManager == null) {
                aquariumManager = FindObjectOfType<AquariumManager>();
            }

            if (aquariumManager == null || aquariumManager.Config == null || aquariumManager.Eggs == null) {
                Debug.LogError("EggRenderer: no AquariumManager, config or egg field; eggs will incubate unseen.");
                return;
            }

            Build();
        }

        private void LateUpdate() {
            if (eggRenderers == null || aquariumManager == null) {
                return;
            }

            EggField field = aquariumManager.Eggs;
            if (field == null) {
                return;
            }

            for (int i = 0; i < eggRenderers.Length; i++) {
                Vector2 position;
                Color tint;
                float ripeness;

                if (!field.TryGetVisual(i, out position, out tint, out ripeness)) {
                    if (eggRenderers[i].gameObject.activeSelf) {
                        eggRenderers[i].gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!eggRenderers[i].gameObject.activeSelf) {
                    eggRenderers[i].gameObject.SetActive(true);
                }

                eggTransforms[i].position = new Vector3(position.x, position.y, 0f);

                float swell = baseScale * Mathf.Lerp(1f, RipeSwell, ripeness);
                eggTransforms[i].localScale = new Vector3(swell, swell, 1f);

                eggRenderers[i].color = tint;
            }
        }

        private void Build() {
            AquariumConfig config = aquariumManager.Config;
            EggField field = aquariumManager.Eggs;

            int size = field.Capacity;
            eggTransforms = new Transform[size];
            eggRenderers = new SpriteRenderer[size];

            Sprite sprite = ProceduralSpriteLibrary.GetEgg(EggTextureSize);
            float spriteHeight = sprite != null ? sprite.bounds.size.y : 1f;
            baseScale = spriteHeight > Mathf.Epsilon ? config.EggWorldSize / spriteHeight : 1f;

            for (int i = 0; i < size; i++) {
                GameObject egg = new GameObject("Egg_" + i);
                egg.transform.SetParent(transform, false);

                SpriteRenderer renderer = egg.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = config.EggSortingOrder;
                egg.SetActive(false);

                eggRenderers[i] = renderer;
                eggTransforms[i] = egg.transform;
            }
        }
    }
}
