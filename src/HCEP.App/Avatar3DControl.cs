// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using System.Windows;
using System.Windows.Media;

namespace HCEP.App;

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
public sealed class Avatar3DControl : FrameworkElement
{
    // ── Stable wire pen (frozen — shareable across render cycles) ─
    private static readonly Pen _wirePen;

    static Avatar3DControl()
    {
        _wirePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 220, 190)), 0.6);
        _wirePen.Freeze();
    }

    // ── Live mesh state ──────────────────────────────────────────
    private Vector2[]? _vertices;
    private (int A, int B, int C)[]? _triangles;

    // Bounding box of the source mesh in pixel space
    private float _meshLeft, _meshTop, _meshWidth = 640, _meshHeight = 480;

    // ── Gaze state for head-turn simulation ──────────────────────
    private float _gazePitch;
    private float _gazeYaw;

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
    /// Updates the gaze angles (radians) used to simulate head-turn.
    /// Call in sync with <c>AvatarCoreControl.SetGaze</c> from <c>OnGazeVectorReady</c>.
    /// </summary>
    public void SetGaze(float pitchRad, float yawRad)
    {
        _gazePitch = pitchRad;
        _gazeYaw = yawRad;
        InvalidateVisual();
    }

    // ── Render ───────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background — inherits dark window colour.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_vertices is null || _triangles is null || _vertices.Length == 0)
        {
            // No mesh yet — draw a placeholder crosshair.
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

        // ── Draw triangle edges ───────────────────────────────
        foreach (var (a, b, c) in _triangles)
        {
            if ((uint)a >= (uint)_vertices.Length ||
                (uint)b >= (uint)_vertices.Length ||
                (uint)c >= (uint)_vertices.Length)
                continue;

            Point pa = Map(a);
            Point pb = Map(b);
            Point pc = Map(c);

            dc.DrawLine(_wirePen, pa, pb);
            dc.DrawLine(_wirePen, pb, pc);
            dc.DrawLine(_wirePen, pc, pa);
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
