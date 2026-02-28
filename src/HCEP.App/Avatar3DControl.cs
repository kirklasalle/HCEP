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
/// (~121 vertices, ~218 triangles) using WPF DrawingContext.
///
/// ── Rendering Modes ───────────────────────────────────────────
/// <b>Full-mesh mode</b> (preferred): <c>GetProjectedShape</c> succeeded.
///   Mesh vertices are already projected with the user's head pose baked in
///   (pitch / yaw / roll from Kinect <c>Get3DPose</c>).  No additional
///   head-rotation transform is applied — the tracking data is authoritative.
///
/// <b>Fallback mode</b>: <c>GetProjectedShape</c> not yet available.
///   Renders 87-point feature-point edge chains with a naive cos-compress
///   (yaw) and vertical shift (pitch) to approximate the head orientation.
///
/// ── Eye-First Tracking ────────────────────────────────────────
/// <c>SetGaze(pitch, yaw)</c> drives the pupils within the live eye sockets.
/// In full-mesh mode the pupil offset is computed from the EYE-RELATIVE
/// gaze component:
///
///   eyeRelativeYaw = gazeYaw − headYawRad
///
/// This ensures:
///   • Pupils move first within the sockets (while the head is still centred).
///   • As the head turns (Kinect tracks it → mesh rotates), the pupils settle
///     back toward centre — the head has "taken over" that angular range.
///   • If the total gaze exceeds the eye socket limit <em>and</em> the head
///     hasn't turned yet, pupils sit at the socket edge until it does.
///
/// ── Head Pose Input ───────────────────────────────────────────
/// <c>SetHeadPose(Vector3 rotationDeg)</c> receives
/// <c>FaceFrame.HeadRotation</c> from <c>AvatarWindow.OnSnapshotReady</c>.
/// The values (pitchDeg, yawDeg, rollDeg) are converted to radians and
/// used only for the eye-relative pupil computation.
///
/// ── Thread Safety ─────────────────────────────────────────────
/// <c>SetMesh</c>, <c>SetFeaturePoints</c>, <c>UpdateEyeData</c>,
/// <c>SetHeadPose</c>, and <c>SetGaze</c> may be called from any thread;
/// WPF marshals <c>InvalidateVisual</c> to the UI dispatcher automatically.
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

    // ── Head pose (from Kinect FaceFrame.HeadRotation) ──────────────────
    // In full-mesh mode these are baked into GetProjectedShape vertices.
    // In fallback mode these drive the head rotation transform.
    private float _headYawRad;
    private float _headPitchRad;
    private float _headRollRad;

    // ── Mesh status (surfaced to AvatarWindow HUD) ───────────────────────
    public int MeshVertexCount { get; private set; }
    public int MeshTriangleCount { get; private set; }

    // ── Cached eye socket screen positions (for GazeVectorEngine eye provider) ──
    private Point _leftEyeLocalPt;    // updated each OnRender frame
    private Point _rightEyeLocalPt;
    public Point LeftEyeScreenPos { get; private set; }
    public Point RightEyeScreenPos { get; private set; }

    // ── Mesh eye-socket lock state (stable mesh-ID anchors) ───────────────────
    private int[]? _leftEyeSocketMeshIds;
    private int[]? _rightEyeSocketMeshIds;
    private Point _leftEyeSocketSmoothed;
    private Point _rightEyeSocketSmoothed;
    private bool _eyeSocketSmoothingReady;
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
        bool topologyChanged = _vertices is null || _vertices.Length != vertices.Length;

        _vertices = vertices;
        _triangles = triangles.Select(t => (t.First, t.Second, t.Third)).ToArray();
        MeshVertexCount = vertices.Length;
        MeshTriangleCount = triangles.Length;

        if (topologyChanged)
        {
            _leftEyeSocketMeshIds = null;
            _rightEyeSocketMeshIds = null;
            _eyeSocketSmoothingReady = false;
        }

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
        _triangles = null;   // null = edge-chain fallback mode
        MeshVertexCount = 0;
        MeshTriangleCount = 0;
        _leftEyeSocketMeshIds = null;
        _rightEyeSocketMeshIds = null;
        _eyeSocketSmoothingReady = false;
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

    /// <summary>
    /// Updates the head pose from Kinect FaceFrame.HeadRotation (degrees).
    /// <paramref name="rotationDeg"/> = (pitchDeg, yawDeg, rollDeg).
    /// Used to compute the eye-relative gaze component — the portion of the
    /// total gaze angle that the eyes must cover because the head has not yet
    /// rotated that far.  The head rotation itself is baked into the
    /// <c>GetProjectedShape</c> mesh vertices, so no additional rendering
    /// transform is needed for the head.
    /// </summary>
    public void SetHeadPose(System.Numerics.Vector3 rotationDeg)
    {
        const float Deg2Rad = MathF.PI / 180f;
        _headYawRad = rotationDeg.Y * Deg2Rad;
        _headPitchRad = rotationDeg.X * Deg2Rad;
        _headRollRad = rotationDeg.Z * Deg2Rad;
        // In fallback mode, head rotation is rendered by OnRender.
        // In full-mesh mode, rotation is baked into GetProjectedShape vertices.
        InvalidateVisual();
    }

    // IAvatarComponent
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze(p, y, d);
    void IAvatarComponent.ResetGaze()
    {
        _vertices = null;
        _triangles = null;
        _leftEyeSocketMeshIds = null;
        _rightEyeSocketMeshIds = null;
        _eyeSocketSmoothingReady = false;
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
        double fitScale = Math.Min(
            (w - padX * 2) / _meshWidth,
            (h - padY * 2) / _meshHeight);

        double offX = (w - _meshWidth * fitScale) / 2.0 - _meshLeft * fitScale;
        double offY = (h - _meshHeight * fitScale) / 2.0 - _meshTop * fitScale;

        // ── Whether the real Candide-3 projected mesh is active ──────────
        // When true: GetProjectedShape succeeded; head rotation is already
        // baked into the vertex positions.  We render with a pure transform.
        // When false: edge-chain fallback using 87 feature points; we apply
        // a naive cos-compress / pitch-shift to approximate head orientation.
        bool hasMesh = _triangles is not null;

        // ── Transform parameters (full-mesh vs feature-point fallback) ───
        // Full-mesh mode: no manual head transforms — they are in the vertices.
        // Fallback mode : use REAL head rotation from Kinect FaceFrame.HeadRotation.
        //   yaw  → X-compress (cos) + slight X-shift (sin)
        //   pitch → Y-shift
        //   roll  → rotation around face centre
        double meshCentreX = w / 2.0;
        double meshCentreY = h / 2.0;

        double headYaw = _headYawRad;
        double headPitch = _headPitchRad;
        double headRoll = _headRollRad;

        double yawCompress = hasMesh ? 1.0 :
            Math.Cos(Math.Clamp(headYaw, -Math.PI / 3, Math.PI / 3));
        double yawShiftX = hasMesh ? 0.0 :
            Math.Sin(-headYaw) * w * 0.12;   // lateral shift with yaw
        double pitchShift = hasMesh ? 0.0 :
            Math.Sin(-headPitch) * h * 0.10;  // vertical shift with pitch
        double rollAngle = hasMesh ? 0.0 : headRoll;

        // ── Vertex mapping ───────────────────────────────────────────────
        // Converts a mesh-space vertex index to screen-space Point.
        // Fallback mode applies: yaw-compress, yaw-shift, pitch-shift, then roll.
        Point Map(int idx)
        {
            Vector2 v = _vertices[idx];
            double x = v.X * fitScale + offX;
            double y = v.Y * fitScale + offY + pitchShift;
            if (!hasMesh)
            {
                x = meshCentreX + (x - meshCentreX) * yawCompress + yawShiftX;
                // Apply roll rotation around the visual centre
                if (Math.Abs(rollAngle) > 0.001)
                {
                    double dx = x - meshCentreX;
                    double dy = y - meshCentreY;
                    double cosR = Math.Cos(rollAngle);
                    double sinR = Math.Sin(rollAngle);
                    x = meshCentreX + dx * cosR - dy * sinR;
                    y = meshCentreY + dx * sinR + dy * cosR;
                }
            }
            return new Point(x, y);
        }

        // ── Draw triangle edges OR feature-point edge chains ─────────────
        if (hasMesh)
        {
            // Full wireframe mesh — GetProjectedShape succeeded.
            foreach (var (a, b, c) in _triangles!)
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
            // Feature-point wireframe: Kinect 87-point edge chains (fallback).
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
        }

        // ── Gaze-driven pupils in eye sockets ────────────────────────────
        // In high-poly mesh mode, anchor pupils to mesh-derived eye regions.
        // In fallback mode, use legacy feature-point eye loops.
        //
        // Pupil positioning
        // ─────────────────
        // GazeVectorReady pitch/yaw are geometrically computed angles from the
        // avatar eye socket toward the user's eye position (camera-space geometry).
        // FaceFrame.HeadRotation is the user's head orientation from Kinect.
        // These are in DIFFERENT frames — subtraction would be meaningless.
        //
        // In full-mesh mode the projected feature-point positions already reflect
        // the user's head orientation (GetProjectedShape bakes in head pose).
        // The pupils are offset WITHIN those live socket positions using the
        // raw geometric gaze angles — no head-pose subtraction.
        //
        // MaxGazeAngle = 20° covers the practical gaze range before Kinect
        // tracking fidelity degrades.  Using 20° (vs 45°) means an 8° gaze
        // registers as 40% of full travel — visually clear.
        if (hasMesh && _vertices is { Length: > 20 })
        {
            // Seed lock once from live eye contour points (feature points), then keep
            // those stable mesh vertex IDs as socket anchors across frames.
            if ((_leftEyeSocketMeshIds is null || _rightEyeSocketMeshIds is null)
                && _featurePoints is { Length: > 35 }
                && TrySeedEyeSocketMeshIds(_vertices, _featurePoints, out var leftIds, out var rightIds))
            {
                _leftEyeSocketMeshIds = leftIds;
                _rightEyeSocketMeshIds = rightIds;
                _eyeSocketSmoothingReady = false;
            }

            Point MapMeshRaw(Vector2 v) => new(v.X * fitScale + offX, v.Y * fitScale + offY);

            if (_leftEyeSocketMeshIds is { Length: > 0 } leftSocketIds
                && _rightEyeSocketMeshIds is { Length: > 0 } rightSocketIds
                && TryComputeSocket(_vertices, leftSocketIds, MapMeshRaw, out var leftSocket)
                && TryComputeSocket(_vertices, rightSocketIds, MapMeshRaw, out var rightSocket))
            {
                // Temporal smoothing to remove per-frame centroid jitter while keeping
                // responsiveness; this is key for visual "lock" under lean/yaw.
                const double Alpha = 0.32;
                if (!_eyeSocketSmoothingReady)
                {
                    _leftEyeSocketSmoothed = new Point(leftSocket.Cx, leftSocket.Cy);
                    _rightEyeSocketSmoothed = new Point(rightSocket.Cx, rightSocket.Cy);
                    _eyeSocketSmoothingReady = true;
                }
                else
                {
                    _leftEyeSocketSmoothed = new Point(
                        _leftEyeSocketSmoothed.X + (leftSocket.Cx - _leftEyeSocketSmoothed.X) * Alpha,
                        _leftEyeSocketSmoothed.Y + (leftSocket.Cy - _leftEyeSocketSmoothed.Y) * Alpha);
                    _rightEyeSocketSmoothed = new Point(
                        _rightEyeSocketSmoothed.X + (rightSocket.Cx - _rightEyeSocketSmoothed.X) * Alpha,
                        _rightEyeSocketSmoothed.Y + (rightSocket.Cy - _rightEyeSocketSmoothed.Y) * Alpha);
                }

                // Blend toward live eyelid feature-point centroids for tighter visual seating.
                // Mesh IDs provide stability; feature loops provide immediate eyelid alignment.
                Point leftAnchor = _leftEyeSocketSmoothed;
                Point rightAnchor = _rightEyeSocketSmoothed;
                double yawAbsNorm = Math.Clamp(Math.Abs(_headYawRad) / (Math.PI / 4.0), 0.0, 1.0);
                if (_featurePoints is { Length: > 35 }
                    && TryFeatureEyeCenter(_featurePoints, [30, 31, 32, 33, 34, 35], fitScale, offX, offY, out var fpLeft)
                    && TryFeatureEyeCenter(_featurePoints, [9, 10, 11, 12, 13, 14], fitScale, offX, offY, out var fpRight))
                {
                    // If locked sockets drift too far from eyelid contours, reseed IDs.
                    // This keeps persistence while recovering from pose-driven lock drift.
                    double driftL = Math.Sqrt((leftAnchor.X - fpLeft.X) * (leftAnchor.X - fpLeft.X)
                                            + (leftAnchor.Y - fpLeft.Y) * (leftAnchor.Y - fpLeft.Y));
                    double driftR = Math.Sqrt((rightAnchor.X - fpRight.X) * (rightAnchor.X - fpRight.X)
                                            + (rightAnchor.Y - fpRight.Y) * (rightAnchor.Y - fpRight.Y));
                    double driftThreshold = Math.Max(Math.Min(leftSocket.HalfW, rightSocket.HalfW) * 0.95, 10.0);
                    if ((driftL > driftThreshold || driftR > driftThreshold)
                        && TrySeedEyeSocketMeshIds(_vertices, _featurePoints, out var reseedLeft, out var reseedRight))
                    {
                        _leftEyeSocketMeshIds = reseedLeft;
                        _rightEyeSocketMeshIds = reseedRight;
                        _eyeSocketSmoothingReady = false;
                        leftAnchor = fpLeft;
                        rightAnchor = fpRight;
                    }

                    // Stronger feature influence at high yaw keeps pupils seated in sockets.
                    double blend = 0.52 + (0.33 * yawAbsNorm);
                    leftAnchor = new Point(
                        leftAnchor.X + (fpLeft.X - leftAnchor.X) * blend,
                        leftAnchor.Y + (fpLeft.Y - leftAnchor.Y) * blend);
                    rightAnchor = new Point(
                        rightAnchor.X + (fpRight.X - rightAnchor.X) * blend,
                        rightAnchor.Y + (fpRight.Y - rightAnchor.Y) * blend);
                }

                const double MaxGazeAngle = Math.PI / 9.0; // 20°

                // Eye-leads/head-follows: eyes take initial movement, then settle toward
                // center as head yaw/pitch catches up.
                double eyeRelativeYaw = _gazeYaw - (_headYawRad * 0.45);
                double eyeRelativePitch = _gazePitch - (_headPitchRad * 0.35);

                double normYaw = Math.Clamp(eyeRelativeYaw, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;
                double normPitch = Math.Clamp(eyeRelativePitch, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;

                // Eyeball-like rotation mapping: use sin() so center response is smooth
                // and extremes saturate naturally near socket limits.
                double rotYaw = Math.Sin(normYaw * (Math.PI / 2.0));
                double rotPitch = Math.Sin(normPitch * (Math.PI / 2.0));

                const double Travel = 0.48;
                double lTX = leftSocket.HalfW * Travel, lTY = leftSocket.HalfH * Travel;
                double rTX = rightSocket.HalfW * Travel, rTY = rightSocket.HalfH * Travel;

                double conv = Math.Min(leftSocket.HalfW, rightSocket.HalfW) * 0.18
                              * Math.Clamp((1.2 - _gazeDistM) / 1.2, 0.0, 1.0)
                              * (1.0 - 0.60 * yawAbsNorm);

                // Slight downward seating bias keeps pupils inside wireframe eyelid loops.
                // Increase with yaw because projected eyelids visually rise under turn.
                double seatBiasYLeft = leftSocket.HalfH * (0.12 + 0.08 * yawAbsNorm);
                double seatBiasYRight = rightSocket.HalfH * (0.12 + 0.08 * yawAbsNorm);

                double lpx = leftAnchor.X + rotYaw * lTX + conv;
                double lpy = leftAnchor.Y - rotPitch * lTY + seatBiasYLeft;
                double rpx = rightAnchor.X + rotYaw * rTX - conv;
                double rpy = rightAnchor.Y - rotPitch * rTY + seatBiasYRight;

                // Hard separation safety: never allow both pupils to collapse into one eye.
                double minSep = Math.Max(Math.Min(leftSocket.HalfW, rightSocket.HalfW) * 1.1, 6.0);
                if (rpx - lpx < minSep)
                {
                    double cx = (lpx + rpx) / 2.0;
                    lpx = cx - minSep / 2.0;
                    rpx = cx + minSep / 2.0;
                }

                double pr = Math.Max(Math.Min(Math.Min(leftSocket.HalfW, rightSocket.HalfW) * 0.24, 10.0), 3.6);

                var pupilBrush = new SolidColorBrush(Color.FromArgb(255, 0, 220, 190));
                pupilBrush.Freeze();
                dc.DrawEllipse(pupilBrush, null, new Point(lpx, lpy), pr, pr);
                dc.DrawEllipse(pupilBrush, null, new Point(rpx, rpy), pr, pr);

                _leftEyeLocalPt = leftAnchor;
                _rightEyeLocalPt = rightAnchor;
            }
        }
        else if (_featurePoints is { Length: > 35 })
        {
            // MapFP: applies fit-to-bounds transform to a feature-point index.
            // Both FaceMeshVertices2D and FeaturePoints2D live in the same 640×480
            // projected space, so we use the same fitScale/offX/offY offsets.
            Point MapFP(int idx)
            {
                if (idx >= _featurePoints.Length || _featurePoints[idx] == Vector2.Zero)
                    return new Point(double.NaN, double.NaN);
                Vector2 v = _featurePoints[idx];
                double fx = v.X * fitScale + offX;
                double fy = v.Y * fitScale + offY + pitchShift;
                if (!hasMesh)
                {
                    fx = meshCentreX + (fx - meshCentreX) * yawCompress + yawShiftX;
                    if (Math.Abs(rollAngle) > 0.001)
                    {
                        double dx = fx - meshCentreX;
                        double dy = fy - meshCentreY;
                        double cosR = Math.Cos(rollAngle);
                        double sinR = Math.Sin(rollAngle);
                        fx = meshCentreX + dx * cosR - dy * sinR;
                        fy = meshCentreY + dx * sinR + dy * cosR;
                    }
                }
                return new Point(fx, fy);
            }

            // Compute centroid and HALF-SPAN (true half-extent) of an eye socket.
            // Returns (cx, cy, halfW, halfH) — halfW drives X travel, halfH drives Y travel.
            static (double cx, double cy, double halfW, double halfH) EyeSocket(
                Func<int, Point> mapFP, int[] idx)
            {
                double sx = 0, sy = 0;
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                int n = 0;
                foreach (int i in idx)
                {
                    var p = mapFP(i);
                    if (double.IsNaN(p.X)) continue;
                    sx += p.X; sy += p.Y; n++;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                if (n == 0) return (double.NaN, double.NaN, 0, 0);
                return (sx / n, sy / n,
                    (maxX - minX) / 2.0,
                    Math.Max((maxY - minY) / 2.0, (maxX - minX) / 4.0)); // min vert extent
            }

            int[] rIdx = [9, 10, 11, 12, 13, 14];
            int[] lIdx = [30, 31, 32, 33, 34, 35];
            var (rcx, rcy, rHW, rHH) = EyeSocket(MapFP, rIdx);
            var (lcx, lcy, lHW, lHH) = EyeSocket(MapFP, lIdx);

            if (!double.IsNaN(rcx) && !double.IsNaN(lcx) && rHW > 1 && lHW > 1)
            {
                // ── Normalised gaze angles ────────────────────────────────
                // MaxGazeAngle = 20° — practical Kinect tracking limit.
                // No head-pose subtraction: gaze and head-pose live in
                // different reference frames (geometry vs. camera-space rotation).
                const double MaxGazeAngle = Math.PI / 9.0; // 20°
                double normYaw = Math.Clamp(_gazeYaw, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;
                double normPitch = Math.Clamp(_gazePitch, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;

                // Travel = fraction of socket half-span pupils move for full-angle gaze.
                // 0.65 keeps the dot clearly inside the socket at extremes.
                const double Travel = 0.65;
                double rTX = rHW * Travel, rTY = rHH * Travel;
                double lTX = lHW * Travel, lTY = lHH * Travel;

                // Binocular convergence: pupils angle inward as user leans closer.
                double conv = rHW * 0.30 * Math.Clamp((1.2 - _gazeDistM) / 1.2, 0.0, 1.0);

                // Right pupil: yaw+ = look right → +X; pitch+ = look up → −Y.
                // Convergence pulls right pupil LEFT (toward nose = −X).
                double rpx = rcx + normYaw * rTX - conv;
                double rpy = rcy - normPitch * rTY;
                // Left pupil: convergence pulls left pupil RIGHT (toward nose = +X).
                double lpx = lcx + normYaw * lTX + conv;
                double lpy = lcy - normPitch * lTY;

                // Pupil visual radius: ~28% of socket half-width, minimum 4px.
                double pr = Math.Max(Math.Min(rHW, lHW) * 0.28, 4.0);

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

    private readonly struct SocketShape(double cx, double cy, double halfW, double halfH)
    {
        public double Cx { get; } = cx;
        public double Cy { get; } = cy;
        public double HalfW { get; } = halfW;
        public double HalfH { get; } = halfH;
    }

    private static bool TryComputeSocket(
        Vector2[] verts,
        int[] ids,
        Func<Vector2, Point> map,
        out SocketShape socket)
    {
        double sx = 0, sy = 0;
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        int n = 0;

        foreach (int id in ids)
        {
            if ((uint)id >= (uint)verts.Length) continue;
            Vector2 v = verts[id];
            if (v == Vector2.Zero) continue;
            var p = map(v);
            sx += p.X;
            sy += p.Y;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            n++;
        }

        if (n < 3)
        {
            socket = default;
            return false;
        }

        socket = new SocketShape(
            sx / n,
            sy / n,
            Math.Max((maxX - minX) / 2.0, 5.0),
            Math.Max((maxY - minY) / 2.0, 3.5));
        return true;
    }

    private bool TrySeedEyeSocketMeshIds(
        Vector2[] meshVerts,
        Vector2[] featurePoints,
        out int[] leftIds,
        out int[] rightIds)
    {
        float centerX = ComputeMeshCenterX(meshVerts);

        leftIds = BuildSocketIds(meshVerts, featurePoints, [30, 31, 32, 33, 34, 35], centerX, requireLeftSide: true);
        rightIds = BuildSocketIds(meshVerts, featurePoints, [9, 10, 11, 12, 13, 14], centerX, requireLeftSide: false);

        // Enforce disjoint sets for persistent dual-eye lock.
        if (leftIds.Length > 0 && rightIds.Length > 0)
        {
            var leftSet = new HashSet<int>(leftIds);
            rightIds = rightIds.Where(i => !leftSet.Contains(i)).ToArray();
        }

        // Fallback reseed by geometry if one side is weak.
        if (leftIds.Length < 4)
            leftIds = BuildSocketBySide(meshVerts, centerX, true);
        if (rightIds.Length < 4)
            rightIds = BuildSocketBySide(meshVerts, centerX, false);

        return leftIds.Length >= 4 && rightIds.Length >= 4;
    }

    private int[] BuildSocketIds(
        Vector2[] meshVerts,
        Vector2[] featurePoints,
        int[] fpEyeIndices,
        float centerX,
        bool requireLeftSide)
    {
        var seeds = new HashSet<int>();
        var fpValid = new List<Vector2>(fpEyeIndices.Length);

        foreach (int fpIdx in fpEyeIndices)
        {
            if ((uint)fpIdx >= (uint)featurePoints.Length) continue;
            Vector2 fp = featurePoints[fpIdx];
            if (fp == Vector2.Zero) continue;
            fpValid.Add(fp);

            float bestD2 = float.MaxValue;
            int best = -1;
            for (int i = 0; i < meshVerts.Length; i++)
            {
                Vector2 mv = meshVerts[i];
                if (mv == Vector2.Zero) continue;

                // Side constraint prevents both eyes from snapping to same socket.
                if (requireLeftSide)
                {
                    if (mv.X > centerX) continue;
                }
                else
                {
                    if (mv.X < centerX) continue;
                }

                float dx = mv.X - fp.X;
                float dy = mv.Y - fp.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            if (best >= 0) seeds.Add(best);
        }

        if (seeds.Count == 0)
            return [];

        // Expand one triangle hop around seeds to capture full socket ring.
        if (_triangles is { Length: > 0 })
        {
            var expanded = new HashSet<int>(seeds);
            foreach (var (a, b, c) in _triangles)
            {
                if (seeds.Contains(a) || seeds.Contains(b) || seeds.Contains(c))
                {
                    expanded.Add(a);
                    expanded.Add(b);
                    expanded.Add(c);
                }
            }
            seeds = expanded;
        }

        Vector2 center = Vector2.Zero;
        if (fpValid.Count > 0)
        {
            foreach (var p in fpValid) center += p;
            center /= fpValid.Count;
        }

        return seeds
            .OrderBy(i =>
            {
                Vector2 mv = meshVerts[i];
                float dx = mv.X - center.X;
                float dy = mv.Y - center.Y;
                return dx * dx + dy * dy;
            })
            .Take(18)
            .ToArray();
    }

    private static float ComputeMeshCenterX(Vector2[] meshVerts)
    {
        float sx = 0f;
        int n = 0;
        foreach (var v in meshVerts)
        {
            if (v == Vector2.Zero) continue;
            sx += v.X;
            n++;
        }
        return n > 0 ? sx / n : 320f;
    }

    private int[] BuildSocketBySide(Vector2[] meshVerts, float centerX, bool left)
    {
        float expY = _meshTop + _meshHeight * 0.42f;
        float expX = left
            ? _meshLeft + _meshWidth * 0.36f
            : _meshLeft + _meshWidth * 0.64f;

        return meshVerts
            .Select((v, i) => (v, i))
            .Where(t => t.v != Vector2.Zero)
            .Where(t => left ? t.v.X <= centerX : t.v.X >= centerX)
            .OrderBy(t =>
            {
                float dx = t.v.X - expX;
                float dy = t.v.Y - expY;
                return dx * dx + dy * dy;
            })
            .Take(14)
            .Select(t => t.i)
            .ToArray();
    }

    private static bool TryFeatureEyeCenter(
        Vector2[] featurePoints,
        int[] indices,
        double fitScale,
        double offX,
        double offY,
        out Point center)
    {
        double sx = 0, sy = 0;
        int n = 0;
        foreach (int i in indices)
        {
            if ((uint)i >= (uint)featurePoints.Length) continue;
            Vector2 fp = featurePoints[i];
            if (fp == Vector2.Zero) continue;
            sx += fp.X * fitScale + offX;
            sy += fp.Y * fitScale + offY;
            n++;
        }

        if (n < 3)
        {
            center = default;
            return false;
        }

        center = new Point(sx / n, sy / n);
        return true;
    }
}
