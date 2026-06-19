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
/// Describes a biomechanically accurate saccade for avatar eye animation.
/// Uses Main Sequence equation parameters from HCEP research.
/// </summary>
public sealed record SaccadeDescriptor
{
    /// <summary>Saccade start gaze direction.</summary>
    public required Vector3 FromDirection { get; init; }

    /// <summary>Saccade target gaze direction.</summary>
    public required Vector3 ToDirection { get; init; }

    /// <summary>Amplitude in degrees.</summary>
    public float AmplitudeDeg { get; init; }

    /// <summary>Peak velocity in degrees/second (Main Sequence).</summary>
    public float PeakVelocityDegPerSec { get; init; }

    /// <summary>Total saccade duration in milliseconds.</summary>
    public float DurationMs { get; init; }

    /// <summary>Current phase of the saccade.</summary>
    public SaccadePhase Phase { get; init; } = SaccadePhase.Fixation;

    /// <summary>Elapsed time in current saccade (ms).</summary>
    public float ElapsedMs { get; init; }

    /// <summary>Progress through the saccade [0..1].</summary>
    public float Progress => DurationMs > 0 ? Math.Clamp(ElapsedMs / DurationMs, 0f, 1f) : 0f;

    /// <summary>
    /// Computes saccade duration using Main Sequence equation:
    /// Duration = 2.2 * Amplitude + 21 ms.
    /// </summary>
    public static float ComputeDurationMs(float amplitudeDeg) =>
        Anthropometrics.SaccadeDurationMsPerDeg * amplitudeDeg + 21.0f;

    /// <summary>
    /// Computes peak velocity using Main Sequence:
    /// PeakVelocity = 500 * (1 - e^(-Amplitude/15)).
    /// </summary>
    public static float ComputePeakVelocity(float amplitudeDeg) =>
        Anthropometrics.SaccadeVelocityCoeff * (1.0f - MathF.Exp(-amplitudeDeg / 15.0f));
}
