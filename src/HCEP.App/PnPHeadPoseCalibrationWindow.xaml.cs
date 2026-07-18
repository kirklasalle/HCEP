// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Live visualisation of the head-pose Perspective-n-Point (PnP) solve.
///
/// <para>
/// Renders — on a fixed logical 640×480 canvas that scales to the window —
/// the six canonical face landmarks projected back through the current head
/// pose (yellow), the corresponding observed image points from the face
/// tracker (cyan), and the per-landmark reprojection residuals (red). Head
/// pose axes are drawn as R = X (yaw), G = Y (pitch), B = Z (roll).
/// </para>
///
/// <para>
/// All rendering runs on the WPF dispatcher timer at ~30 Hz; this window
/// only READS the orchestrator's <c>LatestFaceFrame</c> — it never mutates
/// pipeline state.
/// </para>
/// </summary>
public partial class PnPHeadPoseCalibrationWindow : Window
{
    private readonly HCEPPipelineOrchestrator _orchestrator;
    private readonly DispatcherTimer _timer;

    // Kinect v1 color intrinsics (matches VideoOverlayControl).
    private const double Fx = 525.0;
    private const double Fy = 525.0;
    private const double Cx = 320.0;
    private const double Cy = 240.0;

    // Canonical landmark indices (Kinect v1 FaceTrackLib 87-point space)
    // — best-effort, mirrored from HCEP.Core.Models.Anthropometrics.
    private const int NoseTipIdx = 5;
    private const int ChinIdx = 8;
    private const int LeftEyeOuterIdx = 20;
    private const int RightEyeOuterIdx = 11;
    private const int LeftMouthCornerIdx = 33;
    private const int RightMouthCornerIdx = 32;

    // 3D canonical face model (mm), origin at nose tip.
    private static readonly (int Idx, Vector3 P)[] Model = new (int, Vector3)[]
    {
        (NoseTipIdx,         new Vector3( 0.0f,   0.0f,   0.0f)),
        (ChinIdx,            new Vector3( 0.0f, -63.5f, -12.5f)),
        (LeftEyeOuterIdx,    new Vector3(-43.3f, 32.7f, -26.0f)),
        (RightEyeOuterIdx,   new Vector3( 43.3f, 32.7f, -26.0f)),
        (LeftMouthCornerIdx, new Vector3(-28.9f,-28.9f, -24.1f)),
        (RightMouthCornerIdx,new Vector3( 28.9f,-28.9f, -24.1f)),
    };

    public PnPHeadPoseCalibrationWindow(HCEPPipelineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnTick(object? sender, EventArgs e)
    {
        var face = _orchestrator.LatestFaceFrame;
        ClearCanvas();
        if (face is null || !face.IsTracked || face.FeaturePoints2D.Length == 0)
        {
            StatusText.Text = "Waiting for tracked face...";
            LandmarkCountText.Text = "0 / 6";
            PitchText.Text = YawText.Text = RollText.Text = "—";
            TranslationText.Text = "—";
            MeanReprojectionText.Text = "—";
            MaxReprojectionText.Text = "—";
            return;
        }

        // Live pose values come from FaceTrackLib.
        var rot = face.HeadRotation;   // (pitch, yaw, roll) degrees
        var tr = face.HeadTranslation; // (X, Y, Z) mm
        PitchText.Text = $"{rot.X:+0.0;-0.0;0.0}";
        YawText.Text = $"{rot.Y:+0.0;-0.0;0.0}";
        RollText.Text = $"{rot.Z:+0.0;-0.0;0.0}";
        TranslationText.Text = $"X={tr.X:0.0}  Y={tr.Y:0.0}  Z={tr.Z:0.0}";

        // Reproject each canonical model landmark and compare to observed 2D point.
        var rx = ToRad(rot.X); // pitch
        var ry = ToRad(rot.Y); // yaw
        var rz = ToRad(rot.Z); // roll
        var R = RotationMatrix(rx, ry, rz);

        int used = 0;
        double sumErr = 0, maxErr = 0;

        foreach (var (idx, p) in Model)
        {
            if (idx < 0 || idx >= face.FeaturePoints2D.Length) continue;
            var observed = face.FeaturePoints2D[idx];
            if (observed == Vector2.Zero) continue;

            // Rigid-body transform: cameraSpace = R * modelPoint + t
            var cam = Vector3.Transform(p, R) + tr;
            if (cam.Z <= 1) continue;

            // Pinhole projection into 640×480 image space.
            double u = Fx * cam.X / cam.Z + Cx;
            double v = -Fy * cam.Y / cam.Z + Cy; // Kinect Y is up; image Y is down.

            var projected = new Point(u, v);
            var observedPt = new Point(observed.X, observed.Y);
            var dx = projected.X - observedPt.X;
            var dy = projected.Y - observedPt.Y;
            var err = Math.Sqrt(dx * dx + dy * dy);
            sumErr += err;
            maxErr = Math.Max(maxErr, err);
            used++;

            DrawResidual(observedPt, projected);
            DrawDot(projected, Colors.Yellow, 5);
            DrawDot(observedPt, Color.FromRgb(0, 200, 255), 4);
        }

        LandmarkCountText.Text = $"{used} / {Model.Length}";
        if (used > 0)
        {
            StatusText.Text = "Tracking ✓";
            MeanReprojectionText.Text = $"{sumErr / used:F2}";
            MaxReprojectionText.Text = $"{maxErr:F2}";
        }
        else
        {
            StatusText.Text = "No usable landmarks";
            MeanReprojectionText.Text = "—";
            MaxReprojectionText.Text = "—";
        }

        // Draw pose axes at head centre.
        DrawPoseAxes(tr, R);
    }

    private void ClearCanvas()
    {
        // Preserve the two legend TextBlocks (first two children).
        while (PoseCanvas.Children.Count > 2)
            PoseCanvas.Children.RemoveAt(2);
    }

    private void DrawDot(Point p, Color color, double radius)
    {
        var e = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = new SolidColorBrush(color),
            IsHitTestVisible = false,
        };
        System.Windows.Controls.Canvas.SetLeft(e, p.X - radius);
        System.Windows.Controls.Canvas.SetTop(e, p.Y - radius);
        PoseCanvas.Children.Add(e);
    }

    private void DrawResidual(Point observed, Point projected)
    {
        var line = new Line
        {
            X1 = observed.X,
            Y1 = observed.Y,
            X2 = projected.X,
            Y2 = projected.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 60, 60)),
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
        };
        PoseCanvas.Children.Add(line);
    }

    private void DrawPoseAxes(Vector3 tr, Matrix4x4 r)
    {
        // Project head-centre origin.
        if (tr.Z <= 1) return;
        double u0 = Fx * tr.X / tr.Z + Cx;
        double v0 = -Fy * tr.Y / tr.Z + Cy;

        DrawAxis(tr, r, new Vector3(60, 0, 0), Color.FromRgb(255, 80, 80));   // R  (yaw)
        DrawAxis(tr, r, new Vector3(0, 60, 0), Color.FromRgb(80, 255, 120));  // G  (pitch)
        DrawAxis(tr, r, new Vector3(0, 0, 60), Color.FromRgb(80, 160, 255));  // B  (roll)
        DrawDot(new Point(u0, v0), Colors.White, 3);
    }

    private void DrawAxis(Vector3 tr, Matrix4x4 r, Vector3 axisEndMm, Color color)
    {
        var end = Vector3.Transform(axisEndMm, r) + tr;
        if (tr.Z <= 1 || end.Z <= 1) return;

        double u0 = Fx * tr.X / tr.Z + Cx;
        double v0 = -Fy * tr.Y / tr.Z + Cy;
        double u1 = Fx * end.X / end.Z + Cx;
        double v1 = -Fy * end.Y / end.Z + Cy;

        var line = new Line
        {
            X1 = u0,
            Y1 = v0,
            X2 = u1,
            Y2 = v1,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2.4,
            IsHitTestVisible = false,
        };
        PoseCanvas.Children.Add(line);
    }

    // ── Math helpers ───────────────────────────────────────────

    private static float ToRad(float deg) => deg * MathF.PI / 180f;

    /// <summary>
    /// Builds an intrinsic X → Y → Z rotation matrix
    /// (pitch about X, yaw about Y, roll about Z).
    /// </summary>
    private static Matrix4x4 RotationMatrix(float pitchRad, float yawRad, float rollRad)
    {
        var rx = Matrix4x4.CreateRotationX(pitchRad);
        var ry = Matrix4x4.CreateRotationY(yawRad);
        var rz = Matrix4x4.CreateRotationZ(rollRad);
        return rx * ry * rz;
    }
}
