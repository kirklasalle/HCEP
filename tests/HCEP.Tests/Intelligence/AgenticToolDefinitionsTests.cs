// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Text.Json;
using HCEP.Intelligence;

namespace HCEP.Tests.Intelligence;

public sealed class AgenticToolDefinitionsTests
{
    [Fact]
    public void GetHCEPTools_ReturnsFiveTools()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        Assert.Equal(5, tools.Count);
    }

    [Fact]
    public void GetHCEPTools_AllHaveFunctionType()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        Assert.All(tools, t => Assert.Equal("function", t.Type));
    }

    [Fact]
    public void GetHCEPTools_AllHaveNonEmptyNames()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        Assert.All(tools, t => Assert.NotEmpty(t.Function.Name));
    }

    [Fact]
    public void GetHCEPTools_AllHaveDescriptions()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        Assert.All(tools, t => Assert.NotEmpty(t.Function.Description));
    }

    [Fact]
    public void GetHCEPTools_ContainsExpectedToolNames()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        var names = tools.Select(t => t.Function.Name).ToHashSet();

        Assert.Contains("query_knowledge", names);
        Assert.Contains("get_hcep_state", names);
        Assert.Contains("store_knowledge", names);
        Assert.Contains("summarize_person", names);
        Assert.Contains("analyze_gaze_pattern", names);
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        string json = AgenticToolDefinitions.ToJson();

        Assert.NotEmpty(json);
        // Should parse without throwing
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void StoreKnowledge_RequiresThreeParams()
    {
        var tools = AgenticToolDefinitions.GetHCEPTools();
        var storeTool = tools.First(t => t.Function.Name == "store_knowledge");

        Assert.Equal(3, storeTool.Function.Parameters.Required.Length);
        Assert.Contains("subject", storeTool.Function.Parameters.Required);
        Assert.Contains("relation", storeTool.Function.Parameters.Required);
        Assert.Contains("object", storeTool.Function.Parameters.Required);
    }
}
