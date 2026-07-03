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
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Phase 10 — Expression Mirror.
///
/// Monitors incoming <see cref="SceneSnapshot"/> frames for human smile signals and
/// schedules a reciprocal avatar smile after a biologically motivated reaction delay.
///
/// Detection
/// ─────────
/// Kinect v1 provides six Action Units. Smile is inferred from:
///   • AU2 (<c>LipStretcher</c>) as the primary signal (lip corners pulled back).
///   • AU5 (<c>OuterBrowRaiser</c>) as a Duchenne marker (genuine smile indicator,
///     Ekman &amp; Friesen 1982 — involves orbicularis oculi contraction).
///
/// When <c>LipStretcher ≥ SmileThreshold</c> for <c>MinSmileDurationMs</c>:
///   • With AU5 ≥ DuchenneThreshold → genuine smile: full avatar smile + brow relaxation.
///   • Without AU5 → social smile: 70% intensity avatar smile.
///
/// Reciprocation
/// ─────────────
/// Fires <see cref="SmileRequested"/> after a random 200-400ms biological reaction delay
/// (Dimberg et al. 2000; Hatfield et al. 1993).  If the human smile ends before the delay
/// elapses, the request is still fired once at reduced intensity (mirroring of past state).
/// Repeated smiles repeat at <see cref="SmileRepeatIntervalMs"/> spacing.
///
/// Smile fade-out
/// ──────────────
/// When the human stops smiling, the avatar smile decays over <see cref="SmileFadeMs"/>.
/// <see cref="SmileRequested"/> is fired periodically with decreasing intensity until 0.
/// </summary>
public sealed class ExpressionMirror
{
    // ── Thresholds ────────────────────────────────────────────────────────────
    private const float SmileThreshold = 0.30f;  // LipStretcher trigger
    private const float DuchenneThreshold = 0.20f;  // OuterBrowRaiser → genuine
    private const long MinSmileDurationMs = 120;   // required hold before reacting

    // ── Timing ────────────────────────────────────────────────────────────────
    private const long SmileReactionMinMs = 200;
    private const long SmileReactionRangeMs = 200;  // random [200, 400] ms delay
    private const long SmileRepeatIntervalMs = 4_000;
    private const long SmileFadeMs = 800;

    // ── State ────────────────────────────────────────────────────────────────
    private long _smileOnsetMs = -1;
    private bool _smileActive;
    private bool _duchenne;
    private long _pendingFireMs = -1;   // scheduled fire time (after reaction delay)
    private long _lastFireMs = -1;
    private float _lastFireIntensity;
    private long _smileEndMs = -1;   // when human smile ended (for fade-out)
    private static readonly Random _rng = new();

    /// <summary>
    /// Raised when the avatar should display a smile.
    /// <paramref name="intensity"/> [0..1]: 0 = neutral, 1 = full smile.
    /// Fired from the calling thread — callers must marshal to UI if needed.
    /// </summary>
    public event Action<float>? SmileRequested;

    /// <summary>
    /// Call on every <see cref="SceneSnapshot"/> frame (pipeline thread).
    /// </summary>
    public void OnSnapshot(SceneSnapshot snapshot)
    {
        var face = snapshot.PrimaryPerson?.Face;
        long now = Environment.TickCount64;

        float lipStretch = 0f, outerBrow = 0f;
        if (face is { IsTracked: true })
        {
            var aus = face.ActionUnits;
            lipStretch = SafeAU(aus, (int)ActionUnit.LipStretcher);
            outerBrow = SafeAU(aus, (int)ActionUnit.OuterBrowRaiser);
        }

        bool humanSmiling = lipStretch >= SmileThreshold;

        // ── Track smile onset ─────────────────────────────────────────────────
        if (humanSmiling)
        {
            if (!_smileActive)
            {
                _smileActive = true;
                _smileOnsetMs = now;
                _duchenne = outerBrow >= DuchenneThreshold;
                _smileEndMs = -1;

                // Schedule reaction-delay fire
                long reactionMs = SmileReactionMinMs + _rng.NextInt64(SmileReactionRangeMs);
                _pendingFireMs = now + reactionMs;
            }
            else
            {
                // Update Duchenne flag while smile is active
                if (outerBrow >= DuchenneThreshold) _duchenne = true;
            }
        }
        else if (_smileActive)
        {
            // Smile just ended
            _smileActive = false;
            _smileEndMs = now;
        }

        // ── Fire pending reciprocation ─────────────────────────────────────────
        if (_pendingFireMs >= 0 && now >= _pendingFireMs)
        {
            long holdDuration = _smileActive ? (now - _smileOnsetMs) : (_smileEndMs - _smileOnsetMs);
            if (holdDuration >= MinSmileDurationMs)
            {
                float intensity = _duchenne ? 1.0f : 0.70f;
                _lastFireIntensity = intensity;
                _lastFireMs = now;
                SmileRequested?.Invoke(intensity);
            }
            _pendingFireMs = -1;
        }

        // ── Repeat smile while human keeps smiling ─────────────────────────────
        if (_smileActive && _lastFireMs >= 0 && (now - _lastFireMs) >= SmileRepeatIntervalMs)
        {
            float intensity = _duchenne ? 1.0f : 0.70f;
            _lastFireIntensity = intensity;
            _lastFireMs = now;
            SmileRequested?.Invoke(intensity);
        }

        // ── Fade-out when human smile ended ────────────────────────────────────
        if (!_smileActive && _smileEndMs >= 0 && _lastFireMs >= 0)
        {
            long fadeElapsed = now - _smileEndMs;
            if (fadeElapsed < SmileFadeMs)
            {
                // Emit decaying intensity every ~100ms
                if ((now - _lastFireMs) >= 100)
                {
                    float t = (float)fadeElapsed / SmileFadeMs;
                    float intensity = _lastFireIntensity * (1f - t);
                    _lastFireMs = now;
                    SmileRequested?.Invoke(MathF.Max(0f, intensity));
                }
            }
            else if (_lastFireMs > 0 && _lastFireIntensity > 0f)
            {
                // Ensure a final 0-intensity call to fully reset
                _lastFireMs = now;
                _lastFireIntensity = 0f;
                SmileRequested?.Invoke(0f);
            }
        }
    }

    /// <summary>Resets all state (e.g. on tracking loss).</summary>
    public void Reset()
    {
        _smileActive = false;
        _smileOnsetMs = -1;
        _pendingFireMs = -1;
        _lastFireMs = -1;
        _lastFireIntensity = 0f;
        _smileEndMs = -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float SafeAU(float[] aus, int idx) =>
        idx < aus.Length ? aus[idx] : 0f;
}
