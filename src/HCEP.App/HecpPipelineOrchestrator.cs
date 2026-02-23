// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.Immutable;
using HCEP.Audio;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Intelligence;
using HCEP.Kinect;
using HCEP.Knowledge;
using HCEP.Telemetry;
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

    public HCEPPipelineOrchestrator(
        ISensorSource sensor,
        SimulatedSensorSource fallbackSensor,
        VisionPipeline vision,
        AudioPipeline audio,
        PersonKnowledgeManager personKnowledge,
        AgenticToolExecutor toolExecutor,
        ILlmEngine llmEngine,
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
        _telemetry = telemetry;
        _logger = logger;
    }

    // ── IPipelineOrchestrator ──────────────────────────────────

    public bool IsRunning => _isRunning;
    public SceneSnapshot? LatestSnapshot => _latestSnapshot;
    public double CurrentFps => _hcepFpsCounter.Fps;
    public event Action<SceneSnapshot>? SnapshotReady;
    public event Action<SpeechResult>? SpeechReady;
    public event Action<ColorFrame>? ColorFrameReady;
    public event Action<DepthFrame>? DepthFrameReady;
    public event Action<ColorFrame>? InfraredFrameReady;
    public event Action<SkeletonFrame>? SkeletonFrameReady;
    public event Action<LlmExchange>? LlmResponseReady;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting HCEP pipeline...");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

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
        _sensor.ColorFrameReady += color => ColorFrameReady?.Invoke(color);
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
            _sensor.ColorFrameReady += color => ColorFrameReady?.Invoke(color);
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

                var hcep = _latestHcep;
                var latestSkel = _latestSkeleton;
                var latestFace = _latestFace;

                // Build TrackedPerson from whatever data is available
                TrackedPerson? person = null;
                if (hcep is not null || latestFace is not null || latestSkel is not null)
                {
                    person = new TrackedPerson
                    {
                        TrackingId = hcep?.PersonId ?? latestSkel?.TrackingId ?? 0,
                        State = hcep is not null ? TrackingState.Tracked : TrackingState.PositionOnly,
                        LatestHcep = hcep,
                        HeadPosition = hcep?.GazeOrigin
                            ?? (latestSkel?.Joints?.ContainsKey(3) == true ? latestSkel.Joints[3] : default),
                        LastSeen = hcep?.Timestamp ?? DateTimeOffset.UtcNow,
                        JointPositions = latestSkel?.Joints,
                        JointStates = latestSkel?.JointStates,
                        DistanceM = latestSkel?.Position.Z ?? hcep?.GazeOrigin.Z ?? 0,
                        Face = latestFace,
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
}
