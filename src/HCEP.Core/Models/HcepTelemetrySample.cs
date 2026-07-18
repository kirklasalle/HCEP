// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Numerics;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// Compact rolling sample used to summarize the recent telemetry window for
/// chat grounding. One sample is captured per pipeline snapshot and later
/// collapsed into a bounded prompt summary so the LLM can reason over trends
/// without being flooded by full-frame raw telemetry.
/// </summary>
public sealed record HcepTelemetrySample
{
    public required DateTimeOffset Timestamp { get; init; }
    public int TrackedPersons { get; init; }
    public string? PrimaryIdentity { get; init; }
    public HcepMode Mode { get; init; } = HcepMode.Unknown;
    public GazeRegion Region { get; init; } = GazeRegion.Unknown;
    public CognitiveState Cognitive { get; init; } = CognitiveState.Unknown;
    public EmotionalValence Valence { get; init; } = EmotionalValence.Unknown;
    public float Confidence { get; init; }
    public float? DistanceM { get; init; }
    public Vector3? HeadRotationDeg { get; init; }
    public string? LatestSpeech { get; init; }
}