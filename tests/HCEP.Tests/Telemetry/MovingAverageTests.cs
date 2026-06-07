// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Telemetry;

namespace HCEP.Tests.Telemetry;

public sealed class MovingAverageTests
{
    [Fact]
    public void Empty_AverageIsZero()
    {
        var ma = new MovingAverage(10);
        Assert.Equal(0.0, ma.Average);
    }

    [Fact]
    public void Empty_MinIsZero()
    {
        var ma = new MovingAverage(10);
        Assert.Equal(0.0, ma.Min);
    }

    [Fact]
    public void Empty_MaxIsZero()
    {
        var ma = new MovingAverage(10);
        Assert.Equal(0.0, ma.Max);
    }

    [Fact]
    public void SingleValue_AverageEqualsValue()
    {
        var ma = new MovingAverage(10);
        ma.Add(42.0);
        Assert.Equal(42.0, ma.Average, precision: 10);
    }

    [Fact]
    public void MultipleValues_AverageIsCorrect()
    {
        var ma = new MovingAverage(10);
        ma.Add(10.0);
        ma.Add(20.0);
        ma.Add(30.0);
        Assert.Equal(20.0, ma.Average, precision: 10);
    }

    [Fact]
    public void MinMax_TrackedCorrectly()
    {
        var ma = new MovingAverage(10);
        ma.Add(5.0);
        ma.Add(15.0);
        ma.Add(10.0);
        Assert.Equal(5.0, ma.Min, precision: 10);
        Assert.Equal(15.0, ma.Max, precision: 10);
    }

    [Fact]
    public void WindowOverflow_OldValuesDropped()
    {
        var ma = new MovingAverage(3);
        ma.Add(10.0); // [10, _, _]
        ma.Add(20.0); // [10, 20, _]
        ma.Add(30.0); // [10, 20, 30]  avg = 20
        ma.Add(40.0); // [40, 20, 30]  avg = 30  (10 dropped from sum)

        Assert.Equal(30.0, ma.Average, precision: 10);
    }

    [Fact]
    public void WindowOverflow_SumRemainsAccurate()
    {
        var ma = new MovingAverage(2);
        ma.Add(100.0);
        ma.Add(200.0);
        ma.Add(300.0); // drops 100

        // Average of [200, 300] = 250
        Assert.Equal(250.0, ma.Average, precision: 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void VariousWindowSizes_ProduceValidAverage(int windowSize)
    {
        var ma = new MovingAverage(windowSize);
        double expectedSum = 0;
        int expectedCount = 0;

        for (int i = 1; i <= windowSize * 2; i++)
        {
            ma.Add(i);
            if (i > windowSize)
            {
                expectedSum += i;
                expectedSum -= (i - windowSize);
            }
            else
            {
                expectedSum += i;
                expectedCount = i;
            }
        }

        expectedCount = windowSize;
        double expectedAvg = expectedSum / expectedCount;
        Assert.Equal(expectedAvg, ma.Average, precision: 6);
    }

    [Fact]
    public void ThreadSafety_ConcurrentAdds_DoNotCorrupt()
    {
        var ma = new MovingAverage(100);
        int iterations = 1000;

        Parallel.For(0, iterations, i =>
        {
            ma.Add(i);
        });

        // Just verify it doesn't throw and produces a reasonable value
        double avg = ma.Average;
        Assert.True(avg >= 0, "Average should be non-negative");
        Assert.True(!double.IsNaN(avg), "Average should not be NaN");
        Assert.True(!double.IsInfinity(avg), "Average should not be Infinity");
    }
}
