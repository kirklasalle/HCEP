// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Telemetry;

/// <summary>
/// Lock-free circular buffer moving average for real-time metric computation.
/// </summary>
internal sealed class MovingAverage
{
    private readonly double[] _buffer;
    private readonly int _capacity;
    private int _index;
    private int _count;
    private double _sum;
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    private readonly object _lock = new();

    public MovingAverage(int windowSize)
    {
        _capacity = windowSize;
        _buffer = new double[windowSize];
    }

    public void Add(double value)
    {
        lock (_lock)
        {
            if (_count >= _capacity)
                _sum -= _buffer[_index];
            else
                _count++;

            _buffer[_index] = value;
            _sum += value;
            _index = (_index + 1) % _capacity;

            if (value < _min) _min = value;
            if (value > _max) _max = value;
        }
    }

    public double Average
    {
        get
        {
            lock (_lock)
            {
                return _count > 0 ? _sum / _count : 0.0;
            }
        }
    }

    public double Min
    {
        get
        {
            lock (_lock)
            {
                return _count > 0 ? _min : 0.0;
            }
        }
    }

    public double Max
    {
        get
        {
            lock (_lock)
            {
                return _count > 0 ? _max : 0.0;
            }
        }
    }
}
