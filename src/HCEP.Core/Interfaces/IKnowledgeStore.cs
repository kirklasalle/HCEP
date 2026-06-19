// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
namespace HCEP.Core.Interfaces;

/// <summary>
/// Knowledge store abstraction — adapter over UKS (Universal Knowledge Store)
/// or any graph-based knowledge representation.
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>
    /// Stores a fact as a subject-relation-object triple.
    /// </summary>
    void Assert(string subject, string relation, string obj);

    /// <summary>
    /// Queries for objects matching a subject-relation pattern.
    /// </summary>
    IReadOnlyList<string> Query(string subject, string relation);

    /// <summary>
    /// Queries for all relations and objects for a subject.
    /// </summary>
    IReadOnlyList<(string Relation, string Object)> QueryAll(string subject);

    /// <summary>
    /// Checks if a specific triple exists.
    /// </summary>
    bool Exists(string subject, string relation, string obj);

    /// <summary>
    /// Removes a triple from the store.
    /// </summary>
    bool Retract(string subject, string relation, string obj);

    /// <summary>
    /// Erases all facts associated with a subject (GDPR Right to be Forgotten / compliance).
    /// </summary>
    void Erase(string subject);

    /// <summary>
    /// Purges all facts older than the specified retention age.
    /// </summary>
    void PurgeExpired(TimeSpan maxAge);

    /// <summary>
    /// Gets a natural-language summary of knowledge about a subject
    /// suitable for LLM context injection.
    /// </summary>
    string Summarize(string subject, int maxTokens = 200);

    /// <summary>Number of assertions in the store.</summary>
    int Count { get; }

    /// <summary>Persists the knowledge store to disk.</summary>
    Task SaveAsync(string path, CancellationToken ct = default);

    /// <summary>Loads the knowledge store from disk.</summary>
    Task LoadAsync(string path, CancellationToken ct = default);
}
