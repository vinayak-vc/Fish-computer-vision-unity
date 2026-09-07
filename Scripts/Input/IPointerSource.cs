using System;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// The visitor, as far as any behaviour in this project is concerned.
    ///
    /// The client's brief is explicit that this installation has no person tracking and no hand tracking,
    /// and that the mouse pointer stands in for the visitor everywhere. This interface is the seam that
    /// keeps that decision reversible: a hand tracker would be a second implementation, and not one line
    /// of fish behaviour would change. Nothing downstream may read UnityEngine.Input directly.
    ///
    /// All positions and velocities are in world units, not screen pixels, so behaviour is identical on
    /// every resolution and aspect ratio.
    /// </summary>
    public interface IPointerSource {
        /// <summary>
        /// False when there is nothing to react to: the pointer is outside the window, the source has no
        /// camera, or a future hand tracker has lost its subject. Fish must fall back to ordinary
        /// swimming rather than steering towards a stale position.
        /// </summary>
        bool IsAvailable { get; }

        Vector2 WorldPosition { get; }

        Vector2 WorldVelocity { get; }

        /// <summary> Magnitude of WorldVelocity, smoothed. This is what a startle threshold is compared against. </summary>
        float Speed { get; }

        /// <summary> Raised with the world position of a press. A hand tracker would raise this on a grab or a tap. </summary>
        event Action<Vector2> Pressed;
    }
}
