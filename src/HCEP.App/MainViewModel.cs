// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// Main view model for the HCEP dashboard.
/// Observes pipeline output and updates UI bindings on the dispatcher.
/// Manages child window lifecycle and Kinect device settings.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPipelineOrchestrator _pipeline;
    private readonly ISensorSource _sensor;
    private readonly ILlmEngine _llmEngine;
    private readonly ITelemetryService _telemetry;
    private readonly IServiceProvider _services;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _metricsTimer;

    // Child window references (prevent duplicates)
    private SensorViewWindow? _sensorViewWindow;
    private KinectVideoWindow? _kinectVideoWindow;
    private CalibrationWindow? _calibrationWindow;
    private AvatarWindow? _avatarWindow;

    public MainViewModel(
        IPipelineOrchestrator pipeline,
        ISensorSource sensor,
        ILlmEngine llmEngine,
        ITelemetryService telemetry,
        IServiceProvider services,
        ILogger<MainViewModel> logger)
    {
        _pipeline = pipeline;
        _sensor = sensor;
        _llmEngine = llmEngine;
        _telemetry = telemetry;
        _services = services;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        // Initialize elevation from actual sensor
        try { _elevationAngle = _sensor.ElevationAngle; } catch { }
    }

    // ── Observable Properties ──────────────────────────────────

    [ObservableProperty] private string _currentMode = "—";
    [ObservableProperty] private string _gazeRegion = "—";
    [ObservableProperty] private string _cognitiveState = "—";
    [ObservableProperty] private string _valence = "—";
    [ObservableProperty] private double _confidence;
    [ObservableProperty] private double _confidencePercent;
    [ObservableProperty] private double _fps;
    [ObservableProperty] private double _visionLatencyMs;
    [ObservableProperty] private int _trackedPersons;
    [ObservableProperty] private double _beamAngle;
    [ObservableProperty] private string _sensorStatus = "Disconnected";
    [ObservableProperty] private Brush _sensorStatusBrush = Brushes.Gray;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _chatInput = "";
    [ObservableProperty] private SceneSnapshot? _latestSnapshot;

    // ── Kinect Settings Properties ─────────────────────────────

    private int _elevationAngle;
    public int ElevationAngle
    {
        get => _elevationAngle;
        set
        {
            if (SetProperty(ref _elevationAngle, Math.Clamp(value, -27, 27)))
            {
                try { _sensor.ElevationAngle = _elevationAngle; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to set elevation"); }
            }
        }
    }

    [ObservableProperty] private bool _skeletonTrackingEnabled = true;
    [ObservableProperty] private bool _faceTrackingEnabled = true;
    [ObservableProperty] private bool _colorStreamEnabled = true;
    [ObservableProperty] private bool _depthStreamEnabled = true;
    [ObservableProperty] private bool _showFullSkeleton = true;
    [ObservableProperty] private string _enrollmentName = "";
    [ObservableProperty] private string _recognizedIdentity = "—";
    [ObservableProperty] private string _leftEyePosition = "—";
    [ObservableProperty] private string _rightEyePosition = "—";
    [ObservableProperty] private string _interOcularDistance = "—";

    // ── Avatar Gaze Telemetry ──────────────────────────────────
    [ObservableProperty] private string _avatarTrackingMode = "—";
    [ObservableProperty] private string _avatarUserDistance = "—";
    [ObservableProperty] private string _avatarGazePitch = "—";
    [ObservableProperty] private string _avatarGazeYaw = "—";

    // ── Avatar Mode (2D / 3D hot-swap) ───────────────────────
    [ObservableProperty] private string _currentAvatarMode = "2D Happy";

    partial void OnCurrentAvatarModeChanged(string value)
    {
        if (_avatarWindow is { IsLoaded: true })
            _avatarWindow.SetAvatarMode(value == "3D Wireframe");
    }

    private bool _suppressSeatedModeToggle;

    partial void OnShowFullSkeletonChanged(bool value)
    {
        if (_suppressSeatedModeToggle) return;
        try
        {
            _sensor.SeatedMode = !value;
            // If manually switching back to full-body, reset the auto-fallback flag
            if (value && _pipeline is HCEPPipelineOrchestrator orch)
                orch.ResetAutoFallback();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to toggle skeleton mode"); }
    }

    /// <summary>
    /// Called by the orchestrator when it auto-switches between seated/full-body mode.
    /// Updates the UI toggle without re-triggering the sensor switch.
    /// </summary>
    private void OnGazeVectorReady(float pitch, float yaw, float distanceM, bool isPrecision)
    {
        // GazeVectorReady fires from the background pipeline thread — dispatch to UI.
        _dispatcher.InvokeAsync(() =>
        {
            AvatarTrackingMode = isPrecision ? "Precision" : "Fallback";
            AvatarUserDistance = $"{distanceM:F2} m";
            AvatarGazePitch = $"{pitch * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
            AvatarGazeYaw = $"{yaw * 180f / MathF.PI:+0.0;-0.0;+0.0}°";
        });
    }

    private void OnSeatedModeChanged(bool isSeated)
    {
        _dispatcher.InvokeAsync(() =>
        {
            _suppressSeatedModeToggle = true;
            try
            {
                ShowFullSkeleton = !isSeated;
                StatusMessage = isSeated
                    ? "Auto-switched to SEATED mode (closer range)"
                    : "Switched to FULL BODY mode";
            }
            finally
            {
                _suppressSeatedModeToggle = false;
            }
        }, DispatcherPriority.Normal);
    }

    private WriteableBitmap? _videoFrame;
    public WriteableBitmap? VideoFrame
    {
        get => _videoFrame;
        private set => SetProperty(ref _videoFrame, value);
    }
    private int _videoThrottle;

    public ObservableCollection<string> SpeechLog { get; } = [];
    public ObservableCollection<string> ChatLog { get; } = [];

    // ── Lifecycle ──────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try
        {
            _pipeline.SnapshotReady += OnSnapshotReady;
            _pipeline.SpeechReady += OnSpeechReady;
            _pipeline.ColorFrameReady += OnColorFrameReady;
            _pipeline.LlmResponseReady += OnLlmResponseReady;

            // Subscribe to orchestrator-specific events
            if (_pipeline is HCEPPipelineOrchestrator orch)
            {
                orch.SeatedModeChanged += OnSeatedModeChanged;
                orch.GazeVectorReady += OnGazeVectorReady;
            }

            // Start metrics refresh timer (4 Hz)
            _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _metricsTimer.Tick += (_, _) => RefreshMetrics();
            _metricsTimer.Start();

            await _pipeline.StartAsync();
            StatusMessage = "Pipeline running";
            _logger.LogInformation("Dashboard initialized");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to initialize pipeline");
        }
    }

    public async Task ShutdownAsync()
    {
        _metricsTimer?.Stop();
        _pipeline.SnapshotReady -= OnSnapshotReady;
        _pipeline.SpeechReady -= OnSpeechReady;
        _pipeline.ColorFrameReady -= OnColorFrameReady;
        _pipeline.LlmResponseReady -= OnLlmResponseReady;
        if (_pipeline is HCEPPipelineOrchestrator o)
        {
            o.SeatedModeChanged -= OnSeatedModeChanged;
            o.GazeVectorReady -= OnGazeVectorReady;
        }

        try { await _pipeline.StopAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error during shutdown"); }
    }

    // ── Commands ───────────────────────────────────────────────

    [RelayCommand]
    private void OpenSensorView()
    {
        if (_sensorViewWindow is { IsLoaded: true })
        {
            _sensorViewWindow.Activate();
            return;
        }
        _sensorViewWindow = _services.GetRequiredService<SensorViewWindow>();
        _sensorViewWindow.Closed += (_, _) => _sensorViewWindow = null;
        _sensorViewWindow.Show();
        _logger.LogInformation("Sensor View window opened");
    }

    [RelayCommand]
    private void OpenKinectVideo()
    {
        if (_kinectVideoWindow is { IsLoaded: true })
        {
            _kinectVideoWindow.Activate();
            return;
        }
        _kinectVideoWindow = _services.GetRequiredService<KinectVideoWindow>();
        _kinectVideoWindow.Closed += (_, _) => _kinectVideoWindow = null;
        _kinectVideoWindow.Show();
        _logger.LogInformation("Kinect Video window opened");
    }

    [RelayCommand]
    private void OpenCalibration()
    {
        if (_calibrationWindow is { IsLoaded: true })
        {
            _calibrationWindow.Activate();
            return;
        }
        _calibrationWindow = _services.GetRequiredService<CalibrationWindow>();
        _calibrationWindow.Closed += (_, _) => _calibrationWindow = null;
        _calibrationWindow.Show();
        _logger.LogInformation("Calibration window opened");
    }

    [RelayCommand]
    private void OpenAvatarWindow()
    {
        if (_avatarWindow is { IsLoaded: true })
        {
            _avatarWindow.Activate();
            return;
        }
        _avatarWindow = _services.GetRequiredService<AvatarWindow>();
        _avatarWindow.Closed += (_, _) => _avatarWindow = null;
        _avatarWindow.Show();
        _logger.LogInformation("Avatar window opened");
    }

    [RelayCommand]
    private void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void ElevationUp()
    {
        ElevationAngle = Math.Min(ElevationAngle + 2, 27);
    }

    [RelayCommand]
    private void ElevationDown()
    {
        ElevationAngle = Math.Max(ElevationAngle - 2, -27);
    }

    [RelayCommand]
    private void EnrollFace()
    {
        if (string.IsNullOrWhiteSpace(EnrollmentName))
        {
            StatusMessage = "Enter a name to enroll";
            return;
        }

        if (_pipeline is HCEPPipelineOrchestrator orchestrator)
        {
            if (!orchestrator.IsArcFaceModelLoaded)
            {
                StatusMessage = "Face recognition model not loaded — place arcface.onnx in models/ folder";
                return;
            }

            var name = EnrollmentName.Trim();
            orchestrator.EnrollFace(name);
            StatusMessage = $"Enrolling face as '{name}'...";
            _logger.LogInformation("Enrolling face: {Name}", name);
            EnrollmentName = "";
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        var message = ChatInput;
        ChatInput = "";
        ChatLog.Add($"You: {message}");

        try
        {
            var hcep = _pipeline.LatestSnapshot?.PrimaryPerson?.LatestHcep;
            var exchange = await _llmEngine.PromptAsync(message, hcep);
            ChatLog.Add($"HCEP: {exchange.Response}");
        }
        catch (Exception ex)
        {
            ChatLog.Add($"[Error: {ex.Message}]");
        }
    }

    // ── Pipeline Callbacks ─────────────────────────────────────

    private long _snapshotCount;

    private void OnSnapshotReady(SceneSnapshot snapshot)
    {
        var count = Interlocked.Increment(ref _snapshotCount);

        _dispatcher.InvokeAsync(() =>
        {
            var person = snapshot.PrimaryPerson;
            var hcep = person?.LatestHcep;

            if (hcep is not null)
            {
                CurrentMode = hcep.Mode.ToString().ToUpperInvariant();
                GazeRegion = hcep.Region.ToString();
                CognitiveState = hcep.Cognitive.ToString();
                Valence = hcep.Valence.ToString();
                Confidence = hcep.Confidence;
                ConfidencePercent = hcep.Confidence * 100;
            }
            else if (person is not null)
            {
                // Show partial tracking state when we have a person but no HCEP
                CurrentMode = "TRACKING";
                GazeRegion = person.Face is not null ? "Face detected" : "Position only";
                CognitiveState = "Detecting";
                Valence = "—";
            }

            TrackedPersons = snapshot.Persons.Length;

            // Update identity display
            if (person is not null)
            {
                RecognizedIdentity = !string.IsNullOrEmpty(person.IdentityName)
                    ? $"{person.IdentityName} ({person.IdentityConfidence:P0})"
                    : "Unknown";

                // ── Eye Location Telemetry (PRIMARY data) ──
                if (person.LeftEyePosition != default)
                    LeftEyePosition = $"({person.LeftEyePosition.X:F3}, {person.LeftEyePosition.Y:F3}, {person.LeftEyePosition.Z:F2})";
                else
                    LeftEyePosition = "—";

                if (person.RightEyePosition != default)
                    RightEyePosition = $"({person.RightEyePosition.X:F3}, {person.RightEyePosition.Y:F3}, {person.RightEyePosition.Z:F2})";
                else
                    RightEyePosition = "—";

                InterOcularDistance = person.InterOcularDistanceM > 0
                    ? $"{person.InterOcularDistanceM * 1000:F1} mm"
                    : "—";
            }
            else
            {
                RecognizedIdentity = "—";
                LeftEyePosition = "—";
                RightEyePosition = "—";
                InterOcularDistance = "—";
            }

            // Update visualization panel
            LatestSnapshot = snapshot;

            if (count <= 3 || count % 300 == 0)
                _logger.LogInformation(
                    "UI snapshot #{Count}: persons={Persons} hasHcep={HasHcep} hasFace={HasFace} mode={Mode}",
                    count, snapshot.Persons.Length, hcep is not null,
                    person?.Face is not null, CurrentMode);
        }, DispatcherPriority.Render);
    }

    private void OnSpeechReady(SpeechResult result)
    {
        _dispatcher.InvokeAsync(() =>
        {
            SpeechLog.Add($"[{result.Timestamp:HH:mm:ss}] {result.Text}");

            // Auto-scroll: keep only the last 100 entries
            while (SpeechLog.Count > 100)
                SpeechLog.RemoveAt(0);
        }, DispatcherPriority.Normal);
    }

    private void OnColorFrameReady(ColorFrame frame)
    {
        // Throttle to ~15 fps for display
        if (Interlocked.Increment(ref _videoThrottle) % 2 != 0) return;

        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (VideoFrame is null || VideoFrame.PixelWidth != frame.Width
                                       || VideoFrame.PixelHeight != frame.Height)
                {
                    VideoFrame = new WriteableBitmap(
                        frame.Width, frame.Height, 96, 96,
                        PixelFormats.Bgra32, null);
                }

                VideoFrame.WritePixels(
                    new Int32Rect(0, 0, frame.Width, frame.Height),
                    frame.PixelData,
                    frame.Width * frame.BytesPerPixel,
                    0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnColorFrameReady ({W}x{H}, {Len} bytes)",
                    frame.Width, frame.Height, frame.PixelData?.Length ?? 0);
            }
        }, DispatcherPriority.Render);
    }

    private void OnLlmResponseReady(LlmExchange exchange)
    {
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                // Show the user's speech that triggered the LLM
                ChatLog.Add($"User: {exchange.UserMessage}");

                // Show the LLM response with model info
                var model = exchange.IsLocal ? "local" : "cloud";
                ChatLog.Add($"HCEP ({model}): {exchange.Response}");

                // Auto-scroll: keep last 100 entries
                while (ChatLog.Count > 100)
                    ChatLog.RemoveAt(0);

                _telemetry.RecordGauge("llm.last_latency_ms", exchange.Latency.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnLlmResponseReady");
            }
        }, DispatcherPriority.Normal);
    }

    private void RefreshMetrics()
    {
        try
        {
            Fps = _pipeline.CurrentFps;
            VisionLatencyMs = _telemetry.GetGauge("vision.frame_ms.avg_ms");
            BeamAngle = _telemetry.GetGauge("audio.beam_angle");

            SensorStatus = _pipeline.IsRunning ? "Connected" : "Disconnected";
            SensorStatusBrush = _pipeline.IsRunning
                ? (Brush)Application.Current.Resources["SuccessBrush"]
                : (Brush)Application.Current.Resources["ErrorBrush"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RefreshMetrics");
        }
    }
}
