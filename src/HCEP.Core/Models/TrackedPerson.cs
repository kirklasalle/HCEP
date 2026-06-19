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
/// Represents a person being tracked across frames.
/// Aggregates skeleton, face, gaze, and identity data.
/// </summary>
public sealed record TrackedPerson
{
    /// <summary>Stable tracking identifier (persists across frames).</summary>
    public required int TrackingId { get; init; }

    /// <summary>Overall person tracking quality.</summary>
    public TrackingState State { get; init; } = TrackingState.NotTracked;

    /// <summary>Recognized identity name (null if unknown).</summary>
    public string? IdentityName { get; init; }

    /// <summary>ArcFace embedding for identity matching (512-d vector).</summary>
    public float[]? FaceEmbedding { get; init; }

    /// <summary>Cosine similarity score for identity match [0..1].</summary>
    public float IdentityConfidence { get; init; }

    /// <summary>Current 3D skeleton joint positions (Kinect 20-joint model).</summary>
    public ImmutableDictionary<int, Vector3>? JointPositions { get; init; }

    /// <summary>Per-joint tracking states keyed by joint ordinal (0-19).</summary>
    public ImmutableDictionary<int, TrackingState>? JointStates { get; init; }

    /// <summary>Face tracking data for this person.</summary>
    public FaceFrame? Face { get; init; }

    /// <summary>Most recent HCEP reading.</summary>
    public HcepReading? LatestHcep { get; init; }

    /// <summary>Head position in camera space.</summary>
    public Vector3 HeadPosition { get; init; }

    /// <summary>
    /// Left eye 3D position in camera space (meters).
    /// Computed from head position + inter-ocular offset with head rotation.
    /// This is the physical LOCATION of the eye, not gaze direction.
    /// </summary>
    public Vector3 LeftEyePosition { get; init; }

    /// <summary>
    /// Right eye 3D position in camera space (meters).
    /// Computed from head position + inter-ocular offset with head rotation.
    /// This is the physical LOCATION of the eye, not gaze direction.
    /// </summary>
    public Vector3 RightEyePosition { get; init; }

    /// <summary>
    /// Inter-ocular distance in meters (distance between eyes).
    /// ~0.063m average adult. 0 if eye positions are unavailable.
    /// </summary>
    public float InterOcularDistanceM =>
        (LeftEyePosition == default && RightEyePosition == default)
            ? 0f
            : Vector3.Distance(LeftEyePosition, RightEyePosition);

    /// <summary>Distance from sensor in meters.</summary>
    public float DistanceM { get; init; }

    /// <summary>Last time this person was actively tracked.</summary>
    public DateTimeOffset LastSeen { get; init; }

    // ── Phase 6: True Gaze Avatar ──────────────────────────────

    /// <summary>
    /// Avatar IK look-at target — the 3D camera-space position (metres) of the
    /// user's eye socket that the avatar is currently fixating (driven by
    /// <see cref="HCEP.Spatial.MicroSaccadeController"/>).
    /// Null when face tracking data is unavailable.
    /// </summary>
    public Vector3? AvatarIkTarget { get; init; }

    /// <summary>
    /// Parallax-corrected gaze direction — the raw <see cref="GazeEstimate.HybridDirection"/>
    /// with the camera-to-screen calibration offset applied by
    /// <see cref="HCEP.Spatial.CalibrationMatrixCalculator"/>.
    /// <see cref="Vector3.Zero"/> when gaze data is unavailable.
    /// </summary>
    public Vector3 CalibratedGazeDirection { get; init; }
}
