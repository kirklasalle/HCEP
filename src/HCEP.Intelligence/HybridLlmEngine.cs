// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Intelligence;

/// <summary>
/// Agentic hybrid LLM engine routing between local Ollama and cloud OpenAI GPT-5-mini.
/// Injects HCEP context into prompts for mode-aware, agentic multi-step responses.
/// Supports tool-use / function-calling patterns for autonomous reasoning.
/// </summary>
public sealed class HybridLlmEngine : ILlmEngine
{
    private readonly HttpClient _httpClient;
    private readonly IKnowledgeStore _knowledge;
    private readonly AgenticToolExecutor _toolExecutor;
    private readonly ILogger<HybridLlmEngine> _logger;

    /// <summary>Maximum agentic reasoning steps before forcing a final answer.</summary>
    public int MaxAgenticSteps { get; set; } = 5;

    /// <summary>Whether to enable agentic tool-use on cloud requests.</summary>
    public bool AgenticToolUseEnabled { get; set; } = true;

    public HybridLlmEngine(
        HttpClient httpClient,
        IKnowledgeStore knowledge,
        AgenticToolExecutor toolExecutor,
        ILogger<HybridLlmEngine> logger)
    {
        _httpClient = httpClient;
        _knowledge = knowledge;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    // ── Configuration ──────────────────────────────────────────

    /// <summary>Ollama API base URL.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Ollama model name.</summary>
    public string OllamaModel { get; set; } = "llama3:8b";

    /// <summary>OpenAI API base URL.</summary>
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>OpenAI model name.</summary>
    public string OpenAiModel { get; set; } = "gpt-5-mini";

    /// <summary>OpenAI API key (empty = cloud disabled).</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>Latency threshold (ms) — if local exceeds this, try cloud.</summary>
    public int LatencyThresholdMs { get; set; } = 3000;

    // ── ILlmEngine ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LlmExchange> PromptAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(hcepContext);
        var start = DateTimeOffset.UtcNow;

        // Try local first
        bool useLocal = forceLocal || string.IsNullOrEmpty(OpenAiApiKey);

        if (useLocal || await IsLocalAvailableAsync(ct))
        {
            try
            {
                var response = await CallOllamaAsync(systemPrompt, userMessage, ct);
                return new LlmExchange
                {
                    SystemPrompt = systemPrompt,
                    UserMessage = userMessage,
                    HcepContext = hcepContext?.ToString(),
                    Response = response,
                    ModelId = OllamaModel,
                    IsLocal = true,
                    Latency = DateTimeOffset.UtcNow - start,
                    Timestamp = start,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama call failed, falling back to cloud");
            }
        }

        // Fallback to cloud
        if (!string.IsNullOrEmpty(OpenAiApiKey))
        {
            try
            {
                var response = await CallOpenAiAsync(systemPrompt, userMessage, ct);
                return new LlmExchange
                {
                    SystemPrompt = systemPrompt,
                    UserMessage = userMessage,
                    HcepContext = hcepContext?.ToString(),
                    Response = response,
                    ModelId = OpenAiModel,
                    IsLocal = false,
                    Latency = DateTimeOffset.UtcNow - start,
                    Timestamp = start,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI call failed");
            }
        }

        return new LlmExchange
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Response = "[No LLM available]",
            Latency = DateTimeOffset.UtcNow - start,
            Timestamp = start,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(hcepContext);

        // Stream from Ollama
        var request = new OllamaRequest
        {
            Model = OllamaModel,
            System = systemPrompt,
            Prompt = userMessage,
            Stream = true,
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"{OllamaBaseUrl}/api/generate")
            {
                Content = content,
            },
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaStreamResponse>(line);
            if (chunk?.Response is not null)
                yield return chunk.Response;

            if (chunk?.Done == true)
                break;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLocalAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2000);
            var response = await _httpClient.GetAsync($"{OllamaBaseUrl}/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsCloudAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(OpenAiApiKey)) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{OpenAiBaseUrl}/models");
            request.Headers.Authorization = new("Bearer", OpenAiApiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(3000);
            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Private ────────────────────────────────────────────────

    private string BuildSystemPrompt(HcepReading? hcep)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are HCEP — Human Communication Eye Protocol assistant.");
        sb.AppendLine("You analyze human communication through eye contact patterns, facial expressions, and speech.");

        if (hcep is not null && hcep.Mode != HcepMode.Unknown)
        {
            sb.AppendLine();
            sb.AppendLine("=== Current HCEP State ===");
            sb.AppendLine($"Mode: {hcep.Mode}");
            sb.AppendLine($"Gaze Region: {hcep.Region}");
            sb.AppendLine($"Cognitive State: {hcep.Cognitive}");
            sb.AppendLine($"Emotional Valence: {hcep.Valence}");
            sb.AppendLine($"Confidence: {hcep.Confidence:F2}");
            sb.AppendLine();

            // Mode-specific behavioral guidance
            sb.AppendLine(hcep.Mode switch
            {
                HcepMode.Logic => "The person is in analytical/logical mode. Respond with structured, precise information.",
                HcepMode.Affect => "The person is emotionally engaged. Be empathetic and emotionally aware in your response.",
                HcepMode.Spirit => "Deep rapport detected. Respond authentically and personally.",
                HcepMode.Heart => "Empathic resonance detected. Use warm, supportive language.",
                HcepMode.Think => "The person is internally processing. Keep your response concise to not interrupt their thought.",
                _ => "",
            });
        }

        return sb.ToString();
    }

    private async Task<string> CallOllamaAsync(string system, string prompt, CancellationToken ct)
    {
        var request = new OllamaRequest
        {
            Model = OllamaModel,
            System = system,
            Prompt = prompt,
            Stream = false,
        };

        var response = await _httpClient.PostAsJsonAsync($"{OllamaBaseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result?.Response ?? string.Empty;
    }

    private async Task<string> CallOpenAiAsync(string system, string userMessage, CancellationToken ct)
    {
        var messages = new List<OpenAiMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "user", Content = userMessage },
        };

        // ── Agentic multi-step reasoning loop ──────────────
        // If tool-use is enabled, include HCEP tool definitions and let the
        // model autonomously invoke tools across multiple reasoning steps.
        int steps = 0;
        while (steps < MaxAgenticSteps)
        {
            steps++;

            var request = new OpenAiRequest
            {
                Model = OpenAiModel,
                Messages = messages,
            };

            // Attach tool definitions on cloud requests when agentic mode is active
            if (AgenticToolUseEnabled)
            {
                request.Tools = AgenticToolDefinitions.GetHCEPTools()
                    .Select(t => new OpenAiToolDef
                    {
                        Type = t.Type,
                        Function = new OpenAiFunctionDef
                        {
                            Name = t.Function.Name,
                            Description = t.Function.Description,
                            Parameters = t.Function.Parameters,
                        },
                    })
                    .ToList();
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{OpenAiBaseUrl}/chat/completions");
            httpRequest.Headers.Authorization = new("Bearer", OpenAiApiKey);
            httpRequest.Content = JsonContent.Create(request);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(ct);
            var choice = result?.Choices?.FirstOrDefault();

            if (choice?.Message is null)
                return string.Empty;

            // Check if the model wants to call tools
            if (choice.Message.ToolCalls is { Count: > 0 } toolCalls)
            {
                _logger.LogDebug("Agentic step {Step}: {Count} tool call(s) requested",
                    steps, toolCalls.Count);

                // Add the assistant's tool-call message to conversation
                messages.Add(new OpenAiMessage
                {
                    Role = "assistant",
                    Content = choice.Message.Content ?? "",
                    ToolCalls = toolCalls,
                });

                // Execute each tool call and add results
                var agenticCalls = toolCalls.Select(tc => new AgenticToolCall
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new AgenticToolCallFunction
                    {
                        Name = tc.Function?.Name ?? "",
                        Arguments = tc.Function?.Arguments ?? "{}",
                    },
                }).ToList();

                var toolResults = await _toolExecutor.ExecuteAsync(agenticCalls, ct);

                foreach (var tr in toolResults)
                {
                    messages.Add(new OpenAiMessage
                    {
                        Role = "tool",
                        Content = tr.Content,
                        ToolCallId = tr.ToolCallId,
                    });
                }

                continue; // Loop back for the model's next response
            }

            // No tool calls — return the final response
            return choice.Message.Content ?? string.Empty;
        }

        _logger.LogWarning("Agentic reasoning hit max steps ({Max}) — returning last response", MaxAgenticSteps);
        return messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "[Max agentic steps reached]";
    }

    // ── DTOs ───────────────────────────────────────────────────

    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("system")] public string System { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
    }

    private sealed class OllamaStreamResponse
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }

    private sealed class OpenAiRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; set; } = [];
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OpenAiToolDef>? Tools { get; set; }
    }

    private sealed class OpenAiMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OpenAiToolCallDto>? ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
    }

    private sealed class OpenAiToolDef
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public OpenAiFunctionDef? Function { get; set; }
    }

    private sealed class OpenAiFunctionDef
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("parameters")] public AgenticParameters? Parameters { get; set; }
    }

    private sealed class OpenAiToolCallDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public OpenAiToolCallFunction? Function { get; set; }
    }

    private sealed class OpenAiToolCallFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
    }

    private sealed class OpenAiResponse
    {
        [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
    }
}
