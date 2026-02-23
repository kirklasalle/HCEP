// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Models;
using HCEP.Spatial;

namespace HCEP.Tests.Spatial;

public sealed class PnPSolverTests
{
    [Fact]
    public void Solve_WithCanonicalFaceModel_ReturnsNonZeroPose()
    {
        // Use the canonical face model 3D points
        var objectPoints = Anthropometrics.CanonicalFaceModel;

        // Simulate 2D projections (face roughly centered at 320,240, ~2m away)
        var imagePoints = new Vector2[]
        {
            new(320, 240),   // Nose tip
            new(320, 290),   // Chin
            new(290, 210),   // Left eye corner
            new(350, 210),   // Right eye corner
            new(300, 270),   // Left mouth corner
            new(340, 270),   // Right mouth corner
        };

        float focalLength = 525f;
        var principal = new Vector2(320, 240);

        var (rotation, translation) = PnPSolver.Solve(objectPoints, imagePoints, focalLength, principal);

        // Translation Z should be positive (face in front of camera)
        Assert.True(translation.Z > 0, "Translation Z should be positive (face in front of camera)");
    }

    [Fact]
    public void Solve_TooFewPoints_ReturnsZero()
    {
        var objectPoints = new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0) };
        var imagePoints = new Vector2[] { new(100, 100), new(200, 100), new(100, 200) };

        var (rotation, translation) = PnPSolver.Solve(objectPoints, imagePoints, 525f, new Vector2(320, 240));

        // 3 points is below minimum (4), should return zero
        Assert.Equal(Vector3.Zero, rotation);
        Assert.Equal(Vector3.Zero, translation);
    }

    [Fact]
    public void Solve_MismatchedCounts_ReturnsZero()
    {
        var objectPoints = new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0) };
        var imagePoints = new Vector2[] { new(100, 100), new(200, 100) }; // only 2

        var (rotation, translation) = PnPSolver.Solve(objectPoints, imagePoints, 525f, new Vector2(320, 240));

        Assert.Equal(Vector3.Zero, rotation);
        Assert.Equal(Vector3.Zero, translation);
    }

    [Fact]
    public void RotationMatrixToEuler_Identity_ReturnsZero()
    {
        var euler = PnPSolver.RotationMatrixToEuler(Matrix4x4.Identity);

        Assert.Equal(0f, euler.X, 0.1f); // pitch
        Assert.Equal(0f, euler.Y, 0.1f); // yaw
        Assert.Equal(0f, euler.Z, 0.1f); // roll
    }
}
