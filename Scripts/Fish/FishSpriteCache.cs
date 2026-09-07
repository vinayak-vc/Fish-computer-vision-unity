using System;
using System.Collections.Generic;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Reference-counted store of runtime-loaded fish sprites, keyed by source file path.
    /// Two fish spawned from the same PNG share one Texture2D, and the texture is only released
    /// once the last fish using it is gone. This is what keeps a multi-hour session from leaking.
    /// </summary>
    public static class FishSpriteCache {
        private sealed class CacheEntry {
            public Sprite Sprite { get; set; }
            public int ReferenceCount { get; set; }
        }

        private static readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static int TrackedSpriteCount {
            get { return entries.Count; }
        }

        /// <summary> Returns a shared sprite for the file, loading it only the first time it is requested. </summary>
        public static bool TryAcquire(string absolutePath, float pixelsPerUnit, out Sprite sprite, out string error) {
            sprite = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(absolutePath)) {
                error = "empty file path";
                return false;
            }

            string key = BuildKey(absolutePath);

            CacheEntry existing;
            if (entries.TryGetValue(key, out existing) && existing.Sprite != null) {
                existing.ReferenceCount++;
                sprite = existing.Sprite;
                return true;
            }

            Sprite loaded;
            if (!FishTextureLoader.TryLoadSprite(absolutePath, pixelsPerUnit, true, out loaded, out error)) {
                return false;
            }

            CacheEntry entry = new CacheEntry();
            entry.Sprite = loaded;
            entry.ReferenceCount = 1;
            entries[key] = entry;

            sprite = loaded;
            return true;
        }

        /// <summary>
        /// Same contract as TryAcquire, for a payload that arrived over the network. The key is the capture
        /// id rather than a file path, so socket fish get the identical reference-counted texture lifetime.
        /// </summary>
        public static bool TryAcquireFromBytes(string key, byte[] pngBytes, float pixelsPerUnit, out Sprite sprite, out string error) {
            sprite = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(key)) {
                error = "empty sprite key";
                return false;
            }

            CacheEntry existing;
            if (entries.TryGetValue(key, out existing) && existing.Sprite != null) {
                existing.ReferenceCount++;
                sprite = existing.Sprite;
                return true;
            }

            Sprite loaded;
            if (!FishTextureLoader.TryCreateSpriteFromBytes(pngBytes, key, pixelsPerUnit, true, out loaded, out error)) {
                return false;
            }

            CacheEntry entry = new CacheEntry();
            entry.Sprite = loaded;
            entry.ReferenceCount = 1;
            entries[key] = entry;

            sprite = loaded;
            return true;
        }

        /// <summary> Drops one reference. The sprite and its texture are destroyed when the count reaches zero. </summary>
        public static void Release(string absolutePath) {
            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return;
            }

            string key = BuildKey(absolutePath);

            CacheEntry entry;
            if (!entries.TryGetValue(key, out entry)) {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0) {
                return;
            }

            entries.Remove(key);
            FishTextureLoader.ReleaseSprite(entry.Sprite);
        }

        /// <summary> Destroys every cached sprite. Used on teardown and between tests. </summary>
        public static void Clear() {
            foreach (KeyValuePair<string, CacheEntry> pair in entries) {
                FishTextureLoader.ReleaseSprite(pair.Value.Sprite);
            }

            entries.Clear();
        }

        public static int GetReferenceCount(string absolutePath) {
            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return 0;
            }

            CacheEntry entry;
            if (!entries.TryGetValue(BuildKey(absolutePath), out entry)) {
                return 0;
            }

            return entry.ReferenceCount;
        }

        private static string BuildKey(string absolutePath) {
            return PathUtility.Normalize(absolutePath);
        }

        /// <summary> Domain reload can be skipped in the Editor, which would otherwise leave destroyed sprites in the dictionary. </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() {
            entries.Clear();
        }
    }
}
