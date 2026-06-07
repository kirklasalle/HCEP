// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Models;

namespace HCEP.Tests.Core;

public sealed class FaceTopologyTests
{
    [Fact]
    public void BasicChains_ContainsSevenChains()
    {
        // Eyes (2), Brows (2), NoseBridge (1), UpperLip (1), Jawline (1)
        Assert.Equal(7, FaceTopology.BasicChains.Length);
    }

    [Fact]
    public void ExtendedChains_ContainsNineChains()
    {
        // Eyes (2), Brows (2), NoseBridge (1), NoseTip (1), OuterLip (1), InnerLip (1), FaceContour (1)
        Assert.Equal(9, FaceTopology.ExtendedChains.Length);
    }

    [Fact]
    public void RightEye_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.RightEye[0], FaceTopology.RightEye[^1]);
    }

    [Fact]
    public void LeftEye_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.LeftEye[0], FaceTopology.LeftEye[^1]);
    }

    [Fact]
    public void NoseTip_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.NoseTip[0], FaceTopology.NoseTip[^1]);
    }

    [Fact]
    public void OuterLip_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.OuterLip[0], FaceTopology.OuterLip[^1]);
    }

    [Fact]
    public void InnerLip_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.InnerLip[0], FaceTopology.InnerLip[^1]);
    }

    [Fact]
    public void FaceContour_IsClosedLoop()
    {
        Assert.Equal(FaceTopology.FaceContour[0], FaceTopology.FaceContour[^1]);
    }

    [Fact]
    public void AllIndices_WithinKinect87PointRange()
    {
        // Kinect FaceTrackLib exposes 87 feature points (0–86)
        // but extended chains reference up to index 67 for inner lip
        foreach (var chain in FaceTopology.ExtendedChains)
        {
            foreach (int idx in chain)
            {
                Assert.InRange(idx, 0, 86);
            }
        }
    }

    [Fact]
    public void EyeIndices_HaveSixPoints()
    {
        Assert.Equal(6, FaceTopology.RightEyeIndices.Length);
        Assert.Equal(6, FaceTopology.LeftEyeIndices.Length);
    }

    [Fact]
    public void EyeIndices_AreDisjoint()
    {
        var rightSet = new HashSet<int>(FaceTopology.RightEyeIndices);
        var leftSet = new HashSet<int>(FaceTopology.LeftEyeIndices);

        Assert.Empty(rightSet.Intersect(leftSet));
    }
}
