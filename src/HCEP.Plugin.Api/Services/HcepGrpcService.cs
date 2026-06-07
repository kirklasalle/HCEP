// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.Threading.Tasks;
using Grpc.Core;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Plugin.Api.Grpc;
using Microsoft.Extensions.Logging;

namespace HCEP.Plugin.Api.Services;

public sealed class HcepGrpcService : HcepPluginService.HcepPluginServiceBase
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ILogger<HcepGrpcService> _logger;

    public HcepGrpcService(IPipelineOrchestrator orchestrator, ILogger<HcepGrpcService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public override Task<HcepStateResponse> GetState(GetStateRequest request, ServerCallContext context)
    {
        var snapshot = _orchestrator.LatestSnapshot;
        return Task.FromResult(MapToResponse(snapshot));
    }

    public override async Task StreamState(StreamStateRequest request, IServerStreamWriter<HcepStateResponse> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("New gRPC streaming client connected.");

        // Create a thread-safe local handler
        Action<SceneSnapshot>? handler = null;

        var tcs = new TaskCompletionSource<bool>();

        // When the call is cancelled, complete the task
        context.CancellationToken.Register(() => tcs.TrySetResult(true));

        handler = async (snapshot) =>
        {
            try
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetResult(true);
                    return;
                }

                await responseStream.WriteAsync(MapToResponse(snapshot));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write gRPC stream frame.");
                tcs.TrySetResult(false);
            }
        };

        _orchestrator.SnapshotReady += handler;

        try
        {
            // Keep stream open until cancellation or write failure
            await tcs.Task;
        }
        finally
        {
            _orchestrator.SnapshotReady -= handler;
            _logger.LogInformation("gRPC streaming client disconnected.");
        }
    }

    private static HcepStateResponse MapToResponse(SceneSnapshot? snapshot)
    {
        var response = new HcepStateResponse
        {
            Timestamp = snapshot?.Timestamp.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
            FrameNumber = snapshot?.FrameNumber ?? 0,
            PersonDetected = snapshot?.PrimaryPerson != null
        };

        if (snapshot?.PrimaryPerson is { } person)
        {
            response.TrackingId = person.TrackingId;
            response.IdentityName = person.IdentityName ?? string.Empty;
            response.IdentityConfidence = person.IdentityConfidence;
            
            response.HeadTransX = person.HeadPosition.X;
            response.HeadTransY = person.HeadPosition.Y;
            response.HeadTransZ = person.HeadPosition.Z;

            if (person.Face is { } face)
            {
                response.HeadRotPitch = face.HeadRotation.X;
                response.HeadRotYaw = face.HeadRotation.Y;
                response.HeadRotRoll = face.HeadRotation.Z;
            }

            if (person.LatestHcep is { } hcep)
            {
                response.HcepMode = hcep.Mode.ToString();
                response.Confidence = hcep.Confidence;
                response.Region = hcep.Region.ToString();
                response.GazeDirX = hcep.GazeDirection.X;
                response.GazeDirY = hcep.GazeDirection.Y;
                response.GazeDirZ = hcep.GazeDirection.Z;
            }
        }

        return response;
    }
}
