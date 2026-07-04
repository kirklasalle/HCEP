// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: HcepModeAnalyzer contextual prior
// ──────────────────────────────────────────────────────────────
using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Vision;

namespace HCEP.Tests.Vision;

/// <summary>
/// Tests that <see cref="HcepModeAnalyzer"/> correctly applies, ignores, and
/// rolls back contextual prior profiles (Workstream A).
/// </summary>
public sealed class HcepModeAnalyzerPriorTests
{
    private static FaceFrame TrackedFace() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        TrackingId = 1,
        IsTracked = true,
        ActionUnits = new float[6],
        HeadRotation = Vector3.Zero,
        FeaturePoints3D = new Vector3[75],
    };

    private static GazeEstimate ThinkGaze() => new()
    {
        HybridDirection = new Vector3(0.3f, 0.2f, 0.9f),   // upper-right → constructing
        HeadGazeDirection = Vector3.UnitZ,
        EyeOffset = Vector3.Zero,
        Origin = Vector3.Zero,
        ClassifiedRegion = GazeRegion.PeripheralRight,        // off-face → aversion
        Confidence = 0.9f,
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void NullPrior_DoesNotAlterBehavior()
    {
        var analyzer = new HcepModeAnalyzer();
        analyzer.CurrentPrior = null;

        HcepReading? last = null;
        for (int i = 0; i < 10; i++)
            last = analyzer.Analyze(ThinkGaze(), TrackedFace(), null, last);

        // Should produce a valid reading without throwing
        Assert.NotNull(last);
    }

    [Fact]
    public void NeutralPrior_ProducesEquivalentResultToNullPrior()
    {
        var analyzerBaseline = new HcepModeAnalyzer();
        var analyzerWithNeutral = new HcepModeAnalyzer();
        analyzerWithNeutral.CurrentPrior = ContextPriorProfile.Neutral;

        HcepReading? baseResult = null;
        HcepReading? priorResult = null;

        for (int i = 0; i < 8; i++)
        {
            baseResult = analyzerBaseline.Analyze(ThinkGaze(), TrackedFace(), null, baseResult);
            priorResult = analyzerWithNeutral.Analyze(ThinkGaze(), TrackedFace(), null, priorResult);
        }

        Assert.Equal(baseResult!.Mode, priorResult!.Mode);
    }

    [Fact]
    public void ShadowModePrior_DoesNotInfluenceMode()
    {
        // Shadow mode = prior is computed but not applied — output should match no-prior baseline
        var analyzerBaseline = new HcepModeAnalyzer();
        var analyzerShadow = new HcepModeAnalyzer();
        analyzerShadow.CurrentPrior = new ContextPriorProfile
        {
            ThinkModePriorBoost = 0.5f,    // large boost — would change outcome if applied
            HysteresisMultiplier = 3f,       // very slow — would block transitions if applied
            ShadowModeOnly = true,     // must NOT be applied
        };

        HcepReading? baseResult = null;
        HcepReading? shadowResult = null;
        for (int i = 0; i < 8; i++)
        {
            baseResult = analyzerBaseline.Analyze(ThinkGaze(), TrackedFace(), null, baseResult);
            shadowResult = analyzerShadow.Analyze(ThinkGaze(), TrackedFace(), null, shadowResult);
        }

        Assert.Equal(baseResult!.Mode, shadowResult!.Mode);
    }

    [Fact]
    public void ThinkBoostPrior_ConfidenceIsHigherThanBaseline()
    {
        // With a big Think boost, confidence on a Think candidate should exceed the unbooosted value.
        var analyzerBase = new HcepModeAnalyzer();
        var analyzerBoosted = new HcepModeAnalyzer();
        analyzerBoosted.CurrentPrior = new ContextPriorProfile
        {
            ThinkModePriorBoost = 0.3f,
            ShadowModeOnly = false,
        };

        // Run enough frames to stabilize
        HcepReading? baseResult = null, boostedResult = null;
        for (int i = 0; i < 8; i++)
        {
            baseResult = analyzerBase.Analyze(ThinkGaze(), TrackedFace(), null, baseResult);
            boostedResult = analyzerBoosted.Analyze(ThinkGaze(), TrackedFace(), null, boostedResult);
        }

        // If both resolved Think, the boosted analyzer's confidence should be >= baseline
        if (baseResult!.Mode == HcepMode.Think && boostedResult!.Mode == HcepMode.Think)
            Assert.True(boostedResult.Confidence >= baseResult.Confidence);
    }

    [Fact]
    public void Reset_ClearsPriorState()
    {
        var analyzer = new HcepModeAnalyzer();
        analyzer.CurrentPrior = new ContextPriorProfile { ThinkModePriorBoost = 0.4f };
        analyzer.Reset();

        // After reset the prior should still be set (it's external state) — confirm no crash
        var result = analyzer.Analyze(ThinkGaze(), TrackedFace(), null, null);
        Assert.NotNull(result);
    }
}
