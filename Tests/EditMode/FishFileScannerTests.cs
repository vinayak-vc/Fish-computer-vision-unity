using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers folder scanning: which files are discovered and which are ignored. </summary>
    public sealed class FishFileScannerTests {
        private FishTestFileFixture fixture;
        private List<string> results;

        [SetUp]
        public void SetUp() {
            fixture = new FishTestFileFixture();
            results = new List<string>();
        }

        [TearDown]
        public void TearDown() {
            fixture.Dispose();
        }

        [Test]
        public void Scan_FindsEveryPngInTheFolder() {
            fixture.WritePng("fish_001.png", 16, 16);
            fixture.WritePng("fish_002.png", 16, 16);
            fixture.WritePng("fish_003.png", 16, 16);

            int found = FishFileScanner.Scan(fixture.FolderPath, "*.png", results);

            Assert.AreEqual(3, found);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void Scan_IgnoresFilesThatAreNotPng() {
            fixture.WritePng("fish_001.png", 16, 16);
            fixture.WriteRawFile("notes.txt", new byte[] { 65, 66, 67 });
            fixture.WriteRawFile("fish_002.jpg", new byte[] { 1, 2, 3 });

            int found = FishFileScanner.Scan(fixture.FolderPath, "*.png", results);

            Assert.AreEqual(1, found);
            Assert.IsTrue(results[0].EndsWith("fish_001.png"));
        }

        [Test]
        public void Scan_ReturnsNothingForAMissingFolder() {
            int found = FishFileScanner.Scan(Path.Combine(fixture.FolderPath, "does_not_exist"), "*.png", results);

            Assert.AreEqual(0, found);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Scan_ReturnsNothingForAnEmptyPath() {
            Assert.AreEqual(0, FishFileScanner.Scan(string.Empty, "*.png", results));
        }

        [Test]
        public void Scan_ClearsPreviousResults() {
            fixture.WritePng("fish_001.png", 16, 16);

            results.Add("stale_entry");
            FishFileScanner.Scan(fixture.FolderPath, "*.png", results);

            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results.Contains("stale_entry"));
        }

        [Test]
        public void IsPngFile_MatchesOnlyThePngExtension() {
            Assert.IsTrue(FishFileScanner.IsPngFile("C:/captures/fish_001.png"));
            Assert.IsTrue(FishFileScanner.IsPngFile("C:/captures/fish_001.PNG"));
            Assert.IsFalse(FishFileScanner.IsPngFile("C:/captures/fish_001.pngx"));
            Assert.IsFalse(FishFileScanner.IsPngFile("C:/captures/fish_001.jpg"));
            Assert.IsFalse(FishFileScanner.IsPngFile(string.Empty));
        }
    }
}
