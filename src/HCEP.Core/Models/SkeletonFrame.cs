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
using System.Collections.Immutable;
using System.Numerics;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// A single skeleton frame from Kinect v1 (20-joint model).
/// </summary>
public sealed record SkeletonFrame
{
    /// <summary>Frame timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Kinect skeleton tracking ID.</summary>
    public int TrackingId { get; init; }

    /// <summary>Overall tracking state of this skeleton.</summary>
    public TrackingState State { get; init; } = TrackingState.NotTracked;

    /// <summary>Skeleton position (hip center) in camera space.</summary>
    public Vector3 Position { get; init; }

    /// <summary>
    /// Joint positions keyed by Kinect JointType ordinal (0-19).
    /// Values in camera coordinate space (meters).
    /// </summary>
    public ImmutableDictionary<int, Vector3> Joints { get; init; } =
        ImmutableDictionary<int, Vector3>.Empty;

    /// <summary>
    /// Joint tracking states keyed by joint ordinal.
    /// </summary>
    public ImmutableDictionary<int, TrackingState> JointStates { get; init; } =
        ImmutableDictionary<int, TrackingState>.Empty;

    /// <summary>Clipped edges flags from Kinect (bitmask).</summary>
    public int ClippedEdges { get; init; }
}
