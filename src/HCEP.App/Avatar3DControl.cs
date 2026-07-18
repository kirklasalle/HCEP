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
    private static readonly Pen _browPen;  // slightly thicker than wirePen for legibility

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
        _browPen = new Pen(new SolidColorBrush(Color.FromArgb(240, 0, 220, 190)), 1.8);
        _browPen.Freeze();

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

    /// <summary>
    /// When true, avatar mirrors user's expressions/brows/gaze. When false,
    /// avatar operates autonomously, using proportional eye socket positions.
    /// </summary>
    public bool IsMirroringEnabled { get; set; }

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

    // ── Eyebrow state ────────────────────────────────────────────────
    // Smoothed AU-driven brow targets. Set via SetBrows(); rendered in OnRender.
    private float _browRaiseTarget3D;
    private float _browFurrowTarget3D;
    private double _browRaiseSmoothed3D;
    private double _browFurrowSmoothed3D;
    private long _lastBrowTicks3D;

    // ── Backchannel nod state (Phase 10) ────────────────────────────────────
    // A nod adds a sin(π·t) pitch offset to headPitch for 500 ms.
    private long _nodStartMs3D = -1;
    private const double NodDuration3DMs = 500.0;
    private const float NodAmplitudePitch3DRad = 0.14f; // ~8°

    // ── Head tilt state (Phase 10) ───────────────────────────────────────────
    private long _tiltStartMs3D = -1;
    private float _tiltRollRad3D;
    private const double TiltDuration3DMs = 600.0;

    // ── Expression mirror state (Phase 10) ──────────────────────────────────
    private float _smileTarget3D;
    private double _smileSmoothed3D;
    private long _lastSmileTicks3D;

    // ── Social gaze offset (Phase 10) ────────────────────────────────────────
    private float _socialGazeYaw3D;
    private float _socialGazePitch3D;

    // ── Proxemic state (Phase 10) ─────────────────────────────────────────────
    private float _proxemicDistM3D = 1.5f;

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

        // Listen for eye position calibration adjustments to redraw the avatar in real time
        EyePositionCalibration.Changed += () => Dispatcher.BeginInvoke(InvalidateVisual);

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
    /// Feature-point-only frame update. Called when the SDK's <c>GetProjectedShape</c>
    /// mesh call fails but the 87 FaceTrackLib landmark points are still valid.
    ///
    /// Behavior (persistent-mesh contract):
    ///   • If a Candide-3 mesh has EVER been acquired (<c>_triangles</c> non-null),
    ///     the persistent mesh geometry is left completely untouched. Only
    ///     <c>_featurePoints</c> is refreshed so eye-socket anchoring stays live.
    ///   • If no mesh has ever been acquired (cold start), the FP array drives
    ///     the wireframe as a fallback surface: <c>_vertices</c>, baked rotation,
    ///     and mesh bounds are refreshed every frame so the wireframe moves
    ///     with the user until a real mesh arrives.
    /// </summary>
    public void SetFeaturePoints(Vector2[] points, Vector3 bakedRotationDeg)
    {
        // Keep the FP array live for eye-socket anchoring — happens in both modes.
        _featurePoints = points;

        if (_triangles is null)
        {
            // Cold-start / mesh-never-acquired: FP is the current source of truth
            // for the wireframe. Refresh vertices + baked rotation every frame so
            // the drawn face tracks live head movement.
            const float Deg2Rad = MathF.PI / 180f;
            _bakedHeadYawRad = bakedRotationDeg.Y * Deg2Rad;
            _bakedHeadPitchRad = bakedRotationDeg.X * Deg2Rad;
            _bakedHeadRollRad = bakedRotationDeg.Z * Deg2Rad;

            bool topologyChanged = _vertices is null || _vertices.Length != points.Length;
            _vertices = points;
            if (topologyChanged)
            {
                _eyeSocketSmoothingReady = false;
                _leftEyeSocketSmoothed = default;
                _rightEyeSocketSmoothed = default;
            }
            ComputeBounds();
        }
        // else: persistent-mesh contract in force — do not touch _vertices,
        // _triangles, or baked rotation. Live head pose flows via SetHeadPose;
        // eye anchoring flows via the _featurePoints update above.

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
    void IAvatarComponent.SetViseme(HCEP.Speech.VisemeData v) => SetViseme(v);
    void IAvatarComponent.SetBrows(float raise, float lower, float modeFurrow) => SetBrows(raise, lower, modeFurrow);
    void IAvatarComponent.ResetGaze()
    {
        _gazeHeadFollower.Reset();
        _eyeSocketSmoothingReady = false;
        _gazePitch = 0;
        _gazeYaw = 0;
        _bakedHeadYawRad = 0;
        _bakedHeadPitchRad = 0;
        _bakedHeadRollRad = 0;
        InvalidateVisual();
    }

    /// <summary>
    /// Sets eyebrow animation targets from Kinect Action Units and HCEP mode.
    /// Blended values are rendered in the next <c>OnRender</c> frame.
    /// </summary>
    public void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f)
    {
        _browRaiseTarget3D = Math.Clamp(outerBrowRaise, 0f, 1f);
        float auFurrow = Math.Clamp(-browLower, 0f, 1f);
        _browFurrowTarget3D = Math.Max(auFurrow, hcepModeFurrow);
    }

    // ── Viseme / lip-sync state ──────────────────────────────────────────────
    private HCEP.Speech.VisemeData _visemeTarget3D = HCEP.Speech.VisemeData.Silence;
    private double _visemeJaw3D;
    private double _visemeRound3D;
    private long _lastVisemeTicks3D;

    /// <summary>
    /// Updates mouth animation for the 3D wireframe avatar from a TTS viseme event.
    /// Draws a proportional open-mouth arc below the nose, scaled to the face.
    /// </summary>
    public void SetViseme(HCEP.Speech.VisemeData viseme)
    {
        _visemeTarget3D = viseme;
        InvalidateVisual();
    }

    /// <summary>
    /// Phase 10 — Triggers a single backchannel nod: a 500 ms sin(π·t) forward-pitch
    /// pulse on the avatar head. Thread-safe; dispatches InvalidateVisual internally.
    /// </summary>
    public void TriggerNod()
    {
        _nodStartMs3D = Environment.TickCount64;
        Dispatcher.BeginInvoke(InvalidateVisual);
    }

    /// <summary>Phase 10 — Triggers a brief 600 ms head-tilt roll animation.</summary>
    public void TriggerTilt(float rollDeg = 6f)
    {
        _tiltRollRad3D = rollDeg * (MathF.PI / 180f);
        _tiltStartMs3D = Environment.TickCount64;
        Dispatcher.BeginInvoke(InvalidateVisual);
    }

    /// <summary>Phase 10 — Expression Mirror: sets the avatar smile target [0..1].</summary>
    public void SetSmile(float intensity) =>
        _smileTarget3D = Math.Clamp(intensity, 0f, 1f);

    /// <summary>Phase 10 — Social Gaze Controller: applies gaze offset (radians).</summary>
    public void SetSocialGazeOffset(float yawRad, float pitchRad)
    {
        _socialGazeYaw3D = yawRad;
        _socialGazePitch3D = pitchRad;
    }

    /// <summary>Phase 10 — Proxemic Response: updates user distance for pupil dilation.</summary>
    public void SetProxemicDistance(float distanceM) =>
        _proxemicDistM3D = Math.Max(0.1f, distanceM);

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
        // Full-mesh mode: render the projected Candide mesh directly; its head
        // pose is already baked into the vertices by GetProjectedShape.
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

        // ── Backchannel nod (Phase 10): add sin(π·t) pitch offset for 500 ms ─────────
        if (_nodStartMs3D >= 0)
        {
            double nodT = (Environment.TickCount64 - _nodStartMs3D) / NodDuration3DMs;
            if (nodT <= 1.0)
                headPitch += NodAmplitudePitch3DRad * (float)Math.Sin(Math.PI * nodT);
            else
                _nodStartMs3D = -1;
        }

        // ── Head tilt (Phase 10): add sin(π·t) roll offset for 600 ms ─────────────────
        if (_tiltStartMs3D >= 0)
        {
            double tiltT = (Environment.TickCount64 - _tiltStartMs3D) / TiltDuration3DMs;
            if (tiltT <= 1.0)
                headRoll += _tiltRollRad3D * (float)Math.Sin(Math.PI * tiltT);
            else
                _tiltStartMs3D = -1;
        }

        // Full-mesh vertices from GetProjectedShape already include the Kinect
        // head pose. In that mode, the eyes are the coordinate authority: live
        // FP eye anchors and mesh vertices are both mapped through the same
        // fit transform with no extra corrective head transform. The fallback
        // FP wireframe still needs the synthetic head transform because it has
        // no projected Candide mesh pose baked into its vertices.
        double correctionYaw = hasMesh ? 0.0 : Math.Clamp(headYaw - _bakedHeadYawRad, -Math.PI / 3, Math.PI / 3);
        double correctionPitch = hasMesh ? 0.0 : Math.Clamp(headPitch - _bakedHeadPitchRad, -Math.PI / 4, Math.PI / 4);
        double correctionRoll = hasMesh ? 0.0 : Math.Clamp(headRoll - _bakedHeadRollRad, -Math.PI / 4, Math.PI / 4);

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

        // ── Eye socket positioning — mesh feature-point anchoring ────────────
        // Uses the Candide-3 feature point eye contour indices to derive
        // anatomically correct eye socket centres that move perfectly with
        // the mesh. Feature points 9–14 define the right eye contour,
        // 30–35 define the left eye contour. These are transformed through
        // the same wireframe pipeline (fitScale, offsets, yaw-compress,
        // pitch-shift, roll) so eyes are always correctly anchored.
        // Falls back to proportional bounding-box placement if feature points
        // are unavailable.
        bool eyesDrawn = false;

        if (hasMesh)
        {
            // ── Compute eye socket centres from feature points ───────────
            // Feature points live in the same 640×480 projected space as the
            // mesh vertices, so we use the same Map() pipeline for transformation.
            var fps = _featurePoints;
            bool haveFPEyes = fps is { Length: > 35 };

            double rCxMesh, rCyMesh, lCxMesh, lCyMesh;
            const double EyeRFrac = 20.5 / 280.0;  // 0.073 — eye radius as fraction of face width

            // Eye-first alignment: whenever live feature points are available,
            // they own eye socket placement. Mirroring controls expression
            // display behavior, not whether the eyes may anchor the mesh.
            bool useLiveAnchoring = haveFPEyes;

            if (useLiveAnchoring)
            {
                // Right eye contour: FP indices 9, 10, 11, 12, 13, 14
                double rSumX = 0, rSumY = 0;
                int rCount = 0;
                int[] rIdx = [9, 10, 11, 12, 13, 14];
                foreach (int idx in rIdx)
                {
                    if (idx < fps!.Length && fps[idx] != System.Numerics.Vector2.Zero)
                    {
                        rSumX += fps[idx].X;
                        rSumY += fps[idx].Y;
                        rCount++;
                    }
                }

                // Left eye contour: FP indices 30, 31, 32, 33, 34, 35
                double lSumX = 0, lSumY = 0;
                int lCount = 0;
                int[] lIdx = [30, 31, 32, 33, 34, 35];
                foreach (int idx in lIdx)
                {
                    if (idx < fps!.Length && fps[idx] != System.Numerics.Vector2.Zero)
                    {
                        lSumX += fps[idx].X;
                        lSumY += fps[idx].Y;
                        lCount++;
                    }
                }

                if (rCount >= 3 && lCount >= 3)
                {
                    // Feature-point-derived eye socket centres (mesh-space coordinates)
                    rCxMesh = rSumX / rCount;
                    rCyMesh = rSumY / rCount;
                    lCxMesh = lSumX / lCount;
                    lCyMesh = lSumY / lCount;

                    // Apply user calibration offsets (fractions of mesh bounding box).
                    // At default slider positions these deltas are zero and eyes remain
                    // anchored to the anatomical feature-point centroid. When the user
                    // adjusts the sliders, the eye positions shift correspondingly so
                    // the calibration is always effective — even in feature-point mode.
                    rCxMesh += EyePositionCalibration.RightEyeOffsetX * _meshWidth;
                    rCyMesh += EyePositionCalibration.RightEyeOffsetY * _meshHeight;
                    lCxMesh += EyePositionCalibration.LeftEyeOffsetX * _meshWidth;
                    lCyMesh += EyePositionCalibration.LeftEyeOffsetY * _meshHeight;
                }
                else
                {
                    // Not enough valid feature points — fall back to proportional
                    (rCxMesh, rCyMesh, lCxMesh, lCyMesh) = ComputeProportionalEyePositions();
                }
            }
            else
            {
                // Mirroring is disabled or feature points unavailable — use proportional placement
                (rCxMesh, rCyMesh, lCxMesh, lCyMesh) = ComputeProportionalEyePositions();
            }

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

            // Eye-first contract: in full-mesh mode the live feature-point
            // eye anchors are authoritative. Do not smooth these centres;
            // even a tiny EMA lag lets the projected mesh move before the eyes.
            const double Alpha = 1.0;
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
            normYaw = Math.Clamp(normYaw + saccYaw + _socialGazeYaw3D, -1.0, 1.0);
            normPitch = Math.Clamp(normPitch + saccPitch + _socialGazePitch3D, -1.0, 1.0);

            double rotYaw = Math.Sin(normYaw * (Math.PI / 2.0));
            double rotPitch = Math.Sin(normPitch * (Math.PI / 2.0));

            eyesDrawn = true;

            // Lock eyeball centres to socket anchors; only iris/pupil rotates.
            DrawEyeSphere(dc, leftAnchor.X, leftAnchor.Y, eyeR, rotYaw, rotPitch);
            DrawEyeSphere(dc, rightAnchor.X, rightAnchor.Y, eyeR, rotYaw, rotPitch);

            // ── Eyebrow animation ──────────────────────────────────────────────
            // Smooth the brow targets (150ms EMA) and draw arcs above each socket.
            // Runs inside the hasMesh block so eyeR and leftAnchor/rightAnchor are live.
            long browNow = Environment.TickCount64;
            double browDt = _lastBrowTicks3D > 0
                ? Math.Clamp((browNow - _lastBrowTicks3D) / 1000.0, 0.001, 0.2)
                : 0.033;
            _lastBrowTicks3D = browNow;
            double browAlpha = 1.0 - Math.Exp(-browDt / 0.15);
            _browRaiseSmoothed3D += (_browRaiseTarget3D - _browRaiseSmoothed3D) * browAlpha;
            _browFurrowSmoothed3D += (_browFurrowTarget3D - _browFurrowSmoothed3D) * browAlpha;

            DrawBrow3D(dc, leftAnchor, eyeR, _browRaiseSmoothed3D, _browFurrowSmoothed3D, isLeft: true);
            DrawBrow3D(dc, rightAnchor, eyeR, _browRaiseSmoothed3D, _browFurrowSmoothed3D, isLeft: false);

            // ── Viseme / mouth animation ──────────────────────────────────────
            // Smooth viseme targets (60ms EMA for co-articulation), then draw a
            // proportional mouth opening centred between the two eye sockets,
            // positioned eyeR * 2.8 below the eye centre line.
            long visNow = Environment.TickCount64;
            double visDt = _lastVisemeTicks3D > 0
                ? Math.Clamp((visNow - _lastVisemeTicks3D) / 1000.0, 0.001, 0.1)
                : 0.033;
            _lastVisemeTicks3D = visNow;
            double visAlpha = 1.0 - Math.Exp(-visDt / 0.060);
            _visemeJaw3D += (_visemeTarget3D.JawOpen - _visemeJaw3D) * visAlpha;
            _visemeRound3D += (_visemeTarget3D.LipRound - _visemeRound3D) * visAlpha;

            // Phase 10 smile smoothing (150ms EMA)
            double smileAlpha3D = 1.0 - Math.Exp(-visDt / 0.150);
            _smileSmoothed3D += (_smileTarget3D - _smileSmoothed3D) * smileAlpha3D;
            _lastSmileTicks3D = visNow;

            DrawMouth3D(dc, leftAnchor, rightAnchor, eyeR, _visemeJaw3D, _visemeRound3D, _smileSmoothed3D);

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
                // Apply user calibration offsets in screen space.
                // The calibration values live in mesh-space fractions, so we must
                // multiply by fitScale to convert them to on-screen pixel deltas.
                // At default slider positions these offsets are zero.
                double calRxPx = EyePositionCalibration.RightEyeOffsetX * _meshWidth * fitScale;
                double calRyPx = EyePositionCalibration.RightEyeOffsetY * _meshHeight * fitScale;
                double calLxPx = EyePositionCalibration.LeftEyeOffsetX * _meshWidth * fitScale;
                double calLyPx = EyePositionCalibration.LeftEyeOffsetY * _meshHeight * fitScale;
                rcx += calRxPx;
                rcy += calRyPx;
                lcx += calLxPx;
                lcy += calLyPx;

                // ── Eye-socket EMA smoothing (fallback mode) ─────────────
                // The FP path was previously drawn from raw feature-point centroids
                // with no smoothing, which produced visible per-frame jitter and
                // decoupled the eyes from the (smoother) wireframe. Apply the same
                // near-instant EMA used by the mesh path so eyes track the head
                // stably without lagging perceptibly.
                const double FpAlpha = 0.55;
                if (!_eyeSocketSmoothingReady)
                {
                    _rightEyeSocketSmoothed = new Point(rcx, rcy);
                    _leftEyeSocketSmoothed = new Point(lcx, lcy);
                    _eyeSocketSmoothingReady = true;
                }
                else
                {
                    _rightEyeSocketSmoothed = new Point(
                        _rightEyeSocketSmoothed.X + (rcx - _rightEyeSocketSmoothed.X) * FpAlpha,
                        _rightEyeSocketSmoothed.Y + (rcy - _rightEyeSocketSmoothed.Y) * FpAlpha);
                    _leftEyeSocketSmoothed = new Point(
                        _leftEyeSocketSmoothed.X + (lcx - _leftEyeSocketSmoothed.X) * FpAlpha,
                        _leftEyeSocketSmoothed.Y + (lcy - _leftEyeSocketSmoothed.Y) * FpAlpha);
                }
                rcx = _rightEyeSocketSmoothed.X;
                rcy = _rightEyeSocketSmoothed.Y;
                lcx = _leftEyeSocketSmoothed.X;
                lcy = _leftEyeSocketSmoothed.Y;

                // ── Normalised gaze angles ────────────────────────────────
                // MaxGazeAngle = 20° — practical Kinect tracking limit.
                // No head-pose subtraction: gaze and head-pose live in
                // different reference frames (geometry vs. camera-space rotation).
                const double MaxGazeAngle = Math.PI / 9.0; // 20°
                double normYaw = Math.Clamp(_gazeYaw, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;
                double normPitch = Math.Clamp(_gazePitch, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;

                // Micro-saccade: reuse the same engine for fallback mode.
                var (saccYawFb, saccPitchFb) = UpdateSaccade();
                normYaw = Math.Clamp(normYaw + saccYawFb + _socialGazeYaw3D, -1.0, 1.0);
                normPitch = Math.Clamp(normPitch + saccPitchFb + _socialGazePitch3D, -1.0, 1.0);

                // Travel = fraction of socket half-span pupils move for full-angle gaze.
                // 0.65 keeps the dot clearly inside the socket at extremes.
                const double Travel = 0.65;
                double rTX = rHW * Travel, rTY = rHH * Travel;
                double lTX = lHW * Travel, lTY = lHH * Travel;

                // Binocular convergence: each eye rotates inward by atan(IOD/2 / userDist)
                // where IOD = 65 mm. Scaled 2.5x for display visibility.
                double conv = rHW * 2.5 * Math.Atan(0.0325 / Math.Max(0.25, _gazeDistM));

                // Right pupil: yaw+ = look right → +X; pitch+ = look up → −Y.
                // Convergence pulls right pupil LEFT (toward nose = −X).
                double rpx = rcx + normYaw * rTX - conv;
                double rpy = rcy - normPitch * rTY;
                // Left pupil: convergence pulls left pupil RIGHT (toward nose = +X).
                double lpx = lcx + normYaw * lTX + conv;
                double lpy = lcy - normPitch * lTY;

                // Pupil visual radius: ~28% of socket half-width, minimum 4px.
                // Phase 10 proxemic dilation: enlarge at close distances (<0.6m).
                double proxemicDilate3D = 1.0 + Math.Clamp(0.6 - _proxemicDistM3D, 0.0, 0.35) * 0.62;
                double pr = Math.Max(Math.Min(rHW, lHW) * 0.28 * proxemicDilate3D, 4.0);

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
    /// Draws a proportional animated mouth between the two eye socket anchors.
    /// The mouth is placed eyeR*2.8 below the eye centre line and scales with the face.
    /// </summary>
    private static void DrawMouth3D(
        DrawingContext dc, Point leftAnchor, Point rightAnchor,
        double eyeR, double jawOpen, double lipRound, double smileIntensity = 0.0)
    {
        // Mouth centre = midpoint between eye sockets
        double mouthCX = (leftAnchor.X + rightAnchor.X) / 2.0;
        double mouthCY = (leftAnchor.Y + rightAnchor.Y) / 2.0 + eyeR * 2.8;

        // Half-widths scale with face; rounds vowels narrow the smile
        double halfW = eyeR * 1.3 - lipRound * eyeR * 0.4;
        halfW = Math.Clamp(halfW, eyeR * 0.5, eyeR * 1.6);

        // Lip geometry (upper arc = smile baseline; lower arc = jaw drop)
        double upperY = mouthCY;
        double lowerY = mouthCY + jawOpen * eyeR * 1.2;

        if (jawOpen < 0.05)
        {
            // Closed / near-closed: draw the smile arc.
            // Phase 10: smile deepens the arc (0.4 → up to 0.7 × eyeR).
            double smileDepth = 0.4 + smileIntensity * 0.30;
            var smileGeo = new StreamGeometry();
            using (var ctx = smileGeo.Open())
            {
                ctx.BeginFigure(new Point(mouthCX - halfW, upperY), false, false);
                ctx.QuadraticBezierTo(
                    new Point(mouthCX, upperY + eyeR * smileDepth),
                    new Point(mouthCX + halfW, upperY), true, false);
            }
            smileGeo.Freeze();
            dc.DrawGeometry(null, _browPen, smileGeo);
        }
        else
        {
            // Open mouth: draw upper arc + lower arc + connecting verticals
            var mouthGeo = new StreamGeometry();
            using (var ctx = mouthGeo.Open())
            {
                // Upper lip arc
                ctx.BeginFigure(new Point(mouthCX - halfW, upperY), false, false);
                ctx.QuadraticBezierTo(
                    new Point(mouthCX, upperY + eyeR * 0.15),
                    new Point(mouthCX + halfW, upperY), true, false);

                // Right connecting line
                ctx.LineTo(new Point(mouthCX + halfW, lowerY), true, false);

                // Lower lip arc (reversed)
                ctx.QuadraticBezierTo(
                    new Point(mouthCX, lowerY - eyeR * 0.15),
                    new Point(mouthCX - halfW, lowerY), true, false);

                // Left connecting line (close the shape)
                ctx.LineTo(new Point(mouthCX - halfW, upperY), true, false);
            }
            mouthGeo.Freeze();
            dc.DrawGeometry(
                new SolidColorBrush(Color.FromArgb(120, 10, 10, 15)) { },
                _browPen, mouthGeo);
        }
    }

    /// <summary>
    /// Draws a single eyebrow arc above the given eye socket anchor.
    ///
    /// The brow is a quadratic bezier with three control points proportional
    /// to <paramref name="eyeR"/>:
    ///   Outer (temporal) → peak (above socket centre) → inner (nasal side)
    ///
    /// <paramref name="raise"/>  [0..1]: raises the whole arch (AU5/AU1 — surprise, query, greeting).
    /// <paramref name="furrow"/> [0..1]: drops the inner end toward the nose, creating the
    ///   characteristic inverted-V of concentration or concern (AU3/AU4 — LOGIC, THINK modes).
    /// </summary>
    private static void DrawBrow3D(
        DrawingContext dc, Point anchor, double eyeR,
        double raise, double furrow, bool isLeft)
    {
        // Proportional offsets from eye socket centre:
        double halfW = eyeR * 1.1;   // half-width of brow span
        double riseN = eyeR * 1.35;  // neutral height above socket centre
        double riseR = raise * eyeR * 0.7;   // extra rise when brow raised
        double dropI = furrow * eyeR * 0.6;   // inner-end drop when furrowed
        double flatPk = furrow * eyeR * 0.3;   // peak flattens when furrowed

        double outerX, innerX;
        if (isLeft)
        {
            // Left brow: outer = temporal (left), inner = nasal (right)
            outerX = anchor.X - halfW;
            innerX = anchor.X + halfW;
        }
        else
        {
            // Right brow: outer = temporal (right), inner = nasal (left)
            outerX = anchor.X + halfW;
            innerX = anchor.X - halfW;
        }

        double outerY = anchor.Y - riseN - riseR + furrow * eyeR * 0.15; // outer holds roughly
        double peakY = anchor.Y - riseN - eyeR * 0.2 - riseR + flatPk;  // peak above socket
        double innerY = anchor.Y - riseN - riseR + dropI;                 // inner drops on furrow

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(outerX, outerY), false, false);
            ctx.QuadraticBezierTo(
                new Point(anchor.X, peakY),
                new Point(innerX, innerY),
                isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, _browPen, geo);
    }

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

    /// <summary>
    /// Fallback eye position computation using Happy Face proportional placement.
    /// Used only when Candide-3 feature points are unavailable.
    /// Returns (rightCx, rightCy, leftCx, leftCy) in mesh-vertex coordinate space.
    /// </summary>
    private (double rCx, double rCy, double lCx, double lCy) ComputeProportionalEyePositions()
    {
        return (
            _meshLeft + EyePositionCalibration.RightEyeX * _meshWidth,
            _meshTop + EyePositionCalibration.RightEyeY * _meshHeight,
            _meshLeft + EyePositionCalibration.LeftEyeX * _meshWidth,
            _meshTop + EyePositionCalibration.LeftEyeY * _meshHeight);
    }

}
