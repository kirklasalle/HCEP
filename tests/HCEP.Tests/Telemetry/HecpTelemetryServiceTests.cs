// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Interfaces;
using HCEP.Telemetry;

namespace HCEP.Tests.Telemetry;

public sealed class HecpTelemetryServiceTests
{
    private readonly HCEPTelemetryService _sut = new();

    // ── Increment ──────────────────────────────────────────────

    [Fact]
    public void Increment_SingleCall_RecordsValue()
    {
        _sut.Increment("frames");
        Assert.Equal(1L, _sut.GetCount("frames"));
    }

    [Fact]
    public void Increment_MultipleCalls_Accumulates()
    {
        _sut.Increment("frames", 5);
        _sut.Increment("frames", 3);
        Assert.Equal(8L, _sut.GetCount("frames"));
    }

    [Fact]
    public void GetCount_UnknownMetric_ReturnsZero()
    {
        Assert.Equal(0L, _sut.GetCount("nonexistent"));
    }

    // ── Gauges ─────────────────────────────────────────────────

    [Fact]
    public void RecordGauge_StoresLatestValue()
    {
        _sut.RecordGauge("fps", 30.0);
        _sut.RecordGauge("fps", 60.0);
        Assert.Equal(60.0, _sut.GetGauge("fps"));
    }

    [Fact]
    public void GetGauge_UnknownMetric_ReturnsZero()
    {
        Assert.Equal(0.0, _sut.GetGauge("unknown"));
    }

    // ── Timings ────────────────────────────────────────────────

    [Fact]
    public void RecordTiming_ExposesAvgGauge()
    {
        _sut.RecordTiming("pipeline", 10.0);
        _sut.RecordTiming("pipeline", 20.0);

        double avg = _sut.GetGauge("pipeline.avg_ms");
        Assert.Equal(15.0, avg, precision: 2);
    }

    // ── StartTimer ─────────────────────────────────────────────

    [Fact]
    public void StartTimer_DisposableRecordsTiming()
    {
        using (_sut.StartTimer("op"))
        {
            // Simulate some work
            Thread.SpinWait(1000);
        }

        double avg = _sut.GetGauge("op.avg_ms");
        Assert.True(avg >= 0.0, "Timer should record a non-negative duration");
    }

    // ── Snapshot ───────────────────────────────────────────────

    [Fact]
    public void Snapshot_ContainsAllMetricTypes()
    {
        _sut.Increment("count.total", 42);
        _sut.RecordGauge("gauge.fps", 29.97);
        _sut.RecordTiming("timing.frame", 16.6);

        var snapshot = _sut.Snapshot();

        Assert.Equal(42.0, snapshot["count.total"]);
        Assert.Equal(29.97, snapshot["gauge.fps"], precision: 2);
        Assert.True(snapshot.ContainsKey("timing.frame.avg_ms"));
        Assert.True(snapshot.ContainsKey("timing.frame.min_ms"));
        Assert.True(snapshot.ContainsKey("timing.frame.max_ms"));
    }

    [Fact]
    public void Snapshot_EmptyService_ReturnsEmptyDictionary()
    {
        var snapshot = _sut.Snapshot();
        Assert.Empty(snapshot);
    }

    // ── ITelemetryService interface compliance ─────────────────

    [Fact]
    public void ImplementsInterface()
    {
        Assert.IsAssignableFrom<ITelemetryService>(_sut);
    }
}
