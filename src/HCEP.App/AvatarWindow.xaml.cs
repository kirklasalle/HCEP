// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using System.Windows;
using System.Windows.Media;
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Hosts the <see cref="AvatarCoreControl"/> and wires it into the
/// <see cref="HCEPPipelineOrchestrator"/> gaze pipeline.
///
/// ── Startup sequence ──────────────────────────────────────────
/// 1. <c>Window_Loaded</c> resolves physical screen dimensions via
///    <c>PresentationSource.CompositionTarget.TransformToDevice</c>
///    (DPI-safe; returns device pixels, not WPF DIPs).
/// 2. Calls <see cref="HCEPPipelineOrchestrator.SetAvatarEyeProvider"/> to
///    register a thread-safe delegate that queries the Avatar's eye screen
///    positions and passes the physical screen size to the math engine.
/// 3. Subscribes to <see cref="HCEPPipelineOrchestrator.GazeVectorReady"/>
///    which fires from the background pipeline loop with (pitch, yaw).
/// 4. <see cref="OnGazeVectorReady"/> dispatches to the UI thread and calls
///    <c>Avatar.SetGaze(pitch, yaw)</c> — completing the pipeline.
///
/// ── Thread safety ─────────────────────────────────────────────
/// • <c>GazeVectorReady</c> fires from a background async task.
/// • <c>AvatarCoreControl.LeftEyeScreenPos</c> / <c>RightEyeScreenPos</c>
///   are plain properties updated on the UI thread via <c>LayoutUpdated</c>.
///   They are accessed from the pipeline thread via the provider delegate —
///   the positions are <c>System.Windows.Point</c> value types, so reads are
///   atomic and no explicit locking is required for this MVP scenario.
/// </summary>
public partial class AvatarWindow : Window
{
    private readonly HCEPPipelineOrchestrator _orchestrator;
    private bool _is3DMode;
    private IAvatarComponent _activeAvatar = null!;  // set in Window_Loaded
    private float _screenWidthPx;
    private float _screenHeightPx;

    public AvatarWindow(HCEPPipelineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();

        Loaded += Window_Loaded;
        Closed += (_, _) =>
        {
            _orchestrator.GazeVectorReady -= OnGazeVectorReady;
            _orchestrator.SnapshotReady -= OnSnapshotReady;
        };
    }

    // ── Startup ───────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // ── Resolve physical screen pixel dimensions ──────────
        // PresentationSource gives the DPI transform; multiplying by
        // SystemParameters (in WPF DIPs at 96dpi baseline) gives device pixels.
        float dpiScaleX = 1f, dpiScaleY = 1f;
        var src = PresentationSource.FromVisual(this);
        if (src is not null)
        {
            dpiScaleX = (float)src.CompositionTarget.TransformToDevice.M11;
            dpiScaleY = (float)src.CompositionTarget.TransformToDevice.M22;
        }
        float screenWidthPx = (float)(SystemParameters.PrimaryScreenWidth * dpiScaleX);
        float screenHeightPx = (float)(SystemParameters.PrimaryScreenHeight * dpiScaleY);

        _screenWidthPx = screenWidthPx;
        _screenHeightPx = screenHeightPx;
        RegisterEyeProvider();

        // ── Subscribe to computed gaze events ─────────────────
        _orchestrator.GazeVectorReady += OnGazeVectorReady;
        _orchestrator.SnapshotReady += OnSnapshotReady;

        TrackingModeText.Text = "waiting";
        _activeAvatar = Avatar;           // default: 2D Happy Face
        AvatarModeCombo.SelectedIndex = 0;
    }

    // ── Mode switch ────────────────────────────────────

    private void AvatarMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AvatarModeCombo.SelectedIndex < 0) return;
        ApplyMode(AvatarModeCombo.SelectedIndex == 1);
    }

    /// <summary>Externally switch the avatar mode (called from MainViewModel).</summary>
    public void SetAvatarMode(bool use3D)
    {
        // Guard: called from VM before the window may be fully loaded
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(() =>
        {
            AvatarModeCombo.SelectedIndex = use3D ? 1 : 0;
            ApplyMode(use3D);
        });
    }

    private void ApplyMode(bool use3D)
    {
        _is3DMode = use3D;
        Avatar.Visibility = use3D ? Visibility.Collapsed : Visibility.Visible;
        Avatar3D.Visibility = use3D ? Visibility.Visible : Visibility.Collapsed;
        _activeAvatar = use3D ? (IAvatarComponent)Avatar3D : Avatar;
        Title = use3D ? "HCEP — True Gaze Avatar (3D Wireframe)"
                       : "HCEP — True Gaze Avatar";
        // Re-register provider so GazeVectorEngine reads the active control's eye positions.
        if (_screenWidthPx > 0) RegisterEyeProvider();
    }

    /// <summary>
    /// Registers the eye-socket screen-position provider with the orchestrator.
    /// The delegate evaluates <c>_is3DMode</c> at call time so it automatically
    /// returns positions from whichever avatar control is currently active.
    /// </summary>
    private void RegisterEyeProvider()
    {
        _orchestrator.SetAvatarEyeProvider(
            provider: () => _is3DMode
                ? (new Vector2((float)Avatar3D.LeftEyeScreenPos.X, (float)Avatar3D.LeftEyeScreenPos.Y),
                   new Vector2((float)Avatar3D.RightEyeScreenPos.X, (float)Avatar3D.RightEyeScreenPos.Y))
                : (new Vector2((float)Avatar.LeftEyeScreenPos.X, (float)Avatar.LeftEyeScreenPos.Y),
                   new Vector2((float)Avatar.RightEyeScreenPos.X, (float)Avatar.RightEyeScreenPos.Y)),
            screenWidthPhysicalPx: _screenWidthPx,
            screenHeightPhysicalPx: _screenHeightPx);
    }

    // ── Mesh data callback (from SnapshotReady, background thread) ─

    private void OnSnapshotReady(SceneSnapshot snapshot)
    {
        var face = snapshot.PrimaryPerson?.Face;

        // When no FaceFrame exists, GazeVectorReady never fires — update status here so
        // the HUD doesn't stay frozen on "waiting" while the pipeline is alive.
        if (face is null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TrackingModeText.Text = snapshot.PrimaryPerson is null ? "SEARCHING" : "NO FACE";
                TrackingModeText.Foreground = System.Windows.Media.Brushes.Gray;
            });
        }

        // Always push feature points — Avatar3D needs them for eye socket gaze tracking
        // regardless of whether the full mesh or edge-chain fallback is active.
        if (face is { IsTracked: true, FeaturePoints2D.Length: > 0 })
        {
            Avatar3D.UpdateEyeData(face.FeaturePoints2D);
            // Pass head pose so Avatar3D can compute eye-relative gaze for pupils.
            // FaceFrame.HeadRotation = (pitchDeg, yawDeg, rollDeg) from Kinect Get3DPose().
            Avatar3D.SetHeadPose(face.HeadRotation);
        }

        // 3D wireframe: push live mesh or feature-point fallback
        if (!_is3DMode) return;
        if (face is null) return;

        if (face.FaceMeshVertices2D is { Length: > 0 } && face.FaceMeshTriangles is { Length: > 0 })
        {
            var verts = face.FaceMeshVertices2D;
            var tris = face.FaceMeshTriangles;
            Dispatcher.BeginInvoke(() => Avatar3D.SetMesh(verts, tris));
        }
        else if (face.FeaturePoints2D is { Length: > 0 })
        {
            // Full mesh not yet available — show 87-point landmark dot-cloud as fallback
            var pts = face.FeaturePoints2D;
            Dispatcher.BeginInvoke(() => Avatar3D.SetFeaturePoints(pts));
        }
    }

    // ── Gaze callback (arrives from background thread) ────────

    private void OnGazeVectorReady(float pitch, float yaw, float distanceM, bool isPrecision)
    {
        // Marshal to UI thread, update Avatar pupils and HUD.
        Dispatcher.BeginInvoke(() =>
        {
            _activeAvatar.SetGaze(pitch, yaw, distanceM);

            PitchText.Text = $"{pitch * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
            YawText.Text = $"{yaw * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
            DistanceText.Text = $"{distanceM:F2} m";

            if (isPrecision)
            {
                TrackingModeText.Text = "PRECISION";
                TrackingModeText.Foreground = Brushes.LightGreen;
            }
            else
            {
                TrackingModeText.Text = "FALLBACK";
                TrackingModeText.Foreground = Brushes.Orange;
            }
        });
    }
}
