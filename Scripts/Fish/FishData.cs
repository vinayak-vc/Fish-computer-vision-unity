using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Per-fish data model. Identity comes from the source PNG; every behaviour value is randomised
    /// once at spawn from the ranges in AquariumConfig so no two fish move alike.
    /// </summary>
    public sealed class FishData {
        public FishData(int id, string sourceFileName, string sourceFilePath, Sprite sprite) {
            Id = id;
            SourceFileName = sourceFileName;
            SourceFilePath = sourceFilePath;
            Sprite = sprite;
        }

        public int Id { get; private set; }

        public string SourceFileName { get; private set; }

        public string SourceFilePath { get; private set; }

        public Sprite Sprite { get; private set; }

        public Vector2 SpawnPosition { get; private set; }

        /// <summary> Uniform transform scale that normalises the source PNG to the configured world size. </summary>
        public float WorldScale { get; private set; }

        public float Speed { get; private set; }

        public float TurnSpeed { get; private set; }

        public float Acceleration { get; private set; }

        /// <summary> 0 is foreground, 1 is background. Drives scale, opacity, speed and sorting order. </summary>
        public float Depth { get; private set; }

        public float SwimAmplitude { get; private set; }

        public float SwimFrequency { get; private set; }

        public float IdleProbability { get; private set; }

        /// <summary> Per-fish offset into the Perlin noise field so wander patterns never synchronise. </summary>
        public float NoiseSeed { get; private set; }

        /// <summary> Per-fish phase offset for the swim animation so tails do not beat in unison. </summary>
        public float AnimationPhase { get; private set; }

        /// <summary> Rolls every behaviour value from the configured ranges. Called once by FishFactory. </summary>
        public void ApplyRandomisedProfile(AquariumConfig config) {
            if (config == null) {
                Debug.LogError("FishData: ApplyRandomisedProfile called with a null AquariumConfig.");
                return;
            }

            Speed = config.SpeedRange.PickRandom();
            TurnSpeed = config.TurnSpeedRange.PickRandom();
            Acceleration = config.AccelerationRange.PickRandom();
            Depth = config.DepthRange.PickRandom();
            SwimAmplitude = config.SwimAmplitudeRange.PickRandom();
            SwimFrequency = config.SwimFrequencyRange.PickRandom();
            IdleProbability = config.IdleProbabilityRange.PickRandom();
            NoiseSeed = UnityEngine.Random.Range(0f, 1000f);
            AnimationPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        public void SetSpawnPosition(Vector2 position) {
            SpawnPosition = position;
        }

        public void SetWorldScale(float scale) {
            WorldScale = scale;
        }
    }
}
