// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Telemetry;

namespace HCEP.Tests.Telemetry;

public sealed class FpsCounterTests
{
    [Fact]
    public void NewCounter_FpsIsZero()
    {
        var counter = new FpsCounter();
        Assert.Equal(0.0, counter.Fps);
    }

    [Fact]
    public void NewCounter_FrameTimeMsIsZero()
    {
        var counter = new FpsCounter();
        Assert.Equal(0.0, counter.FrameTimeMs);
    }

    [Fact]
    public void Tick_MultipleCalls_ProducesPositiveFps()
    {
        var counter = new FpsCounter(windowSize: 10);

        // Simulate several ticks with a tiny delay
        for (int i = 0; i < 10; i++)
        {
            Thread.SpinWait(10_000);
            counter.Tick();
        }

        Assert.True(counter.Fps > 0, "FPS should be positive after ticking");
        Assert.True(counter.FrameTimeMs > 0, "FrameTimeMs should be positive");
    }

    [Fact]
    public void Tick_WindowWrapsAround()
    {
        var counter = new FpsCounter(windowSize: 3);

        // Tick more times than the window size to exercise wrap-around
        for (int i = 0; i < 10; i++)
        {
            Thread.SpinWait(5_000);
            counter.Tick();
        }

        double fps = counter.Fps;
        Assert.True(fps > 0, "FPS should remain positive after wrap-around");
    }

    [Fact]
    public void CustomWindowSize_Respected()
    {
        var counter = new FpsCounter(windowSize: 5);

        for (int i = 0; i < 5; i++)
        {
            Thread.SpinWait(5_000);
            counter.Tick();
        }

        // After exactly windowSize ticks, the counter should be fully populated
        Assert.True(counter.Fps > 0);
    }
}
