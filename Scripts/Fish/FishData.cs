using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Per-fish data model. Identity comes from the source drawing, and every behaviour value is derived
    /// from that identity: from the personality Python sent, or, when it sent none, from a deterministic
    /// hash of the id.
    ///
    /// Nothing here calls UnityEngine.Random. That is the point. Section 6 of the interaction contract
    /// promises that the same drawing always produces the same creature, and a fish whose wander pattern
    /// or draw order was re-rolled at startup would break that promise exactly as visibly as a re-rolled
    /// trait would - the wander pattern is how a fish reads on screen. See docs/decisions.md D-004.
    /// </summary>
    public sealed class FishData {
        /// <summary> Separate stream for values the contract does not cover, so they cannot correlate with the traits. </summary>
        private const string PresentationDomain = "presentation";

        public FishData(int id, string sourceFileName, string sourceFilePath, Sprite sprite, FishTraits traits) {
            Id = id;
            SourceFileName = sourceFileName;
            SourceFilePath = sourceFilePath;
            Sprite = sprite;
            Traits = traits;
        }

        public int Id { get; private set; }

        public string SourceFileName { get; private set; }

        /// <summary> Sprite cache key: an absolute file path for a folder fish, the capture id for a socket fish. </summary>
        public string SourceFilePath { get; private set; }

        public Sprite Sprite { get; private set; }

        /// <summary> The personality Python assigned to this drawing. Immutable, and never re-derived here. </summary>
        public FishTraits Traits { get; private set; }

        public Vector2 SpawnPosition { get; private set; }

        /// <summary> Uniform transform scale that normalises the source PNG to the configured world size. </summary>
        public float WorldScale { get; private set; }

        public float Speed { get; private set; }

        public float TurnSpeed { get; private set; }

        public float Acceleration { get; private set; }

        /// <summary> Multiplier on the configured pitch smoothing. A graceful fish changes attitude slowly. </summary>
        public float SteeringDamping { get; private set; }

        /// <summary>
        /// Seconds this fish waits between mouthfuls. From aggression, and inverted: a pushy fish is back
        /// for the next flake almost at once, a timid one hangs back. Without this, aggression only decides
        /// the rare case where two fish reach the same flake, and reads as doing nothing.
        /// </summary>
        public float EatCooldownSeconds { get; private set; }

        /// <summary>
        /// Parallax: 0 is foreground, 1 is background. Drives scale, opacity, sorting order and speed.
        /// This is NOT personality.preferred_depth, which is the vertical band below. Two different axes.
        /// </summary>
        public float Depth { get; private set; }

        /// <summary> Vertical home band from the personality: 0 hugs the surface, 1 hugs the floor. </summary>
        public float PreferredDepth { get; private set; }

        public float SwimAmplitude { get; private set; }

        public float SwimFrequency { get; private set; }

        public float IdleProbability { get; private set; }

        /// <summary> Per-fish offset into the Perlin noise field so wander patterns never synchronise. </summary>
        public float NoiseSeed { get; private set; }

        /// <summary> Per-fish phase offset for the swim animation so tails do not beat in unison. </summary>
        public float AnimationPhase { get; private set; }

        /// <summary>
        /// Deterministic 0..1 roll used for on-screen size when the payload carried no usable
        /// foreground_area. Held here rather than drawn in the factory so the presentation stream is
        /// consumed in one fixed order.
        /// </summary>
        public float SizeRoll { get; private set; }

        /// <summary>
        /// Turns the personality into motion, once, at spawn. Traits are 0..1 inputs; the response curves
        /// on the config decide what they mean in world units, which is where an artist tunes the feel.
        /// </summary>
        public void ApplyTraitProfile(AquariumConfig config) {
            if (config == null) {
                Debug.LogError("FishData: ApplyTraitProfile called with a null AquariumConfig.");
                return;
            }

            if (Traits == null) {
                Debug.LogError("FishData: ApplyTraitProfile called with no traits; falling back to the identity hash for " + SourceFileName + ".");
                Traits = FishTraitFallback.FromIdentity(SourceFilePath);
            }

            Speed = config.SpeedRange.Evaluate(AquariumConfig.EvaluateResponse(config.SpeedResponse, Traits.Speed));
            Acceleration = config.AccelerationRange.Evaluate(AquariumConfig.EvaluateResponse(config.AccelerationResponse, Traits.Speed));

            // Grace falls as turn rate rises: a graceful fish banks in wide arcs rather than snapping round.
            TurnSpeed = config.TurnSpeedRange.Evaluate(AquariumConfig.EvaluateResponse(config.GraceTurnResponse, Traits.Grace));
            SteeringDamping = config.GraceDampingRange.Evaluate(Mathf.Clamp01(Traits.Grace));

            SwimAmplitude = config.SwimAmplitudeRange.Evaluate(AquariumConfig.EvaluateResponse(config.GraceTailAmplitudeResponse, Traits.Grace));
            SwimFrequency = config.SwimFrequencyRange.Evaluate(AquariumConfig.EvaluateResponse(config.SpeedTailFrequencyResponse, Traits.Speed));
            IdleProbability = config.IdleProbabilityRange.Evaluate(AquariumConfig.EvaluateResponse(config.SpeedIdleResponse, Traits.Speed));

            EatCooldownSeconds = config.AggressionEatCooldownRange.Evaluate(Mathf.Clamp01(Traits.Aggression));

            PreferredDepth = Traits.PreferredDepth;

            // Fixed draw order. Changing it changes where every existing fish sits in the scene.
            DeterministicRandom presentation = DeterministicRandom.FromIdentity(Traits.Identity, PresentationDomain);
            Depth = config.DepthRange.Evaluate(presentation.NextUnit());
            NoiseSeed = presentation.NextRange(0f, 1000f);
            AnimationPhase = presentation.NextRange(0f, Mathf.PI * 2f);
            SizeRoll = presentation.NextUnit();
        }

        public void SetSpawnPosition(Vector2 position) {
            SpawnPosition = position;
        }

        public void SetWorldScale(float scale) {
            WorldScale = scale;
        }
    }
}
