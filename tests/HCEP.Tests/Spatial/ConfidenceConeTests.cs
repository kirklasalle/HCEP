// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Spatial;

namespace HCEP.Tests.Spatial;

public sealed class ConfidenceConeTests
{
    [Fact]
    public void Classify_NoLandmarks_ReturnsUnknown()
    {
        var cone = new ConfidenceCone();
        var (region, dist) = cone.Classify(new Vector3(0, 0, 1));

        Assert.Equal(GazeRegion.Unknown, region);
    }

    [Fact]
    public void Classify_ExactHitOnLeftEye_ReturnsLeftEye()
    {
        var cone = new ConfidenceCone
        {
            ConeRadiusCm = 5.0f, // 5cm cone
        };
        cone.Landmarks[GazeRegion.LeftEye] = new Vector3(-0.03f, 0.02f, 1.5f);
        cone.Landmarks[GazeRegion.RightEye] = new Vector3(0.03f, 0.02f, 1.5f);

        // Gaze hits exactly on left eye
        var (region, dist) = cone.Classify(new Vector3(-0.03f, 0.02f, 1.5f));

        Assert.Equal(GazeRegion.LeftEye, region);
        Assert.Equal(0f, dist, 0.01f);
    }

    [Fact]
    public void Classify_OutsideCone_ReturnsUnknown()
    {
        var cone = new ConfidenceCone
        {
            ConeRadiusCm = 2.0f, // tight 2cm cone
        };
        cone.Landmarks[GazeRegion.LeftEye] = new Vector3(-0.03f, 0.02f, 1.5f);

        // Gaze is way off — 50cm away
        var (region, _) = cone.Classify(new Vector3(0.5f, 0.5f, 1.5f));

        Assert.Equal(GazeRegion.Unknown, region);
    }

    [Fact]
    public void Classify_CloserToMouth_ReturnsMouth()
    {
        var cone = new ConfidenceCone
        {
            ConeRadiusCm = 10.0f,
        };
        cone.Landmarks[GazeRegion.LeftEye] = new Vector3(-0.03f, 0.04f, 1.5f);
        cone.Landmarks[GazeRegion.Mouth] = new Vector3(0, -0.03f, 1.5f);

        // Gaze slightly below the mouth position — closer to mouth than eye
        var (region, _) = cone.Classify(new Vector3(0, -0.02f, 1.5f));

        Assert.Equal(GazeRegion.Mouth, region);
    }
}
