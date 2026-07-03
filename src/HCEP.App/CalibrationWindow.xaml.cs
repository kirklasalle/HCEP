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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Full-screen calibration overlay that computes the physical 3D offset between
/// the Kinect sensor's optical origin and the monitor's centre point.
///
/// ── Calibration Protocol ──────────────────────────────────────
/// 1. The user sits at their normal working distance and focuses their eyes on
///    the crosshair, which is rendered at the exact pixel-centre of this window
///    (maximised to cover the entire display, including the taskbar).
/// 2. Pressing SPACE captures the current Kinect FaceTracking data:
///      • HeadTranslation  — user's head centre in Kinect Camera Space (mm).
///      • HeadRotation     — (Pitch, Yaw, Roll) in degrees.
/// 3. The gaze direction unit-vector G is derived from Pitch/Yaw.
/// 4. The screen surface is assumed to sit at Z = −KinectOffsetZMm in Camera
///    Space (the Kinect protrudes forward of the bezel by KinectOffsetZMm mm,
///    so the screen face is slightly behind the sensor origin).
/// 5. Solving the ray-plane intersection:
///      t            = (screenSurfaceZ − headZ) / G.Z
///      screenCentreX = headX + t·G.X
///      screenCentreY = headY + t·G.Y
/// 6. The resulting KinectToScreenOriginOffset is written back to
///    HCEPPipelineOrchestrator via ApplyCalibration().
///
/// ── Coordinate Convention ─────────────────────────────────────
///   +X = right, +Y = up, +Z = away from Kinect sensor (toward user).
///   User head at large +Z (~800–1500 mm). Screen face at small negative Z.
///
/// ── Sign Convention for Head Rotation (Kinect SDK v1) ─────────
///   Pitch > 0 → face tilted back (chin up / looking up).
///   Yaw   > 0 → face turned left (user's perspective).
///   If the computed offset is obviously inverted after a test run, flip
///   PitchSign or YawSign constants below.
/// </summary>
public partial class CalibrationWindow : Window
{
    // ── Kinect rotation sign constants (flip if calibration result is mirrored)
    private const float PitchSign = 1f;   // +1 ⟹ positive pitch = looking up
    private const float YawSign = 1f;   // +1 ⟹ positive yaw   = turning left

    // ── Fixed Z-depth of the screen surface in Camera Space ────
    // Equals the physical protrusion of the Kinect forward of the screen bezel.
    // This is the one dimension we keep as a known constant; the calibration
    // derives X and Y empirically.
    private const float DefaultScreenSurfaceOffsetZMm = 30f;

    // ── Dependencies ────────────────────────────────────────────
    private readonly HCEPPipelineOrchestrator _orchestrator;

    // ── Live update timer ───────────────────────────────────────
    private readonly DispatcherTimer _refreshTimer;

    // ── Last computed calibration (updated each tick as preview) ─
    private float _previewOffsetXMm;
    private float _previewOffsetYMm;
    private bool _previewValid;

    public CalibrationWindow(HCEPPipelineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(66) // ~15 fps UI update
        };
        _refreshTimer.Tick += OnRefreshTick;
    }

    // ── Window lifecycle ─────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionCrosshair();
        SizeChanged += (_, _) => PositionCrosshair();
        _refreshTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }

    // ── Crosshair layout ─────────────────────────────────────────

    /// <summary>
    /// Positions all Crosshair canvas children relative to the window centre.
    /// Called once on Loaded; also hooking SizeChanged would handle unlikely
    /// resize events.
    /// </summary>
    private void PositionCrosshair()
    {
        double cx = CrosshairCanvas.ActualWidth / 2.0;
        double cy = CrosshairCanvas.ActualHeight / 2.0;

        // Full-width/-height dim guide lines
        HLine.X1 = 0; HLine.Y1 = cy; HLine.X2 = CrosshairCanvas.ActualWidth; HLine.Y2 = cy;
        VLine.X1 = cx; VLine.Y1 = 0; VLine.X2 = cx; VLine.Y2 = CrosshairCanvas.ActualHeight;

        // Outer target ring (80×80)
        Canvas.SetLeft(TargetRing, cx - 40);
        Canvas.SetTop(TargetRing, cy - 40);

        // Inner target ring (20×20)
        Canvas.SetLeft(TargetInner, cx - 10);
        Canvas.SetTop(TargetInner, cy - 10);

        // Centre dot (6×6)
        Canvas.SetLeft(TargetDot, cx - 3);
        Canvas.SetTop(TargetDot, cy - 3);

        // Tick marks (from 46 px to 56 px from centre — just outside outer ring)
        double tickInner = 46, tickOuter = 56;
        TickTop.X1 = cx; TickTop.Y1 = cy - tickOuter; TickTop.X2 = cx; TickTop.Y2 = cy - tickInner;
        TickBottom.X1 = cx; TickBottom.Y1 = cy + tickInner; TickBottom.X2 = cx; TickBottom.Y2 = cy + tickOuter;
        TickLeft.X1 = cx - tickOuter; TickLeft.Y1 = cy; TickLeft.X2 = cx - tickInner; TickLeft.Y2 = cy;
        TickRight.X1 = cx + tickInner; TickRight.Y1 = cy; TickRight.X2 = cx + tickOuter; TickRight.Y2 = cy;
    }

    // ── Live update ───────────────────────────────────────────────

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        var face = _orchestrator.LatestFaceFrame;

        if (face == null || !face.IsTracked)
        {
            TrackingStatusText.Text = "NOT TRACKED";
            TrackingStatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            HeadXText.Text = HeadYText.Text = HeadZText.Text = "—";
            PitchText.Text = YawText.Text = "—";
            _previewValid = false;
            StatusText.Text = "Waiting for face tracking — ensure the Kinect is running and you are visible.";
            return;
        }

        // ── Update live readout ──────────────────────────────────
        TrackingStatusText.Text = "TRACKED ✓";
        TrackingStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;

        var h = face.HeadTranslation;
        HeadXText.Text = $"{h.X:+0.0;-0.0;0.0} mm";
        HeadYText.Text = $"{h.Y:+0.0;-0.0;0.0} mm";
        HeadZText.Text = $"{h.Z:0.0} mm";
        PitchText.Text = $"{face.HeadRotation.X:+0.0;-0.0;0.0}°";
        YawText.Text = $"{face.HeadRotation.Y:+0.0;-0.0;0.0}°";
        WorkingDistText.Text = $"{h.Z:0.0} mm";

        // ── Compute live preview of calibrated offset ────────────
        ComputeOffset(face, out float offsetX, out float offsetY, out bool valid);
        _previewOffsetXMm = offsetX;
        _previewOffsetYMm = offsetY;
        _previewValid = valid;

        if (valid)
        {
            OffsetXText.Text = $"{offsetX:+0.0;-0.0;0.0} mm";
            OffsetYText.Text = $"{offsetY:+0.0;-0.0;0.0} mm";
            OffsetZText.Text = $"{DefaultScreenSurfaceOffsetZMm:0.0} mm";
            StatusText.Text = "Face tracked — press SPACE to capture calibration.";
        }
        else
        {
            OffsetXText.Text = OffsetYText.Text = OffsetZText.Text = "⚠ invalid";
            StatusText.Text = "Gaze direction too shallow — face the Kinect more directly.";
        }
    }

    // ── Calibration math ──────────────────────────────────────────

    /// <summary>
    /// Derives the Kinect-to-screen-centre offset from a single face frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user is looking at the screen centre (crosshair).  Their head
    /// position in Camera Space is <c>H</c>, and their gaze direction <c>G</c>
    /// is derived from HeadRotation Pitch &amp; Yaw.
    /// </para>
    /// <para>
    /// The screen surface lies at <c>Z = −KinectOffsetZMm</c> in Camera Space.
    /// We ray-cast: find scalar <c>t</c> such that <c>H + t·G</c> hits that
    /// Z-plane, giving the screen-centre position <c>S</c>.  Then:
    ///   <c>KinectOffsetX = −S.X,  KinectOffsetY = −S.Y</c>.
    /// </para>
    /// </remarks>
    private static void ComputeOffset(
        FaceFrame face,
        out float offsetXMm,
        out float offsetYMm,
        out bool valid)
    {
        offsetXMm = 0f;
        offsetYMm = 0f;
        valid = false;

        float pitchRad = face.HeadRotation.X * PitchSign * (MathF.PI / 180f);
        float yawRad = face.HeadRotation.Y * YawSign * (MathF.PI / 180f);

        // Gaze unit-vector: looking straight at Kinect = (0, 0, -1) in Camera Space.
        // Rotating that base vector by Pitch (X-axis) then Yaw (Y-axis):
        //   G.X =  sin(yaw)           (positive yaw → turning left → gaze shifts +X toward right side of camera view)
        //   G.Y =  sin(pitch)·cos(yaw)
        //   G.Z = -cos(pitch)·cos(yaw)  (negative: looking toward sensor = -Z)
        var gazeDir = new Vector3(
             MathF.Sin(yawRad),
             MathF.Sin(pitchRad) * MathF.Cos(yawRad),
            -MathF.Cos(pitchRad) * MathF.Cos(yawRad));

        // Guard: gaze Z component must be sufficiently negative (user must face camera).
        // If gazeDir.Z is near zero the ray is parallel to the screen plane.
        if (MathF.Abs(gazeDir.Z) < 0.1f) return;

        var head = face.HeadTranslation; // mm, Camera Space

        // Screen surface Z in Camera Space (screen face is slightly behind sensor origin).
        float screenZ = -DefaultScreenSurfaceOffsetZMm;

        // Ray–plane intersection: head + t·gazeDir = (?, ?, screenZ)
        float t = (screenZ - head.Z) / gazeDir.Z;

        // Guard: t must be negative (screen is between sensor and user, so the
        // ray goes backward from the user's head toward the sensor).
        if (t >= 0f) return;

        float screenCentreX = head.X + t * gazeDir.X;
        float screenCentreY = head.Y + t * gazeDir.Y;

        // KinectOffsetFromScreenCentre = −screenCentrePosition
        // (CalibrationMatrixCalculator convention: offset = "vector from screen centre to Kinect").
        offsetXMm = -screenCentreX;
        offsetYMm = -screenCentreY;
        valid = true;
    }

    // ── Input handling ────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                CaptureCalibration();
                break;

            case Key.Escape:
                StatusText.Text = "Calibration cancelled — no changes saved.";
                _refreshTimer.Stop();
                // Brief pause so user reads the message, then close.
                var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                cancel.Tick += (_, _) => { cancel.Stop(); Close(); };
                cancel.Start();
                break;
        }
    }

    // ── Capture & save ────────────────────────────────────────────

    private void CaptureCalibration()
    {
        var face = _orchestrator.LatestFaceFrame;

        if (face == null || !face.IsTracked)
        {
            StatusText.Text = "⚠ No face tracked — cannot capture. Ensure the Kinect sees your face.";
            return;
        }

        ComputeOffset(face, out float offsetX, out float offsetY, out bool valid);

        if (!valid)
        {
            StatusText.Text = "⚠ Gaze direction invalid — face the Kinect more directly and try again.";
            return;
        }

        // ── Apply to live pipeline ───────────────────────────────
        _orchestrator.ApplyCalibration(
            kinectOffsetXMm: offsetX,
            kinectOffsetYMm: offsetY,
            kinectOffsetZMm: DefaultScreenSurfaceOffsetZMm);

        StatusText.Text =
            $"✓ Calibration saved — X={offsetX:+0.0;-0.0;0.0} mm  " +
            $"Y={offsetY:+0.0;-0.0;0.0} mm  " +
            $"Z={DefaultScreenSurfaceOffsetZMm:0.0} mm";

        _refreshTimer.Stop();

        // Close after a short confirmation pause.
        var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        done.Tick += (_, _) => { done.Stop(); Close(); };
        done.Start();
    }
}
