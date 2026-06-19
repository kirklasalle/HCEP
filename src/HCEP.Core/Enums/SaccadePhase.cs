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
/// Phase of a saccadic eye movement for the avatar animation system.
/// Models biomechanically accurate Bang-Bang motor control.
/// </summary>
public enum SaccadePhase
{
    /// <summary>Eye is stationary at current fixation point.</summary>
    Fixation = 0,

    /// <summary>Acceleration phase — Bang-Bang pulse onset.</summary>
    Acceleration = 1,

    /// <summary>Deceleration phase — counter-pulse braking.</summary>
    Deceleration = 2,

    /// <summary>Post-saccadic suppression — brief fixation settling.</summary>
    Settling = 3,

    /// <summary>Smooth pursuit tracking of a moving target.</summary>
    Pursuit = 4,

    /// <summary>Microsaccade — involuntary fixational movement.</summary>
    Microsaccade = 5,
}
