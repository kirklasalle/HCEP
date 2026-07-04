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
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Plugin.Api.Services;
using HCEP.Plugin.Api.Llm;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HCEP.Plugin.Api;

/// <summary>
/// Hosted service that embeds a Kestrel server inside the HCEP WPF app,
/// exposing REST, WebSockets, and gRPC endpoints on port 5000 (HTTP).
/// </summary>
public sealed class PluginApiServer : IHostedService, IAsyncDisposable
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ITelemetryTrustService _trust;
    private readonly ILogger<PluginApiServer> _logger;
    private WebApplication? _app;

    public PluginApiServer(
        IPipelineOrchestrator orchestrator,
        ITelemetryTrustService trust,
        ILogger<PluginApiServer> logger)
    {
        _orchestrator = orchestrator;
        _trust = trust;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting embedded HCEP Plugin API Server...");

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel to listen on Port 5000 (HTTP only to keep cert management simple)
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(5000);
            });

            // Register services
            builder.Services.AddSingleton(_orchestrator);
            builder.Services.AddGrpc();

            _app = builder.Build();

            // Enable WebSockets
            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            // REST Endpoint: /api/state
            _app.MapGet("/api/state", (IPipelineOrchestrator orch) =>
            {
                var snap = orch.LatestSnapshot;
                return Results.Ok(WrapWithTrust(MapToDto(snap)));
            });

            // REST Endpoint: /api/tools/openai
            _app.MapGet("/api/tools/openai", () =>
            {
                return Results.Ok(HcepLlmAdapters.GetOpenAiSchema());
            });

            // REST Endpoint: /mcp (Model Context Protocol POST handler)
            _app.MapPost("/mcp", async (HttpContext context, IPipelineOrchestrator orch) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                var response = HcepLlmAdapters.HandleMcpRequest(body, orch, MapToDto);
                return Results.Json(response);
            });

            // WebSocket Endpoint: /ws/stream
            _app.Map("/ws/stream", async (HttpContext context, IPipelineOrchestrator orch) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    _logger.LogInformation("WebSocket client connected to /ws/stream.");
                    await HandleWebSocketStreamAsync(webSocket, orch);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });

            // gRPC Endpoint: HcepPluginService
            _app.MapGrpcService<HcepGrpcService>();

            // Start Kestrel non-blocking
            await _app.StartAsync(cancellationToken);
            _logger.LogInformation("HCEP Plugin API Server successfully started on http://*:5000");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HCEP Plugin API Server");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            _logger.LogInformation("Stopping embedded HCEP Plugin API Server...");
            await _app.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private async Task HandleWebSocketStreamAsync(WebSocket webSocket, IPipelineOrchestrator orchestrator)
    {
        Action<SceneSnapshot>? handler = null;
        var tcs = new TaskCompletionSource<bool>();
        bool hadError = false;

        handler = async (snapshot) =>
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var dto = MapToDto(snapshot);
                    var envelope = WrapWithTrust(dto);
                    string json = JsonSerializer.Serialize(envelope);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);

                    await webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: default);
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send WebSocket payload.");
                tcs.TrySetResult(false);
            }
        };

        orchestrator.SnapshotReady += handler;

        // Keep loop alive until WebSocket is closed
        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                // Read messages (mostly discard, we only care about disconnects)
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), default);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket connection error");
            hadError = true;
        }
        finally
        {
            orchestrator.SnapshotReady -= handler;
            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    var closeStatus = hadError
                        ? WebSocketCloseStatus.InternalServerError
                        : WebSocketCloseStatus.NormalClosure;
                    var closeDescription = hadError ? "Internal server error" : "Closing";
                    await webSocket.CloseAsync(closeStatus, closeDescription, default);
                }
                catch { }
            }
            _logger.LogInformation("WebSocket client disconnected.");
        }
    }

    private static object MapToDto(SceneSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                frameNumber = 0,
                personDetected = false
            };
        }

        var person = snapshot.PrimaryPerson;

        return new
        {
            timestamp = snapshot.Timestamp.ToString("O"),
            frameNumber = snapshot.FrameNumber,
            personDetected = person != null,
            primaryPerson = person is null ? null : new
            {
                trackingId = person.TrackingId,
                identityName = person.IdentityName,
                identityConfidence = person.IdentityConfidence,
                headPosition = new { x = person.HeadPosition.X, y = person.HeadPosition.Y, z = person.HeadPosition.Z },
                headRotation = person.Face is null ? null : new { pitch = person.Face.HeadRotation.X, yaw = person.Face.HeadRotation.Y, roll = person.Face.HeadRotation.Z },
                latestHcep = person.LatestHcep is null ? null : new
                {
                    mode = person.LatestHcep.Mode.ToString(),
                    confidence = person.LatestHcep.Confidence,
                    region = person.LatestHcep.Region.ToString(),
                    gazeDirection = new { x = person.LatestHcep.GazeDirection.X, y = person.LatestHcep.GazeDirection.Y, z = person.LatestHcep.GazeDirection.Z }
                }
            }
        };
    }

    /// <summary>
    /// Wraps a payload DTO in a signed trust envelope.
    /// When the PAD trust state is invalid the signature field is null and
    /// signing_state is "invalid" — downstream consumers should degrade to safe mode.
    /// </summary>
    private object WrapWithTrust(object payload)
    {
        string payloadJson = JsonSerializer.Serialize(payload);
        string? signature = _trust.SignPayload(payloadJson);
        return new
        {
            payload,
            trust = new
            {
                signing_state = _trust.State.IsValid ? "valid" : "invalid",
                pad_hash = _trust.State.PadHash,
                key_id = _trust.State.SigningKeyId,
                signature,
            }
        };
    }
}
