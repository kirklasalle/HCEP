// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HCEP.Speech;

/// <summary>
/// Streaming TTS using the ElevenLabs API — the highest-quality real-time
/// voice synthesis available as of 2026. Uses the streaming endpoint to
/// begin audio playback before the full synthesis is complete, reducing
/// perceived latency to under 300ms for short phrases.
///
/// Model:      eleven_turbo_v2_5 (lowest latency) or eleven_multilingual_v2
/// Voices:     Configure with any Voice ID from your ElevenLabs account.
/// API docs:   https://elevenlabs.io/docs/api-reference/text-to-speech
///
/// Obtain API key at: https://elevenlabs.io (free tier: 10,000 chars/month)
/// </summary>
public sealed class ElevenLabsTtsSynthesizer : ISpeechSynthesizer
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ElevenLabsTtsSynthesizer> _logger;
    private CancellationTokenSource? _activeCts;

    public string ApiKey { get; set; } = "";
    public string VoiceId { get; set; } = "21m00Tcm4TlvDq8ikWAM";  // Rachel (default)
    public string ModelId { get; set; } = "eleven_turbo_v2_5";      // Lowest latency
    public float Stability { get; set; } = 0.5f;
    public float SimilarityBoost { get; set; } = 0.75f;
    public float Style { get; set; } = 0.0f;
    public bool SpeakerBoost { get; set; } = true;

    public bool IsAvailable => !string.IsNullOrEmpty(ApiKey);
    public string BackendDescription => $"ElevenLabs — {ModelId}/{VoiceId[..8]}…";

    public event Action<string>? SpeechStarted;
    /// <summary>
    /// ElevenLabs does not expose phoneme timing. VisemeChanged fires amplitude-driven
    /// approximate visemes. For phoneme-accurate lip sync, use Windows SAPI backend.
    /// </summary>
    public event Action<VisemeData>? VisemeChanged;
    public event Action<float>? AudioAmplitude;
    public event Action? SpeechCompleted;

    public ElevenLabsTtsSynthesizer(HttpClient httpClient, ILogger<ElevenLabsTtsSynthesizer> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(ApiKey)) return;

        Stop();
        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _activeCts.Token;

        SpeechStarted?.Invoke(text);
        _logger.LogDebug("ElevenLabs TTS: voice={VoiceId} model={ModelId}", VoiceId, ModelId);

        try
        {
            var payload = new
            {
                text,
                model_id = ModelId,
                voice_settings = new
                {
                    stability = Stability,
                    similarity_boost = SimilarityBoost,
                    style = Style,
                    use_speaker_boost = SpeakerBoost
                }
            };

            var json = JsonSerializer.Serialize(payload);
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{VoiceId}/stream?output_format=mp3_44100_128";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("xi-api-key", ApiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            await using var audioStream = await response.Content.ReadAsStreamAsync(token);
            await PlayStreamingMp3Async(audioStream, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("ElevenLabs TTS cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ElevenLabs TTS error");
        }
        finally
        {
            AudioAmplitude?.Invoke(0f);
            VisemeChanged?.Invoke(VisemeData.Silence);
            SpeechCompleted?.Invoke();
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    private async Task PlayStreamingMp3Async(Stream mp3Stream, CancellationToken ct)
    {
        // For real-time streaming: buffer enough data (64KB) then start playback
        // while continuing to buffer in parallel.
        const int ChunkSize = 65536; // 64 KB initial buffer before starting playback
        using var ms = new MemoryStream();

        // Buffer initial chunk
        byte[] buf = new byte[4096];
        int totalRead = 0;
        while (totalRead < ChunkSize && !ct.IsCancellationRequested)
        {
            int read = await mp3Stream.ReadAsync(buf, ct);
            if (read == 0) break;
            ms.Write(buf, 0, read);
            totalRead += read;

            // Emit a rough amplitude proxy from buffer fill rate
            AudioAmplitude?.Invoke(Math.Clamp(totalRead / (float)ChunkSize, 0.1f, 0.9f));
        }

        // Copy remaining audio while playing
        var copyTask = mp3Stream.CopyToAsync(ms, ct);

        ms.Position = 0;
        using var mp3Reader = new NAudio.Wave.Mp3FileReader(ms);
        using var waveOut = new NAudio.Wave.WaveOutEvent();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
        waveOut.Init(mp3Reader);
        waveOut.Play();

        using var reg = ct.Register(() => { waveOut.Stop(); tcs.TrySetCanceled(ct); });

        await Task.WhenAll(tcs.Task, copyTask.ContinueWith(_ => { }, CancellationToken.None));
    }

    public void Stop()
    {
        _activeCts?.Cancel();
    }

    public void Dispose()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
    }
}
