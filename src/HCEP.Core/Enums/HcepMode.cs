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
/// The five HCEP cognitive-emotional modes derived from gaze behavior analysis.
/// Each mode represents a distinct mental state inferred from eye-contact patterns,
/// saccade dynamics, and dwell-time distributions.
/// </summary>
public enum HcepMode
{
    /// <summary>Unknown or insufficient data to classify.</summary>
    Unknown = 0,

    /// <summary>
    /// LOGIC_MODE — Analytical processing. Characterized by structured gaze patterns,
    /// frequent aversion to upper-left (right-hemisphere access), and measured saccades.
    /// </summary>
    Logic = 1,

    /// <summary>
    /// AFFECT_MODE — Emotional engagement. Characterized by sustained eye contact,
    /// pupil dilation, and Social Triangle gaze patterns (eyes → mouth cycling).
    /// </summary>
    Affect = 2,

    /// <summary>
    /// SPIRIT_MODE — Deep rapport / authentic connection. Characterized by prolonged
    /// mutual gaze, synchronized blink patterns, and minimal aversion.
    /// </summary>
    Spirit = 3,

    /// <summary>
    /// HEART_MODE — Empathic resonance. Characterized by soft-focus gaze,
    /// lower-face attention (mouth/chin), and slower saccade velocity.
    /// </summary>
    Heart = 4,

    /// <summary>
    /// THINK_MODE — Internal cognitive processing. Characterized by gaze aversion
    /// (defocused or off-face), reduced blink rate, and constructive saccade patterns.
    /// </summary>
    Think = 5,
}
