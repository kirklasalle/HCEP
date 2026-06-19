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
using HCEP.Core.Models;

namespace HCEP.Spatial;

/// <summary>
/// Which of the user's eye sockets the avatar IK rig is currently fixating.
/// </summary>
public enum EyeSocketTarget
{
    /// <summary>Avatar is looking at the user's left eye socket.</summary>
    Left,

    /// <summary>Avatar is looking at the user's right eye socket.</summary>
    Right
}

/// <summary>
/// Controls micro-saccade behaviour for the avatar's Inverse Kinematics eye rig.
///
/// Authentic human attention is characterised by small, rapid fixation shifts
/// (micro-saccades) between a conversation partner's eyes — approximately every
/// 1.5 – 3 seconds.  A static stare at a midpoint reads as robotic to the viewer.
///
/// This controller alternates IK focus between the user's left and right eye
/// sockets on a randomised (but bounded) timer to mirror genuine cognitive
/// engagement.
///
/// ── Usage (game/render loop) ─────────────────────────────────
/// <code>
///   var controller = new MicroSaccadeController();
///   // Per frame:
///   controller.Update(deltaSeconds);
///   Vector3 target = controller.GetFocusPoint3D(currentFaceFrame);
///   avatarIkRig.SetEyeLookTarget(target);
/// </code>
///
/// ── Data source constraint ────────────────────────────────────
/// Eye socket positions are derived exclusively from <see cref="FaceFrame"/>
/// basic face tracking data (eye contour feature points, indices 9–14 and 30–35).
/// <see cref="FaceFrame.FeaturePoints3D"/> are already in absolute camera space
/// (metres) as returned by the Kinect v1 SDK — no unit conversion or head-translation
/// offset is applied.  The high-resolution 87-point mesh and pupil indices 67–74
/// are intentionally not used to maintain low-latency, high-reliability operation.
/// </summary>
public sealed class MicroSaccadeController
{
    // ── Configuration ──────────────────────────────────────────

    /// <summary>Minimum hold duration before triggering a saccade (seconds).</summary>
    public float MinHoldSeconds { get; set; } = 1.5f;

    /// <summary>Maximum hold duration before triggering a saccade (seconds).</summary>
    public float MaxHoldSeconds { get; set; } = 3.0f;

    // ── Basic eye-contour feature point indices ────────────────
    // These are the "Face Tracking Basic" indices — DO NOT substitute pupil
    // indices (67-74) or mesh vertices here.
    // (mirrored from FaceFrame: camera-right = person's left eye)
    private static readonly int[] RightEyeContourIndices = [10, 11, 9, 13, 14, 12]; // person's left
    private static readonly int[] LeftEyeContourIndices = [31, 32, 30, 34, 35, 33]; // person's right

    // ── State ──────────────────────────────────────────────────

    private readonly Random _rng = new();

    /// <summary>Current eye socket being targeted by the avatar IK rig.</summary>
    public EyeSocketTarget CurrentTarget { get; private set; } = EyeSocketTarget.Left;

    /// <summary>Seconds remaining on the current fixation before the next saccade.</summary>
    public float TimeRemainingSeconds { get; private set; }

    /// <summary>
    /// Elapsed time since the last saccade (seconds).
    /// Useful for visualising the saccade rhythm in diagnostics.
    /// </summary>
    public float TimeSinceLastSaccadeSeconds { get; private set; }

    /// <summary>Total number of saccades fired since instantiation.</summary>
    public int SaccadeCount { get; private set; }

    // ── Construction ───────────────────────────────────────────

    /// <summary>
    /// Initialises the controller and seeds the first fixation timer.
    /// </summary>
    /// <param name="initialTarget">
    /// Which eye to target first.  Defaults to <see cref="EyeSocketTarget.Left"/>.
    /// </param>
    public MicroSaccadeController(EyeSocketTarget initialTarget = EyeSocketTarget.Left)
    {
        CurrentTarget = initialTarget;
        TimeRemainingSeconds = SampleNextInterval();
    }

    // ── Update ─────────────────────────────────────────────────

    /// <summary>
    /// Advances the saccade timer.  Must be called every render / physics frame.
    /// </summary>
    /// <param name="deltaSeconds">
    /// Elapsed time since the last call, in seconds.
    /// Typically <c>Time.deltaTime</c> in Unity or equivalent in other engines.
    /// </param>
    public void Update(double deltaSeconds)
    {
        float dt = (float)deltaSeconds;
        TimeSinceLastSaccadeSeconds += dt;
        TimeRemainingSeconds -= dt;

        if (TimeRemainingSeconds <= 0f)
            FireSaccade();
    }

    // ── IK Target ──────────────────────────────────────────────

    /// <summary>
    /// Returns the 3D world-space position (Kinect camera space, metres) of the
    /// currently targeted eye socket, derived from the basic eye contour feature
    /// points in the supplied <see cref="FaceFrame"/>.
    /// </summary>
    /// <param name="face">
    /// Current face tracking frame.  Must be tracked
    /// (<see cref="FaceFrame.IsTracked"/> == <c>true</c>).
    /// </param>
    /// <returns>
    /// 3-D position of the target eye socket in camera space (metres).
    /// Falls back to the cyclopean midpoint if feature points are unavailable.
    /// </returns>
    public Vector3 GetFocusPoint3D(FaceFrame face)
    {
        int[] indices = CurrentTarget == EyeSocketTarget.Left
            ? LeftEyeContourIndices
            : RightEyeContourIndices;

        // ComputeEyeSocketCentre3D returns head-relative mm (FeaturePoints3D coordinate space).
        // Add HeadTranslation (also mm, Camera Space) to get absolute Camera Space mm,
        // then divide by 1000 to return metres as the method contract requires.
        Vector3 socketHeadMm = ComputeEyeSocketCentre3D(face, indices);

        Vector3 absoluteMm = socketHeadMm != Vector3.Zero
            ? socketHeadMm + face.HeadTranslation
            : face.CyclopeanPoint3D + face.HeadTranslation;  // cyclopean fallback

        return absoluteMm / 1000f;  // mm → metres (Camera Space)
    }

    /// <summary>
    /// Returns the 3D position of the NON-current (background) eye socket.
    /// Useful for blend-based IK transitions or pre-seeding the next target.
    /// </summary>
    public Vector3 GetOffTargetPoint3D(FaceFrame face)
    {
        int[] indices = CurrentTarget == EyeSocketTarget.Right
            ? LeftEyeContourIndices
            : RightEyeContourIndices;

        Vector3 socketHeadMm = ComputeEyeSocketCentre3D(face, indices);

        Vector3 absoluteMm = socketHeadMm != Vector3.Zero
            ? socketHeadMm + face.HeadTranslation
            : face.CyclopeanPoint3D + face.HeadTranslation;

        return absoluteMm / 1000f;  // mm → metres (Camera Space)
    }

    /// <summary>
    /// Returns a smooth blend weight [0..1] indicating how far through the
    /// current fixation the controller is.  Useful for driving IK blend trees:
    /// 0 = just fired a saccade, 1 = about to fire the next saccade.
    /// </summary>
    public float FixationProgress
    {
        get
        {
            float total = TimeSinceLastSaccadeSeconds + TimeRemainingSeconds;
            return total > 0f ? TimeSinceLastSaccadeSeconds / total : 0f;
        }
    }

    // ── Private helpers ────────────────────────────────────────

    private void FireSaccade()
    {
        CurrentTarget = CurrentTarget == EyeSocketTarget.Left
            ? EyeSocketTarget.Right
            : EyeSocketTarget.Left;

        SaccadeCount++;
        TimeSinceLastSaccadeSeconds = 0f;
        TimeRemainingSeconds = SampleNextInterval();
    }

    /// <summary>Samples a uniform-random interval from [MinHoldSeconds, MaxHoldSeconds].</summary>
    private float SampleNextInterval() =>
        MinHoldSeconds + (float)_rng.NextDouble() * (MaxHoldSeconds - MinHoldSeconds);

    /// <summary>
    /// Computes the centroid of the specified eye contour feature points in
    /// camera space (metres) using only the basic 3D feature point array.
    /// Returns <see cref="Vector3.Zero"/> if no valid points exist.
    /// </summary>
    private static Vector3 ComputeEyeSocketCentre3D(FaceFrame face, int[] indices)
    {
        if (face.FeaturePoints3D.Length == 0) return Vector3.Zero;

        float sumX = 0f, sumY = 0f, sumZ = 0f;
        int count = 0;

        foreach (int i in indices)
        {
            if (i >= face.FeaturePoints3D.Length) continue;
            Vector3 p = face.FeaturePoints3D[i];
            if (p == Vector3.Zero) continue;

            sumX += p.X;
            sumY += p.Y;
            sumZ += p.Z;
            count++;
        }

        if (count == 0) return Vector3.Zero;

        // Returns centroid in head-relative coordinates (mm), matching FaceFrame.FeaturePoints3D.
        // Callers must add face.HeadTranslation and divide by 1000 to get Camera Space metres.
        return new Vector3(sumX / count, sumY / count, sumZ / count);
    }
}
