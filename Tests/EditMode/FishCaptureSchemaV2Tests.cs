using NUnit.Framework;

using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Schema v2 of the wire contract, run against the same LitJson mapper BestHTTP uses.
    ///
    /// These exist because the failure mode is silent. A field renamed on the Python side does not throw:
    /// LitJson skips keys it does not recognise, so the fish still spawns, just with a personality of all
    /// zeroes. That looks like a tuning problem and gets debugged as one. Here it looks like a failing test.
    /// </summary>
    public sealed class FishCaptureSchemaV2Tests {
        private const float Tolerance = 0.0005f;

        private const string FullPayload =
            "{\"id\":\"fish_20260901_182346_001\",\"timestamp\":\"2026-09-01T18:23:46\",\"sequence\":1,\"schema_version\":2," +
            "\"png_base64\":\"iVBORw0KGgo=\",\"format\":\"png\",\"width\":983,\"height\":686," +
            "\"file_resolution\":[983,686]," +
            "\"bounding_box\":{\"x\":0,\"y\":112,\"width\":983,\"height\":686}," +
            "\"bounding_box_frame\":{\"x\":0,\"y\":112,\"width\":983,\"height\":686}," +
            "\"source_resolution\":[1280,960],\"foreground_area\":311227," +
            "\"processing_method\":\"opencv\",\"confidence\":0.0932," +
            "\"object_type\":\"fish\",\"object_confidence\":0.81," +
            "\"features\":{\"aspect_ratio\":1.86,\"circularity\":0.41,\"solidity\":0.78,\"extent\":0.62," +
            "\"elongation\":0.71,\"orientation_deg\":-12.4,\"perimeter\":2840.5,\"complexity\":34.2," +
            "\"vertex_count\":46,\"edge_density\":0.19,\"symmetry\":0.72,\"ink_coverage\":0.31," +
            "\"stroke_width\":4.2,\"spikiness\":0.55,\"color_count\":3," +
            "\"dominant_colors\":[{\"hex\":\"#2f6fb8\",\"ratio\":0.44},{\"hex\":\"#e8d24a\",\"ratio\":0.21},{\"hex\":\"#1b1b1b\",\"ratio\":0.09}]," +
            "\"mean_hue\":208,\"mean_sat\":0.61,\"mean_val\":0.48,\"colorfulness\":38.4}," +
            "\"personality\":{\"speed\":0.81,\"curiosity\":0.90,\"fear\":0.22,\"social\":0.68," +
            "\"aggression\":0.14,\"grace\":0.55,\"preferred_depth\":0.40,\"rarity_score\":0.12,\"rarity_tier\":\"common\"}," +
            "\"anatomy\":null,\"replay\":true}";

        private static FishCaptureMessage Parse(string json) {
            return BestHTTP.JSON.LitJson.JsonMapper.ToObject<FishCaptureMessage>(json);
        }

        [Test]
        public void TheIdentityAndArtworkBlockIsMapped() {
            FishCaptureMessage message = Parse(FullPayload);

            Assert.AreEqual("fish_20260901_182346_001", message.id);
            Assert.AreEqual(1, message.sequence);
            Assert.AreEqual(2, message.schema_version);
            Assert.AreEqual(983, message.width);
            Assert.AreEqual(311227, message.foreground_area);
            Assert.IsTrue(message.replay);
        }

        [Test]
        public void TheClassificationIsMapped() {
            FishCaptureMessage message = Parse(FullPayload);

            Assert.AreEqual("fish", message.object_type);
            Assert.AreEqual(0.81f, message.object_confidence, Tolerance);
        }

        [Test]
        public void EveryPersonalityTraitIsMapped() {
            FishPersonality personality = Parse(FullPayload).personality;

            Assert.IsNotNull(personality);
            Assert.AreEqual(0.81f, personality.speed, Tolerance);
            Assert.AreEqual(0.90f, personality.curiosity, Tolerance);
            Assert.AreEqual(0.22f, personality.fear, Tolerance);
            Assert.AreEqual(0.68f, personality.social, Tolerance);
            Assert.AreEqual(0.14f, personality.aggression, Tolerance);
            Assert.AreEqual(0.55f, personality.grace, Tolerance);
            Assert.AreEqual(0.40f, personality.preferred_depth, Tolerance);
            Assert.AreEqual(0.12f, personality.rarity_score, Tolerance);
            Assert.AreEqual("common", personality.rarity_tier);
        }

        [Test]
        public void EveryFeatureIsMapped() {
            FishFeatures features = Parse(FullPayload).features;

            Assert.IsNotNull(features);
            Assert.AreEqual(1.86f, features.aspect_ratio, Tolerance);
            Assert.AreEqual(0.41f, features.circularity, Tolerance);
            Assert.AreEqual(0.78f, features.solidity, Tolerance);
            Assert.AreEqual(0.62f, features.extent, Tolerance);
            Assert.AreEqual(0.71f, features.elongation, Tolerance);
            Assert.AreEqual(-12.4f, features.orientation_deg, Tolerance);
            Assert.AreEqual(2840.5f, features.perimeter, 0.01f);
            Assert.AreEqual(34.2f, features.complexity, 0.01f);
            Assert.AreEqual(46, features.vertex_count);
            Assert.AreEqual(0.19f, features.edge_density, Tolerance);
            Assert.AreEqual(0.72f, features.symmetry, Tolerance);
            Assert.AreEqual(0.31f, features.ink_coverage, Tolerance);
            Assert.AreEqual(4.2f, features.stroke_width, Tolerance);
            Assert.AreEqual(0.55f, features.spikiness, Tolerance);
            Assert.AreEqual(3, features.color_count);
            Assert.AreEqual(38.4f, features.colorfulness, 0.01f);
        }

        [Test]
        public void TheDominantColourListIsMappedInOrder() {
            FishFeatures features = Parse(FullPayload).features;

            Assert.IsNotNull(features.dominant_colors);
            Assert.AreEqual(3, features.dominant_colors.Length);
            Assert.AreEqual("#2f6fb8", features.dominant_colors[0].hex);
            Assert.AreEqual(0.44f, features.dominant_colors[0].ratio, Tolerance);
            Assert.AreEqual("#1b1b1b", features.dominant_colors[2].hex, "Order matters: the list is descending by ratio.");
        }

        [Test]
        public void HueIsFlaggedAsUnusableOnANearGreyscaleDrawing() {
            // Contract section 4: mean_hue is circular and undefined below mean_sat 0.15. A pencil sketch
            // hits this every time, and a hue of 0 read as "red" would tint half the archive.
            Assert.IsTrue(Parse(FullPayload).features.HasUsableHue());
            Assert.IsFalse(Parse(FullPayload.Replace("\"mean_sat\":0.61", "\"mean_sat\":0.04")).features.HasUsableHue());
        }

        [Test]
        public void ANullAnatomyBlockIsSurvivable() {
            Assert.IsNull(Parse(FullPayload).anatomy, "anatomy is null until Python phase 2, and may stay null.");
        }

        [Test]
        public void APopulatedAnatomyBlockIsMappedWhenItArrives() {
            string json = FullPayload.Replace("\"anatomy\":null",
                "\"anatomy\":{\"confidence\":0.6,\"eyes\":[{\"x\":0.21,\"y\":0.34,\"r\":0.04}]," +
                "\"head\":{\"x\":0.12,\"y\":0.40},\"tail\":{\"x\":0.93,\"y\":0.46},\"fins\":[{\"x\":0.48,\"y\":0.78}]}");

            FishAnatomy anatomy = Parse(json).anatomy;

            Assert.IsNotNull(anatomy);
            Assert.AreEqual(0.6f, anatomy.confidence, Tolerance);
            Assert.AreEqual(1, anatomy.eyes.Length);
            Assert.AreEqual(0.21f, anatomy.eyes[0].x, Tolerance);
            Assert.AreEqual(0.04f, anatomy.eyes[0].r, Tolerance);
            Assert.AreEqual(0.12f, anatomy.head.x, Tolerance);
            Assert.AreEqual(0.93f, anatomy.tail.x, Tolerance);
            Assert.AreEqual(1, anatomy.fins.Length);
        }

        [Test]
        public void ALegacyPayloadWithNoOptionalBlocksStillParses() {
            string json =
                "{\"id\":\"fish_legacy_001\",\"timestamp\":\"2026-08-01T10:00:00\"," +
                "\"png_base64\":\"iVBORw0KGgo=\",\"width\":332,\"height\":153,\"schema_version\":1}";

            FishCaptureMessage message = Parse(json);
            string reason;

            Assert.IsTrue(message.IsUsable(out reason));
            Assert.IsNull(message.features);
            Assert.IsNull(message.personality);
            Assert.IsNull(message.anatomy);
            Assert.IsNull(message.object_type);
        }

        [Test]
        public void UnknownFieldsAreIgnoredRatherThanThrowing() {
            // Contract section 3 rule 1: new keys are added without a version bump, so this client has to
            // survive fields it has never heard of.
            string json = FullPayload.Replace("\"replay\":true", "\"replay\":true,\"a_field_from_the_future\":{\"nested\":[1,2,3]}");

            FishCaptureMessage message = Parse(json);

            Assert.AreEqual("fish_20260901_182346_001", message.id);
            Assert.IsNotNull(message.personality);
        }

        [Test]
        public void AFutureSchemaVersionStillParses() {
            FishCaptureMessage message = Parse(FullPayload.Replace("\"schema_version\":2", "\"schema_version\":7"));
            string reason;

            Assert.AreEqual(7, message.schema_version);
            Assert.IsTrue(message.IsUsable(out reason), "Contract section 3 rule 4: log loudly, but keep working.");
        }

        [Test]
        public void IntegerValuedFloatsAreAccepted() {
            // JSON has one number type and Python emits 1 rather than 1.0 for a whole value, so every float
            // field has to tolerate arriving as an integer.
            string json = FullPayload.Replace("\"speed\":0.81", "\"speed\":1").Replace("\"fear\":0.22", "\"fear\":0");

            FishPersonality personality = Parse(json).personality;

            Assert.AreEqual(1f, personality.speed, Tolerance);
            Assert.AreEqual(0f, personality.fear, Tolerance);
        }
    }
}
