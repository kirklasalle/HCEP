// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Real-time visualization panel showing:
///   • Interlocutor face schematic with labeled regions
///   • Live gaze target crosshair
///   • Mini skeleton wireframe
///   • Head pose indicator
///   • Tracking / identity status
/// Renders directly via OnRender for minimal allocation.
/// </summary>
public sealed class GazeVisualizationControl : FrameworkElement
{
    // ── Dependency Properties ──────────────────────────────────

    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(nameof(Snapshot), typeof(SceneSnapshot),
            typeof(GazeVisualizationControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SceneSnapshot? Snapshot
    {
        get => (SceneSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    // ── Constants ──────────────────────────────────────────────

    // Interlocutor face landmarks (normalized 0..1 within face oval)
    // X: 0 = left edge, 1 = right edge; Y: 0 = top, 1 = bottom
    private static readonly (string Label, double Nx, double Ny, GazeRegion Region)[] _faceRegions =
    [
        ("Forehead",   0.50, 0.18, GazeRegion.Forehead),
        ("L-Eye",      0.35, 0.38, GazeRegion.LeftEye),
        ("R-Eye",      0.65, 0.38, GazeRegion.RightEye),
        ("Bridge",     0.50, 0.42, GazeRegion.NasalBridge),
        ("Center",     0.50, 0.50, GazeRegion.FaceCenter),
        ("Mouth",      0.50, 0.70, GazeRegion.Mouth),
        ("Chin",       0.50, 0.85, GazeRegion.Chin),
    ];

    // Brushes (frozen for thread safety)
    private static readonly Pen _facePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)), 1.5));
    private static readonly Pen _regionPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(100, 148, 163, 184)), 1.0));
    private static readonly Brush _bgBrush = Freeze(new SolidColorBrush(Color.FromRgb(30, 30, 46)));
    private static readonly Brush _faceOvalFill = Freeze(new SolidColorBrush(Color.FromArgb(30, 124, 58, 237)));
    private static readonly Brush _textDim = Freeze(new SolidColorBrush(Color.FromArgb(160, 148, 163, 184)));
    private static readonly Brush _textBright = Freeze(new SolidColorBrush(Color.FromRgb(226, 232, 240)));
    private static readonly Brush _accentBrush = Freeze(new SolidColorBrush(Color.FromRgb(6, 182, 212)));
    private static readonly Brush _primaryBrush = Freeze(new SolidColorBrush(Color.FromRgb(124, 58, 237)));
    private static readonly Brush _successBrush = Freeze(new SolidColorBrush(Color.FromRgb(34, 197, 94)));
    private static readonly Brush _warningBrush = Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11)));
    private static readonly Brush _errorBrush = Freeze(new SolidColorBrush(Color.FromRgb(239, 68, 68)));
    private static readonly Brush _gazeDotBrush = Freeze(new SolidColorBrush(Color.FromArgb(220, 6, 182, 212)));
    private static readonly Brush _highlightBrush = Freeze(new SolidColorBrush(Color.FromArgb(60, 6, 182, 212)));
    private static readonly Pen _gazePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(200, 6, 182, 212)), 2.0));
    private static readonly Pen _skeletonPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(180, 124, 58, 237)), 2.0));
    private static readonly Pen _headPosePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(180, 245, 158, 11)), 2.0));
    private static readonly Pen _highlightPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(140, 6, 182, 212)), 2.0));
    private static readonly Pen _conePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)), 1.0) { DashStyle = DashStyles.Dash });
    private static readonly Brush _modeBarInactiveBg = Freeze(new SolidColorBrush(Color.FromArgb(30, 60, 60, 80)));
    private static readonly Brush _regionInactiveFill = Freeze(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)));
    private static readonly Pen _separatorPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)), 0.5));
    private static readonly Brush _auBarTrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)));

    private static readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface _typefaceBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    // Mode colors
    private static readonly Dictionary<HcepMode, Brush> _modeColors = new()
    {
        [HcepMode.Unknown] = _textDim,
        [HcepMode.Logic] = Freeze(new SolidColorBrush(Color.FromRgb(96, 165, 250))),   // blue
        [HcepMode.Affect] = Freeze(new SolidColorBrush(Color.FromRgb(251, 191, 36))),    // amber
        [HcepMode.Spirit] = Freeze(new SolidColorBrush(Color.FromRgb(167, 139, 250))),   // violet
        [HcepMode.Heart] = Freeze(new SolidColorBrush(Color.FromRgb(251, 113, 133))),   // rose
        [HcepMode.Think] = Freeze(new SolidColorBrush(Color.FromRgb(74, 222, 128))),    // green
    };

    // ── Rendering ──────────────────────────────────────────────

    private double _cachedPixelsPerDip;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 10 || bounds.Height < 10) return;

        // Cache DPI once per render pass
        try
        {
            Visual visual = (Visual?)Application.Current?.MainWindow ?? this;
            _cachedPixelsPerDip = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        }
        catch
        {
            _cachedPixelsPerDip = 1.0; // safe fallback
        }

        try
        {
            RenderCore(dc, bounds);
        }
        catch (Exception ex)
        {
            // Render a crash indicator so the app doesn't die
            try
            {
                dc.DrawRoundedRectangle(_bgBrush, null, bounds, 8, 8);
                DrawText(dc, $"Render error: {ex.Message}", 10, bounds.Height / 2 - 10, 11, _errorBrush);
            }
            catch { /* absolute last resort */ }
        }
    }

    private void RenderCore(DrawingContext dc, Rect bounds)
    {

        // Background
        dc.DrawRoundedRectangle(_bgBrush, null, bounds, 8, 8);

        var snapshot = Snapshot;
        var person = snapshot?.PrimaryPerson;
        var hcep = person?.LatestHcep;
        var face = person?.Face;

        // Panel title
        DrawText(dc, "SENSOR VIEW", 10, 8, 11, _textDim, bold: true);

        if (person is null || hcep is null)
        {
            // Show useful state information instead of just "Waiting..."
            string statusMsg = person is null
                ? "Connected — waiting for person detection..."
                : "Person detected — HCEP analysis starting...";
            DrawText(dc, statusMsg, bounds.Width / 2 - 120, bounds.Height / 2 - 20, 12, _textDim);

            // If we have a face but no HCEP, still show face data
            if (face is not null)
            {
                DrawText(dc, $"Face tracked: yaw={face.HeadRotation.Y:F1}° pitch={face.HeadRotation.X:F1}°",
                    bounds.Width / 2 - 120, bounds.Height / 2 + 4, 10, _textDim);
            }
            if (person?.JointPositions is { Count: > 0 })
            {
                DrawText(dc, $"Skeleton: {person.JointPositions.Count} joints at {person.DistanceM:F2}m",
                    bounds.Width / 2 - 120, bounds.Height / 2 + 20, 10, _textDim);
            }
            return;
        }

        // Layout zones — vertical stack: face schematic on top, info panel below
        double margin = 8;
        double faceAreaLeft = margin;
        double faceAreaTop = 28;
        double faceAreaWidth = bounds.Width - margin * 2;
        double faceAreaHeight = (bounds.Height - 36) * 0.50;

        double infoAreaTop = faceAreaTop + faceAreaHeight + 4;
        double infoAreaLeft = margin;
        double infoAreaWidth = bounds.Width - margin * 2;
        double infoAreaHeight = bounds.Height - infoAreaTop - 4;

        // ── Draw interlocutor face schematic ───────────────────────
        DrawFaceSchematic(dc, faceAreaLeft, faceAreaTop, faceAreaWidth, faceAreaHeight, hcep, face);

        // ── Draw info panel (2-column) ─────────────────────
        DrawInfoPanel(dc, infoAreaLeft, infoAreaTop, infoAreaWidth, infoAreaHeight, person, hcep, face);
    }

    // ── Face Schematic ─────────────────────────────────────────

    private void DrawFaceSchematic(DrawingContext dc, double left, double top,
        double width, double height, HcepReading hcep, FaceFrame? face)
    {
        // Face oval — sized to contain all feature regions
        double cx = left + width * 0.5;
        double cy = top + height * 0.48;
        double rx = Math.Min(width * 0.42, height * 0.38);
        double ry = rx * 1.35;

        dc.DrawEllipse(_faceOvalFill, _facePen, new System.Windows.Point(cx, cy), rx, ry);

        // Region labels + dots — positioned relative to oval center
        foreach (var (label, nx, ny, region) in _faceRegions)
        {
            double px = cx + (nx - 0.5) * rx * 2;
            double py = cy + (ny - 0.5) * ry * 2;

            bool isActive = hcep.Region == region;

            double dotR = isActive ? 6 : 3;
            Brush fill = isActive ? _highlightBrush : _regionInactiveFill;
            dc.DrawEllipse(fill, isActive ? _highlightPen : _regionPen,
                new System.Windows.Point(px, py), dotR, dotR);

            var labelBrush = isActive ? _accentBrush : _textDim;
            double fontSize = isActive ? 9 : 8;
            DrawText(dc, label, px - 16, py + dotR + 1, fontSize, labelBrush);
        }

        // Eye details — inside the oval
        double eyeW = rx * 0.28;
        double eyeH = ry * 0.09;
        double leftEyeX = cx - rx * 0.30;
        double rightEyeX = cx + rx * 0.30;
        double eyeY = cy - ry * 0.24;

        dc.DrawEllipse(null, _facePen,
            new System.Windows.Point(leftEyeX, eyeY), eyeW, eyeH);
        dc.DrawEllipse(null, _facePen,
            new System.Windows.Point(rightEyeX, eyeY), eyeW, eyeH);

        // Pupils
        double pupilR = eyeH * 0.4;
        dc.DrawEllipse(_textBright, null,
            new System.Windows.Point(leftEyeX, eyeY), pupilR, pupilR);
        dc.DrawEllipse(_textBright, null,
            new System.Windows.Point(rightEyeX, eyeY), pupilR, pupilR);

        // Nose bridge line
        double noseTop = cy - ry * 0.06;
        double noseBot = cy + ry * 0.14;
        dc.DrawLine(_facePen, new System.Windows.Point(cx, noseTop), new System.Windows.Point(cx, noseBot));
        dc.DrawLine(_facePen, new System.Windows.Point(cx - 5, noseBot), new System.Windows.Point(cx + 5, noseBot));

        // Mouth arc
        double mouthY = cy + ry * 0.40;
        double mouthW = rx * 0.35;
        var mouthGeo = new StreamGeometry();
        using (var ctx = mouthGeo.Open())
        {
            ctx.BeginFigure(new System.Windows.Point(cx - mouthW, mouthY), false, false);
            ctx.QuadraticBezierTo(
                new System.Windows.Point(cx, mouthY + 6),
                new System.Windows.Point(cx + mouthW, mouthY),
                true, true);
        }
        mouthGeo.Freeze();
        dc.DrawGeometry(null, _facePen, mouthGeo);

        // Gaze crosshair / target
        DrawGazeCrosshair(dc, left, top, width, height, hcep);

        // Mode indicator bar at bottom
        DrawModeBar(dc, left, top + height - 22, width, hcep);
    }

    private void DrawGazeCrosshair(DrawingContext dc, double left, double top,
        double width, double height, HcepReading hcep)
    {
        // Map gaze intersection point to canvas coordinates.
        // GazeDirection is normalized; IntersectionPoint (on HcepReading) gives
        // only Origin + Direction. We'll use the GazeRegion to plot a rough position,
        // and use head pose yaw/pitch for fine positioning within the face area.

        // Convert head pose yaw/pitch to a face-relative position
        float yaw = hcep.HeadPose.Y;     // degrees, + = right
        float pitch = hcep.HeadPose.X;   // degrees, + = down (Kinect convention)

        // Map yaw [-30..+30] to face area X, pitch [-20..+20] to Y
        // Center = (0.5, 0.45) of face area
        double nx = 0.5 + (yaw / 60.0);
        double ny = 0.45 + (pitch / 40.0);

        // Clamp
        nx = Math.Clamp(nx, 0.05, 0.95);
        ny = Math.Clamp(ny, 0.05, 0.95);

        double gx = left + width * nx;
        double gy = top + height * ny;

        // Confidence cone circle
        double coneR = 20 + (1.0 - hcep.Confidence) * 30;
        dc.DrawEllipse(null, _conePen, new System.Windows.Point(gx, gy), coneR, coneR);

        // Crosshair lines
        double chLen = 12;
        dc.DrawLine(_gazePen, new System.Windows.Point(gx - chLen, gy), new System.Windows.Point(gx + chLen, gy));
        dc.DrawLine(_gazePen, new System.Windows.Point(gx, gy - chLen), new System.Windows.Point(gx, gy + chLen));

        // Center dot
        dc.DrawEllipse(_gazeDotBrush, null, new System.Windows.Point(gx, gy), 4, 4);

        // Region label near crosshair
        string regionLabel = hcep.Region.ToString();
        var modeBrush = _modeColors.GetValueOrDefault(hcep.Mode, _textBright);
        DrawText(dc, regionLabel, gx + 14, gy - 7, 10, modeBrush, bold: true);
    }

    private void DrawModeBar(DrawingContext dc, double left, double top, double width, HcepReading hcep)
    {
        // 5 mode segments
        var modes = new[] { HcepMode.Logic, HcepMode.Affect, HcepMode.Spirit, HcepMode.Heart, HcepMode.Think };
        double segW = width / modes.Length;

        for (int i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            bool active = hcep.Mode == mode;

            double sx = left + i * segW;
            var rect = new Rect(sx + 1, top, segW - 2, 18);

            Brush bg = active
                ? _modeColors.GetValueOrDefault(mode, _textDim)
                : _modeBarInactiveBg;
            Brush fg = active ? _bgBrush : _textDim;

            dc.DrawRoundedRectangle(bg, null, rect, 3, 3);
            DrawText(dc, mode.ToString().ToUpperInvariant(), sx + 4, top + 2, active ? 10 : 9, fg, active);
        }
    }

    // ── Info Panel (2-column) ────────────────────────────────

    private void DrawInfoPanel(DrawingContext dc, double left, double top,
        double width, double height, TrackedPerson person, HcepReading hcep, FaceFrame? face)
    {
        // Subtle separator line
        dc.DrawLine(_separatorPen,
            new System.Windows.Point(left, top), new System.Windows.Point(left + width, top));

        double lineH = 16;
        double colGap = 16;

        // Two-column layout
        double colW = (width - colGap) / 2.0;
        double colLeftX = left;
        double colRightX = left + colW + colGap;

        // ── LEFT COLUMN: Tracking + Head Pose + Action Units ──
        double yL = top + 6;

        DrawText(dc, "TRACKING", colLeftX, yL, 9, _textDim, bold: true);
        yL += lineH;

        string identity = person.IdentityName ?? "Unknown";
        var idBrush = person.IdentityName is not null ? _successBrush : _warningBrush;
        DrawText(dc, $"Identity: {identity}", colLeftX, yL, 10, idBrush);
        yL += lineH;

        DrawText(dc, $"ID: {person.TrackingId}  {person.State}", colLeftX, yL, 9, _textDim);
        yL += lineH;

        DrawText(dc, $"Distance: {person.DistanceM:F2}m", colLeftX, yL, 9, _textDim);
        yL += lineH * 1.3;

        if (face is not null)
        {
            DrawText(dc, "HEAD POSE", colLeftX, yL, 9, _textDim, bold: true);
            yL += lineH;

            DrawText(dc, $"Pitch: {face.HeadRotation.X:F1}°  Yaw: {face.HeadRotation.Y:F1}°  Roll: {face.HeadRotation.Z:F1}°",
                colLeftX, yL, 10, _textBright);
            yL += lineH * 1.3;

            DrawText(dc, "ACTION UNITS", colLeftX, yL, 9, _textDim, bold: true);
            yL += lineH;

            string[] auNames = ["UpperLip", "JawLow", "LipStr", "BrowLow", "LipCrnr", "OutBrow"];
            for (int i = 0; i < Math.Min(face.ActionUnits.Length, auNames.Length); i++)
            {
                if (yL > top + height - 8) break;

                float val = face.ActionUnits[i];
                DrawText(dc, $"{auNames[i]}: {val:+0.00;-0.00}", colLeftX, yL, 9, _textDim);

                double barLeft = colLeftX + 85;
                double barW = Math.Max(colW - 95, 10);
                double barH = 3;
                double barY = yL + 5;
                dc.DrawRoundedRectangle(_auBarTrackBrush, null,
                    new Rect(barLeft, barY, barW, barH), 2, 2);

                double fillW = Math.Max(0, Math.Abs(val) * barW);
                double fillX = val >= 0 ? barLeft + barW * 0.5 : barLeft + barW * 0.5 - fillW;
                var barColor = val >= 0 ? _accentBrush : _warningBrush;
                dc.DrawRoundedRectangle(barColor, null,
                    new Rect(fillX, barY, fillW, barH), 2, 2);

                yL += lineH;
            }
        }

        // ── RIGHT COLUMN: Gaze + Mini Skeleton ────────────────
        double yR = top + 6;

        DrawText(dc, "GAZE", colRightX, yR, 9, _textDim, bold: true);
        yR += lineH;

        var modeBrush = _modeColors.GetValueOrDefault(hcep.Mode, _textBright);
        DrawText(dc, $"Region: {hcep.Region}", colRightX, yR, 10, modeBrush);
        yR += lineH;
        DrawText(dc, $"Confidence: {hcep.Confidence:P0}", colRightX, yR, 10, _textBright);
        yR += lineH;

        var dir = hcep.GazeDirection;
        DrawText(dc, $"Dir: ({dir.X:F3}, {dir.Y:F3}, {dir.Z:F3})", colRightX, yR, 9, _textDim);
        yR += lineH * 1.3;

        // Mini skeleton in remaining right column space
        double skelRemaining = height - (yR - top);
        if (skelRemaining > 30)
        {
            DrawText(dc, "SKELETON", colRightX, yR, 9, _textDim, bold: true);
            yR += lineH;
            DrawMiniSkeleton(dc, colRightX, yR, colW, Math.Min(skelRemaining - lineH, 120), person);
        }
    }

    // ── Mini Skeleton ──────────────────────────────────────────

    private void DrawMiniSkeleton(DrawingContext dc, double left, double top,
        double width, double height, TrackedPerson person)
    {
        if (person.JointPositions is null || person.JointPositions.Count == 0)
        {
            DrawText(dc, "No skeleton data", left + 10, top + 10, 10, _textDim);
            return;
        }

        // Map 3D joint positions to 2D mini view
        // Joint indices: 0=HipCenter, 2=ShoulderCenter, 3=Head
        var joints = person.JointPositions;

        // Get available joints and map to 2D
        double cx = left + width * 0.5;
        double skelTop = top + 5;
        double scale = height * 0.8;

        System.Windows.Point? MapJoint(int idx)
        {
            if (!joints.TryGetValue(idx, out var j)) return null;
            // X: left-right, Y: up-down, in camera space (meters)
            double px = cx + j.X * scale * 2;
            double py = skelTop + (1.0 - (j.Y + 0.5) / 1.0) * scale;
            double clampMin = Math.Min(top, top + height);
            double clampMax = Math.Max(top, top + height);
            return new System.Windows.Point(px, Math.Clamp(py, clampMin, clampMax));
        }

        void DrawBone(int a, int b)
        {
            var pa = MapJoint(a);
            var pb = MapJoint(b);
            if (pa is not null && pb is not null)
                dc.DrawLine(_skeletonPen, pa.Value, pb.Value);
        }

        void DrawJointDot(int idx)
        {
            var p = MapJoint(idx);
            if (p is not null)
                dc.DrawEllipse(_primaryBrush, null, p.Value, 4, 4);
        }

        // Spine
        DrawBone(0, 2);  // HipCenter → ShoulderCenter
        DrawBone(2, 3);  // ShoulderCenter → Head

        // Draw joint dots
        foreach (var kvp in joints)
            DrawJointDot(kvp.Key);

        // Head circle
        var headPt = MapJoint(3);
        if (headPt is not null)
            dc.DrawEllipse(null, _skeletonPen, headPt.Value, 10, 10);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void DrawText(DrawingContext dc, string text, double x, double y,
        double fontSize, Brush brush, bool bold = false)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? _typefaceBold : _typeface,
            fontSize,
            brush,
            _cachedPixelsPerDip > 0 ? _cachedPixelsPerDip : 1.0);

        dc.DrawText(ft, new System.Windows.Point(x, y));
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
