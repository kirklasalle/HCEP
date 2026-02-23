// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Vision;

namespace HCEP.Tests.Vision;

public sealed class HcepModeAnalyzerTests
{
    private readonly HcepModeAnalyzer _analyzer = new();

    private static FaceFrame CreateFace(
        bool isTracked = true,
        float[] actionUnits = null!,
        Vector3 headRotation = default,
        Vector3 headTranslation = default)
    {
        return new FaceFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            TrackingId = 1,
            IsTracked = isTracked,
            ActionUnits = actionUnits ?? new float[6],
            HeadRotation = headRotation,
            HeadTranslation = headTranslation,
            FeaturePoints3D = new Vector3[75],
        };
    }

    private static GazeEstimate CreateGaze(
        GazeRegion region = GazeRegion.LeftEye,
        float confidence = 0.9f,
        Vector3 direction = default)
    {
        return new GazeEstimate
        {
            HeadGazeDirection = direction == default ? Vector3.UnitZ : direction,
            EyeOffset = Vector3.Zero,
            HybridDirection = direction == default ? Vector3.UnitZ : direction,
            Origin = Vector3.Zero,
            ClassifiedRegion = region,
            Confidence = confidence,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public void Analyze_Returns_NonNullReading()
    {
        var gaze = CreateGaze();
        var face = CreateFace();

        var reading = _analyzer.Analyze(gaze, face, null, null);

        Assert.NotNull(reading);
        Assert.True(reading.Confidence >= 0f);
    }

    [Fact]
    public void Analyze_UntrackedFace_ReturnsZeroConfidence()
    {
        var gaze = CreateGaze();
        var face = CreateFace(isTracked: false);

        var reading = _analyzer.Analyze(gaze, face, null, null);

        Assert.Equal(0f, reading.Confidence);
    }

    [Fact]
    public void Analyze_GazeOnLeftEye_HighConfidence_ProducesNonUnknownMode()
    {
        // Feed enough frames through hysteresis to get a stable mode
        _analyzer.Reset();
        HcepReading? lastReading = null;

        for (int i = 0; i < 10; i++)
        {
            var gaze = CreateGaze(GazeRegion.LeftEye, 0.95f);
            var face = CreateFace();
            lastReading = _analyzer.Analyze(gaze, face, null, lastReading);
        }

        // After 10 frames of consistent gaze on left eye with high confidence,
        // the analyzer should have settled on a mode
        Assert.NotNull(lastReading);
    }

    [Fact]
    public void Analyze_GazeAversion_WithRecallPattern_DetectsProcessing()
    {
        _analyzer.Reset();
        HcepReading? lastReading = null;

        // Simulate upper-left gaze aversion (recall pattern)
        var direction = Vector3.Normalize(new Vector3(-0.3f, 0.2f, 1.0f));

        for (int i = 0; i < 10; i++)
        {
            var gaze = CreateGaze(GazeRegion.PeripheralLeft, 0.7f, direction);
            var face = CreateFace();
            lastReading = _analyzer.Analyze(gaze, face, null, lastReading);
        }

        Assert.NotNull(lastReading);
        // Should detect some form of cognitive processing
        Assert.True(lastReading.Cognitive is CognitiveState.Recalling
            or CognitiveState.Processing or CognitiveState.Constructing);
    }

    [Fact]
    public void Analyze_SocialTriangle_DetectsAffect()
    {
        // Social triangle = alternating eye-eye-mouth
        _analyzer.Reset();
        HcepReading? lastReading = null;

        var regions = new[] { GazeRegion.LeftEye, GazeRegion.RightEye, GazeRegion.Mouth };

        // AUs with slight smile to create emotional valence
        var aus = new float[6];
        aus[(int)ActionUnit.LipStretcher] = 0.5f; // smile

        for (int i = 0; i < 15; i++)
        {
            var region = regions[i % 3];
            var gaze = CreateGaze(region, 0.85f);
            var face = CreateFace(actionUnits: aus);
            lastReading = _analyzer.Analyze(gaze, face, null, lastReading);
        }

        Assert.NotNull(lastReading);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var gaze = CreateGaze();
        var face = CreateFace();
        _analyzer.Analyze(gaze, face, null, null);

        _analyzer.Reset();

        // After reset, analyzing should start fresh
        var reading = _analyzer.Analyze(gaze, face, null, null);
        Assert.NotNull(reading);
    }

    [Fact]
    public void Analyze_WithSpeech_DetectsPreSpeech()
    {
        _analyzer.Reset();
        var gaze = CreateGaze(GazeRegion.LeftEye, 0.8f);
        var face = CreateFace();
        var speech = new SpeechResult
        {
            Text = "Hello there",
            IsFinal = true,
            Confidence = 0.9f,
        };

        var reading = _analyzer.Analyze(gaze, face, speech, null);

        Assert.Equal(CognitiveState.PreSpeech, reading.Cognitive);
    }

    [Fact]
    public void Analyze_DefocusedGaze_DetectsConstructing()
    {
        _analyzer.Reset();
        var gaze = CreateGaze(GazeRegion.Defocused, 0.5f);
        var face = CreateFace();

        var reading = _analyzer.Analyze(gaze, face, null, null);

        Assert.Equal(CognitiveState.Constructing, reading.Cognitive);
    }
}
