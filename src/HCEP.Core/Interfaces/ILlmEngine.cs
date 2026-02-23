// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// Agentic hybrid LLM engine — routes between local Ollama and cloud GPT-5-mini
/// based on query complexity, latency budget, and HCEP context.
/// Supports agentic tool-use patterns for multi-step reasoning.
/// </summary>
public interface ILlmEngine
{
    /// <summary>
    /// Sends a prompt with HCEP context to the LLM and returns the exchange.
    /// Automatically routes to local or cloud model based on policy.
    /// </summary>
    Task<LlmExchange> PromptAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        CancellationToken ct = default);

    /// <summary>
    /// Streams tokens from the LLM response.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        CancellationToken ct = default);

    /// <summary>Whether the local Ollama server is reachable.</summary>
    Task<bool> IsLocalAvailableAsync(CancellationToken ct = default);

    /// <summary>Whether the cloud endpoint is configured and reachable.</summary>
    Task<bool> IsCloudAvailableAsync(CancellationToken ct = default);
}
