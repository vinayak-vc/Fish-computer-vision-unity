using System.Collections.Generic;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The save file, and the capture-id memory that has to survive with it.
    ///
    /// The registry tests here are the important ones. A restarted aquarium restores its fish from disk
    /// and then Python replays its ten most recent captures; if those ids are not remembered across the
    /// restart, every one of them spawns a second copy of a fish already in the tank. That failure is
    /// silent, cumulative, and undoes the whole of M0.
    /// </summary>
    public sealed class AquariumSaveStateTests {
        private static string ToJson(AquariumSaveState state) {
            return BestHTTP.JSON.LitJson.JsonMapper.ToJson(state);
        }

        private static AquariumSaveState FromJson(string json) {
            return BestHTTP.JSON.LitJson.JsonMapper.ToObject<AquariumSaveState>(json);
        }

        private static FishSaveRecord BuildRecord() {
            FishSaveRecord record = new FishSaveRecord();
            record.captureId = "fish_20260902_120000_001";
            record.speed = 0.81f;
            record.curiosity = 0.9f;
            record.fear = 0.22f;
            record.social = 0.68f;
            record.aggression = 0.14f;
            record.grace = 0.55f;
            record.preferredDepth = 0.4f;
            record.rarityScore = 0.12f;
            record.rarityTier = (int)FishRarityTier.Uncommon;
            record.objectType = (int)FishObjectType.Jellyfish;
            record.objectConfidence = 0.81f;
            record.traitSource = (int)FishTraitSource.Payload;
            record.foregroundArea = 311227;
            record.positionX = -3.25f;
            record.positionY = 1.5f;
            record.affection = 0.65f;
            record.ageSeconds = 421.5f;
            record.foodEaten = 12;
            return record;
        }

        [Test]
        public void ASavedFishSurvivesTheRoundTrip() {
            AquariumSaveState state = new AquariumSaveState();
            state.fish.Add(BuildRecord());

            FishSaveRecord restored = FromJson(ToJson(state)).fish[0];

            Assert.AreEqual("fish_20260902_120000_001", restored.captureId);
            Assert.AreEqual(0.81f, restored.speed, 0.0005f);
            Assert.AreEqual(0.9f, restored.curiosity, 0.0005f);
            Assert.AreEqual(0.22f, restored.fear, 0.0005f);
            Assert.AreEqual(0.68f, restored.social, 0.0005f);
            Assert.AreEqual(0.14f, restored.aggression, 0.0005f);
            Assert.AreEqual(0.55f, restored.grace, 0.0005f);
            Assert.AreEqual(0.4f, restored.preferredDepth, 0.0005f);
            Assert.AreEqual((int)FishRarityTier.Uncommon, restored.rarityTier);
            Assert.AreEqual((int)FishObjectType.Jellyfish, restored.objectType);
            Assert.AreEqual((int)FishTraitSource.Payload, restored.traitSource);
        }

        [Test]
        public void TheLifeAFishHasLivedSurvivesTheRoundTrip() {
            AquariumSaveState state = new AquariumSaveState();
            state.fish.Add(BuildRecord());

            FishSaveRecord restored = FromJson(ToJson(state)).fish[0];

            Assert.AreEqual(-3.25f, restored.positionX, 0.0005f);
            Assert.AreEqual(1.5f, restored.positionY, 0.0005f);
            Assert.AreEqual(0.65f, restored.affection, 0.0005f, "Trust earned over a visit must not be lost to a restart.");
            Assert.AreEqual(421.5f, restored.ageSeconds, 0.005f);
            Assert.AreEqual(12, restored.foodEaten);
            Assert.AreEqual(311227, restored.foregroundArea, "Without the mask area the fish comes back a different size.");
        }

        [Test]
        public void TheCaptureIdMemoryAndTheTallySurviveTheRoundTrip() {
            AquariumSaveState state = new AquariumSaveState();
            state.knownCaptureIds.Add("fish_a");
            state.knownCaptureIds.Add("fish_b");
            state.capturesTodayDate = "2026-09-02";
            state.capturesTodayCount = 7;
            state.latestSequence = 143;

            AquariumSaveState restored = FromJson(ToJson(state));

            Assert.AreEqual(2, restored.knownCaptureIds.Count);
            Assert.AreEqual("fish_a", restored.knownCaptureIds[0]);
            Assert.AreEqual("2026-09-02", restored.capturesTodayDate);
            Assert.AreEqual(7, restored.capturesTodayCount);
            Assert.AreEqual(143, restored.latestSequence);
        }

        [Test]
        public void AnEmptyTankRoundTrips() {
            AquariumSaveState restored = FromJson(ToJson(new AquariumSaveState()));

            Assert.AreEqual(AquariumSaveVersion.Current, restored.version);
            Assert.IsNotNull(restored.fish);
            Assert.AreEqual(0, restored.fish.Count);
        }

        [Test]
        public void SavedNumbersAreWrittenInvariantly() {
            // A machine whose locale uses a decimal comma would otherwise write nonsense - a bug that
            // never appears on the developer's machine and always appears on the installation's.
            AquariumSaveState state = new AquariumSaveState();
            state.fish.Add(BuildRecord());

            string json = ToJson(state);

            Assert.IsTrue(json.Contains("0.81"), json);
            Assert.IsFalse(json.Contains("0,81"), json);
        }

        [Test]
        public void RestoredTraitsRebuildTheSameCreature() {
            FishSaveRecord record = BuildRecord();

            FishTraits traits = new FishTraits(record.captureId, record.speed, record.curiosity, record.fear,
                record.social, record.aggression, record.grace, record.preferredDepth, record.rarityScore,
                (FishRarityTier)record.rarityTier, (FishObjectType)record.objectType, record.objectConfidence,
                (FishTraitSource)record.traitSource);

            Assert.AreEqual(0.81f, traits.Speed, 0.0005f);
            Assert.AreEqual(FishObjectType.Jellyfish, traits.ObjectType);
            Assert.AreEqual(FishRarityTier.Uncommon, traits.RarityTier);
            Assert.AreEqual(FishTraitSource.Payload, traits.Source,
                "Rebuilding from the file rather than the hash is what stops a fish coming back a stranger.");
        }

        [Test]
        public void OnlyTheSavedFishesIdsAreRemembered() {
            // The rule that replaced "every id ever seen". Seeding exists to stop Python's replay burst
            // duplicating the fish this file restores, so an id with no fish behind it has nothing to
            // duplicate - and remembering it would mean that drawing could never come back.
            AquariumSaveState state = new AquariumSaveState();
            state.fish.Add(BuildRecord());
            state.knownCaptureIds.Add(BuildRecord().captureId);

            AquariumSaveState restored = FromJson(ToJson(state));

            Assert.AreEqual(1, restored.knownCaptureIds.Count);
            Assert.AreEqual(restored.fish[0].captureId, restored.knownCaptureIds[0]);
        }

        [Test]
        public void ARememberedIdIsNeverWrittenTwice() {
            // An earlier version asked every ingest service for everything it had seen, and because
            // loading seeds the combined list into BOTH services, saving read it back out of both - so the
            // list doubled on every save-and-load cycle.
            AquariumSaveState state = new AquariumSaveState();
            state.knownCaptureIds.Add("fish_a");

            AquariumSaveState restored = FromJson(ToJson(state));

            Assert.AreEqual(1, restored.knownCaptureIds.Count);
        }

        [Test]
        public void SeedingRememberedIdsMakesThemDuplicates() {
            // The restart path in one test: ids come back from the file, and Python's replay burst is then
            // recognised instead of spawning a second copy of every restored fish.
            FishCaptureRegistry fresh = new FishCaptureRegistry();
            Assert.IsTrue(fresh.TryClaim("fish_a"), "Before seeding, an id is new.");

            FishCaptureRegistry seeded = new FishCaptureRegistry();
            seeded.TryClaim("fish_a");
            seeded.TryClaim("fish_b");

            Assert.IsFalse(seeded.TryClaim("fish_a"), "After seeding, the same capture is a duplicate.");
            Assert.IsFalse(seeded.TryClaim("fish_b"));
        }

        [Test]
        public void TheRegistryIsBoundedSoASaveFileCannotGrowForever() {
            FishCaptureRegistry registry = new FishCaptureRegistry(8);

            for (int i = 0; i < 500; i++) {
                registry.TryClaim("fish_" + i.ToString("D4"));
            }

            Assert.AreEqual(8, registry.Capacity);
            Assert.LessOrEqual(registry.Count, 8);
        }

        [Test]
        public void EvictionDropsTheOldestAndKeepsTheReplayWindow() {
            FishCaptureRegistry registry = new FishCaptureRegistry(4);

            for (int i = 0; i < 10; i++) {
                registry.TryClaim("fish_" + i.ToString("D2"));
            }

            Assert.IsFalse(registry.IsClaimed("fish_00"), "The oldest is safe to forget; Python will never replay it.");
            Assert.IsTrue(registry.IsClaimed("fish_09"), "The newest must be remembered, because it is inside the replay window.");
        }

        [Test]
        public void ASaveFileFromAFutureVersionIsRecognisable() {
            AquariumSaveState state = new AquariumSaveState();
            state.version = AquariumSaveVersion.Current + 5;

            AquariumSaveState restored = FromJson(ToJson(state));

            Assert.Greater(restored.version, AquariumSaveVersion.Current,
                "The service ignores such a file rather than misreading it into an odd-looking tank.");
        }
    }
}
