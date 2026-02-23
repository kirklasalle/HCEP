// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Knowledge;

public sealed class UksKnowledgeAdapterTests
{
    private readonly UksKnowledgeAdapter _adapter;
    private readonly InMemoryKnowledgeStore _fallback;

    public UksKnowledgeAdapterTests()
    {
        _fallback = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        _adapter = new UksKnowledgeAdapter(
            NullLogger<UksKnowledgeAdapter>.Instance,
            _fallback);
    }

    [Fact]
    public void IsUksLoaded_WithoutDll_ReturnsFalse()
    {
        // In test environment, UKS.dll is not present
        Assert.False(_adapter.IsUksLoaded);
    }

    [Fact]
    public void Assert_FallbackMode_StoresInMemory()
    {
        _adapter.Assert("Alice", "isA", "Person");

        Assert.Equal(1, _adapter.Count);
        Assert.True(_adapter.Exists("Alice", "isA", "Person"));
    }

    [Fact]
    public void Query_FallbackMode_ReturnsCorrectResults()
    {
        _adapter.Assert("Alice", "likes", "Coffee");
        _adapter.Assert("Alice", "likes", "Tea");

        var results = _adapter.Query("Alice", "likes");

        Assert.Equal(2, results.Count);
        Assert.Contains("Coffee", results);
    }

    [Fact]
    public void QueryAll_FallbackMode_ReturnsAllRelations()
    {
        _adapter.Assert("Bob", "isA", "Person");
        _adapter.Assert("Bob", "age", "30");

        var all = _adapter.QueryAll("Bob");

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Retract_FallbackMode_RemovesTriple()
    {
        _adapter.Assert("Alice", "isA", "Person");
        bool removed = _adapter.Retract("Alice", "isA", "Person");

        Assert.True(removed);
        Assert.Equal(0, _adapter.Count);
    }

    [Fact]
    public void Summarize_FallbackMode_DelegatesToMirror()
    {
        _adapter.Assert("Alice", "isA", "Person");
        string summary = _adapter.Summarize("Alice");

        Assert.Contains("Alice", summary);
    }

    [Fact]
    public async Task SaveLoad_FallbackMode_Roundtrips()
    {
        _adapter.Assert("Alice", "isA", "Person");

        string path = Path.Combine(Path.GetTempPath(), $"HCEP_uks_test_{Guid.NewGuid()}.json");
        try
        {
            await _adapter.SaveAsync(path);

            var newFallback = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
            var newAdapter = new UksKnowledgeAdapter(
                NullLogger<UksKnowledgeAdapter>.Instance,
                newFallback);
            await newAdapter.LoadAsync(path);

            Assert.Equal(1, newAdapter.Count);
            Assert.True(newAdapter.Exists("Alice", "isA", "Person"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var fallback = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        var adapter = new UksKnowledgeAdapter(NullLogger<UksKnowledgeAdapter>.Instance, fallback);
        adapter.Assert("Test", "isA", "Test");

        var ex = Record.Exception(() => adapter.Dispose());
        Assert.Null(ex);
    }
}
