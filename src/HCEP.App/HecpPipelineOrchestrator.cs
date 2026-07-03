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
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Audio;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Intelligence;
using HCEP.Kinect;
using HCEP.Knowledge;
using HCEP.Telemetry;
using HCEP.Spatial;
using HCEP.Vision;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// Orchestrates the full HCEP perception pipeline:
/// Sensor → Vision Pipeline → Audio Pipeline → Scene Snapshot.
/// Runs as a background async loop, composing snapshots from all subsystems.
/// </summary>
public sealed partial class HCEPPipelineOrchestrator : IPipelineOrchestrator, IAsyncDisposable
{
    private ISensorSource _sensor;
    private readonly SimulatedSensorSource _fallbackSensor;
    private readonly VisionPipeline _vision;
    private readonly AudioPipeline _audio;
    private readonly PersonKnowledgeManager _personKnowledge;
    private readonly AgenticToolExecutor _toolExecutor;
    private readonly ILlmEngine _llmEngine;
    private readonly IFaceRecognizer _faceRecognizer;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<HCEPPipelineOrchestrator> _logger;
    private readonly FpsCounter _fpsCounter = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private Task? _hcepConsumerTask;
    private Task? _speechTask;
    private volatile bool _isRunning;
    private volatile SceneSnapshot? _latestSnapshot;
    private volatile SpeechResult? _latestSpeech;
    private volatile SkeletonFrame? _latestSkeleton;
    private volatile FaceFrame? _latestFace;
    private volatile HcepReading? _latestHcep;
    private readonly FpsCounter _hcepFpsCounter = new();
    private long _faceFrameCount;
    private long _skelFrameCount;

    // ── Phase 6: True Gaze Avatar ───────────────────────────
    // MVP defaults — Kinect centred horizontally, 120 mm above screen centre,
    // 30 mm forward of the bezel.  24" monitor (1920×1080) physical dims.
    private const float KinectOffsetXMm = 0f;
    private const float KinectOffsetYMm = 120f;   // mm above screen centre
    private const float KinectOffsetZMm = 30f;   // mm in front of bezel
    private const float ScreenWidthMm = 530f;   // ~24" 16:9 panel
    private const float ScreenHeightMm = 300f;

    private CalibrationMatrixCalculator _calibration = new(
        kinectOffsetFromScreenCentreMm: new Vector3(KinectOffsetXMm, KinectOffsetYMm, KinectOffsetZMm),
        screenWidthMm: ScreenWidthMm,
        screenHeightMm: ScreenHeightMm);

    private readonly MicroSaccadeController _saccade = new();

    // ── Phase 3: World-Space Gaze Engine ────────────────────
    private readonly GazeVectorEngine _gazeEngine = new();
    /// <summary>Thread-safe delegate that reads the Avatar eye screen positions (physical px).</summary>
    private Func<(Vector2 left, Vector2 right)>? _avatarEyeProvider;
    private float _avatarScreenWidthPx;
    private float _avatarScreenHeightPx;

    // ── Auto-fallback: full-body → seated mode ──────────────
    private DateTimeOffset _lastPersonSeenAt = DateTimeOffset.MinValue;
    private bool _autoFellBackToSeated;

    /// <summary>
    /// Seconds without a detected person before the pipeline auto-switches from full-body
    /// to seated skeleton-tracking mode. Configurable at runtime.
    /// Default: 5 seconds. Set to <see cref="double.MaxValue"/> to disable auto-fallback.
    /// </summary>
    public double AutoFallbackSeconds { get; set; } = 5.0;

    public HCEPPipelineOrchestrator(
        ISensorSource sensor,
        SimulatedSensorSource fallbackSensor,
        VisionPipeline vision,
        AudioPipeline audio,
        PersonKnowledgeManager personKnowledge,
        AgenticToolExecutor toolExecutor,
        ILlmEngine llmEngine,
        IFaceRecognizer faceRecognizer,
        ITelemetryService telemetry,
        ILogger<HCEPPipelineOrchestrator> logger)
    {
        _sensor = sensor;
        _fallbackSensor = fallbackSensor;
        _vision = vision;
        _audio = audio;
        _personKnowledge = personKnowledge;
        _toolExecutor = toolExecutor;
        _llmEngine = llmEngine;
        _faceRecognizer = faceRecognizer;
        _telemetry = telemetry;
        _logger = logger;
    }

    // ── IPipelineOrchestrator ──────────────────────────────────

    public bool IsRunning => _isRunning;
    public SceneSnapshot? LatestSnapshot => _latestSnapshot;
    public double CurrentFps => _hcepFpsCounter.Fps;

    /// <summary>
    /// The most recent face-tracking frame from the Kinect sensor.
    /// Read by <see cref="CalibrationWindow"/> during the calibration protocol.
    /// </summary>
    public FaceFrame? LatestFaceFrame => _latestFace;

    /// <summary>
    /// Applies an empirically-determined Kinect-to-screen-centre offset computed
    /// by <see cref="CalibrationWindow"/> and rebuilds the calibration matrix.
    /// Safe to call from any thread — the new matrix is swapped atomically.
    /// </summary>
    /// <param name="kinectOffsetXMm">Kinect horizontal offset from screen centre (mm, +ve = Kinect right of centre).</param>
    /// <param name="kinectOffsetYMm">Kinect vertical offset above screen centre (mm, +ve = Kinect above centre).</param>
    /// <param name="kinectOffsetZMm">Kinect forward protrusion beyond screen bezel (mm, +ve = Kinect in front).</param>
    public void ApplyCalibration(float kinectOffsetXMm, float kinectOffsetYMm, float kinectOffsetZMm)
    {
        _calibration = new CalibrationMatrixCalculator(
            kinectOffsetFromScreenCentreMm: new Vector3(kinectOffsetXMm, kinectOffsetYMm, kinectOffsetZMm),
            screenWidthMm: ScreenWidthMm,
            screenHeightMm: ScreenHeightMm);

        // Calibration changed — reset smoothing so the new geometry takes effect immediately.
        _gazeEngine.Reset();

        _logger.LogInformation(
            "Calibration applied — KinectOffset: X={X:F1} mm, Y={Y:F1} mm, Z={Z:F1} mm",
            kinectOffsetXMm, kinectOffsetYMm, kinectOffsetZMm);
    }

    /// <summary>
    /// Registers the Avatar eye provider delegate and physical screen dimensions
    /// for the Phase 3 world-space gaze calculation.
    /// Called once by <see cref="AvatarWindow"/> after its visual tree is live.
    /// </summary>
    /// <param name="provider">Returns (left, right) avatar eye socket positions in physical screen pixels.</param>
    /// <param name="screenWidthPhysicalPx">Physical screen width (device pixels, not WPF DIPs).</param>
    /// <param name="screenHeightPhysicalPx">Physical screen height (device pixels, not WPF DIPs).</param>
    public void SetAvatarEyeProvider(
        Func<(Vector2 left, Vector2 right)> provider,
        float screenWidthPhysicalPx,
        float screenHeightPhysicalPx)
    {
        _avatarEyeProvider = provider;
        _avatarScreenWidthPx = screenWidthPhysicalPx;
        _avatarScreenHeightPx = screenHeightPhysicalPx;
        _gazeEngine.Reset();
        _logger.LogInformation(
            "AvatarEyeProvider registered — screen {W:F0}×{H:F0} px",
            screenWidthPhysicalPx, screenHeightPhysicalPx);
    }

    /// <summary>
    /// Enroll the currently tracked face under the given name.
    /// The enrollment runs on the next face recognition cycle (~1 sec).
    /// </summary>
    public void EnrollFace(string name)
    {
        _vision.PendingEnrollmentName = name;
        _logger.LogInformation("Face enrollment requested for: {Name}", name);
    }

    /// <summary>Number of enrolled face identities.</summary>
    public int EnrolledFaceCount => _faceRecognizer.EnrolledCount;

    /// <summary>Whether the ArcFace model has been loaded successfully.</summary>
    public bool IsArcFaceModelLoaded => _faceRecognizer is ArcFaceRecognizer arc && arc.IsModelLoaded;

    /// <summary>
    /// Optional TTS engine for lip-sync viseme events.
    /// Set by the DI container if HCEP.Speech is wired in (Phase 13).
    /// AvatarWindow subscribes to <c>TtsEngine.VisemeChanged</c>.
    /// </summary>
    public HCEP.Speech.HybridTtsEngine? TtsEngine { get; set; }

    /// <summary>
    /// Resets the auto-fallback flag so the system can try full-body mode again.
    /// Called when the user manually toggles back to full-body.
    /// </summary>
    public void ResetAutoFallback()
    {
        _autoFellBackToSeated = false;
        _lastPersonSeenAt = DateTimeOffset.UtcNow; // give 5 more seconds before retrying fallback
        _logger.LogInformation("Auto-fallback reset — trying full-body mode again");
    }

    /// <summary>
    /// Fires when the orchestrator auto-switches between seated/full-body mode.
    /// Boolean is true when seated mode was engaged.
    /// </summary>
    public event Action<bool>? SeatedModeChanged;

    public event Action<SceneSnapshot>? SnapshotReady;
    public event Action<SpeechResult>? SpeechReady;
    public event Action<ColorFrame>? ColorFrameReady;
    public event Action<DepthFrame>? DepthFrameReady;
    public event Action<ColorFrame>? InfraredFrameReady;
    public event Action<SkeletonFrame>? SkeletonFrameReady;
    public event Action<LlmExchange>? LlmResponseReady;

    /// <summary>
    /// Fires each snapshot tick (~10 Hz) with the smoothed gaze data for
    /// <c>AvatarCoreControl.SetGaze()</c>.  Raised from the background pipeline
    /// thread — subscribers must marshal to the UI thread before touching WPF objects.
    /// Parameters: (pitch radians, yaw radians, userDistanceM, isPrecisionTracking).
    /// </summary>
    public event Action<float, float, float, bool>? GazeVectorReady;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting HCEP pipeline...");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // ── Load ArcFace model (best-effort) ──────────────────
        try
        {
            var modelDir = Path.Combine(AppContext.BaseDirectory, "models");
            var modelPath = Path.Combine(modelDir, "arcface.onnx");
            if (File.Exists(modelPath))
            {
                if (_faceRecognizer is ArcFaceRecognizer arcFace)
                {
                    arcFace.LoadModel(modelPath);
                    _logger.LogInformation("ArcFace model loaded from {Path}", modelPath);
                }
            }
            else
            {
                _logger.LogInformation("ArcFace model not found at {Path} — face recognition disabled. " +
                    "Place arcface.onnx in the models/ folder to enable.", modelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ArcFace model — face recognition disabled");
        }

        // ── Load Whisper model (best-effort) ──────────────────
        try
        {
            var modelDir = Path.Combine(AppContext.BaseDirectory, "models");
            var whisperPath = Path.Combine(modelDir, "ggml-tiny.bin");
            if (File.Exists(whisperPath))
            {
                await _audio.LoadModelAsync(whisperPath, _cts.Token);
                _logger.LogInformation("Whisper model loaded from {Path}", whisperPath);
            }
            else
            {
                _logger.LogInformation("Whisper model not found at {Path} — speech recognition disabled. " +
                    "Place ggml-tiny.bin in the models/ folder to enable.", whisperPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Whisper model — speech recognition disabled");
        }

        // Initialize sensor — fall back to simulated if real sensor is unavailable
        await _sensor.InitializeAsync(SensorStreamType.All, _cts.Token);
        if (_sensor.State != SensorState.Connected)
        {
            _logger.LogWarning("{Sensor} unavailable — falling back to simulated mode",
                _sensor.GetType().Name);
            _sensor = _fallbackSensor;
            await _sensor.InitializeAsync(SensorStreamType.All, _cts.Token);
        }

        // Wire sensor events to pipeline channels
        WireSensorEvents(_sensor);

        // Start sub-pipelines
        await _vision.StartAsync(_cts.Token);
        await _audio.StartAsync(_cts.Token);

        // Start sensor streaming
        await _sensor.StartAsync(_cts.Token);

        // If hardware sensor failed to start, fall back to simulated
        if (_sensor.State != SensorState.Connected && _sensor is not SimulatedSensorSource)
        {
            _logger.LogWarning("Sensor failed after StartAsync (state={State}) — switching to simulated",
                _sensor.State);

            // Unwire event handlers from the failed sensor before switching
            UnwireSensorEvents(_sensor);
            _sensor = _fallbackSensor;
            await _sensor.InitializeAsync(SensorStreamType.All, _cts.Token);

            // Re-wire events on fallback sensor
            WireSensorEvents(_sensor);

            await _sensor.StartAsync(_cts.Token);
        }

        // Start HCEP reading consumer (background)
        _hcepConsumerTask = ConsumeHcepReadingsAsync(_cts.Token);

        // Start snapshot composition loop (timer-driven)
        _loopTask = RunSnapshotLoopAsync(_cts.Token);

        // Start speech consumption loop
        _speechTask = RunSpeechLoopAsync(_cts.Token);

        _isRunning = true;

        _logger.LogInformation("HCEP pipeline started");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Stopping HCEP pipeline...");
        _isRunning = false;

        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; }
            catch (OperationCanceledException) { }
        }
        if (_hcepConsumerTask is not null)
        {
            try { await _hcepConsumerTask; }
            catch (OperationCanceledException) { }
        }
        if (_speechTask is not null)
        {
            try { await _speechTask; }
            catch (OperationCanceledException) { }
        }

        UnwireSensorEvents(_sensor);
        await _sensor.StopAsync(ct);
        await _vision.StopAsync();
        await _audio.StopAsync();

        _cts?.Dispose();
        _logger.LogInformation("HCEP pipeline stopped");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isRunning)
            await StopAsync();

        _cts?.Dispose();
    }
}
