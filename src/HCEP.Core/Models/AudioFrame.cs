// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Models;

/// <summary>
/// A single audio frame captured from the Kinect 4-microphone array.
/// 16-bit PCM, 16 kHz, mono beam-formed output.
/// </summary>
public sealed record AudioFrame
{
    /// <summary>Frame timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>PCM audio samples (16-bit signed, stored as byte[]).</summary>
    public required byte[] PcmData { get; init; }

    /// <summary>Number of valid bytes in <see cref="PcmData"/>.</summary>
    public int ByteCount { get; init; }

    /// <summary>Sample rate in Hz (16000 for Kinect).</summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>Bits per sample (16 for Kinect PCM).</summary>
    public int BitsPerSample { get; init; } = 16;

    /// <summary>Channel count (1 = mono beam-formed).</summary>
    public int Channels { get; init; } = 1;

    /// <summary>Beam angle in degrees [-50..+50].</summary>
    public double BeamAngleDeg { get; init; }

    /// <summary>Sound source angle in degrees [-50..+50].</summary>
    public double SourceAngleDeg { get; init; }

    /// <summary>Sound source confidence [0..1].</summary>
    public double SourceConfidence { get; init; }
}
