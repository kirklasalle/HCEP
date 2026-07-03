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
using System;
using HCEP.Core.Enums;

namespace HCEP.App;

/// <summary>
/// Phase 10 — Social Gaze Pattern Controller.
///
/// Drives biologically authentic avatar gaze offsets that simulate the
/// "Social Triangle" scanning pattern (Argyle &amp; Cook 1976; Kleinke 1986):
/// the avatar's gaze naturally cycles between the user's left eye, right eye,
/// and mouth during engaged, socially warm interaction.
///
/// Mode mapping
/// ────────────
/// <list type="bullet">
///   <item><c>AFFECT</c> / <c>SPIRIT</c> — full social triangle at biological rate
///     (Left Eye → Right Eye → Mouth → repeat; ~3 fixations/second).</item>
///   <item><c>HEART</c> — slower triangle (2 fixations/second), heavier toward mouth
///     (empathic/listening posture).</item>
///   <item><c>LOGIC</c> — minimal; brief inter-eye oscillation only (direct gaze).</item>
///   <item><c>THINK</c> — gaze slightly averted (−10° yaw) to signal internal processing.</item>
///   <item><c>Unknown</c> — no offset.</item>
/// </list>
///
/// Proxemic modulation
/// ──────────────────
/// At intimate distances (&lt; 0.45 m) the triangle amplitude is halved — real people
/// widen the social triangle only at comfortable social distances (Kendon 1967).
///
/// Thread safety
/// ─────────────
/// <see cref="Update"/> may be called from the pipeline thread.
/// The <see cref="GazeOffsetChanged"/> event fires on the calling thread;
/// consumers should marshal to UI as required.
/// </summary>
public sealed class SocialGazeController
{
    // ── Fixation targets [yawRad, pitchRad] for each triangle vertex ──────────
    // Offset conventions match HCEP gaze: pitch+ = up, yaw+ = right.
    // These are offsets RELATIVE to the current computed gaze direction.
    private static readonly (float Yaw, float Pitch)[] TriangleFull =
    [
        (-0.055f,  0.022f),   // Left eye  (from avatar's perspective)
        ( 0.055f,  0.022f),   // Right eye
        ( 0.000f, -0.085f),   // Mouth (below nose line)
    ];

    private static readonly (float Yaw, float Pitch)[] TriangleSlow =
    [
        (-0.040f,  0.018f),   // Left eye (narrower, HEART mode)
        ( 0.040f,  0.018f),   // Right eye
        ( 0.000f, -0.095f),   // Mouth (slightly lower for empathy)
    ];

    private static readonly (float Yaw, float Pitch)[] DirectGaze =
    [
        (-0.025f,  0.010f),   // Left eye only
        ( 0.025f,  0.010f),   // Right eye only
    ];

    private static readonly (float Yaw, float Pitch) ThinkAversion = (-0.10f, 0.008f);

    // ── Dwell times ───────────────────────────────────────────────────────────
    private const long DwellFullMs = 340;   // AFFECT/SPIRIT: ~3 fixations/s
    private const long DwellSlowMs = 500;   // HEART: ~2 fixations/s
    private const long DwellDirectMs = 600;  // LOGIC
    private const long ThinkHoldMs = 1500;  // THINK: hold aversion then return

    // ── State ─────────────────────────────────────────────────────────────────
    private HcepMode _lastMode = HcepMode.Unknown;
    private int _fixIdx;
    private long _fixStartMs;
    private float _proxemicScale = 1.0f;

    /// <summary>
    /// Raised whenever the gaze offset changes.
    /// <paramref name="yawRad"/>   positive = look right.
    /// <paramref name="pitchRad"/> positive = look up.
    /// </summary>
    public event Action<float, float>? GazeOffsetChanged;

    /// <summary>
    /// Updates the social gaze pattern.  Call at ~10–30 Hz from the pipeline thread.
    /// <paramref name="mode"/> current HCEP mode.
    /// <paramref name="distanceM"/> user distance in metres (proxemic modulation).
    /// </summary>
    public void Update(HcepMode mode, float distanceM = 1.5f)
    {
        long now = Environment.TickCount64;

        // Proxemic amplitude scale: halve offset inside intimate zone (< 0.45 m).
        _proxemicScale = distanceM < 0.45f ? 0.5f : 1.0f;

        // Mode transition — reset fixation index.
        if (mode != _lastMode)
        {
            _lastMode = mode;
            _fixIdx = 0;
            _fixStartMs = now;
        }

        (float yaw, float pitch) = ComputeOffset(mode, now);
        GazeOffsetChanged?.Invoke(yaw * _proxemicScale, pitch * _proxemicScale);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private (float Yaw, float Pitch) ComputeOffset(HcepMode mode, long now)
    {
        switch (mode)
        {
            case HcepMode.Affect:
            case HcepMode.Spirit:
                return AdvanceTriangle(TriangleFull, DwellFullMs, now);

            case HcepMode.Heart:
                return AdvanceTriangle(TriangleSlow, DwellSlowMs, now);

            case HcepMode.Logic:
                return AdvanceTriangle(DirectGaze, DwellDirectMs, now);

            case HcepMode.Think:
                // Hold the aversion offset for ThinkHoldMs then briefly return.
                long elapsed = now - _fixStartMs;
                if (elapsed < ThinkHoldMs)
                    return ThinkAversion;
                // Return to neutral for a moment before re-averting
                if (elapsed < ThinkHoldMs + 400)
                    return (0f, 0f);
                _fixStartMs = now;
                return ThinkAversion;

            default:
                return (0f, 0f);
        }
    }

    private (float Yaw, float Pitch) AdvanceTriangle(
        (float Yaw, float Pitch)[] targets, long dwellMs, long now)
    {
        if (_fixIdx >= targets.Length) _fixIdx = 0;

        if ((now - _fixStartMs) >= dwellMs)
        {
            _fixIdx = (_fixIdx + 1) % targets.Length;
            _fixStartMs = now;
        }

        return targets[_fixIdx];
    }
}
