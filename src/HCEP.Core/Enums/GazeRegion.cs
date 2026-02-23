// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Enums;

/// <summary>
/// Discrete gaze-target regions on the interlocutor's face and environment.
/// Used for HCEP mode classification and saccade animation targeting.
/// </summary>
public enum GazeRegion
{
    /// <summary>Gaze target cannot be determined.</summary>
    Unknown = 0,

    /// <summary>Left eye of the interlocutor (from observer's perspective).</summary>
    LeftEye = 1,

    /// <summary>Right eye of the interlocutor (from observer's perspective).</summary>
    RightEye = 2,

    /// <summary>Bridge of nose / between the eyes (cyclopean point).</summary>
    NasalBridge = 3,

    /// <summary>Mouth region — Social Triangle vertex.</summary>
    Mouth = 4,

    /// <summary>Forehead / upper face.</summary>
    Forehead = 5,

    /// <summary>Chin / lower face.</summary>
    Chin = 6,

    /// <summary>Left peripheral (off-face, left of interlocutor).</summary>
    PeripheralLeft = 7,

    /// <summary>Right peripheral (off-face, right of interlocutor).</summary>
    PeripheralRight = 8,

    /// <summary>Above face — upward gaze aversion.</summary>
    Above = 9,

    /// <summary>Below face — downward gaze aversion / submission signal.</summary>
    Below = 10,

    /// <summary>Defocused / thousand-yard stare — THINK_MODE indicator.</summary>
    Defocused = 11,

    /// <summary>Center face — general face-directed gaze.</summary>
    FaceCenter = 12,
}
