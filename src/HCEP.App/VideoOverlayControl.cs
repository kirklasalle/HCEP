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
/// Transparent overlay drawn on top of the Kinect video feed.
/// Renders skeleton wireframe, face bounding box, and facial feature
/// point wireframe — matching the classic Kinect SDK demo visuals.
/// Uses OnRender for minimal allocation.
/// </summary>
public sealed class VideoOverlayControl : FrameworkElement
{
    // ── Dependency Properties ──────────────────────────────────

    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(nameof(Snapshot), typeof(SceneSnapshot),
            typeof(VideoOverlayControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SceneSnapshot? Snapshot
    {
        get => (SceneSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public static readonly DependencyProperty ShowFullSkeletonProperty =
        DependencyProperty.Register(nameof(ShowFullSkeleton), typeof(bool),
            typeof(VideoOverlayControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowFullSkeleton
    {
        get => (bool)GetValue(ShowFullSkeletonProperty);
        set => SetValue(ShowFullSkeletonProperty, value);
    }

    // ── Kinect v1 Color Camera Intrinsics (640×480) ────────────

    private const double Fx = 525.0;
    private const double Fy = 525.0;
    private const double Cx = 320.0;
    private const double Cy = 240.0;
    private const double ImageW = 640.0;
    private const double ImageH = 480.0;

    // ── Brushes & Pens (frozen) ────────────────────────────────

    private static readonly Pen _skeletonPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 255, 0)), 3.0));
    private static readonly Pen _inferredBonePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 255, 0)), 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen _faceRectPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 0)), 2.0));
    private static readonly Pen _faceWirePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 255, 128)), 1.0));
    private static readonly Brush _jointTracked = Freeze(new SolidColorBrush(Color.FromArgb(230, 0, 255, 0)));
    private static readonly Brush _jointInferred = Freeze(new SolidColorBrush(Color.FromArgb(120, 255, 255, 0)));
    private static readonly Brush _facePointBrush = Freeze(new SolidColorBrush(Color.FromArgb(200, 0, 255, 128)));
    private static readonly Brush _pupilBrush = Freeze(new SolidColorBrush(Color.FromArgb(255, 255, 0, 128)));
    private static readonly Typeface _typeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    // ── Kinect v1 Skeleton Bone Connections (20 joints, indices 0–19) ──

    private static readonly (int A, int B)[] _bones =
    [
        // Spine
        (0, 1),   // HipCenter → Spine
        (1, 2),   // Spine → ShoulderCenter
        (2, 3),   // ShoulderCenter → Head
        // Left arm
        (2, 4),   // ShoulderCenter → ShoulderLeft
        (4, 5),   // ShoulderLeft → ElbowLeft
        (5, 6),   // ElbowLeft → WristLeft
        (6, 7),   // WristLeft → HandLeft
        // Right arm
        (2, 8),   // ShoulderCenter → ShoulderRight
        (8, 9),   // ShoulderRight → ElbowRight
        (9, 10),  // ElbowRight → WristRight
        (10, 11), // WristRight → HandRight
        // Left leg
        (0, 12),  // HipCenter → HipLeft
        (12, 13), // HipLeft → KneeLeft
        (13, 14), // KneeLeft → AnkleLeft
        (14, 15), // AnkleLeft → FootLeft
        // Right leg
        (0, 16),  // HipCenter → HipRight
        (16, 17), // HipRight → KneeRight
        (17, 18), // KneeRight → AnkleRight
        (18, 19), // AnkleRight → FootRight
    ];

    // ── FaceTrackLib 87-Point Feature Edge Chains ──────────────
    // Connects key facial feature points into wireframe outlines.
    // Based on the Kinect v1 FeaturePoint enum (indices 0–86).

    private static readonly int[][] _faceEdgeChains =
    [
        // Right eye (loop): outer → midTop → aboveMid → inner → belowMid → midBottom → outer
        [10, 11, 9, 13, 14, 12, 10],
        // Left eye (loop): outer → midTop → aboveMid → inner → belowMid → midBottom → outer
        [31, 32, 30, 34, 35, 33, 31],
        // Right eyebrow: outer → midTop → inner → midBottom
        [5, 6, 7, 8],
        // Left eyebrow: rightSide → midTop → leftSide → midBottom
        [29, 28, 27, 26],
        // Nose bridge: inner right eye to inner left eye
        [13, 34],
        // Upper lip contour: right corner → right dip → right top → right upper →
        //   center dip → left upper → left top → left dip → left corner
        [16, 18, 19, 20, 2, 21, 22, 23, 24],
        // Jawline: right side → right chin → bottom → left side
        [15, 17, 4, 25],
    ];

    // ── Cached DPI ─────────────────────────────────────────────

    private double _cachedPixelsPerDip;

    // ── Rendering ──────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var snapshot = Snapshot;
        if (snapshot is null) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        try { _cachedPixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { _cachedPixelsPerDip = 1.0; }

        // Compute Uniform stretch transform to match the Image control
        double imageAspect = ImageW / ImageH;
        double controlAspect = w / h;
        double scale, offsetX, offsetY;

        if (controlAspect > imageAspect)
        {
            // Control wider than image — letterbox bars on sides
            scale = h / ImageH;
            offsetX = (w - ImageW * scale) / 2.0;
            offsetY = 0;
        }
        else
        {
            // Control taller — bars on top/bottom
            scale = w / ImageW;
            offsetX = 0;
            offsetY = (h - ImageH * scale) / 2.0;
        }

        try
        {
            foreach (var person in snapshot.Persons)
            {
                // ── 1. Skeleton wireframe ──────────────────────
                if (person.JointPositions is { Count: > 0 })
                    DrawSkeleton(dc, person, scale, offsetX, offsetY);

                // ── 2. Face bounding box + wireframe ───────────
                if (person.Face is { IsTracked: true } face)
                {
                    DrawFaceRect(dc, face, scale, offsetX, offsetY);
                    DrawFaceWireframe(dc, face, scale, offsetX, offsetY);
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var ft = new FormattedText(
                    $"Overlay: {ex.Message}",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _typeface, 10, Brushes.OrangeRed,
                    _cachedPixelsPerDip > 0 ? _cachedPixelsPerDip : 1.0);
                dc.DrawText(ft, new Point(4, 4));
            }
            catch { /* last resort */ }
        }
    }

    // ── Coordinate Helpers ─────────────────────────────────────

    /// <summary>
    /// Map Kinect 640×480 pixel coords → control coords (Uniform stretch).
    /// </summary>
    private static Point MapPixel(double px, double py, double scale, double offsetX, double offsetY)
        => new(px * scale + offsetX, py * scale + offsetY);

    /// <summary>
    /// Project a 3D camera-space position (meters) to Kinect 640×480 pixel coords
    /// using the standard Kinect v1 pinhole camera model.
    /// </summary>
    private static bool TryProject(Vector3 pos, out double px, out double py)
    {
        if (pos.Z < 0.1f)
        {
            px = py = double.NaN;
            return false;
        }
        px = Fx * pos.X / pos.Z + Cx;
        py = -Fy * pos.Y / pos.Z + Cy;   // Kinect Y up → pixel Y down
        return true;
    }

    // ── Skeleton Rendering ─────────────────────────────────────

    // Upper-body bone indices (spine + arms only — no legs)
    private static readonly HashSet<int> _upperBodyJoints = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static bool IsUpperBodyBone((int A, int B) bone)
        => _upperBodyJoints.Contains(bone.A) && _upperBodyJoints.Contains(bone.B);

    /// <summary>
    /// Detect whether the person is sitting by checking if leg joints are close to hip center
    /// vertically. When sitting, knees are at roughly the same Y as hips.
    /// Only considers joints that are actually present in the dictionary.
    /// Inferred joints are still valid for posture detection.
    /// </summary>
    private static bool IsSitting(TrackedPerson person)
    {
        var joints = person.JointPositions!;
        var states = person.JointStates;

        // Need hip centre as reference
        if (!joints.TryGetValue(0, out var hip))
            return false; // can't tell, assume standing

        // If knee joints aren't present at all AND tracking states say NotTracked
        // → likely position-only mode, assume sitting / upper body
        bool hasLeftKnee = joints.ContainsKey(13);
        bool hasRightKnee = joints.ContainsKey(17);

        if (!hasLeftKnee && !hasRightKnee)
        {
            // Check states: if both are explicitly NotTracked → sitting
            if (states is not null)
            {
                bool leftNotTracked = states.TryGetValue(13, out var ls) && ls == TrackingState.NotTracked;
                bool rightNotTracked = states.TryGetValue(17, out var rs) && rs == TrackingState.NotTracked;
                if (leftNotTracked && rightNotTracked)
                    return true;
            }
            return true; // no knee data at all
        }

        // If knees exist (Tracked or Inferred), use Y-position heuristic:
        // Sitting = knees at similar Y to hips (within 20cm)
        float hipY = hip.Y;
        int kneeCount = 0;
        int sittingCount = 0;

        if (hasLeftKnee && joints.TryGetValue(13, out var lKnee))
        {
            kneeCount++;
            if (Math.Abs(lKnee.Y - hipY) < 0.20f)
                sittingCount++;
        }
        if (hasRightKnee && joints.TryGetValue(17, out var rKnee))
        {
            kneeCount++;
            if (Math.Abs(rKnee.Y - hipY) < 0.20f)
                sittingCount++;
        }

        // Sitting if all available knees are at hip level
        return kneeCount > 0 && sittingCount == kneeCount;
    }

    private void DrawSkeleton(DrawingContext dc, TrackedPerson person,
        double scale, double offsetX, double offsetY)
    {
        var joints = person.JointPositions!;
        var states = person.JointStates;
        bool fullBody = ShowFullSkeleton;

        // When toggle is ON  → always try to draw every joint the Kinect provides.
        // When toggle is OFF → auto-detect sitting and restrict to upper body.
        bool sitting = !fullBody && IsSitting(person);
        bool drawLegs = !sitting;

        // Draw bones
        foreach (var (a, b) in _bones)
        {
            // Skip leg bones if not drawing full body
            if (!drawLegs && !IsUpperBodyBone((a, b)))
                continue;

            if (!joints.TryGetValue(a, out var ja) || !joints.TryGetValue(b, out var jb))
                continue;

            if (!TryProject(ja, out double pxA, out double pyA)) continue;
            if (!TryProject(jb, out double pxB, out double pyB)) continue;

            var ptA = MapPixel(pxA, pyA, scale, offsetX, offsetY);
            var ptB = MapPixel(pxB, pyB, scale, offsetX, offsetY);

            // Choose pen based on tracking state
            bool aInferred = states is not null &&
                             states.TryGetValue(a, out var stateA) &&
                             stateA != TrackingState.Tracked;
            bool bInferred = states is not null &&
                             states.TryGetValue(b, out var stateB) &&
                             stateB != TrackingState.Tracked;

            var pen = (aInferred || bInferred) ? _inferredBonePen : _skeletonPen;
            dc.DrawLine(pen, ptA, ptB);
        }

        // Draw joint dots
        foreach (var (idx, pos) in joints)
        {
            // Skip leg joints if not drawing full body
            if (!drawLegs && !_upperBodyJoints.Contains(idx))
                continue;

            if (!TryProject(pos, out double px, out double py)) continue;
            var pt = MapPixel(px, py, scale, offsetX, offsetY);

            bool isInferred = states is not null &&
                              states.TryGetValue(idx, out var jState) &&
                              jState != TrackingState.Tracked;

            // Head joint gets a circle, others get filled dots
            if (idx == 3)
            {
                var headPen = isInferred ? _inferredBonePen : _skeletonPen;
                dc.DrawEllipse(null, headPen, pt, 16 * scale, 16 * scale);
            }
            else
            {
                double r = 4 * scale;
                Brush fill = isInferred ? _jointInferred : _jointTracked;
                dc.DrawEllipse(fill, null, pt, r, r);
            }
        }

        // Draw a body mode label
        string label;
        if (!fullBody)
            label = sitting ? "SITTING" : "UPPER BODY";
        else
            label = IsSitting(person) ? "SITTING (full)" : "STANDING";
        try
        {
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, 9, _jointTracked,
                _cachedPixelsPerDip > 0 ? _cachedPixelsPerDip : 1.0);

            // Position label near hip centre
            if (joints.TryGetValue(0, out var hipPos) && TryProject(hipPos, out double hpx, out double hpy))
            {
                var hipPt = MapPixel(hpx, hpy, scale, offsetX, offsetY);
                dc.DrawText(ft, new Point(hipPt.X + 10, hipPt.Y - 5));
            }
        }
        catch { /* drawing label not critical */ }
    }

    // ── Face Bounding Box ──────────────────────────────────────

    private static void DrawFaceRect(DrawingContext dc, FaceFrame face,
        double scale, double offsetX, double offsetY)
    {
        var (x, y, w, h) = face.FaceRect;
        if (w <= 0 || h <= 0) return;

        var topLeft = MapPixel(x, y, scale, offsetX, offsetY);
        var bottomRight = MapPixel(x + w, y + h, scale, offsetX, offsetY);
        dc.DrawRectangle(null, _faceRectPen,
            new Rect(topLeft, bottomRight));
    }

    // ── Face Feature Point Wireframe ───────────────────────────

    private void DrawFaceWireframe(DrawingContext dc, FaceFrame face,
        double scale, double offsetX, double offsetY)
    {
        var pts = face.FeaturePoints2D;
        if (pts.Length < 2) return;

        // Draw all populated feature points as small green dots
        for (int i = 0; i < pts.Length; i++)
        {
            var p = pts[i];
            if (p == Vector2.Zero) continue;     // skip unpopulated points

            var pt = MapPixel(p.X, p.Y, scale, offsetX, offsetY);

            // Pupils get larger magenta dots
            if (i == 69 || i == 73)
                dc.DrawEllipse(_pupilBrush, null, pt, 3.5 * scale, 3.5 * scale);
            else
                dc.DrawEllipse(_facePointBrush, null, pt, 1.8 * scale, 1.8 * scale);
        }

        // Connect feature points along edge chains to form wireframe
        foreach (var chain in _faceEdgeChains)
        {
            for (int i = 0; i < chain.Length - 1; i++)
            {
                int a = chain[i];
                int b = chain[i + 1];
                if (a >= pts.Length || b >= pts.Length) continue;

                var pa = pts[a];
                var pb = pts[b];
                if (pa == Vector2.Zero || pb == Vector2.Zero) continue;

                var ptA = MapPixel(pa.X, pa.Y, scale, offsetX, offsetY);
                var ptB = MapPixel(pb.X, pb.Y, scale, offsetX, offsetY);
                dc.DrawLine(_faceWirePen, ptA, ptB);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

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
