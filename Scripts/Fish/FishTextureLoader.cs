using System;
using System.IO;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Runtime PNG to Sprite pipeline. Nothing here recolours, filters or resamples the artwork:
    /// the bytes on disk become the texture the fish is rendered with, alpha included.
    /// </summary>
    public static class FishTextureLoader {
        public const int MaximumTextureDimension = 8192;

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary> Cheap guard against half-written files: a complete PNG always starts with this 8 byte signature. </summary>
        public static bool HasPngSignature(byte[] bytes) {
            if (bytes == null || bytes.Length < PngSignature.Length) {
                return false;
            }

            for (int i = 0; i < PngSignature.Length; i++) {
                if (bytes[i] != PngSignature[i]) {
                    return false;
                }
            }

            return true;
        }

        public static bool TryReadFile(string absolutePath, out byte[] bytes, out string error) {
            bytes = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(absolutePath)) {
                error = "empty file path";
                return false;
            }

            if (!File.Exists(absolutePath)) {
                error = "file no longer exists";
                return false;
            }

            try {
                bytes = File.ReadAllBytes(absolutePath);
            } catch (Exception exception) {
                error = exception.Message;
                return false;
            }

            if (bytes.Length == 0) {
                error = "file is empty";
                bytes = null;
                return false;
            }

            if (!HasPngSignature(bytes)) {
                error = "file is not a valid PNG";
                bytes = null;
                return false;
            }

            return true;
        }

        /// <summary> Decodes PNG bytes into an uncompressed RGBA32 texture so transparency survives untouched. </summary>
        public static bool TryCreateTexture(byte[] bytes, string debugName, bool markNonReadable, out Texture2D texture, out string error) {
            texture = null;
            error = string.Empty;

            if (bytes == null || bytes.Length == 0) {
                error = "no image data";
                return false;
            }

            Texture2D created = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            created.name = debugName;
            created.filterMode = FilterMode.Bilinear;
            created.wrapMode = TextureWrapMode.Clamp;

            if (!created.LoadImage(bytes, markNonReadable)) {
                DestroyObject(created);
                error = "PNG could not be decoded";
                return false;
            }

            if (created.width <= 0 || created.height <= 0) {
                DestroyObject(created);
                error = "decoded image has no dimensions";
                return false;
            }

            if (created.width > MaximumTextureDimension || created.height > MaximumTextureDimension) {
                string rejected = created.width + "x" + created.height;
                DestroyObject(created);
                error = "image is larger than the " + MaximumTextureDimension + " pixel limit (" + rejected + ")";
                return false;
            }

            texture = created;
            return true;
        }

        /// <summary> Full-rect sprite with a centred pivot. FullRect keeps the exact source aspect and avoids tight-mesh vertex churn. </summary>
        public static Sprite CreateSprite(Texture2D texture, string spriteName, float pixelsPerUnit) {
            if (texture == null) {
                Debug.LogError("FishTextureLoader: CreateSprite called with a null texture.");
                return null;
            }

            Rect rect = new Rect(0f, 0f, texture.width, texture.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            Sprite sprite = Sprite.Create(texture, rect, pivot, Mathf.Max(1f, pixelsPerUnit), 0, SpriteMeshType.FullRect);
            sprite.name = spriteName;
            return sprite;
        }

        /// <summary> Reads, validates and converts a PNG file in one call. </summary>
        public static bool TryLoadSprite(string absolutePath, float pixelsPerUnit, bool markNonReadable, out Sprite sprite, out string error) {
            sprite = null;

            byte[] bytes;
            if (!TryReadFile(absolutePath, out bytes, out error)) {
                return false;
            }

            string fileName = Path.GetFileName(absolutePath);

            Texture2D texture;
            if (!TryCreateTexture(bytes, fileName, markNonReadable, out texture, out error)) {
                return false;
            }

            sprite = CreateSprite(texture, fileName, pixelsPerUnit);
            if (sprite == null) {
                DestroyObject(texture);
                error = "sprite creation failed";
                return false;
            }

            return true;
        }

        /// <summary> Destroys the sprite and the texture behind it. Skipping the texture is the classic runtime-loading leak. </summary>
        public static void ReleaseSprite(Sprite sprite) {
            if (sprite == null) {
                return;
            }

            Texture2D texture = sprite.texture;
            DestroyObject(sprite);

            if (texture != null) {
                DestroyObject(texture);
            }
        }

        /// <summary> Destroy is deferred at runtime but illegal outside play mode, so edit-mode tests need the immediate form. </summary>
        private static void DestroyObject(UnityEngine.Object target) {
            if (target == null) {
                return;
            }

            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(target);
            } else {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
