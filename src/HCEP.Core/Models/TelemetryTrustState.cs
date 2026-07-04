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
/// Immutable snapshot of the PAD-validation trust state for a running HCEP session.
/// Carried in outbound telemetry envelopes so downstream consumers can verify
/// that the data stream was produced under a valid ethical state.
/// </summary>
public sealed record TelemetryTrustState
{
    /// <summary>True when the Permanent Active Directives passed hash verification at boot.</summary>
    public bool IsValid { get; init; }

    /// <summary>Truncated SHA-256 of the verified PAD content (first 16 hex chars + "...").</summary>
    public string PadHash { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the session signing key was bootstrapped.</summary>
    public DateTimeOffset BootTimestamp { get; init; }

    /// <summary>First 4 bytes of the session key material, hex-encoded — for log correlation.</summary>
    public string SigningKeyId { get; init; } = string.Empty;

    /// <summary>Pre-built invalid sentinel returned when PAD verification fails.</summary>
    public static readonly TelemetryTrustState Invalid = new() { IsValid = false };
}
