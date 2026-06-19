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
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// 3-stage gaze estimate combining head pose, eye position, and
/// a confidence cone for target classification.
/// </summary>
public sealed record GazeEstimate
{
    /// <summary>Head pose contribution to gaze direction (Stage 1).</summary>
    public required Vector3 HeadGazeDirection { get; init; }

    /// <summary>Eye-in-head rotation offset (Stage 2).</summary>
    public required Vector3 EyeOffset { get; init; }

    /// <summary>Fused hybrid gaze direction (Stage 3: head + eye blend).</summary>
    public required Vector3 HybridDirection { get; init; }

    /// <summary>Gaze ray origin in camera coordinate space.</summary>
    public required Vector3 Origin { get; init; }

    /// <summary>3D intersection point on the target plane.</summary>
    public Vector3 IntersectionPoint { get; init; }

    /// <summary>Confidence cone half-angle in degrees.</summary>
    public float ConeHalfAngleDeg { get; init; }

    /// <summary>Classified face region within the confidence cone.</summary>
    public GazeRegion ClassifiedRegion { get; init; } = GazeRegion.Unknown;

    /// <summary>Estimation confidence [0..1].</summary>
    public float Confidence { get; init; }

    /// <summary>Timestamp of the source frame.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
