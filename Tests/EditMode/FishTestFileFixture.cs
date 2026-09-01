using System;
using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Creates throwaway PNG files on disk for the loader, scanner and queue tests. </summary>
    public sealed class FishTestFileFixture : IDisposable {
        public FishTestFileFixture() {
            FolderPath = PathUtility.Normalize(Path.Combine(Path.GetTempPath(), "FishAquariumTests_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(FolderPath);
        }

        public string FolderPath { get; private set; }

        /// <summary> Writes a real PNG with a fully opaque left half and a fully transparent right half. </summary>
        public string WritePng(string fileName, int width, int height) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    bool opaque = x < (width / 2);
                    texture.SetPixel(x, y, new Color(1f, 0.35f, 0.1f, opaque ? 1f : 0f));
                }
            }

            texture.Apply(false, false);

            byte[] encoded = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            string path = PathUtility.Normalize(Path.Combine(FolderPath, fileName));
            File.WriteAllBytes(path, encoded);
            return path;
        }

        public string WriteRawFile(string fileName, byte[] contents) {
            string path = PathUtility.Normalize(Path.Combine(FolderPath, fileName));
            File.WriteAllBytes(path, contents);
            return path;
        }

        public void Dispose() {
            try {
                if (Directory.Exists(FolderPath)) {
                    Directory.Delete(FolderPath, true);
                }
            } catch (Exception) {
                // A locked temp folder must never fail a test run.
            }
        }
    }
}
