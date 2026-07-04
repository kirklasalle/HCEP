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
/// Rolling estimate of the human speaker's speech cadence, derived from
/// Whisper.net segment timing and transcript length.
/// Consumed by <see cref="HCEP.App.BackchannelController"/> to schedule
/// biologically plausible nod timing.
///
/// Scientific basis: Condon &amp; Ogston (1967) interactional synchrony;
/// VAD prosodic rhythm as a backchannel scheduling signal.
/// </summary>
public sealed class SpeechCadenceProfile
{
    /// <summary>
    /// Estimated syllables per second based on the last final speech segment.
    /// Normal conversational range: 3–6 syll/s.
    /// Default 4.0 — approximately normal English cadence.
    /// </summary>
    public float SyllablesPerSecond { get; set; } = 4f;

    /// <summary>Average pause duration between utterances (ms), rolling estimate.</summary>
    public float AveragePauseDurationMs { get; set; } = 500f;

    /// <summary>Duration of the last completed speech burst (ms).</summary>
    public float LastSpeechBurstMs { get; set; }

    /// <summary>UTC timestamp of the most recent cadence update.</summary>
    public DateTimeOffset LastUpdate { get; set; }

    /// <summary>
    /// True when cadence data is fresh enough to drive backchannel scheduling
    /// (within the last 10 seconds).
    /// </summary>
    public bool IsFresh =>
        LastUpdate != DateTimeOffset.MinValue
        && (DateTimeOffset.UtcNow - LastUpdate).TotalSeconds < 10;

    /// <summary>Neutral default profile — normal English speech rate.</summary>
    public static readonly SpeechCadenceProfile Default = new();
}
