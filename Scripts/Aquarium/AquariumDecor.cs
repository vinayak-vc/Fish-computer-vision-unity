using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Scatters plants and rocks along the floor. Positions are stored as normalised horizontal
    /// fractions so the decoration re-spreads correctly when the window aspect ratio changes,
    /// without rebuilding any GameObjects.
    /// </summary>
    public sealed class AquariumDecor : MonoBehaviour {
        private const int RockTextureSize = 96;
        private const int RockSortingOrder = -70;
        private const int PlantSortingOrder = -60;

        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional. When empty a plant hierarchy is created in code.")]
        [SerializeField] private PlantController plantPrefab;
        [Tooltip("Optional parents so the scene hierarchy keeps plants and rocks in named groups.")]
        [SerializeField] private Transform plantsParent;
        [SerializeField] private Transform rocksParent;
        [SerializeField] private int plantCount = 9;
        [SerializeField] private int rockCount = 9;
        [SerializeField] private Color plantColour = new Color(0.07f, 0.23f, 0.15f, 1f);
        [SerializeField] private Color rockColour = new Color(0.09f, 0.11f, 0.13f, 1f);
        [Range(0f, 0.6f)]
        [SerializeField] private float floorHeightFraction = 0.16f;
        [SerializeField] private Vector2 rockSizeRange = new Vector2(0.4f, 1.0f);

        private readonly List<Transform> decorations = new List<Transform>();
        private readonly List<float> normalisedPositions = new List<float>();
        private readonly List<float> verticalOffsets = new List<float>();

        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Start() {
            ResolveCamera();
            BuildDecorations();
            LayOutDecorations();
        }

        private void Update() {
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) {
                return;
            }

            LayOutDecorations();
        }

        private void ResolveCamera() {
            if (targetCamera == null) {
                targetCamera = Camera.main;
            }
        }

        private void BuildDecorations() {
            for (int i = 0; i < rockCount; i++) {
                GameObject rock = new GameObject("Rock_" + i);
                rock.transform.SetParent(rocksParent != null ? rocksParent : transform, false);

                float size = UnityEngine.Random.Range(rockSizeRange.x, rockSizeRange.y);
                Sprite sprite = ProceduralSpriteLibrary.GetRock(RockTextureSize, i);
                float spriteHeight = sprite.bounds.size.y;
                float scale = spriteHeight > Mathf.Epsilon ? size / spriteHeight : 1f;
                rock.transform.localScale = new Vector3(scale, scale, 1f);

                float tint = UnityEngine.Random.Range(0.75f, 1.2f);

                SpriteRenderer renderer = rock.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(rockColour.r * tint, rockColour.g * tint, rockColour.b * tint, rockColour.a);
                renderer.sortingOrder = RockSortingOrder + i;

                Register(rock.transform, UnityEngine.Random.Range(-0.02f, 0.06f));
            }

            for (int i = 0; i < plantCount; i++) {
                PlantController plant = CreatePlant(i);
                plant.Rebuild(UnityEngine.Random.Range(4, 9), plantColour, PlantSortingOrder + (i * 8));
                Register(plant.transform, UnityEngine.Random.Range(-0.01f, 0.08f));
            }
        }

        private PlantController CreatePlant(int index) {
            Transform parent = plantsParent != null ? plantsParent : transform;

            if (plantPrefab != null) {
                PlantController instance = Instantiate(plantPrefab, parent);
                instance.name = "Plant_" + index;
                return instance;
            }

            GameObject plant = new GameObject("Plant_" + index);
            plant.transform.SetParent(parent, false);
            return plant.AddComponent<PlantController>();
        }

        private void Register(Transform decoration, float verticalOffset) {
            decorations.Add(decoration);
            normalisedPositions.Add(UnityEngine.Random.value);
            verticalOffsets.Add(verticalOffset);
        }

        private void LayOutDecorations() {
            ResolveCamera();

            if (targetCamera == null) {
                Debug.LogError("AquariumDecor: no camera available to lay decorations out against.");
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight * targetCamera.aspect;
            Vector3 centre = targetCamera.transform.position;

            float floorTop = centre.y - halfHeight + (halfHeight * 2f * floorHeightFraction);

            for (int i = 0; i < decorations.Count; i++) {
                Transform decoration = decorations[i];
                if (decoration == null) {
                    continue;
                }

                float x = Mathf.Lerp(centre.x - halfWidth, centre.x + halfWidth, normalisedPositions[i]);
                decoration.position = new Vector3(x, floorTop + verticalOffsets[i], 0f);
            }
        }
    }
}
