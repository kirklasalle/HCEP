// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Spatial;

namespace HCEP.Tests.Spatial;

public sealed class RayPlaneTests
{
    [Fact]
    public void Intersect_RayHitsPlane_ReturnsTrue()
    {
        var origin = new Vector3(0, 0, 0);
        var dir = Vector3.Normalize(new Vector3(0, 0, 1));    // looking forward
        var planePoint = new Vector3(0, 0, 2);
        var planeNormal = new Vector3(0, 0, -1);              // facing back toward camera

        bool hit = RayPlane.Intersect(origin, dir, planePoint, planeNormal, out var intersection);

        Assert.True(hit);
        Assert.Equal(2f, intersection.Z, 1e-4f);
        Assert.Equal(0f, intersection.X, 1e-4f);
        Assert.Equal(0f, intersection.Y, 1e-4f);
    }

    [Fact]
    public void Intersect_ParallelRay_ReturnsFalse()
    {
        var origin = new Vector3(0, 0, 0);
        var dir = new Vector3(1, 0, 0);                       // moving sideways
        var planePoint = new Vector3(0, 0, 2);
        var planeNormal = new Vector3(0, 0, -1);

        bool hit = RayPlane.Intersect(origin, dir, planePoint, planeNormal, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Intersect_BehindOrigin_ReturnsFalse()
    {
        var origin = new Vector3(0, 0, 5);
        var dir = new Vector3(0, 0, 1);                       // looking away from plane
        var planePoint = new Vector3(0, 0, 2);
        var planeNormal = new Vector3(0, 0, -1);

        bool hit = RayPlane.Intersect(origin, dir, planePoint, planeNormal, out _);

        Assert.False(hit);
    }

    [Fact]
    public void PointToRayDistance_OnRay_ReturnsZero()
    {
        var origin = new Vector3(0, 0, 0);
        var dir = new Vector3(0, 0, 1);
        var point = new Vector3(0, 0, 5);

        float dist = RayPlane.PointToRayDistance(point, origin, dir);

        Assert.Equal(0f, dist, 1e-4f);
    }

    [Fact]
    public void PointToRayDistance_OffRay_ReturnsCorrectDistance()
    {
        var origin = new Vector3(0, 0, 0);
        var dir = new Vector3(0, 0, 1);
        var point = new Vector3(3, 0, 5);                     // 3m off to the side

        float dist = RayPlane.PointToRayDistance(point, origin, dir);

        Assert.Equal(3f, dist, 1e-2f);
    }

    [Fact]
    public void AngleBetweenDeg_SameDirection_ReturnsZero()
    {
        var a = new Vector3(0, 0, 1);
        var b = new Vector3(0, 0, 1);

        float angle = RayPlane.AngleBetweenDeg(a, b);

        Assert.Equal(0f, angle, 1e-2f);
    }

    [Fact]
    public void AngleBetweenDeg_Perpendicular_Returns90()
    {
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(0, 1, 0);

        float angle = RayPlane.AngleBetweenDeg(a, b);

        Assert.Equal(90f, angle, 0.1f);
    }

    [Fact]
    public void AngleBetweenDeg_Opposite_Returns180()
    {
        var a = new Vector3(0, 0, 1);
        var b = new Vector3(0, 0, -1);

        float angle = RayPlane.AngleBetweenDeg(a, b);

        Assert.Equal(180f, angle, 0.1f);
    }
}
