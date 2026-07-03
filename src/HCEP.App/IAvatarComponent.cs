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
///
/// Brow convention (matches Kinect FaceTrackLib Action Unit values):
///   <paramref name="outerBrowRaise"/> AU5 (OuterBrowRaiser): positive = raised.
///   <paramref name="browLower"/>     AU3 (BrowLowerer): negative = furrowed.
///   <paramref name="hcepModeFurrow"/>: 0-1 autonomous furrow from HCEP mode (LOGIC/THINK).
/// </summary>
public interface IAvatarComponent
{
    /// <summary>Drives the avatar to reflect the given gaze direction.</summary>
    void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f);

    /// <summary>
    /// Drives eyebrow animation from Kinect Action Units and autonomous HCEP mode expression.
    /// Call every frame from <c>AvatarWindow.OnSnapshotReady</c>.
    /// </summary>
    /// <param name="outerBrowRaise">
    /// AU5 OuterBrowRaiser raw value [−1..+1]. Positive = brows raised (surprise, query, greeting).
    /// </param>
    /// <param name="browLower">
    /// AU3 BrowLowerer raw value [−1..+1]. Negative = furrowed (concentration, LOGIC/THINK modes).
    /// </param>
    /// <param name="hcepModeFurrow">
    /// Autonomous furrow target [0..1] derived from the current HCEP mode classification.
    /// Blended with AU values so the avatar expresses the appropriate brow posture
    /// even when the Kinect cannot resolve precise AU magnitudes.
    /// </param>
    void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f);

    /// <summary>Returns the avatar to its neutral / resting pose.</summary>
    void ResetGaze();
}
