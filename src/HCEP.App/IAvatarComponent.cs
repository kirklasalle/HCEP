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
    /// Drives mouth animation from phoneme/viseme data during TTS speech (Phase 13).
    /// Wire to <c>ISpeechSynthesizer.VisemeChanged</c>. Pass <c>VisemeData.Silence</c>
    /// when speech ends to restore the neutral mouth shape.
    /// </summary>
    void SetViseme(HCEP.Speech.VisemeData viseme);

    /// <summary>Drives eyebrow animation from Kinect Action Units and HCEP mode.</summary>
    void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f);

    /// <summary>Returns the avatar to its neutral / resting pose.</summary>
    void ResetGaze();

    /// <summary>
    /// Triggers a single backchannel nod animation (~500 ms vertical head movement).
    /// Phase 10 — called by <c>BackchannelController</c> when sustained human speech
    /// is detected.  Safe to call from any thread; implementations dispatch internally.
    /// </summary>
    void TriggerNod();
}
