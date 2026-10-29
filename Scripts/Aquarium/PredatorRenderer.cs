using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Draws the predator that AquariumPredator simulates. One renderer, created once, shown while a
    /// visit is happening and hidden the rest of the time - which is nearly all of the time, since visits
    /// are minutes apart.
    ///
    /// Purely presentational, like FoodRenderer: it only ever reads the simulation. That separation is
    /// what lets the appear-cross-leave cycle be unit tested without a scene, and it means switching the
    /// visuals off could never change how the fish behave.
    /// </summary>
    public sealed class PredatorRenderer : MonoBehaviour {
        private const int SilhouetteWidth = 128;
        private const int SilhouetteHeight = 48;

        [SerializeField] private AquariumManager aquariumManager;

        private SpriteRenderer silhouette;
        private Transform silhouetteTransform;
        private Color baseColour = Color.black;
        private float worldScale = 1f;

        private void Start() {
            if (aquariumManager == null) {
                aquariumManager = FindObjectOfType<AquariumManager>();
            }

            if (aquariumManager == null || aquariumManager.Config == null) {
                Debug.LogError("PredatorRenderer: no AquariumManager or config; the predator will move but never be seen.");
                return;
            }

            Build();
        }

        private void LateUpdate() {
            if (silhouette == null || aquariumManager == null) {
                return;
            }

            AquariumPredator predator = aquariumManager.Predator;

            if (predator == null || !predator.IsPresent) {
                if (silhouette.gameObject.activeSelf) {
                    silhouette.gameObject.SetActive(false);
                }

                return;
            }

            if (!silhouette.gameObject.activeSelf) {
                silhouette.gameObject.SetActive(true);
            }

            silhouetteTransform.position = new Vector3(predator.Position.x, predator.Position.y, 0f);

            // The sprite is drawn nose-right, so it is mirrored when swimming the other way. Scaling on x
            // rather than setting flipX keeps the one code path for both facings.
            float facing = predator.Heading.x >= 0f ? 1f : -1f;
            silhouetteTransform.localScale = new Vector3(worldScale * facing, worldScale, 1f);

            // Fades with the threat, so it materialises out of the dark rather than popping into it.
            Color colour = baseColour;
            colour.a = baseColour.a * predator.Threat;
            silhouette.color = colour;
        }

        private void Build() {
            AquariumConfig config = aquariumManager.Config;
            baseColour = config.PredatorColour;

            GameObject predator = new GameObject("PredatorSilhouette");
            predator.transform.SetParent(transform, false);

            silhouette = predator.AddComponent<SpriteRenderer>();
            silhouette.sprite = ProceduralSpriteLibrary.GetPredatorSilhouette(SilhouetteWidth, SilhouetteHeight);
            silhouette.color = baseColour;
            silhouette.sortingOrder = config.PredatorSortingOrder;

            float spriteWidth = silhouette.sprite != null ? silhouette.sprite.bounds.size.x : 1f;
            worldScale = spriteWidth > Mathf.Epsilon ? config.PredatorWorldLength / spriteWidth : 1f;

            silhouetteTransform = predator.transform;
            predator.SetActive(false);
        }
    }
}
