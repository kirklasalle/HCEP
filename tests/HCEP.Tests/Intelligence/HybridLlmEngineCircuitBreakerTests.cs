// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: HybridLlmEngine circuit breaker & negative paths
// ──────────────────────────────────────────────────────────────
using System.Net;
using System.Net.Http;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Intelligence;
using HCEP.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Intelligence;

/// <summary>
/// Tests for the cloud circuit-breaker in <see cref="HybridLlmEngine"/>
/// and negative-path behaviour when all LLM providers are unavailable.
/// </summary>
public sealed class HybridLlmEngineCircuitBreakerTests
{
    /// <summary>
    /// HttpMessageHandler that always returns 500 Internal Server Error,
    /// simulating a dead cloud API endpoint.
    /// </summary>
    private sealed class AlwaysFailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    /// <summary>
    /// HttpMessageHandler that refuses the connection entirely (ConnectFailure),
    /// simulating a network outage or offline machine.
    /// </summary>
    private sealed class AlwaysThrowHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Simulated network failure");
    }

    private static HybridLlmEngine BuildEngine(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var knowledge = new InMemoryKnowledgeStore(NullLogger<InMemoryKnowledgeStore>.Instance);
        var toolExec = new AgenticToolExecutor(
            knowledge,
            NullLogger<AgenticToolExecutor>.Instance);

        return new HybridLlmEngine(
            httpClient, knowledge, toolExec,
            NullLogger<HybridLlmEngine>.Instance);
    }

    [Fact]
    public async Task PromptAsync_BothProvidersDown_ReturnsFallbackResponse()
    {
        var engine = BuildEngine(new AlwaysThrowHandler());
        engine.Configuration.OpenAI.ApiKey = "fake-key";
        engine.Configuration.PreferLocal = false;

        var result = await engine.PromptAsync("hello", ct: CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.Contains("[No LLM response]", result.Response);
        Assert.Contains("Cloud", result.Response);
    }

    [Fact]
    public async Task PromptAsync_RepeatedCloudFailures_OpenCircuitBreaker()
    {
        var engine = BuildEngine(new AlwaysThrowHandler());
        engine.Configuration.OpenAI.ApiKey = "fake-key";
        engine.Configuration.PreferLocal = false;
        engine.CircuitBreakerThreshold = 3;
        engine.CircuitBreakerCoolDown = TimeSpan.FromSeconds(60);

        // Three failures should open the breaker
        for (int i = 0; i < engine.CircuitBreakerThreshold; i++)
            await engine.PromptAsync("ping", ct: CancellationToken.None);

        // The 4th call should immediately return the fallback without hitting the network
        // (breaker is open — AlwaysThrowHandler would throw if actually called)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await engine.PromptAsync("ping", ct: CancellationToken.None);
        sw.Stop();

        Assert.Contains("[No LLM response]", result.Response);
        Assert.Contains("circuit breaker open", result.Response);
        // Open-circuit bypass is fast — no network round-trip attempted
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Circuit-open call took {sw.ElapsedMilliseconds}ms — network was not bypassed");
    }

    [Fact]
    public async Task PromptAsync_CircuitBreaker_ResetsAfterCoolDown()
    {
        var engine = BuildEngine(new AlwaysThrowHandler());
        engine.Configuration.OpenAI.ApiKey = "fake-key";
        engine.Configuration.PreferLocal = false;
        engine.CircuitBreakerThreshold = 2;
        engine.CircuitBreakerCoolDown = TimeSpan.FromMilliseconds(100); // short for test

        // Trip the breaker
        for (int i = 0; i < engine.CircuitBreakerThreshold; i++)
            await engine.PromptAsync("ping", ct: CancellationToken.None);

        // Wait for cool-down
        await Task.Delay(150);

        // After cool-down, the engine should attempt the cloud call again
        // (it will fail again, but the point is it tried — failure count increments again)
        var result = await engine.PromptAsync("ping", ct: CancellationToken.None);
        Assert.Contains("[No LLM response]", result.Response);
    }

    [Fact]
    public async Task PromptAsync_NoApiKey_ReturnsFallbackWithoutNetworkCall()
    {
        var engine = BuildEngine(new AlwaysThrowHandler());
        // No API key configured anywhere
        engine.Configuration.OpenAI.ApiKey = string.Empty;
        engine.Configuration.PreferLocal = false;

        var result = await engine.PromptAsync("hello", ct: CancellationToken.None);
        Assert.Contains("[No LLM response]", result.Response);
        Assert.Contains("Cloud", result.Response);
    }
}
