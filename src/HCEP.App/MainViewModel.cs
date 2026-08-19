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
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HCEP.Core.Diagnostics;
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
    private readonly object _chatTelemetryHistoryGate = new();
    private readonly List<HcepTelemetrySample> _chatTelemetryHistory = [];
    private static readonly TimeSpan MaxChatTelemetryHistoryAge = TimeSpan.FromSeconds(5.5);
    private bool _isInitializingChatHarnessSettings;

    // Child window references (prevent duplicates)
    private SensorViewWindow? _sensorViewWindow;
    private KinectVideoWindow? _kinectVideoWindow;
    private CalibrationWindow? _calibrationWindow;
    private AvatarWindow? _avatarWindow;
    private AvatarStudioWindow? _avatarStudioWindow;
    private FaceMeshAlignmentWindow? _faceMeshAlignmentWindow;
    private SkeletalAlignmentWindow? _skeletalAlignmentWindow;
    private PnPHeadPoseCalibrationWindow? _pnpCalibrationWindow;
    private CheckForUpdatesWindow? _checkForUpdatesWindow;
    private EyePositionCalibrationWindow? _eyePositionCalibrationWindow;

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

        if (_llmEngine is HCEP.Intelligence.HybridLlmEngine hybrid)
        {
            _isInitializingChatHarnessSettings = true;
            try
            {
                _chatTelemetryWindowSeconds = Math.Clamp(hybrid.Configuration.ChatTelemetryWindowSeconds, 0, 5);
                _chatTelemetryDensityLevel = Math.Clamp(hybrid.Configuration.ChatTelemetryDensityLevel, 1, 3);
                _chatTelemetryDebugExpanded = hybrid.Configuration.ChatTelemetryDebugExpanded;
                _chatSystemPromptDebugExpanded = hybrid.Configuration.ChatSystemPromptDebugExpanded;
            }
            finally
            {
                _isInitializingChatHarnessSettings = false;
            }
        }

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
    [ObservableProperty] private int _chatTelemetryWindowSeconds = 2;
    [ObservableProperty] private int _chatTelemetryDensityLevel = 2;
    [ObservableProperty] private string _lastInjectedTelemetryText = "No telemetry has been injected into chat yet.";
    [ObservableProperty] private string _lastInjectedSystemPromptText = "No full system prompt has been generated yet.";
    [ObservableProperty] private string _chatPromptBudgetEstimateText = "Est. request size: unavailable";
    [ObservableProperty] private bool _chatTelemetryDebugExpanded;
    [ObservableProperty] private bool _chatSystemPromptDebugExpanded;

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

    // Track pending enrollment so RefreshMetrics can confirm completion
    private string? _pendingEnrollmentName;
    private int _preEnrollmentCount;
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

    public string ChatTelemetryWindowLabel =>
        ChatTelemetryWindowSeconds <= 0 ? "Snapshot" : $"{ChatTelemetryWindowSeconds}s";

    public string ChatTelemetryDensityLabel => ChatTelemetryDensityLevel switch
    {
        1 => "Sparse",
        2 => "Balanced",
        3 => "Dense",
        _ => "Balanced"
    };

    partial void OnChatTelemetryWindowSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(ChatTelemetryWindowLabel));
        PersistChatHarnessSettings();
        RefreshChatPromptPreview();
    }

    partial void OnChatTelemetryDensityLevelChanged(int value)
    {
        OnPropertyChanged(nameof(ChatTelemetryDensityLabel));
        PersistChatHarnessSettings();
        RefreshChatPromptPreview();
    }

    partial void OnChatTelemetryDebugExpandedChanged(bool value)
    {
        PersistChatHarnessSettings();
    }

    partial void OnChatSystemPromptDebugExpandedChanged(bool value)
    {
        PersistChatHarnessSettings();
    }

    partial void OnChatInputChanged(string value)
    {
        RefreshChatPromptPreview();
    }

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
    private void OpenAvatarStudio()
    {
        if (_avatarStudioWindow is { IsLoaded: true })
        {
            _avatarStudioWindow.Activate();
            return;
        }
        _avatarStudioWindow = _services.GetRequiredService<AvatarStudioWindow>();
        _avatarStudioWindow.Closed += (_, _) => _avatarStudioWindow = null;
        _avatarStudioWindow.Show();
        _logger.LogInformation("Avatar Studio window opened");
    }

    [RelayCommand]
    private void OpenFaceMeshAlignment()
    {
        if (_faceMeshAlignmentWindow is { IsLoaded: true })
        {
            _faceMeshAlignmentWindow.Activate();
            return;
        }
        _faceMeshAlignmentWindow = _services.GetRequiredService<FaceMeshAlignmentWindow>();
        _faceMeshAlignmentWindow.Closed += (_, _) => _faceMeshAlignmentWindow = null;
        _faceMeshAlignmentWindow.Owner = System.Windows.Application.Current.MainWindow;
        _faceMeshAlignmentWindow.Show();
        _logger.LogInformation("Face Mesh Alignment window opened");
    }

    [RelayCommand]
    private void OpenSkeletalAlignment()
    {
        if (_skeletalAlignmentWindow is { IsLoaded: true })
        {
            _skeletalAlignmentWindow.Activate();
            return;
        }
        _skeletalAlignmentWindow = _services.GetRequiredService<SkeletalAlignmentWindow>();
        _skeletalAlignmentWindow.Closed += (_, _) => _skeletalAlignmentWindow = null;
        _skeletalAlignmentWindow.Owner = System.Windows.Application.Current.MainWindow;
        _skeletalAlignmentWindow.Show();
        _logger.LogInformation("Skeletal Alignment window opened");
    }

    [RelayCommand]
    private void OpenPnPCalibration()
    {
        if (_pnpCalibrationWindow is { IsLoaded: true })
        {
            _pnpCalibrationWindow.Activate();
            return;
        }
        _pnpCalibrationWindow = _services.GetRequiredService<PnPHeadPoseCalibrationWindow>();
        _pnpCalibrationWindow.Closed += (_, _) => _pnpCalibrationWindow = null;
        _pnpCalibrationWindow.Owner = System.Windows.Application.Current.MainWindow;
        _pnpCalibrationWindow.Show();
        _logger.LogInformation("PnP Head Pose Calibration window opened");
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        if (_checkForUpdatesWindow is { IsLoaded: true })
        {
            _checkForUpdatesWindow.Activate();
            return;
        }
        _checkForUpdatesWindow = _services.GetRequiredService<CheckForUpdatesWindow>();
        _checkForUpdatesWindow.Closed += (_, _) => _checkForUpdatesWindow = null;
        _checkForUpdatesWindow.Owner = System.Windows.Application.Current.MainWindow;
        _checkForUpdatesWindow.Show();
        _logger.LogInformation("Check for Updates window opened");
    }

    [RelayCommand]
    private void OpenEyePositionCalibration()
    {
        if (_eyePositionCalibrationWindow is { IsLoaded: true })
        {
            _eyePositionCalibrationWindow.Activate();
            return;
        }
        _eyePositionCalibrationWindow = _services.GetRequiredService<EyePositionCalibrationWindow>();
        _eyePositionCalibrationWindow.Closed += (_, _) => _eyePositionCalibrationWindow = null;
        _eyePositionCalibrationWindow.Owner = System.Windows.Application.Current.MainWindow;
        _eyePositionCalibrationWindow.Show();
        _logger.LogInformation("Eye Position Calibration window opened");
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var version = HCEP.App.Updates.UpdateService.GetCurrentVersion();
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"HCEP — Human Communication Eye Protocol\n" +
            $"Version {version}\n\n" +
            $"Real-time multi-modal perception platform fusing Kinect sensor input with a\n" +
            $"hybrid local + cloud LLM engine to analyse human communication through eye\n" +
            $"contact patterns, facial expressions, body tracking, and speech.\n\n" +
            $"© 2026 Kirk LaSalle. All rights reserved.\n" +
            $"Proprietary — HCEP theory, Permanent Active Directives, and Body Language\n" +
            $"Protocols are trade secrets of Kirk LaSalle.",
            "About HCEP",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private SettingsWindow? _settingsWindow;

    [RelayCommand]
    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _logger.LogInformation("Settings window opened");
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

            // Biometric consent compliance dialog (S-04)
            var consentResult = MessageBox.Show(
                $"HCEP Biometric Enrollment Consent\n\n" +
                $"To enroll '{name}', HCEP must capture and store facial feature vectors (embeddings).\n" +
                $"This biometric data is encrypted on your local system using Windows DPAPI.\n" +
                $"It will not be uploaded or shared without your explicit permission.\n\n" +
                $"Do you consent to the collection, storage, and processing of this biometric data?",
                "Biometric Data Consent Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (consentResult != MessageBoxResult.Yes)
            {
                StatusMessage = "Enrollment cancelled: Consent was not provided.";
                _logger.LogWarning("Biometric enrollment for '{Name}' cancelled due to lack of consent.", name);
                return;
            }

            orchestrator.EnrollFace(name);
            _pendingEnrollmentName = name;
            _preEnrollmentCount = orchestrator.EnrolledFaceCount;
            StatusMessage = $"Enrolling '{name}'… look at the camera";
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

        string correlationId = CorrelationContext.Create("chat");
        using var correlationScope = CorrelationContext.BeginScope(correlationId);

        try
        {
            _telemetry.Increment("correlation.chat.requests");
            _telemetry.RecordGauge("correlation.chat.last_hash", CorrelationContext.ToNumericFingerprint(correlationId));

            // ── Build the LLM-facing telemetry bundle so the assistant can
            //    ground its reply in real HCEP signals instead of hallucinating
            //    a visual channel. This is additive — the engine still accepts
            //    a plain HcepReading as before.
            var snapshot = _pipeline.LatestSnapshot;
            var person = snapshot?.PrimaryPerson;
            var hcep = person?.LatestHcep;

            var bundle = BuildTelemetryBundle(snapshot);

            if (_llmEngine is HCEP.Intelligence.HybridLlmEngine hybrid)
            {
                bundle = bundle with { Context = hybrid.CurrentContext };
                hybrid.LatestTelemetry = bundle;
                LastInjectedTelemetryText = bundle.ToPromptString();
                LastInjectedSystemPromptText = hybrid.PreviewSystemPrompt(hcep);
            }
            else
            {
                LastInjectedTelemetryText = bundle.ToPromptString();
                LastInjectedSystemPromptText = "System-prompt preview unavailable because the active LLM engine is not HybridLlmEngine.";
            }

            var exchange = await _llmEngine.PromptAsync(message, hcep);
            ChatLog.Add($"HCEP: {exchange.Response}");

            if (!string.IsNullOrWhiteSpace(exchange.CorrelationId))
                _telemetry.RecordGauge("correlation.llm.last_hash", CorrelationContext.ToNumericFingerprint(exchange.CorrelationId));
        }
        catch (Exception ex)
        {
            ChatLog.Add($"[Error: {ex.Message}]");
        }
    }

    /// <summary>
    /// Clears the visible chat log. Additive — does not affect persisted
    /// telemetry or on-disk conversation history.
    /// </summary>
    [RelayCommand]
    private void ClearChat()
    {
        ChatLog.Clear();
    }

    [RelayCommand]
    private void CopyTelemetryPrompt()
    {
        Clipboard.SetText(LastInjectedTelemetryText ?? string.Empty);
        StatusMessage = "Telemetry prompt copied to clipboard";
    }

    [RelayCommand]
    private void CopySystemPrompt()
    {
        Clipboard.SetText(LastInjectedSystemPromptText ?? string.Empty);
        StatusMessage = "Full system prompt copied to clipboard";
    }

    // ── Pipeline Callbacks ─────────────────────────────────────

    private long _snapshotCount;

    private void OnSnapshotReady(SceneSnapshot snapshot)
    {
        CaptureChatTelemetrySample(snapshot);
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
            RefreshChatPromptPreview(snapshot);

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

                if (!string.IsNullOrWhiteSpace(exchange.CorrelationId))
                    _telemetry.RecordGauge("correlation.llm.last_hash", CorrelationContext.ToNumericFingerprint(exchange.CorrelationId));
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

            // ── Enrollment completion detection ────────────────────────────
            // EnrollFace() is asynchronous — PendingEnrollmentName is consumed
            // by the recognition loop (~1 Hz). Poll here at 4 Hz to detect when
            // the enrolled face count increases, confirming success.
            if (_pendingEnrollmentName is not null
                && _pipeline is HCEPPipelineOrchestrator enrollOrch
                && enrollOrch.EnrolledFaceCount > _preEnrollmentCount)
            {
                StatusMessage = $"✓ '{_pendingEnrollmentName}' enrolled successfully ({enrollOrch.EnrolledFaceCount} total)";
                _logger.LogInformation("Face enrollment confirmed for '{Name}'", _pendingEnrollmentName);
                _pendingEnrollmentName = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RefreshMetrics");
        }
    }

    private HcepTelemetryBundle BuildTelemetryBundle(SceneSnapshot? snapshot)
    {
        var person = snapshot?.PrimaryPerson;
        var history = GetTelemetryWindowHistory(snapshot?.Timestamp ?? DateTimeOffset.UtcNow, ChatTelemetryWindowSeconds);
        var (requestedTimelineSampleCount, effectiveTimelineSampleCount, autoCoarsened) = GetTimelineSamplingPlan(history);

        var bundle = new HcepTelemetryBundle
        {
            CorrelationId = CorrelationContext.Current,
            CapturedAt = DateTimeOffset.UtcNow,
            PipelineRunning = _pipeline.IsRunning,
            PipelineFps = _pipeline.CurrentFps,
            TrackedPersons = snapshot?.Persons.Length ?? 0,
            PrimaryHcep = person?.LatestHcep,
            PrimaryIdentity = person?.IdentityName,
            PrimaryDistanceM = person?.DistanceM,
            LeftEyePosition = person?.LeftEyePosition,
            RightEyePosition = person?.RightEyePosition,
            InterOcularDistanceMm = person is null ? null : person.InterOcularDistanceM * 1000f,
            HeadRotationDeg = person?.Face?.HeadRotation,
            LatestSpeech = snapshot?.LatestSpeech?.Text,
            TelemetryWindowSeconds = ChatTelemetryWindowSeconds,
            TelemetryTimelineSampleCount = effectiveTimelineSampleCount,
            RequestedTelemetryTimelineSampleCount = requestedTimelineSampleCount,
            TelemetryTimelineAutoCoarsened = autoCoarsened,
            History = history,
            SensorConnected = SensorStatus.Equals("Connected", StringComparison.OrdinalIgnoreCase),
            CalibrationApplied = _pipeline is HCEPPipelineOrchestrator,
        };

        if (_pipeline is HCEPPipelineOrchestrator orch)
            bundle = bundle with { Cadence = orch.LatestCadence };

        return bundle;
    }

    private void CaptureChatTelemetrySample(SceneSnapshot snapshot)
    {
        var person = snapshot.PrimaryPerson;
        var hcep = person?.LatestHcep;

        var sample = new HcepTelemetrySample
        {
            Timestamp = snapshot.Timestamp,
            TrackedPersons = snapshot.Persons.Length,
            PrimaryIdentity = person?.IdentityName,
            Mode = hcep?.Mode ?? HCEP.Core.Enums.HcepMode.Unknown,
            Region = hcep?.Region ?? HCEP.Core.Enums.GazeRegion.Unknown,
            Cognitive = hcep?.Cognitive ?? HCEP.Core.Enums.CognitiveState.Unknown,
            Valence = hcep?.Valence ?? HCEP.Core.Enums.EmotionalValence.Unknown,
            Confidence = hcep?.Confidence ?? 0f,
            DistanceM = person?.DistanceM,
            HeadRotationDeg = person?.Face?.HeadRotation,
            LatestSpeech = snapshot.LatestSpeech?.Text,
        };

        lock (_chatTelemetryHistoryGate)
        {
            _chatTelemetryHistory.Add(sample);
            PruneTelemetryHistory(snapshot.Timestamp);
        }
    }

    private IReadOnlyList<HcepTelemetrySample> GetTelemetryWindowHistory(DateTimeOffset referenceTime, int seconds)
    {
        lock (_chatTelemetryHistoryGate)
        {
            PruneTelemetryHistory(referenceTime);
            if (seconds <= 0)
                return _chatTelemetryHistory.Count == 0 ? [] : [_chatTelemetryHistory[^1]];

            var cutoff = referenceTime - TimeSpan.FromSeconds(seconds);
            var samples = _chatTelemetryHistory
                .Where(sample => sample.Timestamp >= cutoff)
                .OrderBy(sample => sample.Timestamp)
                .ToArray();

            if (samples.Length == 0 && _chatTelemetryHistory.Count > 0)
                return [_chatTelemetryHistory[^1]];

            return samples;
        }
    }

    private void PruneTelemetryHistory(DateTimeOffset referenceTime)
    {
        var cutoff = referenceTime - MaxChatTelemetryHistoryAge;
        _chatTelemetryHistory.RemoveAll(sample => sample.Timestamp < cutoff);
    }

    private int GetTimelineSampleCount() => ChatTelemetryDensityLevel switch
    {
        1 => 3,
        2 => 5,
        3 => 7,
        _ => 5,
    };

    private (int Requested, int Effective, bool AutoCoarsened) GetTimelineSamplingPlan(IReadOnlyList<HcepTelemetrySample> history)
    {
        int requested = GetTimelineSampleCount();
        int effective = requested;

        int speechChars = history
            .Where(sample => !string.IsNullOrWhiteSpace(sample.LatestSpeech))
            .Sum(sample => Math.Min(sample.LatestSpeech!.Length, 120));

        if (history.Count >= 45 || ChatTelemetryWindowSeconds >= 4)
            effective = Math.Min(effective, 5);

        if (history.Count >= 80 || speechChars >= 260)
            effective = Math.Min(effective, 4);

        if (history.Count >= 120 || speechChars >= 420)
            effective = 3;

        effective = Math.Clamp(effective, 3, 9);
        return (requested, effective, effective < requested);
    }

    private void PersistChatHarnessSettings()
    {
        if (_isInitializingChatHarnessSettings) return;
        if (_llmEngine is not HCEP.Intelligence.HybridLlmEngine hybrid) return;

        hybrid.Configuration.ChatTelemetryWindowSeconds = Math.Clamp(ChatTelemetryWindowSeconds, 0, 5);
        hybrid.Configuration.ChatTelemetryDensityLevel = Math.Clamp(ChatTelemetryDensityLevel, 1, 3);
        hybrid.Configuration.ChatTelemetryDebugExpanded = ChatTelemetryDebugExpanded;
        hybrid.Configuration.ChatSystemPromptDebugExpanded = ChatSystemPromptDebugExpanded;
        HCEP.Intelligence.SettingsPersistence.Save(hybrid.Configuration, _logger);
    }

    private void RefreshChatPromptPreview(SceneSnapshot? snapshotOverride = null)
    {
        var snapshot = snapshotOverride ?? LatestSnapshot ?? _pipeline.LatestSnapshot;
        if (snapshot is null)
        {
            ChatPromptBudgetEstimateText = "Est. request size: waiting for telemetry";
            return;
        }

        var person = snapshot.PrimaryPerson;
        var hcep = person?.LatestHcep;
        var bundle = BuildTelemetryBundle(snapshot);

        if (_llmEngine is HCEP.Intelligence.HybridLlmEngine hybrid)
        {
            bundle = bundle with { Context = hybrid.CurrentContext };
            LastInjectedTelemetryText = bundle.ToPromptString();
            hybrid.LatestTelemetry = bundle;
            LastInjectedSystemPromptText = hybrid.PreviewSystemPrompt(hcep);

            int promptTokens = EstimateTokens(LastInjectedSystemPromptText);
            int userTokens = EstimateTokens(ChatInput);
            int total = promptTokens + userTokens;
            string coarsened = bundle.TelemetryTimelineAutoCoarsened ? " | auto-coarsened" : string.Empty;
            ChatPromptBudgetEstimateText = $"Est. request size: ~{total} tokens (prompt ~{promptTokens}, input ~{userTokens}){coarsened}";
        }
        else
        {
            LastInjectedTelemetryText = bundle.ToPromptString();
            LastInjectedSystemPromptText = "System-prompt preview unavailable because the active LLM engine is not HybridLlmEngine.";
            ChatPromptBudgetEstimateText = "Est. request size: unavailable";
        }
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }
}
