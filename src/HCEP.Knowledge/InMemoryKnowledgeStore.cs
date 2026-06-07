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
    private readonly ConcurrentDictionary<string, Dictionary<(string Relation, string Object), DateTime>> _graph = new();
    private readonly object _writeLock = new();
    private readonly EncryptedStorageProvider _encryptedStorage;

    public InMemoryKnowledgeStore(
        ILogger<InMemoryKnowledgeStore> logger,
        EncryptedStorageProvider? encryptedStorage = null)
    {
        _logger = logger;
        _encryptedStorage = encryptedStorage ?? new EncryptedStorageProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EncryptedStorageProvider>.Instance);
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

        var dict = _graph.GetOrAdd(subject, _ => new Dictionary<(string, string), DateTime>());
        lock (dict)
        {
            dict[(relation, obj)] = DateTime.UtcNow;
        }

        _logger.LogTrace("Assert: ({Subject}, {Relation}, {Object})", subject, relation, obj);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Query(string subject, string relation)
    {
        if (!_graph.TryGetValue(subject, out var dict))
            return [];

        lock (dict)
        {
            return dict.Keys
                .Where(t => t.Relation.Equals(relation, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Object)
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Relation, string Object)> QueryAll(string subject)
    {
        if (!_graph.TryGetValue(subject, out var dict))
            return [];

        lock (dict)
        {
            return dict.Keys.ToList();
        }
    }

    /// <inheritdoc />
    public bool Exists(string subject, string relation, string obj)
    {
        if (!_graph.TryGetValue(subject, out var dict))
            return false;

        lock (dict)
        {
            return dict.ContainsKey((relation, obj));
        }
    }

    /// <inheritdoc />
    public bool Retract(string subject, string relation, string obj)
    {
        if (!_graph.TryGetValue(subject, out var dict))
            return false;

        lock (dict)
        {
            bool removed = dict.Remove((relation, obj));
            if (removed)
                _logger.LogTrace("Retract: ({Subject}, {Relation}, {Object})", subject, relation, obj);
            return removed;
        }
    }

    /// <inheritdoc />
    public void Erase(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return;
        if (_graph.TryRemove(subject, out _))
        {
            _logger.LogInformation("Erase: all knowledge for subject '{Subject}' has been purged.", subject);
        }
    }

    /// <inheritdoc />
    public void PurgeExpired(TimeSpan maxAge)
    {
        DateTime cutoff = DateTime.UtcNow - maxAge;
        var subjectsToRemove = new List<string>();

        foreach (var kvp in _graph)
        {
            var dict = kvp.Value;
            lock (dict)
            {
                var expiredKeys = dict
                    .Where(e => e.Value < cutoff)
                    .Select(e => e.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    dict.Remove(key);
                    _logger.LogTrace("Purged expired fact: ({Subject}, {Relation}, {Object})", kvp.Key, key.Relation, key.Object);
                }

                if (dict.Count == 0)
                {
                    subjectsToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var sub in subjectsToRemove)
        {
            _graph.TryRemove(sub, out _);
        }
    }

    /// <inheritdoc />
    public string Summarize(string subject, int maxTokens = 200)
    {
        if (!_graph.TryGetValue(subject, out var dict))
            return $"No knowledge about '{subject}'.";

        var sb = new StringBuilder();
        sb.Append($"Knowledge about {subject}: ");

        lock (dict)
        {
            int tokenEstimate = 0;
            foreach (var (relation, obj) in dict.Keys)
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
                    .Select(t => new[] { t.Key.Relation, t.Key.Object, t.Value.ToString("O") })
                    .ToList();
            }
        }

        var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await _encryptedStorage.SaveEncryptedAsync(path, json, ct);
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

        var json = await _encryptedStorage.LoadEncryptedAsync(path, ct);
        if (string.IsNullOrWhiteSpace(json)) return;

        var data = JsonSerializer.Deserialize<Dictionary<string, List<string[]>>>(json);

        if (data is null) return;

        _graph.Clear();
        foreach (var (subject, triples) in data)
        {
            var dict = new Dictionary<(string, string), DateTime>();
            foreach (var t in triples)
            {
                if (t.Length >= 2)
                {
                    DateTime ts = DateTime.UtcNow;
                    if (t.Length >= 3 && DateTime.TryParse(t[2], out var parsedTs))
                    {
                        ts = parsedTs;
                    }
                    dict[(t[0], t[1])] = ts;
                }
            }
            _graph[subject] = dict;
        }

        _logger.LogInformation("Knowledge store loaded from {Path} ({Count} triples)", path, Count);
    }
}
