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
