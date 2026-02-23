// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Diagnostics;

namespace HCEP.Telemetry;

/// <summary>
/// High-resolution frame-rate counter using <see cref="Stopwatch"/>.
/// Reports FPS as a rolling average over a configurable window.
/// </summary>
public sealed class FpsCounter
{
    private readonly double[] _frameTimes;
    private readonly int _windowSize;
    private int _index;
    private int _count;
    private long _lastTimestamp;

    public FpsCounter(int windowSize = 60)
    {
        _windowSize = windowSize;
        _frameTimes = new double[windowSize];
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Call once per frame to record timing.
    /// </summary>
    public void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;

        _frameTimes[_index] = elapsed.TotalSeconds;
        _index = (_index + 1) % _windowSize;
        if (_count < _windowSize) _count++;
    }

    /// <summary>
    /// Current frames per second (rolling average).
    /// </summary>
    public double Fps
    {
        get
        {
            if (_count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < _count; i++)
                sum += _frameTimes[i];
            return _count / sum;
        }
    }

    /// <summary>
    /// Average frame time in milliseconds.
    /// </summary>
    public double FrameTimeMs
    {
        get
        {
            if (_count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < _count; i++)
                sum += _frameTimes[i];
            return (sum / _count) * 1000.0;
        }
    }
}
