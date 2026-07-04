// ──────────────────────────────────────────────────────────────
// HCEP — Plugin API Integration Tests
// ──────────────────────────────────────────────────────────────

using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Plugin.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HCEP.Tests.Integration;

public sealed class PluginApiTests
{
#pragma warning disable CS0067
    private sealed class FakePipelineOrchestrator : IPipelineOrchestrator
    {
        public bool IsRunning { get; set; } = true;
        public SceneSnapshot? LatestSnapshot { get; set; }
        public double CurrentFps => 10.0;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public event Action<SceneSnapshot>? SnapshotReady;
        public event Action<SpeechResult>? SpeechReady;
        public event Action<ColorFrame>? ColorFrameReady;
        public event Action<DepthFrame>? DepthFrameReady;
        public event Action<ColorFrame>? InfraredFrameReady;
        public event Action<SkeletonFrame>? SkeletonFrameReady;
        public event Action<LlmExchange>? LlmResponseReady;

        public void TriggerSnapshot(SceneSnapshot snapshot)
        {
            LatestSnapshot = snapshot;
            SnapshotReady?.Invoke(snapshot);
        }
    }

    /// <summary>
    /// Stub trust service — always valid, returns a fixed fake signature.
    /// Used in plugin API tests so they don't depend on real PAD file I/O.
    /// </summary>
    private sealed class StubTrustService : ITelemetryTrustService
    {
        public TelemetryTrustState State { get; } = new TelemetryTrustState
        {
            IsValid = true,
            PadHash = "test0000000000...",
            SigningKeyId = "DEADBEEF",
            BootTimestamp = DateTimeOffset.UtcNow,
        };

        public string? SignPayload(string json) => "stub-signature";
    }
#pragma warning restore CS0067

    [Fact]
    public async Task GetState_ReturnsCorrectSnapshotData()
    {
        // Arrange
        var fakeOrch = new FakePipelineOrchestrator();
        var snapshot = new SceneSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            FrameNumber = 42,
            PrimaryPersonIndex = 0,
            Persons = ImmutableArray.Create(new TrackedPerson
            {
                TrackingId = 1,
                IdentityName = "Alice",
                IdentityConfidence = 0.95f,
                HeadPosition = new Vector3(0.1f, 0.2f, 0.3f),
                LatestHcep = new HcepReading(
                    DateTimeOffset.UtcNow,
                    HcepMode.Logic,
                    GazeRegion.FaceCenter,
                    CognitiveState.Processing,
                    EmotionalValence.Neutral,
                    0.88f,
                    new Vector3(0.1f, 0.2f, 0.3f),
                    new Vector3(0, 0, -1),
                    new Vector3(1, 2, 3),
                    1
                )
            })
        };
        fakeOrch.TriggerSnapshot(snapshot);

        var server = new PluginApiServer(fakeOrch, new StubTrustService(), NullLogger<PluginApiServer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            // Start Server
            await server.StartAsync(cts.Token);

            // Act
            using var client = new HttpClient();
            var response = await client.GetAsync("http://localhost:5000/api/state", cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);

            // Unwrap the PAD-signed trust envelope produced by WrapWithTrust()
            var envelope = json.GetProperty("payload");

            // Assert
            Assert.True(envelope.GetProperty("personDetected").GetBoolean());
            Assert.Equal(42, envelope.GetProperty("frameNumber").GetInt64());

            var person = envelope.GetProperty("primaryPerson");
            Assert.Equal("Alice", person.GetProperty("identityName").GetString());

            var hcep = person.GetProperty("latestHcep");
            Assert.Equal("Logic", hcep.GetProperty("mode").GetString());
            Assert.Equal(0.88, hcep.GetProperty("confidence").GetDouble(), 2);

            // Verify trust envelope is present and valid
            var trust = json.GetProperty("trust");
            Assert.Equal("valid", trust.GetProperty("signing_state").GetString());
            Assert.False(string.IsNullOrEmpty(trust.GetProperty("signature").GetString()));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task WebSocketStream_StreamsSnapshotsRealTime()
    {
        // Arrange
        var fakeOrch = new FakePipelineOrchestrator();
        var server = new PluginApiServer(fakeOrch, new StubTrustService(), NullLogger<PluginApiServer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            // Start Server
            await server.StartAsync(cts.Token);

            // Establish WebSocket Connection
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("ws://localhost:5000/ws/stream"), cts.Token);
            Assert.Equal(WebSocketState.Open, ws.State);

            // Fire Snapshot Event
            var snapshot = new SceneSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                FrameNumber = 99,
                PrimaryPersonIndex = -1,
                Persons = ImmutableArray<TrackedPerson>.Empty
            };

            // Read Task running in background
            var readTask = Task.Run(async () =>
            {
                var buffer = new byte[1024 * 4];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                return JsonSerializer.Deserialize<JsonElement>(json);
            });

            // Trigger snapshot on server
            fakeOrch.TriggerSnapshot(snapshot);

            // Wait for socket read
            var jsonRes = await readTask;

            // Assert
            // WebSocket payload is also wrapped in the trust envelope
            var envelope = jsonRes.GetProperty("payload");
            Assert.Equal(99, envelope.GetProperty("frameNumber").GetInt64());
            Assert.False(envelope.GetProperty("personDetected").GetBoolean());
            Assert.Equal("valid", jsonRes.GetProperty("trust").GetProperty("signing_state").GetString());

            // Close connection
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test done", cts.Token);
            Assert.Equal(WebSocketState.Closed, ws.State);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetOpenAiSchema_ReturnsValidJsonSchema()
    {
        // Arrange
        var fakeOrch = new FakePipelineOrchestrator();
        var server = new PluginApiServer(fakeOrch, new StubTrustService(), NullLogger<PluginApiServer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await server.StartAsync(cts.Token);

            using var client = new HttpClient();
            var response = await client.GetAsync("http://localhost:5000/api/tools/openai", cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);

            Assert.Equal("function", json.GetProperty("type").GetString());
            var func = json.GetProperty("function");
            Assert.Equal("get_hcep_state", func.GetProperty("name").GetString());
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task McpPostListTools_ReturnsMcpToolsList()
    {
        // Arrange
        var fakeOrch = new FakePipelineOrchestrator();
        var server = new PluginApiServer(fakeOrch, new StubTrustService(), NullLogger<PluginApiServer>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await server.StartAsync(cts.Token);

            using var client = new HttpClient();
            var mcpRequest = new { jsonrpc = "2.0", method = "tools/list", id = 1 };
            var response = await client.PostAsJsonAsync("http://localhost:5000/mcp", mcpRequest, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);

            Assert.Equal("2.0", json.GetProperty("jsonrpc").GetString());
            Assert.Equal(1, json.GetProperty("id").GetInt32());
            var result = json.GetProperty("result");
            var tools = result.GetProperty("tools");
            Assert.Equal(1, tools.GetArrayLength());
            Assert.Equal("get_hcep_state", tools[0].GetProperty("name").GetString());
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }
    }
}
