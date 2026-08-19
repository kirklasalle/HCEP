// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: Kinect Fusion 3D Head Scanner & Voxel Reconstruction
// ──────────────────────────────────────────────────────────────
using System;
using System.Numerics;
using HCEP.Core.Models;
using HCEP.Spatial;
using Xunit;

namespace HCEP.Tests.Spatial;

public sealed class KinectFusionHeadScannerTests
{
    [Fact]
    public void Scanner_InitialStateIsIdle()
    {
        var scanner = new KinectFusionHeadScanner();
        Assert.Equal(FusionScanState.Idle, scanner.State);
        Assert.Equal(0, scanner.IntegratedFrameCount);
        Assert.Null(scanner.LatestMesh);
    }

    [Fact]
    public void Scanner_StartScan_TransitionsToScanningState()
    {
        var scanner = new KinectFusionHeadScanner();
        FusionScanState? observedState = null;
        scanner.StateChanged += s => observedState = s;

        scanner.StartScan();
        Assert.Equal(FusionScanState.Scanning, scanner.State);
        Assert.Equal(FusionScanState.Scanning, observedState);
    }

    [Fact]
    public void Scanner_ProceduralHeadMesh_GeneratesValidWatertightGeometry()
    {
        var mesh = KinectFusionHeadScanner.GenerateProceduralHeadMesh(
            headWidth: 0.16f, headHeight: 0.22f, headDepth: 0.18f, rings: 16, sectors: 24);

        Assert.NotNull(mesh);
        Assert.NotEmpty(mesh.Vertices);
        Assert.NotEmpty(mesh.Indices);
        Assert.NotEmpty(mesh.Normals);
        Assert.NotEmpty(mesh.UVs);

        Assert.Equal(mesh.Vertices.Length, mesh.Normals.Length);
        Assert.Equal(mesh.Vertices.Length, mesh.UVs.Length);
        Assert.Equal(0, mesh.Indices.Length % 3); // Must be triangles

        // Validate normals are normalized unit vectors
        foreach (var normal in mesh.Normals)
        {
            float len = normal.Length();
            Assert.InRange(len, 0.98f, 1.02f);
        }

        // Validate UVs are within [0..1] range
        foreach (var uv in mesh.UVs)
        {
            Assert.InRange(uv.X, 0.0f, 1.0f);
            Assert.InRange(uv.Y, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void Scanner_MultiFrameIntegration_ReachesCompletedState()
    {
        var scanner = new KinectFusionHeadScanner();
        scanner.StartScan();

        var fakeFace = new FaceFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            TrackingId = 1,
            IsTracked = true,
            HeadRotation = Vector3.Zero,
            HeadTranslation = new Vector3(0, 0, 1200),
            FeaturePoints3D = new Vector3[87],
            FeaturePoints2D = new Vector2[87],
            ActionUnits = new float[6],
            FaceRect = (100, 100, 200, 200),
            FaceMeshVertices3D = [new Vector3(-50, 0, 1200), new Vector3(50, 0, 1200), new Vector3(0, 50, 1200)],
            FaceMeshTriangles = [(0, 1, 2)],
            FaceMeshUVs = [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1)]
        };

        for (int i = 0; i < 35; i++)
        {
            scanner.IntegrateFrame(null, fakeFace);
        }

        Assert.Equal(FusionScanState.Completed, scanner.State);
        Assert.NotNull(scanner.LatestMesh);
        Assert.Equal(3, scanner.LatestMesh.Vertices.Length);
        Assert.Equal(3, scanner.LatestMesh.Indices.Length);
    }
}
