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
using HCEP.Core.Interfaces;
using HCEP.Core.Models;

namespace HCEP.Knowledge;

/// <summary>
/// Manages person-specific knowledge — enrolls recognized identities
/// and accumulates conversational context per person.
/// </summary>
public sealed class PersonKnowledgeManager
{
    private readonly IKnowledgeStore _store;

    public PersonKnowledgeManager(IKnowledgeStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Records that a person has been seen and recognized.
    /// </summary>
    public void RecordSighting(TrackedPerson person)
    {
        if (person.IdentityName is null) return;

        _store.Assert(person.IdentityName, "lastSeen", DateTimeOffset.UtcNow.ToString("O"));
        _store.Assert(person.IdentityName, "isA", "Person");

        if (person.LatestHcep is not null)
        {
            _store.Assert(person.IdentityName, "lastMode", person.LatestHcep.Mode.ToString());
        }
    }

    /// <summary>
    /// Associates a speech utterance with a person.
    /// </summary>
    public void RecordUtterance(string personName, SpeechResult speech)
    {
        _store.Assert(personName, "said", speech.Text);
        _store.Assert(personName, "lastSpoke", speech.Timestamp.ToString("O"));
    }

    /// <summary>
    /// Gets a context summary for a person suitable for LLM prompt injection.
    /// </summary>
    public string GetPersonContext(string personName, int maxTokens = 200)
    {
        return _store.Summarize(personName, maxTokens);
    }

    /// <summary>
    /// Records an LLM exchange associated with a person.
    /// </summary>
    public void RecordExchange(string personName, LlmExchange exchange)
    {
        if (exchange.Response is not null)
        {
            _store.Assert(personName, "wasAsked", exchange.UserMessage);
            _store.Assert(personName, "wasAnswered", exchange.Response);
        }
    }
}
