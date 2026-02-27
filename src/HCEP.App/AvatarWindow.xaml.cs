// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using System.Windows;

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

    public AvatarWindow(HCEPPipelineOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();

        Loaded += Window_Loaded;
        Closed += (_, _) => _orchestrator.GazeVectorReady -= OnGazeVectorReady;
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

        // ── Register eye-position provider with orchestrator ──
        // This delegate is called from the background pipeline thread at ~10 Hz.
        // It reads value-type Point properties from the Avatar, which is safe.
        _orchestrator.SetAvatarEyeProvider(
            provider: () =>
            {
                var l = Avatar.LeftEyeScreenPos;
                var r = Avatar.RightEyeScreenPos;
                return (new Vector2((float)l.X, (float)l.Y),
                        new Vector2((float)r.X, (float)r.Y));
            },
            screenWidthPhysicalPx: screenWidthPx,
            screenHeightPhysicalPx: screenHeightPx);

        // ── Subscribe to computed gaze events ─────────────────
        _orchestrator.GazeVectorReady += OnGazeVectorReady;

        StatusText.Text = " · registered";
    }

    // ── Gaze callback (arrives from background thread) ────────

    private void OnGazeVectorReady(float pitch, float yaw)
    {
        // Marshal to UI thread and update both Avatar pupils and status bar.
        Dispatcher.BeginInvoke(() =>
        {
            Avatar.SetGaze(pitch, yaw);
            PitchText.Text = $"{pitch * 180f / MathF.PI:+0.0;-0.0;0.0}°";
            YawText.Text = $"{yaw * 180f / MathF.PI:+0.0;-0.0;0.0}°";
            StatusText.Text = " · live";
        });
    }
}
