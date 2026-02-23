// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Threading.Channels;
using HCEP.Core.Channels;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Vision;

/// <summary>
/// Vision processing pipeline — consumes face/skeleton frames from the sensor
/// and produces <see cref="HcepReading"/> results via a channel.
/// Orchestrates gaze estimation, face recognition, and HCEP mode analysis.
/// </summary>
public sealed class VisionPipeline : IAsyncDisposable
{
    private readonly IGazeEstimator _gazeEstimator;
    private readonly IHcepAnalyzer _hcepAnalyzer;
    private readonly IFaceRecognizer _faceRecognizer;
    private readonly ILogger<VisionPipeline> _logger;
    private readonly ITelemetryService _telemetry;

    private readonly Channel<FaceFrame> _faceInput = HCEPChannels.CreateRealTime<FaceFrame>();
    private readonly Channel<HcepReading> _hcepOutput = HCEPChannels.CreateReliable<HcepReading>();

    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private GazeEstimate? _previousGaze;
    private HcepReading? _previousReading;

    /// <summary>
    /// Latest speech result injected by the orchestrator from the audio pipeline.
    /// Set externally so the HCEP analyzer can incorporate speech activity.
    /// </summary>
    public volatile SpeechResult? LatestSpeech;

    public VisionPipeline(
        IGazeEstimator gazeEstimator,
        IHcepAnalyzer hcepAnalyzer,
        IFaceRecognizer faceRecognizer,
        ITelemetryService telemetry,
        ILogger<VisionPipeline> logger)
    {
        _gazeEstimator = gazeEstimator;
        _hcepAnalyzer = hcepAnalyzer;
        _faceRecognizer = faceRecognizer;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>Writer for pushing face frames into the pipeline.</summary>
    public ChannelWriter<FaceFrame> FaceInput => _faceInput.Writer;

    /// <summary>Reader for consuming HCEP readings from the pipeline.</summary>
    public ChannelReader<HcepReading> HcepOutput => _hcepOutput.Reader;

    /// <summary>Starts the background processing loop.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processingTask = ProcessAsync(_cts.Token);
        _logger.LogInformation("Vision pipeline started");
        return Task.CompletedTask;
    }

    /// <summary>Stops the background processing loop.</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _faceInput.Writer.TryComplete();

        if (_processingTask is not null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
        _logger.LogInformation("Vision pipeline stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private long _frameCount;

    private async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var face in _faceInput.Reader.ReadAllAsync(ct))
        {
            try
            {
                _frameCount++;
                if (_frameCount <= 3 || _frameCount % 150 == 0)
                    _logger.LogInformation("VisionPipeline frame #{Frame}: yaw={Yaw:F2} pitch={Pitch:F2} AUs={AuCount}",
                        _frameCount, face.HeadRotation.Y, face.HeadRotation.X, face.ActionUnits.Length);

                using var timer = _telemetry.StartTimer("vision.frame_ms");

                // Stage 1-3: Gaze estimation
                var gaze = _gazeEstimator.Estimate(face, _previousGaze);
                _previousGaze = gaze;

                if (_frameCount <= 3 || _frameCount % 150 == 0)
                    _logger.LogInformation("  Gaze: region={Region} conf={Conf:F3} dir=({X:F3},{Y:F3},{Z:F3})",
                        gaze.ClassifiedRegion, gaze.Confidence, gaze.HybridDirection.X, gaze.HybridDirection.Y, gaze.HybridDirection.Z);

                // HCEP mode analysis (fuse latest speech from audio pipeline)
                var speech = LatestSpeech;
                var reading = _hcepAnalyzer.Analyze(gaze, face, speech, _previousReading);
                _previousReading = reading;

                if (_frameCount <= 3 || _frameCount % 150 == 0)
                    _logger.LogInformation("  HCEP: mode={Mode} region={Region} conf={Conf:F3} cognitive={Cog}",
                        reading.Mode, reading.Region, reading.Confidence, reading.Cognitive);

                // Clear speech after consumption to avoid stale reuse
                if (speech is not null)
                    LatestSpeech = null;

                // Publish result
                await _hcepOutput.Writer.WriteAsync(reading, ct);

                _telemetry.Increment("vision.frames_processed");
                _telemetry.RecordGauge("vision.mode", (double)reading.Mode);
                _telemetry.RecordGauge("vision.confidence", reading.Confidence);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error processing face frame");
                _telemetry.Increment("vision.errors");
            }
        }
    }
}
