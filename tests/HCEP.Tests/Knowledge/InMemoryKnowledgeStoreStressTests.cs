// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: InMemoryKnowledgeStore concurrency & negative-path
// ──────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Knowledge;

/// <summary>
/// Stress and negative-path tests for <see cref="InMemoryKnowledgeStore"/>.
/// Covers: concurrent access, capacity limits, input validation, eviction.
/// </summary>
public sealed class InMemoryKnowledgeStoreStressTests
{
    // ── Concurrency ────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAssert_FromManyThreads_DoesNotCorruptCount()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        const int threads = 8;
        const int assertsPerThread = 100;

        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < assertsPerThread; i++)
                store.Assert($"subject-{t}", "rel", $"obj-{i}");
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(threads * assertsPerThread, store.Count);
    }

    [Fact]
    public async Task ConcurrentAssertAndQuery_DoNotDeadlock()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        for (int i = 0; i < 20; i++)
            store.Assert("Alice", "item", $"value-{i}");

        var errors = new ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var writers = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            int n = 0;
            while (!cts.IsCancellationRequested)
                store.Assert("Alice", "item", $"w-{n++}");
        })).ToArray();

        var readers = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { var r = store.Query("Alice", "item"); }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();

        cts.CancelAfter(2000);
        await Task.WhenAll([.. writers, .. readers]);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ConcurrentQueryAll_DoesNotThrow()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        for (int i = 0; i < 50; i++)
            store.Assert("Bob", $"rel-{i}", $"obj-{i}");

        var errors = new ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 10).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                try { var r = store.QueryAll("Bob"); }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(errors);
    }

    // ── Capacity Limits (G — negative-path) ───────────────────

    [Fact]
    public void Assert_WhenSubjectLimitReached_DropsNewSubjectWithoutThrowing()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance)
        {
            MaxSubjects = 3
        };

        store.Assert("S1", "r", "o");
        store.Assert("S2", "r", "o");
        store.Assert("S3", "r", "o");

        // Fourth new subject should be silently dropped (no exception)
        store.Assert("S4", "r", "o");

        Assert.Equal(3, store.Count);
        Assert.Empty(store.Query("S4", "r"));
    }

    [Fact]
    public void Assert_WhenPerSubjectLimitReached_EvictsOldestEntry()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance)
        {
            MaxTriplesPerSubject = 3
        };

        store.Assert("Alice", "r", "obj1");
        store.Assert("Alice", "r", "obj2");
        store.Assert("Alice", "r", "obj3");

        // Fourth assert must evict one entry, keeping total at 3
        store.Assert("Alice", "r", "obj4");

        var all = store.Query("Alice", "r");
        Assert.Equal(3, all.Count);
        Assert.Contains("obj4", all); // newest always kept
    }

    [Fact]
    public void Assert_ExistingKey_UpdatesTimestampWithoutGrowingCount()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance)
        {
            MaxTriplesPerSubject = 2
        };
        store.Assert("Alice", "r", "obj1");
        store.Assert("Alice", "r", "obj2");
        int countBefore = store.Count;

        // Re-asserting an existing triple should update timestamp but NOT grow count
        store.Assert("Alice", "r", "obj1");
        Assert.Equal(countBefore, store.Count);
    }

    // ── Input Validation (G — negative-path) ──────────────────

    [Fact]
    public void Assert_SubjectTooLong_ThrowsArgumentException()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        var tooLong = new string('x', 256); // MaxSubjectLength = 255
        Assert.Throws<ArgumentException>(() => store.Assert(tooLong, "rel", "obj"));
    }

    [Fact]
    public void Assert_RelationTooLong_ThrowsArgumentException()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        var tooLong = new string('x', 101); // MaxRelationLength = 100
        Assert.Throws<ArgumentException>(() => store.Assert("subject", tooLong, "obj"));
    }

    [Fact]
    public void Assert_ObjectTooLong_ThrowsArgumentException()
    {
        var store = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        var tooLong = new string('x', 10_001); // MaxObjectLength = 10_000
        Assert.Throws<ArgumentException>(() => store.Assert("subject", "rel", tooLong));
    }

    [Fact]
    public void Assert_NullSubject_ThrowsArgumentException()
        => Assert.ThrowsAny<ArgumentException>(
            () => new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance)
                      .Assert(null!, "rel", "obj"));

    [Fact]
    public void Assert_EmptyRelation_ThrowsArgumentException()
        => Assert.Throws<ArgumentException>(
            () => new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance)
                      .Assert("subject", "   ", "obj"));
}
