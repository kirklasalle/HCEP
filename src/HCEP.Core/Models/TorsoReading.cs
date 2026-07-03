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
namespace HCEP.Core.Models;

/// <summary>
/// Proxemic zone classification per Hall (1966) <em>The Hidden Dimension</em>.
/// Kinect v1 provides skeleton Z positions in metres (positive = away from sensor).
/// </summary>
public enum ProxemicZone
{
    /// <summary>Distance not available.</summary>
    Unknown,
    /// <summary>&lt; 0.45 m — intimate space.</summary>
    Intimate,
    /// <summary>0.45 – 1.20 m — personal space.</summary>
    Personal,
    /// <summary>1.20 – 3.70 m — social space.</summary>
    Social,
    /// <summary>&gt; 3.70 m — public space.</summary>
    Public,
}

/// <summary>
/// Phase 9 — Torso analysis output for a single skeleton frame.
/// Computed by <c>HCEP.Kinect.TorsoAnalyzer</c> from Kinect v1 20-joint skeleton.
///
/// Scientific basis: Mehrabian (1972), Argyle (1988), Montepare &amp; Zebrowitz (1993).
/// See <c>HCEP_SCIENCE_FOUNDATION.md</c> §Part II-III.
/// </summary>
public sealed record TorsoReading
{
    /// <summary><c>true</c> when enough skeleton joints are tracked for analysis.</summary>
    public bool IsTracked { get; init; }

    /// <summary>
    /// Shoulder elevation difference in metres: ShoulderLeft.Y − ShoulderRight.Y.
    /// Positive = left shoulder higher; negative = right shoulder higher.
    /// A bilateral shrug elevates BOTH shoulders.
    /// </summary>
    public float ShoulderElevationDiff { get; init; }

    /// <summary>
    /// <c>true</c> when both shoulders are elevated ≥ 0.03 m above the neutral
    /// shoulder-to-hip mid-line (bilateral shrug; uncertainty / discomfort marker).
    /// </summary>
    public bool BilateralShrug { get; init; }

    /// <summary>
    /// Torso forward-lean angle in degrees.
    /// Positive = leaning toward the sensor.  Computed from the angle between the
    /// ShoulderCenter → HipCenter vector and the vertical (world-Y) axis.
    /// </summary>
    public float LeanAngleDeg { get; init; }

    /// <summary><c>true</c> when <see cref="LeanAngleDeg"/> ≥ 8°.</summary>
    public bool ForwardLean { get; init; }

    /// <summary>
    /// Torso horizontal rotation (yaw) in degrees relative to the camera axis.
    /// Positive = turned left (from sensor's perspective).
    /// Computed from the angle of the shoulder vector in the XZ plane.
    /// </summary>
    public float TorsoYawDeg { get; init; }

    /// <summary>
    /// Proxemic zone classification for the current skeleton distance.
    /// </summary>
    public ProxemicZone ProxemicZone { get; init; }

    /// <summary>Singleton representing a non-tracked / unavailable frame.</summary>
    public static readonly TorsoReading Unavailable = new() { IsTracked = false };
}
