// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Threading.Channels;
using HCEP.Core.Channels;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Audio;

/// <summary>
/// Audio processing pipeline — consumes <see cref="AudioFrame"/> from the
/// sensor source and produces <see cref="SpeechResult"/> via a channel.
/// Handles buffering, beam-angle tracking, and Whisper.net transcription.
/// </summary>
public sealed class AudioPipeline : IAsyncDisposable
{
    private readonly ISpeechRecognizer _recognizer;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<AudioPipeline> _logger;

    private readonly Channel<AudioFrame> _audioInput = HCEPChannels.CreateRealTime<AudioFrame>(128);
    private readonly Channel<SpeechResult> _speechOutput = HCEPChannels.CreateReliable<SpeechResult>();

    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public AudioPipeline(
        ISpeechRecognizer recognizer,
        ITelemetryService telemetry,
        ILogger<AudioPipeline> logger)
    {
        _recognizer = recognizer;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>Writer for pushing audio frames into the pipeline.</summary>
    public ChannelWriter<AudioFrame> AudioInput => _audioInput.Writer;

    /// <summary>Reader for consuming speech results.</summary>
    public ChannelReader<SpeechResult> SpeechOutput => _speechOutput.Reader;

    /// <summary>Current beam angle from the Kinect microphone array.</summary>
    public double CurrentBeamAngle { get; private set; }

    /// <summary>Starts the background processing loop.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processingTask = ProcessAsync(_cts.Token);
        _logger.LogInformation("Audio pipeline started");
        return Task.CompletedTask;
    }

    /// <summary>Loads the transcription model into the recognizer.</summary>
    public Task LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        return _recognizer.LoadModelAsync(modelPath, ct);
    }

    /// <summary>Stops the pipeline and flushes remaining audio.</summary>
    public async Task StopAsync()
    {
        // Flush the recognizer before stopping
        try
        {
            var remaining = await _recognizer.FlushAsync();
            foreach (var result in remaining)
                await _speechOutput.Writer.WriteAsync(result);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error flushing speech recognizer");
        }

        _cts?.Cancel();
        _audioInput.Writer.TryComplete();

        if (_processingTask is not null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { }
        }

        _cts?.Dispose();
        _logger.LogInformation("Audio pipeline stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var frame in _audioInput.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var timer = _telemetry.StartTimer("audio.frame_ms");

                // Track beam angle
                CurrentBeamAngle = frame.BeamAngleDeg;
                _telemetry.RecordGauge("audio.beam_angle", frame.BeamAngleDeg);
                _telemetry.RecordGauge("audio.source_confidence", frame.SourceConfidence);

                // Process through Whisper
                var results = await _recognizer.ProcessAsync(frame, ct);

                foreach (var result in results)
                {
                    await _speechOutput.Writer.WriteAsync(result, ct);
                    _telemetry.Increment("audio.transcriptions");
                    _logger.LogDebug("STT: [{Lang}] {Text}", result.Language, result.Text);
                }

                _telemetry.Increment("audio.frames_processed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error processing audio frame");
                _telemetry.Increment("audio.errors");
            }
        }
    }
}
