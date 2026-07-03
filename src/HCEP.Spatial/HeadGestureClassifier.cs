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

namespace HCEP.Spatial;

/// <summary>Discrete head gestures detected by <see cref="HeadGestureClassifier"/>.</summary>
public enum HeadGestureType
{
    /// <summary>No gesture in progress.</summary>
    None,
    /// <summary>Vertical nod — agreement, acknowledgement, backchannel listening.</summary>
    Nod,
    /// <summary>Horizontal shake — negation, disagreement.</summary>
    Shake,
    /// <summary>Head tilted and held left (from the observer's view).</summary>
    TiltLeft,
    /// <summary>Head tilted and held right (from the observer's view).</summary>
    TiltRight,
    /// <summary>Head moves forward (lean-in) toward the sensor.</summary>
    ForwardLean,
    /// <summary>Head moves backward (lean-out) away from the sensor.</summary>
    BackwardLean,
}

/// <summary>
/// Phase 9 — Head Gesture Classifier.
/// 
/// Detects Nod, Shake, TiltLeft, TiltRight, ForwardLean, BackwardLean from a
/// 30 Hz stream of Kinect head-pose angles (degrees) and user-depth distance (metres).
/// 
/// Detection approach:
///   • Nod / Shake: velocity threshold trigger + reversal confirmation.
///     A gesture fires when the angular velocity exceeds the threshold AND then
///     reverses direction, completing the reversal within <see cref="MaxGestureMs"/>.
///   • Tilt: velocity trigger + sustained hold for <see cref="TiltSustainMs"/>.
///   • Lean: depth delta trigger + sustained hold for <see cref="LeanSustainMs"/>.
/// 
/// A refractory period prevents re-triggering immediately after each gesture.
/// </summary>
public sealed class HeadGestureClassifier
{
    // ── Velocity thresholds (degrees per 33 ms frame at 30 fps) ───────────────
    private const float NodPitchVelocityThreshold = 7.0f;  // deg / frame
    private const float ShakeYawVelocityThreshold = 9.0f;
    private const float TiltRollVelocityThreshold = 11.0f;
    private const float LeanDepthVelocityThresholdM = 0.04f; // m / frame

    // ── Duration gates ────────────────────────────────────────────────────────
    private const long MinGestureMs = 70;    // min time before reversal is valid (nod/shake)
    private const long MaxGestureMs = 1800;  // timeout before candidate is abandoned
    private const long TiltSustainMs = 450;   // hold duration to confirm tilt
    private const long LeanSustainMs = 1200;  // hold duration to confirm lean
    private const long RefractoryMs = 600;   // inter-gesture cooldown

    // ── State ────────────────────────────────────────────────────────────────
    private float _prevPitch, _prevYaw, _prevRoll, _prevDistM;
    private long _prevTicks;
    private bool _initialized;

    private HeadGestureType _candidate = HeadGestureType.None;
    private int _gestureSign;   // +1 or -1 — direction of the initiating motion
    private long _candidateStartMs;
    private bool _fired;
    private long _lastFiredMs;

    /// <summary>
    /// Raised on the calling thread when a head gesture is confirmed.
    /// Safe to dispatch to UI from any thread — consumers should marshal if needed.
    /// </summary>
    public event Action<HeadGestureType>? GestureDetected;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Feeds one frame of head-pose data.  Call at ~30 Hz from the pipeline thread.
    /// <paramref name="pitchDeg"/>  positive = looking up.
    /// <paramref name="yawDeg"/>    positive = looking right.
    /// <paramref name="rollDeg"/>   positive = tilting clockwise (from user view).
    /// <paramref name="distanceM"/> user distance from sensor in metres.
    /// </summary>
    public void Update(float pitchDeg, float yawDeg, float rollDeg, float distanceM = 1.5f)
    {
        long now = Environment.TickCount64;

        if (!_initialized)
        {
            _prevPitch = pitchDeg;
            _prevYaw = yawDeg;
            _prevRoll = rollDeg;
            _prevDistM = distanceM;
            _prevTicks = now;
            _initialized = true;
            return;
        }

        // ── Compute per-frame velocity (normalised to 33 ms frame) ────────────
        double dtMs = Math.Clamp(now - _prevTicks, 1, 100);
        float scale = (float)(33.0 / dtMs);

        float dPitch = (pitchDeg - _prevPitch) * scale;
        float dYaw = (yawDeg - _prevYaw) * scale;
        float dRoll = (rollDeg - _prevRoll) * scale;
        float dDist = (distanceM - _prevDistM) * scale;

        _prevPitch = pitchDeg;
        _prevYaw = yawDeg;
        _prevRoll = rollDeg;
        _prevDistM = distanceM;
        _prevTicks = now;

        // ── Initiate new candidate if idle and refractory window has passed ──
        if (_candidate == HeadGestureType.None &&
            (now - _lastFiredMs) >= RefractoryMs)
        {
            if (MathF.Abs(dPitch) >= NodPitchVelocityThreshold)
            {
                _candidate = HeadGestureType.Nod;
                _gestureSign = Math.Sign(dPitch);
                _candidateStartMs = now;
                _fired = false;
            }
            else if (MathF.Abs(dYaw) >= ShakeYawVelocityThreshold)
            {
                _candidate = HeadGestureType.Shake;
                _gestureSign = Math.Sign(dYaw);
                _candidateStartMs = now;
                _fired = false;
            }
            else if (MathF.Abs(dRoll) >= TiltRollVelocityThreshold)
            {
                // Positive roll = clockwise from user's perspective = tilt right.
                _candidate = dRoll > 0f ? HeadGestureType.TiltRight : HeadGestureType.TiltLeft;
                _gestureSign = Math.Sign(dRoll);
                _candidateStartMs = now;
                _fired = false;
            }
            else if (MathF.Abs(dDist) >= LeanDepthVelocityThresholdM)
            {
                // Negative dDist = user is moving closer = forward lean.
                _candidate = dDist < 0f ? HeadGestureType.ForwardLean : HeadGestureType.BackwardLean;
                _gestureSign = Math.Sign(dDist);
                _candidateStartMs = now;
                _fired = false;
            }
        }

        // ── Evaluate active candidate ─────────────────────────────────────────
        long elapsed = now - _candidateStartMs;

        switch (_candidate)
        {
            case HeadGestureType.Nod when !_fired:
                // Confirmed when the pitch velocity reverses after MinGestureMs.
                if (elapsed >= MinGestureMs && Math.Sign(dPitch) != _gestureSign && Math.Abs(dPitch) > 1.5f)
                    Fire(HeadGestureType.Nod, now);
                break;

            case HeadGestureType.Shake when !_fired:
                if (elapsed >= MinGestureMs && Math.Sign(dYaw) != _gestureSign && Math.Abs(dYaw) > 1.5f)
                    Fire(HeadGestureType.Shake, now);
                break;

            case HeadGestureType.TiltLeft:
            case HeadGestureType.TiltRight:
                // Tilt is confirmed once the pose is held for TiltSustainMs.
                if (!_fired && elapsed >= TiltSustainMs)
                    Fire(_candidate, now);
                break;

            case HeadGestureType.ForwardLean:
            case HeadGestureType.BackwardLean:
                if (!_fired && elapsed >= LeanSustainMs)
                    Fire(_candidate, now);
                break;
        }

        // ── Abandon stale candidates ──────────────────────────────────────────
        if (_candidate != HeadGestureType.None && !_fired && elapsed > MaxGestureMs)
            _candidate = HeadGestureType.None;
    }

    /// <summary>Resets all state (e.g. after tracking loss).</summary>
    public void Reset()
    {
        _initialized = false;
        _candidate = HeadGestureType.None;
        _fired = false;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Fire(HeadGestureType gesture, long now)
    {
        _fired = true;
        _lastFiredMs = now;
        _candidate = HeadGestureType.None;
        GestureDetected?.Invoke(gesture);
    }
}
