using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the swimming area: fish can never leave it, and spawn points always start inside it. </summary>
    public sealed class AquariumBoundsTests {
        private const float Tolerance = 0.0005f;

        private GameObject host;
        private AquariumBounds bounds;

        [SetUp]
        public void SetUp() {
            host = new GameObject("AquariumBoundsTestHost");
            bounds = host.AddComponent<AquariumBounds>();
            bounds.ConfigureExplicit(Vector2.zero, new Vector2(20f, 10f));
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void ExplicitBoundsProduceTheExpectedEdges() {
            Assert.AreEqual(-10f, bounds.MinX, Tolerance);
            Assert.AreEqual(10f, bounds.MaxX, Tolerance);
            Assert.AreEqual(-5f, bounds.MinY, Tolerance);
            Assert.AreEqual(5f, bounds.MaxY, Tolerance);
        }

        [Test]
        public void ClampPullsAPositionBackInside() {
            Vector2 clamped = bounds.Clamp(new Vector2(500f, -900f), 0f);

            Assert.AreEqual(10f, clamped.x, Tolerance);
            Assert.AreEqual(-5f, clamped.y, Tolerance);
        }

        [Test]
        public void ClampRespectsPadding() {
            Vector2 clamped = bounds.Clamp(new Vector2(500f, 500f), 2f);

            Assert.AreEqual(8f, clamped.x, Tolerance);
            Assert.AreEqual(3f, clamped.y, Tolerance);
        }

        [Test]
        public void PaddingShrinksTheAreaOnEverySide() {
            Rect padded = bounds.GetPaddedArea(1.5f);

            Assert.AreEqual(17f, padded.width, Tolerance);
            Assert.AreEqual(7f, padded.height, Tolerance);
            Assert.AreEqual(0f, padded.center.x, Tolerance);
            Assert.AreEqual(0f, padded.center.y, Tolerance);
        }

        [Test]
        public void RandomPointsAlwaysLandInsideThePaddedArea() {
            Rect padded = bounds.GetPaddedArea(1f);

            for (int i = 0; i < 200; i++) {
                Vector2 point = bounds.RandomPointInside(1f);

                Assert.GreaterOrEqual(point.x, padded.xMin - Tolerance);
                Assert.LessOrEqual(point.x, padded.xMax + Tolerance);
                Assert.GreaterOrEqual(point.y, padded.yMin - Tolerance);
                Assert.LessOrEqual(point.y, padded.yMax + Tolerance);
            }
        }

        [Test]
        public void EdgeEntryPointsStartOnAnEdgeAndHeadInwards() {
            Rect padded = bounds.GetPaddedArea(0.5f);

            for (int i = 0; i < 100; i++) {
                Vector2 inward;
                Vector2 entry = bounds.RandomEdgeEntryPoint(0.5f, out inward);

                bool onLeftEdge = Mathf.Abs(entry.x - padded.xMin) < Tolerance;
                bool onRightEdge = Mathf.Abs(entry.x - padded.xMax) < Tolerance;

                Assert.IsTrue(onLeftEdge || onRightEdge, "Entry points must sit on the left or right edge.");
                Assert.GreaterOrEqual(entry.y, padded.yMin - Tolerance);
                Assert.LessOrEqual(entry.y, padded.yMax + Tolerance);

                float towardsCentre = padded.center.x - entry.x;
                Assert.Greater(towardsCentre * inward.x, 0f, "The entry heading must point into the aquarium.");
            }
        }

        [Test]
        public void ContainsRejectsPositionsOutsideTheArea() {
            Assert.IsTrue(bounds.Contains(Vector2.zero));
            Assert.IsFalse(bounds.Contains(new Vector2(11f, 0f)));
            Assert.IsFalse(bounds.Contains(new Vector2(0f, -6f)));
        }
    }
}
