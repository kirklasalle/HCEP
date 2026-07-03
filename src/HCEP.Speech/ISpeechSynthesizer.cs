// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
namespace HCEP.Speech;

/// <summary>
/// Describes the mouth shape associated with a single phoneme/viseme.
/// All values [0..1] drive avatar facial parameters directly.
///
/// Scientific basis: Preston Blair (1949) 18-position mouth chart;
/// Disney FACS lip-sync extension; SAPI 21-viseme taxonomy;
/// Cohen &amp; Massaro (1994) DOMINANCE model of audiovisual speech.
/// McGurk &amp; MacDonald (1976) established that visual mouth movement
/// is a first-class speech channel — not decoration.
/// </summary>
public readonly record struct VisemeData
{
    /// <summary>SAPI Viseme ID (0–21). 0 = silence.</summary>
    public int VisemeId { get; init; }

    /// <summary>Jaw openness [0=fully closed .. 1=fully open].</summary>
    public float JawOpen { get; init; }

    /// <summary>Lip rounding [0=neutral .. 1=fully rounded (O/U shape)].</summary>
    public float LipRound { get; init; }

    /// <summary>Lip spreading [0=neutral .. 1=fully spread (I/EE smile shape)].</summary>
    public float LipSpread { get; init; }

    /// <summary>Lip compression [0=open .. 1=lips pressed together (M/B/P bilabials)].</summary>
    public float LipCompressed { get; init; }

    /// <summary>Upper-lip retraction for labio-dental contact (F/V) [0..1].</summary>
    public float UpperLipRetract { get; init; }

    /// <summary>Duration of this viseme in milliseconds (from SAPI timing data).</summary>
    public double DurationMs { get; init; }

    /// <summary>Silence / neutral mouth position.</summary>
    public static readonly VisemeData Silence = new() { VisemeId = 0 };
}

/// <summary>
/// Abstraction over all TTS (Text-to-Speech) backends.
/// </summary>
public interface ISpeechSynthesizer : IDisposable
{
    bool IsAvailable { get; }
    string BackendDescription { get; }

    Task SpeakAsync(string text, CancellationToken ct = default);
    void Stop();

    event Action<string>? SpeechStarted;

    /// <summary>
    /// Fired for each phoneme/viseme transition (~50–200ms intervals).
    /// Wire to the avatar's <c>SetViseme()</c> for phoneme-accurate lip sync.
    /// Natively supported by Windows SAPI. Cloud providers emit amplitude fallback.
    /// </summary>
    event Action<VisemeData>? VisemeChanged;

    /// <summary>Coarse amplitude [0..1] — fallback lip-sync for cloud TTS backends.</summary>
    event Action<float>? AudioAmplitude;

    event Action? SpeechCompleted;
}
