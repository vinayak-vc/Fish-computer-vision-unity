using System;
using System.IO;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// Answers one question: has the writing process finished with this file yet? A PNG that is
    /// still being written is either locked, zero length, or missing its header, and reading it
    /// early produces a corrupt fish.
    /// </summary>
    public static class FishFileReadiness {
        private const int PngHeaderLength = 8;

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static bool TryGetFileSize(string absolutePath, out long size) {
            size = 0;

            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return false;
            }

            try {
                FileInfo info = new FileInfo(absolutePath);
                if (!info.Exists) {
                    return false;
                }

                size = info.Length;
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary> Opens the file with no sharing. Succeeding means no other process still holds a write handle. </summary>
        public static bool CanOpenExclusively(string absolutePath) {
            try {
                using (FileStream stream = File.Open(absolutePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    return stream.Length > 0;
                }
            } catch (Exception) {
                return false;
            }
        }

        public static bool HasPngHeader(string absolutePath) {
            try {
                using (FileStream stream = File.Open(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    if (stream.Length < PngHeaderLength) {
                        return false;
                    }

                    byte[] header = new byte[PngHeaderLength];
                    int read = stream.Read(header, 0, PngHeaderLength);
                    if (read < PngHeaderLength) {
                        return false;
                    }

                    for (int i = 0; i < PngHeaderLength; i++) {
                        if (header[i] != PngSignature[i]) {
                            return false;
                        }
                    }

                    return true;
                }
            } catch (Exception) {
                return false;
            }
        }

        /// <summary> All three checks together: non-empty, not locked, and a real PNG. </summary>
        public static bool IsReadyToRead(string absolutePath, out long size, out string reason) {
            reason = string.Empty;

            if (!TryGetFileSize(absolutePath, out size)) {
                reason = "file is not accessible";
                return false;
            }

            if (size <= 0) {
                reason = "file is still empty";
                return false;
            }

            if (!CanOpenExclusively(absolutePath)) {
                reason = "file is still locked by the writing process";
                return false;
            }

            if (!HasPngHeader(absolutePath)) {
                reason = "file does not have a PNG header";
                return false;
            }

            return true;
        }
    }
}
