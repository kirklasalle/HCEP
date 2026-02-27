// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// A single HCEP reading — the fused analysis output for one frame.
/// Immutable record for safe channel propagation.
/// </summary>
/// <param name="Timestamp">UTC timestamp of the source frame.</param>
/// <param name="Mode">HCEP cognitive-emotional mode classification.</param>
/// <param name="Region">Classified gaze target region.</param>
/// <param name="Cognitive">Inferred cognitive state.</param>
/// <param name="Valence">Emotional valence.</param>
/// <param name="Confidence">Overall classification confidence [0..1].</param>
/// <param name="GazeOrigin">3D gaze ray origin (cyclopean eye, camera space).</param>
/// <param name="GazeDirection">Normalized 3D gaze direction vector.</param>
/// <param name="HeadPose">Head rotation (pitch, yaw, roll) in degrees.</param>
/// <param name="PersonId">Tracked person identifier (across frames).</param>
public sealed record HcepReading(
    DateTimeOffset Timestamp,
    HcepMode Mode,
    GazeRegion Region,
    CognitiveState Cognitive,
    EmotionalValence Valence,
    float Confidence,
    Vector3 GazeOrigin,
    Vector3 GazeDirection,
    Vector3 HeadPose,
    int PersonId)
{
    /// <summary>Empty/default reading.</summary>
    public static readonly HcepReading Empty = new(
        DateTimeOffset.MinValue,
        HcepMode.Unknown,
        GazeRegion.Unknown,
        CognitiveState.Unknown,
        EmotionalValence.Unknown,
        0f,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        -1);
}
