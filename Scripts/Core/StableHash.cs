using System.Text;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// 64-bit FNV-1a over the UTF-8 bytes of a string.
    ///
    /// This is the seed the whole determinism guarantee rests on: the same capture id must produce the
    /// same creature on this machine and on any other, today and after a restart, and the Python side must
    /// be able to reproduce it. string.GetHashCode cannot be used for any of that - it is not stable across
    /// .NET versions, not stable across processes when randomised hashing is on, and not reproducible in
    /// another language. The algorithm is specified in docs/decisions.md D-002; do not change it without
    /// changing that document and the Python implementation in the same commit.
    /// </summary>
    public static class StableHash {
        private const ulong OffsetBasis = 0xCBF29CE484222325UL;
        private const ulong Prime = 0x100000001B3UL;

        /// <summary>
        /// Hash of the identity string. A null or empty value hashes to the offset basis rather than
        /// throwing, so a malformed payload still produces a usable, if shared, creature.
        /// </summary>
        public static ulong Of(string value) {
            if (string.IsNullOrEmpty(value)) {
                return OffsetBasis;
            }

            // Once per fish, not a hot path. UTF-8 rather than the platform encoding so Python's
            // id.encode('utf-8') produces identical bytes for a non-ASCII id.
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            ulong hash = OffsetBasis;

            unchecked {
                for (int i = 0; i < bytes.Length; i++) {
                    hash ^= bytes[i];
                    hash *= Prime;
                }
            }

            return hash;
        }
    }
}
