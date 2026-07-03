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
using System.Windows.Media;
using HCEP.Core.Models;
using HCEP.Spatial;

namespace HCEP.App;

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

    private static readonly Brush _pupilBrush;

    // ── Eye sphere rendering brushes (frozen) ────────────────────
    private static readonly Brush _irisRingBrush;
    private static readonly Brush _pupilDotBrush;
    private static readonly Brush _specularBrush;
    private static readonly Pen _eyeOutlinePen;
    private static readonly Pen _irisOutlinePen;

    static Avatar3DControl()
    {
        _wirePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 220, 190)), 1.2);
        _wirePen.Freeze();
        _pupilBrush = new SolidColorBrush(Color.FromArgb(255, 0, 220, 190));
        _pupilBrush.Freeze();

        // Iris: teal ring (matches wireframe accent)
        _irisRingBrush = new SolidColorBrush(Color.FromArgb(220, 0, 180, 160));
        _irisRingBrush.Freeze();
        // Pupil: dark center
        _pupilDotBrush = new SolidColorBrush(Color.FromArgb(255, 8, 12, 18));
        _pupilDotBrush.Freeze();
        // Specular highlight
        _specularBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        _specularBrush.Freeze();
        // Subtle outline for eye sphere
        _eyeOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 220, 190)), 0.8);
        _eyeOutlinePen.Freeze();
        // Subtle iris ring outline
        _irisOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 140, 130)), 0.6);
        _irisOutlinePen.Freeze();
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
    private float _trackedHeadYawRad;
    private float _trackedHeadPitchRad;
    private float _trackedHeadRollRad;
    private float _bakedHeadYawRad;
    private float _bakedHeadPitchRad;
    private float _bakedHeadRollRad;
    private long _lastHeadPoseTicks;
    private bool _headPoseInitialized;

    // ── Graceful float state ──────────────────────────────────────────────
    // The wireframe head gently mirrors a fraction of the user’s real head rotation.
    // TrackingInfluence: how much of the Kinect rotation bleeds into the display pose
    //   (0.15 = 15% — subtle but visible; gives the avatar a sense of awareness).
    // FloatAmplitude: master scale for the slow sinusoidal drift (~3–4° breathing).
    private const float TrackingInfluence = 0.15f;
    private const float FloatAmplitudeScale = 1.0f;
    private long _floatOriginMs;

    // ── Mesh status (surfaced to AvatarWindow HUD) ───────────────────────
    public int MeshVertexCount { get; private set; }
    public int MeshTriangleCount { get; private set; }

    // ── Cached eye socket screen positions (for GazeVectorEngine eye provider) ──
    private Point _leftEyeLocalPt;    // updated each OnRender frame
    private Point _rightEyeLocalPt;
    public Point LeftEyeScreenPos { get; private set; }
    public Point RightEyeScreenPos { get; private set; }

    // ── Eye-socket smoothing state ─────────────────────────────────────────────
    private Point _leftEyeSocketSmoothed;
    private Point _rightEyeSocketSmoothed;
    private bool _eyeSocketSmoothingReady;

    // ── Micro-saccade engine state ──────────────────────────────────────
    // Simulates natural inter-eye saccades (shifting gaze between the user's
    // left and right eye sockets) and micro-saccade jitter during fixation.
    private static readonly Random _saccadeRng = new();
    private bool _saccadeTargetLeft = true;       // which user eye is the current target
    private long _nextSaccadeMs;                  // tick64 for next inter-eye switch
    private double _saccadeSmoothedYaw;           // exponentially smoothed inter-eye offset
    private double _microTargetX, _microTargetY;  // current micro-jitter target (normalised)
    private double _microSmoothedX, _microSmoothedY; // smoothed micro-jitter
    private long _nextMicroSaccadeMs;             // tick64 for next micro-jitter change
    private long _lastSaccadeUpdateMs;            // for framerate-independent smoothing

    // ── Gaze-driven head turning (eye-contingent head rotation) ────────────
    // When eyes exceed 80% of max gaze angle, head rotates proportionally
    // to create the illusion of the avatar turning its head.
    private GazeHeadFollower _gazeHeadFollower = null!;
    private long _lastGazeHeadFollowerUpdateMs;    // for framerate-independent updates

    // ── Construction ─────────────────────────────────────────────────────
    public Avatar3DControl()
    {
        _floatOriginMs = Environment.TickCount64;
        _lastGazeHeadFollowerUpdateMs = Environment.TickCount64;

        // Initialize the gaze head follower with mesh-mode max gaze angle (20°)
        const float MaxGazeAngleRad = MathF.PI / 9.0f;  // 20 degrees
        _gazeHeadFollower = new GazeHeadFollower(MaxGazeAngleRad);

        // Re-resolve screen coords whenever layout changes — same pattern as AvatarCoreControl.
        LayoutUpdated += (_, _) => UpdateEyeScreenCoordinates();

        // Low-frequency timer keeps micro-saccade animation fluid even during
        // brief sensor data gaps. WPF coalesces repeated InvalidateVisual calls.
        var saccadeTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33), // ~30 Hz
        };
        saccadeTimer.Tick += (_, _) => InvalidateVisual();
        saccadeTimer.Start();
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
    public void SetMesh(Vector2[] vertices, (int First, int Second, int Third)[] triangles, Vector3 bakedRotationDeg)
    {
        const float Deg2Rad = MathF.PI / 180f;
        _bakedHeadYawRad = bakedRotationDeg.Y * Deg2Rad;
        _bakedHeadPitchRad = bakedRotationDeg.X * Deg2Rad;
        _bakedHeadRollRad = bakedRotationDeg.Z * Deg2Rad;

        bool topologyChanged = _vertices is null || _vertices.Length != vertices.Length;

        _vertices = vertices;
        _triangles = triangles.Select(t => (t.First, t.Second, t.Third)).ToArray();
        MeshVertexCount = vertices.Length;
        MeshTriangleCount = triangles.Length;

        if (topologyChanged)
        {
            _eyeSocketSmoothingReady = false;
            // Also zero the smoothed positions so they don't seed the EMA from
            // stale coordinates on the very next frame after topology changes.
            _leftEyeSocketSmoothed = default;
            _rightEyeSocketSmoothed = default;
        }

        ComputeBounds();
        InvalidateVisual();
    }

    /// <summary>
    /// Feature-point fallback: renders a dot cloud using the 87 FaceTrackLib landmark
    /// points when the full <c>GetProjectedShape</c> mesh is not yet available.
    /// Called from <c>AvatarWindow.OnSnapshotReady</c> when <c>FaceMeshVertices2D</c> is null.
    /// </summary>
    public void SetFeaturePoints(Vector2[] points, Vector3 bakedRotationDeg)
    {
        const float Deg2Rad = MathF.PI / 180f;
        _bakedHeadYawRad = bakedRotationDeg.Y * Deg2Rad;
        _bakedHeadPitchRad = bakedRotationDeg.X * Deg2Rad;
        _bakedHeadRollRad = bakedRotationDeg.Z * Deg2Rad;

        _vertices = points;
        _triangles = null;   // null = edge-chain fallback mode
        MeshVertexCount = 0;
        MeshTriangleCount = 0;
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

        // Scale raw tracking down to TrackingInfluence fraction so the head
        // remains nearly still — only a faint 4% ghost of the real rotation
        // reaches the display pose.  The remainder of the motion is a
        // graceful sinusoidal float applied in OnRender.
        float targetYaw = rotationDeg.Y * Deg2Rad * TrackingInfluence;
        float targetPitch = rotationDeg.X * Deg2Rad * TrackingInfluence;
        float targetRoll = rotationDeg.Z * Deg2Rad * TrackingInfluence;

        _trackedHeadYawRad = targetYaw;
        _trackedHeadPitchRad = targetPitch;
        _trackedHeadRollRad = targetRoll;

        long now = Environment.TickCount64;
        if (!_headPoseInitialized)
        {
            _headYawRad = targetYaw;
            _headPitchRad = targetPitch;
            _headRollRad = targetRoll;
            _headPoseInitialized = true;
            _lastHeadPoseTicks = now;
            InvalidateVisual();
            return;
        }

        double dt = Math.Clamp((now - _lastHeadPoseTicks) / 1000.0, 0.0, 0.20);
        _lastHeadPoseTicks = now;

        // Very slow follow — the 15% residual moves smoothly so
        // tracking jitter is absorbed before reaching the display.
        const float EyeLeadDeadzoneRad = 5.0f * (MathF.PI / 180f);
        const float HeadFollowTimeConstantSec = 0.8f;   // gently responsive: head reaches target in ~2.4s

        float followAlpha = (float)(1.0 - Math.Exp(-dt / HeadFollowTimeConstantSec));

        _headYawRad = StepHeadFollow(_headYawRad, targetYaw, followAlpha, EyeLeadDeadzoneRad);
        _headPitchRad = StepHeadFollow(_headPitchRad, targetPitch, followAlpha, EyeLeadDeadzoneRad * 0.85f);
        _headRollRad = StepHeadFollow(_headRollRad, targetRoll, followAlpha, EyeLeadDeadzoneRad);

        // In fallback mode, head rotation is rendered by OnRender.
        // In full-mesh mode, rotation is baked into GetProjectedShape vertices.
        InvalidateVisual();
    }

    // IAvatarComponent
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze(p, y, d);
    void IAvatarComponent.ResetGaze()
    {
        _gazeHeadFollower.Reset();
        _vertices = null;
        _triangles = null;
        _eyeSocketSmoothingReady = false;
        _gazePitch = 0;
        _gazeYaw = 0;
        _bakedHeadYawRad = 0;
        _bakedHeadPitchRad = 0;
        _bakedHeadRollRad = 0;
        InvalidateVisual();
    }

    // ── Render ───────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        // ── Update gaze-driven head follower ────────────────────────────────
        // This must happen before we use _headYawRad/_headPitchRad for rendering.
        // We pass 0f, 0f for user head pose so the follower calculates its target
        // purely relative to the camera gaze direction.
        long now = Environment.TickCount64;
        float elapsedMs = (float)Math.Clamp(now - _lastGazeHeadFollowerUpdateMs, 0, 200);
        _lastGazeHeadFollowerUpdateMs = now;
        _gazeHeadFollower.Update(elapsedMs, _gazeYaw, _gazePitch, 0f, 0f);

        // Transparent background — inherits dark window colour.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Snapshot volatile references (written from background threads).
        var vertices = _vertices;
        var triangles = _triangles;
        var featurePoints = _featurePoints;

        if (vertices is null || vertices.Length == 0)
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
        bool hasMesh = triangles is not null;

        // ── Transform parameters (full-mesh vs feature-point fallback) ───
        // Full-mesh mode: apply a corrective transform from tracked pose toward
        // smoothed display pose so eyes visibly lead before head catches up.
        // Fallback mode : use the smoothed display head rotation directly.
        //   yaw  → X-compress (cos) + slight X-shift (sin)
        //   pitch → Y-shift
        //   roll  → rotation around face centre
        double meshCentreX = w / 2.0;
        double meshCentreY = h / 2.0;

        // ── Graceful float overlay ────────────────────────────────────────
        // Three incommensurable sinusoidal periods (7.3 s, 11.1 s, 14.7 s)
        // produce a slow, non-repeating breathing drift with ~3–4° amplitude.
        // Phase offsets prevent yaw/pitch/roll from peaking simultaneously.
        double _floatT = (Environment.TickCount64 - _floatOriginMs) / 1000.0;
        double floatYaw = FloatAmplitudeScale * 0.055 * Math.Sin(2 * Math.PI * _floatT / 7.3);
        double floatPitch = FloatAmplitudeScale * 0.040 * Math.Sin(2 * Math.PI * _floatT / 11.1 + 0.8);
        double floatRoll = FloatAmplitudeScale * 0.018 * Math.Sin(2 * Math.PI * _floatT / 14.7 + 1.5);

        // ── Apply gaze-driven head rotation if active ──────────────────────
        // If the gaze follower is active (eyes exceeded threshold), use its
        // computed head rotation instead of Kinect tracking. This creates
        // the illusion of the avatar turning its head to follow the eyes.
        var gazeHeadPose = _gazeHeadFollower.GetTargetHeadPose();
        double headYaw, headPitch, headRoll;

        if (gazeHeadPose.IsActive)
        {
            // Gaze-driven mode: override Kinect tracking, use gaze-induced rotation
            headYaw = gazeHeadPose.YawRad + floatYaw;
            headPitch = gazeHeadPose.PitchRad + floatPitch;
            headRoll = gazeHeadPose.RollRad + floatRoll;
        }
        else
        {
            // Normal mode: use Kinect tracking with graceful float
            headYaw = _headYawRad + floatYaw;
            headPitch = _headPitchRad + floatPitch;
            headRoll = _headRollRad + floatRoll;
        }

        double correctionYaw = Math.Clamp(headYaw - _bakedHeadYawRad, -Math.PI / 3, Math.PI / 3);
        double correctionPitch = Math.Clamp(headPitch - _bakedHeadPitchRad, -Math.PI / 4, Math.PI / 4);
        double correctionRoll = Math.Clamp(headRoll - _bakedHeadRollRad, -Math.PI / 4, Math.PI / 4);

        bool applyHeadTransform = !hasMesh
            || Math.Abs(correctionYaw) > 0.0005
            || Math.Abs(correctionPitch) > 0.0005
            || Math.Abs(correctionRoll) > 0.0005;

        double yawCompress = Math.Cos(Math.Clamp(correctionYaw, -Math.PI / 3, Math.PI / 3));
        double yawShiftX = Math.Sin(-correctionYaw) * w * 0.12;   // lateral shift with yaw
        double pitchShift = Math.Sin(-correctionPitch) * h * 0.10;  // vertical shift with pitch
        double rollAngle = correctionRoll;

        // ── Vertex mapping ───────────────────────────────────────────────
        // Converts a mesh-space vertex index to screen-space Point.
        // Fallback mode applies: yaw-compress, yaw-shift, pitch-shift, then roll.
        Point Map(int idx)
        {
            if ((uint)idx >= (uint)vertices.Length) return new Point(0, 0);
            Vector2 v = vertices[idx];
            double x = v.X * fitScale + offX;
            double y = v.Y * fitScale + offY + pitchShift;
            if (applyHeadTransform)
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
            foreach (var (a, b, c) in triangles!)
            {
                if ((uint)a >= (uint)vertices.Length ||
                    (uint)b >= (uint)vertices.Length ||
                    (uint)c >= (uint)vertices.Length)
                    continue;

                dc.DrawLine(_wirePen, Map(a), Map(b));
                dc.DrawLine(_wirePen, Map(b), Map(c));
                dc.DrawLine(_wirePen, Map(c), Map(a));
            }
        }
        else
        {
            // Feature-point wireframe: Kinect 87-point edge chains (fallback).
            foreach (var chain in FaceTopology.ExtendedChains)
            {
                for (int i = 0; i < chain.Length - 1; i++)
                {
                    int a = chain[i], b = chain[i + 1];
                    if (a >= vertices.Length || b >= vertices.Length) continue;
                    Vector2 va = vertices[a], vb = vertices[b];
                    if (va == Vector2.Zero || vb == Vector2.Zero) continue;
                    dc.DrawLine(_wirePen, Map(a), Map(b));
                }
            }
        }

        // ── Eye socket positioning — proportional placement ────────────
        // Uses the same eye/face proportions as the 2D Happy Face avatar:
        //   Happy Face canvas = 280×280
        //   Right eye = (95, 112) → (0.339, 0.400)
        //   Left eye  = (185, 112) → (0.661, 0.400)
        //   Eye radius = 22 → 0.0786 of face width
        // These proportions are applied to the mesh bounding box, then
        // transformed through the same wireframe pipeline (fitScale, offsets,
        // yaw-compress, pitch-shift, roll). No feature-point dependency —
        // eliminates the FP↔mesh projection mismatch that caused crashes.
        bool eyesDrawn = false;

        if (hasMesh)
        {
            // Happy-face baseline + small wireframe calibration.
            const double BaseREyeXFrac = 95.0 / 280.0;    // 0.339
            const double BaseLEyeXFrac = 185.0 / 280.0;   // 0.661
            const double BaseEyeYFrac = 112.0 / 280.0;    // 0.400
            const double EyeRFrac = 20.5 / 280.0;         // 0.073
            const double HorizontalSpreadFrac = 0.016;    // push eyes slightly outward
            const double VerticalDropFrac = 0.018;        // lower eye plane slightly

            double rEyeXFrac = BaseREyeXFrac - HorizontalSpreadFrac;
            double lEyeXFrac = BaseLEyeXFrac + HorizontalSpreadFrac;
            double eyeYFrac = BaseEyeYFrac + VerticalDropFrac;

            // Map proportions into mesh-vertex coordinate space
            double rCxMesh = _meshLeft + rEyeXFrac * _meshWidth;
            double rCyMesh = _meshTop + eyeYFrac * _meshHeight;
            double lCxMesh = _meshLeft + lEyeXFrac * _meshWidth;
            double lCyMesh = _meshTop + eyeYFrac * _meshHeight;

            // Transform to screen space — same pipeline as wireframe vertices
            Point MapCoord(double vx, double vy)
            {
                double x2 = vx * fitScale + offX;
                double y2 = vy * fitScale + offY + pitchShift;
                if (applyHeadTransform)
                {
                    x2 = meshCentreX + (x2 - meshCentreX) * yawCompress + yawShiftX;
                    if (Math.Abs(rollAngle) > 0.001)
                    {
                        double dx2 = x2 - meshCentreX;
                        double dy2 = y2 - meshCentreY;
                        double cosR = Math.Cos(rollAngle);
                        double sinR = Math.Sin(rollAngle);
                        x2 = meshCentreX + dx2 * cosR - dy2 * sinR;
                        y2 = meshCentreY + dx2 * sinR + dy2 * cosR;
                    }
                }
                return new Point(x2, y2);
            }

            Point rightAnchorRaw = MapCoord(rCxMesh, rCyMesh);
            Point leftAnchorRaw = MapCoord(lCxMesh, lCyMesh);

            // Keep eye centres locked to head motion with minimal lag.
            // Using near-instant smoothing preserves stability but follows
            // pan/tilt/roll immediately.
            const double Alpha = 0.98;
            if (!_eyeSocketSmoothingReady)
            {
                _leftEyeSocketSmoothed = leftAnchorRaw;
                _rightEyeSocketSmoothed = rightAnchorRaw;
                _eyeSocketSmoothingReady = true;
            }
            else
            {
                _leftEyeSocketSmoothed = new Point(
                    _leftEyeSocketSmoothed.X + (leftAnchorRaw.X - _leftEyeSocketSmoothed.X) * Alpha,
                    _leftEyeSocketSmoothed.Y + (leftAnchorRaw.Y - _leftEyeSocketSmoothed.Y) * Alpha);
                _rightEyeSocketSmoothed = new Point(
                    _rightEyeSocketSmoothed.X + (rightAnchorRaw.X - _rightEyeSocketSmoothed.X) * Alpha,
                    _rightEyeSocketSmoothed.Y + (rightAnchorRaw.Y - _rightEyeSocketSmoothed.Y) * Alpha);
            }

            Point leftAnchor = _leftEyeSocketSmoothed;
            Point rightAnchor = _rightEyeSocketSmoothed;

            // Eye radius in screen space (proportional to face width).
            // Cap more conservatively to avoid overlap on narrow sockets.
            double interEye = Math.Max(Math.Abs(rightAnchor.X - leftAnchor.X), 20.0);
            double eyeR = Math.Clamp(_meshWidth * EyeRFrac * fitScale, 7.0, interEye * 0.24);

            // ── Gaze computation ──────────────────────────────────────

            const double MaxGazeAngle = Math.PI / 9.0;
            // Use the float-adjusted head pose so pupils remain naturally centred
            // as the head drifts through its breathing cycle.
            double eyeRelativeYaw = _gazeYaw - (headYaw * 0.75);
            double eyeRelativePitch = _gazePitch - (headPitch * 0.55);
            double normYaw = Math.Clamp(eyeRelativeYaw, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;
            double normPitch = Math.Clamp(eyeRelativePitch, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;

            var (saccYaw, saccPitch) = UpdateSaccade();
            normYaw = Math.Clamp(normYaw + saccYaw, -1.0, 1.0);
            normPitch = Math.Clamp(normPitch + saccPitch, -1.0, 1.0);

            double rotYaw = Math.Sin(normYaw * (Math.PI / 2.0));
            double rotPitch = Math.Sin(normPitch * (Math.PI / 2.0));

            eyesDrawn = true;

            // Lock eyeball centres to socket anchors; only iris/pupil rotates.
            DrawEyeSphere(dc, leftAnchor.X, leftAnchor.Y, eyeR, rotYaw, rotPitch);
            DrawEyeSphere(dc, rightAnchor.X, rightAnchor.Y, eyeR, rotYaw, rotPitch);

            _leftEyeLocalPt = leftAnchor;
            _rightEyeLocalPt = rightAnchor;
        }

        if (!eyesDrawn && featurePoints is { Length: > 35 })
        {
            // MapFP: applies fit-to-bounds transform to a feature-point index.
            // Both FaceMeshVertices2D and FeaturePoints2D live in the same 640×480
            // projected space, so we use the same fitScale/offX/offY offsets.
            Point MapFP(int idx)
            {
                if (idx >= featurePoints.Length || featurePoints[idx] == Vector2.Zero)
                    return new Point(double.NaN, double.NaN);
                Vector2 v = featurePoints[idx];
                double fx = v.X * fitScale + offX;
                double fy = v.Y * fitScale + offY + pitchShift;
                if (applyHeadTransform)
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

                // Micro-saccade: reuse the same engine for fallback mode.
                var (saccYawFb, saccPitchFb) = UpdateSaccade();
                normYaw = Math.Clamp(normYaw + saccYawFb, -1.0, 1.0);
                normPitch = Math.Clamp(normPitch + saccPitchFb, -1.0, 1.0);

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

                // ── Eyeball sphere radius for fallback mode ──
                double eyeR = Math.Max(Math.Min(rHW, lHW) * 0.70, 6.0);
                double rotYaw = Math.Sin(normYaw * (Math.PI / 2.0));
                double rotPitch = Math.Sin(normPitch * (Math.PI / 2.0));

                DrawEyeSphere(dc, rpx, rpy, eyeR, rotYaw, rotPitch);
                DrawEyeSphere(dc, lpx, lpy, eyeR, rotYaw, rotPitch);

                // Cache local-space socket centres for LayoutUpdated → screen coord tracking.
                _rightEyeLocalPt = new Point(rcx, rcy);
                _leftEyeLocalPt = new Point(lcx, lcy);
            }
        }
    }

    // ── Micro-Saccade Engine ────────────────────────────────────

    /// <summary>
    /// Computes micro-saccade offsets for this render frame.
    /// Returns (yawOffset, pitchOffset) in normalised [-1, 1] space
    /// to be added to the pupil position before the sin() rotation mapping.
    ///
    /// Behaviour:
    ///   - Every 1.2–3.5 s the target jumps between the user's left and right
    ///     eye socket (small horizontal offset, ~5 % of full travel).
    ///   - Every 300–900 ms a micro-saccade jitter of ±1.5 % (yaw) / ±1 % (pitch)
    ///     simulates natural fixation instability.
    ///   - All transitions use framerate-independent exponential smoothing:
    ///     inter-eye saccade τ = 50 ms (fast ballistic), micro-jitter τ = 200 ms (gentle drift).
    /// </summary>
    private (double yawOffset, double pitchOffset) UpdateSaccade()
    {
        long now = Environment.TickCount64;

        // Framerate-independent delta time (seconds)
        double dt = _lastSaccadeUpdateMs > 0
            ? Math.Clamp((now - _lastSaccadeUpdateMs) / 1000.0, 0.001, 0.2)
            : 0.033;
        _lastSaccadeUpdateMs = now;

        // ── Inter-eye saccade (left eye ↔ right eye) ─────────────
        if (now >= _nextSaccadeMs)
        {
            _saccadeTargetLeft = !_saccadeTargetLeft;
            _nextSaccadeMs = now + 1200 + _saccadeRng.Next(2300); // 1.2–3.5 s
        }

        // ── Micro-saccade jitter ─────────────────────────────────
        if (now >= _nextMicroSaccadeMs)
        {
            _microTargetX = (_saccadeRng.NextDouble() - 0.5) * 0.03;  // ±1.5 %
            _microTargetY = (_saccadeRng.NextDouble() - 0.5) * 0.02;  // ±1.0 %
            _nextMicroSaccadeMs = now + 300 + _saccadeRng.Next(600);   // 300–900 ms
        }

        // Exponential smoothing — time-constant based for framerate independence.
        double saccadeAlpha = 1.0 - Math.Exp(-dt / 0.05);  // τ = 50 ms (ballistic)
        double microAlpha = 1.0 - Math.Exp(-dt / 0.20);  // τ = 200 ms (gentle)

        // Inter-eye horizontal offset: ~5 % of normalised gaze range ≈ 1° at 1.5 m.
        double interEyeTarget = _saccadeTargetLeft ? -0.05 : 0.05;
        _saccadeSmoothedYaw += (interEyeTarget - _saccadeSmoothedYaw) * saccadeAlpha;

        _microSmoothedX += (_microTargetX - _microSmoothedX) * microAlpha;
        _microSmoothedY += (_microTargetY - _microSmoothedY) * microAlpha;

        return (_saccadeSmoothedYaw + _microSmoothedX, _microSmoothedY);
    }

    // ── Eye Sphere Rendering ───────────────────────────────────

    /// <summary>
    /// Draws a 3D eyeball sphere at (<paramref name="cx"/>, <paramref name="cy"/>)
    /// with given radius. The pupil sits on the sphere's meridian and is offset
    /// by <paramref name="rotYaw"/> (horizontal) and <paramref name="rotPitch"/>
    /// (vertical), both in [-1, 1] normalised range.
    ///
    /// Visual layers (back to front):
    ///   1. Sclera sphere — radial gradient (white centre → shadow at rim)
    ///   2. Iris ring — foreshortened ellipse on the sphere surface
    ///   3. Pupil — dark centre of the iris
    ///   4. Specular highlight — small white dot offset upper-left
    /// </summary>
    private static void DrawEyeSphere(
        DrawingContext dc, double cx, double cy, double radius,
        double rotYaw, double rotPitch)
    {
        // ── 1. Sclera (eyeball sphere) ──────────────────────────
        // Radial gradient simulates a convex sphere lit from the front.
        // GradientOrigin is shifted slightly toward the light (upper-left)
        // for a realistic highlight-to-shadow falloff.
        var scleraGradient = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.35),  // light from upper-left
            Center = new Point(0.5, 0.5),
            RadiusX = 0.55,
            RadiusY = 0.55,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 230, 240, 240), 0.0),   // bright white core
                new GradientStop(Color.FromArgb(255, 180, 200, 200), 0.50),  // mid-tone
                new GradientStop(Color.FromArgb(255, 60,  80,  85),  0.85),  // shadow at rim
                new GradientStop(Color.FromArgb(200, 20,  30,  35),  1.0),   // dark edge
            ],
        };
        scleraGradient.Freeze();

        dc.DrawEllipse(scleraGradient, _eyeOutlinePen, new Point(cx, cy), radius, radius);

        // ── 2. Iris + Pupil on the sphere surface ───────────────
        // The iris centre is offset from the eyeball centre by the gaze angles.
        // As the eye rotates, the iris also foreshortens (becomes narrower along
        // the axis of rotation) — this is what makes it look like a circle painted
        // on a sphere rather than a flat sticker.

        double irisR = radius * 0.50;   // iris radius relative to eyeball
        double pupilR = radius * 0.25;  // pupil (dark centre)

        // Offset: pupil travel on the sphere surface. Max travel = ~60% of radius
        // so the iris doesn't clip outside the sclera at extreme gaze.
        double maxTravel = radius * 0.42;
        double irisOffX = rotYaw * maxTravel;
        double irisOffY = -rotPitch * maxTravel; // pitch+= up → -Y

        double irisCX = cx + irisOffX;
        double irisCY = cy + irisOffY;

        // Foreshortening: when the eye looks sideways, the iris appears as an
        // ellipse (compressed along the rotation axis). The compression factor
        // is cos(angle). We derive the angle from the normalised rotation.
        double yawAngle = rotYaw * (Math.PI / 2.0);
        double pitchAngle = rotPitch * (Math.PI / 2.0);
        double foreshortX = Math.Max(Math.Cos(yawAngle), 0.35);   // min 35% to stay visible
        double foreshortY = Math.Max(Math.Cos(pitchAngle), 0.35);

        double irisRX = irisR * foreshortX;
        double irisRY = irisR * foreshortY;
        double pupilRX = pupilR * foreshortX;
        double pupilRY = pupilR * foreshortY;

        // Iris ring gradient (teal → darker edge)
        var irisGradient = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.45, 0.40),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 0, 200, 175), 0.0),     // bright teal centre
                new GradientStop(Color.FromArgb(255, 0, 160, 140), 0.55),    // mid iris
                new GradientStop(Color.FromArgb(255, 0, 100, 90),  1.0),     // dark iris rim
            ],
        };
        irisGradient.Freeze();

        dc.DrawEllipse(irisGradient, _irisOutlinePen, new Point(irisCX, irisCY), irisRX, irisRY);

        // ── 3. Pupil (dark centre of iris) ──────────────────────
        dc.DrawEllipse(_pupilDotBrush, null, new Point(irisCX, irisCY), pupilRX, pupilRY);

        // ── 4. Specular highlight ───────────────────────────────
        // Fixed position relative to the eyeball (not the iris) to simulate
        // a stationary light source. Offset upper-left, small radius.
        double specR = radius * 0.14;
        double specX = cx - radius * 0.22;
        double specY = cy - radius * 0.25;
        dc.DrawEllipse(_specularBrush, null, new Point(specX, specY), specR, specR * 0.85);
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

    private static float StepHeadFollow(float current, float target, float alpha, float deadzone)
    {
        float delta = target - current;
        float abs = MathF.Abs(delta);

        if (abs <= deadzone)
            return current;

        float beyond = abs - deadzone;
        float step = beyond * Math.Clamp(alpha, 0f, 1f);
        return current + MathF.CopySign(step, delta);
    }

}
