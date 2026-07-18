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
using System;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Diagnostics;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

public sealed partial class HCEPPipelineOrchestrator
{
    /// <summary>
    /// Reads speech results from the audio pipeline, injects them into the
    /// vision pipeline (for HCEP mode analysis), fires SpeechReady,
    /// and triggers an LLM response using current HCEP context.
    /// </summary>
    private async Task RunSpeechLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var result in _audio.SpeechOutput.ReadAllAsync(ct))
            {
                _latestSpeech = result;
                _vision.LatestSpeech = result;

                // ── Workstream C: update speech cadence estimate ─────────────
                // Estimate syllables/sec from transcript length and Whisper segment duration.
                if (result.IsFinal && result.Text.Length > 0)
                {
                    double durationSecs = (result.End - result.Start).TotalSeconds;
                    if (durationSecs > 0.1)
                    {
                        // Rough model: 1 syllable ≈ 3.3 chars (5 chars/word × 1.5 syll/word)
                        double estimatedSyllables = result.Text.Length / 3.3;
                        float syllablesPerSec = (float)Math.Clamp(estimatedSyllables / durationSecs, 0.5, 12.0);
                        _latestCadence = new SpeechCadenceProfile
                        {
                            SyllablesPerSecond = syllablesPerSec,
                            LastSpeechBurstMs = (float)(durationSecs * 1000),
                            LastUpdate = DateTimeOffset.UtcNow,
                        };
                    }
                }

                _telemetry.Increment("speech.results");
                _logger.LogDebug("Speech: {Text}", result.Text);

                SpeechReady?.Invoke(result);

                // ── Knowledge: record utterance (M1.2) ────────
                var person = _latestSnapshot?.PrimaryPerson;
                string personName = person?.IdentityName ?? $"Person-{person?.TrackingId ?? 0}";
                try
                {
                    _personKnowledge.RecordUtterance(personName, result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PersonKnowledge.RecordUtterance failed");
                }

                // ── LLM: auto-respond to speech (M1.3) ───────
                // Fire-and-forget the LLM call on ThreadPool to avoid
                // blocking the audio channel reader
                _ = Task.Run(async () =>
                {
                    string correlationId = CorrelationContext.Create("speech-llm");
                    using var correlationScope = CorrelationContext.BeginScope(correlationId);
                    try
                    {
                        _telemetry.Increment("correlation.speech_llm.requests");
                        _telemetry.RecordGauge("correlation.speech_llm.last_hash", CorrelationContext.ToNumericFingerprint(correlationId));

                        var hcep = _latestSnapshot?.PrimaryPerson?.LatestHcep;
                        var exchange = await _llmEngine.PromptAsync(result.Text, hcep, ct: ct);

                        _logger.LogInformation(
                            "LLM response ({Model}, {Latency:F0}ms): {Response}",
                            exchange.ModelId,
                            exchange.Latency.TotalMilliseconds,
                            exchange.Response?[..Math.Min(exchange.Response.Length, 80)]);

                        _telemetry.RecordTiming("llm.latency_ms", exchange.Latency.TotalMilliseconds);
                        _telemetry.Increment(exchange.IsLocal ? "llm.local_calls" : "llm.cloud_calls");

                        if (!string.IsNullOrWhiteSpace(exchange.CorrelationId))
                            _telemetry.RecordGauge("correlation.llm.last_hash", CorrelationContext.ToNumericFingerprint(exchange.CorrelationId));

                        // Record exchange in knowledge store
                        try
                        {
                            _personKnowledge.RecordExchange(personName, exchange);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "PersonKnowledge.RecordExchange failed");
                        }

                        LlmResponseReady?.Invoke(exchange);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "LLM auto-response failed for speech (corr={CorrelationId}): {Text}",
                            correlationId,
                            result.Text[..Math.Min(result.Text.Length, 50)]);
                    }
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech loop error");
        }
    }
}
