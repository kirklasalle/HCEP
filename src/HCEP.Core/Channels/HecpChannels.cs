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
using System.Threading.Channels;

namespace HCEP.Core.Channels;

/// <summary>
/// Factory for creating bounded <see cref="Channel{T}"/> instances
/// used throughout the HCEP pipeline. Centralizes back-pressure policy.
/// </summary>
public static class HCEPChannels
{
    /// <summary>Default pipeline channel capacity (bounded, drop oldest on full).</summary>
    public const int DefaultCapacity = 64;

    /// <summary>
    /// Creates a bounded single-consumer channel with drop-oldest overflow policy.
    /// Ideal for real-time sensor data where stale frames should be discarded.
    /// </summary>
    public static Channel<T> CreateRealTime<T>(int capacity = DefaultCapacity) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

    /// <summary>
    /// Creates a bounded multi-producer channel.
    /// Used when multiple upstream sources feed a single consumer.
    /// </summary>
    public static Channel<T> CreateFanIn<T>(int capacity = DefaultCapacity) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    /// <summary>
    /// Creates an unbounded channel for non-lossy delivery (e.g., speech results).
    /// </summary>
    public static Channel<T> CreateReliable<T>() =>
        Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
}
