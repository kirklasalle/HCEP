// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
//
// Strategy D: Hybrid UKS (Universal Knowledge Store) Adapter
//
// BrainSim III's UKS is a MIT-licensed in-memory knowledge graph
// (Thing → Relationships → Clauses). This adapter wraps UKS behind
// the HCEP IKnowledgeStore interface using late-bound reflection so
// the platform runs cleanly whether or not the UKS assembly is present.
//
// When UKS.dll is detected:
//   - Triples are stored directly in UKS's native Thing + Relationship graph
//   - HCEP benefits from UKS proximity search, type hierarchy, and inference
//
// When UKS.dll is absent:
//   - Delegates to InMemoryKnowledgeStore (no UKS dependency required)
//
// This adapter layer isolates HCEP from tight coupling to UKS internals,
// letting the UKS dependency evolve independently.
// ──────────────────────────────────────────────────────────────

using System.Reflection;
using System.Text;
using HCEP.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HCEP.Knowledge;

/// <summary>
/// UKS (Universal Knowledge Store) adapter — Strategy D hybrid bridge.
/// Late-binds to UKS.dll via reflection. Falls back to
/// <see cref="InMemoryKnowledgeStore"/> when the assembly is absent.
/// </summary>
public sealed class UksKnowledgeAdapter : IKnowledgeStore, IDisposable
{
    private readonly ILogger<UksKnowledgeAdapter> _logger;
    private readonly InMemoryKnowledgeStore _fallback;

    // ── UKS reflection handles ───────────────────────────────
    private readonly Assembly? _uksAssembly;
    private readonly object? _uksInstance;           // UKS module root (MainModule or equivalent)
    private readonly MethodInfo? _addThing;          // AddThing(string label)
    private readonly MethodInfo? _getOrAddThing;     // GetOrAddThing(string label) 
    private readonly MethodInfo? _setRelationship;   // Thing.AddRelationship(Thing target, Thing relType)
    private readonly MethodInfo? _getRelationships;  // Thing.Relationships getter
    private readonly MethodInfo? _deleteThing;       // DeleteThing(Thing t)
    private readonly Type? _thingType;

    private readonly bool _uksAvailable;

    // Mirror graph for persistence (UKS is volatile by default)
    private readonly InMemoryKnowledgeStore _mirror;

    public UksKnowledgeAdapter(
        ILogger<UksKnowledgeAdapter> logger,
        InMemoryKnowledgeStore fallback)
    {
        _logger = logger;
        _fallback = fallback;
        _mirror = fallback; // share the same backing store for persistence

        // ── Attempt late-bind to UKS ─────────────────────────
        _uksAvailable = TryLoadUks(
            out _uksAssembly,
            out _uksInstance,
            out _thingType,
            out _addThing,
            out _getOrAddThing,
            out _setRelationship,
            out _getRelationships,
            out _deleteThing);

        if (_uksAvailable)
            _logger.LogInformation("UKS (BrainSim III) loaded — Strategy D adapter active");
        else
            _logger.LogInformation("UKS.dll not found — using InMemoryKnowledgeStore fallback");
    }

    /// <summary>Whether the UKS assembly was successfully loaded.</summary>
    public bool IsUksLoaded => _uksAvailable;

    // ── IKnowledgeStore ────────────────────────────────────────

    /// <inheritdoc />
    public int Count => _mirror.Count;

    /// <inheritdoc />
    public void Assert(string subject, string relation, string obj)
    {
        // Always persist to the mirror store (serializable fallback)
        _mirror.Assert(subject, relation, obj);

        if (_uksAvailable)
        {
            try
            {
                UksAssert(subject, relation, obj);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UKS Assert failed for ({Subject},{Relation},{Object}) — mirror store has the data",
                    subject, relation, obj);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Query(string subject, string relation)
    {
        // UKS query path — try UKS first for richer inference
        if (_uksAvailable)
        {
            try
            {
                var results = UksQuery(subject, relation);
                if (results.Count > 0) return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UKS Query failed — falling back to mirror");
            }
        }

        return _mirror.Query(subject, relation);
    }

    /// <inheritdoc />
    public IReadOnlyList<(string Relation, string Object)> QueryAll(string subject)
    {
        // Delegate to mirror — UKS QueryAll would need full relationship traversal
        return _mirror.QueryAll(subject);
    }

    /// <inheritdoc />
    public bool Exists(string subject, string relation, string obj)
        => _mirror.Exists(subject, relation, obj);

    /// <inheritdoc />
    public bool Retract(string subject, string relation, string obj)
    {
        bool removed = _mirror.Retract(subject, relation, obj);

        if (_uksAvailable && removed)
        {
            try
            {
                UksRetract(subject, relation, obj);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UKS Retract failed for ({Subject},{Relation},{Object})",
                    subject, relation, obj);
            }
        }

        return removed;
    }

    /// <inheritdoc />
    public void Erase(string subject)
    {
        _mirror.Erase(subject);

        if (_uksAvailable)
        {
            try
            {
                if (_getOrAddThing is not null && _deleteThing is not null && _uksInstance is not null)
                {
                    var subjectThing = _getOrAddThing.Invoke(_uksInstance, [subject]);
                    if (subjectThing is not null)
                    {
                        _deleteThing.Invoke(_uksInstance, [subjectThing]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UKS Erase failed for subject '{Subject}'", subject);
            }
        }
    }

    /// <inheritdoc />
    public void PurgeExpired(TimeSpan maxAge)
    {
        _mirror.PurgeExpired(maxAge);
    }

    /// <inheritdoc />
    public string Summarize(string subject, int maxTokens = 200)
        => _mirror.Summarize(subject, maxTokens);

    /// <inheritdoc />
    public Task SaveAsync(string path, CancellationToken ct = default)
        => _mirror.SaveAsync(path, ct);

    /// <inheritdoc />
    public Task LoadAsync(string path, CancellationToken ct = default)
        => _mirror.LoadAsync(path, ct);

    // ── IDisposable ────────────────────────────────────────────

    public void Dispose()
    {
        // UKS may hold native resources; attempt graceful cleanup
        if (_uksInstance is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "UKS disposal failed"); }
        }
    }

    // ── UKS late-bound operations ──────────────────────────────

    private void UksAssert(string subject, string relation, string obj)
    {
        if (_getOrAddThing is null || _setRelationship is null || _uksInstance is null)
            return;

        // GetOrAddThing creates a Thing node if it doesn't exist
        var subjectThing = _getOrAddThing.Invoke(_uksInstance, [subject]);
        var relThing = _getOrAddThing.Invoke(_uksInstance, [$"rel:{relation}"]);
        var objThing = _getOrAddThing.Invoke(_uksInstance, [obj]);

        if (subjectThing is null || relThing is null || objThing is null)
            return;

        // AddRelationship(target, relType) on the subject Thing
        _setRelationship.Invoke(subjectThing, [objThing, relThing]);
    }

    private IReadOnlyList<string> UksQuery(string subject, string relation)
    {
        if (_getOrAddThing is null || _getRelationships is null || _uksInstance is null)
            return [];

        var subjectThing = _getOrAddThing.Invoke(_uksInstance, [subject]);
        if (subjectThing is null) return [];

        // Get the relationships collection from the subject Thing
        var relationships = _getRelationships.Invoke(subjectThing, null);
        if (relationships is not System.Collections.IEnumerable enumerable)
            return [];

        var results = new List<string>();
        string relLabel = $"rel:{relation}";

        foreach (var rel in enumerable)
        {
            // Each relationship has a RelationshipType (Thing) and Target (Thing)
            var relType = rel.GetType().GetProperty("RelationshipType")?.GetValue(rel);
            var target = rel.GetType().GetProperty("Target")?.GetValue(rel)
                      ?? rel.GetType().GetProperty("T")?.GetValue(rel);

            if (relType is null || target is null) continue;

            var relLabel2 = relType.GetType().GetProperty("Label")?.GetValue(relType)?.ToString();
            if (string.Equals(relLabel2, relLabel, StringComparison.OrdinalIgnoreCase))
            {
                var targetLabel = target.GetType().GetProperty("Label")?.GetValue(target)?.ToString();
                if (targetLabel is not null)
                    results.Add(targetLabel);
            }
        }

        return results;
    }

    private void UksRetract(string subject, string relation, string obj)
    {
        // UKS retraction via reflection — best-effort
        // Complex graph manipulation deferred to future UKS API stabilization
        _logger.LogTrace("UKS retract: ({Subject},{Relation},{Object}) — deferred to mirror store",
            subject, relation, obj);
    }

    // ── Assembly loading ───────────────────────────────────────

    /// <summary>
    /// Attempts to find and load UKS.dll via reflection.
    /// Searches:
    ///   1. Application base directory
    ///   2. "lib" subfolder
    ///   3. HCEP_UKS_PATH environment variable
    /// </summary>
    private bool TryLoadUks(
        out Assembly? assembly,
        out object? instance,
        out Type? thingType,
        out MethodInfo? addThing,
        out MethodInfo? getOrAddThing,
        out MethodInfo? setRelationship,
        out MethodInfo? getRelationships,
        out MethodInfo? deleteThing)
    {
        assembly = null;
        instance = null;
        thingType = null;
        addThing = null;
        getOrAddThing = null;
        setRelationship = null;
        getRelationships = null;
        deleteThing = null;

        // Search paths for UKS.dll
        var searchPaths = new List<string>();

        string baseDir = AppContext.BaseDirectory;
        searchPaths.Add(Path.Combine(baseDir, "UKS.dll"));
        searchPaths.Add(Path.Combine(baseDir, "BrainSimulator.dll"));
        searchPaths.Add(Path.Combine(baseDir, "lib", "UKS.dll"));
        searchPaths.Add(Path.Combine(baseDir, "lib", "BrainSimulator.dll"));

        // Environment variable override
        string? envPath = Environment.GetEnvironmentVariable("HCEP_UKS_PATH");
        if (!string.IsNullOrEmpty(envPath))
            searchPaths.Insert(0, envPath);

        foreach (var path in searchPaths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                assembly = Assembly.LoadFrom(path);
                _logger.LogDebug("Loaded UKS assembly from {Path}", path);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load UKS assembly from {Path}", path);
            }
        }

        if (assembly is null) return false;

        // ── Resolve UKS types ──────────────────────────────
        // BrainSim III structure: namespace BrainSimulator.Modules
        //   class ModuleUKS : ModuleBase
        //   class Thing { string Label; List<Relationship> Relationships; }
        //   class Relationship { Thing RelationshipType; Thing T; }

        thingType = assembly.GetType("BrainSimulator.Thing")
                 ?? assembly.GetType("BrainSimulator.Modules.Thing")
                 ?? assembly.GetType("UKS.Thing");

        if (thingType is null)
        {
            _logger.LogWarning("UKS assembly loaded but Thing type not found — disabling UKS adapter");
            return false;
        }

        // Find the UKS module type
        var moduleType = assembly.GetType("BrainSimulator.Modules.ModuleUKS")
                      ?? assembly.GetType("BrainSimulator.ModuleUKS")
                      ?? assembly.GetType("UKS.ModuleUKS");

        if (moduleType is not null)
        {
            try
            {
                instance = Activator.CreateInstance(moduleType);

                // Bind methods
                addThing = moduleType.GetMethod("AddThing",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, [typeof(string)], null);

                getOrAddThing = moduleType.GetMethod("GetOrAddThing",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, [typeof(string)], null)
                    ?? addThing; // fallback to AddThing which creates-or-gets

                setRelationship = thingType.GetMethod("AddRelationship",
                    BindingFlags.Public | BindingFlags.Instance);

                getRelationships = thingType.GetProperty("Relationships",
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();

                deleteThing = moduleType.GetMethod("DeleteThing",
                    BindingFlags.Public | BindingFlags.Instance);

                _logger.LogDebug("UKS type binding complete — AddThing={AddThing}, Relationships={Rels}",
                    addThing is not null, getRelationships is not null);

                return addThing is not null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UKS module instantiation failed");
                return false;
            }
        }

        _logger.LogWarning("UKS ModuleUKS type not found — disabling UKS adapter");
        return false;
    }
}
