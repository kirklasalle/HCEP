// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
namespace HCEP.App;

/// <summary>
/// Shared contract for all HCEP avatar display controls.
///
/// Any control that can act as a live gaze-driven avatar should implement
/// this interface so the pipeline can hot-swap between them without knowing
/// the concrete type.
///
/// Gaze convention (matches <c>GazeVectorEngine</c> output):
///   Pitch &gt; 0 → looking up.
///   Yaw   &gt; 0 → looking right.
/// </summary>
public interface IAvatarComponent
{
    /// <summary>
    /// Drives the avatar to reflect the given gaze direction.
    /// </summary>
    /// <param name="pitchRad">Vertical gaze angle (radians).</param>
    /// <param name="yawRad">Horizontal gaze angle (radians).</param>
    /// <param name="userDistanceM">
    /// Distance of the user from the sensor (metres, Camera Space Z).
    /// Used for convergence / depth effects. Pass 1.5 if unknown.
    /// </param>
    void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f);

    /// <summary>Returns the avatar to its neutral / resting pose.</summary>
    void ResetGaze();
}
