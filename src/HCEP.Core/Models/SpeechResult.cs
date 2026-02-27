// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Models;

/// <summary>
/// Result from the Whisper.net speech-to-text pipeline.
/// </summary>
public sealed record SpeechResult
{
    /// <summary>Transcribed text.</summary>
    public required string Text { get; init; }

    /// <summary>ISO 639-1 language code (e.g., "en").</summary>
    public string Language { get; init; } = "en";

    /// <summary>Transcription confidence [0..1].</summary>
    public float Confidence { get; init; }

    /// <summary>Audio segment start time.</summary>
    public TimeSpan Start { get; init; }

    /// <summary>Audio segment end time.</summary>
    public TimeSpan End { get; init; }

    /// <summary>Whether this is a final (non-partial) result.</summary>
    public bool IsFinal { get; init; }

    /// <summary>Speaker tracking ID (if diarization is active).</summary>
    public int? SpeakerId { get; init; }

    /// <summary>Beam angle of the audio source at capture time.</summary>
    public double SourceAngleDeg { get; init; }

    /// <summary>Timestamp when the audio was captured.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
