// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Models;

/// <summary>
/// A color image frame from the Kinect RGB camera.
/// </summary>
public sealed record ColorFrame
{
    /// <summary>Frame timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Raw pixel data (BGRA32 format).</summary>
    public required byte[] PixelData { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; } = 640;

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; } = 480;

    /// <summary>Bytes per pixel (4 for BGRA32).</summary>
    public int BytesPerPixel { get; init; } = 4;

    /// <summary>Frame number from sensor.</summary>
    public int FrameNumber { get; init; }
}

/// <summary>
/// A depth image frame from the Kinect IR sensor.
/// </summary>
public sealed record DepthFrame
{
    /// <summary>Frame timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Raw depth data (millimeters, 16-bit per pixel).</summary>
    public required short[] DepthData { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; } = 640;

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; } = 480;

    /// <summary>Minimum reliable depth in millimeters.</summary>
    public int MinDepthMm { get; init; } = 800;

    /// <summary>Maximum reliable depth in millimeters.</summary>
    public int MaxDepthMm { get; init; } = 4000;

    /// <summary>Frame number from sensor.</summary>
    public int FrameNumber { get; init; }
}
