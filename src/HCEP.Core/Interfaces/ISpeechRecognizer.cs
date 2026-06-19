// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// Speech-to-text engine abstraction (Whisper.net backend).
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>
    /// Processes an audio frame and returns transcription results.
    /// May return empty array if not enough audio has accumulated.
    /// </summary>
    Task<SpeechResult[]> ProcessAsync(AudioFrame frame, CancellationToken ct = default);

    /// <summary>
    /// Forces processing of any buffered audio and returns results.
    /// </summary>
    Task<SpeechResult[]> FlushAsync(CancellationToken ct = default);

    /// <summary>Whether the recognizer model is loaded and ready.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Loads the Whisper model asynchronously.
    /// </summary>
    /// <param name="modelPath">Path to the Whisper GGML model file.</param>
    Task LoadModelAsync(string modelPath, CancellationToken ct = default);
}
