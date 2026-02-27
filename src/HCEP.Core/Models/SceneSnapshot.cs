// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.Immutable;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// A complete snapshot of all sensor data and analysis for a single time-step.
/// Immutable for safe propagation through System.Threading.Channels pipelines.
/// </summary>
public sealed record SceneSnapshot
{
    /// <summary>Frame timestamp (UTC).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Monotonic frame sequence number.</summary>
    public long FrameNumber { get; init; }

    /// <summary>All currently tracked persons.</summary>
    public ImmutableArray<TrackedPerson> Persons { get; init; } =
        ImmutableArray<TrackedPerson>.Empty;

    /// <summary>Primary (closest / most-engaged) person index, or -1.</summary>
    public int PrimaryPersonIndex { get; init; } = -1;

    /// <summary>Latest color frame (nullable for performance — not every frame needs color).</summary>
    public ColorFrame? Color { get; init; }

    /// <summary>Latest depth frame.</summary>
    public DepthFrame? Depth { get; init; }

    /// <summary>Current audio frame.</summary>
    public AudioFrame? Audio { get; init; }

    /// <summary>Latest speech transcription result (null if no recent speech).</summary>
    public SpeechResult? LatestSpeech { get; init; }

    /// <summary>Active sensor streams bitmask.</summary>
    public SensorStreamType ActiveStreams { get; init; }

    /// <summary>Pipeline processing latency from sensor capture to snapshot creation.</summary>
    public TimeSpan PipelineLatency { get; init; }

    /// <summary>Convenience accessor for primary tracked person.</summary>
    public TrackedPerson? PrimaryPerson =>
        PrimaryPersonIndex >= 0 && PrimaryPersonIndex < Persons.Length
            ? Persons[PrimaryPersonIndex]
            : null;
}
