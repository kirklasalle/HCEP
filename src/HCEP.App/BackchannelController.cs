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
/// 3. Repeat with <see cref="RepeatNodIntervalMs"/> spacing so the avatar nods
///    at natural conversational rhythm (~one nod every 6–8 seconds of speech).
/// 4. When speech ends (final result + gap > <see cref="SpeechEndGapMs"/>),
///    reset state.
/// </summary>
public sealed class BackchannelController
{
    // ── Timing constants ──────────────────────────────────────────────────────
    /// <summary>Minimum continuous speech before first backchannel nod fires (ms).</summary>
    public const long MinSpeechBeforeNodMs = 2_200;

    /// <summary>Minimum interval between repeated backchannel nods (ms).</summary>
    public const long RepeatNodIntervalMs = 6_500;

    /// <summary>Gap after last speech result before "speech ended" is declared (ms).</summary>
    public const long SpeechEndGapMs = 2_500;

    // ── State ────────────────────────────────────────────────────────────────
    private long _speechOnsetMs = -1;   // TickCount64 when continuous speech began
    private long _lastNodMs = -1;   // TickCount64 of last nod emitted
    private long _lastSpeechMs = -1;   // TickCount64 of last non-null speech frame
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
                // Active in-progress or new final speech utterance
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

        // ── Fire backchannel nod if conditions are met ────────────────────────
        if (!_speechActive || _speechOnsetMs < 0) return;

        long speechDuration = now - _speechOnsetMs;
        bool firstNodReady = speechDuration >= MinSpeechBeforeNodMs && _lastNodMs < 0;
        bool repeatNodReady = _lastNodMs >= 0 && (now - _lastNodMs) >= RepeatNodIntervalMs;

        if (firstNodReady || repeatNodReady)
        {
            _lastNodMs = now;
            NodRequested?.Invoke();
        }
    }

    /// <summary>Resets all state (call on tracking loss or session end).</summary>
    public void Reset()
    {
        _speechOnsetMs = -1;
        _lastNodMs = -1;
        _lastSpeechMs = -1;
        _speechActive = false;
        _lastResultText = null;
    }
}
