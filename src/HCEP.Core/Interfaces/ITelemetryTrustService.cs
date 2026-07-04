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
/// Signs outbound HCEP telemetry payloads and exposes the PAD-validation
/// trust state for the current session.
///
/// Implementations bootstrap a session signing key only when the Permanent
/// Active Directives pass hash verification. If the PAD is tampered with the
/// service remains in an invalid state and <see cref="SignPayload"/> returns
/// null, causing downstream consumers to receive unsigned (untrusted) payloads.
/// </summary>
public interface ITelemetryTrustService
{
    /// <summary>Current PAD-validation trust state for this session.</summary>
    TelemetryTrustState State { get; }

    /// <summary>
    /// Signs <paramref name="json"/> with the session HMAC key.
    /// Returns null when the trust state is invalid (PAD tampered or verification
    /// failed at boot) — callers must treat a null signature as an untrusted payload.
    /// </summary>
    string? SignPayload(string json);
}
