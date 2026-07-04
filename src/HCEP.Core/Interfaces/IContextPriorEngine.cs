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
using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// Computes a <see cref="ContextPriorProfile"/> from the current
/// <see cref="ContextSnapshot"/>, translating time/space/situation signals
/// into prior-adjusted classification thresholds.
/// </summary>
public interface IContextPriorEngine
{
    /// <summary>
    /// Derives prior weights from <paramref name="context"/>.
    /// Implementations are expected to be cheap (no I/O) — called at ~10 Hz
    /// from the snapshot loop.
    /// </summary>
    ContextPriorProfile ComputePrior(ContextSnapshot context);
}
