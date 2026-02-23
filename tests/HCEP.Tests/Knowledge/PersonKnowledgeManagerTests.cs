// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Models;
using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Knowledge;

public sealed class PersonKnowledgeManagerTests
{
    private readonly InMemoryKnowledgeStore _store = new(NullLogger<InMemoryKnowledgeStore>.Instance);
    private readonly PersonKnowledgeManager _manager;

    public PersonKnowledgeManagerTests()
    {
        _manager = new PersonKnowledgeManager(_store);
    }

    [Fact]
    public void RecordSighting_WithName_StoresFacts()
    {
        var person = new TrackedPerson
        {
            TrackingId = 1,
            IdentityName = "Kirk",
        };

        _manager.RecordSighting(person);

        Assert.True(_store.Exists("Kirk", "isA", "Person"));
        var lastSeen = _store.Query("Kirk", "lastSeen");
        Assert.Single(lastSeen);
    }

    [Fact]
    public void RecordSighting_WithNullName_DoesNotStore()
    {
        var person = new TrackedPerson
        {
            TrackingId = 1,
            IdentityName = null,
        };

        _manager.RecordSighting(person);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void RecordUtterance_StoresSpeechFact()
    {
        var speech = new SpeechResult
        {
            Text = "Hello, how are you?",
            Timestamp = DateTimeOffset.UtcNow,
        };

        _manager.RecordUtterance("Kirk", speech);

        Assert.True(_store.Exists("Kirk", "said", "Hello, how are you?"));
    }

    [Fact]
    public void GetPersonContext_ReturnsNonEmptySummary()
    {
        _store.Assert("Kirk", "isA", "Person");
        _store.Assert("Kirk", "likes", "HCEP");

        string context = _manager.GetPersonContext("Kirk");

        Assert.Contains("Kirk", context);
        Assert.NotEmpty(context);
    }

    [Fact]
    public void RecordExchange_StoresQuestionAndAnswer()
    {
        var exchange = new LlmExchange
        {
            UserMessage = "What is HCEP?",
            Response = "HCEP is a theory of eye contact communication.",
        };

        _manager.RecordExchange("Kirk", exchange);

        Assert.True(_store.Exists("Kirk", "wasAsked", "What is HCEP?"));
        Assert.True(_store.Exists("Kirk", "wasAnswered", "HCEP is a theory of eye contact communication."));
    }
}
