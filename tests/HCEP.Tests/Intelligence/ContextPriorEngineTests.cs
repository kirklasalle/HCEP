// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: ContextPriorEngine
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Models;
using HCEP.Intelligence;

namespace HCEP.Tests.Intelligence;

public sealed class ContextPriorEngineTests
{
    private readonly ContextPriorEngine _engine = new();

    private static ContextSnapshot BaseContext() => new()
    {
        TimeOfDay = TimeOfDayCategory.Morning,
        DayType = DayType.Weekday,
        Environment = EnvironmentType.Office,
        Privacy = SituationPrivacy.Public,
    };

    [Fact]
    public void NeutralContext_ReturnsNeutralPrior()
    {
        var context = BaseContext();
        var prior = _engine.ComputePrior(context);

        Assert.Equal(1f, prior.HysteresisMultiplier);
        Assert.Equal(0.4f, prior.ModeTransitionMinConfidence);
        Assert.Equal(0f, prior.ThinkModePriorBoost);
        Assert.Equal(0f, prior.HeartModePriorBoost);
        Assert.Equal(0f, prior.SilenceBias);
    }

    [Fact]
    public void NightContext_IncreasesHysteresisAndSilenceBias()
    {
        var context = BaseContext() with { TimeOfDay = TimeOfDayCategory.Night };
        var prior = _engine.ComputePrior(context);

        Assert.True(prior.HysteresisMultiplier > 1f, "Hysteresis should increase at night");
        Assert.True(prior.SilenceBias > 0f, "Silence bias should increase at night");
    }

    [Fact]
    public void BedroomAtNight_BoostsHeartAndSilence()
    {
        var context = BaseContext() with
        {
            Environment = EnvironmentType.Bedroom,
            TimeOfDay = TimeOfDayCategory.Night,
        };
        var prior = _engine.ComputePrior(context);

        Assert.True(prior.HeartModePriorBoost > 0f);
        Assert.True(prior.SilenceBias >= 0.4f);  // night (0.15) + bedroom (0.25)
    }

    [Fact]
    public void LaboratoryContext_BoostsThinkAndLowersMinConfidence()
    {
        var context = BaseContext() with { Environment = EnvironmentType.Laboratory };
        var prior = _engine.ComputePrior(context);

        Assert.True(prior.ThinkModePriorBoost > 0f);
        Assert.True(prior.ModeTransitionMinConfidence < 0.4f);
    }

    [Fact]
    public void SilenceProtocolAlreadyActive_AmplifiesSilenceBias()
    {
        var context = BaseContext() with { SilenceProtocolActive = true };
        var prior = _engine.ComputePrior(context);

        Assert.True(prior.SilenceBias >= 0.3f);
    }

    [Fact]
    public void AllPriorFields_AreWithinValidRanges()
    {
        // Exercise multiple context combinations and verify all outputs are in range
        var contexts = new[]
        {
            BaseContext(),
            BaseContext() with { TimeOfDay = TimeOfDayCategory.Night, Environment = EnvironmentType.Bedroom },
            BaseContext() with { Environment = EnvironmentType.Studio, Privacy = SituationPrivacy.Private },
        };
        foreach (var ctx in contexts)
        {
            var prior = _engine.ComputePrior(ctx);
            Assert.InRange(prior.ThinkModePriorBoost, 0f, 0.5f);
            Assert.InRange(prior.HeartModePriorBoost, 0f, 0.5f);
            Assert.InRange(prior.SilenceBias, 0f, 1f);
            Assert.InRange(prior.HysteresisMultiplier, 1f, 3f);
            Assert.InRange(prior.ModeTransitionMinConfidence, 0.2f, 0.6f);
        }
    }

    [Fact]
    public void ShadowMode_SetOnPrior_WhenEnabled()
    {
        _engine.ShadowMode = true;
        var prior = _engine.ComputePrior(BaseContext());
        Assert.True(prior.ShadowModeOnly);
        _engine.ShadowMode = false;
    }
}
