// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using HCEP.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HCEP.Knowledge;

/// <summary>
/// In-memory triple-store knowledge graph.
/// Implements <see cref="IKnowledgeStore"/> with a simple Subject→(Relation, Object)
/// representation. Designed as the HCEP adapter layer — can be backed by UKS
/// (BrainSim III) or operate standalone.
/// </summary>
public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly ILogger<InMemoryKnowledgeStore> _logger;
    private readonly ConcurrentDictionary<string, HashSet<(string Relation, string Object)>> _graph = new();
    private readonly object _writeLock = new();

    public InMemoryKnowledgeStore(ILogger<InMemoryKnowledgeStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            int total = 0;
            foreach (var kvp in _graph)
                lock (kvp.Value) { total += kvp.Value.Count; }
            return total;
        }
    }

    /// <inheritdoc />
    public void Assert(string subject, string relation, string obj)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(relation);
        ArgumentException.ThrowIfNullOrWhiteSpace(obj);

        var set = _graph.GetOrAdd(subject, _ => new HashSet<(string, string)>());
        lock (set)
        {
            set.Add((relation, obj));
        }

        _logger.LogTrace("Assert: ({Subject}, {Relation}, {Object})", subject, relation, obj);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Query(string subject, string relation)
    {
        if (!_graph.TryGetValue(subject, out var set))
            return [];

        lock (set)
        {
            return set
                .Where(t => t.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Object)
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Relation, string Object)> QueryAll(string subject)
    {
        if (!_graph.TryGetValue(subject, out var set))
            return [];

        lock (set)
        {
            return set.ToList();
        }
    }

    /// <inheritdoc />
    public bool Exists(string subject, string relation, string obj)
    {
        if (!_graph.TryGetValue(subject, out var set))
            return false;

        lock (set)
        {
            return set.Contains((relation, obj));
        }
    }

    /// <inheritdoc />
    public bool Retract(string subject, string relation, string obj)
    {
        if (!_graph.TryGetValue(subject, out var set))
            return false;

        lock (set)
        {
            bool removed = set.Remove((relation, obj));
            if (removed)
                _logger.LogTrace("Retract: ({Subject}, {Relation}, {Object})", subject, relation, obj);
            return removed;
        }
    }

    /// <inheritdoc />
    public string Summarize(string subject, int maxTokens = 200)
    {
        if (!_graph.TryGetValue(subject, out var set))
            return $"No knowledge about '{subject}'.";

        var sb = new StringBuilder();
        sb.Append($"Knowledge about {subject}: ");

        lock (set)
        {
            int tokenEstimate = 0;
            foreach (var (relation, obj) in set)
            {
                string fact = $"{subject} {relation} {obj}. ";
                int tokens = fact.Length / 4; // rough token estimate
                if (tokenEstimate + tokens > maxTokens) break;
                sb.Append(fact);
                tokenEstimate += tokens;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc />
    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        var serializable = new Dictionary<string, List<string[]>>();

        foreach (var kvp in _graph)
        {
            lock (kvp.Value)
            {
                serializable[kvp.Key] = kvp.Value
                    .Select(t => new[] { t.Relation, t.Object })
                    .ToList();
            }
        }

        var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await File.WriteAllTextAsync(path, json, ct);
        _logger.LogInformation("Knowledge store saved to {Path} ({Count} triples)", path, Count);
    }

    /// <inheritdoc />
    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("Knowledge store file not found: {Path}", path);
            return;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var data = JsonSerializer.Deserialize<Dictionary<string, List<string[]>>>(json);

        if (data is null) return;

        _graph.Clear();
        foreach (var (subject, triples) in data)
        {
            _graph[subject] = new HashSet<(string, string)>(
                triples
                    .Where(t => t.Length >= 2)
                    .Select(t => (t[0], t[1])));
        }

        _logger.LogInformation("Knowledge store loaded from {Path} ({Count} triples)", path, Count);
    }
}
