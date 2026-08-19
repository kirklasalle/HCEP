// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Knowledge;

public sealed class InMemoryKnowledgeStoreTests
{
    private readonly InMemoryKnowledgeStore _store = new(NullLogger<InMemoryKnowledgeStore>.Instance);

    [Fact]
    public void Assert_SingleTriple_IncrementsCount()
    {
        _store.Assert("Alice", "isA", "Person");
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Assert_DuplicateTriple_DoesNotDuplicate()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "isA", "Person");
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Assert_MultipleRelations_TracksAll()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");
        _store.Assert("Alice", "worksAt", "HCEP Labs");
        Assert.Equal(3, _store.Count);
    }

    [Fact]
    public void Query_ExistingRelation_ReturnsObjects()
    {
        _store.Assert("Alice", "likes", "Coffee");
        _store.Assert("Alice", "likes", "Tea");
        _store.Assert("Alice", "isA", "Person");

        var results = _store.Query("Alice", "likes");

        Assert.Equal(2, results.Count);
        Assert.Contains("Coffee", results);
        Assert.Contains("Tea", results);
    }

    [Fact]
    public void Query_NonExistentSubject_ReturnsEmpty()
    {
        var results = _store.Query("Nobody", "isA");
        Assert.Empty(results);
    }

    [Fact]
    public void Query_NonExistentRelation_ReturnsEmpty()
    {
        _store.Assert("Alice", "isA", "Person");
        var results = _store.Query("Alice", "hates");
        Assert.Empty(results);
    }

    [Fact]
    public void QueryAll_ReturnsAllRelations()
    {
        _store.Assert("Bob", "isA", "Person");
        _store.Assert("Bob", "likes", "Music");
        _store.Assert("Bob", "age", "30");

        var all = _store.QueryAll("Bob");

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Exists_PresentTriple_ReturnsTrue()
    {
        _store.Assert("Alice", "isA", "Person");
        Assert.True(_store.Exists("Alice", "isA", "Person"));
    }

    [Fact]
    public void Exists_AbsentTriple_ReturnsFalse()
    {
        Assert.False(_store.Exists("Alice", "isA", "Robot"));
    }

    [Fact]
    public void Retract_PresentTriple_RemovesAndReturnsTrue()
    {
        _store.Assert("Alice", "isA", "Person");
        bool removed = _store.Retract("Alice", "isA", "Person");

        Assert.True(removed);
        Assert.Equal(0, _store.Count);
        Assert.False(_store.Exists("Alice", "isA", "Person"));
    }

    [Fact]
    public void Retract_AbsentTriple_ReturnsFalse()
    {
        bool removed = _store.Retract("Nobody", "isA", "Person");
        Assert.False(removed);
    }

    [Fact]
    public void Summarize_ExistingSubject_ReturnsNonEmpty()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");

        string summary = _store.Summarize("Alice");

        Assert.Contains("Alice", summary);
        Assert.NotEmpty(summary);
    }

    [Fact]
    public void Summarize_NonExistentSubject_ReturnsNoKnowledge()
    {
        string summary = _store.Summarize("Nobody");
        Assert.Contains("No knowledge", summary);
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip_PreservesData()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");
        _store.Assert("Bob", "isA", "Person");

        string path = Path.Combine(Path.GetTempPath(), $"HCEP_test_{Guid.NewGuid()}.json");
        try
        {
            await _store.SaveAsync(path);
            Assert.True(File.Exists(path));

            var loaded = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
            await loaded.LoadAsync(path);

            Assert.Equal(3, loaded.Count);
            Assert.True(loaded.Exists("Alice", "isA", "Person"));
            Assert.True(loaded.Exists("Alice", "likes", "Coffee"));
            Assert.True(loaded.Exists("Bob", "isA", "Person"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_NonExistentFile_DoesNotThrow()
    {
        await _store.LoadAsync(@"C:\nonexistent\path\file.json");
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Assert_NullSubject_Throws()
    {
        Assert.Throws<ArgumentException>(() => _store.Assert("", "isA", "Person"));
    }

    [Fact]
    public void Erase_RemovesAllFactsForSubject()
    {
        _store.Assert("Alice", "isA", "Person");
        _store.Assert("Alice", "likes", "Coffee");
        _store.Assert("Bob", "isA", "Person");

        Assert.Equal(3, _store.Count);

        _store.Erase("Alice");

        Assert.Equal(1, _store.Count);
        Assert.False(_store.Exists("Alice", "isA", "Person"));
        Assert.False(_store.Exists("Alice", "likes", "Coffee"));
        Assert.True(_store.Exists("Bob", "isA", "Person"));
    }

    [Fact]
    public async Task PurgeExpired_RemovesOnlyExpiredFacts()
    {
        _store.Assert("Alice", "isA", "Person"); // asserted now (current time)

        // Wait sufficiently for timer tick across all Windows scheduler quanta
        await Task.Delay(30);

        _store.PurgeExpired(TimeSpan.FromMilliseconds(5));

        // It should have been purged because maxAge is 5ms and it is older than that
        Assert.Equal(0, _store.Count);
    }
}
