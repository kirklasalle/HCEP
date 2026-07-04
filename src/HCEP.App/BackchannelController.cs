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
using HCEP.Core.Models;

namespace HCEP.App;

/// <summary>
/// Phase 10 — Backchannel Engine.
/// 
/// Monitors incoming <see cref="SceneSnapshot"/> frames and generates
/// avatar backchannel signals (nods, listening acknowledgements) in response to
/// sustained human speech.
/// 
/// Strategy
/// ────────
/// 1. When a non-final <see cref="SpeechResult"/> arrives (in-progress speech),
///    start a speech-onset timer.
/// 2. If speech continues for <see cref="MinSpeechBeforeNodMs"/> milliseconds
///    without interruption, fire <see cref="NodRequested"/>.
/// 3. Repeat nods at a cadence-aware interval scaled by the speaker's syllable
///    rate, with bounded Gaussian jitter to avoid metronomic repetition.
///    See <see cref="ComputeRepeatIntervalMs"/> and <see cref="GaussianJitterMs"/>.
/// 4. When speech ends (final result + gap > <see cref="SpeechEndGapMs"/>),
///    reset state.
/// </summary>
public sealed class BackchannelController
{
    // ── Base timing constants ────────────────────────────────────────────────
    /// <summary>Minimum continuous speech before first backchannel nod fires (ms).</summary>
    public const long MinSpeechBeforeNodMs = 2_200;

    /// <summary>Base interval between repeated backchannel nods at neutral cadence (ms).</summary>
    public const long RepeatNodIntervalMs = 6_500;

    /// <summary>Gap after last speech result before "speech ended" is declared (ms).</summary>
    public const long SpeechEndGapMs = 2_500;

    // ── Workstream C: cadence + jitter configuration ─────────────────────────

    /// <summary>
    /// When true, nod repeat intervals are scaled by the speaker's measured
    /// syllable rate. Feature flag C5.
    /// </summary>
    public bool CadenceAwareScheduling { get; set; } = true;

    /// <summary>
    /// Half-width (ms) of the Gaussian jitter window applied to repeat nod timing.
    /// The jitter is sampled from N(0, (JitterWindowMs/2 / 3)²) and clamped to
    /// ±JitterWindowMs/2 so ≈99% of samples fall within the window.
    /// Set to 0 to disable jitter (deterministic fallback).
    /// </summary>
    public int JitterWindowMs { get; set; } = 200;

    /// <summary>
    /// Latest speech cadence provided by the orchestrator.
    /// Updated ~1 Hz from the audio/STT pipeline; null → use base interval.
    /// </summary>
    public SpeechCadenceProfile? CurrentCadence { get; set; }

    // ── State ────────────────────────────────────────────────────────────────
    private long _speechOnsetMs = -1;
    private long _lastNodMs = -1;
    private long _nextNodDueMs = -1;  // pre-computed with jitter
    private long _lastSpeechMs = -1;
    private bool _speechActive;
    private string? _lastResultText;

    /// <summary>Raised when the avatar should produce a nod backchannel.</summary>
    public event Action? NodRequested;

    /// <summary>
    /// Call on every <see cref="SceneSnapshot"/> frame from the pipeline (background thread).
    /// Thread-safe: all fields accessed only from this method; callers must ensure
    /// single-thread access or serialize externally.
    /// </summary>
    public void OnSnapshot(SceneSnapshot snapshot)
    {
        long now = Environment.TickCount64;
        var speech = snapshot.LatestSpeech;

        if (speech is not null && speech.Text.Length > 0)
        {
            bool isSameFinalResult = speech.IsFinal && speech.Text == _lastResultText;
            if (!isSameFinalResult)
            {
                _lastSpeechMs = now;
                _lastResultText = speech.IsFinal ? speech.Text : _lastResultText;

                if (!_speechActive)
                {
                    _speechActive = true;
                    _speechOnsetMs = now;
                }
            }
        }

        // ── Declare speech ended if gap exceeds threshold ─────────────────────
        if (_speechActive && _lastSpeechMs >= 0 && (now - _lastSpeechMs) > SpeechEndGapMs)
        {
            _speechActive = false;
            _speechOnsetMs = -1;
            _lastResultText = null;
        }

        if (!_speechActive || _speechOnsetMs < 0) return;

        long speechDuration = now - _speechOnsetMs;
        bool firstNodReady = speechDuration >= MinSpeechBeforeNodMs && _lastNodMs < 0;
        bool repeatNodReady = _lastNodMs >= 0 && now >= _nextNodDueMs;

        if (firstNodReady || repeatNodReady)
        {
            _lastNodMs = now;
            _nextNodDueMs = now + ComputeRepeatIntervalMs() + GaussianJitterMs();
            NodRequested?.Invoke();
        }
    }

    /// <summary>Resets all state (call on tracking loss or session end).</summary>
    public void Reset()
    {
        _speechOnsetMs = -1;
        _lastNodMs = -1;
        _nextNodDueMs = -1;
        _lastSpeechMs = -1;
        _speechActive = false;
        _lastResultText = null;
    }

    // ── Cadence-aware interval computation ───────────────────────────────────

    /// <summary>
    /// Computes the effective repeat nod interval (ms) based on the current speech
    /// cadence. Faster speech (higher syll/s) tightens the nod interval; slower
    /// speech stretches it. Clamped to [2 500, 12 000] ms for stability.
    /// Falls back to <see cref="RepeatNodIntervalMs"/> when cadence is unavailable
    /// or <see cref="CadenceAwareScheduling"/> is disabled.
    /// </summary>
    private long ComputeRepeatIntervalMs()
    {
        if (!CadenceAwareScheduling || CurrentCadence is null || !CurrentCadence.IsFresh)
            return RepeatNodIntervalMs;

        // Scale inversely with syllable rate relative to the neutral baseline (4 syll/s).
        // 4 syll/s → 6 500 ms, 6 syll/s → ~4 333 ms, 2 syll/s → ~9 750 ms.
        float cadenceRatio = 4f / Math.Max(CurrentCadence.SyllablesPerSecond, 0.5f);
        long interval = (long)(RepeatNodIntervalMs * cadenceRatio);
        return Math.Clamp(interval, 2_500L, 12_000L);
    }

    /// <summary>
    /// Returns a bounded Gaussian jitter in ms (Box-Muller transform).
    /// Introduces the micro-variability that prevents metronomic repetition
    /// (Condon &amp; Ogston 1967; Duchenne smile response timing).
    /// Returns 0 when <see cref="JitterWindowMs"/> is 0 (deterministic fallback).
    /// </summary>
    private long GaussianJitterMs()
    {
        if (JitterWindowMs <= 0) return 0L;
        // Box-Muller: produces N(0,1) from two uniform samples
        double u1 = 1.0 - Random.Shared.NextDouble();
        double u2 = 1.0 - Random.Shared.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        // Scale: σ = half-window / 3 so ≈99% of samples stay within ±half-window
        double sigma = JitterWindowMs / 2.0 / 3.0;
        return (long)Math.Clamp(z * sigma, -JitterWindowMs / 2.0, JitterWindowMs / 2.0);
    }
}
