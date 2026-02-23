// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCEP.Intelligence;

/// <summary>
/// Agentic tool definitions for multi-step LLM reasoning.
/// These tools are passed to the cloud LLM (GPT-5-mini) in the
/// OpenAI function-calling / tool-use format, enabling the model
/// to autonomously invoke HCEP capabilities during a conversation.
/// </summary>
public static class AgenticToolDefinitions
{
    /// <summary>
    /// Gets the complete set of HCEP tools available for agentic LLM invocation.
    /// </summary>
    public static IReadOnlyList<AgenticTool> GetHCEPTools() =>
    [
        new AgenticTool
        {
            Type = "function",
            Function = new AgenticFunction
            {
                Name = "query_knowledge",
                Description = "Query the HCEP knowledge store for facts about a person or entity. Returns stored triples (subject, relation, object).",
                Parameters = new AgenticParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, AgenticProperty>
                    {
                        ["subject"] = new() { Type = "string", Description = "The entity to query (e.g., person name)" },
                        ["relation"] = new() { Type = "string", Description = "Optional relation filter (e.g., 'lastMode', 'said'). If empty, returns all." },
                    },
                    Required = ["subject"],
                },
            },
        },

        new AgenticTool
        {
            Type = "function",
            Function = new AgenticFunction
            {
                Name = "get_hcep_state",
                Description = "Get the current HCEP mode state for the person being tracked. Returns the active communication mode (LOGIC, AFFECT, SPIRIT, HEART, THINK), gaze region, cognitive state, and confidence.",
                Parameters = new AgenticParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, AgenticProperty>
                    {
                        ["person_name"] = new() { Type = "string", Description = "Person name (optional — defaults to primary tracked person)" },
                    },
                    Required = [],
                },
            },
        },

        new AgenticTool
        {
            Type = "function",
            Function = new AgenticFunction
            {
                Name = "store_knowledge",
                Description = "Assert a new fact into the HCEP knowledge store as a subject-relation-object triple.",
                Parameters = new AgenticParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, AgenticProperty>
                    {
                        ["subject"] = new() { Type = "string", Description = "Subject entity" },
                        ["relation"] = new() { Type = "string", Description = "Relationship type (e.g., 'likes', 'worksAt', 'isA')" },
                        ["object"] = new() { Type = "string", Description = "Object value" },
                    },
                    Required = ["subject", "relation", "object"],
                },
            },
        },

        new AgenticTool
        {
            Type = "function",
            Function = new AgenticFunction
            {
                Name = "summarize_person",
                Description = "Get a natural-language summary of everything known about a person, suitable for context injection.",
                Parameters = new AgenticParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, AgenticProperty>
                    {
                        ["person_name"] = new() { Type = "string", Description = "Name of the person to summarize" },
                        ["max_tokens"] = new() { Type = "integer", Description = "Maximum token budget for summary (default: 200)" },
                    },
                    Required = ["person_name"],
                },
            },
        },

        new AgenticTool
        {
            Type = "function",
            Function = new AgenticFunction
            {
                Name = "analyze_gaze_pattern",
                Description = "Analyze recent gaze patterns to infer engagement level, attention shifts, and conversational intent.",
                Parameters = new AgenticParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, AgenticProperty>
                    {
                        ["window_seconds"] = new() { Type = "number", Description = "Time window to analyze in seconds (default: 10)" },
                    },
                    Required = [],
                },
            },
        },
    ];

    /// <summary>
    /// Serializes the tool definitions to JSON for the OpenAI API request.
    /// </summary>
    public static string ToJson() =>
        JsonSerializer.Serialize(GetHCEPTools(), AgenticJsonContext.Default.IReadOnlyListAgenticTool);
}

// ── Agentic DTOs ───────────────────────────────────────────────

/// <summary>Tool definition in the OpenAI function-calling format.</summary>
public sealed class AgenticTool
{
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public required AgenticFunction Function { get; init; }
}

/// <summary>Function metadata for agentic tool invocation.</summary>
public sealed class AgenticFunction
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("parameters")] public required AgenticParameters Parameters { get; init; }
}

/// <summary>JSON Schema parameters for a tool function.</summary>
public sealed class AgenticParameters
{
    [JsonPropertyName("type")] public string Type { get; init; } = "object";
    [JsonPropertyName("properties")] public Dictionary<string, AgenticProperty> Properties { get; init; } = [];
    [JsonPropertyName("required")] public string[] Required { get; init; } = [];
}

/// <summary>Single parameter property schema.</summary>
public sealed class AgenticProperty
{
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}

/// <summary>Tool call returned by the LLM.</summary>
public sealed class AgenticToolCall
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public AgenticToolCallFunction? Function { get; init; }
}

/// <summary>Function call details within a tool call.</summary>
public sealed class AgenticToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; init; } = "{}";
}

/// <summary>Result of executing an agentic tool call.</summary>
public sealed record AgenticToolResult(string ToolCallId, string Name, string Content);

/// <summary>Serialization context for agentic tool DTOs.</summary>
[JsonSerializable(typeof(IReadOnlyList<AgenticTool>))]
[JsonSerializable(typeof(AgenticToolCall))]
[JsonSerializable(typeof(AgenticToolCallFunction))]
[JsonSerializable(typeof(AgenticToolResult))]
[JsonSerializable(typeof(List<AgenticToolCall>))]
internal sealed partial class AgenticJsonContext : JsonSerializerContext;
