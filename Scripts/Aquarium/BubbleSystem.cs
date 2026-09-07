using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Pooled bubbles. The pool is allocated once and every bubble is advanced in a single loop,
    /// so a continuously running aquarium never instantiates or destroys a bubble GameObject.
    /// </summary>
    public sealed class BubbleSystem : MonoBehaviour {
        private const int BubbleTextureSize = 64;

        private struct BubbleState {
            public bool Active;
            public float BaseX;
            public float RiseSpeed;
            public float DriftPhase;
            public float DriftFrequency;
            public float DriftAmplitude;
            public float Life;
            public float MaxLife;
        }

        [SerializeField] private Camera targetCamera;
        [Tooltip("Optional. When empty the pool is built from a generated bubble sprite.")]
        [SerializeField] private SpriteRenderer bubblePrefab;
        [SerializeField] private int poolSize = 40;
        [SerializeField] private float spawnIntervalSeconds = 0.4f;
        [SerializeField] private Vector2 riseSpeedRange = new Vector2(0.35f, 1.05f);
        [SerializeField] private Vector2 sizeRange = new Vector2(0.06f, 0.2f);
        [SerializeField] private Vector2 driftFrequencyRange = new Vector2(0.5f, 1.6f);
        [SerializeField] private float driftAmplitude = 0.16f;
        [SerializeField] private Color bubbleColour = new Color(0.85f, 0.97f, 1f, 0.5f);
        [SerializeField] private int sortingOrder = 220;

        private Transform[] bubbleTransforms;
        private SpriteRenderer[] bubbleRenderers;
        private BubbleState[] bubbleStates;
        private float spawnTimer;
        private int spawnCursor;
        private float floorY;
        private float ceilingY;
        private float halfWidth;
        private float centreX;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Start() {
            ResolveCamera();
            BuildPool();
            MeasureCamera();
        }

        private void Update() {
            if (bubbleStates == null) {
                return;
            }

            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight) {
                MeasureCamera();
            }

            float deltaTime = Time.deltaTime;

            spawnTimer -= deltaTime;
            if (spawnTimer <= 0f) {
                spawnTimer = spawnIntervalSeconds;
                SpawnBubble();
            }

            AdvanceBubbles(deltaTime);
        }

        private void ResolveCamera() {
            if (targetCamera == null) {
                targetCamera = Camera.main;
            }
        }

        private void BuildPool() {
            int size = Mathf.Max(1, poolSize);

            bubbleTransforms = new Transform[size];
            bubbleRenderers = new SpriteRenderer[size];
            bubbleStates = new BubbleState[size];

            Sprite sprite = ProceduralSpriteLibrary.GetBubble(BubbleTextureSize);

            for (int i = 0; i < size; i++) {
                SpriteRenderer renderer = CreateBubbleRenderer(i, sprite);
                renderer.gameObject.SetActive(false);

                bubbleRenderers[i] = renderer;
                bubbleTransforms[i] = renderer.transform;
            }
        }

        private SpriteRenderer CreateBubbleRenderer(int index, Sprite sprite) {
            if (bubblePrefab != null) {
                SpriteRenderer instance = Instantiate(bubblePrefab, transform);
                instance.name = "Bubble_" + index;

                if (instance.sprite == null) {
                    instance.sprite = sprite;
                }

                instance.sortingOrder = sortingOrder;
                return instance;
            }

            GameObject bubble = new GameObject("Bubble_" + index);
            bubble.transform.SetParent(transform, false);

            SpriteRenderer renderer = bubble.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = bubbleColour;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private void MeasureCamera() {
            ResolveCamera();

            if (targetCamera == null) {
                Debug.LogError("BubbleSystem: no camera available to size the bubble field against.");
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float halfHeight = targetCamera.orthographicSize;
            Vector3 centre = targetCamera.transform.position;

            halfWidth = halfHeight * targetCamera.aspect;
            centreX = centre.x;
            floorY = centre.y - halfHeight;
            ceilingY = centre.y + halfHeight;
        }

        private void SpawnBubble() {
            int index = FindFreeBubble();
            if (index < 0) {
                return;
            }

            float size = UnityEngine.Random.Range(sizeRange.x, sizeRange.y);
            Sprite sprite = bubbleRenderers[index].sprite;
            float spriteHeight = sprite != null ? sprite.bounds.size.y : 1f;
            float scale = spriteHeight > Mathf.Epsilon ? size / spriteHeight : 1f;

            BubbleState state = new BubbleState();
            state.Active = true;
            state.BaseX = UnityEngine.Random.Range(centreX - halfWidth, centreX + halfWidth);
            state.RiseSpeed = UnityEngine.Random.Range(riseSpeedRange.x, riseSpeedRange.y);
            state.DriftPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            state.DriftFrequency = UnityEngine.Random.Range(driftFrequencyRange.x, driftFrequencyRange.y);
            state.DriftAmplitude = driftAmplitude * UnityEngine.Random.Range(0.4f, 1.3f);
            state.Life = 0f;
            state.MaxLife = Mathf.Max(0.5f, (ceilingY - floorY) / Mathf.Max(0.05f, state.RiseSpeed));

            bubbleStates[index] = state;

            bubbleTransforms[index].position = new Vector3(state.BaseX, floorY, 0f);
            bubbleTransforms[index].localScale = new Vector3(scale, scale, 1f);
            bubbleRenderers[index].gameObject.SetActive(true);
        }

        private int FindFreeBubble() {
            for (int offset = 0; offset < bubbleStates.Length; offset++) {
                int index = (spawnCursor + offset) % bubbleStates.Length;
                if (bubbleStates[index].Active) {
                    continue;
                }

                spawnCursor = (index + 1) % bubbleStates.Length;
                return index;
            }

            return -1;
        }

        private void AdvanceBubbles(float deltaTime) {
            for (int i = 0; i < bubbleStates.Length; i++) {
                if (!bubbleStates[i].Active) {
                    continue;
                }

                bubbleStates[i].Life += deltaTime;

                float progress = bubbleStates[i].Life / bubbleStates[i].MaxLife;
                if (progress >= 1f) {
                    bubbleStates[i].Active = false;
                    bubbleRenderers[i].gameObject.SetActive(false);
                    continue;
                }

                float y = Mathf.Lerp(floorY, ceilingY, progress);
                float drift = Mathf.Sin((bubbleStates[i].Life * bubbleStates[i].DriftFrequency * Mathf.PI * 2f) + bubbleStates[i].DriftPhase) * bubbleStates[i].DriftAmplitude;

                bubbleTransforms[i].position = new Vector3(bubbleStates[i].BaseX + drift, y, 0f);

                float fade = Mathf.Min(Mathf.InverseLerp(0f, 0.12f, progress), 1f - Mathf.InverseLerp(0.82f, 1f, progress));
                Color colour = bubbleColour;
                colour.a = bubbleColour.a * Mathf.Clamp01(fade);
                bubbleRenderers[i].color = colour;
            }
        }
    }
}
