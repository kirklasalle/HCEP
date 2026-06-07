// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    // ── Micro-saccade engine state ────────────────────────────
    private static readonly Random _saccadeRng = new();
    private bool _saccadeTargetLeft = true;
    private long _nextSaccadeMs;
    private double _saccadeSmoothedYaw;
    private double _microTargetX, _microTargetY;
    private double _microSmoothedX, _microSmoothedY;
    private long _nextMicroSaccadeMs;
    private long _lastSaccadeUpdateMs;

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

    public void ResetGaze()
    {
        _normYaw = 0;
        _normPitch = 0;
    }

    // IAvatarComponent
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze((double)p, (double)y, (double)d);
    void IAvatarComponent.ResetGaze() => ResetGaze();

    // ── Eye Sphere Rendering ──────────────────────────────────

    private void RenderEyes()
    {
        if (!IsLoaded) return;

        var (saccYaw, saccPitch) = UpdateSaccade();
        double nYaw = Clamp(_normYaw + saccYaw, -1.0, 1.0);
        double nPitch = Clamp(_normPitch + saccPitch, -1.0, 1.0);

        double rotYaw = Math.Sin(nYaw * (Math.PI / 2.0));
        double rotPitch = Math.Sin(nPitch * (Math.PI / 2.0));

        // Convergence
        double conv = EyeRadius * 0.18 * Clamp((1.2 - _userDistM) / 1.2, 0.0, 1.0);

        // Pupil travel
        const double Travel = 0.48;
        double travel = EyeRadius * Travel;

        double leftPupilX = LeftSocketCentreLocal.X + rotYaw * travel + conv;
        double leftPupilY = LeftSocketCentreLocal.Y - rotPitch * travel;
        double rightPupilX = RightSocketCentreLocal.X + rotYaw * travel - conv;
        double rightPupilY = RightSocketCentreLocal.Y - rotPitch * travel;

        // Render left eye sphere into the canvas
        RenderEyeOnCanvas(LeftEyeHost, LeftSocketCentreLocal.X - 73, LeftSocketCentreLocal.Y - 90,
                          leftPupilX - 73, leftPupilY - 90, EyeRadius, rotYaw, rotPitch);
        RenderEyeOnCanvas(RightEyeHost, RightSocketCentreLocal.X - 163, RightSocketCentreLocal.Y - 90,
                          rightPupilX - 163, rightPupilY - 90, EyeRadius, rotYaw, rotPitch);
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

    // ── Helpers ───────────────────────────────────────────────

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
