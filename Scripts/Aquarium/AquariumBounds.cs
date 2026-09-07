using System;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary> Where the swimming area comes from. FitToCamera keeps the aquarium correct on any aspect ratio. </summary>
    public enum AquariumBoundsSource {
        FitToCamera = 0,
        BoxCollider = 1,
        Explicit = 2
    }

    /// <summary>
    /// Logical swimming area, expressed in world units. Nothing in the fish code reads screen pixels;
    /// everything is clamped against this rect, so 16:9, 16:10, 4:3 and fullscreen all behave identically.
    /// </summary>
    public sealed class AquariumBounds : MonoBehaviour {
        [SerializeField] private AquariumBoundsSource source = AquariumBoundsSource.FitToCamera;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private BoxCollider2D boundsCollider;
        [SerializeField] private Vector2 explicitCenter = Vector2.zero;
        [SerializeField] private Vector2 explicitSize = new Vector2(16f, 9f);

        [Header("Camera Fit Margins")]
        [SerializeField] private float horizontalMargin = 0.5f;
        [SerializeField] private float topMargin = 0.5f;
        [Tooltip("Larger bottom margin keeps fish clear of the plants and gravel.")]
        [SerializeField] private float bottomMargin = 1.4f;

        [Header("Gizmo")]
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private Color gizmoColour = new Color(0.25f, 0.85f, 1f, 0.75f);

        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastOrthographicSize;

        /// <summary> Raised whenever the swimming area changes, for example when the window is resized. </summary>
        public event Action<Rect> BoundsChanged;

        public Rect Area { get; private set; }

        public float MinX {
            get { return Area.xMin; }
        }

        public float MaxX {
            get { return Area.xMax; }
        }

        public float MinY {
            get { return Area.yMin; }
        }

        public float MaxY {
            get { return Area.yMax; }
        }

        public Vector2 Center {
            get { return Area.center; }
        }

        private void Awake() {
            Recalculate();
        }

        private void Update() {
            if (source != AquariumBoundsSource.FitToCamera) {
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null) {
                return;
            }

            bool changed = Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || !Mathf.Approximately(camera.orthographicSize, lastOrthographicSize);
            if (changed) {
                Recalculate();
            }
        }

        /// <summary> Rebuilds the swimming area from the configured source. Safe to call at any time. </summary>
        public void Recalculate() {
            Rect updated;

            switch (source) {
                case AquariumBoundsSource.BoxCollider:
                    updated = BuildFromCollider();
                    break;
                case AquariumBoundsSource.Explicit:
                    updated = BuildFromExplicitValues();
                    break;
                default:
                    updated = BuildFromCamera();
                    break;
            }

            if (updated == Area) {
                return;
            }

            Area = updated;

            if (BoundsChanged != null) {
                BoundsChanged(Area);
            }
        }

        /// <summary> Swimming area shrunk on every side by padding, used to keep fish bodies fully inside the view. </summary>
        public Rect GetPaddedArea(float padding) {
            float clampedPadding = Mathf.Max(0f, padding);
            float width = Mathf.Max(0.01f, Area.width - (clampedPadding * 2f));
            float height = Mathf.Max(0.01f, Area.height - (clampedPadding * 2f));
            return new Rect(Area.center.x - (width * 0.5f), Area.center.y - (height * 0.5f), width, height);
        }

        public bool Contains(Vector2 position) {
            return Area.Contains(position);
        }

        public Vector2 Clamp(Vector2 position, float padding) {
            Rect padded = GetPaddedArea(padding);
            return new Vector2(Mathf.Clamp(position.x, padded.xMin, padded.xMax), Mathf.Clamp(position.y, padded.yMin, padded.yMax));
        }

        public Vector2 RandomPointInside(float padding) {
            Rect padded = GetPaddedArea(padding);
            return new Vector2(UnityEngine.Random.Range(padded.xMin, padded.xMax), UnityEngine.Random.Range(padded.yMin, padded.yMax));
        }

        /// <summary> A point on the left or right edge plus the heading that carries the fish into the aquarium. </summary>
        public Vector2 RandomEdgeEntryPoint(float padding, out Vector2 inwardDirection) {
            Rect padded = GetPaddedArea(padding);
            bool fromRight = UnityEngine.Random.value < 0.5f;

            float x = fromRight ? padded.xMax : padded.xMin;
            float y = UnityEngine.Random.Range(padded.yMin + (padded.height * 0.15f), padded.yMax - (padded.height * 0.15f));

            inwardDirection = fromRight ? Vector2.left : Vector2.right;
            return new Vector2(x, y);
        }

        /// <summary> Assigns the bounds source and values from code. Used by the edit-mode tests. </summary>
        public void ConfigureExplicit(Vector2 center, Vector2 size) {
            source = AquariumBoundsSource.Explicit;
            explicitCenter = center;
            explicitSize = size;
            Recalculate();
        }

        private Camera ResolveCamera() {
            if (targetCamera != null) {
                return targetCamera;
            }

            targetCamera = Camera.main;
            return targetCamera;
        }

        private Rect BuildFromCamera() {
            Camera camera = ResolveCamera();
            if (camera == null) {
                return BuildFromExplicitValues();
            }

            if (!camera.orthographic) {
                Debug.LogError("AquariumBounds: the aquarium camera must be orthographic.");
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastOrthographicSize = camera.orthographicSize;

            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            Vector3 cameraPosition = camera.transform.position;

            float minX = cameraPosition.x - halfWidth + horizontalMargin;
            float maxX = cameraPosition.x + halfWidth - horizontalMargin;
            float minY = cameraPosition.y - halfHeight + bottomMargin;
            float maxY = cameraPosition.y + halfHeight - topMargin;

            return BuildRect(minX, minY, maxX, maxY);
        }

        private Rect BuildFromCollider() {
            if (boundsCollider == null) {
                Debug.LogError("AquariumBounds: BoxCollider source selected but no BoxCollider2D is assigned.");
                return BuildFromExplicitValues();
            }

            Bounds colliderBounds = boundsCollider.bounds;
            return BuildRect(colliderBounds.min.x, colliderBounds.min.y, colliderBounds.max.x, colliderBounds.max.y);
        }

        private Rect BuildFromExplicitValues() {
            float halfWidth = Mathf.Abs(explicitSize.x) * 0.5f;
            float halfHeight = Mathf.Abs(explicitSize.y) * 0.5f;
            return BuildRect(explicitCenter.x - halfWidth, explicitCenter.y - halfHeight, explicitCenter.x + halfWidth, explicitCenter.y + halfHeight);
        }

        private static Rect BuildRect(float minX, float minY, float maxX, float maxY) {
            float width = Mathf.Max(0.01f, maxX - minX);
            float height = Mathf.Max(0.01f, maxY - minY);
            return new Rect(minX, minY, width, height);
        }

        private void OnDrawGizmos() {
            if (!drawGizmo) {
                return;
            }

            Rect area = Application.isPlaying ? Area : PreviewArea();
            Gizmos.color = gizmoColour;
            Gizmos.DrawWireCube(new Vector3(area.center.x, area.center.y, 0f), new Vector3(area.width, area.height, 0.01f));
        }

        private Rect PreviewArea() {
            switch (source) {
                case AquariumBoundsSource.BoxCollider:
                    return BuildFromCollider();
                case AquariumBoundsSource.Explicit:
                    return BuildFromExplicitValues();
                default:
                    return BuildFromCamera();
            }
        }
    }
}
