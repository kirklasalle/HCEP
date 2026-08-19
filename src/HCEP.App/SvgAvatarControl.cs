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
using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using HCEP.Speech;

namespace HCEP.App;

/// <summary>
/// Dynamic 2D SVG vector avatar control with full HCEP eye, brow, mouth,
/// smile, and viseme reactivity. Renders responsive vector geometry and
/// exports clean SVG markup for HCEP Avatar Studio.
/// </summary>
public sealed class SvgAvatarControl : FrameworkElement, IAvatarComponent
{
    // ── Stylistic / Parametric Properties ──
    public string AvatarName { get; set; } = "Custom 2D Avatar";
    public Color SkinColor { get; set; } = Color.FromRgb(15, 23, 42); // Deep midnight slate
    public Color AccentGlowColor { get; set; } = Color.FromRgb(0, 229, 255); // Cyan glow
    public Color IrisColor { get; set; } = Color.FromRgb(0, 255, 194); // Teal iris
    public Color ScleraColor { get; set; } = Color.FromArgb(240, 240, 248, 255);
    public double EyeRadiusX { get; set; } = 28;
    public double EyeRadiusY { get; set; } = 38;
    public double PupilRadius { get; set; } = 12;
    public double EyeSpacing { get; set; } = 70;
    public double BrowThickness { get; set; } = 3.5;
    public bool ShowCyberneticAccents { get; set; } = true;

    // ── Live Reactive State ──
    private float _gazePitch;
    private float _gazeYaw;
    private float _gazeDistM = 1.5f;
    private float _socialGazeYaw;
    private float _socialGazePitch;
    private float _proxemicDistM = 1.5f;

    private float _outerBrowRaise;
    private float _browLower;
    private float _hcepModeFurrow;
    private float _smileIntensity;
    private VisemeData _currentViseme = VisemeData.Silence;

    // Blink State
    private double _blinkProgress; // 0 = open, 1 = fully closed
    private long _lastBlinkTick;
    private bool _isBlinking;

    public Point LeftEyeScreenPos { get; private set; }
    public Point RightEyeScreenPos { get; private set; }

    public SvgAvatarControl()
    {
        LayoutUpdated += (_, _) => UpdateEyePositions();
        Loaded += (_, _) => InvalidateVisual();
    }

    private void UpdateEyePositions()
    {
        double w = ActualWidth > 10 ? ActualWidth : 400;
        double h = ActualHeight > 10 ? ActualHeight : 400;
        double cx = w / 2.0;
        double cy = h / 2.0 - 15;

        var ptLeftLocal = new Point(cx - EyeSpacing, cy);
        var ptRightLocal = new Point(cx + EyeSpacing, cy);

        try
        {
            LeftEyeScreenPos = PointToScreen(ptLeftLocal);
            RightEyeScreenPos = PointToScreen(ptRightLocal);
        }
        catch
        {
            LeftEyeScreenPos = ptLeftLocal;
            RightEyeScreenPos = ptRightLocal;
        }
    }

    // ── IAvatarComponent Implementation ──

    public void SetGaze(float pitchRad, float yawRad, float userDistanceM = 1.5f)
    {
        _gazePitch = pitchRad;
        _gazeYaw = yawRad;
        _gazeDistM = Math.Max(0.1f, userDistanceM);
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void SetViseme(VisemeData viseme)
    {
        _currentViseme = viseme;
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void SetBrows(float outerBrowRaise, float browLower, float hcepModeFurrow = 0f)
    {
        _outerBrowRaise = outerBrowRaise;
        _browLower = browLower;
        _hcepModeFurrow = hcepModeFurrow;
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void ResetGaze()
    {
        _gazePitch = 0f;
        _gazeYaw = 0f;
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void TriggerNod() { /* Micro-animation dispatch */ }
    public void TriggerTilt(float rollDeg = 6f) { /* Micro-tilt dispatch */ }

    public void SetSmile(float intensity)
    {
        _smileIntensity = Math.Clamp(intensity, 0f, 1f);
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void SetSocialGazeOffset(float yawRad, float pitchRad)
    {
        _socialGazeYaw = yawRad;
        _socialGazePitch = pitchRad;
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    public void SetProxemicDistance(float distanceM)
    {
        _proxemicDistM = distanceM;
    }

    public void TriggerBlink()
    {
        _isBlinking = true;
        _blinkProgress = 1.0;
        _lastBlinkTick = Environment.TickCount64;
        Dispatcher.BeginInvoke(new Action(InvalidateVisual));
    }

    // ── WPF Vector Rendering ──

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth > 10 ? ActualWidth : 400;
        double h = ActualHeight > 10 ? ActualHeight : 400;
        double cx = w / 2.0;
        double cy = h / 2.0;

        // Auto-recover blink
        if (_isBlinking)
        {
            long elapsed = Environment.TickCount64 - _lastBlinkTick;
            if (elapsed > 180)
            {
                _isBlinking = false;
                _blinkProgress = 0.0;
            }
            else
            {
                _blinkProgress = Math.Sin((elapsed / 180.0) * Math.PI);
            }
        }

        // Head Background
        var headBrush = new SolidColorBrush(SkinColor);
        var headGlowPen = new Pen(new SolidColorBrush(AccentGlowColor), 2.5);
        dc.DrawRoundedRectangle(headBrush, headGlowPen, new Rect(cx - 140, cy - 170, 280, 340), 120, 140);

        // Cybernetic tech lines & halo if enabled
        if (ShowCyberneticAccents)
        {
            var haloPen = new Pen(new SolidColorBrush(Color.FromArgb(90, AccentGlowColor.R, AccentGlowColor.G, AccentGlowColor.B)), 1.2)
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawEllipse(null, haloPen, new Point(cx, cy - 15), 160, 190);

            var accentPen = new Pen(new SolidColorBrush(Color.FromArgb(160, AccentGlowColor.R, AccentGlowColor.G, AccentGlowColor.B)), 1.5);
            dc.DrawLine(accentPen, new Point(cx - 110, cy + 120), new Point(cx - 60, cy + 150));
            dc.DrawLine(accentPen, new Point(cx + 110, cy + 120), new Point(cx + 60, cy + 150));
        }

        // Eyebrows
        double browShift = (_outerBrowRaise * 14.0) - (_browLower * 12.0) - (_hcepModeFurrow * 10.0);
        var browPen = new Pen(new SolidColorBrush(AccentGlowColor), BrowThickness);
        
        // Left Brow
        dc.DrawLine(browPen,
            new Point(cx - EyeSpacing - EyeRadiusX - 4, cy - EyeRadiusY - 14 - browShift),
            new Point(cx - EyeSpacing + EyeRadiusX + 4, cy - EyeRadiusY - 10 - browShift + (_browLower * 6.0)));

        // Right Brow
        dc.DrawLine(browPen,
            new Point(cx + EyeSpacing - EyeRadiusX - 4, cy - EyeRadiusY - 10 - browShift + (_browLower * 6.0)),
            new Point(cx + EyeSpacing + EyeRadiusX + 4, cy - EyeRadiusY - 14 - browShift));

        // Eyes (Left & Right)
        DrawEye(dc, cx - EyeSpacing, cy - 15);
        DrawEye(dc, cx + EyeSpacing, cy - 15);

        // Mouth / Visemes
        DrawMouth(dc, cx, cy + 85);
    }

    private void DrawEye(DrawingContext dc, double eyeX, double eyeY)
    {
        double effectiveRadiusY = EyeRadiusY * (1.0 - _blinkProgress * 0.92);

        // Sclera
        var scleraBrush = new SolidColorBrush(ScleraColor);
        var scleraPen = new Pen(new SolidColorBrush(Color.FromArgb(180, AccentGlowColor.R, AccentGlowColor.G, AccentGlowColor.B)), 1.8);
        dc.DrawEllipse(scleraBrush, scleraPen, new Point(eyeX, eyeY), EyeRadiusX, effectiveRadiusY);

        if (_blinkProgress > 0.85) return; // Fully shut during peak blink

        // Gaze offset computation
        float totalYaw = _gazeYaw + _socialGazeYaw;
        float totalPitch = _gazePitch + _socialGazePitch;

        double maxOffsetX = EyeRadiusX - PupilRadius - 3;
        double maxOffsetY = effectiveRadiusY - PupilRadius - 2;

        double offsetX = Math.Clamp(totalYaw * 32.0, -maxOffsetX, maxOffsetX);
        double offsetY = Math.Clamp(-totalPitch * 28.0, -maxOffsetY, maxOffsetY);

        double pupilX = eyeX + offsetX;
        double pupilY = eyeY + offsetY;

        // Iris
        var irisBrush = new SolidColorBrush(IrisColor);
        dc.DrawEllipse(irisBrush, null, new Point(pupilX, pupilY), PupilRadius * 1.35, PupilRadius * 1.35);

        // Pupil
        var pupilBrush = new SolidColorBrush(Color.FromRgb(10, 15, 26));
        dc.DrawEllipse(pupilBrush, null, new Point(pupilX, pupilY), PupilRadius, PupilRadius);

        // Specular Reflex
        var specularBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        dc.DrawEllipse(specularBrush, null, new Point(pupilX - PupilRadius * 0.35, pupilY - PupilRadius * 0.35), 3.2, 3.2);
    }

    private void DrawMouth(DrawingContext dc, double mouthX, double mouthY)
    {
        var mouthPen = new Pen(new SolidColorBrush(AccentGlowColor), 2.8);

        if (_currentViseme.JawOpen > 0.15f)
        {
            // Open mouth for speech
            double openH = _currentViseme.JawOpen * 22.0;
            double openW = 26.0 + (_currentViseme.LipRound * 10.0);
            var mouthFill = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            dc.DrawEllipse(mouthFill, mouthPen, new Point(mouthX, mouthY), openW, openH);
        }
        else
        {
            // Smiling or neutral curve
            double curve = (_smileIntensity * 16.0) + 2.0;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(mouthX - 32, mouthY - curve * 0.3) };
            fig.Segments.Add(new QuadraticBezierSegment(
                new Point(mouthX, mouthY + curve),
                new Point(mouthX + 32, mouthY - curve * 0.3),
                true));
            geo.Figures.Add(fig);
            dc.DrawGeometry(null, mouthPen, geo);
        }
    }

    /// <summary>
    /// Generates standalone, valid SVG XML markup representing this avatar.
    /// </summary>
    public string GenerateSvgMarkup(int width = 512, int height = 512)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\">");
        sb.AppendLine("  <defs>");
        sb.AppendLine($"    <filter id=\"glow\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\">");
        sb.AppendLine($"      <feGaussianBlur stdDeviation=\"4\" result=\"blur\" />");
        sb.AppendLine($"      <feMerge><feMergeNode in=\"blur\"/><feMergeNode in=\"SourceGraphic\"/></feMerge>");
        sb.AppendLine("    </filter>");
        sb.AppendLine("  </defs>");

        double cx = width / 2.0;
        double cy = height / 2.0;

        string skinHex = $"#{SkinColor.R:X2}{SkinColor.G:X2}{SkinColor.B:X2}";
        string glowHex = $"#{AccentGlowColor.R:X2}{AccentGlowColor.G:X2}{AccentGlowColor.B:X2}";
        string irisHex = $"#{IrisColor.R:X2}{IrisColor.G:X2}{IrisColor.B:X2}";

        // Head
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <rect x=\"{cx - 140}\" y=\"{cy - 170}\" width=\"280\" height=\"340\" rx=\"120\" ry=\"140\" fill=\"{skinHex}\" stroke=\"{glowHex}\" stroke-width=\"3\" filter=\"url(#glow)\"/>");

        // Halo
        if (ShowCyberneticAccents)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  <ellipse cx=\"{cx}\" cy=\"{cy - 15}\" rx=\"160\" ry=\"190\" fill=\"none\" stroke=\"{glowHex}\" stroke-width=\"1.5\" stroke-dasharray=\"6,6\" opacity=\"0.4\"/>");
        }

        // Brows
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <line x1=\"{cx - EyeSpacing - EyeRadiusX - 4}\" y1=\"{cy - EyeRadiusY - 14}\" x2=\"{cx - EyeSpacing + EyeRadiusX + 4}\" y2=\"{cy - EyeRadiusY - 10}\" stroke=\"{glowHex}\" stroke-width=\"{BrowThickness}\" stroke-linecap=\"round\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <line x1=\"{cx + EyeSpacing - EyeRadiusX - 4}\" y1=\"{cy - EyeRadiusY - 10}\" x2=\"{cx + EyeSpacing + EyeRadiusX + 4}\" y2=\"{cy - EyeRadiusY - 14}\" stroke=\"{glowHex}\" stroke-width=\"{BrowThickness}\" stroke-linecap=\"round\"/>");

        // Left Eye
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <ellipse cx=\"{cx - EyeSpacing}\" cy=\"{cy - 15}\" rx=\"{EyeRadiusX}\" ry=\"{EyeRadiusY}\" fill=\"#F0F8FF\" stroke=\"{glowHex}\" stroke-width=\"2\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx - EyeSpacing}\" cy=\"{cy - 15}\" r=\"{PupilRadius * 1.35}\" fill=\"{irisHex}\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx - EyeSpacing}\" cy=\"{cy - 15}\" r=\"{PupilRadius}\" fill=\"#0A0F1A\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx - EyeSpacing - 4}\" cy=\"{cy - 19}\" r=\"3.5\" fill=\"#FFFFFF\" opacity=\"0.9\"/>");

        // Right Eye
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <ellipse cx=\"{cx + EyeSpacing}\" cy=\"{cy - 15}\" rx=\"{EyeRadiusX}\" ry=\"{EyeRadiusY}\" fill=\"#F0F8FF\" stroke=\"{glowHex}\" stroke-width=\"2\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx + EyeSpacing}\" cy=\"{cy - 15}\" r=\"{PupilRadius * 1.35}\" fill=\"{irisHex}\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx + EyeSpacing}\" cy=\"{cy - 15}\" r=\"{PupilRadius}\" fill=\"#0A0F1A\"/>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <circle cx=\"{cx + EyeSpacing - 4}\" cy=\"{cy - 19}\" r=\"3.5\" fill=\"#FFFFFF\" opacity=\"0.9\"/>");

        // Mouth
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <path d=\"M {cx - 32} {cy + 85} Q {cx} {cy + 98} {cx + 32} {cy + 85}\" fill=\"none\" stroke=\"{glowHex}\" stroke-width=\"3\" stroke-linecap=\"round\"/>");

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
