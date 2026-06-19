// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
using System.Numerics;

namespace HCEP.Spatial;

/// <summary>
/// Coordinate mapping utilities for the Kinect v1 sensor space.
/// Converts between depth, color, and skeleton coordinate spaces.
/// </summary>
public static class CoordinateMapper
{
    // ── Kinect v1 Camera Intrinsics (640×480) ──────────────────
    public const float DepthFocalLengthX = 525.0f;
    public const float DepthFocalLengthY = 525.0f;
    public const float DepthPrincipalX = 320.0f;
    public const float DepthPrincipalY = 240.0f;

    public const float ColorFocalLengthX = 531.15f;
    public const float ColorFocalLengthY = 531.15f;
    public const float ColorPrincipalX = 320.0f;
    public const float ColorPrincipalY = 240.0f;

    /// <summary>
    /// Projects a 3D point (camera space, meters) to a 2D depth image pixel.
    /// </summary>
    public static Vector2 ProjectToDepth(Vector3 point)
    {
        if (MathF.Abs(point.Z) < 1e-6f) return new Vector2(-1, -1);

        float x = (point.X * DepthFocalLengthX / point.Z) + DepthPrincipalX;
        float y = (point.Y * DepthFocalLengthY / point.Z) + DepthPrincipalY;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Projects a 3D point (camera space, meters) to a 2D color image pixel.
    /// </summary>
    public static Vector2 ProjectToColor(Vector3 point)
    {
        if (MathF.Abs(point.Z) < 1e-6f) return new Vector2(-1, -1);

        float x = (point.X * ColorFocalLengthX / point.Z) + ColorPrincipalX;
        float y = (point.Y * ColorFocalLengthY / point.Z) + ColorPrincipalY;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Back-projects a depth pixel to a 3D point in camera space.
    /// </summary>
    /// <param name="pixelX">X coordinate in depth image.</param>
    /// <param name="pixelY">Y coordinate in depth image.</param>
    /// <param name="depthMm">Depth value in millimeters.</param>
    /// <returns>3D point in camera space (meters).</returns>
    public static Vector3 DepthToCamera(float pixelX, float pixelY, float depthMm)
    {
        float z = depthMm / 1000f;
        float x = (pixelX - DepthPrincipalX) * z / DepthFocalLengthX;
        float y = (pixelY - DepthPrincipalY) * z / DepthFocalLengthY;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Computes the Euclidean distance between two 3D points in meters.
    /// </summary>
    public static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

    /// <summary>
    /// Computes the distance from the sensor (Z=0 plane) in meters.
    /// </summary>
    public static float DistanceFromSensor(Vector3 point) => point.Z;
}
