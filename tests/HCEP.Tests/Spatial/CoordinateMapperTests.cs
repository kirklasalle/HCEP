// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Spatial;

namespace HCEP.Tests.Spatial;

public sealed class CoordinateMapperTests
{
    [Fact]
    public void ProjectToDepth_CenterPoint_ReturnsPrincipalPoint()
    {
        // A point at (0,0,1m) should project to the principal point
        var point = new Vector3(0, 0, 1);
        var pixel = CoordinateMapper.ProjectToDepth(point);

        Assert.Equal(CoordinateMapper.DepthPrincipalX, pixel.X, 1e-2f);
        Assert.Equal(CoordinateMapper.DepthPrincipalY, pixel.Y, 1e-2f);
    }

    [Fact]
    public void ProjectToDepth_ZeroDepth_ReturnsNegativeOne()
    {
        var point = new Vector3(0, 0, 0);
        var pixel = CoordinateMapper.ProjectToDepth(point);

        Assert.Equal(-1, pixel.X);
        Assert.Equal(-1, pixel.Y);
    }

    [Fact]
    public void DepthToCamera_PrincipalPoint_ReturnsCenterRay()
    {
        float depthMm = 2000; // 2 meters
        var point = CoordinateMapper.DepthToCamera(
            CoordinateMapper.DepthPrincipalX,
            CoordinateMapper.DepthPrincipalY,
            depthMm);

        Assert.Equal(0f, point.X, 1e-4f);
        Assert.Equal(0f, point.Y, 1e-4f);
        Assert.Equal(2f, point.Z, 1e-4f);
    }

    [Fact]
    public void ProjectToDepth_DepthToCamera_Roundtrip()
    {
        // A 3D point should survive project→backproject round-trip
        var original = new Vector3(0.5f, -0.3f, 2.0f);
        var pixel = CoordinateMapper.ProjectToDepth(original);
        float depthMm = original.Z * 1000f;
        var reconstructed = CoordinateMapper.DepthToCamera(pixel.X, pixel.Y, depthMm);

        Assert.Equal(original.X, reconstructed.X, 1e-3f);
        Assert.Equal(original.Y, reconstructed.Y, 1e-3f);
        Assert.Equal(original.Z, reconstructed.Z, 1e-3f);
    }

    [Fact]
    public void ProjectToColor_CenterPoint_ReturnsPrincipalPoint()
    {
        var point = new Vector3(0, 0, 1);
        var pixel = CoordinateMapper.ProjectToColor(point);

        Assert.Equal(CoordinateMapper.ColorPrincipalX, pixel.X, 1e-2f);
        Assert.Equal(CoordinateMapper.ColorPrincipalY, pixel.Y, 1e-2f);
    }

    [Fact]
    public void Distance_KnownPoints_ReturnsCorrectValue()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(3, 4, 0);

        Assert.Equal(5f, CoordinateMapper.Distance(a, b), 1e-4f);
    }

    [Fact]
    public void DistanceFromSensor_ReturnsZComponent()
    {
        var point = new Vector3(1.5f, 2.5f, 3.7f);

        Assert.Equal(3.7f, CoordinateMapper.DistanceFromSensor(point), 1e-4f);
    }
}
