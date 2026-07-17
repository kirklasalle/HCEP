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
using System.Collections.Generic;
using System.Text.Json.Serialization;
using HCEP.Core.Models; // For AgenticParameters if referenced

namespace HCEP.Intelligence;

internal sealed class OllamaRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("system")] public string System { get; set; } = "";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("stream")] public bool Stream { get; set; }
}

internal sealed class OllamaResponse
{
    [JsonPropertyName("response")] public string? Response { get; set; }
}

internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")] public List<OllamaTagInfo>? Models { get; set; }
}

internal sealed class OllamaTagInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal sealed class OllamaStreamResponse
{
    [JsonPropertyName("response")] public string? Response { get; set; }
    [JsonPropertyName("done")] public bool Done { get; set; }
}

internal sealed class LlamaCppNativeRequest
{
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.7f;
    [JsonPropertyName("stream")] public bool Stream { get; set; }
}

internal sealed class LlamaCppNativeResponse
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

internal sealed class AnthropicRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 4096;
    [JsonPropertyName("system")] public string System { get; set; } = "";
    [JsonPropertyName("messages")] public List<AnthropicMessage> Messages { get; set; } = [];
    [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.7f;
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

internal sealed class AnthropicResponse
{
    [JsonPropertyName("content")] public List<AnthropicContentPart>? Content { get; set; }
}

internal sealed class AnthropicContentPart
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

internal sealed class GeminiRequest
{
    [JsonPropertyName("systemInstruction")] public GeminiSystemInstruction? SystemInstruction { get; set; }
    [JsonPropertyName("contents")] public List<GeminiContent> Contents { get; set; } = [];
}

internal sealed class GeminiSystemInstruction
{
    [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = [];
}

internal sealed class GeminiContent
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = [];
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

internal sealed class GeminiResponse
{
    [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
}

internal sealed class GeminiModelsResponse
{
    [JsonPropertyName("models")] public List<GeminiModelInfo>? Models { get; set; }
}

internal sealed class GeminiModelInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("supportedGenerationMethods")] public List<string>? SupportedGenerationMethods { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
}

internal sealed class OpenAiRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; set; } = [];
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiToolDef>? Tools { get; set; }
}

internal sealed class OpenAiMessage
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

internal sealed class OpenAiToolDef
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiFunctionDef? Function { get; set; }
}

internal sealed class OpenAiFunctionDef
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("parameters")] public AgenticParameters? Parameters { get; set; }
}

internal sealed class OpenAiToolCallDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiToolCallFunction? Function { get; set; }
}

internal sealed class OpenAiToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
}

internal sealed class OpenAiResponse
{
    [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; set; }
}

internal sealed class OpenAiModelsResponse
{
    [JsonPropertyName("data")] public List<OpenAiModelInfo>? Data { get; set; }
}

internal sealed class OpenAiModelInfo
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
}
