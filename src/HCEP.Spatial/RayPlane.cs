// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;

namespace HCEP.Spatial;

/// <summary>
/// Ray-plane intersection and distance calculations for gaze target mapping.
/// </summary>
public static class RayPlane
{
    /// <summary>
    /// Computes the intersection of a ray with a plane.
    /// </summary>
    /// <param name="rayOrigin">Origin of the ray.</param>
    /// <param name="rayDirection">Normalized direction of the ray.</param>
    /// <param name="planePoint">A point on the plane.</param>
    /// <param name="planeNormal">Normal vector of the plane.</param>
    /// <param name="intersection">Output intersection point.</param>
    /// <returns>True if the ray intersects the plane (non-parallel, forward).</returns>
    public static bool Intersect(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        Vector3 planePoint,
        Vector3 planeNormal,
        out Vector3 intersection)
    {
        intersection = Vector3.Zero;

        float denom = Vector3.Dot(planeNormal, rayDirection);
        if (MathF.Abs(denom) < 1e-8f)
            return false; // Ray parallel to plane

        float t = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denom;
        if (t < 0)
            return false; // Intersection behind ray origin

        intersection = rayOrigin + rayDirection * t;
        return true;
    }

    /// <summary>
    /// Gets the distance from a point to a ray (for cone classification).
    /// </summary>
    /// <param name="point">The target point.</param>
    /// <param name="rayOrigin">Ray origin.</param>
    /// <param name="rayDirection">Normalized ray direction.</param>
    /// <returns>Perpendicular distance from the point to the ray.</returns>
    public static float PointToRayDistance(Vector3 point, Vector3 rayOrigin, Vector3 rayDirection)
    {
        Vector3 v = point - rayOrigin;
        float t = Vector3.Dot(v, rayDirection);
        Vector3 projection = rayOrigin + rayDirection * t;
        return Vector3.Distance(point, projection);
    }

    /// <summary>
    /// Computes the angle between two vectors in degrees.
    /// </summary>
    public static float AngleBetweenDeg(Vector3 a, Vector3 b)
    {
        float dot = Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b));
        dot = Math.Clamp(dot, -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }
}
