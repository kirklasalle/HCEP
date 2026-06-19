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
namespace HCEP.Core.Models;

/// <summary>
/// Shared Kinect FaceTrackLib 87-point feature-point edge connectivity.
/// Indices match the KinectSensorSource FeaturePoints2D ordering
/// (Candide-3 model, 0–86).
/// </summary>
public static class FaceTopology
{
    // ── Common eye / brow / nose-bridge / lip / jaw chains ──

    /// <summary>Right eye loop: outer → midTop → aboveMid → inner → belowMid → midBottom → outer.</summary>
    public static readonly int[] RightEye = [10, 11, 9, 13, 14, 12, 10];

    /// <summary>Left eye loop.</summary>
    public static readonly int[] LeftEye = [31, 32, 30, 34, 35, 33, 31];

    /// <summary>Right eyebrow: outer → midTop → inner → midBottom.</summary>
    public static readonly int[] RightBrow = [5, 6, 7, 8];

    /// <summary>Left eyebrow: rightSide → midTop → leftSide → midBottom.</summary>
    public static readonly int[] LeftBrow = [29, 28, 27, 26];

    /// <summary>Nose bridge: inner right eye to inner left eye.</summary>
    public static readonly int[] NoseBridge = [13, 34];

    /// <summary>
    /// Upper lip contour: right corner → right dip → right top → right upper →
    /// center dip → left upper → left top → left dip → left corner.
    /// </summary>
    public static readonly int[] UpperLip = [16, 18, 19, 20, 2, 21, 22, 23, 24];

    /// <summary>Jawline: right side → right chin → bottom → left side.</summary>
    public static readonly int[] Jawline = [15, 17, 4, 25];

    // ── Extended chains (full-mesh avatar fallback) ─────────

    /// <summary>Nose tip and nostrils (loop).</summary>
    public static readonly int[] NoseTip = [40, 41, 42, 43, 44, 45, 40];

    /// <summary>Outer lip (loop).</summary>
    public static readonly int[] OuterLip = [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 48];

    /// <summary>Inner lip (loop).</summary>
    public static readonly int[] InnerLip = [60, 61, 62, 63, 64, 65, 66, 67, 60];

    /// <summary>Full face contour (loop).</summary>
    public static readonly int[] FaceContour = [0, 1, 2, 3, 4, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 0];

    // ── Pre-built chain arrays ──────────────────────────────

    /// <summary>
    /// Basic edge chains used by the video overlay wireframe.
    /// Covers eyes, brows, nose bridge, upper lip, and jawline.
    /// </summary>
    public static readonly int[][] BasicChains =
    [
        RightEye,
        LeftEye,
        RightBrow,
        LeftBrow,
        NoseBridge,
        UpperLip,
        Jawline,
    ];

    /// <summary>
    /// Extended edge chains used by the 3D avatar fallback renderer.
    /// Includes everything in <see cref="BasicChains"/> plus nose tip,
    /// outer/inner lip, and full face contour.
    /// </summary>
    public static readonly int[][] ExtendedChains =
    [
        RightEye,
        LeftEye,
        RightBrow,
        LeftBrow,
        NoseBridge,
        NoseTip,
        OuterLip,
        InnerLip,
        FaceContour,
    ];

    // ── Feature-point index groups ──────────────────────────

    /// <summary>Right eye feature-point indices (non-looped).</summary>
    public static readonly int[] RightEyeIndices = [9, 10, 11, 12, 13, 14];

    /// <summary>Left eye feature-point indices (non-looped).</summary>
    public static readonly int[] LeftEyeIndices = [30, 31, 32, 33, 34, 35];
}
