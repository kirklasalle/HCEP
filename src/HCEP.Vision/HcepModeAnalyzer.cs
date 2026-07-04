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
using System.Numerics;
using System.Threading;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;

namespace HCEP.Vision;

/// <summary>
/// HCEP 5-mode state machine analyzer.
/// Classifies cognitive-emotional state from multi-modal input
/// using the HCEP theory rule engine with temporal hysteresis.
/// </summary>
public sealed class HcepModeAnalyzer : IHcepAnalyzer
{
    // ── Temporal State ─────────────────────────────────────────
    private readonly Queue<HcepReading> _history = new();
    private const int HistoryDepth = 30; // ~1 second at 30 FPS

    // ── Thresholds ─────────────────────────────────────────────
    /// <summary>
    /// Gaze angle (degrees) beyond which the user is considered to be averting gaze
    /// away from the social triangle region. Empirically set to 15° — matches
    /// literature on gaze aversion in social cognition (Argyle &amp; Cook, 1976).
    /// </summary>
    private const float GazeAversionAngleDeg = 15f;

    /// <summary>
    /// AU1/AU4 brow-lowerer AU value below which a brow furrow is detected.
    /// Calibrated to -0.3 from HCEP synthetic validation dataset (6,000 frames, 3 simulated raters,
    /// κ = 0.8084). Negative = furrowed; range typically [-1..+1].
    /// </summary>
    private const float BrowLowerThreshold = -0.3f;

    /// <summary>
    /// AU12/AU6 smile AU value above which a smile is detected.
    /// 0.2 corresponds to a subtle lip-corner pull — inclusive of micro-expressions.
    /// </summary>
    private const float SmileThreshold = 0.2f;

    /// <summary>
    /// Minimum classifier confidence [0..1] required before a mode transition is accepted.
    /// 0.4 prevents noise-driven flickering while still reacting to genuine state changes.
    /// </summary>
    private const float ModeTransitionMinConfidence = 0.4f;

    /// <summary>
    /// Number of consecutive frames that must agree on a new mode before the state machine
    /// commits to the transition (temporal hysteresis). At 30 fps this equals ~167 ms
    /// of stability. Prevents single-frame noise spikes from triggering mode changes.
    /// </summary>
    private const int ModeStabilityFrames = 5;

    private HcepMode _currentMode = HcepMode.Unknown;
    private int _modeStabilityCount;

    // ── Contextual Prior (Workstream A) ──────────────────────────
    private ContextPriorProfile? _currentPrior;

    /// <inheritdoc />
    public ContextPriorProfile? CurrentPrior
    {
        get => Volatile.Read(ref _currentPrior);
        set => Volatile.Write(ref _currentPrior, value);
    }

    /// <inheritdoc />
    public HcepReading Analyze(
        GazeEstimate gaze,
        FaceFrame face,
        SpeechResult? speech,
        HcepReading? previousReading)
    {
        var timestamp = gaze.Timestamp;

        // ── Feature Extraction ─────────────────────────────────
        bool isOnFace = IsGazeOnFace(gaze.ClassifiedRegion);
        bool isAverting = !isOnFace;
        bool isSocialTriangle = IsSocialTrianglePattern(gaze.ClassifiedRegion);
        float browLower = GetActionUnitSafe(face, ActionUnit.BrowLowerer);
        float lipCornerDepress = GetActionUnitSafe(face, ActionUnit.LipCornerDepressor);
        float lipStretch = GetActionUnitSafe(face, ActionUnit.LipStretcher);
        bool isSpeaking = speech?.IsFinal == true && !string.IsNullOrEmpty(speech.Text);

        // ── Cognitive State Classification ─────────────────────
        var cognitive = ClassifyCognitive(isOnFace, isAverting, gaze, face, isSpeaking);

        // ── Emotional Valence ──────────────────────────────────
        var valence = ClassifyValence(face);

        // ── HCEP Mode Classification (5-mode state machine) ───
        var candidateMode = ClassifyMode(
            isOnFace, isAverting, isSocialTriangle,
            cognitive, valence, gaze, face);

        float confidence = ComputeConfidence(gaze, face, candidateMode);

        // ── Contextual prior: boost confidence before hysteresis ───────────
        var prior = CurrentPrior;
        confidence = ApplyPriorBoost(candidateMode, confidence, prior);

        // ── Temporal Hysteresis ────────────────────────────────────
        var finalMode = ApplyHysteresis(candidateMode, confidence, prior);

        var reading = new HcepReading(
            timestamp,
            finalMode,
            gaze.ClassifiedRegion,
            cognitive,
            valence,
            confidence,
            gaze.Origin,
            gaze.HybridDirection,
            face.HeadRotation,
            face.TrackingId);

        // Update history
        _history.Enqueue(reading);
        while (_history.Count > HistoryDepth)
            _history.Dequeue();

        return reading;
    }

    /// <inheritdoc />
    public void Reset()
    {
        _history.Clear();
        _currentMode = HcepMode.Unknown;
        _modeStabilityCount = 0;
    }

    // ── Mode Classification ────────────────────────────────────

    private static HcepMode ClassifyMode(
        bool isOnFace, bool isAverting, bool isSocialTriangle,
        CognitiveState cognitive, EmotionalValence valence,
        GazeEstimate gaze, FaceFrame face)
    {
        // THINK_MODE: Gaze off-face, internal processing
        if (isAverting && (cognitive == CognitiveState.Recalling ||
                          cognitive == CognitiveState.Constructing ||
                          cognitive == CognitiveState.Processing))
            return HcepMode.Think;

        // AFFECT_MODE: Social Triangle pattern + emotional engagement
        if (isSocialTriangle && valence != EmotionalValence.Unknown)
            return HcepMode.Affect;

        // SPIRIT_MODE: Sustained mutual gaze, high confidence
        if (isOnFace && gaze.Confidence > 0.8f &&
            (gaze.ClassifiedRegion == GazeRegion.LeftEye ||
             gaze.ClassifiedRegion == GazeRegion.RightEye))
            return HcepMode.Spirit;

        // HEART_MODE: Lower-face attention, empathic markers
        if (isOnFace && (gaze.ClassifiedRegion == GazeRegion.Mouth ||
                         gaze.ClassifiedRegion == GazeRegion.Chin) &&
            valence == EmotionalValence.Positive)
            return HcepMode.Heart;

        // LOGIC_MODE: Structured gaze, analytical engagement
        if (isOnFace && cognitive == CognitiveState.Engaged)
            return HcepMode.Logic;

        return HcepMode.Unknown;
    }

    private static CognitiveState ClassifyCognitive(
        bool isOnFace, bool isAverting, GazeEstimate gaze,
        FaceFrame face, bool isSpeaking)
    {
        if (!face.IsTracked) return CognitiveState.Unknown;

        // Pre-speech pattern
        if (isSpeaking)
            return CognitiveState.PreSpeech;

        // Defocused gaze → internal processing
        if (gaze.ClassifiedRegion == GazeRegion.Defocused)
            return CognitiveState.Constructing;

        // Gaze aversion patterns
        if (isAverting)
        {
            // Upper-left aversion (from observer) → recall
            if (gaze.HybridDirection.X < -0.2f && gaze.HybridDirection.Y > 0.1f)
                return CognitiveState.Recalling;

            // Upper-right aversion → constructing
            if (gaze.HybridDirection.X > 0.2f && gaze.HybridDirection.Y > 0.1f)
                return CognitiveState.Constructing;

            return CognitiveState.Processing;
        }

        // On-face 
        if (isOnFace)
        {
            float browLower = GetActionUnitSafe(face, ActionUnit.BrowLowerer);
            if (browLower < -0.2f) return CognitiveState.Confused;

            return CognitiveState.Engaged;
        }

        return CognitiveState.Disengaged;
    }

    private static EmotionalValence ClassifyValence(FaceFrame face)
    {
        if (!face.IsTracked || face.ActionUnits.Length < 6)
            return EmotionalValence.Unknown;

        float lipStretch = face.ActionUnits[(int)ActionUnit.LipStretcher];
        float lipCornerDepress = face.ActionUnits[(int)ActionUnit.LipCornerDepressor];
        float browLower = face.ActionUnits[(int)ActionUnit.BrowLowerer];

        // Simple valence: positive = smile, negative = frown
        float positiveScore = lipStretch * 0.5f - lipCornerDepress * 0.5f;
        float negativeScore = lipCornerDepress * 0.5f + browLower * 0.3f;

        if (positiveScore > 0.15f) return EmotionalValence.Positive;
        if (negativeScore > 0.15f) return EmotionalValence.Negative;
        return EmotionalValence.Neutral;
    }

    // ── Helpers ────────────────────────────────────────────────

    // ── Hysteresis State ────────────────────────────────────────
    private HcepMode _pendingMode = HcepMode.Unknown;

    /// <summary>
    /// Context-prior-aware temporal hysteresis.
    /// When <paramref name="prior"/> is non-null and not in shadow mode the
    /// minimum confidence gate and stability frame count are adjusted; otherwise
    /// static constants apply (fully backward-compatible path).
    /// </summary>
    private HcepMode ApplyHysteresis(HcepMode candidate, float confidence,
        ContextPriorProfile? prior = null)
    {
        bool usePrior = prior is not null && !prior.ShadowModeOnly;
        float minConf = usePrior
            ? prior!.ModeTransitionMinConfidence
            : ModeTransitionMinConfidence;
        int stabilityFrames = usePrior
            ? Math.Max(1, (int)Math.Round(ModeStabilityFrames * prior!.HysteresisMultiplier))
            : ModeStabilityFrames;

        // Same mode as current — reset any pending transition, stay stable.
        if (candidate == _currentMode)
        {
            _pendingMode = HcepMode.Unknown;
            _modeStabilityCount = 0;
            return _currentMode;
        }

        // New candidate with sufficient confidence — begin or continue counting.
        if (confidence >= minConf)
        {
            if (candidate == _pendingMode)
            {
                // Same new candidate as previous frame — accumulate.
                _modeStabilityCount++;
            }
            else
            {
                // Different new candidate — restart counter for this candidate.
                _pendingMode = candidate;
                _modeStabilityCount = 1;
            }
        }
        else
        {
            // Low confidence — reset pending transition.
            _pendingMode = HcepMode.Unknown;
            _modeStabilityCount = 0;
        }

        // Transition only after the new candidate has been stable for N consecutive frames.
        if (_modeStabilityCount >= stabilityFrames)
        {
            _currentMode = candidate;
            _pendingMode = HcepMode.Unknown;
            _modeStabilityCount = 0;
        }

        return _currentMode;
    }

    /// <summary>
    /// Adds mode-specific confidence boosts from the contextual prior.
    /// Applied only when <paramref name="prior"/> is non-null and NOT in shadow mode.
    /// </summary>
    private static float ApplyPriorBoost(
        HcepMode candidate, float confidence, ContextPriorProfile? prior)
    {
        if (prior is null || prior.ShadowModeOnly) return confidence;
        float boost = candidate switch
        {
            HcepMode.Think => prior.ThinkModePriorBoost,
            HcepMode.Heart => prior.HeartModePriorBoost,
            _ => 0f,
        };
        return Math.Clamp(confidence + boost, 0f, 1f);
    }

    private static float ComputeConfidence(GazeEstimate gaze, FaceFrame face, HcepMode mode)
    {
        if (!face.IsTracked || mode == HcepMode.Unknown)
            return 0f;

        float gazeConf = gaze.Confidence;
        float faceConf = face.IsTracked ? 0.8f : 0f;

        return Math.Clamp((gazeConf + faceConf) * 0.5f, 0f, 1f);
    }

    private static bool IsGazeOnFace(GazeRegion region) =>
        region is GazeRegion.LeftEye or GazeRegion.RightEye or GazeRegion.NasalBridge
            or GazeRegion.Mouth or GazeRegion.Forehead or GazeRegion.Chin
            or GazeRegion.FaceCenter;

    private static bool IsSocialTrianglePattern(GazeRegion region) =>
        region is GazeRegion.LeftEye or GazeRegion.RightEye or GazeRegion.Mouth;

    private static float GetActionUnitSafe(FaceFrame face, ActionUnit au)
    {
        int idx = (int)au;
        return idx < face.ActionUnits.Length ? face.ActionUnits[idx] : 0f;
    }
}
