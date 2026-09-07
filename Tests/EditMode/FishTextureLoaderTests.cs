using System.IO;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the PNG to Texture2D to Sprite pipeline, including transparency preservation. </summary>
    public sealed class FishTextureLoaderTests {
        private FishTestFileFixture fixture;

        [SetUp]
        public void SetUp() {
            fixture = new FishTestFileFixture();
        }

        [TearDown]
        public void TearDown() {
            FishSpriteCache.Clear();
            fixture.Dispose();
        }

        [Test]
        public void TryLoadSprite_ProducesSpriteMatchingSourceDimensions() {
            string path = fixture.WritePng("fish_001.png", 200, 100);

            Sprite sprite;
            string error;
            bool loaded = FishTextureLoader.TryLoadSprite(path, 100f, false, out sprite, out error);

            Assert.IsTrue(loaded, "Loading a valid PNG should succeed. Error: " + error);
            Assert.IsNotNull(sprite);
            Assert.AreEqual(200, sprite.texture.width);
            Assert.AreEqual(100, sprite.texture.height);
            Assert.AreEqual(200f, sprite.rect.width);
            Assert.AreEqual(100f, sprite.rect.height);

            FishTextureLoader.ReleaseSprite(sprite);
        }

        [Test]
        public void TryLoadSprite_PreservesAlphaChannel() {
            string path = fixture.WritePng("fish_alpha.png", 32, 8);

            Sprite sprite;
            string error;
            bool loaded = FishTextureLoader.TryLoadSprite(path, 100f, false, out sprite, out error);

            Assert.IsTrue(loaded, error);

            Color opaquePixel = sprite.texture.GetPixel(2, 4);
            Color transparentPixel = sprite.texture.GetPixel(29, 4);

            Assert.AreEqual(1f, opaquePixel.a, 0.01f, "The opaque half of the drawing must stay opaque.");
            Assert.AreEqual(0f, transparentPixel.a, 0.01f, "The transparent background must stay transparent.");

            FishTextureLoader.ReleaseSprite(sprite);
        }

        [Test]
        public void TryLoadSprite_UsesACentredPivotSoTheFishRotatesAboutItsMiddle() {
            string path = fixture.WritePng("fish_pivot.png", 64, 32);

            Sprite sprite;
            string error;
            Assert.IsTrue(FishTextureLoader.TryLoadSprite(path, 100f, false, out sprite, out error), error);

            Assert.AreEqual(32f, sprite.pivot.x, 0.01f);
            Assert.AreEqual(16f, sprite.pivot.y, 0.01f);

            FishTextureLoader.ReleaseSprite(sprite);
        }

        [Test]
        public void TryReadFile_RejectsFilesThatAreNotPng() {
            string path = fixture.WriteRawFile("not_a_fish.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            byte[] bytes;
            string error;
            bool read = FishTextureLoader.TryReadFile(path, out bytes, out error);

            Assert.IsFalse(read);
            Assert.IsNull(bytes);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryReadFile_RejectsEmptyFiles() {
            string path = fixture.WriteRawFile("empty.png", new byte[0]);

            byte[] bytes;
            string error;

            Assert.IsFalse(FishTextureLoader.TryReadFile(path, out bytes, out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryReadFile_RejectsMissingFiles() {
            byte[] bytes;
            string error;

            Assert.IsFalse(FishTextureLoader.TryReadFile(Path.Combine(fixture.FolderPath, "missing.png"), out bytes, out error));
        }

        [Test]
        public void SpriteCache_SharesOneTextureBetweenFishFromTheSameFile() {
            string path = fixture.WritePng("shared.png", 48, 48);

            Sprite first;
            Sprite second;
            string error;

            Assert.IsTrue(FishSpriteCache.TryAcquire(path, 100f, out first, out error), error);
            Assert.IsTrue(FishSpriteCache.TryAcquire(path, 100f, out second, out error), error);

            Assert.AreSame(first, second, "Two fish from one PNG must share a single sprite and texture.");
            Assert.AreEqual(2, FishSpriteCache.GetReferenceCount(path));

            FishSpriteCache.Release(path);
            Assert.AreEqual(1, FishSpriteCache.GetReferenceCount(path), "Releasing one fish must not free a texture another fish is using.");

            FishSpriteCache.Release(path);
            Assert.AreEqual(0, FishSpriteCache.GetReferenceCount(path));
            Assert.AreEqual(0, FishSpriteCache.TrackedSpriteCount);
        }
    }
}
