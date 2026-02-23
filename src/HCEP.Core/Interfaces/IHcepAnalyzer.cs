// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// HCEP mode analyzer — classifies the cognitive-emotional state from
/// multi-modal input (gaze, face AUs, speech, temporal context).
/// </summary>
public interface IHcepAnalyzer
{
    /// <summary>
    /// Analyzes a single frame to produce an HCEP reading.
    /// </summary>
    /// <param name="gaze">Current gaze estimate.</param>
    /// <param name="face">Current face frame data.</param>
    /// <param name="speech">Latest speech result (may be null if silent).</param>
    /// <param name="previousReading">Previous frame's HCEP reading for temporal continuity.</param>
    /// <returns>Classified HCEP reading.</returns>
    HcepReading Analyze(
        GazeEstimate gaze,
        FaceFrame face,
        SpeechResult? speech,
        HcepReading? previousReading);

    /// <summary>
    /// Resets internal temporal state (e.g., when switching tracked person).
    /// </summary>
    void Reset();
}
