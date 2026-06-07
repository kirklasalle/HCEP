// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.Threading;
using System.Threading.Tasks;
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
                    try
                    {
                        var hcep = _latestSnapshot?.PrimaryPerson?.LatestHcep;
                        var exchange = await _llmEngine.PromptAsync(result.Text, hcep, ct: ct);

                        _logger.LogInformation(
                            "LLM response ({Model}, {Latency:F0}ms): {Response}",
                            exchange.ModelId,
                            exchange.Latency.TotalMilliseconds,
                            exchange.Response?[..Math.Min(exchange.Response.Length, 80)]);

                        _telemetry.RecordTiming("llm.latency_ms", exchange.Latency.TotalMilliseconds);
                        _telemetry.Increment(exchange.IsLocal ? "llm.local_calls" : "llm.cloud_calls");

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
                        _logger.LogWarning(ex, "LLM auto-response failed for speech: {Text}",
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
