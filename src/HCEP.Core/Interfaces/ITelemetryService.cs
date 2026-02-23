// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Interfaces;

/// <summary>
/// Structured telemetry / metrics recorder for performance monitoring.
/// </summary>
public interface ITelemetryService
{
    /// <summary>Records a named counter increment.</summary>
    void Increment(string metric, long value = 1);

    /// <summary>Records a timing measurement in milliseconds.</summary>
    void RecordTiming(string metric, double ms);

    /// <summary>Records a gauge value (e.g., FPS, queue depth).</summary>
    void RecordGauge(string metric, double value);

    /// <summary>Starts a timing scope — dispose to record elapsed time.</summary>
    IDisposable StartTimer(string metric);

    /// <summary>Gets the current value of a gauge metric.</summary>
    double GetGauge(string metric);

    /// <summary>Gets the total count of a counter metric.</summary>
    long GetCount(string metric);

    /// <summary>Dumps all metrics to a dictionary snapshot.</summary>
    IReadOnlyDictionary<string, double> Snapshot();
}
