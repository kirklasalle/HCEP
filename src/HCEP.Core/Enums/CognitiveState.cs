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
namespace HCEP.Core.Enums;

/// <summary>
/// Cognitive states inferred from gaze patterns, saccade dynamics, and
/// multi-modal sensor fusion. Maps to HCEP theory categories.
/// </summary>
public enum CognitiveState
{
    Unknown = 0,

    /// <summary>Focused attention on interlocutor.</summary>
    Engaged = 1,

    /// <summary>Active analytical processing — structured gaze.</summary>
    Processing = 2,

    /// <summary>Recalling from memory — characteristic gaze aversion pattern.</summary>
    Recalling = 3,

    /// <summary>Constructing / imagining — different aversion from recall.</summary>
    Constructing = 4,

    /// <summary>Emotional response — pupil changes, Social Triangle fixation.</summary>
    Emotional = 5,

    /// <summary>Disengaged — gaze off-face, reduced attention markers.</summary>
    Disengaged = 6,

    /// <summary>Deceptive indicators — asymmetric AU patterns + gaze mismatch.</summary>
    Guarded = 7,

    /// <summary>Deep listening — minimal saccades, sustained eye contact.</summary>
    Listening = 8,

    /// <summary>About to speak — pre-speech gaze shift pattern.</summary>
    PreSpeech = 9,

    /// <summary>Confusion or uncertainty — rapid scanning, furrowed brow.</summary>
    Confused = 10,

    /// <summary>Agreement / rapport — synchronized patterns.</summary>
    Aligned = 11,
}
