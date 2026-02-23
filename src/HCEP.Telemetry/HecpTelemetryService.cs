// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Diagnostics;
using HCEP.Core.Interfaces;

namespace HCEP.Telemetry;

/// <summary>
/// High-performance in-memory telemetry service with lock-free counters.
/// Thread-safe for concurrent pipeline stages.
/// </summary>
public sealed class HCEPTelemetryService : ITelemetryService
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, MovingAverage> _timings = new();

    /// <inheritdoc />
    public void Increment(string metric, long value = 1)
    {
        _counters.AddOrUpdate(metric, value, (_, existing) => existing + value);
    }

    /// <inheritdoc />
    public void RecordTiming(string metric, double ms)
    {
        var avg = _timings.GetOrAdd(metric, _ => new MovingAverage(128));
        avg.Add(ms);
        // Also expose as gauge for real-time display
        _gauges[metric + ".avg_ms"] = avg.Average;
    }

    /// <inheritdoc />
    public void RecordGauge(string metric, double value)
    {
        _gauges[metric] = value;
    }

    /// <inheritdoc />
    public IDisposable StartTimer(string metric)
    {
        return new TimingScope(this, metric);
    }

    /// <inheritdoc />
    public double GetGauge(string metric)
    {
        return _gauges.GetValueOrDefault(metric, 0.0);
    }

    /// <inheritdoc />
    public long GetCount(string metric)
    {
        return _counters.GetValueOrDefault(metric, 0L);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, double> Snapshot()
    {
        var snapshot = new Dictionary<string, double>();

        foreach (var kvp in _counters)
            snapshot[kvp.Key] = kvp.Value;

        foreach (var kvp in _gauges)
            snapshot[kvp.Key] = kvp.Value;

        foreach (var kvp in _timings)
        {
            snapshot[kvp.Key + ".avg_ms"] = kvp.Value.Average;
            snapshot[kvp.Key + ".min_ms"] = kvp.Value.Min;
            snapshot[kvp.Key + ".max_ms"] = kvp.Value.Max;
        }

        return snapshot;
    }

    // ── Timing Scope ───────────────────────────────────────────

    private sealed class TimingScope(HCEPTelemetryService service, string metric) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(_start);
            service.RecordTiming(metric, elapsed.TotalMilliseconds);
        }
    }
}
