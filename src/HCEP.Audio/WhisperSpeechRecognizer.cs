// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace HCEP.Audio;

/// <summary>
/// Whisper.net-based speech recognizer.
/// Accumulates audio chunks and runs transcription when enough
/// audio has been buffered (VAD-like chunk detection).
/// </summary>
public sealed class WhisperSpeechRecognizer : ISpeechRecognizer
{
    private readonly ILogger<WhisperSpeechRecognizer> _logger;
    private WhisperProcessor? _processor;
    private WhisperFactory? _factory;
    private readonly MemoryStream _audioBuffer = new();
    private bool _isReady;

    /// <summary>Minimum audio buffer length (in bytes) before processing. ~2 seconds at 16kHz/16-bit.</summary>
    private const int MinBufferBytes = 16000 * 2 * 2; // 2 seconds

    /// <summary>Maximum audio buffer length before forced flush. ~10 seconds.</summary>
    private const int MaxBufferBytes = 16000 * 2 * 10;

    public WhisperSpeechRecognizer(ILogger<WhisperSpeechRecognizer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsReady => _isReady;

    /// <inheritdoc />
    public async Task LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("Whisper model not found at {Path}", modelPath);
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(modelPath);
                _processor = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(Environment.ProcessorCount / 2)
                    .Build();
            }, ct);

            _isReady = true;
            _logger.LogInformation("Whisper model loaded from {Path}", modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Whisper model");
            _isReady = false;
        }
    }

    /// <inheritdoc />
    public async Task<SpeechResult[]> ProcessAsync(AudioFrame frame, CancellationToken ct = default)
    {
        if (!_isReady || _processor is null)
            return [];

        // Accumulate audio data
        _audioBuffer.Write(frame.PcmData, 0, frame.ByteCount > 0 ? frame.ByteCount : frame.PcmData.Length);

        // Process when enough audio has accumulated
        if (_audioBuffer.Length >= MinBufferBytes)
        {
            bool force = _audioBuffer.Length >= MaxBufferBytes;
            if (force || HasSpeechEnd(frame))
            {
                return await TranscribeBufferAsync(frame, ct);
            }
        }

        return [];
    }

    /// <inheritdoc />
    public async Task<SpeechResult[]> FlushAsync(CancellationToken ct = default)
    {
        if (!_isReady || _processor is null || _audioBuffer.Length == 0)
            return [];

        var dummyFrame = new AudioFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            PcmData = [],
        };

        return await TranscribeBufferAsync(dummyFrame, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        await _audioBuffer.DisposeAsync();
    }

    // ── Private ────────────────────────────────────────────────

    private async Task<SpeechResult[]> TranscribeBufferAsync(AudioFrame frame, CancellationToken ct)
    {
        if (_processor is null) return [];

        try
        {
            // Convert PCM bytes to float samples for Whisper
            var pcmBytes = _audioBuffer.ToArray();
            var samples = ConvertPcm16ToFloat(pcmBytes);

            // Reset buffer
            _audioBuffer.SetLength(0);

            // Run inference
            var results = new List<SpeechResult>();

            await foreach (var segment in _processor.ProcessAsync(samples, ct))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    results.Add(new SpeechResult
                    {
                        Text = segment.Text.Trim(),
                        Start = segment.Start,
                        End = segment.End,
                        IsFinal = true,
                        Confidence = 0.85f, // Whisper doesn't provide per-segment confidence
                        SourceAngleDeg = frame.SourceAngleDeg,
                        Timestamp = frame.Timestamp,
                    });
                }
            }

            return results.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Whisper transcription error");
            return [];
        }
    }

    private static float[] ConvertPcm16ToFloat(byte[] pcmData)
    {
        int sampleCount = pcmData.Length / 2;
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(pcmData, i * 2);
            samples[i] = sample / 32768f;
        }

        return samples;
    }

    /// <summary>
    /// Simple energy-based speech endpoint detection.
    /// Returns true when audio energy drops below threshold (silence).
    /// </summary>
    private static bool HasSpeechEnd(AudioFrame frame)
    {
        if (frame.PcmData.Length < 4) return false;

        // Compute RMS energy of the last chunk
        double sumSq = 0;
        int samples = Math.Min(frame.PcmData.Length / 2, 800); // last ~50ms
        int offset = frame.PcmData.Length - samples * 2;

        for (int i = 0; i < samples; i++)
        {
            short sample = BitConverter.ToInt16(frame.PcmData, offset + i * 2);
            sumSq += sample * sample;
        }

        double rms = Math.Sqrt(sumSq / samples);
        return rms < 500; // Silence threshold
    }
}
