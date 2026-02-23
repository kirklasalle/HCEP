// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using System.Text.Json;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Intelligence;
using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Intelligence;

public sealed class AgenticToolExecutorTests
{
    private readonly AgenticToolExecutor _executor;
    private readonly InMemoryKnowledgeStore _store;

    public AgenticToolExecutorTests()
    {
        _store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        _executor = new AgenticToolExecutor(_store, NullLogger<AgenticToolExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteQueryKnowledge_ReturnsStoredFacts()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");

        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_1",
                Function = new AgenticToolCallFunction
                {
                    Name = "query_knowledge",
                    Arguments = """{"subject": "Alice"}""",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Single(results);
        Assert.Equal("call_1", results[0].ToolCallId);
        Assert.Contains("Alice", results[0].Content);
        Assert.Contains("Person", results[0].Content);
    }

    [Fact]
    public async Task ExecuteQueryKnowledge_WithRelation_FiltersResults()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");

        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_2",
                Function = new AgenticToolCallFunction
                {
                    Name = "query_knowledge",
                    Arguments = """{"subject": "Alice", "relation": "likes"}""",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Single(results);
        Assert.Contains("Coffee", results[0].Content);
        Assert.DoesNotContain("isA", results[0].Content);
    }

    [Fact]
    public async Task ExecuteStoreKnowledge_PersistsFact()
    {
        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_3",
                Function = new AgenticToolCallFunction
                {
                    Name = "store_knowledge",
                    Arguments = """{"subject": "Bob", "relation": "worksAt", "object": "HCEP Labs"}""",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Single(results);
        Assert.Contains("true", results[0].Content);
        Assert.True(_store.Exists("Bob", "worksAt", "HCEP Labs"));
    }

    [Fact]
    public async Task ExecuteGetHcepState_NoData_ReturnsNoTracking()
    {
        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_4",
                Function = new AgenticToolCallFunction
                {
                    Name = "get_hcep_state",
                    Arguments = "{}",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Single(results);
        Assert.Contains("no_tracking_data", results[0].Content);
    }

    [Fact]
    public async Task ExecuteGetHcepState_WithData_ReturnsMode()
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            HcepMode.Logic,
            GazeRegion.LeftEye,
            CognitiveState.Engaged,
            EmotionalValence.Neutral,
            0.85f,
            Vector3.Zero,
            new Vector3(0, 0, 1),
            Vector3.Zero,
            1);

        _executor.UpdateState(reading, null);

        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_5",
                Function = new AgenticToolCallFunction
                {
                    Name = "get_hcep_state",
                    Arguments = "{}",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Contains("Logic", results[0].Content);
        Assert.Contains("Engaged", results[0].Content);
    }

    [Fact]
    public async Task ExecuteSummarizePerson_ReturnsSummary()
    {
        _store.Assert("Kirk", "isA", "Person");
        _store.Assert("Kirk", "invented", "HCEP Theory");

        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_6",
                Function = new AgenticToolCallFunction
                {
                    Name = "summarize_person",
                    Arguments = """{"person_name": "Kirk"}""",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Contains("Kirk", results[0].Content);
    }

    [Fact]
    public async Task ExecuteUnknownTool_ReturnsError()
    {
        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_7",
                Function = new AgenticToolCallFunction
                {
                    Name = "nonexistent_tool",
                    Arguments = "{}",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Contains("error", results[0].Content);
        Assert.Contains("Unknown tool", results[0].Content);
    }

    [Fact]
    public async Task ExecuteMultipleTools_ReturnsAllResults()
    {
        _store.Assert("Alice", "isA", "Person");

        var calls = new List<AgenticToolCall>
        {
            new()
            {
                Id = "call_a",
                Function = new AgenticToolCallFunction
                {
                    Name = "query_knowledge",
                    Arguments = """{"subject": "Alice"}""",
                },
            },
            new()
            {
                Id = "call_b",
                Function = new AgenticToolCallFunction
                {
                    Name = "store_knowledge",
                    Arguments = """{"subject": "Alice", "relation": "likes", "object": "Tea"}""",
                },
            },
        };

        var results = await _executor.ExecuteAsync(calls);

        Assert.Equal(2, results.Count);
        Assert.Equal("call_a", results[0].ToolCallId);
        Assert.Equal("call_b", results[1].ToolCallId);
    }
}
