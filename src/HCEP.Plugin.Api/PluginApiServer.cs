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
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Diagnostics;
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
    private readonly ITelemetryService? _telemetry;
    private readonly ILogger<PluginApiServer> _logger;
    private WebApplication? _app;
    private readonly int _port;
    private readonly string _bindAddress;
    private readonly string? _apiKey;

    public PluginApiServer(
        IPipelineOrchestrator orchestrator,
        ITelemetryTrustService trust,
        ILogger<PluginApiServer> logger,
        ITelemetryService? telemetry = null)
    {
        _orchestrator = orchestrator;
        _trust = trust;
        _telemetry = telemetry;
        _logger = logger;
        _port = ResolvePort(logger);
        _bindAddress = Environment.GetEnvironmentVariable("HCEP_PLUGIN_BIND") ?? "0.0.0.0";
        _apiKey = Environment.GetEnvironmentVariable("HCEP_PLUGIN_API_KEY");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting embedded HCEP Plugin API Server...");

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel using env-configurable bind + port.
            builder.WebHost.ConfigureKestrel(options =>
            {
                if (IPAddress.TryParse(_bindAddress, out var ip))
                    options.Listen(ip, _port);
                else
                    options.ListenAnyIP(_port);
            });

            // Register services
            builder.Services.AddSingleton(_orchestrator);
            builder.Services.AddGrpc();

            _app = builder.Build();

            _app.Use(async (context, next) =>
            {
                string correlationId = ResolveOrCreateCorrelationId(context);
                context.Items["correlation_id"] = correlationId;
                context.Response.Headers["X-Correlation-ID"] = correlationId;
                context.Response.Headers["X-HCEP-Plugin-Port"] = _port.ToString();

                using var correlationScope = CorrelationContext.BeginScope(correlationId);
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["CorrelationId"] = correlationId
                });

                _telemetry?.Increment("correlation.plugin.requests");
                _telemetry?.RecordGauge("correlation.plugin.last_hash", CorrelationContext.ToNumericFingerprint(correlationId));

                if (string.IsNullOrWhiteSpace(_apiKey) || context.Request.Path == "/health")
                {
                    await next();
                    return;
                }

                string? bearer = ExtractApiKey(context);
                if (!IsAuthorized(bearer))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "plugin_api_unauthorized",
                        detail = "Set Authorization: Bearer <token> or api_key querystring to access authenticated HCEP plugin endpoints."
                    });
                    return;
                }

                await next();
            });

            // Enable WebSockets
            _app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            });

            _app.MapGet("/health", (HttpContext context) => Results.Ok(new
            {
                service = "HCEP Plugin API",
                correlation_id = GetCorrelationIdFromContext(context),
                bind = _bindAddress,
                port = _port,
                auth = string.IsNullOrWhiteSpace(_apiKey) ? "disabled" : "bearer-or-query-api-key",
                orchestrator_running = _orchestrator.IsRunning,
                has_snapshot = _orchestrator.LatestSnapshot is not null,
                trust_state = _trust.State.IsValid ? "valid" : "invalid",
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            }));

            // REST Endpoint: /api/state
            _app.MapGet("/api/state", (HttpContext context, IPipelineOrchestrator orch) =>
            {
                var snap = orch.LatestSnapshot;
                return Results.Ok(WrapWithTrust(MapToDto(snap), GetCorrelationIdFromContext(context)));
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
                    string correlationId = GetCorrelationIdFromContext(context);
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    _logger.LogInformation("WebSocket client connected to /ws/stream.");
                    await HandleWebSocketStreamAsync(webSocket, orch, correlationId);
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
            _logger.LogInformation("HCEP Plugin API Server successfully started on http://{Bind}:{Port} (auth={Auth})",
                _bindAddress, _port, string.IsNullOrWhiteSpace(_apiKey) ? "disabled" : "enabled");
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

    private async Task HandleWebSocketStreamAsync(WebSocket webSocket, IPipelineOrchestrator orchestrator, string correlationId)
    {
        Action<SceneSnapshot>? handler = null;
        var tcs = new TaskCompletionSource<bool>();
        bool hadError = false;

        handler = async (snapshot) =>
        {
            using var correlationScope = CorrelationContext.BeginScope(correlationId);
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var dto = MapToDto(snapshot);
                    var envelope = WrapWithTrust(dto, correlationId);
                    string json = JsonSerializer.Serialize(envelope);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);

                    _telemetry?.Increment("correlation.plugin.ws_messages");
                    _telemetry?.RecordGauge("correlation.plugin.last_hash", CorrelationContext.ToNumericFingerprint(correlationId));

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
    private object WrapWithTrust(object payload, string? correlationId = null)
    {
        string payloadJson = JsonSerializer.Serialize(payload);
        string? signature = _trust.SignPayload(payloadJson);
        return new
        {
            correlation_id = correlationId,
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

    private static string ResolveOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var header) &&
            !string.IsNullOrWhiteSpace(header.ToString()))
            return header.ToString().Trim();

        if (context.Request.Headers.TryGetValue("X-Request-ID", out var requestId) &&
            !string.IsNullOrWhiteSpace(requestId.ToString()))
            return requestId.ToString().Trim();

        return CorrelationContext.Create("plugin");
    }

    private static string GetCorrelationIdFromContext(HttpContext context)
    {
        if (context.Items.TryGetValue("correlation_id", out var value) && value is string id && !string.IsNullOrWhiteSpace(id))
            return id;

        return ResolveOrCreateCorrelationId(context);
    }

    private static int ResolvePort(ILogger logger)
    {
        string? raw = Environment.GetEnvironmentVariable("HCEP_PLUGIN_PORT");
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out int parsed) && parsed is > 0 and <= 65535)
            return parsed;

        if (!string.IsNullOrWhiteSpace(raw))
            logger.LogWarning("Invalid HCEP_PLUGIN_PORT='{Port}' — defaulting to 5000", raw);

        return 5000;
    }

    private string? ExtractApiKey(HttpContext context)
    {
        string? auth = context.Request.Headers.Authorization;
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth[7..].Trim();

        if (context.Request.Query.TryGetValue("api_key", out var queryValue))
            return queryValue.ToString();

        return null;
    }

    private bool IsAuthorized(string? provided)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return true;
        if (string.IsNullOrWhiteSpace(provided)) return false;

        byte[] left = Encoding.UTF8.GetBytes(_apiKey);
        byte[] right = Encoding.UTF8.GetBytes(provided);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
