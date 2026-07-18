// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
namespace HCEP.App;

/// <summary>
/// Shared logging cadence helpers for high-frequency UI and sensor paths.
/// </summary>
internal static class AppLog
{
    public static bool ShouldTraceFrame(long count)
        => count is > 0 and <= 5 || count % 300 == 0;
}