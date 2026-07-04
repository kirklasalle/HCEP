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
/// Context-derived prior that adjusts HCEP classification thresholds before
/// final mode arbitration. Computed by <see cref="HCEP.Core.Interfaces.IContextPriorEngine"/>
/// from the current <see cref="ContextSnapshot"/>.
///
/// When <see cref="ShadowModeOnly"/> is true the prior is logged for comparison
/// but never applied — enabling A/B telemetry without risk. Set
/// <see cref="ShadowModeOnly"/> to false once the prior has been validated.
///
/// Scientific basis: Bayesian priors on detection thresholds derived from
/// chronemic context (Hall 1959, 1983), behavior settings (Barker 1968), and
/// circadian modulation of communication register (Aschoff 1965).
/// </summary>
public sealed record ContextPriorProfile
{
    /// <summary>
    /// Additive confidence boost for Think-mode classification [0..0.5].
    /// Raises the effective confidence of a Think candidate so fewer sensor frames
    /// are needed to confirm it in quiet, reflective environments.
    /// </summary>
    public float ThinkModePriorBoost { get; init; }

    /// <summary>
    /// Additive confidence boost for Heart-mode classification [0..0.5].
    /// Applied in empathic / intimate contexts such as a bedroom at night.
    /// </summary>
    public float HeartModePriorBoost { get; init; }

    /// <summary>
    /// Proportional bias toward silence behavior [0..1].
    /// Used by <see cref="HCEP.Intelligence.SilenceProtocolEvaluator"/> as an
    /// additional weight — the higher this value the lower the speech-initiation
    /// threshold needs to be exceeded before silence is maintained.
    /// </summary>
    public float SilenceBias { get; init; }

    /// <summary>
    /// Scale factor applied to the base temporal hysteresis window (5 frames at
    /// 30 fps ≈ 167 ms). Range [1..3]. Evening/night contexts use ≈1.4 (≈233 ms),
    /// giving the classifier more time to confirm mode transitions so natural
    /// slowdowns in communication rhythm are not penalized.
    /// </summary>
    public float HysteresisMultiplier { get; init; } = 1f;

    /// <summary>
    /// Context-adjusted minimum confidence required for a mode transition [0.2..0.6].
    /// Focused environments (laboratory, studio) lower this slightly so the
    /// classifier is more responsive to genuine internal states.
    /// </summary>
    public float ModeTransitionMinConfidence { get; init; } = 0.4f;

    /// <summary>
    /// Feature flag: when true the prior is computed and logged but NOT applied to
    /// live classification. Enables shadow-mode A/B comparison (backlog item A6).
    /// </summary>
    public bool ShadowModeOnly { get; init; }

    /// <summary>Neutral baseline — no contextual adjustment applied.</summary>
    public static readonly ContextPriorProfile Neutral = new();
}
