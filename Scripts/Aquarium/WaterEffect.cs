using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Slow-moving light shafts falling from the surface. Built from generated sprites and animated
    /// with a sine on rotation and opacity, so it adds atmosphere without a shader or a post pass.
    /// A scrolling caustics material can be layered on later by driving the same renderers.
    /// </summary>
    public sealed class WaterEffect : MonoBehaviour {
        private const int RayTextureWidth = 64;
        private const int RayTextureHeight = 512;

        [SerializeField] private Camera targetCamera;
        [SerializeField] private int rayCount = 5;
        [SerializeField] private Color rayColour = new Color(0.78f, 0.95f, 1f, 0.055f);
        [SerializeField] private Vector2 rayWidthRange = new Vector2(0.7f, 2.3f);
        [SerializeField] private float baseTiltDegrees = 11f;
        [SerializeField] private float swayDegrees = 2.6f;
        [SerializeField] private float swaySpeed = 0.18f;
        [SerializeField] private float opacityPulse = 0.35f;
        [SerializeField] private int sortingOrder = 240;

        private Transform[] rayTransforms;
        private SpriteRenderer[] rayRenderers;
        private float[] rayPhases;
        private float[] rayBaseAngles;
        private float[] normalisedPositions;
        private float animationTime;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Start() {
            ResolveCamera();
            BuildRays();
            LayOutRays();
        }

        private void Update() {
            if (rayTransforms == null) {
                return;
            }

            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight) {
                LayOutRays();
            }

            animationTime += Time.deltaTime * swaySpeed;

            for (int i = 0; i < rayTransforms.Length; i++) {
                float wave = Mathf.Sin(animationTime + rayPhases[i]);

                rayTransforms[i].localRotation = Quaternion.Euler(0f, 0f, rayBaseAngles[i] + (wave * swayDegrees));

                Color colour = rayColour;
                colour.a = rayColour.a * (1f + (wave * opacityPulse));
                rayRenderers[i].color = colour;
            }
        }

        private void ResolveCamera() {
            if (targetCamera == null) {
                targetCamera = Camera.main;
            }
        }

        private void BuildRays() {
            int count = Mathf.Max(1, rayCount);

            rayTransforms = new Transform[count];
            rayRenderers = new SpriteRenderer[count];
            rayPhases = new float[count];
            rayBaseAngles = new float[count];
            normalisedPositions = new float[count];

            Sprite sprite = ProceduralSpriteLibrary.GetLightRay(RayTextureWidth, RayTextureHeight);

            for (int i = 0; i < count; i++) {
                GameObject ray = new GameObject("LightRay_" + i);
                ray.transform.SetParent(transform, false);

                SpriteRenderer renderer = ray.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = rayColour;
                renderer.sortingOrder = sortingOrder;

                rayTransforms[i] = ray.transform;
                rayRenderers[i] = renderer;
                rayPhases[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                rayBaseAngles[i] = baseTiltDegrees + UnityEngine.Random.Range(-4f, 4f);
                normalisedPositions[i] = count > 1 ? ((i + UnityEngine.Random.Range(0.15f, 0.85f)) / count) : 0.5f;
            }
        }

        private void LayOutRays() {
            ResolveCamera();

            if (targetCamera == null) {
                Debug.LogError("WaterEffect: no camera available to place light rays against.");
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float halfHeight = targetCamera.orthographicSize;
            float halfWidth = halfHeight * targetCamera.aspect;
            Vector3 centre = targetCamera.transform.position;
            float viewHeight = halfHeight * 2f;

            for (int i = 0; i < rayTransforms.Length; i++) {
                Sprite sprite = rayRenderers[i].sprite;
                Vector2 spriteSize = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;

                float width = UnityEngine.Random.Range(rayWidthRange.x, rayWidthRange.y);
                float scaleX = spriteSize.x > Mathf.Epsilon ? width / spriteSize.x : 1f;
                float scaleY = spriteSize.y > Mathf.Epsilon ? (viewHeight * 1.35f) / spriteSize.y : 1f;

                float x = Mathf.Lerp(centre.x - halfWidth, centre.x + halfWidth, normalisedPositions[i]);

                rayTransforms[i].position = new Vector3(x, centre.y + halfHeight, 0f);
                rayTransforms[i].localScale = new Vector3(scaleX, scaleY, 1f);
            }
        }
    }
}
