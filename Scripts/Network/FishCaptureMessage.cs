using System;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Rectangle from the capture payload. Kept as a wire type so the field names match the JSON exactly.
    /// </summary>
    [Serializable]
    public sealed class FishBoundingBox {
        public int x;
        public int y;
        public int width;
        public int height;
    }

    /// <summary>
    /// One entry of the features.dominant_colors list: an sRGB hex string and the fraction of the mask
    /// it covers. Descending by ratio, at most five entries.
    /// </summary>
    [Serializable]
    public sealed class FishDominantColor {
        public string hex;
        public float ratio;
    }

    /// <summary>
    /// Raw measurements of the drawing, from section 4 of the interaction contract. All measured on the
    /// segmentation mask in image coordinates, origin top-left.
    ///
    /// Unity reads these for presentation decisions only - on-screen size from foreground_area, colour
    /// accents from dominant_colors. It must never derive behaviour from them: personality is Python's to
    /// compute, and recomputing it here would drift out of agreement with the archived metadata.
    /// </summary>
    [Serializable]
    public sealed class FishFeatures {
        public float aspect_ratio;
        public float circularity;
        public float solidity;
        public float extent;
        public float elongation;
        public float orientation_deg;
        public float perimeter;
        public float complexity;
        public int vertex_count;
        public float edge_density;
        public float symmetry;
        public float ink_coverage;
        public float stroke_width;
        public float spikiness;
        public int color_count;
        public FishDominantColor[] dominant_colors;

        /// <summary> Degrees, 0..359. Undefined when mean_sat is low; guard with HasUsableHue. </summary>
        public float mean_hue;

        public float mean_sat;
        public float mean_val;
        public float colorfulness;

        /// <summary> Contract section 4: hue is circular and meaningless on a near-greyscale drawing. </summary>
        public bool HasUsableHue() {
            return mean_sat >= 0.15f;
        }
    }

    /// <summary>
    /// The eight personality traits, from section 6 of the interaction contract. Every value is 0..1
    /// inclusive and deterministic: the same drawing photographed twice produces the same personality.
    ///
    /// Unity must not add randomness on top. That determinism is the whole point of the feature - a child
    /// has to be able to say "that one is mine, it swims like that because of how I drew it".
    /// </summary>
    [Serializable]
    public sealed class FishPersonality {
        public float speed;
        public float curiosity;
        public float fear;
        public float social;
        public float aggression;
        public float grace;

        /// <summary> 0 hugs the surface, 1 hugs the floor. Not the same axis as parallax depth. </summary>
        public float preferred_depth;

        public float rarity_score;

        /// <summary> common | uncommon | rare | legendary. Pre-bucketed so Unity does not hardcode thresholds. </summary>
        public string rarity_tier;
    }

    /// <summary> One detected eye. Normalised 0..1 against the PNG, origin top-left; r is the radius. </summary>
    [Serializable]
    public sealed class FishAnatomyEye {
        public float x;
        public float y;
        public float r;
    }

    /// <summary> One anatomy landmark. Normalised 0..1 against the PNG, origin top-left. </summary>
    [Serializable]
    public sealed class FishAnatomyPoint {
        public float x;
        public float y;
    }

    /// <summary>
    /// Anatomy landmarks, from section 7 of the interaction contract. Phase 2 on the Python side, so this
    /// block is null today and may stay null.
    ///
    /// Every landmark is optional: children's drawings defeat landmark detection routinely, and the
    /// geometric fallback will be used often.
    ///
    /// Coordinates are OpenCV convention: normalised 0..1, origin TOP-LEFT, +y DOWN. Unity textures start
    /// at the bottom-left, so anything reading these must flip y - uv.y = 1f - a.y. Getting that wrong
    /// puts every eye on the fish's belly, and the symptom looks like a detection failure rather than a
    /// coordinate bug. Nothing consumes this block yet; the conversion lands with M9.
    /// </summary>
    [Serializable]
    public sealed class FishAnatomy {
        public float confidence;
        public FishAnatomyEye[] eyes;
        public FishAnatomyPoint head;
        public FishAnatomyPoint tail;
        public FishAnatomyPoint[] fins;
    }

    /// <summary>
    /// One fish_captured event, exactly as the Python capture station sends it. Mirrors section 3 of
    /// docs/interaction_contract.md; change the two together or not at all.
    ///
    /// The snake_case field names are deliberate: this is a wire contract owned by the other side of the
    /// socket, and LitJson maps JSON keys onto members by name. Everything downstream works from the
    /// cleaner types this is translated into, so the naming does not leak past the network layer.
    ///
    /// Unknown JSON keys are skipped by the parser rather than throwing, so the capture station can add
    /// fields without breaking this client. The optional blocks - features, personality, anatomy - arrive
    /// as null on legacy schema v1 captures replayed from disk; FishTraitResolver supplies the fallback.
    /// </summary>
    [Serializable]
    public sealed class FishCaptureMessage {
        public string id;
        public string timestamp;

        /// <summary> Monotonic counter within the capture station's output directory. </summary>
        public int sequence;

        /// <summary>
        /// Complete PNG with an alpha channel, base64 encoded, no data-URI prefix.
        /// Deliberately typed as string rather than byte[]: the parser would base64-decode a byte[]
        /// on the main thread, and these payloads reach a megabyte. The decode happens on a worker.
        /// </summary>
        public string png_base64;

        public string format;
        public int width;
        public int height;
        public int[] file_resolution;
        public FishBoundingBox bounding_box;
        public FishBoundingBox bounding_box_frame;
        public int[] source_resolution;

        /// <summary> Mask area in source pixels. Drives on-screen size, so a small drawing stays a small fish. </summary>
        public int foreground_area;

        public string processing_method;

        /// <summary> Segmenter self-assessment, NOT a classifier score. object_confidence is the classifier's. </summary>
        public float confidence;

        public int schema_version;

        /// <summary> fish | plant | rock | jellyfish | creature | unknown. Null on schema v1. </summary>
        public string object_type;

        /// <summary> Below 0.5 the classifier is guessing. Still spawn the named type; skip type-specific VFX. </summary>
        public float object_confidence;

        public FishFeatures features;
        public FishPersonality personality;
        public FishAnatomy anatomy;

        /// <summary> Absent on live events, so the default of false is the correct reading of a live arrival. </summary>
        public bool replay;

        /// <summary> Cheap sanity check before any decoding work is queued. </summary>
        public bool IsUsable(out string reason) {
            if (string.IsNullOrEmpty(id)) {
                reason = "payload has no id";
                return false;
            }

            if (string.IsNullOrEmpty(png_base64)) {
                reason = "payload has no png_base64 data";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public override string ToString() {
            return id + " " + width + "x" + height + " " + (replay ? "replay" : "live");
        }
    }
}
