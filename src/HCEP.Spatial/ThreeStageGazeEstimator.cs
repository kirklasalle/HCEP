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
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;

namespace HCEP.Spatial;

/// <summary>
/// 3-stage gaze estimator implementing the HCEP gaze algorithm:
///   Stage 1 — Head-pose gaze: SolvePnP → forward look direction
///   Stage 2 — Eye-in-head offset: pupil displacement from feature points
///   Stage 3 — Hybrid fusion: blend head + eye → ray-plane → confidence cone
/// </summary>
public sealed class ThreeStageGazeEstimator : IGazeEstimator
{
    // ── Camera intrinsics (Kinect v1 defaults, 640×480) ────────
    private const float DefaultFocalLength = 525.0f;    // pixels
    private static readonly Vector2 DefaultPrincipalPoint = new(320f, 240f);

    // ── Blending ───────────────────────────────────────────────
    /// <summary>Weight of head pose vs. eye displacement [0..1]. 0.6 = 60% head.</summary>
    public float HeadWeight { get; set; } = 0.6f;

    /// <summary>Temporal smoothing factor [0..1]. Higher = more smoothing.</summary>
    public float SmoothingAlpha { get; set; } = 0.3f;

    // ── Per-person calibration ─────────────────────────────────
    private readonly Dictionary<int, float> _personIpd = new();

    private readonly ConfidenceCone _cone = new();

    /// <inheritdoc />
    public GazeEstimate Estimate(FaceFrame face, GazeEstimate? previousEstimate = null)
    {
        if (!face.IsTracked)
            return CreateEmpty(face.Timestamp);

        // ── Stage 1: Head Pose Gaze ────────────────────────────
        Vector3 headForward = ComputeHeadGazeDirection(face);

        // ── Stage 2: Eye-in-Head Offset ────────────────────────
        Vector3 eyeOffset = ComputeEyeOffset(face);

        // ── Stage 3: Hybrid Fusion ─────────────────────────────
        Vector3 hybridDir = Vector3.Normalize(
            headForward * HeadWeight + eyeOffset * (1f - HeadWeight));

        // Temporal smoothing
        if (previousEstimate is not null)
        {
            hybridDir = Vector3.Normalize(
                Vector3.Lerp(previousEstimate.HybridDirection, hybridDir, 1f - SmoothingAlpha));
        }

        // Gaze origin = cyclopean eye position
        Vector3 origin = face.CyclopeanPoint3D / 1000f; // mm → meters

        // Ray-plane intersection with interlocutor face plane
        Vector3 intersection = Vector3.Zero;
        GazeRegion region = GazeRegion.Unknown;
        float confidence = 0f;

        // Assume interlocutor at ~1m forward
        Vector3 targetPlane = new(0, 0, 1.0f);
        Vector3 planeNormal = new(0, 0, -1);

        if (RayPlane.Intersect(origin, hybridDir, targetPlane, planeNormal, out intersection))
        {
            // Set up interlocutor face landmarks at the target plane (meters).
            // In v0.1 the "interlocutor" is a canonical face centered on the
            // sensor's optical axis at the target-plane distance.
            SetupInterlocutorLandmarks(targetPlane.Z);

            var (classRegion, distCm) = _cone.Classify(intersection);
            region = classRegion;
            confidence = distCm < Anthropometrics.DefaultConeRadiusCm
                ? 1f - (distCm / Anthropometrics.DefaultConeRadiusCm)
                : 0f;
        }

        return new GazeEstimate
        {
            HeadGazeDirection = headForward,
            EyeOffset = eyeOffset,
            HybridDirection = hybridDir,
            Origin = origin,
            IntersectionPoint = intersection,
            ConeHalfAngleDeg = ComputeConeAngle(origin, intersection),
            ClassifiedRegion = region,
            Confidence = Math.Clamp(confidence, 0f, 1f),
            Timestamp = face.Timestamp,
        };
    }

    /// <inheritdoc />
    public void CalibrateForPerson(int trackingId, float ipdMm)
    {
        _personIpd[trackingId] = ipdMm;
    }

    // ── Internal Methods ───────────────────────────────────────

    /// <summary>
    /// Places a canonical interlocutor face at the given Z distance (meters)
    /// so the confidence cone can classify gaze intersection points.
    /// Uses anthropometric averages for a standard adult face.
    /// </summary>
    private void SetupInterlocutorLandmarks(float targetZ)
    {
        _cone.Landmarks.Clear();
        float halfIpd = Anthropometrics.MeanIpdMm / 2000f; // ~0.0315 m
        _cone.Landmarks[GazeRegion.LeftEye]     = new Vector3(-halfIpd, 0f, targetZ);
        _cone.Landmarks[GazeRegion.RightEye]    = new Vector3( halfIpd, 0f, targetZ);
        _cone.Landmarks[GazeRegion.NasalBridge] = new Vector3(0f, -0.01f, targetZ);
        _cone.Landmarks[GazeRegion.Mouth]       = new Vector3(0f, -0.05f, targetZ);
        _cone.Landmarks[GazeRegion.Forehead]    = new Vector3(0f,  0.05f, targetZ);
        _cone.Landmarks[GazeRegion.Chin]        = new Vector3(0f, -0.07f, targetZ);
        _cone.Landmarks[GazeRegion.FaceCenter]  = new Vector3(0f,  0f,    targetZ);
    }

    private static Vector3 ComputeHeadGazeDirection(FaceFrame face)
    {
        // Convert head rotation (Euler degrees) to a forward direction vector
        float pitch = face.HeadRotation.X * (MathF.PI / 180f);
        float yaw = face.HeadRotation.Y * (MathF.PI / 180f);

        // Forward vector rotated by head pose
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * MathF.Cos(pitch),
            -MathF.Sin(pitch),
            MathF.Cos(yaw) * MathF.Cos(pitch)));
    }

    private static Vector3 ComputeEyeOffset(FaceFrame face)
    {
        if (face.FeaturePoints3D.Length < 74)
            return new Vector3(0, 0, 1); // Default forward

        // Compute eye-in-head direction from actual pupil displacement.
        //
        // Strategy: The eye contour centroid approximates the eye socket center.
        // The pupil position relative to this centroid reveals where the eye
        // is actually looking within the orbit. We average both eyes for the
        // cyclopean eye-in-head offset.
        //
        // Feature point indices (Kinect v1 FaceTrackLib):
        //   Left eye contour:  30-35   Left pupil:  69
        //   Right eye contour: 9-14    Right pupil: 73

        Vector3 leftPupil = face.LeftPupil3D;
        Vector3 rightPupil = face.RightPupil3D;

        // Compute left eye socket center from contour points (indices 30-35)
        Vector3 leftSocketCenter = Vector3.Zero;
        int leftCount = 0;
        for (int i = 30; i <= 35 && i < face.FeaturePoints3D.Length; i++)
        {
            Vector3 pt = face.FeaturePoints3D[i];
            if (pt != Vector3.Zero)
            {
                leftSocketCenter += pt;
                leftCount++;
            }
        }

        // Compute right eye socket center from contour points (indices 9-14)
        Vector3 rightSocketCenter = Vector3.Zero;
        int rightCount = 0;
        for (int i = 9; i <= 14 && i < face.FeaturePoints3D.Length; i++)
        {
            Vector3 pt = face.FeaturePoints3D[i];
            if (pt != Vector3.Zero)
            {
                rightSocketCenter += pt;
                rightCount++;
            }
        }

        if (leftCount == 0 || rightCount == 0)
            return new Vector3(0, 0, 1); // Fallback if contour points missing

        leftSocketCenter /= leftCount;
        rightSocketCenter /= rightCount;

        // Pupil displacement relative to socket center (in mm, head-relative coords).
        // This is the actual eye-in-head rotation signal.
        Vector3 leftOffset = leftPupil - leftSocketCenter;
        Vector3 rightOffset = rightPupil - rightSocketCenter;

        // Average both eyes for cyclopean eye-in-head direction
        Vector3 meanOffset = (leftOffset + rightOffset) * 0.5f;

        // Project into a forward-looking direction: the offset is lateral/vertical
        // displacement; the dominant forward component is the eye depth.
        Vector3 eyeDir = new Vector3(
            meanOffset.X,
            meanOffset.Y,
            Anthropometrics.EyeDepthMm);  // Forward component = orbital depth

        float len = eyeDir.Length();
        return len > 1e-6f ? eyeDir / len : new Vector3(0, 0, 1);
    }

    private static float ComputeConeAngle(Vector3 origin, Vector3 intersection)
    {
        float dist = Vector3.Distance(origin, intersection);
        if (dist < 0.01f) return 90f;

        float coneRadiusM = Anthropometrics.DefaultConeRadiusCm / 100f;
        return MathF.Atan(coneRadiusM / dist) * (180f / MathF.PI);
    }

    private static GazeEstimate CreateEmpty(DateTimeOffset timestamp) => new()
    {
        HeadGazeDirection = Vector3.UnitZ,
        EyeOffset = Vector3.UnitZ,
        HybridDirection = Vector3.UnitZ,
        Origin = Vector3.Zero,
        Confidence = 0,
        Timestamp = timestamp,
    };
}
