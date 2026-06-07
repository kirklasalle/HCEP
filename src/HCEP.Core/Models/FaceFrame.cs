// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// Face tracking data for a single frame from Kinect Face Tracking SDK.
/// Contains Action Units, 2D/3D feature points, and head pose.
/// </summary>
public sealed record FaceFrame
{
    /// <summary>Frame timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Associated skeleton tracking ID.</summary>
    public int TrackingId { get; init; }

    /// <summary>Whether the face is successfully tracked.</summary>
    public bool IsTracked { get; init; }

    /// <summary>
    /// Six Kinect v1 Action Unit weights.
    /// Index maps to <see cref="ActionUnit"/> enum.
    /// Values in [-1..+1] range.
    /// </summary>
    public float[] ActionUnits { get; init; } = Array.Empty<float>();

    /// <summary>
    /// 2D projected feature points (pixel coordinates).
    /// Kinect v1 provides 87+ feature points.
    /// </summary>
    public Vector2[] FeaturePoints2D { get; init; } = Array.Empty<Vector2>();

    /// <summary>
    /// 3D feature points in head-relative coordinates (mm).
    /// Pupil points are indices 67-74.
    /// </summary>
    public Vector3[] FeaturePoints3D { get; init; } = Array.Empty<Vector3>();

    /// <summary>Head rotation: (pitch, yaw, roll) in degrees.</summary>
    public Vector3 HeadRotation { get; init; }

    /// <summary>Head translation in camera space (mm).</summary>
    public Vector3 HeadTranslation { get; init; }

    /// <summary>Face bounding rectangle (X, Y, Width, Height) in pixels.</summary>
    public (int X, int Y, int Width, int Height) FaceRect { get; init; }

    // ── Triangle Mesh (from FaceTrackLib SDK) ──────────────────

    /// <summary>
    /// Full face mesh vertices projected to 2D pixel coordinates.
    /// Typically ~121 vertices from the FaceTrackLib 3D face model.
    /// Null when using skeleton-approximate face tracking.
    /// </summary>
    public Vector2[]? FaceMeshVertices2D { get; init; }

    /// <summary>
    /// Full face mesh vertices projected to 2D pixel coordinates in a neutral, front-facing pose.
    /// Used by the autonomous 3D avatar to prevent mimicking the user.
    /// Null when using skeleton-approximate face tracking.
    /// </summary>
    public Vector2[]? NeutralFaceMeshVertices2D { get; init; }

    /// <summary>
    /// Triangle mesh topology: array of (First, Second, Third) vertex indices.
    /// Static — does not change between frames.
    /// Null when using skeleton-approximate face tracking.
    /// </summary>
    public (int First, int Second, int Third)[]? FaceMeshTriangles { get; init; }

    /// <summary>
    /// Last HRESULT from IFTModel.GetProjectedShape.
    /// 0 = success / not attempted yet.
    /// Non-zero = failure code (displayed in MESH HUD for diagnostics).
    /// </summary>
    public uint MeshHr { get; init; }

    // ── Pupil Accessors ────────────────────────────────────────
    /// <summary>Left pupil 3D position (feature point index 69).</summary>
    public Vector3 LeftPupil3D =>
        FeaturePoints3D.Length > 69 ? FeaturePoints3D[69] : Vector3.Zero;

    /// <summary>Right pupil 3D position (feature point index 73).</summary>
    public Vector3 RightPupil3D =>
        FeaturePoints3D.Length > 73 ? FeaturePoints3D[73] : Vector3.Zero;

    /// <summary>Cyclopean (midpoint between pupils) 3D position.</summary>
    public Vector3 CyclopeanPoint3D => (LeftPupil3D + RightPupil3D) * 0.5f;

    // ── Eye Center Location Accessors ──────────────────────────

    // Right eye contour indices (camera-right = person's left eye)
    private static readonly int[] _rightEyeIndices = [10, 11, 9, 13, 14, 12];
    // Left eye contour indices (camera-left = person's right eye)
    private static readonly int[] _leftEyeIndices = [31, 32, 30, 34, 35, 33];

    /// <summary>
    /// Left eye center in 2D pixel coords — centroid of left eye contour feature points.
    /// This represents the physical LOCATION of the eye, not pupil gaze direction.
    /// </summary>
    public Vector2 LeftEyeCenter2D => ComputeEyeCenter2D(_leftEyeIndices);

    /// <summary>
    /// Right eye center in 2D pixel coords — centroid of right eye contour feature points.
    /// This represents the physical LOCATION of the eye, not pupil gaze direction.
    /// </summary>
    public Vector2 RightEyeCenter2D => ComputeEyeCenter2D(_rightEyeIndices);

    /// <summary>
    /// Inter-ocular distance in pixels (distance between eye centers).
    /// Key metric for face scale estimation and eye tracking quality.
    /// </summary>
    public float InterOcularDistance2D
    {
        get
        {
            var l = LeftEyeCenter2D;
            var r = RightEyeCenter2D;
            return (l == Vector2.Zero || r == Vector2.Zero)
                ? 0f
                : Vector2.Distance(l, r);
        }
    }

    private Vector2 ComputeEyeCenter2D(int[] indices)
    {
        if (FeaturePoints2D.Length == 0) return Vector2.Zero;
        float sumX = 0, sumY = 0;
        int count = 0;
        foreach (var i in indices)
        {
            if (i >= FeaturePoints2D.Length) continue;
            var p = FeaturePoints2D[i];
            if (p == Vector2.Zero) continue;
            sumX += p.X;
            sumY += p.Y;
            count++;
        }
        return count > 0 ? new Vector2(sumX / count, sumY / count) : Vector2.Zero;
    }
}
