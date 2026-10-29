using System.IO;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The local artwork cache, which is what makes a restart survivable at all: Python replays only its
    /// ten most recent captures, so every fish older than that comes back from here or not at all.
    ///
    /// These touch the real file system under persistentDataPath and clean up after themselves. Capture
    /// ids are opaque per contract section 3 rule 3, so the file-naming tests matter more than they look -
    /// an id containing a path separator would otherwise write outside the cache directory.
    /// </summary>
    public sealed class FishArtworkCacheTests {
        private const string SampleId = "fish_20260902_120000_001";

        private static readonly byte[] ValidPng = {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        };

        [TearDown]
        public void RemoveTestArtwork() {
            FishArtworkCache.TryRemove(SampleId);
            FishArtworkCache.TryRemove("other_fish");
            FishArtworkCache.TryRemove("../escape");
            FishArtworkCache.TryRemove("a/b/c");
        }

        [Test]
        public void StoringThenLoadingReturnsTheSameBytes() {
            string error;
            Assert.IsTrue(FishArtworkCache.TryStore(SampleId, ValidPng, out error), error);

            byte[] loaded;
            Assert.IsTrue(FishArtworkCache.TryLoad(SampleId, out loaded, out error), error);
            Assert.AreEqual(ValidPng, loaded);
        }

        [Test]
        public void AnUncachedDrawingIsReportedRatherThanThrowing() {
            byte[] loaded;
            string error;

            Assert.IsFalse(FishArtworkCache.TryLoad("never_stored_" + SampleId, out loaded, out error));
            Assert.IsNotEmpty(error);
            Assert.IsNull(loaded);
        }

        [Test]
        public void ContainsReflectsWhatHasBeenStored() {
            string error;

            Assert.IsFalse(FishArtworkCache.Contains(SampleId));
            FishArtworkCache.TryStore(SampleId, ValidPng, out error);
            Assert.IsTrue(FishArtworkCache.Contains(SampleId));
        }

        [Test]
        public void StoringTheSameDrawingTwiceIsCheapAndHarmless() {
            // The bytes for an id never change, so a replay burst must not rewrite the whole cache.
            string error;

            Assert.IsTrue(FishArtworkCache.TryStore(SampleId, ValidPng, out error));
            Assert.IsTrue(FishArtworkCache.TryStore(SampleId, ValidPng, out error));
            Assert.IsTrue(FishArtworkCache.Contains(SampleId));
        }

        [Test]
        public void EmptyOrMissingImageDataIsRefused() {
            string error;

            Assert.IsFalse(FishArtworkCache.TryStore(SampleId, null, out error));
            Assert.IsFalse(FishArtworkCache.TryStore(SampleId, new byte[0], out error));
            Assert.IsFalse(FishArtworkCache.TryStore(string.Empty, ValidPng, out error));
        }

        [Test]
        public void AFileThatIsNotAPngIsRefusedOnLoad() {
            // Guards against a truncated write or a hand-placed file: a cached blob that decodes to
            // nothing would produce an invisible fish rather than an honest failure.
            string path = FishArtworkCache.ResolvePath(SampleId);
            Directory.CreateDirectory(FishArtworkCache.ResolveCacheFolder());
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

            byte[] loaded;
            string error;

            Assert.IsFalse(FishArtworkCache.TryLoad(SampleId, out loaded, out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void EveryHostileIdStillResolvesInsideTheCacheDirectory() {
            // Contract section 3 rule 3: ids are opaque, so they are never parsed - but they do become
            // file names, and one carrying a separator must not be able to write outside the cache.
            //
            // The invariant tested is the one that matters: the resolved path's directory is the cache
            // directory. A name merely CONTAINING dots is harmless - ".." only traverses when it is a
            // whole path segment - so asserting on the text of the name would be the wrong check.
            string[] hostileIds = { "../escape", "../../etc/passwd", "a/b/c", "a\\b\\c", "..", ".", "with spaces", "with:colon" };
            string cacheFolder = FishArtworkCache.ResolveCacheFolder();

            for (int i = 0; i < hostileIds.Length; i++) {
                string name = FishArtworkCache.BuildFileName(hostileIds[i]);
                string resolved = FishArtworkCache.ResolvePath(hostileIds[i]);

                Assert.IsFalse(name.Contains("/"), hostileIds[i] + " produced the name " + name);
                Assert.IsFalse(name.Contains("\\"), hostileIds[i] + " produced the name " + name);
                Assert.IsTrue(name.EndsWith(".png"), hostileIds[i] + " produced the name " + name);

                string resolvedFolder = PathUtility.NormalizeFolder(Path.GetDirectoryName(resolved));
                Assert.AreEqual(cacheFolder, resolvedFolder, hostileIds[i] + " escaped to " + resolved);
            }
        }

        [Test]
        public void IdsThatSanitiseToTheSameTextStillGetDifferentFiles() {
            // "a/b" and "a_b" both sanitise to "a_b", so the appended hash of the original id is what
            // keeps two drawings from overwriting each other.
            Assert.AreNotEqual(FishArtworkCache.BuildFileName("a/b"), FishArtworkCache.BuildFileName("a_b"));
        }

        [Test]
        public void AFileNameIsStableForAGivenId() {
            Assert.AreEqual(FishArtworkCache.BuildFileName(SampleId), FishArtworkCache.BuildFileName(SampleId));
        }

        [Test]
        public void AVeryLongIdProducesAUsableFileName() {
            string longId = new string('x', 4000);
            string name = FishArtworkCache.BuildFileName(longId);

            Assert.Less(name.Length, 160, "A path-length limit is a real failure mode on Windows.");
            Assert.IsTrue(name.EndsWith(".png"));
        }

        [Test]
        public void AnEmptyIdStillProducesAFileName() {
            Assert.IsTrue(FishArtworkCache.BuildFileName(null).EndsWith(".png"));
            Assert.IsTrue(FishArtworkCache.BuildFileName(string.Empty).EndsWith(".png"));
        }

        [Test]
        public void RemovingDeletesTheCachedDrawing() {
            string error;
            FishArtworkCache.TryStore(SampleId, ValidPng, out error);

            Assert.IsTrue(FishArtworkCache.TryRemove(SampleId));
            Assert.IsFalse(FishArtworkCache.Contains(SampleId));
            Assert.IsFalse(FishArtworkCache.TryRemove(SampleId), "Removing something already gone is not a success.");
        }

        [Test]
        public void TwoDrawingsAreStoredIndependently() {
            string error;
            FishArtworkCache.TryStore(SampleId, ValidPng, out error);
            FishArtworkCache.TryStore("other_fish", ValidPng, out error);

            Assert.IsTrue(FishArtworkCache.Contains(SampleId));
            Assert.IsTrue(FishArtworkCache.Contains("other_fish"));

            FishArtworkCache.TryRemove(SampleId);

            Assert.IsFalse(FishArtworkCache.Contains(SampleId));
            Assert.IsTrue(FishArtworkCache.Contains("other_fish"), "Removing one drawing must not take the others with it.");
        }

        [Test]
        public void TheCacheFolderIsUnderPersistentDataPath() {
            // Never captures/: contract section 11 says that directory is Python's, and a consumer writing
            // into it would collide with the capture loop.
            string folder = FishArtworkCache.ResolveCacheFolder();

            Assert.IsTrue(folder.Contains(FishArtworkCache.CacheFolderName));
            Assert.IsFalse(folder.Contains("captures"));
        }
    }
}
