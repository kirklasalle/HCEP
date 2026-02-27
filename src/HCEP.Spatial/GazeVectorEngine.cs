// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;

namespace HCEP.Spatial;

/// <summary>
/// Phase 3 — World-Space Gaze Math Engine.
///
/// Bridges the physical world (Kinect Camera Space, metres) with the screen
/// (pixel coordinates from AvatarCoreControl) to compute the exact Pitch/Yaw
/// angles needed for the Avatar's pupils to achieve true eye contact.
///
/// ── Pipeline (per frame) ──────────────────────────────────────
/// 1. User Eye Pos     : <c>_saccade.GetFocusPoint3D(face)</c> → Camera Space, METRES.
/// 2. Avatar Eye World : <see cref="AvatarEyeScreenToWorldMm"/> converts the avatar
///                       eye socket's physical screen pixel coordinate into a 3D point
///                       on the monitor plane in Camera Space, MM.
/// 3. Gaze Vector      : <c>Normalize(userEyeMm − avatarEyeMm)</c> — points FROM
///                       the avatar eye TOWARD the user's eye.
/// 4. Pitch / Yaw      : derived from the gaze vector via atan2.
/// 5. Smoothing        : EMA (exponential moving average) removes Kinect jitter.
///
/// ── Coordinate Convention ─────────────────────────────────────
///   Camera Space: +X = right, +Y = up, +Z = away from sensor (toward user).
///   Kinect eye positions arrive in METRES; this class converts to mm internally.
///   Monitor plane: Z = −KinectOffsetZMm  (screen face is slightly behind Kinect).
///
/// ── Gaze Angle Convention (matches AvatarCoreControl.SetGaze) ─
///   Pitch > 0 → looking up  → pupils translate −Y.
///   Yaw   > 0 → looking right → pupils translate +X.
///
/// ── DPI Scaling Note ──────────────────────────────────────────
///   <paramref name="avatarEyeScreenPx"/> MUST be physical device pixels returned
///   by <c>Visual.PointToScreen()</c> — not WPF device-independent pixels (DIPs).
///   The caller (AvatarWindow) obtains physical screen dimensions via
///   <c>PresentationSource.CompositionTarget.TransformToDevice</c> and passes them
///   as <c>screenWidthPhysicalPx</c> / <c>screenHeightPhysicalPx</c> to the
///   orchestrator's <c>SetAvatarEyeProvider()</c> method.
/// </summary>
public sealed class GazeVectorEngine
{
    // ── Tuning ─────────────────────────────────────────────────

    /// <summary>
    /// EMA smoothing factor [0..1].
    /// Higher = snappier but noisier. Lower = smoother but lagging.
    /// Default 0.20 ≈ ~5-frame rolling average at 10 Hz.
    /// </summary>
    public float SmoothingAlpha { get; set; } = 0.20f;

    /// <summary>
    /// Vertical gaze correction applied after EMA smoothing (radians).
    /// Negative value shifts pupils downward — use to compensate for the
    /// "looking at forehead" bias introduced by Kinect FaceTracking placing
    /// eye sockets higher than the actual perceived eye contact point.
    /// Default: −5° (≈ −0.0873 rad). Tune in 1° steps during validation.
    /// </summary>
    public float VerticalCorrectionRad { get; set; } = -5f * MathF.PI / 180f;

    // ── Smooth state ───────────────────────────────────────────
    private float _smoothedPitch;
    private float _smoothedYaw;
    private bool _initialized;

    // ── Static world-space conversion ─────────────────────────

    /// <summary>
    /// Converts an Avatar eye socket's physical screen pixel position into a 3D
    /// point on the monitor plane in Kinect Camera Space (mm).
    /// </summary>
    /// <param name="avatarEyeScreenPx">
    /// Physical screen pixel coordinates of the avatar eye socket centre,
    /// as returned by <c>Visual.PointToScreen()</c>.
    /// </param>
    /// <param name="screenSizePhysicalPx">Physical screen dimensions (width, height) in pixels.</param>
    /// <param name="screenSizeMm">Physical screen dimensions (width, height) in mm.</param>
    /// <param name="kinectOffsetFromScreenCentreMm">
    /// Calibrated offset of the Kinect from the monitor centre (mm).
    /// Convention (per <see cref="CalibrationMatrixCalculator"/>):
    ///   positive Y = Kinect is above screen centre.
    ///   positive Z = Kinect protrudes forward of screen face.
    /// </param>
    /// <returns>
    /// 3D position of the avatar eye in Camera Space (mm).
    /// Z is fixed to the screen surface: <c>−kinectOffsetFromScreenCentreMm.Z</c>.
    /// </returns>
    public static Vector3 AvatarEyeScreenToWorldMm(
        Vector2 avatarEyeScreenPx,
        Vector2 screenSizePhysicalPx,
        Vector2 screenSizeMm,
        Vector3 kinectOffsetFromScreenCentreMm)
    {
        // ── Screen centre in physical pixels ──────────────────
        float centrePixX = screenSizePhysicalPx.X / 2f;
        float centrePixY = screenSizePhysicalPx.Y / 2f;

        // ── Pixel offset from screen centre ───────────────────
        float dxPx = avatarEyeScreenPx.X - centrePixX;
        float dyPx = avatarEyeScreenPx.Y - centrePixY;

        // ── Convert pixel offset to mm ────────────────────────
        float dxMm = dxPx / screenSizePhysicalPx.X * screenSizeMm.X;
        // Invert Y: screen pixels increase downward; world Y increases upward.
        float dyMm = -dyPx / screenSizePhysicalPx.Y * screenSizeMm.Y;

        // ── Screen centre in Camera Space ─────────────────────
        // KinectOffset convention: vector FROM screen centre TO Kinect.
        // → Screen centre is at −KinectOffset in Camera Space.
        float scX = -kinectOffsetFromScreenCentreMm.X;
        float scY = -kinectOffsetFromScreenCentreMm.Y;
        float scZ = -kinectOffsetFromScreenCentreMm.Z;  // screen surface

        return new Vector3(scX + dxMm, scY + dyMm, scZ);
    }

    // ── Per-frame gaze computation ─────────────────────────────

    /// <summary>
    /// Computes the smoothed (pitch, yaw) in radians for the Avatar's pupils
    /// to look from <paramref name="avatarEyeWorldMm"/> toward
    /// <paramref name="userEyePosMeters"/>.
    /// </summary>
    /// <param name="userEyePosMeters">
    /// User's eye position in Kinect Camera Space, METRES
    /// (from <c>MicroSaccadeController.GetFocusPoint3D(face)</c>).
    /// </param>
    /// <param name="avatarEyeWorldMm">
    /// Avatar eye position in Kinect Camera Space, MM
    /// (from <see cref="AvatarEyeScreenToWorldMm"/>).
    /// </param>
    /// <returns>Smoothed (pitch radians, yaw radians) for <c>AvatarCoreControl.SetGaze()</c>.</returns>
    public (float pitch, float yaw) Compute(Vector3 userEyePosMeters, Vector3 avatarEyeWorldMm)
    {
        // Bring user eye into mm to match avatar world mm
        Vector3 userEyeMm = userEyePosMeters * 1000f;

        // Gaze delta: FROM avatar eye TOWARD user eye
        Vector3 delta = userEyeMm - avatarEyeWorldMm;

        // Guard: delta too small → no meaningful direction, hold last value
        if (delta.LengthSquared() < 1f)
            return (_smoothedPitch, _smoothedYaw);

        Vector3 g = Vector3.Normalize(delta);

        // ── Pitch: vertical angle above the XZ plane ──────────
        // atan2(Y, horizontal_distance) — positive = looking up
        float horizontalDist = MathF.Sqrt(g.X * g.X + g.Z * g.Z);
        float rawPitch = MathF.Atan2(g.Y, horizontalDist);

        // ── Yaw: horizontal angle in Camera-Space XZ plane ────
        // atan2(X, |Z|) — positive = gaze pointing right (+X) → avatar looks right
        float rawYaw = MathF.Atan2(g.X, MathF.Abs(g.Z));

        // ── EMA smoothing ─────────────────────────────────────
        if (!_initialized)
        {
            _smoothedPitch = rawPitch;
            _smoothedYaw = rawYaw;
            _initialized = true;
        }
        else
        {
            _smoothedPitch += SmoothingAlpha * (rawPitch - _smoothedPitch);
            _smoothedYaw += SmoothingAlpha * (rawYaw - _smoothedYaw);
        }

        // Apply vertical correction AFTER smoothing so offset is stable.
        float correctedPitch = _smoothedPitch + VerticalCorrectionRad;

        return (correctedPitch, _smoothedYaw);
    }

    /// <summary>
    /// Resets the EMA smoothing filter.
    /// Call when tracking is lost or the saccade target changes significantly.
    /// </summary>
    public void Reset()
    {
        _initialized = false;
        _smoothedPitch = 0f;
        _smoothedYaw = 0f;
    }
}
