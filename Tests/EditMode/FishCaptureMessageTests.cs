using NUnit.Framework;

using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Locks down the wire contract. These run against the same LitJson mapper BestHTTP uses, so a rename
    /// or a type change on either side of the socket fails here rather than at the installation.
    /// </summary>
    public sealed class FishCaptureMessageTests {
        private const string LiveEventJson =
            "{\"id\":\"fish_20260901_163556_002\",\"timestamp\":\"2026-09-01T16:35:56\"," +
            "\"png_base64\":\"iVBORw0KGgo=\",\"format\":\"png\",\"width\":332,\"height\":153," +
            "\"file_resolution\":[332,153]," +
            "\"bounding_box\":{\"x\":237,\"y\":0,\"width\":332,\"height\":153}," +
            "\"bounding_box_frame\":{\"x\":537,\"y\":150,\"width\":332,\"height\":153}," +
            "\"source_resolution\":[640,480],\"foreground_area\":20395," +
            "\"processing_method\":\"opencv\",\"confidence\":0.1147,\"schema_version\":1}";

        private static FishCaptureMessage Parse(string json) {
            return BestHTTP.JSON.LitJson.JsonMapper.ToObject<FishCaptureMessage>(json);
        }

        [Test]
        public void EveryContractFieldIsMapped() {
            FishCaptureMessage message = Parse(LiveEventJson);

            Assert.AreEqual("fish_20260901_163556_002", message.id);
            Assert.AreEqual("2026-09-01T16:35:56", message.timestamp);
            Assert.AreEqual("iVBORw0KGgo=", message.png_base64);
            Assert.AreEqual("png", message.format);
            Assert.AreEqual(332, message.width);
            Assert.AreEqual(153, message.height);
            Assert.AreEqual(20395, message.foreground_area);
            Assert.AreEqual("opencv", message.processing_method);
            Assert.AreEqual(0.1147f, message.confidence, 0.0001f);
            Assert.AreEqual(1, message.schema_version);
        }

        [Test]
        public void ArrayAndNestedObjectFieldsAreMapped() {
            FishCaptureMessage message = Parse(LiveEventJson);

            Assert.AreEqual(new[] { 332, 153 }, message.file_resolution);
            Assert.AreEqual(new[] { 640, 480 }, message.source_resolution);

            Assert.IsNotNull(message.bounding_box);
            Assert.AreEqual(237, message.bounding_box.x);
            Assert.AreEqual(332, message.bounding_box.width);

            Assert.IsNotNull(message.bounding_box_frame);
            Assert.AreEqual(537, message.bounding_box_frame.x);
            Assert.AreEqual(150, message.bounding_box_frame.y);
        }

        [Test]
        public void AMissingReplayFlagReadsAsALiveEvent() {
            Assert.IsFalse(Parse(LiveEventJson).replay, "replay is absent on live events, so it must default to false.");
        }

        [Test]
        public void AReplayedEventIsFlagged() {
            string json = LiveEventJson.Replace("\"schema_version\":1", "\"replay\":true,\"schema_version\":1");

            Assert.IsTrue(Parse(json).replay);
        }

        [Test]
        public void UnknownFieldsAreIgnoredRatherThanThrowing() {
            string json = LiveEventJson.Replace("\"schema_version\":1", "\"schema_version\":2,\"a_field_added_later\":{\"nested\":[1,2,3]}");

            FishCaptureMessage message = Parse(json);

            Assert.IsNotNull(message, "The capture station must be able to add fields without breaking this client.");
            Assert.AreEqual(2, message.schema_version);
            Assert.AreEqual("fish_20260901_163556_002", message.id);
        }

        [Test]
        public void APayloadWithArtworkIsUsable() {
            string reason;

            Assert.IsTrue(Parse(LiveEventJson).IsUsable(out reason), reason);
        }

        [Test]
        public void APayloadWithoutArtworkIsRejected() {
            string json = LiveEventJson.Replace("\"png_base64\":\"iVBORw0KGgo=\"", "\"png_base64\":\"\"");

            string reason;
            Assert.IsFalse(Parse(json).IsUsable(out reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void APayloadWithoutAnIdIsRejected() {
            string json = LiveEventJson.Replace("\"id\":\"fish_20260901_163556_002\"", "\"id\":\"\"");

            string reason;
            Assert.IsFalse(Parse(json).IsUsable(out reason));
        }

        [Test]
        public void ConfidenceIsCarriedButNeverGatesAnything() {
            string json = LiveEventJson.Replace("\"confidence\":0.1147", "\"confidence\":0.01");

            string reason;
            FishCaptureMessage message = Parse(json);

            Assert.AreEqual(0.01f, message.confidence, 0.0001f);
            Assert.IsTrue(message.IsUsable(out reason), "Low confidence is normal and healthy; it must not stop a fish spawning.");
        }
    }
}
