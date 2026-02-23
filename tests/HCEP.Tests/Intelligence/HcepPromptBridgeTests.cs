// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Intelligence;

namespace HCEP.Tests.Intelligence;

public sealed class HcepPromptBridgeTests
{
    [Fact]
    public void GenerateContext_IncludesMode()
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            HcepMode.Logic,
            GazeRegion.LeftEye,
            CognitiveState.Engaged,
            EmotionalValence.Neutral,
            0.85f,
            Vector3.Zero,
            new Vector3(0, 0, 1),
            Vector3.Zero,
            1);

        string context = HcepPromptBridge.GenerateContext(reading);

        Assert.Contains("Logic", context);
        Assert.Contains("Engaged", context);
        Assert.Contains("85%", context);
    }

    [Fact]
    public void GenerateContext_WithSpeech_IncludesUtterance()
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            HcepMode.Affect,
            GazeRegion.Mouth,
            CognitiveState.PreSpeech,
            EmotionalValence.Positive,
            0.7f,
            Vector3.Zero,
            new Vector3(0, 0, 1),
            Vector3.Zero,
            1);

        var speech = new SpeechResult
        {
            Text = "I really enjoyed that.",
            Confidence = 0.9f,
            SourceAngleDeg = 15,
        };

        string context = HcepPromptBridge.GenerateContext(reading, speech);

        Assert.Contains("I really enjoyed that.", context);
        Assert.Contains("90%", context);
    }

    [Fact]
    public void GenerateContext_AffectMode_IncludesEmpathyInstruction()
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            HcepMode.Affect,
            GazeRegion.LeftEye,
            CognitiveState.Engaged,
            EmotionalValence.Positive,
            0.8f,
            Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 1);

        string context = HcepPromptBridge.GenerateContext(reading);

        Assert.Contains("empathetic", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateContext_ThinkMode_IncludesBriefInstruction()
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            HcepMode.Think,
            GazeRegion.PeripheralLeft,
            CognitiveState.Processing,
            EmotionalValence.Neutral,
            0.6f,
            Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 1);

        string context = HcepPromptBridge.GenerateContext(reading);

        Assert.Contains("brief", context, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HcepMode.Spirit, true)]   // Deep rapport → cloud
    [InlineData(HcepMode.Think, false)]    // Internal processing → local
    public void ShouldUseCloud_ModeRouting(HcepMode mode, bool expectedCloud)
    {
        var reading = new HcepReading(
            DateTimeOffset.UtcNow,
            mode,
            GazeRegion.LeftEye,
            CognitiveState.Engaged,
            EmotionalValence.Neutral,
            0.85f,
            Vector3.Zero, Vector3.UnitZ, Vector3.Zero, 1);

        // Long query to trigger Spirit→cloud
        string query = new string('x', 150);

        bool useCloud = HcepPromptBridge.ShouldUseCloud(reading, query);

        Assert.Equal(expectedCloud, useCloud);
    }
}
