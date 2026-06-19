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
using System.Numerics;

namespace HCEP.Core.Models;

/// <summary>
/// Anthropometric constants for gaze geometry estimation.
/// Values from peer-reviewed population studies; used in SolvePnP and
/// ray-plane intersection calculations.
/// </summary>
public static class Anthropometrics
{
    // ── Interpupillary Distance ────────────────────────────────
    /// <summary>Mean adult interpupillary distance in millimeters.</summary>
    public const float MeanIpdMm = 63.0f;

    /// <summary>Standard deviation of IPD in millimeters.</summary>
    public const float IpdStdDevMm = 3.5f;

    // ── Facial Proportions ─────────────────────────────────────
    /// <summary>Sellion-to-menton distance in millimeters (face height proxy).</summary>
    public const float SellionMentonMm = 118.0f;

    /// <summary>Depth from cornea to orbital rear wall in millimeters.</summary>
    public const float EyeDepthMm = 12.0f;

    /// <summary>Average corneal radius in millimeters.</summary>
    public const float CornealRadiusMm = 7.8f;

    // ── Confidence Cone ────────────────────────────────────────
    /// <summary>Default confidence cone radius at the gaze target plane, in centimeters.</summary>
    public const float DefaultConeRadiusCm = 5.0f;

    /// <summary>Default dwell-time threshold for fixation detection, in milliseconds.</summary>
    public const int DefaultDwellTimeMs = 200;

    // ── Main Sequence Saccade ──────────────────────────────────
    /// <summary>Peak saccade velocity coefficient (degrees/sec per degree amplitude).</summary>
    public const float SaccadeVelocityCoeff = 500.0f;

    /// <summary>Saccade duration constant (ms per degree of amplitude).</summary>
    public const float SaccadeDurationMsPerDeg = 2.2f;

    /// <summary>Minimum saccade amplitude threshold in degrees.</summary>
    public const float MinSaccadeAmplitudeDeg = 0.5f;

    // ── 3D Face Model Landmarks (canonical, in mm) ─────────────
    /// <summary>
    /// Canonical 3D face model points (nose tip, chin, left/right eye corners,
    /// left/right mouth corners) used for SolvePnP head pose estimation.
    /// Coordinates in mm, origin at nose tip.
    /// </summary>
    public static readonly Vector3[] CanonicalFaceModel =
    [
        new( 0.0f,    0.0f,    0.0f),     // Nose tip
        new( 0.0f,  -63.6f,  -12.5f),     // Chin
        new(-43.3f,  32.7f,  -26.0f),     // Left eye left corner
        new( 43.3f,  32.7f,  -26.0f),     // Right eye right corner
        new(-28.9f, -28.9f,  -24.1f),     // Left mouth corner
        new( 28.9f, -28.9f,  -24.1f),     // Right mouth corner
    ];
}
