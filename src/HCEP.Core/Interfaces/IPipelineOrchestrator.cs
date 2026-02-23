// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// Orchestrates the full HCEP perception pipeline:
/// Sensor → Tracking → Gaze → HCEP → Intelligence.
/// Implemented as a hosted background service.
/// </summary>
public interface IPipelineOrchestrator
{
    /// <summary>Starts the full pipeline.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops the pipeline gracefully.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Whether the pipeline is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Latest scene snapshot (thread-safe read).</summary>
    SceneSnapshot? LatestSnapshot { get; }

    /// <summary>Fires on each new scene snapshot.</summary>
    event Action<SceneSnapshot>? SnapshotReady;

    /// <summary>Fires when a new speech transcription is available.</summary>
    event Action<SpeechResult>? SpeechReady;

    /// <summary>Fires when a new color frame is available from the sensor.</summary>
    event Action<ColorFrame>? ColorFrameReady;

    /// <summary>Fires when a new depth frame is available from the sensor.</summary>
    event Action<DepthFrame>? DepthFrameReady;

    /// <summary>Fires when a new infrared frame is available from the sensor (BGRA32 grayscale).</summary>
    event Action<ColorFrame>? InfraredFrameReady;

    /// <summary>Fires when a new skeleton frame is available from the sensor.</summary>
    event Action<SkeletonFrame>? SkeletonFrameReady;

    /// <summary>Fires when the LLM produces a response (auto-triggered on speech or manual query).</summary>
    event Action<LlmExchange>? LlmResponseReady;

    /// <summary>Current pipeline throughput (frames per second).</summary>
    double CurrentFps { get; }
}
