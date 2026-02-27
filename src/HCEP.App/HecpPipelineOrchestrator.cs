// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.Immutable;
using System.IO;
using System.Numerics;
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
public sealed class HCEPPipelineOrchestrator : IPipelineOrchestrator
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
    private const double AutoFallbackSeconds = 5.0;

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
    /// Fires each snapshot tick (~10 Hz) with the smoothed (pitch, yaw) radians
    /// for <c>AvatarCoreControl.SetGaze()</c>.  Raised from the background pipeline
    /// thread — subscribers must marshal to the UI thread before touching WPF objects.
    /// </summary>
    public event Action<float, float>? GazeVectorReady;

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
        long faceFrameCount = 0;
        _sensor.FaceFrameReady += face =>
        {
            _latestFace = face;
            bool written = _vision.FaceInput.TryWrite(face);
            var count = Interlocked.Increment(ref faceFrameCount);
            if (count <= 5 || count % 300 == 0)
                _logger.LogInformation(
                    "FaceFrame #{Count}: written={Written} tracked={IsTracked} yaw={Yaw:F1} pitch={Pitch:F1}",
                    count, written, face.IsTracked, face.HeadRotation.Y, face.HeadRotation.X);
        };
        _sensor.AudioFrameReady += audio => _audio.AudioInput.TryWrite(audio);
        _sensor.ColorFrameReady += color =>
        {
            _vision.LatestColor = color;
            ColorFrameReady?.Invoke(color);
        };
        _sensor.DepthFrameReady += depth => DepthFrameReady?.Invoke(depth);
        _sensor.InfraredFrameReady += ir => InfraredFrameReady?.Invoke(ir);
        long skelFrameCount = 0;
        _sensor.SkeletonFrameReady += skel =>
        {
            _latestSkeleton = skel;
            SkeletonFrameReady?.Invoke(skel);
            var count = Interlocked.Increment(ref skelFrameCount);
            if (count <= 5 || count % 300 == 0)
                _logger.LogInformation(
                    "SkeletonFrame #{Count}: id={Id} state={State} joints={Joints} pos=({X:F2},{Y:F2},{Z:F2})",
                    count, skel.TrackingId, skel.State, skel.Joints?.Count ?? 0,
                    skel.Position.X, skel.Position.Y, skel.Position.Z);
        };

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

            // Unwire the failed sensor
            _sensor = _fallbackSensor;
            await _sensor.InitializeAsync(SensorStreamType.All, _cts.Token);

            // Re-wire events on fallback (reuse same counters)
            _sensor.FaceFrameReady += face =>
            {
                _latestFace = face;
                bool written = _vision.FaceInput.TryWrite(face);
                var count = Interlocked.Increment(ref faceFrameCount);
                if (count <= 5 || count % 300 == 0)
                    _logger.LogInformation(
                        "FaceFrame #{Count} (fallback): written={Written} tracked={IsTracked}",
                        count, written, face.IsTracked);
            };
            _sensor.AudioFrameReady += audio => _audio.AudioInput.TryWrite(audio);
            _sensor.ColorFrameReady += color =>
            {
                _vision.LatestColor = color;
                ColorFrameReady?.Invoke(color);
            };
            _sensor.DepthFrameReady += depth => DepthFrameReady?.Invoke(depth);
            _sensor.InfraredFrameReady += ir => InfraredFrameReady?.Invoke(ir);
            _sensor.SkeletonFrameReady += skel =>
            {
                _latestSkeleton = skel;
                SkeletonFrameReady?.Invoke(skel);
            };

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

        await _sensor.StopAsync(ct);
        await _vision.StopAsync();
        await _audio.StopAsync();

        _cts?.Dispose();
        _logger.LogInformation("HCEP pipeline stopped");
    }

    // ── Snapshot Loop ──────────────────────────────────────────

    /// <summary>
    /// Consumes HcepReadings from the vision pipeline in a background task,
    /// storing the latest reading for the snapshot timer to pick up.
    /// </summary>
    private async Task ConsumeHcepReadingsAsync(CancellationToken ct)
    {
        long hcepFrameCount = 0;
        try
        {
            await foreach (var reading in _vision.HcepOutput.ReadAllAsync(ct))
            {
                _latestHcep = reading;
                _hcepFpsCounter.Tick();
                hcepFrameCount++;

                if (hcepFrameCount <= 5 || hcepFrameCount % 150 == 0)
                    _logger.LogInformation(
                        "HCEP reading #{Frame}: mode={Mode} region={Region} conf={Conf:F3} hcepFps={Fps:F1}",
                        hcepFrameCount, reading.Mode, reading.Region, reading.Confidence, _hcepFpsCounter.Fps);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HCEP consumer error");
        }
    }

    /// <summary>
    /// Timer-driven snapshot loop at ~10 Hz. Always produces snapshots
    /// so the main window updates even when no HCEP data is available.
    /// This prevents the UI from staying blank while waiting for face tracking.
    /// </summary>
    private async Task RunSnapshotLoopAsync(CancellationToken ct)
    {
        long frameNumber = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); // ~10 Hz

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _fpsCounter.Tick();
                frameNumber++;

                // ── Phase 6: advance micro-saccade timer (loop runs at ~10 Hz)
                _saccade.Update(0.1);

                var hcep = _latestHcep;
                var latestSkel = _latestSkeleton;
                var latestFace = _latestFace;
                var recognition = _vision.LatestRecognition;

                // Build TrackedPerson from whatever data is available
                TrackedPerson? person = null;
                if (hcep is not null || latestFace is not null || latestSkel is not null)
                {
                    var headPos = hcep?.GazeOrigin
                            ?? (latestSkel?.Joints?.ContainsKey(3) == true ? latestSkel.Joints[3] : default);

                    // ── Eye Location Computation ──────────────────
                    // Derive 3D camera-space positions for each eye from head position
                    // and inter-ocular offset (~32mm half-distance), applying yaw rotation.
                    var (leftEye, rightEye) = ComputeEyePositions(headPos, latestFace);

                    // ── Phase 6: calibrated gaze + IK target ──────────
                    Vector3 calibratedGaze = Vector3.Zero;
                    Vector3? avatarIkTarget = null;

                    if (hcep is not null)
                    {
                        // Dynamic parallax correction using live user depth (mm)
                        float userDepthMm = hcep.GazeOrigin.Z * 1000f; // metres → mm
                        calibratedGaze = _calibration.ApplyCalibration(
                            hcep.GazeDirection, userDepthMm);
                    }

                    if (latestFace is not null && latestFace.IsTracked)
                    {
                        avatarIkTarget = _saccade.GetFocusPoint3D(latestFace);
                    }

                    // ── Phase 3: World-Space Gaze Vector → Avatar ─────────
                    // High-fidelity path : IsTracked = true  → precise eye-socket position.
                    // Bounding-box fallback: IsTracked = false → HeadTranslation centre.
                    if (latestFace is not null && _avatarEyeProvider is not null && _avatarScreenWidthPx > 0)
                    {
                        Vector3 userEyeM;
                        if (latestFace.IsTracked && avatarIkTarget.HasValue)
                        {
                            // avatarIkTarget is already in Camera Space metres (from GetFocusPoint3D).
                            userEyeM = avatarIkTarget.Value;
                        }
                        else
                        {
                            // HeadTranslation is in Camera Space mm → convert to metres.
                            // Reset EMA so stale high-fidelity values don't bleed into fallback.
                            _gazeEngine.Reset();
                            userEyeM = latestFace.HeadTranslation / 1000f;
                        }

                        var (leftPx, rightPx) = _avatarEyeProvider();

                        // Mirror the saccade: if fixating user's LEFT eye, use Avatar LEFT eye socket.
                        Vector2 avatarEyePx = _saccade.CurrentTarget == EyeSocketTarget.Left
                            ? leftPx : rightPx;

                        var cal = _calibration; // thread-safe snapshot
                        Vector3 avatarEyeWorldMm = GazeVectorEngine.AvatarEyeScreenToWorldMm(
                            avatarEyePx,
                            new Vector2(_avatarScreenWidthPx, _avatarScreenHeightPx),
                            new Vector2(ScreenWidthMm, ScreenHeightMm),
                            cal.KinectOffsetFromScreenCentreMm);

                        // userEyeM is in Camera Space metres — GazeVectorEngine converts to mm internally.
                        var (pitch, yaw) = _gazeEngine.Compute(userEyeM, avatarEyeWorldMm);

                        GazeVectorReady?.Invoke(pitch, yaw);
                    }

                    person = new TrackedPerson
                    {
                        TrackingId = hcep?.PersonId ?? latestSkel?.TrackingId ?? 0,
                        State = hcep is not null ? TrackingState.Tracked : TrackingState.PositionOnly,
                        LatestHcep = hcep,
                        IdentityName = recognition?.IdentityName,
                        FaceEmbedding = recognition?.Embedding,
                        IdentityConfidence = recognition?.Similarity ?? 0f,
                        HeadPosition = headPos,
                        LeftEyePosition = leftEye,
                        RightEyePosition = rightEye,
                        LastSeen = hcep?.Timestamp ?? DateTimeOffset.UtcNow,
                        JointPositions = latestSkel?.Joints,
                        JointStates = latestSkel?.JointStates,
                        DistanceM = latestSkel?.Position.Z ?? hcep?.GazeOrigin.Z ?? 0,
                        Face = latestFace,
                        CalibratedGazeDirection = calibratedGaze,
                        AvatarIkTarget = avatarIkTarget,
                    };

                    // Knowledge Store integration (M1.2)
                    try { _personKnowledge.RecordSighting(person); }
                    catch (Exception ex)
                    {
                        if (frameNumber <= 3)
                            _logger.LogWarning(ex, "PersonKnowledge.RecordSighting failed (frame {Frame})", frameNumber);
                    }

                    // Agentic Tool State update (M1.3)
                    if (hcep is not null)
                        _toolExecutor.UpdateState(hcep, person);
                }

                var snapshot = new SceneSnapshot
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    FrameNumber = frameNumber,
                    Persons = person is not null
                        ? ImmutableArray.Create(person)
                        : ImmutableArray<TrackedPerson>.Empty,
                    PrimaryPersonIndex = person is not null ? 0 : -1,
                    LatestSpeech = _latestSpeech,
                    ActiveStreams = SensorStreamType.All,
                    PipelineLatency = hcep is not null
                        ? DateTimeOffset.UtcNow - hcep.Timestamp
                        : TimeSpan.Zero,
                };

                _latestSnapshot = snapshot;
                _telemetry.RecordGauge("pipeline.fps", _hcepFpsCounter.Fps);
                if (hcep is not null)
                    _telemetry.RecordTiming("pipeline.latency_ms", snapshot.PipelineLatency.TotalMilliseconds);

                // ── Auto-fallback: full-body → seated if no detection ──
                if (person is not null)
                {
                    _lastPersonSeenAt = DateTimeOffset.UtcNow;
                    // Person detected — stay in current mode.
                }
                else if (!_sensor.SeatedMode
                         && !_autoFellBackToSeated
                         && _lastPersonSeenAt != DateTimeOffset.MinValue
                         && (DateTimeOffset.UtcNow - _lastPersonSeenAt).TotalSeconds > AutoFallbackSeconds)
                {
                    _sensor.SeatedMode = true;
                    _autoFellBackToSeated = true;
                    _logger.LogWarning(
                        "No person detected for {Sec}s in full-body mode — auto-switching to SEATED mode",
                        AutoFallbackSeconds);
                    SeatedModeChanged?.Invoke(true);
                }
                else if (!_sensor.SeatedMode
                         && !_autoFellBackToSeated
                         && _lastPersonSeenAt == DateTimeOffset.MinValue
                         && frameNumber >= 50)
                {
                    _sensor.SeatedMode = true;
                    _autoFellBackToSeated = true;
                    _logger.LogWarning(
                        "No person detected after {Frames} snapshots — auto-switching to SEATED mode",
                        frameNumber);
                    SeatedModeChanged?.Invoke(true);
                }

                SnapshotReady?.Invoke(snapshot);

                if (frameNumber <= 5 || frameNumber % 300 == 0)
                    _logger.LogInformation(
                        "Snapshot #{Frame}: persons={Persons} hcepMode={Mode} hcepFps={HcepFps:F1} hasFace={HasFace} hasSkel={HasSkel}",
                        frameNumber, snapshot.Persons.Length,
                        hcep?.Mode.ToString() ?? "None",
                        _hcepFpsCounter.Fps,
                        latestFace is not null,
                        latestSkel is not null);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot loop error");
        }
    }

    /// <summary>
    /// Reads speech results from the audio pipeline, injects them into the
    /// vision pipeline (for HCEP mode analysis), fires SpeechReady,
    /// and triggers an LLM response using current HCEP context.
    /// </summary>
    private async Task RunSpeechLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var result in _audio.SpeechOutput.ReadAllAsync(ct))
            {
                _latestSpeech = result;
                _vision.LatestSpeech = result;

                _telemetry.Increment("speech.results");
                _logger.LogDebug("Speech: {Text}", result.Text);

                SpeechReady?.Invoke(result);

                // ── Knowledge: record utterance (M1.2) ────────
                var person = _latestSnapshot?.PrimaryPerson;
                string personName = person?.IdentityName ?? $"Person-{person?.TrackingId ?? 0}";
                try
                {
                    _personKnowledge.RecordUtterance(personName, result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PersonKnowledge.RecordUtterance failed");
                }

                // ── LLM: auto-respond to speech (M1.3) ───────
                // Fire-and-forget the LLM call on ThreadPool to avoid
                // blocking the audio channel reader
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var hcep = _latestSnapshot?.PrimaryPerson?.LatestHcep;
                        var exchange = await _llmEngine.PromptAsync(result.Text, hcep, ct: ct);

                        _logger.LogInformation(
                            "LLM response ({Model}, {Latency:F0}ms): {Response}",
                            exchange.ModelId,
                            exchange.Latency.TotalMilliseconds,
                            exchange.Response?[..Math.Min(exchange.Response.Length, 80)]);

                        _telemetry.RecordTiming("llm.latency_ms", exchange.Latency.TotalMilliseconds);
                        _telemetry.Increment(exchange.IsLocal ? "llm.local_calls" : "llm.cloud_calls");

                        // Record exchange in knowledge store
                        try
                        {
                            _personKnowledge.RecordExchange(personName, exchange);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "PersonKnowledge.RecordExchange failed");
                        }

                        LlmResponseReady?.Invoke(exchange);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "LLM auto-response failed for speech: {Text}",
                            result.Text[..Math.Min(result.Text.Length, 50)]);
                    }
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech loop error");
        }
    }

    // ── Eye Location Helpers ───────────────────────────────────

    /// <summary>
    /// Average adult inter-ocular half-distance in meters (~32mm from midline to each eye).
    /// Total inter-pupillary distance ~63mm.
    /// </summary>
    private const float EyeHalfDistanceM = 0.032f;

    /// <summary>
    /// Computes 3D camera-space positions of both eyes from head position
    /// and current face tracking data. Applies yaw rotation so eye positions
    /// track correctly when the head turns.
    /// </summary>
    private static (Vector3 Left, Vector3 Right) ComputeEyePositions(
        Vector3 headPos, FaceFrame? face)
    {
        if (headPos == default) return (default, default);

        // Yaw angle from face tracking (degrees → radians)
        float yawRad = (face?.HeadRotation.Y ?? 0f) * MathF.PI / 180f;
        float cosY = MathF.Cos(yawRad);
        float sinY = MathF.Sin(yawRad);

        // Lateral offset rotated by yaw (X-Z plane)
        // Left eye: -X in head space
        var leftOffset = new Vector3(
            -EyeHalfDistanceM * cosY,
            0,
            EyeHalfDistanceM * sinY);

        // Right eye: +X in head space
        var rightOffset = new Vector3(
            EyeHalfDistanceM * cosY,
            0,
            -EyeHalfDistanceM * sinY);

        return (headPos + leftOffset, headPos + rightOffset);
    }
}
