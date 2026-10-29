using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary> Single source of truth for every tunable value in the aquarium. Created via Assets > Create > Fish Aquarium > Aquarium Config. </summary>
    [CreateAssetMenu(fileName = "AquariumConfig", menuName = "Fish Aquarium/Aquarium Config", order = 0)]
    public sealed class AquariumConfig : ScriptableObject {
        [Header("Runtime Mode")]
        [SerializeField] private AquariumRuntimeMode runtimeMode = AquariumRuntimeMode.Development;
        [SerializeField] private bool fullscreenInProduction = true;
        [SerializeField] private int targetFrameRate = 60;
        [SerializeField] private int vSyncCount = 1;
        [SerializeField] private bool allowExternalSettingsOverride = true;

        [Header("Fish Input Folder")]
        [Tooltip("Root the relative path below is resolved against. Absolute uses the explicit path field instead.")]
        [SerializeField] private InputFolderRoot inputFolderRoot = InputFolderRoot.StreamingAssets;
        [SerializeField] private string inputFolderRelativePath = "FishInput";
        [SerializeField] private string inputFolderAbsolutePath = string.Empty;
        [SerializeField] private string fileSearchPattern = "*.png";
        [SerializeField] private bool createInputFolderIfMissing = true;

        [Header("Fish Input Behaviour")]
        [SerializeField] private bool loadExistingFishOnStartup = true;
        [SerializeField] private bool watchForNewFiles = true;
        [Tooltip("Seconds a newly detected file must stay unchanged before Unity attempts to read it.")]
        [SerializeField] private float fileStabilitySeconds = 0.35f;
        [Tooltip("Seconds a pending file may stay unreadable before it is abandoned with a warning.")]
        [SerializeField] private float fileReadTimeoutSeconds = 20f;
        [Tooltip("Safety-net rescan interval in seconds. 0 disables it; the FileSystemWatcher stays the primary source.")]
        [SerializeField] private float periodicRescanSeconds = 15f;
        [Tooltip("Maximum PNGs decoded per frame. Keeps a burst of new files from stalling the render loop.")]
        [SerializeField] private int maxFishLoadsPerFrame = 1;
        [Tooltip("Section 45 default: a deleted PNG leaves its already-spawned fish alone.")]
        [SerializeField] private bool removeFishWhenSourceFileDeleted = false;

        [Header("Test Fish (development only)")]
        [SerializeField] private bool loadTestFishInDevelopment = true;
        [SerializeField] private string testFishFolderRelativePath = "Sample";

        [Header("Aquarium Population")]
        [SerializeField] private int maxFishCount = 30;
        [SerializeField] private FishOverflowStrategy overflowStrategy = FishOverflowStrategy.RemoveOldest;
        [SerializeField] private FishSpawnStrategy spawnStrategy = FishSpawnStrategy.EdgeEntry;
        [SerializeField] private float spawnAnimationDuration = 0.7f;
        [SerializeField] private float despawnAnimationDuration = 0.5f;
        [SerializeField] private float spawnStartScaleMultiplier = 0.55f;

        [Header("Fish Size Normalisation")]
        [Tooltip("World-unit length of the longest side of the fish. Aspect ratio of the source PNG is always preserved.")]
        [SerializeField] private FloatRange fishWorldSizeRange = new FloatRange(1.7f, 2.7f);
        [SerializeField] private float spritePixelsPerUnit = 100f;
        [Tooltip("Size the fish from foreground_area, so a small drawing stays a small fish. Off falls back to the deterministic identity roll.")]
        [SerializeField] private bool sizeFromForegroundArea = true;
        [Tooltip("Mask area in source pixels that maps to the smallest fish in the range above.")]
        [SerializeField] private int foregroundAreaAtMinimumSize = 40000;
        [Tooltip("Mask area in source pixels that maps to the largest fish in the range above. Anything larger is clamped.")]
        [SerializeField] private int foregroundAreaAtMaximumSize = 420000;
        [Tooltip("Shapes the mapping between those two areas. Input and output are both 0..1.")]
        [SerializeField] private AnimationCurve foregroundAreaResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Fish Movement")]
        [SerializeField] private FloatRange speedRange = new FloatRange(0.5f, 1.5f);
        [SerializeField] private FloatRange turnSpeedRange = new FloatRange(30f, 90f);
        [SerializeField] private FloatRange accelerationRange = new FloatRange(0.8f, 2.0f);
        [Tooltip("Peak heading deviation in degrees driven by Perlin noise. Keeps motion organic without jitter.")]
        [SerializeField] private float directionNoiseDegrees = 22f;
        [Tooltip("Perlin sampling rate. Low values give slow, smooth wander.")]
        [SerializeField] private float directionNoiseFrequency = 0.28f;
        [Tooltip("Scales the vertical part of target seeking. Below 1 the fish cruise in flatter, more fish-like arcs instead of diving.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float verticalSteeringDamping = 0.55f;
        [SerializeField] private float targetReachDistance = 0.45f;
        [SerializeField] private float minimumTargetTravelDistance = 2.2f;
        [SerializeField] private FloatRange idleProbabilityRange = new FloatRange(0.05f, 0.25f);
        [SerializeField] private FloatRange idleDurationRange = new FloatRange(0.6f, 2.2f);
        [Tooltip("Distance from the aquarium edge at which fish start steering back inwards.")]
        [SerializeField] private float boundsAvoidanceMargin = 1.1f;
        [SerializeField] private float boundsAvoidanceStrength = 2.4f;
        [SerializeField] private float boundsPadding = 0.35f;

        [Header("Trait Response")]
        [Tooltip("Python sends traits, never behaviour. These curves are where a 0..1 trait becomes motion, and they are the intended place to tune how a personality reads on screen. Input is the trait, output is a 0..1 position within the matching range above.")]
        [SerializeField] private AnimationCurve speedResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve accelerationResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Grace to turn rate. A graceful fish banks in wide arcs, so this curve normally falls: high grace means a LOW turn-rate limit.")]
        [SerializeField] private AnimationCurve graceTurnResponse = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("Grace to steering damping, multiplying the pitch smoothing below. A graceful fish changes attitude slowly.")]
        [SerializeField] private FloatRange graceDampingRange = new FloatRange(1.6f, 0.5f);
        [Tooltip("Tail-beat amplitude. Driven by grace: a graceful fish sweeps further.")]
        [SerializeField] private AnimationCurve graceTailAmplitudeResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Tail-beat frequency. Driven by speed: a darting fish beats faster.")]
        [SerializeField] private AnimationCurve speedTailFrequencyResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Idle probability across the trait speed range. A sluggish fish pauses more, so this normally falls.")]
        [SerializeField] private AnimationCurve speedIdleResponse = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Preferred Depth")]
        [Tooltip("Vertical home band from personality.preferred_depth: 0 is the surface, 1 is the floor. This is NOT parallax depth, which is the Depth block below.")]
        [SerializeField] private bool preferredDepthEnabled = true;
        [Tooltip("Height of the band a fish keeps to, as a fraction of the aquarium height. Larger values let fish roam more vertically.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float preferredDepthBandHeight = 0.45f;
        [Tooltip("How hard a fish is pulled back towards its band once outside it. 0 disables the band without disabling the trait.")]
        [SerializeField] private float preferredDepthHomingStrength = 1.3f;

        [Header("Fish Separation")]
        [SerializeField] private bool separationEnabled = true;
        [SerializeField] private float separationRadius = 1.0f;
        [SerializeField] private float separationStrength = 0.9f;

        [Header("Flocking")]
        [Tooltip("Alignment and cohesion, the other two thirds of boids. Separation above is the first third and is independent of this switch.")]
        [SerializeField] private bool flockingEnabled = true;
        [Tooltip("How far a fish looks for schoolmates. Also sets the spatial hash cell size, so raising it makes neighbour queries wider and slower.")]
        [SerializeField] private float neighbourRadius = 2.6f;
        [Tooltip("Turn towards the average heading of the school, at social 1.")]
        [SerializeField] private float alignmentStrength = 1.15f;
        [Tooltip("Draw towards the centre of the school, at social 1. Kept below the depth homing strength so a school still respects its water layer.")]
        [SerializeField] private float cohesionStrength = 0.95f;
        [Tooltip("Social to alignment weight. A solitary fish should get nothing here, or low-social fish will drift into the schools they are supposed to stay out of.")]
        [SerializeField] private AnimationCurve socialAlignmentResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Social to cohesion weight.")]
        [SerializeField] private AnimationCurve socialCohesionResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Neighbours one fish will consider in a frame. A hard ceiling on the per-fish cost when the whole shoal piles into one corner, which is exactly when the frame budget matters.")]
        [SerializeField] private int maxNeighboursConsidered = 32;

        [Header("New Arrival Recognition")]
        [Tooltip("A fish that has just been drawn draws a crowd, and the crowd is made of the curious ones. Design point 16.")]
        [SerializeField] private bool noveltyEnabled = true;
        [Tooltip("Seconds a new fish stays interesting. Its pull fades to nothing across this.")]
        [SerializeField] private float noveltySeconds = 5f;
        [Tooltip("How far the interest carries. Larger than the neighbour radius on purpose - fish notice an arrival from across part of the tank.")]
        [SerializeField] private float noveltyRadius = 5f;
        [SerializeField] private float noveltyAttractionStrength = 1.7f;
        [Tooltip("Curiosity to how strongly this fish investigates an arrival.")]
        [SerializeField] private AnimationCurve curiosityNoveltyResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Fish Orientation")]
        [Tooltip("Describes the source PNG, not the swimming direction: tick it only when the drawings have their nose pointing RIGHT. The sample drawings face left, so this is off by default. Getting it wrong makes every fish swim tail first.")]
        [SerializeField] private bool artworkFacesRight = false;
        [Tooltip("Horizontal direction required before the sprite flips. Prevents flicker when swimming near-vertically.")]
        [SerializeField] private float facingFlipThreshold = 0.16f;
        [Tooltip("Maximum nose-up or nose-down tilt. The fish never rolls upside down.")]
        [SerializeField] private float maxPitchAngle = 32f;
        [SerializeField] private float pitchSmoothing = 6f;

        [Header("Fish Swim Animation")]
        [SerializeField] private FloatRange swimAmplitudeRange = new FloatRange(0.02f, 0.06f);
        [SerializeField] private FloatRange swimFrequencyRange = new FloatRange(0.8f, 1.8f);
        [SerializeField] private float bodyRollDegrees = 4.5f;
        [SerializeField] private float breathingScaleAmount = 0.025f;

        [Header("Depth")]
        [SerializeField] private FloatRange depthRange = new FloatRange(0f, 1f);
        [Tooltip("How much a fully background fish shrinks. 0.4 renders it at 60 percent of its normalised size.")]
        [SerializeField] private float depthScaleInfluence = 0.32f;
        [Tooltip("How much a fully background fish fades. 0.25 renders it at 75 percent opacity.")]
        [SerializeField] private float depthAlphaInfluence = 0.25f;
        [SerializeField] private float depthSpeedInfluence = 0.3f;
        [SerializeField] private int foregroundSortingOrder = 200;
        [SerializeField] private int backgroundSortingOrder = 20;

        [Header("Pointer Interaction")]
        [Tooltip("The visitor reaches the fish only through the pointer: there is no person or hand tracking in this build. Turning this off leaves the tank running as an unattended screensaver.")]
        [SerializeField] private bool pointerInteractionEnabled = true;
        [Tooltip("World-unit radius within which a fish notices the pointer at all.")]
        [SerializeField] private float pointerInfluenceRadius = 4.5f;
        [Tooltip("Pull towards the pointer at curiosity 1. Scaled by curiosity, and by how close the pointer is.")]
        [SerializeField] private float pointerAttractionStrength = 1.7f;
        [Tooltip("Push away from the pointer at fear 1. Deliberately stronger than attraction, so a scared fish wins over a curious one.")]
        [SerializeField] private float pointerRepulsionStrength = 3.4f;
        [Tooltip("Fish closer than this to the pointer always back off, whatever their curiosity. Stops a curious fish sitting under the cursor.")]
        [SerializeField] private float pointerPersonalSpace = 0.9f;

        [Header("Pointer Startle")]
        [Tooltip("Pointer speed in world units per second that startles a maximally fearful fish. A calm fish needs the multiplier below on top.")]
        [SerializeField] private float startleSpeedThreshold = 3.5f;
        [Tooltip("How much harder it is to startle a fearless fish. 3 means a fear-0 fish needs three times the pointer speed a fear-1 fish does.")]
        [SerializeField] private float startleFearlessMultiplier = 3.5f;
        [SerializeField] private float startleDurationSeconds = 1.5f;
        [Tooltip("Extra flee force while startled, on top of the ordinary fear repulsion.")]
        [SerializeField] private float startleStrength = 4.5f;
        [Tooltip("Speed multiplier applied while a fish is startled, so a bolt actually looks like a bolt.")]
        [SerializeField] private float startleSpeedMultiplier = 2.1f;

        [Header("Pointer Ripples")]
        [Tooltip("A click sends out a radial force field, strength over distance, decaying over its lifetime.")]
        [SerializeField] private bool rippleOnClickEnabled = true;
        [SerializeField] private float rippleStrength = 8f;
        [SerializeField] private float rippleDurationSeconds = 2.2f;
        [Tooltip("World-unit reach of one ripple. Beyond this it is ignored, which is what keeps the cost bounded.")]
        [SerializeField] private float rippleRadius = 7f;
        [Tooltip("Radius of the temporary danger zone a click leaves behind, which fish route around until the ripple dies.")]
        [SerializeField] private float rippleDangerRadius = 2.4f;
        [Tooltip("Ripples alive at once. Older ones are dropped when a visitor clicks faster than they decay.")]
        [SerializeField] private int maxConcurrentRipples = 8;

        [Header("Pointer Affection")]
        [Tooltip("A pointer that lingers slowly near a fish earns its trust, and a trusting fish approaches instead of fleeing.")]
        [SerializeField] private bool affectionEnabled = true;
        [SerializeField] private float affectionProximityRadius = 2.6f;
        [Tooltip("Pointer speed below which proximity counts as calm company rather than a threat.")]
        [SerializeField] private float affectionPointerSpeedLimit = 1.2f;
        [SerializeField] private float affectionGainPerSecond = 0.22f;
        [Tooltip("How fast trust fades when the pointer is away. Slow on purpose: affection is meant to persist across a visit, and across restarts once M5 lands.")]
        [SerializeField] private float affectionDecayPerSecond = 0.02f;
        [Tooltip("Affection above which fear inverts into approach. Below it, affection only softens the flee response.")]
        [Range(0f, 1f)]
        [SerializeField] private float affectionTrustThreshold = 0.55f;

        [Header("Feeding")]
        [Tooltip("A press near the surface drops food instead of a ripple. Design point 2.")]
        [SerializeField] private bool feedingEnabled = true;
        [Tooltip("How much of the tank height, measured down from the surface, counts as the feeding band. A press inside it feeds; anywhere else sends a shockwave.")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float feedSurfaceBand = 0.18f;
        [Tooltip("Flakes dropped per press. A pinch rather than a single crumb, so a crowd has something to form around.")]
        [SerializeField] private int foodPerPinch = 6;
        [Tooltip("World-unit horizontal scatter of one pinch.")]
        [SerializeField] private float foodPinchSpread = 1.5f;
        [SerializeField] private int maxFoodParticles = 48;
        [SerializeField] private float foodSinkSpeed = 0.55f;
        [Tooltip("Seconds before a flake goes stale and vanishes, whether or not anything ate it.")]
        [SerializeField] private float foodLifetimeSeconds = 16f;
        [Tooltip("How close a fish must be to eat. Also the range within which fish compete for the same flake.")]
        [SerializeField] private float foodEatRadius = 0.35f;
        [Tooltip("How far a fish notices food. Generous on purpose: the crowd is the point.")]
        [SerializeField] private float foodAwarenessRadius = 5.5f;
        [Tooltip("Pull towards food. Deliberately stronger than the depth homing, so a surface-dweller will dive for a sinking flake.")]
        [SerializeField] private float foodAttractionStrength = 2.6f;
        [Tooltip("Curiosity to how hard this fish goes for food. Note the curve does not start at zero: a fish that never noticed food at all would look broken rather than incurious.")]
        [SerializeField] private AnimationCurve curiosityFoodResponse = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);
        [Tooltip("Seconds between mouthfuls, across the aggression range. Inverted on purpose: a pushy fish is straight back in for the next flake, a timid one hesitates. This is what makes aggression visible when flakes are spread out and no two fish are actually contesting one.")]
        [SerializeField] private FloatRange aggressionEatCooldownRange = new FloatRange(1.5f, 0.3f);

        [Header("Food Appearance")]
        [SerializeField] private Color foodColour = new Color(0.72f, 0.46f, 0.22f, 1f);
        [SerializeField] private float foodWorldSize = 0.14f;
        [SerializeField] private int foodSortingOrder = 210;

        [Header("Rarity And Anatomy")]
        [Tooltip("Design points 7 and 26. A rare drawing glows, trails and is announced more loudly; eye highlights are pinned to Python's landmarks when it sends them.")]
        [SerializeField] private bool rarityEffectsEnabled = true;
        [Tooltip("Rarity score to glow. Ordinary drawings should get nothing: if everything glows, nothing looks rare.")]
        [SerializeField] private AnimationCurve rarityGlowResponse = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float rarityMaximumGlow = 0.7f;
        [Tooltip("Lowest tier that trails. Gated on the tier so the score thresholds stay in Python's hands.")]
        [SerializeField] private FishRarityTier minimumTrailTier = FishRarityTier.Rare;
        [SerializeField] private float rarityTrailSeconds = 0.55f;
        [SerializeField] private float rarityTrailStartWidth = 0.35f;
        [Tooltip("How much louder a legendary drawing's birth moment is than a common one's, in both length and brightness.")]
        [SerializeField] private float rarityBirthLoudness = 2.2f;

        [Header("Eye Highlight")]
        [Tooltip("Draw a highlight on the eye. Uses Python's anatomy landmark when it sends one and a geometric fallback otherwise - which today is always, since the anatomy block is null until Python phase 2.")]
        [SerializeField] private bool eyeHighlightEnabled = true;
        [Tooltip("Eye size as a fraction of the fish's longest side, when the landmark carries no radius.")]
        [SerializeField] private float eyeHighlightScale = 0.9f;
        [SerializeField] private Color eyeHighlightColour = new Color(0.05f, 0.06f, 0.09f, 0.85f);

        [Header("Non-Fish Drawings")]
        [Tooltip("Design points 21 and 22, contract section 5. Plants and rocks anchor to the floor, jellyfish drift and pulse and ignore flocking, creatures may be predators. A misclassification must be survivable, never fatal.")]
        [SerializeField] private bool objectTypeBehavioursEnabled = true;
        [Tooltip("How far a plant leans, in degrees, and how fast. Rocks do not move at all.")]
        [SerializeField] private float plantSwayDegrees = 9f;
        [SerializeField] private float plantSwaySpeed = 0.5f;
        [Tooltip("World units a jellyfish rises and falls as it pulses.")]
        [SerializeField] private float jellyfishPulseAmplitude = 0.55f;
        [SerializeField] private float jellyfishPulseSpeed = 0.6f;
        [Tooltip("Fraction of its normal speed a jellyfish drifts at. It is a drifter, not a swimmer.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float jellyfishDriftFactor = 0.35f;
        [Tooltip("How much a jellyfish brightens and dims as it pulses. This is the type-specific effect that a low-confidence classification skips.")]
        [SerializeField] private float jellyfishGlowAmount = 0.35f;
        [Tooltip("Aggression a creature needs before other fish treat it as a predator. Below this it is just an odd-looking fish.")]
        [Range(0f, 1f)]
        [SerializeField] private float creaturePredatorAggressionThreshold = 0.6f;
        [Tooltip("How far a predatory creature frightens other fish. Much smaller than the procedural predator's reach: this is a bully, not an event.")]
        [SerializeField] private float creatureFearRadius = 3.2f;
        [SerializeField] private float creatureFleeStrength = 2.6f;

        [Header("Social Bonds")]
        [Tooltip("Design points 5 and 8. Fish that keep each other company form a pair, a pair produces an egg, and the egg hatches a baby whose looks and personality come from both parents.")]
        [SerializeField] private bool socialBondsEnabled = true;
        [Tooltip("How close two fish must be to count as keeping company.")]
        [SerializeField] private float bondRadius = 1.8f;
        [Tooltip("Seconds of company before a pair forms. Both fish choosing each other accrues this twice as fast as one following the other around.")]
        [SerializeField] private float secondsTogetherToPair = 14f;
        [Tooltip("How fast familiarity fades when two fish are apart. This is what makes the requirement SUSTAINED company rather than cumulative.")]
        [SerializeField] private float familiarityDecayPerSecond = 0.6f;
        [Tooltip("Seconds a formed pair takes to produce an egg.")]
        [SerializeField] private float secondsPerBrood = 30f;
        [SerializeField] private int maxEggs = 8;
        [SerializeField] private float eggIncubationSeconds = 12f;
        [Tooltip("How far a baby's traits may wander from the midpoint of its parents'. Zero would make every baby of one pair identical and flatten the tank over an evening.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float broodTraitJitter = 0.12f;
        [Tooltip("How much smaller a baby is than the average of its parents.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float babyScaleFactor = 0.45f;

        [Header("Egg Appearance")]
        [SerializeField] private float eggWorldSize = 0.22f;
        [SerializeField] private int eggSortingOrder = 205;

        [Header("Aquarium Energy")]
        [Tooltip("Design point 11. One 0..1 value describing how alive the tank feels, driving fish speed, bubble density and the water's brightness together, so the whole aquarium swells and settles as one mood.")]
        [SerializeField] private bool aquariumEnergyEnabled = true;
        [Tooltip("Where the mood sits with nobody there. Not zero: an empty tank should still be alive.")]
        [Range(0f, 1f)]
        [SerializeField] private float energyRestingLevel = 0.35f;
        [Tooltip("Pointer speed, in world units per second, that counts as full excitement.")]
        [SerializeField] private float energyPointerSpeedForFull = 6f;
        [Tooltip("How fast the tank wakes up. Deliberately faster than it settles.")]
        [SerializeField] private float energyRisePerSecond = 0.8f;
        [Tooltip("How fast it calms down again. Slow, so a tank does not visibly deflate the moment somebody steps away.")]
        [SerializeField] private float energyDecayPerSecond = 0.12f;
        [Tooltip("Immediate lift from one drawing arriving. A capture is an event with no duration, so it bumps the mood rather than holding it up.")]
        [Range(0f, 1f)]
        [SerializeField] private float energyPerCapture = 0.25f;
        [Tooltip("Extra fish speed at full energy. 0.3 means a busy tank swims 30 percent faster.")]
        [SerializeField] private float energySpeedInfluence = 0.3f;

        [Header("Currents")]
        [Tooltip("Design points 17 and 18. Slow eddies plus a prevailing drift that carries the shoal from one end of the tank to the other over minutes.")]
        [SerializeField] private bool currentsEnabled = true;
        [Tooltip("World-unit size of one eddy. Larger is broader and slower.")]
        [SerializeField] private float currentScale = 7f;
        [SerializeField] private float currentStrength = 0.55f;
        [Tooltip("Seconds for the prevailing drift to turn right around. Minutes, so it reads as migration rather than as wind.")]
        [SerializeField] private float migrationPeriodSeconds = 150f;
        [SerializeField] private float migrationStrength = 0.4f;
        [Tooltip("Extra choppiness at the height of a storm, on top of the ordinary current.")]
        [SerializeField] private float stormTurbulence = 1.3f;

        [Header("Day And Night")]
        [Tooltip("Design points 14 and 15. Seconds for a full day. Compressed hard on purpose: a real day would be indistinguishable from a static scene over the two minutes this milestone is judged on.")]
        [SerializeField] private float dayLengthSeconds = 300f;
        [Tooltip("How much darker the tank gets at midnight. 0 disables the cycle's colour without disabling its timing.")]
        [Range(0f, 1f)]
        [SerializeField] private float nightDarkening = 0.55f;
        [SerializeField] private float stormIntervalSeconds = 110f;
        [SerializeField] private float stormDurationSeconds = 22f;
        [Tooltip("Seconds a storm takes to build and to clear, so nothing snaps.")]
        [SerializeField] private float stormRampSeconds = 5f;
        [Tooltip("Colour the water is tinted towards at the height of a storm.")]
        [SerializeField] private Color stormWaterTint = new Color(0.42f, 0.5f, 0.58f, 1f);

        [Header("Predator")]
        [Tooltip("Design point 20. A generated shape - never a visitor's drawing - that crosses the tank now and then and scatters everything.")]
        [SerializeField] private bool predatorEnabled = true;
        [Tooltip("Seconds between visits. Minutes on purpose: a threat that arrives often stops being a threat.")]
        [SerializeField] private float predatorIntervalSeconds = 165f;
        [SerializeField] private float predatorCrossingSeconds = 11f;
        [Tooltip("Seconds it takes to become frightening and to stop being so, so the scatter builds rather than snapping.")]
        [SerializeField] private float predatorFadeSeconds = 2.5f;
        [Tooltip("How far away fish notice it. Much larger than the pointer radius: the whole tank should react.")]
        [SerializeField] private float predatorFearRadius = 9f;
        [SerializeField] private float predatorFleeStrength = 7f;
        [Tooltip("Speed multiplier for a fish fleeing a predator, at full threat.")]
        [SerializeField] private float predatorPanicSpeedMultiplier = 2.4f;
        [SerializeField] private float predatorWorldLength = 3.4f;
        [SerializeField] private Color predatorColour = new Color(0.09f, 0.11f, 0.15f, 0.92f);
        [SerializeField] private int predatorSortingOrder = 240;

        [Header("Lifecycle")]
        [Tooltip("Design point 25. A fish being removed to make room swims off towards the nearest edge as it fades, instead of dissolving where it floats.")]
        [SerializeField] private bool departingFishSwimAway = true;
        [SerializeField] private float departureSwimStrength = 3.2f;

        [Header("Persistence")]
        [Tooltip("Save the tank to persistentDataPath and restore it on start. Contract section 1: Unity owns the life, so this file is the authority on everything but the artwork.")]
        [SerializeField] private bool persistenceEnabled = true;
        [Tooltip("Seconds between automatic saves. The tank is also saved on quit; this is the safety net for a machine that loses power.")]
        [SerializeField] private float saveIntervalSeconds = 20f;
        [Tooltip("Capture ids remembered across restarts. Only has to cover Python's replay window, so this is generous. See FishCaptureRegistry.")]
        [SerializeField] private int maxPersistedCaptureIds = 4096;
        [Tooltip("Delete cached artwork for fish no longer in the tank. Off keeps every drawing ever captured, which is the safer default for an archive.")]
        [SerializeField] private bool pruneUnusedArtwork = false;

        [Header("Birth Moment")]
        [Tooltip("Design point 28. Seconds a newly drawn fish is celebrated for. Never runs on a replay or a restore, which are the aquarium putting fish back rather than announcing them.")]
        [SerializeField] private float birthMomentSeconds = 1.4f;
        [Tooltip("How much brighter a fish glows during its birth moment. 1 disables the effect without disabling the timing.")]
        [SerializeField] private float birthMomentBrightness = 2.1f;

        [Header("Socket.IO Source")]
        [Tooltip("Receive fish from the Python capture station over Socket.IO. Independent of the folder watcher; both can run at once.")]
        [SerializeField] private bool socketSourceEnabled = true;
        [SerializeField] private bool socketAutoConnect = true;
        [SerializeField] private string socketHost = "127.0.0.1";
        [SerializeField] private int socketPort = 8765;
        [Tooltip("Socket.IO namespace. The capture station uses the default namespace.")]
        [SerializeField] private string socketNamespace = "/";
        [SerializeField] private string socketEventName = "fish_captured";
        [SerializeField] private SocketTransportMode socketTransport = SocketTransportMode.PollingThenUpgradeToWebSocket;
        [Tooltip("0 or below means keep retrying forever, which is what an unattended installation wants.")]
        [SerializeField] private int socketReconnectionAttempts = 0;
        [SerializeField] private float socketReconnectionDelaySeconds = 1f;
        [SerializeField] private float socketReconnectionDelayMaxSeconds = 10f;
        [SerializeField] private float socketConnectTimeoutSeconds = 20f;
        [Tooltip("Schema the payload contract was written against. A higher value on the wire is logged once, loudly; a lower one is the documented legacy replay case and is only noted.")]
        [SerializeField] private int expectedSchemaVersion = 2;

        [Header("Debug")]
        [SerializeField] private bool debugOverlayVisibleOnStart = true;
        [SerializeField] private bool drawAquariumBoundsGizmo = true;


        public AquariumRuntimeMode RuntimeMode {
            get { return runtimeMode; }
        }

        public bool FullscreenInProduction {
            get { return fullscreenInProduction; }
        }

        public int TargetFrameRate {
            get { return targetFrameRate; }
        }

        public int VSyncCount {
            get { return vSyncCount; }
        }

        public bool AllowExternalSettingsOverride {
            get { return allowExternalSettingsOverride; }
        }

        public InputFolderRoot InputFolderRootMode {
            get { return inputFolderRoot; }
        }

        public string InputFolderRelativePath {
            get { return inputFolderRelativePath; }
        }

        public string InputFolderAbsolutePath {
            get { return inputFolderAbsolutePath; }
        }

        public string FileSearchPattern {
            get { return fileSearchPattern; }
        }

        public bool CreateInputFolderIfMissing {
            get { return createInputFolderIfMissing; }
        }

        public bool LoadExistingFishOnStartup {
            get { return loadExistingFishOnStartup; }
        }

        public bool WatchForNewFiles {
            get { return watchForNewFiles; }
        }

        public float FileStabilitySeconds {
            get { return fileStabilitySeconds; }
        }

        public float FileReadTimeoutSeconds {
            get { return fileReadTimeoutSeconds; }
        }

        public float PeriodicRescanSeconds {
            get { return periodicRescanSeconds; }
        }

        public int MaxFishLoadsPerFrame {
            get { return maxFishLoadsPerFrame; }
        }

        public bool RemoveFishWhenSourceFileDeleted {
            get { return removeFishWhenSourceFileDeleted; }
        }

        public bool LoadTestFishInDevelopment {
            get { return loadTestFishInDevelopment; }
        }

        public string TestFishFolderRelativePath {
            get { return testFishFolderRelativePath; }
        }

        public int MaxFishCount {
            get { return maxFishCount; }
        }

        public FishOverflowStrategy OverflowStrategy {
            get { return overflowStrategy; }
        }

        public FishSpawnStrategy SpawnStrategy {
            get { return spawnStrategy; }
        }

        public float SpawnAnimationDuration {
            get { return spawnAnimationDuration; }
        }

        public float DespawnAnimationDuration {
            get { return despawnAnimationDuration; }
        }

        public float SpawnStartScaleMultiplier {
            get { return spawnStartScaleMultiplier; }
        }

        public FloatRange FishWorldSizeRange {
            get { return fishWorldSizeRange; }
        }

        public float SpritePixelsPerUnit {
            get { return spritePixelsPerUnit; }
        }

        public bool SizeFromForegroundArea {
            get { return sizeFromForegroundArea; }
        }

        public int ForegroundAreaAtMinimumSize {
            get { return foregroundAreaAtMinimumSize; }
        }

        public int ForegroundAreaAtMaximumSize {
            get { return foregroundAreaAtMaximumSize; }
        }

        public AnimationCurve ForegroundAreaResponse {
            get { return foregroundAreaResponse; }
        }

        public AnimationCurve SpeedResponse {
            get { return speedResponse; }
        }

        public AnimationCurve AccelerationResponse {
            get { return accelerationResponse; }
        }

        public AnimationCurve GraceTurnResponse {
            get { return graceTurnResponse; }
        }

        public FloatRange GraceDampingRange {
            get { return graceDampingRange; }
        }

        public AnimationCurve GraceTailAmplitudeResponse {
            get { return graceTailAmplitudeResponse; }
        }

        public AnimationCurve SpeedTailFrequencyResponse {
            get { return speedTailFrequencyResponse; }
        }

        public AnimationCurve SpeedIdleResponse {
            get { return speedIdleResponse; }
        }

        public bool PreferredDepthEnabled {
            get { return preferredDepthEnabled; }
        }

        public float PreferredDepthBandHeight {
            get { return preferredDepthBandHeight; }
        }

        public float PreferredDepthHomingStrength {
            get { return preferredDepthHomingStrength; }
        }

        public bool PointerInteractionEnabled {
            get { return pointerInteractionEnabled; }
        }

        public float PointerInfluenceRadius {
            get { return pointerInfluenceRadius; }
        }

        public float PointerAttractionStrength {
            get { return pointerAttractionStrength; }
        }

        public float PointerRepulsionStrength {
            get { return pointerRepulsionStrength; }
        }

        public float PointerPersonalSpace {
            get { return pointerPersonalSpace; }
        }

        public float StartleSpeedThreshold {
            get { return startleSpeedThreshold; }
        }

        public float StartleFearlessMultiplier {
            get { return startleFearlessMultiplier; }
        }

        public float StartleDurationSeconds {
            get { return startleDurationSeconds; }
        }

        public float StartleStrength {
            get { return startleStrength; }
        }

        public float StartleSpeedMultiplier {
            get { return startleSpeedMultiplier; }
        }

        public bool RippleOnClickEnabled {
            get { return rippleOnClickEnabled; }
        }

        public float RippleStrength {
            get { return rippleStrength; }
        }

        public float RippleDurationSeconds {
            get { return rippleDurationSeconds; }
        }

        public float RippleRadius {
            get { return rippleRadius; }
        }

        public float RippleDangerRadius {
            get { return rippleDangerRadius; }
        }

        public int MaxConcurrentRipples {
            get { return maxConcurrentRipples; }
        }

        public bool AffectionEnabled {
            get { return affectionEnabled; }
        }

        public float AffectionProximityRadius {
            get { return affectionProximityRadius; }
        }

        public float AffectionPointerSpeedLimit {
            get { return affectionPointerSpeedLimit; }
        }

        public float AffectionGainPerSecond {
            get { return affectionGainPerSecond; }
        }

        public float AffectionDecayPerSecond {
            get { return affectionDecayPerSecond; }
        }

        public float AffectionTrustThreshold {
            get { return affectionTrustThreshold; }
        }

        public FloatRange SpeedRange {
            get { return speedRange; }
        }

        public FloatRange TurnSpeedRange {
            get { return turnSpeedRange; }
        }

        public FloatRange AccelerationRange {
            get { return accelerationRange; }
        }

        public float DirectionNoiseDegrees {
            get { return directionNoiseDegrees; }
        }

        public float DirectionNoiseFrequency {
            get { return directionNoiseFrequency; }
        }

        public float VerticalSteeringDamping {
            get { return verticalSteeringDamping; }
        }

        public float TargetReachDistance {
            get { return targetReachDistance; }
        }

        public float MinimumTargetTravelDistance {
            get { return minimumTargetTravelDistance; }
        }

        public FloatRange IdleProbabilityRange {
            get { return idleProbabilityRange; }
        }

        public FloatRange IdleDurationRange {
            get { return idleDurationRange; }
        }

        public float BoundsAvoidanceMargin {
            get { return boundsAvoidanceMargin; }
        }

        public float BoundsAvoidanceStrength {
            get { return boundsAvoidanceStrength; }
        }

        public float BoundsPadding {
            get { return boundsPadding; }
        }

        public bool SeparationEnabled {
            get { return separationEnabled; }
        }

        public float SeparationRadius {
            get { return separationRadius; }
        }

        public float SeparationStrength {
            get { return separationStrength; }
        }

        public bool FlockingEnabled {
            get { return flockingEnabled; }
        }

        public float NeighbourRadius {
            get { return neighbourRadius; }
        }

        public float AlignmentStrength {
            get { return alignmentStrength; }
        }

        public float CohesionStrength {
            get { return cohesionStrength; }
        }

        public AnimationCurve SocialAlignmentResponse {
            get { return socialAlignmentResponse; }
        }

        public AnimationCurve SocialCohesionResponse {
            get { return socialCohesionResponse; }
        }

        public int MaxNeighboursConsidered {
            get { return maxNeighboursConsidered; }
        }

        public bool NoveltyEnabled {
            get { return noveltyEnabled; }
        }

        public float NoveltySeconds {
            get { return noveltySeconds; }
        }

        public float NoveltyRadius {
            get { return noveltyRadius; }
        }

        public float NoveltyAttractionStrength {
            get { return noveltyAttractionStrength; }
        }

        public AnimationCurve CuriosityNoveltyResponse {
            get { return curiosityNoveltyResponse; }
        }

        public bool FeedingEnabled {
            get { return feedingEnabled; }
        }

        public float FeedSurfaceBand {
            get { return feedSurfaceBand; }
        }

        public int FoodPerPinch {
            get { return foodPerPinch; }
        }

        public float FoodPinchSpread {
            get { return foodPinchSpread; }
        }

        public int MaxFoodParticles {
            get { return maxFoodParticles; }
        }

        public float FoodSinkSpeed {
            get { return foodSinkSpeed; }
        }

        public float FoodLifetimeSeconds {
            get { return foodLifetimeSeconds; }
        }

        public float FoodEatRadius {
            get { return foodEatRadius; }
        }

        public float FoodAwarenessRadius {
            get { return foodAwarenessRadius; }
        }

        public float FoodAttractionStrength {
            get { return foodAttractionStrength; }
        }

        public AnimationCurve CuriosityFoodResponse {
            get { return curiosityFoodResponse; }
        }

        public FloatRange AggressionEatCooldownRange {
            get { return aggressionEatCooldownRange; }
        }





        public bool RarityEffectsEnabled {
            get { return rarityEffectsEnabled; }
        }

        public AnimationCurve RarityGlowResponse {
            get { return rarityGlowResponse; }
        }

        public float RarityMaximumGlow {
            get { return rarityMaximumGlow; }
        }

        public FishRarityTier MinimumTrailTier {
            get { return minimumTrailTier; }
        }

        public float RarityTrailSeconds {
            get { return rarityTrailSeconds; }
        }

        public float RarityTrailStartWidth {
            get { return rarityTrailStartWidth; }
        }

        public float RarityBirthLoudness {
            get { return rarityBirthLoudness; }
        }

        public bool EyeHighlightEnabled {
            get { return eyeHighlightEnabled; }
        }

        public float EyeHighlightScale {
            get { return eyeHighlightScale; }
        }

        public Color EyeHighlightColour {
            get { return eyeHighlightColour; }
        }

        public bool ObjectTypeBehavioursEnabled {
            get { return objectTypeBehavioursEnabled; }
        }

        public float PlantSwayDegrees {
            get { return plantSwayDegrees; }
        }

        public float PlantSwaySpeed {
            get { return plantSwaySpeed; }
        }

        public float JellyfishPulseAmplitude {
            get { return jellyfishPulseAmplitude; }
        }

        public float JellyfishPulseSpeed {
            get { return jellyfishPulseSpeed; }
        }

        public float JellyfishDriftFactor {
            get { return jellyfishDriftFactor; }
        }

        public float JellyfishGlowAmount {
            get { return jellyfishGlowAmount; }
        }

        public float CreaturePredatorAggressionThreshold {
            get { return creaturePredatorAggressionThreshold; }
        }

        public float CreatureFearRadius {
            get { return creatureFearRadius; }
        }

        public float CreatureFleeStrength {
            get { return creatureFleeStrength; }
        }

        public bool SocialBondsEnabled {
            get { return socialBondsEnabled; }
        }

        public float BondRadius {
            get { return bondRadius; }
        }

        public float SecondsTogetherToPair {
            get { return secondsTogetherToPair; }
        }

        public float FamiliarityDecayPerSecond {
            get { return familiarityDecayPerSecond; }
        }

        public float SecondsPerBrood {
            get { return secondsPerBrood; }
        }

        public int MaxEggs {
            get { return maxEggs; }
        }

        public float EggIncubationSeconds {
            get { return eggIncubationSeconds; }
        }

        public float BroodTraitJitter {
            get { return broodTraitJitter; }
        }

        public float BabyScaleFactor {
            get { return babyScaleFactor; }
        }

        public float EggWorldSize {
            get { return eggWorldSize; }
        }

        public int EggSortingOrder {
            get { return eggSortingOrder; }
        }

        public bool AquariumEnergyEnabled {
            get { return aquariumEnergyEnabled; }
        }

        public float EnergyRestingLevel {
            get { return energyRestingLevel; }
        }

        public float EnergyPointerSpeedForFull {
            get { return energyPointerSpeedForFull; }
        }

        public float EnergyRisePerSecond {
            get { return energyRisePerSecond; }
        }

        public float EnergyDecayPerSecond {
            get { return energyDecayPerSecond; }
        }

        public float EnergyPerCapture {
            get { return energyPerCapture; }
        }

        public float EnergySpeedInfluence {
            get { return energySpeedInfluence; }
        }

        public bool CurrentsEnabled {
            get { return currentsEnabled; }
        }

        public float CurrentScale {
            get { return currentScale; }
        }

        public float CurrentStrength {
            get { return currentStrength; }
        }

        public float MigrationPeriodSeconds {
            get { return migrationPeriodSeconds; }
        }

        public float MigrationStrength {
            get { return migrationStrength; }
        }

        public float StormTurbulence {
            get { return stormTurbulence; }
        }

        public float DayLengthSeconds {
            get { return dayLengthSeconds; }
        }

        public float NightDarkening {
            get { return nightDarkening; }
        }

        public float StormIntervalSeconds {
            get { return stormIntervalSeconds; }
        }

        public float StormDurationSeconds {
            get { return stormDurationSeconds; }
        }

        public float StormRampSeconds {
            get { return stormRampSeconds; }
        }

        public Color StormWaterTint {
            get { return stormWaterTint; }
        }

        public bool PredatorEnabled {
            get { return predatorEnabled; }
        }

        public float PredatorIntervalSeconds {
            get { return predatorIntervalSeconds; }
        }

        public float PredatorCrossingSeconds {
            get { return predatorCrossingSeconds; }
        }

        public float PredatorFadeSeconds {
            get { return predatorFadeSeconds; }
        }

        public float PredatorFearRadius {
            get { return predatorFearRadius; }
        }

        public float PredatorFleeStrength {
            get { return predatorFleeStrength; }
        }

        public float PredatorPanicSpeedMultiplier {
            get { return predatorPanicSpeedMultiplier; }
        }

        public float PredatorWorldLength {
            get { return predatorWorldLength; }
        }

        public Color PredatorColour {
            get { return predatorColour; }
        }

        public int PredatorSortingOrder {
            get { return predatorSortingOrder; }
        }

        public bool DepartingFishSwimAway {
            get { return departingFishSwimAway; }
        }

        public float DepartureSwimStrength {
            get { return departureSwimStrength; }
        }

        public bool PersistenceEnabled {
            get { return persistenceEnabled; }
        }

        public float SaveIntervalSeconds {
            get { return saveIntervalSeconds; }
        }

        public int MaxPersistedCaptureIds {
            get { return maxPersistedCaptureIds; }
        }

        public bool PruneUnusedArtwork {
            get { return pruneUnusedArtwork; }
        }

        public float BirthMomentSeconds {
            get { return birthMomentSeconds; }
        }

        public float BirthMomentBrightness {
            get { return birthMomentBrightness; }
        }

        public Color FoodColour {
            get { return foodColour; }
        }

        public float FoodWorldSize {
            get { return foodWorldSize; }
        }

        public int FoodSortingOrder {
            get { return foodSortingOrder; }
        }

        public bool ArtworkFacesRight {
            get { return artworkFacesRight; }
        }

        public float FacingFlipThreshold {
            get { return facingFlipThreshold; }
        }

        public float MaxPitchAngle {
            get { return maxPitchAngle; }
        }

        public float PitchSmoothing {
            get { return pitchSmoothing; }
        }

        public FloatRange SwimAmplitudeRange {
            get { return swimAmplitudeRange; }
        }

        public FloatRange SwimFrequencyRange {
            get { return swimFrequencyRange; }
        }

        public float BodyRollDegrees {
            get { return bodyRollDegrees; }
        }

        public float BreathingScaleAmount {
            get { return breathingScaleAmount; }
        }

        public FloatRange DepthRange {
            get { return depthRange; }
        }

        public float DepthScaleInfluence {
            get { return depthScaleInfluence; }
        }

        public float DepthAlphaInfluence {
            get { return depthAlphaInfluence; }
        }

        public float DepthSpeedInfluence {
            get { return depthSpeedInfluence; }
        }

        public int ForegroundSortingOrder {
            get { return foregroundSortingOrder; }
        }

        public int BackgroundSortingOrder {
            get { return backgroundSortingOrder; }
        }

        public bool DebugOverlayVisibleOnStart {
            get { return debugOverlayVisibleOnStart; }
        }

        public bool DrawAquariumBoundsGizmo {
            get { return drawAquariumBoundsGizmo; }
        }

        public bool SocketSourceEnabled {
            get { return socketSourceEnabled; }
        }

        public bool SocketAutoConnect {
            get { return socketAutoConnect; }
        }

        public string SocketHost {
            get { return socketHost; }
        }

        public int SocketPort {
            get { return socketPort; }
        }

        public string SocketNamespace {
            get { return socketNamespace; }
        }

        public string SocketEventName {
            get { return socketEventName; }
        }

        public SocketTransportMode SocketTransport {
            get { return socketTransport; }
        }

        public int SocketReconnectionAttempts {
            get { return socketReconnectionAttempts; }
        }

        public float SocketReconnectionDelaySeconds {
            get { return socketReconnectionDelaySeconds; }
        }

        public float SocketReconnectionDelayMaxSeconds {
            get { return socketReconnectionDelayMaxSeconds; }
        }

        public float SocketConnectTimeoutSeconds {
            get { return socketConnectTimeoutSeconds; }
        }

        public int ExpectedSchemaVersion {
            get { return expectedSchemaVersion; }
        }

        public bool IsDevelopmentMode {
            get { return runtimeMode == AquariumRuntimeMode.Development; }
        }

        /// <summary> Absolute path Unity watches for fish PNGs, always derived from the serialised root plus relative path. </summary>
        public string ResolveInputFolderPath() {
            if (inputFolderRoot == InputFolderRoot.Absolute) {
                return NormalizePath(inputFolderAbsolutePath);
            }

            string root = inputFolderRoot == InputFolderRoot.PersistentData ? Application.persistentDataPath : Application.streamingAssetsPath;
            return NormalizePath(Path.Combine(root, inputFolderRelativePath));
        }

        /// <summary> Socket.IO endpoint of the capture station, built from the configured host and port. </summary>
        public string ResolveSocketUrl() {
            string host = string.IsNullOrWhiteSpace(socketHost) ? "127.0.0.1" : socketHost.Trim();
            return "http://" + host + ":" + socketPort + "/socket.io/";
        }

        /// <summary> Absolute path of the development-only sample fish folder under StreamingAssets. </summary>
        public string ResolveTestFishFolderPath() {
            return NormalizePath(Path.Combine(Application.streamingAssetsPath, testFishFolderRelativePath));
        }

        /// <summary> Applied by AppConfig when an external settings file overrides the authored values. </summary>
        public void ApplyExternalOverrides(string overrideInputFolder, int overrideMaxFishCount, AquariumRuntimeMode overrideRuntimeMode) {
            if (!string.IsNullOrWhiteSpace(overrideInputFolder)) {
                inputFolderRoot = InputFolderRoot.Absolute;
                inputFolderAbsolutePath = overrideInputFolder;
            }

            if (overrideMaxFishCount > 0) {
                maxFishCount = overrideMaxFishCount;
            }

            runtimeMode = overrideRuntimeMode;
        }

        /// <summary>
        /// Maps a 0..1 trait through a response curve to a 0..1 position within a range. Null and empty
        /// curves fall through to the raw trait rather than to zero, because OnValidate does not run in a
        /// player and an aquarium that stopped moving in production would be a poor way to learn that.
        /// </summary>
        public static float EvaluateResponse(AnimationCurve curve, float trait) {
            float clampedTrait = Mathf.Clamp01(trait);

            if (curve == null || curve.length == 0) {
                return clampedTrait;
            }

            return Mathf.Clamp01(curve.Evaluate(clampedTrait));
        }

        private static AnimationCurve RepairCurve(AnimationCurve curve, float startValue, float endValue) {
            if (curve != null && curve.length > 0) {
                return curve;
            }

            return AnimationCurve.Linear(0f, startValue, 1f, endValue);
        }

        private static string NormalizePath(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return string.Empty;
            }

            return PathUtility.NormalizeFolder(path);
        }

        private void OnValidate() {
            maxFishCount = Mathf.Max(1, maxFishCount);
            maxFishLoadsPerFrame = Mathf.Max(1, maxFishLoadsPerFrame);
            spritePixelsPerUnit = Mathf.Max(1f, spritePixelsPerUnit);
            fileStabilitySeconds = Mathf.Max(0f, fileStabilitySeconds);
            fileReadTimeoutSeconds = Mathf.Max(1f, fileReadTimeoutSeconds);
            periodicRescanSeconds = Mathf.Max(0f, periodicRescanSeconds);
            spawnAnimationDuration = Mathf.Max(0.01f, spawnAnimationDuration);
            despawnAnimationDuration = Mathf.Max(0.01f, despawnAnimationDuration);
            spawnStartScaleMultiplier = Mathf.Clamp(spawnStartScaleMultiplier, 0.01f, 1f);
            verticalSteeringDamping = Mathf.Clamp(verticalSteeringDamping, 0.05f, 1f);
            targetReachDistance = Mathf.Max(0.05f, targetReachDistance);
            minimumTargetTravelDistance = Mathf.Max(0.1f, minimumTargetTravelDistance);
            boundsPadding = Mathf.Max(0f, boundsPadding);
            boundsAvoidanceMargin = Mathf.Max(0.01f, boundsAvoidanceMargin);
            separationRadius = Mathf.Max(0.01f, separationRadius);
            facingFlipThreshold = Mathf.Clamp(facingFlipThreshold, 0f, 0.9f);
            maxPitchAngle = Mathf.Clamp(maxPitchAngle, 0f, 80f);
            depthScaleInfluence = Mathf.Clamp01(depthScaleInfluence);
            depthAlphaInfluence = Mathf.Clamp01(depthAlphaInfluence);
            depthSpeedInfluence = Mathf.Clamp01(depthSpeedInfluence);

            socketPort = Mathf.Clamp(socketPort, 1, 65535);
            socketReconnectionDelaySeconds = Mathf.Max(0.1f, socketReconnectionDelaySeconds);
            socketReconnectionDelayMaxSeconds = Mathf.Max(socketReconnectionDelaySeconds, socketReconnectionDelayMaxSeconds);
            socketConnectTimeoutSeconds = Mathf.Max(1f, socketConnectTimeoutSeconds);

            if (string.IsNullOrWhiteSpace(socketNamespace)) {
                socketNamespace = "/";
            }

            if (string.IsNullOrWhiteSpace(socketEventName)) {
                socketEventName = "fish_captured";
            }

            if (string.IsNullOrWhiteSpace(fileSearchPattern)) {
                fileSearchPattern = "*.png";
            }

            rarityMaximumGlow = Mathf.Max(0f, rarityMaximumGlow);
            rarityTrailSeconds = Mathf.Max(0.01f, rarityTrailSeconds);
            rarityTrailStartWidth = Mathf.Max(0.001f, rarityTrailStartWidth);
            rarityBirthLoudness = Mathf.Max(1f, rarityBirthLoudness);
            eyeHighlightScale = Mathf.Max(0.01f, eyeHighlightScale);
            rarityGlowResponse = RepairCurve(rarityGlowResponse, 0f, 1f);

            plantSwayDegrees = Mathf.Max(0f, plantSwayDegrees);
            plantSwaySpeed = Mathf.Max(0f, plantSwaySpeed);
            jellyfishPulseAmplitude = Mathf.Max(0f, jellyfishPulseAmplitude);
            jellyfishPulseSpeed = Mathf.Max(0f, jellyfishPulseSpeed);
            jellyfishGlowAmount = Mathf.Max(0f, jellyfishGlowAmount);
            creatureFearRadius = Mathf.Max(0.01f, creatureFearRadius);
            creatureFleeStrength = Mathf.Max(0f, creatureFleeStrength);

            bondRadius = Mathf.Max(0.01f, bondRadius);
            secondsTogetherToPair = Mathf.Max(0.1f, secondsTogetherToPair);
            familiarityDecayPerSecond = Mathf.Max(0f, familiarityDecayPerSecond);
            secondsPerBrood = Mathf.Max(0.1f, secondsPerBrood);
            maxEggs = Mathf.Max(1, maxEggs);
            eggIncubationSeconds = Mathf.Max(0.1f, eggIncubationSeconds);
            eggWorldSize = Mathf.Max(0.01f, eggWorldSize);

            energyPointerSpeedForFull = Mathf.Max(0.01f, energyPointerSpeedForFull);
            energyRisePerSecond = Mathf.Max(0f, energyRisePerSecond);
            energyDecayPerSecond = Mathf.Max(0f, energyDecayPerSecond);
            energySpeedInfluence = Mathf.Max(0f, energySpeedInfluence);
            currentScale = Mathf.Max(0.01f, currentScale);
            currentStrength = Mathf.Max(0f, currentStrength);
            migrationPeriodSeconds = Mathf.Max(1f, migrationPeriodSeconds);
            migrationStrength = Mathf.Max(0f, migrationStrength);
            stormTurbulence = Mathf.Max(0f, stormTurbulence);
            dayLengthSeconds = Mathf.Max(1f, dayLengthSeconds);
            stormIntervalSeconds = Mathf.Max(1f, stormIntervalSeconds);
            stormDurationSeconds = Mathf.Max(0f, stormDurationSeconds);
            stormRampSeconds = Mathf.Max(0.01f, stormRampSeconds);
            predatorIntervalSeconds = Mathf.Max(1f, predatorIntervalSeconds);
            predatorCrossingSeconds = Mathf.Max(0.5f, predatorCrossingSeconds);
            predatorFadeSeconds = Mathf.Max(0.01f, predatorFadeSeconds);
            predatorFearRadius = Mathf.Max(0.01f, predatorFearRadius);
            predatorFleeStrength = Mathf.Max(0f, predatorFleeStrength);
            predatorPanicSpeedMultiplier = Mathf.Max(1f, predatorPanicSpeedMultiplier);
            predatorWorldLength = Mathf.Max(0.1f, predatorWorldLength);
            departureSwimStrength = Mathf.Max(0f, departureSwimStrength);

            saveIntervalSeconds = Mathf.Max(1f, saveIntervalSeconds);
            maxPersistedCaptureIds = Mathf.Max(1, maxPersistedCaptureIds);
            birthMomentSeconds = Mathf.Max(0f, birthMomentSeconds);
            birthMomentBrightness = Mathf.Max(1f, birthMomentBrightness);

            feedSurfaceBand = Mathf.Clamp(feedSurfaceBand, 0.02f, 0.6f);
            foodPerPinch = Mathf.Max(1, foodPerPinch);
            foodPinchSpread = Mathf.Max(0f, foodPinchSpread);
            maxFoodParticles = Mathf.Max(1, maxFoodParticles);
            foodSinkSpeed = Mathf.Max(0f, foodSinkSpeed);
            foodLifetimeSeconds = Mathf.Max(0.1f, foodLifetimeSeconds);
            foodEatRadius = Mathf.Max(0.01f, foodEatRadius);
            foodAwarenessRadius = Mathf.Max(foodEatRadius, foodAwarenessRadius);
            foodAttractionStrength = Mathf.Max(0f, foodAttractionStrength);
            foodWorldSize = Mathf.Max(0.01f, foodWorldSize);

            // Deliberately NOT Normalized(): the range runs high-to-low, because a more aggressive fish
            // waits less between mouthfuls.
            aggressionEatCooldownRange = new FloatRange(Mathf.Max(0f, aggressionEatCooldownRange.Min), Mathf.Max(0f, aggressionEatCooldownRange.Max));

            neighbourRadius = Mathf.Max(separationRadius, neighbourRadius);
            alignmentStrength = Mathf.Max(0f, alignmentStrength);
            cohesionStrength = Mathf.Max(0f, cohesionStrength);
            maxNeighboursConsidered = Mathf.Max(1, maxNeighboursConsidered);
            noveltySeconds = Mathf.Max(0f, noveltySeconds);
            noveltyRadius = Mathf.Max(0.01f, noveltyRadius);
            noveltyAttractionStrength = Mathf.Max(0f, noveltyAttractionStrength);

            foregroundAreaAtMinimumSize = Mathf.Max(1, foregroundAreaAtMinimumSize);
            foregroundAreaAtMaximumSize = Mathf.Max(foregroundAreaAtMinimumSize + 1, foregroundAreaAtMaximumSize);
            preferredDepthBandHeight = Mathf.Clamp(preferredDepthBandHeight, 0.05f, 1f);
            preferredDepthHomingStrength = Mathf.Max(0f, preferredDepthHomingStrength);

            pointerInfluenceRadius = Mathf.Max(0.01f, pointerInfluenceRadius);
            pointerAttractionStrength = Mathf.Max(0f, pointerAttractionStrength);
            pointerRepulsionStrength = Mathf.Max(0f, pointerRepulsionStrength);
            pointerPersonalSpace = Mathf.Clamp(pointerPersonalSpace, 0f, pointerInfluenceRadius);
            startleSpeedThreshold = Mathf.Max(0.01f, startleSpeedThreshold);
            startleFearlessMultiplier = Mathf.Max(1f, startleFearlessMultiplier);
            startleDurationSeconds = Mathf.Max(0f, startleDurationSeconds);
            startleStrength = Mathf.Max(0f, startleStrength);
            startleSpeedMultiplier = Mathf.Max(1f, startleSpeedMultiplier);
            rippleStrength = Mathf.Max(0f, rippleStrength);
            rippleDurationSeconds = Mathf.Max(0.01f, rippleDurationSeconds);
            rippleRadius = Mathf.Max(0.01f, rippleRadius);
            rippleDangerRadius = Mathf.Clamp(rippleDangerRadius, 0f, rippleRadius);
            maxConcurrentRipples = Mathf.Max(1, maxConcurrentRipples);
            affectionProximityRadius = Mathf.Max(0.01f, affectionProximityRadius);
            affectionPointerSpeedLimit = Mathf.Max(0.01f, affectionPointerSpeedLimit);
            affectionGainPerSecond = Mathf.Max(0f, affectionGainPerSecond);
            affectionDecayPerSecond = Mathf.Max(0f, affectionDecayPerSecond);
            affectionTrustThreshold = Mathf.Clamp01(affectionTrustThreshold);

            // A curve added to this asset after it was last serialised deserialises with no keyframes, and
            // an empty AnimationCurve evaluates to zero everywhere - which would silently freeze every fish
            // that reads it. Repairing here means the default is what an artist sees, not a dead aquarium.
            foregroundAreaResponse = RepairCurve(foregroundAreaResponse, 0f, 1f);
            speedResponse = RepairCurve(speedResponse, 0f, 1f);
            accelerationResponse = RepairCurve(accelerationResponse, 0f, 1f);
            graceTurnResponse = RepairCurve(graceTurnResponse, 1f, 0f);
            graceTailAmplitudeResponse = RepairCurve(graceTailAmplitudeResponse, 0f, 1f);
            speedTailFrequencyResponse = RepairCurve(speedTailFrequencyResponse, 0f, 1f);
            speedIdleResponse = RepairCurve(speedIdleResponse, 1f, 0f);
            socialAlignmentResponse = RepairCurve(socialAlignmentResponse, 0f, 1f);
            socialCohesionResponse = RepairCurve(socialCohesionResponse, 0f, 1f);
            curiosityNoveltyResponse = RepairCurve(curiosityNoveltyResponse, 0f, 1f);
            curiosityFoodResponse = RepairCurve(curiosityFoodResponse, 0.3f, 1f);

            fishWorldSizeRange = fishWorldSizeRange.Normalized();
            speedRange = speedRange.Normalized();
            turnSpeedRange = turnSpeedRange.Normalized();
            accelerationRange = accelerationRange.Normalized();
            idleProbabilityRange = idleProbabilityRange.Normalized();
            idleDurationRange = idleDurationRange.Normalized();
            swimAmplitudeRange = swimAmplitudeRange.Normalized();
            swimFrequencyRange = swimFrequencyRange.Normalized();
            depthRange = depthRange.Normalized();
        }
    }
}
