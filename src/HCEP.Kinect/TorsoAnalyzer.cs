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
using System.Collections.Immutable;
using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Kinect;

/// <summary>
/// Phase 9 — Torso Analyzer.
///
/// Extracts body-language signals from a Kinect v1 20-joint skeleton:
/// shoulder shrug detection, torso lean, torso yaw, and proxemic zone
/// classification per Hall (1966).
///
/// Joint index map (NUI_SKELETON_POSITION_INDEX):
///   0 = HipCenter   1 = Spine       2 = ShoulderCenter  3 = Head
///   4 = ShoulderLeft 8 = ShoulderRight
///  12 = HipLeft    16 = HipRight
/// </summary>
public sealed class TorsoAnalyzer
{
    // ── Thresholds ────────────────────────────────────────────────────────────
    /// <summary>Both-shoulder elevation above neutral needed for bilateral shrug (m).</summary>
    private const float ShrugThresholdM = 0.030f;   // ~3 cm above mid-shoulder line

    /// <summary>Minimum lean angle (degrees) to declare ForwardLean = true.</summary>
    private const float LeanThresholdDeg = 8.0f;

    // ── Proxemic zone thresholds (Hall, 1966) ─────────────────────────────────
    private const float IntimateMaxM = 0.45f;
    private const float PersonalMaxM = 1.20f;
    private const float SocialMaxM = 3.70f;

    // ── Joint indices ─────────────────────────────────────────────────────────
    private const int JHipCenter = 0;
    private const int JSpine = 1;
    private const int JShoulderCenter = 2;
    private const int JHead = 3;
    private const int JShoulderLeft = 4;
    private const int JShoulderRight = 8;
    private const int JHipLeft = 12;
    private const int JHipRight = 16;

    // ── Required tracking states ──────────────────────────────────────────────
    private static readonly int[] RequiredJoints =
    [
        JHipCenter, JShoulderCenter, JShoulderLeft, JShoulderRight
    ];

    /// <summary>
    /// Analyzes a single Kinect skeleton frame and produces a <see cref="TorsoReading"/>.
    /// Returns <see cref="TorsoReading.Unavailable"/> if required joints are not tracked.
    /// </summary>
    public TorsoReading Analyze(SkeletonFrame? frame)
    {
        if (frame is null || frame.State == TrackingState.NotTracked)
            return TorsoReading.Unavailable;

        var joints = frame.Joints;
        var states = frame.JointStates;

        // Require all four primary joints to be at least Inferred
        foreach (int j in RequiredJoints)
        {
            if (!joints.ContainsKey(j) || !states.ContainsKey(j))
                return TorsoReading.Unavailable;
            if (states[j] == TrackingState.NotTracked)
                return TorsoReading.Unavailable;
        }

        var hipCenter = joints[JHipCenter];
        var shoulderCenter = joints[JShoulderCenter];
        var shoulderLeft = joints[JShoulderLeft];
        var shoulderRight = joints[JShoulderRight];

        // ── Shoulder elevation ────────────────────────────────────────────────
        float shoulderElevDiff = shoulderLeft.Y - shoulderRight.Y;

        // Neutral shoulder mid-height is the average Y of both shoulders.
        float shoulderMidY = (shoulderLeft.Y + shoulderRight.Y) / 2f;
        // Neutral expected shoulder height is proportional to spine length.
        float spineLength = Vector3.Distance(shoulderCenter, hipCenter);
        float neutralExpectedY = hipCenter.Y + spineLength * 0.82f;
        float elevAboveNeutral = shoulderMidY - neutralExpectedY;
        bool bilateralShrug = elevAboveNeutral >= ShrugThresholdM;

        // ── Torso lean ────────────────────────────────────────────────────────
        // Vector from HipCenter to ShoulderCenter in the YZ plane.
        // Lean angle = angle from vertical (Y-axis), positive = toward sensor (−Z).
        Vector3 torsoVec = shoulderCenter - hipCenter;
        float leanAngleRad = MathF.Atan2(-torsoVec.Z, MathF.Max(0.01f, torsoVec.Y));
        float leanAngleDeg = leanAngleRad * (180f / MathF.PI);
        bool forwardLean = leanAngleDeg >= LeanThresholdDeg;

        // ── Torso yaw (rotation) ───────────────────────────────────────────────
        // Project shoulder vector onto XZ plane; angle = yaw from camera axis.
        Vector3 shoulderVec = Vector3.Normalize(shoulderRight - shoulderLeft);
        float torsoYawDeg = MathF.Atan2(shoulderVec.Z, shoulderVec.X) * (180f / MathF.PI);

        // ── Proxemic zone ─────────────────────────────────────────────────────
        float distM = frame.Position.Z; // HipCenter Z = user distance from sensor
        var zone = ClassifyProxemicZone(distM);

        return new TorsoReading
        {
            IsTracked = true,
            ShoulderElevationDiff = shoulderElevDiff,
            BilateralShrug = bilateralShrug,
            LeanAngleDeg = leanAngleDeg,
            ForwardLean = forwardLean,
            TorsoYawDeg = torsoYawDeg,
            ProxemicZone = zone,
        };
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static ProxemicZone ClassifyProxemicZone(float distM)
    {
        if (distM <= 0f) return ProxemicZone.Unknown;
        if (distM < IntimateMaxM) return ProxemicZone.Intimate;
        if (distM < PersonalMaxM) return ProxemicZone.Personal;
        if (distM < SocialMaxM) return ProxemicZone.Social;
        return ProxemicZone.Public;
    }
}
