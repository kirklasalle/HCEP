// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HCEP.Spatial;

namespace HCEP.App;

/// <summary>
/// HCEP True Gaze Avatar — a self-contained 2D "Happy Face" WPF UserControl
/// with 3D eye sphere rendering and gaze-driven pupils.
///
/// ── Design Principles ─────────────────────────────────────────
/// • Zero external assets — all rendering uses native WPF shapes/drawing.
/// • Self-aware: continuously resolves its own eye-socket screen coordinates so
///   the gaze-vector pipeline can aim at the exact physical pixels of each eye.
/// • Gaze-driven: SetGaze drives the iris/pupil position within 3D eye spheres.
///
/// ── Coordinate Layout (local canvas space, origin = top-left of control) ─────
///   Face circle centre  : (140, 140)   radius 120 px
///   Left  eye centre    : ( 95, 112)   socket diam 44 px
///   Right eye centre    : (185, 112)   socket diam 44 px
/// </summary>
public partial class AvatarCoreControl : UserControl, IAvatarComponent
{
    // ── Layout constants (must match XAML geometry) ────────────

    /// <summary>Gaze angle (radians) that maps to maximum pupil travel.</summary>
    private const double MaxGazeAngleRad = Math.PI / 4.0;   // 45°

    /// <summary>Eye sphere radius in canvas-space pixels.</summary>
    private const double EyeRadius = 22.0;

    // Local-canvas centres of each eye socket (Canvas.Left + Width/2, Canvas.Top + Height/2)
    private static readonly Point LeftSocketCentreLocal = new(95.0, 112.0);
    private static readonly Point RightSocketCentreLocal = new(185.0, 112.0);

    // ── Frozen brushes/pens (same palette as Avatar3DControl) ──
    private static readonly Brush _irisRingBrush;
    private static readonly Brush _pupilDotBrush;
    private static readonly Brush _specularBrush;
    private static readonly Pen _eyeOutlinePen;
    private static readonly Pen _irisOutlinePen;

    static AvatarCoreControl()
    {
        _irisRingBrush = new SolidColorBrush(Color.FromArgb(220, 0, 180, 160));
        _irisRingBrush.Freeze();
        _pupilDotBrush = new SolidColorBrush(Color.FromArgb(255, 8, 12, 18));
        _pupilDotBrush.Freeze();
        _specularBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        _specularBrush.Freeze();
        _eyeOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(100, 0, 220, 190)), 0.8);
        _eyeOutlinePen.Freeze();
        _irisOutlinePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 140, 130)), 0.6);
        _irisOutlinePen.Freeze();
    }

    // ── Gaze state ────────────────────────────────────────────
    private double _normYaw;
    private double _normPitch;
    private double _userDistM = 1.5;

    // ── Head pose and rotation state ──────────────────────────────────────────
    // Head pose from Kinect tracking (pitch/yaw/roll in radians)
    private double _headYawRad = 0.0;
    private double _headPitchRad = 0.0;
    private double _headRollRad = 0.0;

    // ── Gaze-driven head turning (eye-contingent head rotation) ───────────────
    // When eyes exceed 80% of max gaze angle, head rotates proportionally
    // to create the illusion of the avatar turning its head.
    private GazeHeadFollower _gazeHeadFollower = null!;
    private long _lastGazeHeadFollowerUpdateMs;    // for framerate-independent updates

    // ── Micro-saccade engine state ─────────────────────────────────────────
    private static readonly Random _saccadeRng = new();
    private bool _saccadeTargetLeft = true;
    private long _nextSaccadeMs;
    private double _saccadeSmoothedYaw;
    private double _microTargetX, _microTargetY;
    private double _microSmoothedX, _microSmoothedY;
    private long _nextMicroSaccadeMs;
    private long _lastSaccadeUpdateMs;

    // ── Blink / eyelid engine state ───────────────────────────
    private enum BlinkState { Idle, Closing, Opening }
    private BlinkState _blinkState = BlinkState.Idle;
    private long _nextBlinkMs;
    private long _blinkStateStartMs;
    private bool _blinkInitialized;
    private double _blinkAmount;

    private const double BlinkCloseMs = 70.0;
    private const double BlinkOpenMs = 95.0;
    private const double LidMaxCoverPx = 22.5; // ~half of 44px socket
    private const double UpperBaseCoverPx = 8.8; // 20% of 44px socket
    private const double LowerBaseCoverPx = 2.2; // 5% of 44px socket

    // ── Drawing visuals for each eye ──────────────────────────
    private DrawingVisual _leftEyeVisual = new();
    private DrawingVisual _rightEyeVisual = new();

    // ── Public screen-coordinate properties ───────────────────

    public Point LeftEyeScreenPos { get; private set; }
    public Point RightEyeScreenPos { get; private set; }

    // ── Construction ──────────────────────────────────────────

    public AvatarCoreControl()
    {
        InitializeComponent();

        // Initialize the gaze head follower with 2D-mode max gaze angle (45°)
        const float MaxRad = (float)MaxGazeAngleRad;  // Math.PI / 4.0
        _gazeHeadFollower = new GazeHeadFollower(MaxRad);
        _lastGazeHeadFollowerUpdateMs = Environment.TickCount64;

        // Add drawing visuals to the eye host canvases
        LeftEyeHost.Loaded += (_, _) =>
        {
            LeftEyeHost.Children.Clear();
            // Wrap in a custom host that can hold DrawingVisuals
        };
        RightEyeHost.Loaded += (_, _) =>
        {
            RightEyeHost.Children.Clear();
        };

        LayoutUpdated += (_, _) => UpdateEyeScreenCoordinates();

        // Render timer for smooth micro-saccade animation
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        timer.Tick += (_, _) => RenderEyes();
        timer.Start();
    }

    // ── Screen-coordinate self-awareness ──────────────────────

    public void UpdateEyeScreenCoordinates()
    {
        if (PresentationSource.FromVisual(RootCanvas) is null) return;
        try
        {
            LeftEyeScreenPos = RootCanvas.PointToScreen(LeftSocketCentreLocal);
            RightEyeScreenPos = RootCanvas.PointToScreen(RightSocketCentreLocal);
        }
        catch (InvalidOperationException) { }
    }

    // ── Gaze / pupil control ───────────────────────────────────

    public void SetGaze(double pitchRad, double yawRad, double userDistanceM = 1.5)
    {
        _normYaw = Clamp(yawRad / MaxGazeAngleRad, -1.0, 1.0);
        _normPitch = Clamp(pitchRad / MaxGazeAngleRad, -1.0, 1.0);
        _userDistM = userDistanceM;
    }

    public void SetHeadPose(System.Numerics.Vector3 rotationDeg)
    {
        const float Deg2Rad = MathF.PI / 180f;
        _headYawRad = rotationDeg.Y * Deg2Rad;
        _headPitchRad = rotationDeg.X * Deg2Rad;
        _headRollRad = rotationDeg.Z * Deg2Rad;
    }

    public void ResetGaze()
    {
        _normYaw = 0;
        _normPitch = 0;
        _gazeHeadFollower.Reset();
    }

    // IAvatarComponent
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze((double)p, (double)y, (double)d);
    void IAvatarComponent.ResetGaze() => ResetGaze();

    // ── Eye Sphere Rendering ──────────────────────────────────

    private void RenderEyes()
    {
        if (!IsLoaded) return;

        // ── Update gaze-driven head follower ────────────────────────────────
        // This must happen before we use headYaw/headPitch for rendering.
        // We pass 0f, 0f for user head pose so the follower calculates its target
        // purely relative to the camera gaze direction.
        long now = Environment.TickCount64;
        float elapsedMs = (float)Math.Clamp(now - _lastGazeHeadFollowerUpdateMs, 0, 200);
        _lastGazeHeadFollowerUpdateMs = now;
        _gazeHeadFollower.Update(elapsedMs, (float)(_normYaw * MaxGazeAngleRad), (float)(_normPitch * MaxGazeAngleRad), 0f, 0f);

        // ── Determine active head pose ──────────────────────────────────────────
        // The head turning is driven by the gaze follower autonomously to track/follow the user.
        var gazeHeadPose = _gazeHeadFollower.GetTargetHeadPose();
        double headYaw = gazeHeadPose.YawRad;
        double headPitch = gazeHeadPose.PitchRad;

        var (saccYaw, saccPitch) = UpdateSaccade();
        double nYaw = Clamp(_normYaw + saccYaw, -1.0, 1.0);
        double nPitch = Clamp(_normPitch + saccPitch, -1.0, 1.0);

        double rotYaw = Math.Sin(nYaw * (Math.PI / 2.0));
        double rotPitch = Math.Sin(nPitch * (Math.PI / 2.0));

        double blink = UpdateBlink();
        ApplyEyelids(blink, nPitch);

        // ── Apply 2D plane movement in 3D space ─────────────────────────────────────────
        // The happy face is a 2D plane that translates and rotates in 3D:
        // - YAW (left/right): Plane rotates around Y axis + translates horizontally
        // - PITCH (up/down): Plane rotates around X axis + translates vertically
        // - Z-depth: Slight forward/backward movement to enhance 3D illusion
        // This creates true 3D motion without jiggling or distortion.

        // Clamp rotation angles to reasonable ranges (more conservative)
        double yawClamped = Math.Clamp(headYaw, -Math.PI / 6, Math.PI / 6);      // ±30° (was ±60°)
        double pitchClamped = Math.Clamp(headPitch, -Math.PI / 7, Math.PI / 7);  // ±26° (was ±45°)

        // Translation in 2D canvas space (X and Y movement) - very subtle
        // As the head yaws, the whole face slides left/right (minimal)
        double translateX = Math.Sin(yawClamped) * 20.0;  // Max ±20 pixels at extreme yaw (was 60)
        // As the head pitches, the whole face slides up/down (minimal)
        double translateY = -Math.Sin(pitchClamped) * 15.0;  // Max ±15 pixels at extreme pitch (was 40)

        // Slight Z-depth effect: face comes forward slightly when looking center, back when extreme
        // This is subtle but enhances the 3D illusion
        double depthScale = 1.0 - (Math.Abs(yawClamped) + Math.Abs(pitchClamped)) * 0.03;
        depthScale = Math.Clamp(depthScale, 0.97, 1.0);  // Max 3% size change (was 5%)

        // Build unified transform for entire 2D plane (ALL elements move together)
        var unifiedPlaneTransform = new TransformGroup();

        // 1. Scale for depth effect (subtle, keeps face recognizable)
        unifiedPlaneTransform.Children.Add(new ScaleTransform(depthScale, depthScale, 140, 140));

        // 2. Rotation around center (all three axes simulated via 2D transforms)
        //    Yaw: pure rotation around face center
        if (Math.Abs(yawClamped) > 0.001)
        {
            unifiedPlaneTransform.Children.Add(new RotateTransform(yawClamped * 180.0 / Math.PI, 140, 140));
        }
        //    Pitch: rotation + slight vertical squash for perspective (very subtle)
        if (Math.Abs(pitchClamped) > 0.001)
        {
            // Apply minimal pitch squash to show 3D tilt (not full squishing)
            double pitchScale = Math.Cos(pitchClamped);  // ranges from 1.0 to ~0.92 at ±45°
            pitchScale = Math.Clamp(pitchScale, 0.85, 1.0);  // Limit to maintain visibility
            unifiedPlaneTransform.Children.Add(new ScaleTransform(1.0, pitchScale, 140, 140));
        }

        // 3. Translation in X and Y (actual plane movement in 3D)
        if (Math.Abs(translateX) > 0.1 || Math.Abs(translateY) > 0.1)
        {
            unifiedPlaneTransform.Children.Add(new TranslateTransform(translateX, translateY));
        }

        // ── APPLY TRANSFORM TO ROOT CANVAS ────────────────────────────────────────────────
        // This ensures ALL face elements (face circle, eyelids, eyes, smile) move together
        // as a unified 2D plane. Individual animations (blink, pupil movement) still work
        // within this rotated/translated coordinate space.
        RootCanvas.RenderTransform = unifiedPlaneTransform;

        // ── Eye rendering (within rotated coordinate space) ────────────────────────────────
        // Eyes are positioned at their fixed socket locations on the canvas
        // The RootCanvas transform automatically applies to them
        // Pupil movement still happens within the eye socket in this rotated space

        // Convergence (eyes angle inward based on distance)
        double conv = EyeRadius * 0.18 * Clamp((1.2 - _userDistM) / 1.2, 0.0, 1.0);

        // Pupil travel within socket
        const double Travel = 0.48;
        double travel = EyeRadius * Travel;

        // Pupils move within their sockets based on gaze direction
        double leftPupilX = LeftSocketCentreLocal.X + rotYaw * travel + conv;
        double leftPupilY = LeftSocketCentreLocal.Y - rotPitch * travel;
        double rightPupilX = RightSocketCentreLocal.X + rotYaw * travel - conv;
        double rightPupilY = RightSocketCentreLocal.Y - rotPitch * travel;

        // Render eyes into their socket canvases (pupils move within the rotated canvas)
        RenderEyeOnCanvas(LeftEyeHost, LeftSocketCentreLocal.X - 22, LeftSocketCentreLocal.Y - 22,
                          leftPupilX - 22, leftPupilY - 22, EyeRadius, rotYaw, rotPitch);
        RenderEyeOnCanvas(RightEyeHost, RightSocketCentreLocal.X - 22, RightSocketCentreLocal.Y - 22,
                          rightPupilX - 22, rightPupilY - 22, EyeRadius, rotYaw, rotPitch);
    }

    private void RenderEyeOnCanvas(Canvas host, double cx, double cy,
        double pupilCx, double pupilCy, double radius,
        double rotYaw, double rotPitch)
    {
        // Use the Background property trick: render the eye sphere into a DrawingBrush
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            DrawEyeSphere(dc, cx, cy, radius, rotYaw, rotPitch);
        }
        drawing.Freeze();

        host.Background = new DrawingBrush(drawing)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(cx - radius - 2, cy - radius - 2, (radius + 2) * 2, (radius + 2) * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(-2, -2, (radius + 2) * 2, (radius + 2) * 2),
        };
    }

    /// <summary>
    /// Draws a 3D eyeball sphere — identical rendering to Avatar3DControl.DrawEyeSphere.
    /// </summary>
    private static void DrawEyeSphere(
        DrawingContext dc, double cx, double cy, double radius,
        double rotYaw, double rotPitch)
    {
        // 1. Sclera
        var scleraGradient = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.35),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.55,
            RadiusY = 0.55,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 230, 240, 240), 0.0),
                new GradientStop(Color.FromArgb(255, 180, 200, 200), 0.50),
                new GradientStop(Color.FromArgb(255, 60,  80,  85),  0.85),
                new GradientStop(Color.FromArgb(200, 20,  30,  35),  1.0),
            ],
        };
        scleraGradient.Freeze();
        dc.DrawEllipse(scleraGradient, _eyeOutlinePen, new Point(cx, cy), radius, radius);

        // 2. Iris
        double irisR = radius * 0.50;
        double pupilR = radius * 0.25;
        double maxTravel = radius * 0.42;
        double irisOffX = rotYaw * maxTravel;
        double irisOffY = -rotPitch * maxTravel;
        double irisCX = cx + irisOffX;
        double irisCY = cy + irisOffY;

        double yawAngle = rotYaw * (Math.PI / 2.0);
        double pitchAngle = rotPitch * (Math.PI / 2.0);
        double foreshortX = Math.Max(Math.Cos(yawAngle), 0.35);
        double foreshortY = Math.Max(Math.Cos(pitchAngle), 0.35);

        double irisRX = irisR * foreshortX;
        double irisRY = irisR * foreshortY;
        double pupilRX = pupilR * foreshortX;
        double pupilRY = pupilR * foreshortY;

        var irisGradient = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.45, 0.40),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 0, 200, 175), 0.0),
                new GradientStop(Color.FromArgb(255, 0, 160, 140), 0.55),
                new GradientStop(Color.FromArgb(255, 0, 100, 90),  1.0),
            ],
        };
        irisGradient.Freeze();
        dc.DrawEllipse(irisGradient, _irisOutlinePen, new Point(irisCX, irisCY), irisRX, irisRY);

        // 3. Pupil
        dc.DrawEllipse(_pupilDotBrush, null, new Point(irisCX, irisCY), pupilRX, pupilRY);

        // 4. Specular highlight
        double specR = radius * 0.14;
        double specX = cx - radius * 0.22;
        double specY = cy - radius * 0.25;
        dc.DrawEllipse(_specularBrush, null, new Point(specX, specY), specR, specR * 0.85);
    }

    // ── Micro-Saccade Engine (identical to Avatar3DControl) ──────

    private (double yawOffset, double pitchOffset) UpdateSaccade()
    {
        long now = Environment.TickCount64;
        double dt = _lastSaccadeUpdateMs > 0
            ? Clamp((now - _lastSaccadeUpdateMs) / 1000.0, 0.001, 0.2)
            : 0.033;
        _lastSaccadeUpdateMs = now;

        if (now >= _nextSaccadeMs)
        {
            _saccadeTargetLeft = !_saccadeTargetLeft;
            _nextSaccadeMs = now + 1200 + _saccadeRng.Next(2300);
        }

        if (now >= _nextMicroSaccadeMs)
        {
            _microTargetX = (_saccadeRng.NextDouble() - 0.5) * 0.03;
            _microTargetY = (_saccadeRng.NextDouble() - 0.5) * 0.02;
            _nextMicroSaccadeMs = now + 300 + _saccadeRng.Next(600);
        }

        double saccadeAlpha = 1.0 - Math.Exp(-dt / 0.05);
        double microAlpha = 1.0 - Math.Exp(-dt / 0.20);

        double interEyeTarget = _saccadeTargetLeft ? -0.05 : 0.05;
        _saccadeSmoothedYaw += (interEyeTarget - _saccadeSmoothedYaw) * saccadeAlpha;
        _microSmoothedX += (_microTargetX - _microSmoothedX) * microAlpha;
        _microSmoothedY += (_microTargetY - _microSmoothedY) * microAlpha;

        return (_saccadeSmoothedYaw + _microSmoothedX, _microSmoothedY);
    }

    private double UpdateBlink()
    {
        long now = Environment.TickCount64;

        if (!_blinkInitialized)
        {
            _blinkInitialized = true;
            _nextBlinkMs = now + 1800 + _saccadeRng.Next(2200); // 1.8–4.0 s first blink
        }

        switch (_blinkState)
        {
            case BlinkState.Idle:
                _blinkAmount = 0.0;
                if (now >= _nextBlinkMs)
                {
                    _blinkState = BlinkState.Closing;
                    _blinkStateStartMs = now;
                }
                break;

            case BlinkState.Closing:
                {
                    double t = Clamp((now - _blinkStateStartMs) / BlinkCloseMs, 0.0, 1.0);
                    _blinkAmount = t;
                    if (t >= 1.0)
                    {
                        _blinkState = BlinkState.Opening;
                        _blinkStateStartMs = now;
                    }
                    break;
                }

            case BlinkState.Opening:
                {
                    double t = Clamp((now - _blinkStateStartMs) / BlinkOpenMs, 0.0, 1.0);
                    _blinkAmount = 1.0 - t;
                    if (t >= 1.0)
                    {
                        _blinkAmount = 0.0;
                        _blinkState = BlinkState.Idle;
                        _nextBlinkMs = now + 2200 + _saccadeRng.Next(3600); // 2.2–5.8 s
                    }
                    break;
                }
        }

        return _blinkAmount;
    }

    private void ApplyEyelids(double blink, double normPitch)
    {
        double t = Clamp(blink, 0.0, 1.0);
        double upperCover = UpperBaseCoverPx + t * (LidMaxCoverPx - UpperBaseCoverPx);
        double lowerCover = LowerBaseCoverPx + t * (LidMaxCoverPx - LowerBaseCoverPx);

        // Subtle eyelid follow:
        //   normPitch > 0 (look up)   -> lids open slightly (less cover)
        //   normPitch < 0 (look down) -> lids close slightly (more cover)
        double pitch = Clamp(normPitch, -1.0, 1.0);
        double upperFollow = -pitch * 2.2; // upper lid reacts a bit more
        double lowerFollow = -pitch * 1.2; // lower lid reacts gently

        upperCover = Clamp(upperCover + upperFollow, 0.0, LidMaxCoverPx);
        lowerCover = Clamp(lowerCover + lowerFollow, 0.0, LidMaxCoverPx);

        LeftUpperLid.Height = upperCover;
        RightUpperLid.Height = upperCover;

        LeftLowerLid.Height = lowerCover;
        RightLowerLid.Height = lowerCover;

        Canvas.SetTop(LeftLowerLid, 134.0 - lowerCover);
        Canvas.SetTop(RightLowerLid, 134.0 - lowerCover);
    }

    public void TriggerBlink()
    {
        long now = Environment.TickCount64;
        _blinkState = BlinkState.Closing;
        _blinkStateStartMs = now;
    }

    // ── Helpers ───────────────────────────────────────────────

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
