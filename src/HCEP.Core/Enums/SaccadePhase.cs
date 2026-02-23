// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
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
