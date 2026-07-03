// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using Microsoft.Extensions.Logging;

namespace HCEP.Speech;

/// <summary>
/// Routing TTS engine: tries providers in priority order and uses the first
/// available one. Default priority: ElevenLabs (best quality) → OpenAI TTS
/// (good quality, widely available) → Windows SAPI (offline fallback, always works).
///
/// This means the avatar always speaks, even with no cloud API keys configured.
/// When a cloud key is added via Settings, it automatically upgrades to the
/// higher-quality provider on the next utterance.
/// </summary>
public sealed class HybridTtsEngine : ISpeechSynthesizer
{
    private readonly ElevenLabsTtsSynthesizer _elevenLabs;
    private readonly OpenAiTtsSynthesizer _openAi;
    private readonly WindowsTtsSynthesizer _windows;
    private readonly ILogger<HybridTtsEngine> _logger;

    public bool IsAvailable => true; // Windows SAPI is always available
    public string BackendDescription => ActiveSynthesizer.BackendDescription;

    private ISpeechSynthesizer ActiveSynthesizer =>
        _elevenLabs.IsAvailable ? _elevenLabs :
        _openAi.IsAvailable ? _openAi :
                                  _windows;

    public event Action<string>? SpeechStarted;
    public event Action<VisemeData>? VisemeChanged;
    public event Action<float>? AudioAmplitude;
    public event Action? SpeechCompleted;

    public HybridTtsEngine(
        ElevenLabsTtsSynthesizer elevenLabs,
        OpenAiTtsSynthesizer openAi,
        WindowsTtsSynthesizer windows,
        ILogger<HybridTtsEngine> logger)
    {
        _elevenLabs = elevenLabs;
        _openAi = openAi;
        _windows = windows;
        _logger = logger;

        // Relay all events from whichever backend fires them
        foreach (var synth in new ISpeechSynthesizer[] { elevenLabs, openAi, windows })
        {
            synth.SpeechStarted += text => SpeechStarted?.Invoke(text);
            synth.VisemeChanged += viseme => VisemeChanged?.Invoke(viseme);
            synth.AudioAmplitude += amp => AudioAmplitude?.Invoke(amp);
            synth.SpeechCompleted += () => SpeechCompleted?.Invoke();
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        var synth = ActiveSynthesizer;
        _logger.LogDebug("TTS routing to: {Backend}", synth.BackendDescription);
        await synth.SpeakAsync(text, ct);
    }

    public void Stop()
    {
        _elevenLabs.Stop();
        _openAi.Stop();
        _windows.Stop();
    }

    public void Dispose()
    {
        _elevenLabs.Dispose();
        _openAi.Dispose();
        _windows.Dispose();
    }
}
