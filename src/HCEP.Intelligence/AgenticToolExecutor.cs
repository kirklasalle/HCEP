// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Text.Json;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Intelligence;

/// <summary>
/// Executes agentic tool calls returned by the cloud LLM (GPT-5-mini).
/// Dispatches to HCEP services (knowledge store, gaze analysis, person tracking)
/// and returns structured results for multi-step reasoning loops.
/// </summary>
public sealed class AgenticToolExecutor
{
    private readonly IKnowledgeStore _knowledge;
    private readonly ILogger<AgenticToolExecutor> _logger;

    // Current HCEP state — injected per-frame by the pipeline orchestrator
    private volatile HcepReading? _currentReading;
    private volatile TrackedPerson? _primaryPerson;

    public AgenticToolExecutor(
        IKnowledgeStore knowledge,
        ILogger<AgenticToolExecutor> logger)
    {
        _knowledge = knowledge;
        _logger = logger;
    }

    /// <summary>
    /// Updates the agentic executor's view of the current HCEP state.
    /// Called by the pipeline orchestrator each frame.
    /// </summary>
    public void UpdateState(HcepReading? reading, TrackedPerson? primaryPerson)
    {
        _currentReading = reading;
        _primaryPerson = primaryPerson;
    }

    /// <summary>
    /// Executes a list of tool calls and returns results for LLM re-injection.
    /// Supports the agentic multi-step reasoning loop.
    /// </summary>
    public Task<IReadOnlyList<AgenticToolResult>> ExecuteAsync(
        IReadOnlyList<AgenticToolCall> toolCalls,
        CancellationToken ct = default)
    {
        var results = new List<AgenticToolResult>(toolCalls.Count);

        foreach (var call in toolCalls)
        {
            if (call.Function is null) continue;

            _logger.LogDebug("Executing agentic tool: {Tool}({Args})",
                call.Function.Name, call.Function.Arguments);

            string result;
            try
            {
                result = call.Function.Name switch
                {
                    "query_knowledge" => ExecuteQueryKnowledge(call.Function.Arguments),
                    "get_hcep_state" => ExecuteGetHcepState(call.Function.Arguments),
                    "store_knowledge" => ExecuteStoreKnowledge(call.Function.Arguments),
                    "summarize_person" => ExecuteSummarizePerson(call.Function.Arguments),
                    "analyze_gaze_pattern" => ExecuteAnalyzeGazePattern(call.Function.Arguments),
                    _ => $"{{\"error\": \"Unknown tool: {call.Function.Name}\"}}",
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agentic tool execution failed: {Tool}", call.Function.Name);
                result = $"{{\"error\": \"{ex.Message}\"}}";
            }

            results.Add(new AgenticToolResult(call.Id, call.Function.Name, result));
        }

        return Task.FromResult<IReadOnlyList<AgenticToolResult>>(results);
    }

    // ── Tool implementations ───────────────────────────────────

    private string ExecuteQueryKnowledge(string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var root = doc.RootElement;

        string subject = root.GetProperty("subject").GetString() ?? "";
        string? relation = root.TryGetProperty("relation", out var rel)
            ? rel.GetString() : null;

        if (string.IsNullOrEmpty(relation))
        {
            var all = _knowledge.QueryAll(subject);
            return JsonSerializer.Serialize(new
            {
                subject,
                facts = all.Select(f => new { f.Relation, f.Object }).ToArray(),
                count = all.Count,
            });
        }

        var results = _knowledge.Query(subject, relation);
        return JsonSerializer.Serialize(new
        {
            subject,
            relation,
            values = results,
            count = results.Count,
        });
    }

    private string ExecuteGetHcepState(string argsJson)
    {
        var reading = _currentReading;
        var person = _primaryPerson;

        if (reading is null)
            return "{\"state\": \"no_tracking_data\", \"message\": \"No HCEP data currently available.\"}";

        return JsonSerializer.Serialize(new
        {
            mode = reading.Mode.ToString(),
            gaze_region = reading.Region.ToString(),
            cognitive_state = reading.Cognitive.ToString(),
            emotional_valence = reading.Valence.ToString(),
            confidence = reading.Confidence,
            person_name = person?.IdentityName ?? "Unknown",
            tracking_state = person?.State.ToString() ?? "None",
        });
    }

    private string ExecuteStoreKnowledge(string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var root = doc.RootElement;

        string subject = root.GetProperty("subject").GetString() ?? "";
        string relation = root.GetProperty("relation").GetString() ?? "";
        string obj = root.GetProperty("object").GetString() ?? "";

        _knowledge.Assert(subject, relation, obj);

        return JsonSerializer.Serialize(new
        {
            stored = true,
            subject,
            relation,
            @object = obj,
            total_facts = _knowledge.Count,
        });
    }

    private string ExecuteSummarizePerson(string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var root = doc.RootElement;

        string personName = root.GetProperty("person_name").GetString() ?? "";
        int maxTokens = root.TryGetProperty("max_tokens", out var mt)
            ? mt.GetInt32() : 200;

        string summary = _knowledge.Summarize(personName, maxTokens);

        return JsonSerializer.Serialize(new
        {
            person = personName,
            summary,
            facts_count = _knowledge.QueryAll(personName).Count,
        });
    }

    private string ExecuteAnalyzeGazePattern(string argsJson)
    {
        // Gaze pattern analysis — provides current-frame summary
        // (full temporal analysis requires GazeHistory buffer — deferred to v0.2)
        var reading = _currentReading;
        if (reading is null)
            return "{\"analysis\": \"No gaze data available.\"}";

        return JsonSerializer.Serialize(new
        {
            current_region = reading.Region.ToString(),
            mode = reading.Mode.ToString(),
            confidence = reading.Confidence,
            cognitive = reading.Cognitive.ToString(),
            analysis = reading.Mode switch
            {
                Core.Enums.HcepMode.Logic => "User is in analytical focus — sustained gaze with minimal saccades.",
                Core.Enums.HcepMode.Affect => "Emotional engagement detected — gaze patterns show affective processing.",
                Core.Enums.HcepMode.Spirit => "Deep rapport — mutual gaze with extended fixation periods.",
                Core.Enums.HcepMode.Heart => "Empathic connection — gaze patterns indicate emotional resonance.",
                Core.Enums.HcepMode.Think => "Internal processing — gaze aversion with cognitive load indicators.",
                _ => "Insufficient data for pattern analysis.",
            },
            note = "Full temporal gaze history analysis available in v0.2.",
        });
    }
}
