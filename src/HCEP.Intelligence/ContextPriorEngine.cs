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
using HCEP.Core.Interfaces;
using HCEP.Core.Models;

namespace HCEP.Intelligence;

/// <summary>
/// Translates a <see cref="ContextSnapshot"/> into a <see cref="ContextPriorProfile"/>
/// that adjusts HCEP mode classification thresholds before final arbitration.
///
/// Scientific basis:
/// - Chronemics (Hall 1959, 1983): time-of-day modulates communication register.
/// - Behavior settings (Barker 1968): environment constrains behavioral norms.
/// - Affiliative silence (Jaworski 1993): intimate contexts favour presence over speech.
/// - Circadian rhythms (Aschoff 1965): evening slows communication cadence.
/// </summary>
public sealed class ContextPriorEngine : IContextPriorEngine
{
    /// <summary>
    /// When true all priors are computed and logged but NOT applied to live
    /// classification — shadow-mode for A/B comparison. Feature flag A6.
    /// Set to false to enable full contextual prior inference.
    /// </summary>
    public bool ShadowMode { get; set; } = false;

    /// <inheritdoc />
    public ContextPriorProfile ComputePrior(ContextSnapshot context)
    {
        float silenceBias = 0f;
        float hysteresisMultiplier = 1f;
        float thinkBoost = 0f;
        float heartBoost = 0f;
        float minConfidence = 0.4f;

        // ── Time-of-day: evening / night → slower rhythms, lower urgency ────
        if (context.TimeOfDay is TimeOfDayCategory.Evening or TimeOfDayCategory.Night)
        {
            hysteresisMultiplier = 1.4f;   // ~233 ms stability window at 30 fps
            silenceBias += 0.15f;
        }

        // ── Bedroom at night: affiliative silence (Jaworski, 1993) ──────────
        if (context.Environment == EnvironmentType.Bedroom
            && context.TimeOfDay is TimeOfDayCategory.Night)
        {
            silenceBias += 0.25f;
            heartBoost = 0.12f;
        }

        // ── Focus environments: library / studio / lab → reflective modes ───
        if (context.Environment is EnvironmentType.Laboratory or EnvironmentType.Studio)
        {
            thinkBoost = 0.18f;
            heartBoost = 0.08f;
            minConfidence = 0.35f;  // less evidence needed in quiet, structured context
        }

        // ── Private interaction → lower threshold for introspective modes ───
        if (context.Privacy == SituationPrivacy.Private)
        {
            thinkBoost += 0.05f;
            heartBoost += 0.05f;
        }

        // ── Silence protocol already active → amplify silence bias ──────────
        if (context.SilenceProtocolActive)
            silenceBias = Math.Min(silenceBias + 0.30f, 1f);

        return new ContextPriorProfile
        {
            ThinkModePriorBoost = Math.Clamp(thinkBoost, 0f, 0.5f),
            HeartModePriorBoost = Math.Clamp(heartBoost, 0f, 0.5f),
            SilenceBias = Math.Clamp(silenceBias, 0f, 1f),
            HysteresisMultiplier = Math.Clamp(hysteresisMultiplier, 1f, 3f),
            ModeTransitionMinConfidence = Math.Clamp(minConfidence, 0.2f, 0.6f),
            ShadowModeOnly = ShadowMode,
        };
    }
}
