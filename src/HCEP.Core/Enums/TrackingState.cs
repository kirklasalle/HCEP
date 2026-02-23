// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Enums;

/// <summary>
/// Tracking quality state for a person or joint in the scene.
/// </summary>
public enum TrackingState
{
    NotTracked = 0,
    PositionOnly = 1,
    Inferred = 2,
    Tracked = 3,
}

/// <summary>
/// Sensor connection state.
/// </summary>
public enum SensorState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Error = 3,
    Initializing = 4,
}
