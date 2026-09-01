using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// A clump of leaves that sways in the current. Leaves are generated on Start when none are
    /// authored, so the prefab stays a bare root. The sway is a sine on each leaf transform, not a
    /// simulation, so a bed of plants costs almost nothing.
    /// </summary>
    public sealed class PlantController : MonoBehaviour {
        private const int LeafTextureWidth = 16;
        private const int LeafTextureHeight = 200;

        [SerializeField] private int leafCount = 5;
        [SerializeField] private Color leafColour = new Color(0.07f, 0.23f, 0.15f, 1f);
        [SerializeField] private int sortingOrder = -60;
        [SerializeField] private float swayDegrees = 7f;
        [SerializeField] private float swaySpeed = 0.7f;
        [SerializeField] private Vector2 leafHeightRange = new Vector2(1.0f, 2.3f);
        [SerializeField] private float leafSpread = 0.42f;

        private Transform[] leaves;
        private float[] basePhases;
        private float[] baseAngles;
        private float animationTime;

        private void Start() {
            if (leaves != null) {
                return;
            }

            BuildLeaves();
        }

        private void Update() {
            if (leaves == null) {
                return;
            }

            animationTime += Time.deltaTime * swaySpeed;

            for (int i = 0; i < leaves.Length; i++) {
                Transform leaf = leaves[i];
                if (leaf == null) {
                    continue;
                }

                float angle = baseAngles[i] + (Mathf.Sin(animationTime + basePhases[i]) * swayDegrees);
                leaf.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        /// <summary> Re-rolls the clump. Called by AquariumDecor after it positions the plant. </summary>
        public void Rebuild(int count, Color colour, int order) {
            leafCount = Mathf.Max(1, count);
            leafColour = colour;
            sortingOrder = order;
            BuildLeaves();
        }

        private void BuildLeaves() {
            ClearExistingLeaves();

            leaves = new Transform[leafCount];
            basePhases = new float[leafCount];
            baseAngles = new float[leafCount];

            Sprite leafSprite = ProceduralSpriteLibrary.GetLeaf(LeafTextureWidth, LeafTextureHeight);

            for (int i = 0; i < leafCount; i++) {
                GameObject leaf = new GameObject("Leaf_" + i);
                leaf.transform.SetParent(transform, false);

                float horizontal = leafCount > 1 ? Mathf.Lerp(-leafSpread, leafSpread, (float)i / (leafCount - 1)) : 0f;
                horizontal += UnityEngine.Random.Range(-0.05f, 0.05f);

                float height = UnityEngine.Random.Range(leafHeightRange.x, leafHeightRange.y);
                float spriteHeight = leafSprite.bounds.size.y;
                float scale = spriteHeight > Mathf.Epsilon ? height / spriteHeight : 1f;

                leaf.transform.localPosition = new Vector3(horizontal, 0f, 0f);
                leaf.transform.localScale = new Vector3(scale, scale, 1f);

                float tint = UnityEngine.Random.Range(0.8f, 1.15f);

                SpriteRenderer renderer = leaf.AddComponent<SpriteRenderer>();
                renderer.sprite = leafSprite;
                renderer.color = new Color(leafColour.r * tint, leafColour.g * tint, leafColour.b * tint, leafColour.a);
                renderer.sortingOrder = sortingOrder + i;

                baseAngles[i] = horizontal * 24f;
                basePhases[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                leaves[i] = leaf.transform;
            }
        }

        private void ClearExistingLeaves() {
            if (leaves == null) {
                return;
            }

            for (int i = 0; i < leaves.Length; i++) {
                if (leaves[i] == null) {
                    continue;
                }

                Destroy(leaves[i].gameObject);
            }

            leaves = null;
        }
    }
}
