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
using System.Security.Cryptography;
using System.Text;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;

namespace HCEP.Intelligence;

/// <summary>
/// Bootstraps a per-session HMAC-SHA256 signing key from the verified Permanent
/// Active Directives. Signs outbound HCEP telemetry payloads so downstream
/// plugin consumers can verify that the data stream was produced under a valid
/// ethical state.
///
/// Key derivation: SHA256(PAD_text) XOR random_32_bytes — unique per boot,
/// bound to the PAD content, not exportable.
///
/// Fail-closed: if PAD verification fails at construction, <see cref="State"/>
/// is invalid and <see cref="SignPayload"/> returns null permanently.
/// </summary>
public sealed class TelemetryTrustService : ITelemetryTrustService, IDisposable
{
    private readonly HMACSHA256? _hmac;
    private readonly TelemetryTrustState _state;

    /// <inheritdoc />
    public TelemetryTrustState State => _state;

    public TelemetryTrustService()
    {
        bool padValid = ActiveDirectivesManager.TryVerifyDirectives(out string directives);

        if (padValid)
        {
            // Derive signing key: SHA256(PAD) XOR fresh random bytes
            // This binds the key to the PAD content without hard-coding it.
            byte[] padHash = SHA256.HashData(Encoding.UTF8.GetBytes(directives));
            byte[] sessionBytes = new byte[32];
            RandomNumberGenerator.Fill(sessionBytes);
            byte[] keyMaterial = new byte[32];
            for (int i = 0; i < 32; i++)
                keyMaterial[i] = (byte)(sessionBytes[i] ^ padHash[i]);

            _hmac = new HMACSHA256(keyMaterial);
            _state = new TelemetryTrustState
            {
                IsValid = true,
                PadHash = Convert.ToHexString(padHash)[..16] + "...",
                BootTimestamp = DateTimeOffset.UtcNow,
                SigningKeyId = Convert.ToHexString(sessionBytes[..4]),
            };

            // Zero key material after use
            Array.Clear(keyMaterial);
            Array.Clear(sessionBytes);
        }
        else
        {
            _state = TelemetryTrustState.Invalid with { BootTimestamp = DateTimeOffset.UtcNow };
        }
    }

    /// <inheritdoc />
    public string? SignPayload(string json)
    {
        if (_hmac is null) return null;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(_hmac.ComputeHash(bytes));
    }

    public void Dispose() => _hmac?.Dispose();
}
