// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Models;

/// <summary>
/// LLM prompt/response pair for the Intelligence layer.
/// </summary>
public sealed record LlmExchange
{
    /// <summary>System prompt or context preamble.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>User message content.</summary>
    public required string UserMessage { get; init; }

    /// <summary>HCEP context injected into the prompt.</summary>
    public string? HcepContext { get; init; }

    /// <summary>LLM response text.</summary>
    public string? Response { get; init; }

    /// <summary>Model identifier (e.g., "llama3:8b" or "gpt-5-mini").</summary>
    public string? ModelId { get; init; }

    /// <summary>Whether this used the local (Ollama) or cloud (OpenAI) endpoint.</summary>
    public bool IsLocal { get; init; }

    /// <summary>Total token count (prompt + completion).</summary>
    public int TotalTokens { get; init; }

    /// <summary>Response latency.</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>Request timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
