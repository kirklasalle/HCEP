// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;

namespace HCEP.Speech;

/// <summary>
/// Offline TTS using the Windows built-in Speech API (SAPI / System.Speech).
/// Works on any Windows 10/11 machine with no API keys or internet required.
///
/// Voice quality depends on the voices installed via Windows Settings →
/// Time &amp; Language → Speech. Windows 11 includes high-quality neural voices
/// such as "Microsoft Aria" (en-US) that are dramatically better than older SAPI voices.
///
/// To list installed voices: run HCEP and check the log for "Available SAPI voices".
/// To install new voices: Settings → Time &amp; Language → Speech → Manage voices.
/// </summary>
public sealed class WindowsTtsSynthesizer : ISpeechSynthesizer
{
    private readonly SpeechSynthesizer _synth;
    private readonly ILogger<WindowsTtsSynthesizer> _logger;
    private volatile bool _speaking;

    /// <summary>
    /// Preferred voice name (partial match). Leave empty to use the system default.
    /// Examples: "Aria", "Jenny", "Guy", "Microsoft David"
    /// </summary>
    public string PreferredVoice { get; set; } = "";

    /// <summary>Speech rate: -10 (slowest) to +10 (fastest). Default 0 = normal.</summary>
    public int Rate { get; set; } = 0;

    /// <summary>Speech volume: 0–100. Default 90.</summary>
    public int Volume { get; set; } = 90;

    public bool IsAvailable => true; // SAPI is always available on Windows

    public string BackendDescription
    {
        get
        {
            var voice = _synth.Voice;
            return $"Windows SAPI — {voice?.Name ?? "default"}";
        }
    }

    public event Action<string>? SpeechStarted;
    public event Action<VisemeData>? VisemeChanged;
    public event Action<float>? AudioAmplitude;
    public event Action? SpeechCompleted;

    public WindowsTtsSynthesizer(ILogger<WindowsTtsSynthesizer> logger)
    {
        _logger = logger;
        _synth = new SpeechSynthesizer();
        _synth.SetOutputToDefaultAudioDevice();
        _synth.SpeakStarted += (_, e) => SpeechStarted?.Invoke(e.Prompt.ToString() ?? "");
        _synth.SpeakCompleted += (_, _) =>
        {
            _speaking = false;
            VisemeChanged?.Invoke(VisemeData.Silence);
            AudioAmplitude?.Invoke(0f);
            SpeechCompleted?.Invoke();
        };

        // ── Phoneme-accurate lip sync via SAPI VisemeReached ──────────
        // SAPI fires one event per phoneme (~50–200ms intervals) with the
        // viseme ID and its duration. We convert to VisemeData using the
        // Preston Blair / SAPI mapping table in VisemeController.
        _synth.VisemeReached += (_, e) =>
        {
            var viseme = VisemeController.FromSapiViseme(e.Viseme, e.Duration.TotalMilliseconds);
            VisemeChanged?.Invoke(viseme);

            // Also emit an amplitude proxy from JawOpen for simple integrations
            AudioAmplitude?.Invoke(viseme.JawOpen);
        };

        LogAvailableVoices();
    }

    private void LogAvailableVoices()
    {
        var voices = _synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo.Name)
            .ToList();
        _logger.LogInformation("Available SAPI voices ({Count}): {Voices}", voices.Count, string.Join(", ", voices));
    }

    /// <summary>
    /// Selects the best available voice matching <see cref="PreferredVoice"/> preference.
    /// Falls back to the system default if no match is found.
    /// </summary>
    private void ApplyVoiceSelection()
    {
        _synth.Rate = Rate;
        _synth.Volume = Volume;

        if (string.IsNullOrWhiteSpace(PreferredVoice)) return;

        var match = _synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .FirstOrDefault(v => v.Name.Contains(PreferredVoice, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            _synth.SelectVoice(match.Name);
            _logger.LogInformation("TTS voice selected: {Voice}", match.Name);
        }
        else
        {
            _logger.LogWarning("Preferred TTS voice '{Voice}' not found — using system default", PreferredVoice);
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Stop(); // cancel any in-progress speech
        ApplyVoiceSelection();

        _speaking = true;
        _logger.LogDebug("TTS speaking ({Chars} chars): {Preview}", text.Length, text[..Math.Min(60, text.Length)]);

        // SpeakAsync from System.Speech is non-blocking but not Task-based.
        // We wrap it in a TaskCompletionSource so callers can await it.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted() { tcs.TrySetResult(true); }
        SpeechCompleted += OnCompleted;

        using var reg = ct.Register(() =>
        {
            _synth.SpeakAsyncCancelAll();
            tcs.TrySetCanceled(ct);
        });

        try
        {
            _synth.SpeakAsync(text);
            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("TTS cancelled");
        }
        finally
        {
            SpeechCompleted -= OnCompleted;
        }
    }

    public void Stop()
    {
        if (_speaking)
        {
            _synth.SpeakAsyncCancelAll();
            _speaking = false;
        }
    }

    public void Dispose()
    {
        _synth.SpeakAsyncCancelAll();
        _synth.Dispose();
    }
}
