// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// Abstraction over the Kinect v1 sensor. Provides lifecycle management
/// and frame stream access.
/// </summary>
public interface ISensorSource : IAsyncDisposable
{
    /// <summary>Current sensor connection state.</summary>
    SensorState State { get; }

    /// <summary>Initialize and open the sensor with the requested streams.</summary>
    Task InitializeAsync(SensorStreamType streams, CancellationToken ct = default);

    /// <summary>Start streaming frames.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop streaming frames (sensor stays connected).</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Fires when a new skeleton frame is available.</summary>
    event Action<SkeletonFrame>? SkeletonFrameReady;

    /// <summary>Fires when a new face frame is available.</summary>
    event Action<FaceFrame>? FaceFrameReady;

    /// <summary>Fires when a new color frame is available.</summary>
    event Action<ColorFrame>? ColorFrameReady;

    /// <summary>Fires when a new depth frame is available.</summary>
    event Action<DepthFrame>? DepthFrameReady;

    /// <summary>Fires when a new infrared frame is available (BGRA32 grayscale).</summary>
    event Action<ColorFrame>? InfraredFrameReady;

    /// <summary>Fires when a new audio frame is available.</summary>
    event Action<AudioFrame>? AudioFrameReady;

    /// <summary>Fires when the sensor state changes.</summary>
    event Action<SensorState>? StateChanged;

    /// <summary>Sensor tilt angle in degrees [-27..+27].</summary>
    int ElevationAngle { get; set; }

    /// <summary>
    /// When true, Kinect uses seated/upper-body-only tracking (10 joints).
    /// When false, uses default full-body tracking (20 joints).
    /// Can be changed at runtime.
    /// </summary>
    bool SeatedMode { get; set; }
}
