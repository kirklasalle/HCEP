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
using HCEP.Core.Models;

namespace HCEP.Core.Interfaces;

/// <summary>
/// 3-stage gaze estimation pipeline:
///   Stage 1 — Head pose gaze (SolvePnP)
///   Stage 2 — Eye-in-head offset (pupil feature points)
///   Stage 3 — Hybrid fusion + Confidence Cone classification
/// </summary>
public interface IGazeEstimator
{
    /// <summary>
    /// Estimates gaze from a face frame using the 3-stage pipeline.
    /// </summary>
    /// <param name="face">Current face tracking data.</param>
    /// <param name="previousEstimate">Previous frame's estimate for temporal smoothing.</param>
    /// <returns>Fused gaze estimate with classified region.</returns>
    GazeEstimate Estimate(FaceFrame face, GazeEstimate? previousEstimate = null);

    /// <summary>
    /// Updates the anthropometric model for a specific person
    /// (e.g., measured IPD from face recognition enrollment).
    /// </summary>
    void CalibrateForPerson(int trackingId, float ipdMm);
}
