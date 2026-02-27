// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Windows;
using System.Windows.Controls;

namespace HCEP.App;

/// <summary>
/// HCEP True Gaze Avatar — a self-contained 2D "Happy Face" WPF UserControl.
///
/// ── Design Principles ─────────────────────────────────────────
/// • Zero external assets — all rendering uses native WPF shapes defined in XAML.
/// • Self-aware: continuously resolves its own eye-socket screen coordinates so
///   the gaze-vector pipeline can aim at the exact physical pixels of each eye.
/// • Gaze-driven: <see cref="SetGaze(double, double)"/> translates the pupils
///   within their sockets via <see cref="System.Windows.Media.TranslateTransform"/>.
///
/// ── Coordinate Layout (local canvas space, origin = top-left of control) ─────
///   Face circle centre  : (140, 140)   radius 120 px
///   Left  eye centre    : ( 95, 112)   socket diam 44 px, pupil diam 20 px
///   Right eye centre    : (185, 112)   socket diam 44 px, pupil diam 20 px
///   Max pupil travel    : ±12 px  (socket radius 22 − pupil radius 10)
///
/// ── Gaze Convention ───────────────────────────────────────────
///   Pitch > 0  → looking UP   → pupils translate −Y
///   Yaw   > 0  → looking RIGHT→ pupils translate +X
///   Input is in radians; <see cref="MaxGazeAngleRad"/> (45°) maps to max travel.
/// </summary>
public partial class AvatarCoreControl : UserControl, IAvatarComponent
{
    // ── Layout constants (must match XAML geometry) ────────────

    /// <summary>Pupil max travel in pixels from socket centre (socket r − pupil r).</summary>
    private const double MaxPupilTravelPx = 12.0;

    /// <summary>Gaze angle (radians) that maps to maximum pupil travel.</summary>
    private const double MaxGazeAngleRad = Math.PI / 4.0;   // 45°

    // Local-canvas centres of each eye socket (Canvas.Left + Width/2, Canvas.Top + Height/2)
    private static readonly Point LeftSocketCentreLocal = new(95.0, 112.0);
    private static readonly Point RightSocketCentreLocal = new(185.0, 112.0);

    // ── Public screen-coordinate properties ───────────────────

    /// <summary>
    /// Screen pixel coordinates of the left eye socket centre.
    /// Updated each time the control's layout changes (window move, resize, scroll).
    /// <c>default</c> until the control is first rendered.
    /// </summary>
    public Point LeftEyeScreenPos { get; private set; }

    /// <summary>
    /// Screen pixel coordinates of the right eye socket centre.
    /// See <see cref="LeftEyeScreenPos"/> for lifecycle notes.
    /// </summary>
    public Point RightEyeScreenPos { get; private set; }

    // ── Construction ──────────────────────────────────────────

    public AvatarCoreControl()
    {
        InitializeComponent();

        // Re-resolve screen coordinates whenever the layout changes (includes
        // window move, resize, DPI change, or control repositioning).
        LayoutUpdated += (_, _) => UpdateEyeScreenCoordinates();
    }

    // ── Screen-coordinate self-awareness ──────────────────────

    /// <summary>
    /// Resolves and caches the absolute screen pixel coordinates of each eye-socket
    /// centre using WPF's <c>Visual.PointToScreen</c> pipeline.
    ///
    /// Safe to call on any layout tick; internally guards against calls before the
    /// control is connected to a presentable visual tree.
    /// </summary>
    public void UpdateEyeScreenCoordinates()
    {
        // Guard: PointToScreen requires the control to be in a live visual tree
        // with a valid PresentationSource (i.e., attached to a shown Window).
        if (PresentationSource.FromVisual(RootCanvas) is null) return;

        try
        {
            LeftEyeScreenPos = RootCanvas.PointToScreen(LeftSocketCentreLocal);
            RightEyeScreenPos = RootCanvas.PointToScreen(RightSocketCentreLocal);
        }
        catch (InvalidOperationException)
        {
            // Control was detached between the null-check and the call — ignore.
        }
    }

    // ── Gaze / pupil control ───────────────────────────────────

    /// <summary>
    /// Drives the pupils by applying a <see cref="System.Windows.Media.TranslateTransform"/>
    /// to both <c>LeftPupil</c> and <c>RightPupil</c> ellipses.
    ///
    /// <para>
    /// The supplied angles are clamped so pupils remain within their sockets.
    /// Pass (0, 0) to re-centre the pupils (resting / neutral gaze).
    /// </para>
    /// </summary>
    /// <param name="pitchRad">
    /// Vertical gaze angle in radians.
    /// Positive = looking up → pupils translate up (negative Y).
    /// </param>
    /// <param name="yawRad">
    /// Horizontal gaze angle in radians.
    /// Positive = looking right → pupils translate right (positive X).
    /// </param>
    /// <param name="userDistanceM">
    /// User's distance from the sensor in metres (Camera Space Z).
    /// Used to compute binocular convergence: pupils angle inward when the
    /// user is close. Pass 0 or omit to suppress convergence.
    /// </param>
    public void SetGaze(double pitchRad, double yawRad, double userDistanceM = 1.5)
    {
        double dx = Clamp(yawRad / MaxGazeAngleRad, -1.0, 1.0) * MaxPupilTravelPx;
        double dy = Clamp(pitchRad / MaxGazeAngleRad, -1.0, 1.0) * MaxPupilTravelPx;

        // ── Binocular convergence ──────────────────────────────
        // When the user leans in (small distanceM), pupils converge inward
        // to simulate the avatar focusing on a near object.
        //   Reference far  : 1.2 m → zero convergence
        //   Reference close : 0.0 m → max convergence (±ConvergenceMaxPx)
        const double ConvergenceMaxPx = 6.0;  // half of MaxPupilTravelPx
        const double ConvergenceFarM = 1.2;
        double convergence = ConvergenceMaxPx *
            Clamp((ConvergenceFarM - userDistanceM) / ConvergenceFarM, 0.0, 1.0);

        // Pitch > 0 = looking up = pupils move UP = negative Y
        // Left pupil converges rightward (+X); right pupil converges leftward (−X).
        LeftPupilTransform.X = dx + convergence;
        LeftPupilTransform.Y = -dy;
        RightPupilTransform.X = dx - convergence;
        RightPupilTransform.Y = -dy;
    }

    /// <summary>Re-centres both pupils (neutral / resting gaze).</summary>
    public void ResetGaze()
    {
        LeftPupilTransform.X = 0;
        LeftPupilTransform.Y = 0;
        RightPupilTransform.X = 0;
        RightPupilTransform.Y = 0;
    }

    // IAvatarComponent ─ explicit impl bridges float pipeline → double WPF math
    void IAvatarComponent.SetGaze(float p, float y, float d) => SetGaze((double)p, (double)y, (double)d);
    void IAvatarComponent.ResetGaze() => ResetGaze();

    // ── Helpers ───────────────────────────────────────────────

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
