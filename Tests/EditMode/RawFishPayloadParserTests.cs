using NUnit.Framework;

using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The fast path exists to keep a megabyte of JSON off the main thread, so it has to be exactly right
    /// about which frames it claims. Anything it does not recognise must fall through to the stock parser.
    /// </summary>
    public sealed class RawFishPayloadParserTests {
        private const string EventName = "fish_captured";
        private const string Payload = "{\"id\":\"fish_001\",\"png_base64\":\"iVBORw0KGgo=\",\"schema_version\":1}";

        [Test]
        public void ExtractsTheArgumentFromAPlainEventFrame() {
            string argument;

            Assert.IsTrue(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "\"," + Payload + "]", EventName, out argument));
            Assert.AreEqual(Payload, argument);
        }

        [Test]
        public void ExtractsTheArgumentWhenTheFrameCarriesANamespaceAndAckId() {
            string argument;

            Assert.IsTrue(RawFishPayloadParser.TryExtractCaptureArgument("42/aquarium,17[\"" + EventName + "\"," + Payload + "]", EventName, out argument));
            Assert.AreEqual(Payload, argument);
        }

        [Test]
        public void ToleratesWhitespaceAroundTheArgument() {
            string argument;

            Assert.IsTrue(RawFishPayloadParser.TryExtractCaptureArgument("42[ \"" + EventName + "\" , " + Payload + " ]", EventName, out argument));
            Assert.AreEqual(Payload, argument);
        }

        [Test]
        public void DeclinesADifferentEvent() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("42[\"some_other_event\"," + Payload + "]", EventName, out argument));
        }

        [Test]
        public void DeclinesAnEventWhoseNameMerelyStartsWithOurs() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "_v2\"," + Payload + "]", EventName, out argument),
                "Matching on a prefix would hijack a different event.");
        }

        [Test]
        public void DeclinesConnectAndDisconnectFrames() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("40", EventName, out argument));
            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("41", EventName, out argument));
        }

        [Test]
        public void DeclinesBinaryEventFramesSoAttachmentsStayWithTheStockParser() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("451-[\"" + EventName + "\"," + Payload + "]", EventName, out argument));
        }

        [Test]
        public void DeclinesAnEventCarryingMoreThanOneArgument() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "\"," + Payload + ",\"extra\"]", EventName, out argument),
                "The contract is one object per event; anything else belongs to the stock parser.");
        }

        [Test]
        public void DeclinesAnEventWhoseArgumentIsNotAnObject() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "\",\"just a string\"]", EventName, out argument));
        }

        [Test]
        public void DeclinesMalformedAndEmptyFrames() {
            string argument;

            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument(string.Empty, EventName, out argument));
            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument(null, EventName, out argument));
            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "\"", EventName, out argument));
            Assert.IsFalse(RawFishPayloadParser.TryExtractCaptureArgument("not a socket.io frame", EventName, out argument));
        }

        [Test]
        public void TheExtractedArgumentSurvivesARoundTripThroughTheJsonMapper() {
            string argument;
            Assert.IsTrue(RawFishPayloadParser.TryExtractCaptureArgument("42[\"" + EventName + "\"," + Payload + "]", EventName, out argument));

            FishCaptureMessage message = BestHTTP.JSON.LitJson.JsonMapper.ToObject<FishCaptureMessage>(argument);

            Assert.AreEqual("fish_001", message.id);
            Assert.AreEqual("iVBORw0KGgo=", message.png_base64);
        }
    }
}
