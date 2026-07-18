using System.Numerics;
using System.Windows;
using System.Windows.Media;
using HCEP.Speech;

namespace HCEP.App;

/// <summary>
/// Procedural high-density head-and-shoulders wireframe avatar.
///
/// This avatar does not depend on Kinect FaceTrackLib mesh availability. It renders
/// a deterministic ellipsoid head, neck, and shoulder topology and applies the
/// standard HCEP eye, brow, mouth, gaze, proxemic, nod, tilt, and viseme signals.
/// Eye anchors are projected from the same model transform as the mesh so the eyes
/// remain the visual parent coordinate for the face.
/// </summary>
public sealed class AvatarHighPolyWireframeControl : FrameworkElement, IAvatarComponent
{
    private static readonly ProceduralMesh Mesh = ProceduralMesh.Build();
    private static readonly Pen MeshPenFront = CreatePen(Color.FromArgb(210, 0, 230, 205), 0.72);
    private static readonly Pen MeshPenBack = CreatePen(Color.FromArgb(70, 0, 160, 150), 0.55);
    private static readonly Pen FeaturePen = CreatePen(Color.FromArgb(235, 96, 255, 225), 1.25);
    private static readonly Pen BrowPen = CreatePen(Color.FromArgb(235, 0, 230, 205), 1.55);
    private static readonly Pen EyeOutlinePen = CreatePen(Color.FromArgb(130, 0, 230, 205), 0.85);
    private static readonly Pen IrisOutlinePen = CreatePen(Color.FromArgb(150, 0, 150, 135), 0.65);
    private static readonly Brush PupilBrush = CreateBrush(Color.FromArgb(255, 8, 12, 18));
    private static readonly Brush SpecularBrush = CreateBrush(Color.FromArgb(210, 255, 255, 255));

    private Point[]? _projected;
    private double[]? _depths;

    private float _gazePitch;
    private float _gazeYaw;
    private float _gazeDistM = 1.5f;
    private float _socialGazeYaw;
    private float _socialGazePitch;
    private float _proxemicDistM = 1.5f;

    private float _headYawRad;
    private float _headPitchRad;
    private float _headRollRad;
    private bool _headPoseInitialized;
    private long _lastHeadPoseTicks;
    private readonly long _floatOriginMs = Environment.TickCount64;

    private float _browRaiseTarget;
    private float _browFurrowTarget;
    private double _browRaise;
    private double _browFurrow;
    private long _lastBrowTicks;

    private VisemeData _visemeTarget = VisemeData.Silence;
    private double _visemeJaw;
    private double _visemeRound;
    private float _smileTarget;
    private double _smile;
    private long _lastMouthTicks;

    private long _nodStartMs = -1;
    private long _tiltStartMs = -1;
    private float _tiltRollRad;

    private static readonly Random SaccadeRng = new();
    private bool _saccadeTargetLeft = true;
    private long _nextSaccadeMs;
    private long _nextMicroSaccadeMs;
    private long _lastSaccadeUpdateMs;
    private double _saccadeSmoothedYaw;
    private double _microTargetX;
    private double _microTargetY;
    private double _microSmoothedX;
    private double _microSmoothedY;

    private bool _blinkInitialized;
    private BlinkState _blinkState = BlinkState.Idle;
    private long _nextBlinkMs;
    private long _blinkStateStartMs;
    private double _blinkAmount;

    private Point _leftEyeLocalPt;
    private Point _rightEyeLocalPt;

    public AvatarHighPolyWireframeControl()
    {
        LayoutUpdated += (_, _) => UpdateEyeScreenCoordinates();
        Loaded += (_, _) => InvalidateVisual();
    }

    public bool IsMirroringEnabled { get; set; }

    public int MeshVertexCount => Mesh.Vertices.Length;

    public int MeshLineCount => Mesh.Edges.Length;

    public Point LeftEyeScreenPos { get; private set; }

    public Point RightEyeScreenPos { get; private set; }

    public void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f)
    {
        _gazePitch = pitchRad;
        _gazeYaw = yawRad;
        _gazeDistM = Math.Max(0.1f, userDistanceM);
        RequestRender();
    }

    public void SetHeadPose(Vector3 rotationDeg)
    {
        const float Deg2Rad = MathF.PI / 180f;
        const float TrackingInfluence = 0.18f;

        float targetYaw = rotationDeg.Y * Deg2Rad * TrackingInfluence;
        float targetPitch = rotationDeg.X * Deg2Rad * TrackingInfluence;
        float targetRoll = rotationDeg.Z * Deg2Rad * TrackingInfluence;

        long now = Environment.TickCount64;
        if (!_headPoseInitialized)
        {
            _headYawRad = targetYaw;
            _headPitchRad = targetPitch;
            _headRollRad = targetRoll;
            _headPoseInitialized = true;
            _lastHeadPoseTicks = now;
            RequestRender();
            return;
        }

        double dt = Math.Clamp((now - _lastHeadPoseTicks) / 1000.0, 0.0, 0.2);
        _lastHeadPoseTicks = now;
        float alpha = (float)(1.0 - Math.Exp(-dt / 0.28));

        _headYawRad += (targetYaw - _headYawRad) * alpha;
        _headPitchRad += (targetPitch - _headPitchRad) * alpha;
        _headRollRad += (targetRoll - _headRollRad) * alpha;
        RequestRender();
    }

    public void SetViseme(VisemeData viseme)
    {
        _visemeTarget = viseme;
        RequestRender();
    }

    public void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f)
    {
        _browRaiseTarget = Math.Clamp(outerBrowRaise, 0f, 1f);
        _browFurrowTarget = Math.Clamp(Math.Max(Math.Abs(browLower), hcepModeFurrow), 0f, 1f);
        RequestRender();
    }

    public void ResetGaze()
    {
        _gazePitch = 0f;
        _gazeYaw = 0f;
        _socialGazeYaw = 0f;
        _socialGazePitch = 0f;
        _saccadeSmoothedYaw = 0.0;
        _microTargetX = 0.0;
        _microTargetY = 0.0;
        _microSmoothedX = 0.0;
        _microSmoothedY = 0.0;
        RequestRender();
    }

    public void TriggerNod()
    {
        _nodStartMs = Environment.TickCount64;
        RequestRender();
    }

    public void TriggerTilt(float rollDeg = 6f)
    {
        _tiltRollRad = rollDeg * (MathF.PI / 180f);
        _tiltStartMs = Environment.TickCount64;
        RequestRender();
    }

    public void SetSmile(float intensity)
    {
        _smileTarget = Math.Clamp(intensity, 0f, 1f);
        RequestRender();
    }

    public void SetSocialGazeOffset(float yawRad, float pitchRad)
    {
        _socialGazeYaw = yawRad;
        _socialGazePitch = pitchRad;
        RequestRender();
    }

    public void SetProxemicDistance(float distanceM)
    {
        _proxemicDistM = Math.Max(0.1f, distanceM);
        RequestRender();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (ActualWidth <= 1 || ActualHeight <= 1)
            return;

        EnsureProjectionBuffers();

        double scale = Math.Min(ActualWidth / 3.35, ActualHeight / 2.55);
        var centre = new Point(ActualWidth / 2.0, ActualHeight * 0.46);

        long now = Environment.TickCount64;
        double t = (now - _floatOriginMs) / 1000.0;
        double yaw = _headYawRad + 0.020 * Math.Sin(2 * Math.PI * t / 8.7);
        double pitch = _headPitchRad + 0.014 * Math.Sin(2 * Math.PI * t / 11.3 + 0.7);
        double roll = _headRollRad + 0.010 * Math.Sin(2 * Math.PI * t / 13.1 + 1.2);

        if (_nodStartMs >= 0)
        {
            double nodT = (now - _nodStartMs) / 500.0;
            if (nodT <= 1.0)
                pitch += 0.10 * Math.Sin(Math.PI * nodT);
            else
                _nodStartMs = -1;
        }

        if (_tiltStartMs >= 0)
        {
            double tiltT = (now - _tiltStartMs) / 600.0;
            if (tiltT <= 1.0)
                roll += _tiltRollRad * Math.Sin(Math.PI * tiltT);
            else
                _tiltStartMs = -1;
        }

        var projected = _projected!;
        var depths = _depths!;
        for (int i = 0; i < Mesh.Vertices.Length; i++)
            projected[i] = Project(Mesh.Vertices[i], centre, scale, yaw, pitch, roll, out depths[i]);

        foreach (var edge in Mesh.Edges)
        {
            double z = (depths[edge.A] + depths[edge.B]) * 0.5;
            dc.DrawLine(z < 0 ? MeshPenFront : MeshPenBack, projected[edge.A], projected[edge.B]);
        }

        Point leftAnchor = Project(Mesh.LeftEyeAnchor, centre, scale, yaw, pitch, roll, out _);
        Point rightAnchor = Project(Mesh.RightEyeAnchor, centre, scale, yaw, pitch, roll, out _);
        double eyeR = Math.Clamp(scale * 0.075, 6.0, 18.0);

        _leftEyeLocalPt = leftAnchor;
        _rightEyeLocalPt = rightAnchor;
        UpdateEyeScreenCoordinates();

        DrawEyes(dc, leftAnchor, rightAnchor, eyeR, yaw, pitch);
        DrawBrows(dc, leftAnchor, rightAnchor, eyeR, now);
        DrawMouth(dc, leftAnchor, rightAnchor, eyeR, now);
    }

    private void DrawEyes(DrawingContext dc, Point leftAnchor, Point rightAnchor, double eyeR, double headYaw, double headPitch)
    {
        const double MaxGazeAngle = Math.PI / 9.0;
        double eyeRelativeYaw = _gazeYaw + _socialGazeYaw - headYaw * 0.55;
        double eyeRelativePitch = _gazePitch + _socialGazePitch - headPitch * 0.45;
        double normYaw = Math.Clamp(eyeRelativeYaw, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;
        double normPitch = Math.Clamp(eyeRelativePitch, -MaxGazeAngle, MaxGazeAngle) / MaxGazeAngle;

        var (saccYaw, saccPitch) = UpdateSaccade();
        normYaw = Math.Clamp(normYaw + saccYaw, -1.0, 1.0);
        normPitch = Math.Clamp(normPitch + saccPitch, -1.0, 1.0);

        double conv = Math.Atan(0.0325 / Math.Max(0.25, _gazeDistM)) / MaxGazeAngle;
        double blink = UpdateBlink();
        double rotPitch = Math.Sin(normPitch * (Math.PI / 2.0));
        double leftRotYaw = Math.Sin(Math.Clamp(normYaw + conv, -1.0, 1.0) * (Math.PI / 2.0));
        double rightRotYaw = Math.Sin(Math.Clamp(normYaw - conv, -1.0, 1.0) * (Math.PI / 2.0));

        DrawEyeSphere(dc, leftAnchor, eyeR, leftRotYaw, rotPitch, blink);
        DrawEyeSphere(dc, rightAnchor, eyeR, rightRotYaw, rotPitch, blink);
    }

    private void DrawBrows(DrawingContext dc, Point leftAnchor, Point rightAnchor, double eyeR, long now)
    {
        double dt = _lastBrowTicks > 0 ? Math.Clamp((now - _lastBrowTicks) / 1000.0, 0.001, 0.2) : 0.033;
        _lastBrowTicks = now;
        double alpha = 1.0 - Math.Exp(-dt / 0.15);
        _browRaise += (_browRaiseTarget - _browRaise) * alpha;
        _browFurrow += (_browFurrowTarget - _browFurrow) * alpha;

        DrawBrow(dc, leftAnchor, eyeR, true);
        DrawBrow(dc, rightAnchor, eyeR, false);
    }

    private void DrawBrow(DrawingContext dc, Point anchor, double eyeR, bool isLeft)
    {
        double halfW = eyeR * 1.28;
        double neutralY = anchor.Y - eyeR * 1.35 - _browRaise * eyeR * 0.75;
        double innerDrop = _browFurrow * eyeR * 0.65;
        double peakLift = eyeR * 0.28 - _browFurrow * eyeR * 0.20;

        double outerX = isLeft ? anchor.X - halfW : anchor.X + halfW;
        double innerX = isLeft ? anchor.X + halfW : anchor.X - halfW;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(outerX, neutralY + _browFurrow * eyeR * 0.10), false, false);
            ctx.QuadraticBezierTo(
                new Point(anchor.X, neutralY - peakLift),
                new Point(innerX, neutralY + innerDrop), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, BrowPen, geo);
    }

    private void DrawMouth(DrawingContext dc, Point leftAnchor, Point rightAnchor, double eyeR, long now)
    {
        double dt = _lastMouthTicks > 0 ? Math.Clamp((now - _lastMouthTicks) / 1000.0, 0.001, 0.2) : 0.033;
        _lastMouthTicks = now;
        double visAlpha = 1.0 - Math.Exp(-dt / 0.060);
        double smileAlpha = 1.0 - Math.Exp(-dt / 0.150);
        _visemeJaw += (_visemeTarget.JawOpen - _visemeJaw) * visAlpha;
        _visemeRound += (_visemeTarget.LipRound - _visemeRound) * visAlpha;
        _smile += (_smileTarget - _smile) * smileAlpha;

        double cx = (leftAnchor.X + rightAnchor.X) * 0.5;
        double cy = (leftAnchor.Y + rightAnchor.Y) * 0.5 + eyeR * 2.75;
        double halfW = Math.Clamp(eyeR * (1.45 - _visemeRound * 0.45), eyeR * 0.55, eyeR * 1.8);
        double lowerY = cy + _visemeJaw * eyeR * 1.35;
        double smileDepth = eyeR * (0.26 + _smile * 0.42);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx - halfW, cy), false, false);
            ctx.QuadraticBezierTo(new Point(cx, cy + smileDepth), new Point(cx + halfW, cy), true, false);
            if (_visemeJaw > 0.05)
            {
                ctx.LineTo(new Point(cx + halfW * 0.85, lowerY), true, false);
                ctx.QuadraticBezierTo(new Point(cx, lowerY + eyeR * 0.18), new Point(cx - halfW * 0.85, lowerY), true, false);
                ctx.LineTo(new Point(cx - halfW, cy), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, FeaturePen, geo);
    }

    private void DrawEyeSphere(DrawingContext dc, Point centre, double radius, double rotYaw, double rotPitch, double blink)
    {
        double proxemicDilate = 1.0 + Math.Clamp(0.6 - _proxemicDistM, 0.0, 0.35) * 0.62;
        double lidScale = Math.Max(0.08, 1.0 - blink * 0.92);

        var sclera = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.35),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.58,
            RadiusY = 0.58,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 232, 245, 244), 0.0),
                new GradientStop(Color.FromArgb(255, 176, 204, 202), 0.52),
                new GradientStop(Color.FromArgb(255, 50, 75, 80), 0.88),
                new GradientStop(Color.FromArgb(210, 12, 18, 24), 1.0),
            ],
        };
        sclera.Freeze();
        dc.DrawEllipse(sclera, EyeOutlinePen, centre, radius, radius * lidScale);

        if (blink > 0.82)
        {
            dc.DrawLine(BrowPen, new Point(centre.X - radius * 0.90, centre.Y), new Point(centre.X + radius * 0.90, centre.Y));
            return;
        }

        double maxTravel = radius * 0.42;
        double irisCx = centre.X + rotYaw * maxTravel;
        double irisCy = centre.Y - rotPitch * maxTravel;
        double yawAngle = rotYaw * (Math.PI / 2.0);
        double pitchAngle = rotPitch * (Math.PI / 2.0);
        double irisRx = radius * 0.50 * Math.Max(Math.Cos(yawAngle), 0.35);
        double irisRy = radius * 0.50 * Math.Max(Math.Cos(pitchAngle), 0.35) * lidScale;
        double pupilRx = radius * 0.24 * proxemicDilate * Math.Max(Math.Cos(yawAngle), 0.35);
        double pupilRy = radius * 0.24 * proxemicDilate * Math.Max(Math.Cos(pitchAngle), 0.35) * lidScale;

        var iris = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.44, 0.38),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.52,
            RadiusY = 0.52,
            GradientStops =
            [
                new GradientStop(Color.FromArgb(255, 70, 255, 225), 0.0),
                new GradientStop(Color.FromArgb(255, 0, 190, 165), 0.58),
                new GradientStop(Color.FromArgb(255, 0, 95, 88), 1.0),
            ],
        };
        iris.Freeze();
        dc.DrawEllipse(iris, IrisOutlinePen, new Point(irisCx, irisCy), irisRx, irisRy);
        dc.DrawEllipse(PupilBrush, null, new Point(irisCx, irisCy), pupilRx, pupilRy);
        dc.DrawEllipse(SpecularBrush, null, new Point(centre.X - radius * 0.24, centre.Y - radius * 0.28), radius * 0.14, radius * 0.11 * lidScale);
    }

    private (double yawOffset, double pitchOffset) UpdateSaccade()
    {
        long now = Environment.TickCount64;
        double dt = _lastSaccadeUpdateMs > 0 ? Math.Clamp((now - _lastSaccadeUpdateMs) / 1000.0, 0.001, 0.2) : 0.033;
        _lastSaccadeUpdateMs = now;

        if (now >= _nextSaccadeMs)
        {
            _saccadeTargetLeft = !_saccadeTargetLeft;
            _nextSaccadeMs = now + 1200 + SaccadeRng.Next(2300);
        }

        if (now >= _nextMicroSaccadeMs)
        {
            _microTargetX = (SaccadeRng.NextDouble() - 0.5) * 0.026;
            _microTargetY = (SaccadeRng.NextDouble() - 0.5) * 0.018;
            _nextMicroSaccadeMs = now + 300 + SaccadeRng.Next(600);
        }

        double saccadeAlpha = 1.0 - Math.Exp(-dt / 0.05);
        double microAlpha = 1.0 - Math.Exp(-dt / 0.20);
        double interEyeTarget = _saccadeTargetLeft ? -0.045 : 0.045;
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
            _nextBlinkMs = now + 1800 + SaccadeRng.Next(2200);
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
                    double t = Math.Clamp((now - _blinkStateStartMs) / 70.0, 0.0, 1.0);
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
                    double t = Math.Clamp((now - _blinkStateStartMs) / 115.0, 0.0, 1.0);
                    _blinkAmount = 1.0 - t;
                    if (t >= 1.0)
                    {
                        _blinkAmount = 0.0;
                        _blinkState = BlinkState.Idle;
                        _nextBlinkMs = now + 2200 + SaccadeRng.Next(3600);
                    }
                    break;
                }
        }

        return _blinkAmount;
    }

    private Point Project(ModelVertex vertex, Point centre, double scale, double yaw, double pitch, double roll, out double depth)
    {
        double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
        double x1 = vertex.X * cosY + vertex.Z * sinY;
        double z1 = -vertex.X * sinY + vertex.Z * cosY;

        double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
        double y2 = vertex.Y * cosP - z1 * sinP;
        double z2 = vertex.Y * sinP + z1 * cosP;

        double cosR = Math.Cos(roll), sinR = Math.Sin(roll);
        double x3 = x1 * cosR - y2 * sinR;
        double y3 = x1 * sinR + y2 * cosR;

        depth = z2;
        double perspective = 1.0 / (1.0 + (z2 + 0.9) * 0.10);
        perspective = Math.Clamp(perspective, 0.82, 1.18);
        return new Point(centre.X + x3 * scale * perspective, centre.Y + y3 * scale * perspective);
    }

    private void EnsureProjectionBuffers()
    {
        if (_projected?.Length == Mesh.Vertices.Length && _depths?.Length == Mesh.Vertices.Length)
            return;

        _projected = new Point[Mesh.Vertices.Length];
        _depths = new double[Mesh.Vertices.Length];
    }

    private void UpdateEyeScreenCoordinates()
    {
        if (!IsLoaded) return;
        try
        {
            LeftEyeScreenPos = PointToScreen(_leftEyeLocalPt);
            RightEyeScreenPos = PointToScreen(_rightEyeLocalPt);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RequestRender()
    {
        if (Dispatcher.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.BeginInvoke(InvalidateVisual);
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private enum BlinkState
    {
        Idle,
        Closing,
        Opening,
    }

    private readonly record struct ModelVertex(double X, double Y, double Z);

    private sealed record ProceduralMesh(ModelVertex[] Vertices, (int A, int B)[] Edges, ModelVertex LeftEyeAnchor, ModelVertex RightEyeAnchor)
    {
        public static ProceduralMesh Build()
        {
            var vertices = new List<ModelVertex>(6000);
            var edges = new List<(int A, int B)>(12000);

            int Add(double x, double y, double z)
            {
                vertices.Add(new ModelVertex(x, y, z));
                return vertices.Count - 1;
            }

            void Edge(int a, int b) => edges.Add((a, b));

            const int headLat = 40;
            const int headLon = 88;
            var head = new int[headLat, headLon];
            for (int lat = 0; lat < headLat; lat++)
            {
                double theta = -1.38 + (2.76 * lat / (headLat - 1));
                double ring = Math.Cos(theta);
                double vertical = (Math.Sin(theta) + 1.0) * 0.5;
                double y = -0.38 + 0.88 * Math.Sin(theta);
                for (int lon = 0; lon < headLon; lon++)
                {
                    double phi = 2.0 * Math.PI * lon / headLon;
                    double cosPhi = Math.Cos(phi);
                    double sinPhi = Math.Sin(phi);
                    double front = Math.Max(0.0, -sinPhi);
                    double back = Math.Max(0.0, sinPhi);
                    double side = Math.Abs(cosPhi);

                    double cranialVault = 1.0 + 0.10 * Gaussian(vertical, 0.24, 0.20);
                    double templeInset = 1.0 - 0.07 * Gaussian(vertical, 0.42, 0.12) * side;
                    double cheekbone = 1.0 + 0.12 * Gaussian(vertical, 0.60, 0.11) * front;
                    double jawTaper = 1.0 - 0.22 * Gaussian(vertical, 0.86, 0.16);
                    double chinTaper = 1.0 - 0.30 * Gaussian(vertical, 0.97, 0.08);
                    double facePlane = 1.0 - 0.08 * Gaussian(vertical, 0.70, 0.22) * front;

                    double xRadius = 0.64 * ring * cranialVault * templeInset * cheekbone * jawTaper * chinTaper;
                    double zRadius = 0.53 * ring * (1.0 + 0.10 * back - 0.10 * facePlane * front);
                    double nosePlane = 0.055 * Gaussian(vertical, 0.58, 0.11) * front;
                    double mouthPlane = 0.030 * Gaussian(vertical, 0.75, 0.10) * front;

                    double x = xRadius * cosPhi;
                    double z = zRadius * sinPhi - nosePlane - mouthPlane;
                    head[lat, lon] = Add(x, y, z);
                }
            }

            for (int lat = 0; lat < headLat; lat++)
            {
                for (int lon = 0; lon < headLon; lon++)
                {
                    Edge(head[lat, lon], head[lat, (lon + 1) % headLon]);
                    if (lat < headLat - 1)
                        Edge(head[lat, lon], head[lat + 1, lon]);
                }
            }

            const int neckRows = 10;
            const int neckLon = 64;
            var neck = new int[neckRows, neckLon];
            for (int row = 0; row < neckRows; row++)
            {
                double t = row / (double)(neckRows - 1);
                double y = 0.42 + 0.33 * t;
                double neckHalfW = 0.18 + 0.06 * t;
                double neckDepth = 0.14 + 0.025 * t;
                for (int lon = 0; lon < neckLon; lon++)
                {
                    double phi = 2.0 * Math.PI * lon / neckLon;
                    double front = Math.Max(0.0, -Math.Sin(phi));
                    double sideTendon = 0.025 * front * Math.Pow(Math.Abs(Math.Cos(phi)), 1.7);
                    neck[row, lon] = Add(
                        neckHalfW * Math.Cos(phi),
                        y,
                        neckDepth * Math.Sin(phi) - sideTendon);
                }
            }

            for (int row = 0; row < neckRows; row++)
            {
                for (int lon = 0; lon < neckLon; lon++)
                {
                    Edge(neck[row, lon], neck[row, (lon + 1) % neckLon]);
                    if (row < neckRows - 1)
                        Edge(neck[row, lon], neck[row + 1, lon]);
                }
            }

            const int shoulderRows = 20;
            const int shoulderCols = 89;
            var shoulders = new int[shoulderRows, shoulderCols];
            for (int row = 0; row < shoulderRows; row++)
            {
                double t = row / (double)(shoulderRows - 1);
                double halfW = 0.30 + 1.30 * Math.Sin(t * Math.PI / 2.0);
                for (int col = 0; col < shoulderCols; col++)
                {
                    double u = -1.0 + 2.0 * col / (shoulderCols - 1);
                    double absU = Math.Abs(u);
                    double shoulderFalloff = Math.Sqrt(Math.Max(0.0, 1.0 - Math.Pow(absU, 2.8) * 0.18));
                    double trapeziusRise = 0.12 * Math.Exp(-Math.Pow(absU / 0.28, 2.0)) * (1.0 - t);
                    double deltoidDrop = 0.18 * Math.Pow(absU, 1.75) * Math.Sqrt(t);
                    double x = u * halfW * shoulderFalloff;
                    double y = 0.66 + 0.42 * t + deltoidDrop - trapeziusRise;
                    double z = -0.10 - 0.18 * (1.0 - absU) * (1.0 - 0.35 * t) + 0.12 * t;
                    shoulders[row, col] = Add(x, y, z);
                }
            }

            for (int row = 0; row < shoulderRows; row++)
            {
                for (int col = 0; col < shoulderCols; col++)
                {
                    if (col < shoulderCols - 1)
                        Edge(shoulders[row, col], shoulders[row, col + 1]);
                    if (row < shoulderRows - 1)
                        Edge(shoulders[row, col], shoulders[row + 1, col]);
                }
            }

            AddEyeContour(Add, Edge, -0.28);
            AddEyeContour(Add, Edge, 0.28);
            AddBrowRidge(Add, Edge, -0.28);
            AddBrowRidge(Add, Edge, 0.28);
            AddNoseAndMidline(vertices, edges, Add, Edge);
            AddMouthAndLips(Add, Edge);
            AddJawAndCheeks(Add, Edge);
            AddEars(Add, Edge);
            AddNeckAndClavicles(Add, Edge);

            return new ProceduralMesh(
                vertices.ToArray(),
                edges.ToArray(),
                new ModelVertex(-0.255, -0.39, -0.585),
                new ModelVertex(0.255, -0.39, -0.585));
        }

        private static double Gaussian(double value, double centre, double width)
        {
            double n = (value - centre) / width;
            return Math.Exp(-n * n);
        }

        private static void AddEyeContour(Func<double, double, double, int> add, Action<int, int> edge, double centreX)
        {
            int previous = -1;
            int first = -1;
            for (int i = 0; i < 36; i++)
            {
                double a = 2.0 * Math.PI * i / 36.0;
                double x = centreX + 0.142 * Math.Cos(a);
                double y = -0.405 + 0.046 * Math.Sin(a) + 0.012 * Math.Sin(2.0 * a);
                double z = -0.645 - 0.010 * Math.Cos(a);
                int current = add(x, y, z);
                if (first < 0)
                    first = current;
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
            edge(previous, first);
        }

        private static void AddBrowRidge(Func<double, double, double, int> add, Action<int, int> edge, double centreX)
        {
            int previous = -1;
            for (int i = 0; i < 32; i++)
            {
                double t = i / 31.0;
                double x = centreX + (t - 0.5) * 0.34;
                double arch = Math.Sin(t * Math.PI);
                double y = -0.505 - arch * 0.050;
                double z = -0.622 - arch * 0.026;
                int current = add(x, y, z);
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
        }

        private static void AddNoseAndMidline(
            List<ModelVertex> vertices,
            List<(int A, int B)> edges,
            Func<double, double, double, int> add,
            Action<int, int> edge)
        {
            int bridgeTop = add(0.0, -0.355, -0.635);
            int bridgeMid = add(0.0, -0.235, -0.715);
            int bridgeLow = add(0.0, -0.130, -0.755);
            int tip = add(0.0, -0.035, -0.815);
            int baseL = add(-0.105, 0.028, -0.690);
            int baseR = add(0.105, 0.028, -0.690);
            int nostrilL = add(-0.065, 0.020, -0.770);
            int nostrilR = add(0.065, 0.020, -0.770);
            int philtrum = add(0.0, 0.155, -0.650);
            edge(bridgeTop, bridgeMid);
            edge(bridgeMid, bridgeLow);
            edge(bridgeLow, tip);
            edge(tip, baseL);
            edge(tip, baseR);
            edge(baseL, nostrilL);
            edge(nostrilL, tip);
            edge(baseR, nostrilR);
            edge(nostrilR, tip);
            edge(tip, philtrum);

            AddNostrilArc(add, edge, -0.070);
            AddNostrilArc(add, edge, 0.070);
        }

        private static void AddNostrilArc(Func<double, double, double, int> add, Action<int, int> edge, double centreX)
        {
            int previous = -1;
            for (int i = 0; i < 14; i++)
            {
                double t = i / 13.0;
                double a = Math.PI * (0.15 + 0.70 * t);
                int current = add(
                    centreX + 0.045 * Math.Cos(a),
                    0.050 + 0.022 * Math.Sin(a),
                    -0.785);
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
        }

        private static void AddMouthAndLips(Func<double, double, double, int> add, Action<int, int> edge)
        {
            AddLip(add, edge, upper: true);
            AddLip(add, edge, upper: false);

            int leftCorner = add(-0.205, 0.248, -0.662);
            int rightCorner = add(0.205, 0.248, -0.662);
            edge(leftCorner, rightCorner);
        }

        private static void AddLip(Func<double, double, double, int> add, Action<int, int> edge, bool upper)
        {
            int previous = -1;
            for (int i = 0; i < 38; i++)
            {
                double t = i / 37.0;
                double x = -0.205 + 0.410 * t;
                double curve = Math.Sin(t * Math.PI);
                double y = upper
                    ? 0.228 - 0.040 * curve + 0.016 * Math.Sin(2.0 * Math.PI * t)
                    : 0.276 + 0.050 * curve;
                double z = -0.676 - 0.020 * curve;
                int current = add(x, y, z);
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
        }

        private static void AddJawAndCheeks(Func<double, double, double, int> add, Action<int, int> edge)
        {
            AddPolyline(add, edge, [
                new ModelVertex(-0.560, -0.075, -0.545),
                new ModelVertex(-0.585, 0.095, -0.560),
                new ModelVertex(-0.535, 0.270, -0.590),
                new ModelVertex(-0.400, 0.430, -0.610),
                new ModelVertex(-0.205, 0.525, -0.625),
                new ModelVertex(0.000, 0.555, -0.635),
                new ModelVertex(0.205, 0.525, -0.625),
                new ModelVertex(0.400, 0.430, -0.610),
                new ModelVertex(0.535, 0.270, -0.590),
                new ModelVertex(0.585, 0.095, -0.560),
                new ModelVertex(0.560, -0.075, -0.545),
            ]);

            AddPolyline(add, edge, [
                new ModelVertex(-0.455, -0.180, -0.630),
                new ModelVertex(-0.360, -0.060, -0.675),
                new ModelVertex(-0.255, 0.030, -0.692),
                new ModelVertex(-0.135, 0.095, -0.675),
            ]);
            AddPolyline(add, edge, [
                new ModelVertex(0.455, -0.180, -0.630),
                new ModelVertex(0.360, -0.060, -0.675),
                new ModelVertex(0.255, 0.030, -0.692),
                new ModelVertex(0.135, 0.095, -0.675),
            ]);
        }

        private static void AddEars(Func<double, double, double, int> add, Action<int, int> edge)
        {
            AddEar(add, edge, -1.0);
            AddEar(add, edge, 1.0);
        }

        private static void AddEar(Func<double, double, double, int> add, Action<int, int> edge, double side)
        {
            AddClosedOval(add, edge, side * 0.660, -0.185, -0.030, 0.085, 0.190, 42);
            AddClosedOval(add, edge, side * 0.665, -0.155, -0.050, 0.042, 0.105, 28);
            AddPolyline(add, edge, [
                new ModelVertex(side * 0.650, -0.265, -0.055),
                new ModelVertex(side * 0.615, -0.185, -0.070),
                new ModelVertex(side * 0.630, -0.060, -0.058),
            ]);
        }

        private static void AddNeckAndClavicles(Func<double, double, double, int> add, Action<int, int> edge)
        {
            AddPolyline(add, edge, [
                new ModelVertex(-0.185, 0.420, -0.260),
                new ModelVertex(-0.260, 0.555, -0.230),
                new ModelVertex(-0.380, 0.690, -0.205),
                new ModelVertex(-0.610, 0.805, -0.170),
                new ModelVertex(-0.910, 0.905, -0.120),
            ]);
            AddPolyline(add, edge, [
                new ModelVertex(0.185, 0.420, -0.260),
                new ModelVertex(0.260, 0.555, -0.230),
                new ModelVertex(0.380, 0.690, -0.205),
                new ModelVertex(0.610, 0.805, -0.170),
                new ModelVertex(0.910, 0.905, -0.120),
            ]);
            AddPolyline(add, edge, [
                new ModelVertex(-0.060, 0.725, -0.285),
                new ModelVertex(-0.270, 0.790, -0.300),
                new ModelVertex(-0.540, 0.850, -0.285),
                new ModelVertex(-0.870, 0.885, -0.235),
            ]);
            AddPolyline(add, edge, [
                new ModelVertex(0.060, 0.725, -0.285),
                new ModelVertex(0.270, 0.790, -0.300),
                new ModelVertex(0.540, 0.850, -0.285),
                new ModelVertex(0.870, 0.885, -0.235),
            ]);
        }

        private static void AddClosedOval(
            Func<double, double, double, int> add,
            Action<int, int> edge,
            double centreX,
            double centreY,
            double centreZ,
            double radiusX,
            double radiusY,
            int segments)
        {
            int previous = -1;
            int first = -1;
            for (int i = 0; i < segments; i++)
            {
                double a = 2.0 * Math.PI * i / segments;
                int current = add(centreX + radiusX * Math.Cos(a), centreY + radiusY * Math.Sin(a), centreZ);
                if (first < 0)
                    first = current;
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
            edge(previous, first);
        }

        private static void AddPolyline(Func<double, double, double, int> add, Action<int, int> edge, ModelVertex[] points)
        {
            int previous = -1;
            foreach (var point in points)
            {
                int current = add(point.X, point.Y, point.Z);
                if (previous >= 0)
                    edge(previous, current);
                previous = current;
            }
        }
    }
}