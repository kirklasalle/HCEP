// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Threading;

namespace HCEP.Core.Diagnostics;

/// <summary>
/// Async-flow correlation context used to propagate request/operation IDs
/// across chat, plugin API, telemetry, and LLM execution paths.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentCorrelation = new();

    /// <summary>Gets the current correlation ID for the active async flow.</summary>
    public static string? Current => CurrentCorrelation.Value;

    /// <summary>
    /// Creates a new correlation ID with a stable prefix.
    /// Example: "chat-0123abcd...".
    /// </summary>
    public static string Create(string prefix)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "hcep" : prefix.Trim().ToLowerInvariant();
        return $"{safePrefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Ensures a correlation ID exists for the current async flow and returns it.
    /// </summary>
    public static string Ensure(string prefix = "hcep")
    {
        if (!string.IsNullOrWhiteSpace(CurrentCorrelation.Value))
            return CurrentCorrelation.Value!;

        string id = Create(prefix);
        CurrentCorrelation.Value = id;
        return id;
    }

    /// <summary>
    /// Begins a scoped correlation context for the current async flow.
    /// Disposing the scope restores the previous correlation ID.
    /// </summary>
    public static IDisposable BeginScope(string correlationId)
    {
        var prior = CurrentCorrelation.Value;
        CurrentCorrelation.Value = string.IsNullOrWhiteSpace(correlationId) ? Create("hcep") : correlationId;
        return new RestoreScope(prior);
    }

    /// <summary>
    /// Stable numeric fingerprint suitable for low-cardinality telemetry gauges.
    /// Returns an unsigned 32-bit value as <see cref="double"/>.
    /// </summary>
    public static double ToNumericFingerprint(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId)) return 0d;

        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in correlationId)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private sealed class RestoreScope(string? prior) : IDisposable
    {
        public void Dispose()
        {
            CurrentCorrelation.Value = prior;
        }
    }
}
