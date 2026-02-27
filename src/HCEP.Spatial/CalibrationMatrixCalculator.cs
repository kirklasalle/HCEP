// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;

namespace HCEP.Spatial;

/// <summary>
/// Solves the Gaze Parallax Problem for a camera mounted above (or offset from)
/// the display plane.
///
/// The Kinect sensor's optical axis does not coincide with the screen's optical
/// axis.  A raw gaze vector computed from camera-space data therefore points
/// slightly wrong when the avatar must look at the user's *actual eye sockets
/// relative to the screen*.  This class precomputes the delta-angle transform
/// (Δpitch, Δyaw) and exposes methods to apply it in real-time.
///
/// ── Coordinate convention ─────────────────────────────────────
///   +X = right,  +Y = up,  +Z = away from sensor (depth)
///   All offsets and screen dimensions are in millimetres.
/// </summary>
public sealed class CalibrationMatrixCalculator
{
    // ── Mount geometry ─────────────────────────────────────────

    /// <summary>
    /// Physical offset of the Kinect sensor from the screen centre (mm).
    ///   X: horizontal — positive = Kinect is to the right of screen centre.
    ///   Y: vertical   — positive = Kinect is above screen centre.
    ///   Z: depth       — positive = Kinect protrudes forward of screen surface.
    /// Typical real-world value: (0, +120, +30) — centred horizontally,
    /// 120 mm above screen centre, 30 mm in front of bezel.
    /// </summary>
    public Vector3 KinectOffsetFromScreenCentreMm { get; }

    /// <summary>Screen width in millimetres (physical panel dimension).</summary>
    public float ScreenWidthMm { get; }

    /// <summary>Screen height in millimetres (physical panel dimension).</summary>
    public float ScreenHeightMm { get; }

    // ── Derived delta angles ───────────────────────────────────

    /// <summary>
    /// Pre-computed delta-yaw: the horizontal angle (radians) between the Kinect
    /// optical axis and the screen optical axis — positive = screen centre is to
    /// the LEFT of the camera's forward direction.
    /// </summary>
    public float DeltaYawRad { get; private set; }

    /// <summary>
    /// Pre-computed delta-pitch: the vertical angle (radians) between the Kinect
    /// optical axis and the screen optical axis — positive = screen centre is
    /// BELOW the camera's forward direction.
    /// </summary>
    public float DeltaPitchRad { get; private set; }

    // ── Pre-computed rotation matrix ───────────────────────────

    private Matrix4x4 _calibrationMatrix;

    // ── Construction ───────────────────────────────────────────

    /// <summary>
    /// Initialises the calculator with the physical geometry of the rig.
    /// </summary>
    /// <param name="kinectOffsetFromScreenCentreMm">
    /// Vector from the screen centre to the Kinect lens, in mm.
    /// Positive Y means the Kinect sits above screen centre.
    /// </param>
    /// <param name="screenWidthMm">Physical screen width in mm.</param>
    /// <param name="screenHeightMm">Physical screen height in mm.</param>
    public CalibrationMatrixCalculator(
        Vector3 kinectOffsetFromScreenCentreMm,
        float screenWidthMm,
        float screenHeightMm)
    {
        KinectOffsetFromScreenCentreMm = kinectOffsetFromScreenCentreMm;
        ScreenWidthMm = screenWidthMm;
        ScreenHeightMm = screenHeightMm;

        ComputeDeltaAngles();
    }

    // ── Core math ──────────────────────────────────────────────

    /// <summary>
    /// Bootstrap initialisation — bakes the calibration matrix using the
    /// Kinect's physical forward protrusion (Z offset) as a static depth.
    /// Called automatically by the constructor.  Prefer the per-frame
    /// <see cref="ApplyCalibration(Vector3, float)"/> overload at runtime.
    /// </summary>
    public void ComputeDeltaAngles()
    {
        float fallbackDepthMm = KinectOffsetFromScreenCentreMm.Z > 10f
            ? KinectOffsetFromScreenCentreMm.Z
            : 600f;
        ComputeDynamicMatrix(fallbackDepthMm);
    }

    /// <summary>
    /// Per-frame core: recomputes Δpitch, Δyaw and the calibration matrix
    /// using the user's <em>live</em> working distance.
    /// </summary>
    /// <param name="userDepthMm">
    /// Distance from the Kinect sensor to the user's eye sockets in mm
    /// (use <c>FaceFrame.HeadTranslation.Z</c>).
    /// </param>
    private void ComputeDynamicMatrix(float userDepthMm)
    {
        var o = KinectOffsetFromScreenCentreMm;

        // Vector from the Kinect to the screen centre
        float dx = -o.X;   // lateral component
        float dy = -o.Y;   // vertical component

        // Use live user depth as the angular denominator so the parallax
        // correction scales correctly at every working distance.
        float depth = userDepthMm > 10f ? userDepthMm : 600f;

        // Δyaw   = angle about Y axis  (horizontal correction)
        // Δpitch = angle about X axis  (vertical correction)
        DeltaYawRad = MathF.Atan2(dx, depth);
        DeltaPitchRad = MathF.Atan2(dy, depth);

        // Build Y-then-X rotation matrix
        _calibrationMatrix =
            Matrix4x4.CreateRotationY(DeltaYawRad) *
            Matrix4x4.CreateRotationX(DeltaPitchRad);
    }

    /// <summary>
    /// <b>Primary per-frame API.</b>  Recomputes the parallax correction using
    /// the user's live working distance and applies it to the raw gaze vector.
    /// </summary>
    /// <param name="rawGazeDirection">
    /// Unit vector in Kinect camera space (output from
    /// <see cref="ThreeStageGazeEstimator"/>).
    /// </param>
    /// <param name="userDepthMm">
    /// Distance from the sensor to the user's eye sockets in mm.
    /// Use <c>FaceFrame.HeadTranslation.Z</c>.
    /// </param>
    /// <returns>
    /// Parallax-corrected gaze direction relative to the screen optical axis.
    /// Guaranteed normalised.
    /// </returns>
    public Vector3 ApplyCalibration(Vector3 rawGazeDirection, float userDepthMm)
    {
        ComputeDynamicMatrix(userDepthMm);
        return Vector3.Normalize(Vector3.TransformNormal(rawGazeDirection, _calibrationMatrix));
    }

    /// <summary>
    /// Convenience overload — applies the last baked matrix without recomputing.
    /// Use only when <c>userDepthMm</c> is unavailable; prefer the depth overload
    /// in the hot path.
    /// </summary>
    public Vector3 ApplyCalibration(Vector3 rawGazeDirection)
    {
        return Vector3.Normalize(Vector3.TransformNormal(rawGazeDirection, _calibrationMatrix));
    }

    /// <summary>
    /// Converts a corrected gaze direction into a normalised screen UV
    /// coordinate [0..1, 0..1] assuming the screen is at the specified
    /// working distance.
    /// </summary>
    /// <param name="calibratedDirection">Output of <see cref="ApplyCalibration"/>.</param>
    /// <param name="userDepthMm">
    /// The distance from the sensor to the user's eye sockets in mm
    /// (typically the Z component of HeadTranslation from <see cref="HCEP.Core.Models.FaceFrame"/>).
    /// </param>
    /// <returns>
    /// Screen UV in [0..1]; (0,0) = top-left, (1,1) = bottom-right.
    /// Returns <c>null</c> if the ray is parallel to / behind the screen.
    /// </returns>
    public Vector2? ProjectToScreenUv(Vector3 calibratedDirection, float userDepthMm)
    {
        if (calibratedDirection.Z <= 0f) return null;

        // Ray-plane intersection: screen plane is at Z = userDepthMm
        float t = userDepthMm / calibratedDirection.Z;
        float hitX = calibratedDirection.X * t;   // mm from screen optical axis
        float hitY = calibratedDirection.Y * t;   // mm from screen optical axis

        // Map to UV: centre of screen = (0.5, 0.5)
        float u = hitX / ScreenWidthMm + 0.5f;
        float v = hitY / ScreenHeightMm + 0.5f;   // V increases downward on screen

        // Clamp — gaze can land outside screen bounds and that is valid data,
        // but callers may wish to clamp themselves.
        return new Vector2(u, v);
    }

    // ── Diagnostics ────────────────────────────────────────────

    /// <summary>
    /// Returns the delta angles in degrees for logging / UI display.
    /// </summary>
    public (float YawDeg, float PitchDeg) GetDeltaAnglesDegrees() =>
        (DeltaYawRad * 180f / MathF.PI,
         DeltaPitchRad * 180f / MathF.PI);

    /// <inheritdoc />
    public override string ToString() =>
        $"CalibrationMatrix | Δyaw={DeltaYawRad * 180f / MathF.PI:F2}° " +
        $"Δpitch={DeltaPitchRad * 180f / MathF.PI:F2}° | " +
        $"Offset={KinectOffsetFromScreenCentreMm} mm";
}
