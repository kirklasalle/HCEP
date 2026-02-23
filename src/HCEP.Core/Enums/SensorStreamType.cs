// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Enums;

/// <summary>
/// Kinect sensor stream types used for selective stream activation.
/// </summary>
[Flags]
public enum SensorStreamType
{
    None = 0,
    Color = 1 << 0,
    Depth = 1 << 1,
    Skeleton = 1 << 2,
    Audio = 1 << 3,
    FaceTracking = 1 << 4,
    All = Color | Depth | Skeleton | Audio | FaceTracking,
}

/// <summary>
/// Action Unit identifiers from Kinect Face Tracking SDK v1.
/// Based on FACS (Facial Action Coding System).
/// </summary>
public enum ActionUnit
{
    /// <summary>AU0 — Upper Lip Raiser.</summary>
    UpperLipRaiser = 0,

    /// <summary>AU1 — Jaw Lowerer.</summary>
    JawLowerer = 1,

    /// <summary>AU2 — Lip Stretcher.</summary>
    LipStretcher = 2,

    /// <summary>AU3 — Brow Lowerer.</summary>
    BrowLowerer = 3,

    /// <summary>AU4 — Lip Corner Depressor.</summary>
    LipCornerDepressor = 4,

    /// <summary>AU5 — Outer Brow Raiser.</summary>
    OuterBrowRaiser = 5,
}
