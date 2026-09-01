using System.IO;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the checks that stop Unity reading a PNG the capture process has not finished writing. </summary>
    public sealed class FishFileReadinessTests {
        private FishTestFileFixture fixture;

        [SetUp]
        public void SetUp() {
            fixture = new FishTestFileFixture();
        }

        [TearDown]
        public void TearDown() {
            fixture.Dispose();
        }

        [Test]
        public void ACompleteFileIsReportedAsReady() {
            string path = fixture.WritePng("fish_001.png", 32, 32);

            long size;
            string reason;

            Assert.IsTrue(FishFileReadiness.IsReadyToRead(path, out size, out reason), reason);
            Assert.Greater(size, 0);
        }

        [Test]
        public void AnEmptyFileIsNotReady() {
            string path = fixture.WriteRawFile("fish_empty.png", new byte[0]);

            long size;
            string reason;

            Assert.IsFalse(FishFileReadiness.IsReadyToRead(path, out size, out reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void AFileWithoutAPngHeaderIsNotReady() {
            string path = fixture.WriteRawFile("fish_partial.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

            long size;
            string reason;

            Assert.IsFalse(FishFileReadiness.IsReadyToRead(path, out size, out reason));
        }

        [Test]
        public void AFileStillHeldOpenForWritingIsNotReady() {
            string path = fixture.WritePng("fish_locked.png", 32, 32);

            using (FileStream writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read)) {
                long size;
                string reason;

                Assert.IsFalse(FishFileReadiness.IsReadyToRead(path, out size, out reason), "A file another process still has open must not be loaded.");
                Assert.IsNotEmpty(reason);
                Assert.IsNotNull(writer);
            }
        }

        [Test]
        public void AMissingFileIsNotReady() {
            long size;
            string reason;

            Assert.IsFalse(FishFileReadiness.IsReadyToRead(Path.Combine(fixture.FolderPath, "missing.png"), out size, out reason));
        }
    }
}
