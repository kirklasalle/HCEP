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
    private SpeechResult? _latestSpeech;
    public SpeechResult? LatestSpeech
    {
        get => Interlocked.CompareExchange(ref _latestSpeech, null!, null!);
        set => Interlocked.Exchange(ref _latestSpeech, value);
    }

    /// <summary>
    /// Latest color frame injected by the orchestrator for face crop extraction.
    /// Set externally; consumed by the recognition loop.
    /// </summary>
    private ColorFrame? _latestColor;
    public ColorFrame? LatestColor
    {
        get => Interlocked.CompareExchange(ref _latestColor, null!, null!);
        set => Interlocked.Exchange(ref _latestColor, value);
    }

    /// <summary>
    /// Latest face recognition result (updated ~1 Hz when model is loaded).
    /// Read by the orchestrator to populate TrackedPerson identity fields.
    /// </summary>
    private FaceRecognitionResult? _latestRecognition;
    public FaceRecognitionResult? LatestRecognition
    {
        get => Interlocked.CompareExchange(ref _latestRecognition, null!, null!);
        set => Interlocked.Exchange(ref _latestRecognition, value);
    }

    /// <summary>
    /// Enroll the next detected face under the given name.
    /// Set externally by the UI; cleared after enrollment.
    /// </summary>
    private string? _pendingEnrollmentName;
    public string? PendingEnrollmentName
    {
        get => Interlocked.CompareExchange(ref _pendingEnrollmentName, null!, null!);
        set => Interlocked.Exchange(ref _pendingEnrollmentName, value);
    }

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
    private long _recognitionFrameInterval = 30; // run recognition every ~1 sec at 30fps

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

                // ── Face Recognition (~1 Hz or on enrollment) ──
                try
                {
                    var enrollName = PendingEnrollmentName;
                    bool shouldRecognize = enrollName is not null
                        || (_frameCount % _recognitionFrameInterval == 0);

                    if (shouldRecognize && face.IsTracked)
                    {
                        var colorFrame = LatestColor;
                        if (colorFrame?.PixelData is { Length: > 0 })
                        {
                            var crop = ExtractFaceCrop(colorFrame, face.FaceRect);
                            if (crop is not null)
                            {
                                var (cropData, cropW, cropH) = crop.Value;
                                var embedding = _faceRecognizer.GenerateEmbedding(cropData, cropW, cropH);

                                if (embedding.Length > 0)
                                {
                                    // Enrollment request
                                    if (enrollName is not null)
                                    {
                                        _faceRecognizer.Enroll(enrollName, embedding);
                                        PendingEnrollmentName = null;
                                        _logger.LogInformation("Enrolled face: {Name} (total enrolled: {Count})",
                                            enrollName, _faceRecognizer.EnrolledCount);

                                        LatestRecognition = new FaceRecognitionResult
                                        {
                                            IdentityName = enrollName,
                                            Similarity = 1.0f,
                                            Embedding = embedding,
                                            Timestamp = DateTimeOffset.UtcNow,
                                        };
                                    }
                                    else
                                    {
                                        // Match against enrolled faces
                                        var match = _faceRecognizer.Match(embedding);
                                        LatestRecognition = new FaceRecognitionResult
                                        {
                                            IdentityName = match?.Name,
                                            Similarity = match?.Similarity ?? 0f,
                                            Embedding = embedding,
                                            Timestamp = DateTimeOffset.UtcNow,
                                        };

                                        if (_frameCount % 150 == 0)
                                            _logger.LogInformation("  FaceRec: match={Name} sim={Sim:F3} enrolled={Count}",
                                                match?.Name ?? "unknown", match?.Similarity ?? 0f, _faceRecognizer.EnrolledCount);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception rex)
                {
                    if (_frameCount <= 5)
                        _logger.LogWarning(rex, "Face recognition error (frame {Frame})", _frameCount);
                }

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

    /// <summary>
    /// Extracts a face crop from the color frame using the face bounding rectangle.
    /// Returns BGR24 pixel data suitable for ArcFaceRecognizer, or null if out of bounds.
    /// Adds a 20% margin around the face rect for better recognition.
    /// </summary>
    private static (byte[] Data, int Width, int Height)? ExtractFaceCrop(
        ColorFrame color, (int X, int Y, int Width, int Height) faceRect)
    {
        var (fx, fy, fw, fh) = faceRect;
        if (fw <= 0 || fh <= 0) return null;

        // Add 20% margin for better alignment coverage
        int marginX = (int)(fw * 0.2);
        int marginY = (int)(fh * 0.2);
        int x0 = Math.Max(0, fx - marginX);
        int y0 = Math.Max(0, fy - marginY);
        int x1 = Math.Min(color.Width, fx + fw + marginX);
        int y1 = Math.Min(color.Height, fy + fh + marginY);

        int cropW = x1 - x0;
        int cropH = y1 - y0;
        if (cropW <= 4 || cropH <= 4) return null;

        // Extract BGR24 from BGRA32 source
        var bgrData = new byte[cropW * cropH * 3];
        int srcStride = color.Width * color.BytesPerPixel;
        int dstIdx = 0;

        for (int row = y0; row < y1; row++)
        {
            int srcRowStart = row * srcStride + x0 * color.BytesPerPixel;
            for (int col = 0; col < cropW; col++)
            {
                int srcIdx = srcRowStart + col * color.BytesPerPixel;
                if (srcIdx + 2 >= color.PixelData.Length) continue;

                bgrData[dstIdx++] = color.PixelData[srcIdx];     // B
                bgrData[dstIdx++] = color.PixelData[srcIdx + 1]; // G
                bgrData[dstIdx++] = color.PixelData[srcIdx + 2]; // R
            }
        }

        return (bgrData, cropW, cropH);
    }
}
