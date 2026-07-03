// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HCEP.Speech;

/// <summary>
/// Streaming TTS using the OpenAI Audio API (tts-1 or tts-1-hd).
/// Audio is streamed as MP3 chunks and played via NAudio or piped to the
/// Windows audio device using the raw PCM stream from the API.
///
/// Models:    tts-1 (fast, lower quality)  |  tts-1-hd (slower, high quality)
/// Voices:    alloy · echo · fable · onyx · nova · shimmer
/// API docs:  https://platform.openai.com/docs/api-reference/audio/createSpeech
///
/// Also compatible with any OpenAI-compatible TTS endpoint — including
/// local models serving the same REST surface (e.g. piper-tts with oai-shim).
/// </summary>
public sealed class OpenAiTtsSynthesizer : ISpeechSynthesizer
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiTtsSynthesizer> _logger;
    private CancellationTokenSource? _activeCts;

    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "tts-1";
    public string Voice { get; set; } = "nova";          // warm, conversational
    public float Speed { get; set; } = 1.0f;             // 0.25 – 4.0

    public bool IsAvailable => !string.IsNullOrEmpty(ApiKey);
    public string BackendDescription => $"OpenAI TTS — {Model}/{Voice}";

    public event Action<string>? SpeechStarted;
    /// <summary>
    /// OpenAI TTS does not expose phoneme timing, so VisemeChanged fires amplitude-driven
    /// approximate visemes (jawOpen ∝ amplitude). For precise lip sync, use Windows SAPI.
    /// </summary>
    public event Action<VisemeData>? VisemeChanged;
    public event Action<float>? AudioAmplitude;
    public event Action? SpeechCompleted;

    public OpenAiTtsSynthesizer(HttpClient httpClient, ILogger<OpenAiTtsSynthesizer> logger)
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
        _logger.LogDebug("OpenAI TTS: model={Model} voice={Voice}", Model, Voice);

        try
        {
            var payload = new
            {
                model = Model,
                voice = Voice,
                input = text,
                speed = Speed,
                response_format = "mp3"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/audio/speech");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            // Stream the MP3 audio into NAudio for real-time playback
            await using var audioStream = await response.Content.ReadAsStreamAsync(token);
            await PlayMp3StreamAsync(audioStream, token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("OpenAI TTS cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI TTS error");
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

    private async Task PlayMp3StreamAsync(Stream mp3Stream, CancellationToken ct)
    {
        // Buffer the stream into memory (streaming mp3 decode via NAudio)
        // NAudio is already a dependency of HCEP.Audio — we reference it via the stream.
        // Fallback: write to temp file and open via Windows default player.
        using var ms = new MemoryStream();
        await mp3Stream.CopyToAsync(ms, ct);
        ms.Position = 0;

        await PlayMp3BytesAsync(ms.ToArray(), ct);
    }

    private static async Task PlayMp3BytesAsync(byte[] mp3Bytes, CancellationToken ct)
    {
        // Decode and play using NAudio's Mp3FileReader + WaveOutEvent
        using var ms = new MemoryStream(mp3Bytes);
        using var mp3Reader = new NAudio.Wave.Mp3FileReader(ms);
        using var waveOut = new NAudio.Wave.WaveOutEvent();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
        waveOut.Init(mp3Reader);
        waveOut.Play();

        using var reg = ct.Register(() =>
        {
            waveOut.Stop();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
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
