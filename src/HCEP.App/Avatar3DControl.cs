// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using System.Windows;
using System.Windows.Media;

namespace HCEP.App;

// ── Kinect FaceTracking SDK 87-point feature-point edge connectivity ──────────
// Indices match KinectSensorSource FeaturePoints2D ordering.
// Mirrors and extends VideoOverlayControl._faceEdgeChains.
file static class FaceEdgeChains
{
    public static readonly int[][] Chains =
    [
        // Eyes (closed loops)
        [10, 11, 9, 13, 14, 12, 10],                                           // right eye
        [31, 32, 30, 34, 35, 33, 31],                                          // left eye
        // Eyebrows
        [5, 6, 7, 8],                                                          // right brow
        [29, 28, 27, 26],                                                      // left brow
        // Nose
        [13, 34],                                                              // bridge
        [40, 41, 42, 43, 44, 45, 40],                                          // tip + nostrils
        // Mouth
        [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 48],                 // outer lip
        [60, 61, 62, 63, 64, 65, 66, 67, 60],                                 // inner lip
        // Jaw / face contour
        [0, 1, 2, 3, 4, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 0],
    ];
}

/// <summary>
/// 3D Wireframe Avatar — renders the Kinect FaceTrackLib Candide-3 mesh
/// (typically ~121 vertices, ~218 triangles) using WPF DrawingContext.
///
/// ── Coordinate Pipeline ───────────────────────────────────────
/// Input  : <c>FaceFrame.FaceMeshVertices2D</c> — pixel coordinates in the
///          640×480 depth/color camera image returned by KinectSensorSource.
/// Render : Vertices are fit into the control's bounds (with 8% padding),
///          maintaining aspect ratio. No matrix inversion needed.
///
/// ── Head-Turn Simulation (True Gaze) ─────────────────────────
/// <c>SetGaze(pitch, yaw)</c> applies two perceptual transforms per frame:
///   • Yaw  → X-axis compression around the mesh centre (cos projection).
///             Simulates the face turning left/right to maintain eye contact.
///   • Pitch → Vertical shift (sin scale of control height).
///             Simulates the head tilting up/down toward the user.
///
/// ── Rendering ─────────────────────────────────────────────────
/// • Wire stroke: semi-transparent cyan (#C800DCBE, 0.6 px).
/// • Wire edges are deduplicated per triangle share — drawing each
///   half-edge from the triangle list is sufficient for a visual MVP.
/// • Transparent background allows the dark <c>AvatarWindow</c> to show through.
/// </summary>
public sealed class Avatar3DControl : FrameworkElement, IAvatarComponent
{
    // ── Stable wire pen (frozen — shareable across render cycles) ─
    private static readonly Pen _wirePen;

    static Avatar3DControl()
    {
        _wirePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 220, 190)), 1.2);
        _wirePen.Freeze();
    }

    // ── Live mesh state ──────────────────────────────────────────
    private Vector2[]? _vertices;
    private (int A, int B, int C)[]? _triangles;
    private Vector2[]? _featurePoints;   // 87 SDK landmarks — always updated alongside mesh

    // Bounding box of the source mesh in pixel space
    private float _meshLeft, _meshTop, _meshWidth = 640, _meshHeight = 480;

    // ── Gaze state ──────────────────────────────────────────────────────
    private float _gazePitch;
    private float _gazeYaw;
    private float _gazeDistM = 1.5f;

    // ── Cached eye socket screen positions (for GazeVectorEngine eye provider) ──
    private Point _leftEyeLocalPt;    // updated each OnRender frame
    private Point _rightEyeLocalPt;
    public Point LeftEyeScreenPos { get; private set; }
    public Point RightEyeScreenPos { get; private set; }
    // ── Construction ─────────────────────────────────────────────────────
    public Avatar3DControl()
    {
        // Re-resolve screen coords whenever layout changes — same pattern as AvatarCoreControl.
        LayoutUpdated += (_, _) => UpdateEyeScreenCoordinates();
    }

    private void UpdateEyeScreenCoordinates()
    {
        if (PresentationSource.FromVisual(this) is null) return;
        try
        {
            LeftEyeScreenPos = PointToScreen(_leftEyeLocalPt);
            RightEyeScreenPos = PointToScreen(_rightEyeLocalPt);
        }
        catch (InvalidOperationException) { }
    }
    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Updates the wireframe mesh. Thread-safe: can be called from any thread;
    /// <c>InvalidateVisual</c> is marshalled to the UI dispatcher automatically by WPF.
    /// </summary>
    public void SetMesh(Vector2[] vertices, (int First, int Second, int Third)[] triangles)
    {
        _vertices = vertices;
        _triangles = triangles.Select(t => (t.First, t.Second, t.Third)).ToArray();
        ComputeBounds();
        InvalidateVisual();
    }

    /// <summary>
    /// Feature-point fallback: renders a dot cloud using the 87 FaceTrackLib landmark
    /// points when the full <c>GetProjectedShape</c> mesh is not yet available.
    /// Called from <c>AvatarWindow.OnSnapshotReady</c> when <c>FaceMeshVertices2D</c> is null.
    /// </summary>
    public void SetFeaturePoints(Vector2[] points)
    {
        _vertices = points;
        _triangles = null;   // null = dot-cloud mode
        ComputeBounds();
        InvalidateVisual();
    }

    /// <summary>
    /// Stores the latest 87-point feature-point array for eye socket centre
    /// computation. Called every frame from <c>AvatarWindow.OnSnapshotReady</c>
    /// whenever the face is tracked, independent of whether full mesh is active.
    /// Thread-safe: reference assignment is atomic on all .NET platforms.
    /// </summary>
    public void UpdateEyeData(Vector2[] featurePoints) => _featurePoints = featurePoints;

    /// <summary>
    /// Updates the gaze angles (radians) and user distance.
    /// Drives pupil position within the live eye sockets.
    /// </summary>
    public void SetGaze(float pitchRad, float yawRad, float distanceM = 1.5f)
    {
        _gazePitch = pitchRad;
        _gazeYaw = yawRad;
        _gazeDistM = distanceM;
        InvalidateVisual();
    }

    // IAvatarComponent
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze(p, y, d);
    void IAvatarComponent.ResetGaze()
    {
        _vertices = null;
        _triangles = null;
        _gazePitch = 0;
        _gazeYaw = 0;
        InvalidateVisual();
    }

    // ── Render ───────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background — inherits dark window colour.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_vertices is null || _vertices.Length == 0)
        {
            // No data at all — draw a placeholder crosshair.
            var grey = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);
            grey.Freeze();
            double cx = ActualWidth / 2, cy = ActualHeight / 2;
            dc.DrawLine(grey, new Point(cx - 20, cy), new Point(cx + 20, cy));
            dc.DrawLine(grey, new Point(cx, cy - 20), new Point(cx, cy + 20));
            return;
        }

        double w = ActualWidth;
        double h = ActualHeight;

        if (_meshWidth <= 0 || _meshHeight <= 0) return;

        // ── Fit-to-bounds with uniform scale and 8% padding ───
        double padX = w * 0.08;
        double padY = h * 0.08;
        double scale = Math.Min(
            (w - padX * 2) / _meshWidth,
            (h - padY * 2) / _meshHeight);

        double offX = (w - _meshWidth * scale) / 2.0 - _meshLeft * scale;
        double offY = (h - _meshHeight * scale) / 2.0 - _meshTop * scale;

        // ── Head-turn transforms ──────────────────────────────
        // Yaw  → compress X around mesh centre to simulate rotation.
        // Pitch→ shift all vertices up/down.
        double yawCompress = Math.Cos(Math.Clamp(_gazeYaw, -Math.PI / 3, Math.PI / 3));
        double pitchShift = Math.Sin(-_gazePitch) * h * 0.07;
        double meshCentreX = w / 2.0;

        Point Map(int idx)
        {
            Vector2 v = _vertices[idx];
            double x = v.X * scale + offX;
            double y = v.Y * scale + offY + pitchShift;
            // Compress X toward mesh centre to simulate yaw rotation.
            x = meshCentreX + (x - meshCentreX) * yawCompress;
            return new Point(x, y);
        }

        // ── Draw triangle edges OR dot-cloud ─────────────────
        if (_triangles is not null)
        {
            // Full wireframe mesh
            foreach (var (a, b, c) in _triangles)
            {
                if ((uint)a >= (uint)_vertices.Length ||
                    (uint)b >= (uint)_vertices.Length ||
                    (uint)c >= (uint)_vertices.Length)
                    continue;

                dc.DrawLine(_wirePen, Map(a), Map(b));
                dc.DrawLine(_wirePen, Map(b), Map(c));
                dc.DrawLine(_wirePen, Map(c), Map(a));
            }
        }
        else
        {
            // Feature-point wireframe: Kinect 87-point edge chains.
            // Skip Vector2.Zero entries — they are uninitialized slots.
            foreach (var chain in FaceEdgeChains.Chains)
            {
                for (int i = 0; i < chain.Length - 1; i++)
                {
                    int a = chain[i], b = chain[i + 1];
                    if (a >= _vertices.Length || b >= _vertices.Length) continue;
                    Vector2 va = _vertices[a], vb = _vertices[b];
                    if (va == Vector2.Zero || vb == Vector2.Zero) continue;
                    dc.DrawLine(_wirePen, Map(a), Map(b));
                }
            }

            // Pupils drawn in the unified gaze block below.
        }

        // ── Gaze-driven pupils in eye sockets ────────────────────────────────
        // Uses FeaturePoints2D indices to locate the eye socket centres + radii,
        // regardless of whether the full mesh or edge-chain fallback is active.
        // Right eye loop: fp indices [9,10,11,12,13,14]
        // Left  eye loop: fp indices [30,31,32,33,34,35]
        if (_featurePoints is { Length: > 35 })
        {
            // MapFP: applies the SAME fit-to-bounds transform as Map but to feature-point indices.
            // Both FaceMeshVertices2D and FeaturePoints2D are in the same 640×480 projected space.
            Point MapFP(int idx)
            {
                if (idx >= _featurePoints.Length || _featurePoints[idx] == Vector2.Zero)
                    return new Point(double.NaN, double.NaN);
                Vector2 v = _featurePoints[idx];
                double fx = v.X * scale + offX;
                double fy = v.Y * scale + offY + pitchShift;
                fx = meshCentreX + (fx - meshCentreX) * yawCompress;
                return new Point(fx, fy);
            }

            // Compute centroid + half-width of an eye socket from a set of fp indices.
            static (double cx, double cy, double r) EyeSocket(
                Func<int, Point> mapFP, int[] idx)
            {
                double sx = 0, sy = 0;
                double minX = double.MaxValue, maxX = double.MinValue;
                int n = 0;
                foreach (int i in idx)
                {
                    var p = mapFP(i);
                    if (double.IsNaN(p.X)) continue;
                    sx += p.X; sy += p.Y; n++;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                }
                if (n == 0) return (double.NaN, double.NaN, 0);
                // Radius = half the horizontal eye span, so pupil fits inside.
                return (sx / n, sy / n, (maxX - minX) * 0.42);
            }

            int[] rIdx = [9, 10, 11, 12, 13, 14];
            int[] lIdx = [30, 31, 32, 33, 34, 35];
            var (rcx, rcy, rr) = EyeSocket(MapFP, rIdx);
            var (lcx, lcy, lr) = EyeSocket(MapFP, lIdx);

            if (!double.IsNaN(rcx) && !double.IsNaN(lcx) && rr > 1 && lr > 1)
            {
                // Gaze math mirrors AvatarCoreControl.SetGaze:
                // Pitch > 0 = looking UP → pupils translate −Y
                // Yaw   > 0 = looking RIGHT → pupils translate +X
                const double MaxAngle = Math.PI / 4.0;
                double normYaw = Math.Clamp(_gazeYaw, -MaxAngle, MaxAngle) / MaxAngle;
                double normPitch = Math.Clamp(_gazePitch, -MaxAngle, MaxAngle) / MaxAngle;
                // Binocular convergence: pupils angle inward as user leans in.
                double conv = rr * 0.25 * Math.Clamp((1.2 - _gazeDistM) / 1.2, 0.0, 1.0);
                double travel = 0.42;  // fraction of socket radius for full-angle travel

                // Right pupil: convergence pulls LEFT (toward nose)
                double rpx = rcx + normYaw * rr * travel - conv;
                double rpy = rcy - normPitch * rr * travel;
                // Left  pupil: convergence pulls RIGHT (toward nose)
                double lpx = lcx + normYaw * lr * travel + conv;
                double lpy = lcy - normPitch * lr * travel;

                double pr = Math.Min(rr, lr) * 0.30;  // pupil radius relative to socket

                var pupilBrush = new SolidColorBrush(Color.FromArgb(255, 0, 220, 190));
                pupilBrush.Freeze();
                dc.DrawEllipse(pupilBrush, null, new Point(rpx, rpy), pr, pr);
                dc.DrawEllipse(pupilBrush, null, new Point(lpx, lpy), pr, pr);

                // Cache local-space socket centres for LayoutUpdated → screen coord tracking.
                _rightEyeLocalPt = new Point(rcx, rcy);
                _leftEyeLocalPt = new Point(lcx, lcy);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private void ComputeBounds()
    {
        if (_vertices is null || _vertices.Length == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vector2 v in _vertices)
        {
            // Skip uninitialized slots — Vector2.Zero is not a real face point.
            // Including zeros pulls minX/minY to 0 which shifts the entire face
            // to the lower-right and creates the stray upper-left dot.
            if (v == Vector2.Zero) continue;
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
        }

        if (maxX > minX && maxY > minY)
        {
            _meshLeft = minX;
            _meshTop = minY;
            _meshWidth = maxX - minX;
            _meshHeight = maxY - minY;
        }
    }
}
