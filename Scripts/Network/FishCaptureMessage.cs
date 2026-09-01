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
    /// One fish_captured event, exactly as the Python capture station sends it.
    ///
    /// The snake_case field names are deliberate: this is a wire contract owned by the other side of the
    /// socket, and LitJson maps JSON keys onto members by name. Everything downstream works from the
    /// cleaner types this is translated into, so the naming does not leak past the network layer.
    ///
    /// Unknown JSON keys are skipped by the parser rather than throwing, so the capture station can add
    /// fields without breaking this client.
    /// </summary>
    [Serializable]
    public sealed class FishCaptureMessage {
        public string id;
        public string timestamp;

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
        public int foreground_area;
        public string processing_method;
        public float confidence;
        public int schema_version;

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
