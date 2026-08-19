// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: ArcFace 5-Point Landmark Alignment & Centroid Multi-Pose
// ──────────────────────────────────────────────────────────────
using System;
using System.Numerics;
using HCEP.Vision;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HCEP.Tests.Vision;

public sealed class ArcFaceAlignmentAndCentroidTests
{
    [Fact]
    public void GenerateAlignedEmbedding_WithoutModelLoaded_ReturnsEmptyArrayGracefully()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        var pixels = new byte[640 * 480 * 4];
        var landmarks = new Vector2[]
        {
            new(300, 200), // Left Eye
            new(340, 200), // Right Eye
            new(320, 230), // Nose
            new(305, 260), // Mouth Left
            new(335, 260), // Mouth Right
        };

        var embedding = recognizer.GenerateAlignedEmbedding(pixels, 640, 480, landmarks, 4);
        Assert.NotNull(embedding);
        Assert.Empty(embedding);
    }

    [Fact]
    public void Enroll_SingleAndMultiPose_CentroidAveragingMaintainsUnitNorm()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        
        // Pose 1 embedding
        float[] emb1 = new float[512];
        emb1[0] = 1.0f;

        recognizer.Enroll("Kirk", emb1);
        Assert.Equal(1, recognizer.EnrolledCount);

        var match1 = recognizer.Match(emb1);
        Assert.NotNull(match1);
        Assert.Equal("Kirk", match1.Value.Name);
        Assert.InRange(match1.Value.Similarity, 0.99f, 1.01f);

        // Pose 2 embedding (orthogonal angle)
        float[] emb2 = new float[512];
        emb2[1] = 1.0f;

        recognizer.Enroll("Kirk", emb2);
        Assert.Equal(1, recognizer.EnrolledCount); // Count still 1 (updated centroid)

        // The running centroid EMA (0.8 existing + 0.2 new) yields normalized vector (0.970, 0.243)
        var matchBlend = recognizer.Match(emb1);
        Assert.NotNull(matchBlend);
        Assert.Equal("Kirk", matchBlend.Value.Name);
        Assert.InRange(matchBlend.Value.Similarity, 0.90f, 0.99f);
    }

    [Fact]
    public void GenerateAlignedEmbedding_InvalidInputs_HandledSafely()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);

        // Insufficient landmarks (< 5)
        var insufficientLandmarks = new Vector2[]
        {
            new(100, 100),
            new(200, 200)
        };
        var res1 = recognizer.GenerateAlignedEmbedding(new byte[1000], 100, 100, insufficientLandmarks, 4);
        Assert.Empty(res1);

        // Empty pixels
        var res2 = recognizer.GenerateAlignedEmbedding(ReadOnlySpan<byte>.Empty, 100, 100, new Vector2[5], 4);
        Assert.Empty(res2);
    }
}
