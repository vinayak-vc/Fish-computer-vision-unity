using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Eggs waiting to hatch. Design point 5, the middle step between a pair forming and a baby swimming.
    ///
    /// Pooled and fixed-capacity like FoodField and RippleField, ticked by AquariumManager, and a plain
    /// class so the whole incubation can be tested without a scene.
    ///
    /// An egg carries what the baby will need, because by the time it hatches a parent may well be gone -
    /// removed to make room, or lost to a restart. Holding the blended traits on the egg rather than
    /// looking the parents up at hatch time means a brood is never orphaned halfway.
    /// </summary>
    public sealed class EggField {
        /// <summary> What one egg is, and everything the baby inherits from it. </summary>
        public struct Egg {
            public Vector2 Position { get; set; }
            public float Age { get; set; }
            public bool Alive { get; set; }

            /// <summary> Personality already blended from the parents, so neither parent need still exist. </summary>
            public FishTraits Traits { get; set; }

            /// <summary> Sprite cache key of the parent whose drawing the baby wears. </summary>
            public string SpriteKey { get; set; }

            /// <summary> Blended colour the baby is tinted with. </summary>
            public Color Tint { get; set; }

            /// <summary> On-screen size, already averaged from the parents and shrunk. </summary>
            public float WorldScale { get; set; }
        }

        private readonly Egg[] eggs;
        private readonly float incubationSeconds;

        private int activeCount;

        public EggField(int capacity, float secondsToHatch) {
            eggs = new Egg[Mathf.Max(1, capacity)];
            incubationSeconds = Mathf.Max(0.1f, secondsToHatch);
        }

        public int ActiveCount {
            get { return activeCount; }
        }

        public int Capacity {
            get { return eggs.Length; }
        }

        /// <summary> Lays an egg. Refused rather than recycling one, because an egg about to hatch is a fish. </summary>
        public bool TryLay(Vector2 position, FishTraits blendedTraits, string spriteKey, Color tint, float worldScale) {
            for (int i = 0; i < eggs.Length; i++) {
                if (eggs[i].Alive) {
                    continue;
                }

                eggs[i].Position = position;
                eggs[i].Age = 0f;
                eggs[i].Alive = true;
                eggs[i].Traits = blendedTraits;
                eggs[i].SpriteKey = spriteKey;
                eggs[i].Tint = tint;
                eggs[i].WorldScale = worldScale;

                activeCount++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ages every egg and reports the ones ready to hatch. They stay alive until the caller confirms,
        /// so an egg the aquarium has no room for waits rather than being lost.
        /// </summary>
        public int CollectHatched(float deltaTime, List<int> hatchedIndices) {
            if (hatchedIndices != null) {
                hatchedIndices.Clear();
            }

            if (activeCount == 0) {
                return 0;
            }

            for (int i = 0; i < eggs.Length; i++) {
                if (!eggs[i].Alive) {
                    continue;
                }

                eggs[i].Age += deltaTime;

                if (eggs[i].Age >= incubationSeconds && hatchedIndices != null) {
                    hatchedIndices.Add(i);
                }
            }

            return hatchedIndices != null ? hatchedIndices.Count : 0;
        }

        public bool TryGetEgg(int index, out Egg egg) {
            egg = default(Egg);

            if (index < 0 || index >= eggs.Length || !eggs[index].Alive) {
                return false;
            }

            egg = eggs[index];
            return true;
        }

        /// <summary> Normalised age for the renderer, so an egg can visibly ripen. </summary>
        public bool TryGetVisual(int index, out Vector2 position, out Color tint, out float ripeness) {
            position = Vector2.zero;
            tint = Color.white;
            ripeness = 0f;

            if (index < 0 || index >= eggs.Length || !eggs[index].Alive) {
                return false;
            }

            position = eggs[index].Position;
            tint = eggs[index].Tint;
            ripeness = Mathf.Clamp01(eggs[index].Age / incubationSeconds);
            return true;
        }

        /// <summary> Removes a hatched egg. Called once the baby actually exists. </summary>
        public void Remove(int index) {
            if (index < 0 || index >= eggs.Length || !eggs[index].Alive) {
                return;
            }

            eggs[index].Alive = false;
            eggs[index].Traits = null;
            eggs[index].SpriteKey = null;
            activeCount--;
        }

        public void Clear() {
            for (int i = 0; i < eggs.Length; i++) {
                eggs[i].Alive = false;
                eggs[i].Traits = null;
                eggs[i].SpriteKey = null;
            }

            activeCount = 0;
        }
    }
}
