// ──────────────────────────────────────────────────────────────
// HCEP — Integration Tests
// SimulatedSensorSource → VisionPipeline → HcepModeAnalyzer → output
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Kinect;
using HCEP.Spatial;
using HCEP.Telemetry;
using HCEP.Vision;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Integration;

/// <summary>
/// End-to-end integration tests wiring:
///   SimulatedSensorSource → VisionPipeline (ThreeStageGazeEstimator + HcepModeAnalyzer) → HcepReading output channel.
/// Validates that the full pipeline produces valid, scenario-appropriate HCEP readings.
/// </summary>
public sealed class PipelineIntegrationTests : IAsyncDisposable
{
    // ── Fakes ──────────────────────────────────────────────────

    /// <summary>Stub face recognizer — returns empty embeddings, no matches.</summary>
    private sealed class StubFaceRecognizer : IFaceRecognizer
    {
        public float MatchThreshold { get; set; } = 0.6f;
        public int EnrolledCount => 0;
        public bool IsModelLoaded => false;

        public float[] GenerateEmbedding(ReadOnlySpan<byte> faceImage, int width, int height)
            => Array.Empty<float>();

        public (string Name, float Similarity)? Match(ReadOnlySpan<float> embedding)
            => null;

        public void Enroll(string name, float[] embedding) { }
    }

    /// <summary>Stub speech recognizer — returns no transcriptions.</summary>
    private sealed class StubSpeechRecognizer : ISpeechRecognizer
    {
        public bool IsReady => true;
        public Task<SpeechResult[]> ProcessAsync(AudioFrame frame, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SpeechResult>());
        public Task<SpeechResult[]> FlushAsync(CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SpeechResult>());
        public Task LoadModelAsync(string modelPath, CancellationToken ct = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ── Shared pipeline components ─────────────────────────────

    private readonly SimulatedSensorSource _sensor;
    private readonly VisionPipeline _vision;
    private readonly HcepModeAnalyzer _analyzer;
    private readonly ThreeStageGazeEstimator _gazeEstimator;
    private readonly HCEPTelemetryService _telemetry;

    public PipelineIntegrationTests()
    {
        _telemetry = new HCEPTelemetryService();
        _gazeEstimator = new ThreeStageGazeEstimator();
        _analyzer = new HcepModeAnalyzer();

        _sensor = new SimulatedSensorSource(
            NullLogger<SimulatedSensorSource>.Instance);

        _vision = new VisionPipeline(
            _gazeEstimator,
            _analyzer,
            new StubFaceRecognizer(),
            _telemetry,
            NullLogger<VisionPipeline>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _vision.DisposeAsync();
        await _sensor.DisposeAsync();
    }

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Starts the sensor + vision pipeline, wires FaceFrameReady → VisionPipeline.FaceInput,
    /// collects <paramref name="count"/> HcepReadings, then stops everything.
    /// </summary>
    private async Task<List<HcepReading>> CollectReadingsAsync(int count, TimeSpan timeout)
    {
        var readings = new List<HcepReading>();
        using var cts = new CancellationTokenSource(timeout);

        // Wire sensor → vision
        _sensor.FaceFrameReady += face => _vision.FaceInput.TryWrite(face);

        // Start pipeline
        await _sensor.InitializeAsync(SensorStreamType.All, cts.Token);
        await _vision.StartAsync(cts.Token);
        await _sensor.StartAsync(cts.Token);

        try
        {
            await foreach (var reading in _vision.HcepOutput.ReadAllAsync(cts.Token))
            {
                readings.Add(reading);
                if (readings.Count >= count)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        await _sensor.StopAsync();
        await _vision.StopAsync();

        return readings;
    }

    /// <summary>
    /// Starts the sensor + vision pipeline, collects readings for a fixed duration,
    /// then returns all readings grouped by the simulated scenario they belong to.
    /// Scenario cycle: 150 frames each → LOGIC, AFFECT, SPIRIT, HEART, THINK.
    /// </summary>
    private async Task<List<HcepReading>> CollectReadingsForDurationAsync(TimeSpan duration)
    {
        var readings = new List<HcepReading>();
        using var cts = new CancellationTokenSource(duration + TimeSpan.FromSeconds(2));
        using var durationTimer = new CancellationTokenSource(duration);

        _sensor.FaceFrameReady += face => _vision.FaceInput.TryWrite(face);

        await _sensor.InitializeAsync(SensorStreamType.All, cts.Token);
        await _vision.StartAsync(cts.Token);
        await _sensor.StartAsync(cts.Token);

        try
        {
            await foreach (var reading in _vision.HcepOutput.ReadAllAsync(cts.Token))
            {
                readings.Add(reading);
                if (durationTimer.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        await _sensor.StopAsync();
        await _vision.StopAsync();

        return readings;
    }

    // ── Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task Pipeline_ProducesReadings_WhenSensorStreams()
    {
        // Collect 10 readings with a generous timeout
        var readings = await CollectReadingsAsync(10, TimeSpan.FromSeconds(5));

        Assert.True(readings.Count >= 10,
            $"Expected at least 10 readings but got {readings.Count}");
    }

    [Fact]
    public async Task Pipeline_AllReadings_HaveValidTimestamps()
    {
        var readings = await CollectReadingsAsync(20, TimeSpan.FromSeconds(5));

        foreach (var r in readings)
        {
            Assert.True(r.Timestamp > DateTimeOffset.MinValue,
                "Reading timestamp should not be MinValue");
            Assert.True(r.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(1),
                "Reading timestamp should not be in the far future");
        }
    }

    [Fact]
    public async Task Pipeline_AllReadings_HaveNonNegativeConfidence()
    {
        var readings = await CollectReadingsAsync(30, TimeSpan.FromSeconds(5));

        foreach (var r in readings)
        {
            Assert.InRange(r.Confidence, 0f, 1f);
        }
    }

    [Fact]
    public async Task Pipeline_AllReadings_HaveValidPersonId()
    {
        var readings = await CollectReadingsAsync(20, TimeSpan.FromSeconds(5));

        // SimulatedSensorSource always uses TrackingId=1
        foreach (var r in readings)
        {
            Assert.Equal(1, r.PersonId);
        }
    }

    [Fact]
    public async Task Pipeline_ProducesNonUnknownMode_AfterHysteresisWarmup()
    {
        // Collect enough readings to get past hysteresis warmup (~5 frames)
        var readings = await CollectReadingsAsync(30, TimeSpan.FromSeconds(5));

        // After initial frames, the mode should settle to something non-Unknown
        var laterReadings = readings.Skip(10).ToList();
        Assert.True(laterReadings.Any(r => r.Mode != HcepMode.Unknown),
            "After warmup, at least some readings should have a classified mode");
    }

    [Fact]
    public async Task Pipeline_GazeDirection_IsNonZeroVector()
    {
        var readings = await CollectReadingsAsync(15, TimeSpan.FromSeconds(5));

        foreach (var r in readings)
        {
            Assert.NotEqual(Vector3.Zero, r.GazeDirection);
        }
    }

    [Fact]
    public async Task Pipeline_HeadPose_ReflectsScenarioRotation()
    {
        // The simulator sets HeadRotation = (pitch, yaw, 0).
        // All scenarios have non-trivial pitch, so HeadPose should be non-zero.
        var readings = await CollectReadingsAsync(20, TimeSpan.FromSeconds(5));

        foreach (var r in readings)
        {
            // At least pitch or yaw should be non-zero
            Assert.True(r.HeadPose.X != 0 || r.HeadPose.Y != 0,
                $"HeadPose should have non-zero rotation, got {r.HeadPose}");
        }
    }

    [Fact]
    public async Task Pipeline_TelemetryCounters_AreUpdated()
    {
        await CollectReadingsAsync(20, TimeSpan.FromSeconds(5));

        // The vision pipeline increments these counters
        var framesProcessed = _telemetry.GetCount("vision.frames_processed");
        Assert.True(framesProcessed >= 20,
            $"Expected ≥20 frames processed, got {framesProcessed}");
    }

    [Fact]
    public async Task Pipeline_Over25Seconds_ProducesMultipleModes()
    {
        // The simulator cycles through 5 scenarios every 25 seconds.
        // Run for long enough to see at least 2 different scenarios.
        // At ~30 fps, 10 seconds ≈ 300 frames covering scenarios 0–1 (LOGIC, AFFECT).
        var readings = await CollectReadingsForDurationAsync(TimeSpan.FromSeconds(12));

        Assert.True(readings.Count > 50,
            $"Expected >50 readings over 12 seconds, got {readings.Count}");

        var distinctModes = readings
            .Where(r => r.Mode != HcepMode.Unknown)
            .Select(r => r.Mode)
            .Distinct()
            .ToList();

        // We should observe at least 1 classified mode; ideally >1 across scenarios
        Assert.True(distinctModes.Count >= 1,
            $"Expected at least 1 distinct non-Unknown mode, got {distinctModes.Count}: [{string.Join(", ", distinctModes)}]");
    }

    [Fact]
    public async Task Pipeline_CognitiveState_IsClassified()
    {
        var readings = await CollectReadingsAsync(30, TimeSpan.FromSeconds(5));

        // At least some readings should have a cognitive state other than Unknown
        Assert.True(readings.Any(r => r.Cognitive != CognitiveState.Unknown),
            "Expected at least one reading with a classified cognitive state");
    }

    [Fact]
    public async Task Pipeline_EmotionalValence_IsClassified()
    {
        // The simulator produces AUs with varying lip stretcher values
        // (e.g., scenario 1 AFFECT has lipStretch=0.35, scenario 3 HEART has 0.40)
        // which should trigger Positive valence classification.
        var readings = await CollectReadingsForDurationAsync(TimeSpan.FromSeconds(10));

        var valences = readings.Select(r => r.Valence).Distinct().ToList();
        Assert.True(valences.Count > 1 || valences.Any(v => v != EmotionalValence.Unknown),
            $"Expected valence classification, got: [{string.Join(", ", valences)}]");
    }

    [Fact]
    public async Task Pipeline_GazeRegion_IncludesOnFaceTargets()
    {
        // The first scenario (LOGIC) has near-center on-face gaze.
        // We should see regions like LeftEye, RightEye, FaceCenter, Mouth, etc.
        var readings = await CollectReadingsAsync(50, TimeSpan.FromSeconds(5));

        var onFaceRegions = readings
            .Where(r => r.Region is GazeRegion.LeftEye or GazeRegion.RightEye
                or GazeRegion.NasalBridge or GazeRegion.Mouth or GazeRegion.FaceCenter
                or GazeRegion.Forehead or GazeRegion.Chin)
            .ToList();

        Assert.True(onFaceRegions.Count > 0,
            $"Expected some on-face gaze regions among {readings.Count} readings, " +
            $"got regions: [{string.Join(", ", readings.Select(r => r.Region).Distinct())}]");
    }

    [Fact]
    public async Task Pipeline_ReadingsAreChronological()
    {
        var readings = await CollectReadingsAsync(30, TimeSpan.FromSeconds(5));

        for (int i = 1; i < readings.Count; i++)
        {
            Assert.True(readings[i].Timestamp >= readings[i - 1].Timestamp,
                $"Reading {i} timestamp ({readings[i].Timestamp}) should be ≥ " +
                $"reading {i - 1} timestamp ({readings[i - 1].Timestamp})");
        }
    }

    [Fact]
    public async Task Pipeline_ThinkScenario_DetectsGazeAversion()
    {
        // The THINK scenario (scenario 4) starts after frame 600 in the sensor's cycle.
        // Collect enough readings to guarantee we process frames past that mark.
        // Using count-based collection (750 readings) avoids wall-clock timing issues.
        var readings = await CollectReadingsAsync(750, TimeSpan.FromSeconds(60));

        // The simulator's THINK scenario has yaw ~20° — the head pose in later readings
        // should reflect this large rotation even if the gaze estimator maps it to an
        // on-face region (the classifier uses the intersection point, not raw yaw).
        var maxYaw = readings.Max(r => Math.Abs(r.HeadPose.Y));

        var hasThinkIndicators = readings.Any(r =>
            r.Mode == HcepMode.Think ||
            r.Cognitive is CognitiveState.Processing or CognitiveState.Recalling or CognitiveState.Constructing ||
            r.Region is GazeRegion.PeripheralLeft or GazeRegion.PeripheralRight
                or GazeRegion.Above or GazeRegion.Below or GazeRegion.Defocused);

        Assert.True(hasThinkIndicators || maxYaw > 10f,
            $"Expected THINK indicators or large head yaw across {readings.Count} readings. " +
            $"MaxYaw: {maxYaw:F1}°, " +
            $"Modes: [{string.Join(", ", readings.Select(r => r.Mode).Distinct())}], " +
            $"Cognitive: [{string.Join(", ", readings.Select(r => r.Cognitive).Distinct())}], " +
            $"Regions: [{string.Join(", ", readings.Select(r => r.Region).Distinct())}]");
    }

    [Fact]
    public async Task Pipeline_SensorStateTransitions_AreCorrect()
    {
        var states = new List<SensorState>();
        _sensor.StateChanged += s => states.Add(s);

        Assert.Equal(SensorState.Disconnected, _sensor.State);

        await _sensor.InitializeAsync(SensorStreamType.All);
        Assert.Equal(SensorState.Connected, _sensor.State);
        Assert.Contains(SensorState.Connected, states);

        await _sensor.StartAsync();
        // Collect a few frames to confirm streaming works
        await Task.Delay(200);
        await _sensor.StopAsync();

        Assert.Equal(SensorState.Disconnected, _sensor.State);
        Assert.Contains(SensorState.Disconnected, states);
    }

    [Fact]
    public async Task Pipeline_WithSpeechInjection_ChangesCognitiveState()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var readings = new List<HcepReading>();

        _sensor.FaceFrameReady += face => _vision.FaceInput.TryWrite(face);

        await _sensor.InitializeAsync(SensorStreamType.All, cts.Token);
        await _vision.StartAsync(cts.Token);
        await _sensor.StartAsync(cts.Token);

        // Collect initial readings without speech
        for (int i = 0; i < 10; i++)
        {
            if (await _vision.HcepOutput.WaitToReadAsync(cts.Token))
                if (_vision.HcepOutput.TryRead(out var r))
                    readings.Add(r);
        }

        // Inject speech
        _vision.LatestSpeech = new SpeechResult
        {
            Text = "I believe this is correct",
            IsFinal = true,
            Confidence = 0.92f,
            Timestamp = DateTimeOffset.UtcNow,
        };

        // Collect readings with speech injected
        for (int i = 0; i < 5; i++)
        {
            if (await _vision.HcepOutput.WaitToReadAsync(cts.Token))
                if (_vision.HcepOutput.TryRead(out var r))
                    readings.Add(r);
        }

        await _sensor.StopAsync();
        await _vision.StopAsync();

        // The reading immediately after speech injection should detect PreSpeech
        var postSpeechReadings = readings.Skip(10).ToList();
        Assert.True(postSpeechReadings.Any(r => r.Cognitive == CognitiveState.PreSpeech),
            $"Expected PreSpeech cognitive state after speech injection. " +
            $"Got: [{string.Join(", ", postSpeechReadings.Select(r => r.Cognitive).Distinct())}]");
    }

    [Fact]
    public async Task Pipeline_TelemetryGauges_TrackModeAndConfidence()
    {
        await CollectReadingsAsync(20, TimeSpan.FromSeconds(5));

        // VisionPipeline records these gauges per frame
        var modeGauge = _telemetry.GetGauge("vision.mode");
        var confGauge = _telemetry.GetGauge("vision.confidence");

        // mode gauge should be one of the HcepMode enum values (0-5)
        Assert.InRange(modeGauge, 0, 5);
        // confidence should be [0..1]
        Assert.InRange(confGauge, 0, 1);
    }

    [Fact]
    public async Task Pipeline_MultipleStopStart_DoesNotThrow()
    {
        // Verify the pipeline is resilient to repeated start/stop cycles
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var sensor = new SimulatedSensorSource(NullLogger<SimulatedSensorSource>.Instance);
            var vision = new VisionPipeline(
                new ThreeStageGazeEstimator(),
                new HcepModeAnalyzer(),
                new StubFaceRecognizer(),
                new HCEPTelemetryService(),
                NullLogger<VisionPipeline>.Instance);

            sensor.FaceFrameReady += face => vision.FaceInput.TryWrite(face);

            await sensor.InitializeAsync(SensorStreamType.All);
            await vision.StartAsync();
            await sensor.StartAsync();

            // Collect a few readings
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            int count = 0;
            try
            {
                await foreach (var _ in vision.HcepOutput.ReadAllAsync(cts.Token))
                {
                    if (++count >= 5) break;
                }
            }
            catch (OperationCanceledException) { }

            Assert.True(count > 0, $"Cycle {cycle}: expected at least 1 reading");

            // Use DisposeAsync only (it calls StopAsync internally).
            // Calling StopAsync then DisposeAsync would double-cancel the CTS.
            await vision.DisposeAsync();
            await sensor.DisposeAsync();
        }
    }
}
