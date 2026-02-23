// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Models;

namespace HCEP.Tests.Core;

public sealed class CoreModelsTests
{
    [Fact]
    public void HcepReading_Empty_HasDefaultValues()
    {
        var empty = HcepReading.Empty;

        Assert.Equal(HcepMode.Unknown, empty.Mode);
        Assert.Equal(GazeRegion.Unknown, empty.Region);
        Assert.Equal(CognitiveState.Unknown, empty.Cognitive);
        Assert.Equal(EmotionalValence.Unknown, empty.Valence);
        Assert.Equal(0f, empty.Confidence);
        Assert.Equal(-1, empty.PersonId);
    }

    [Fact]
    public void Anthropometrics_MeanIpd_Is63mm()
    {
        Assert.Equal(63.0f, Anthropometrics.MeanIpdMm);
    }

    [Fact]
    public void Anthropometrics_CanonicalFaceModel_Has6Points()
    {
        Assert.Equal(6, Anthropometrics.CanonicalFaceModel.Length);
    }

    [Fact]
    public void Anthropometrics_NoseTip_IsOrigin()
    {
        Assert.Equal(Vector3.Zero, Anthropometrics.CanonicalFaceModel[0]);
    }

    [Fact]
    public void FaceFrame_CyclopeanPoint_IsMidpointOfPupils()
    {
        var points3D = new Vector3[75];
        points3D[69] = new Vector3(-30, 0, 0);  // Left pupil
        points3D[73] = new Vector3(30, 0, 0);   // Right pupil

        var face = new FaceFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            IsTracked = true,
            FeaturePoints3D = points3D,
        };

        var cyclopean = face.CyclopeanPoint3D;
        Assert.Equal(0f, cyclopean.X, 0.01f);
        Assert.Equal(0f, cyclopean.Y, 0.01f);
    }

    [Fact]
    public void FaceFrame_Pupils_WithTooFewPoints_ReturnZero()
    {
        var face = new FaceFrame
        {
            Timestamp = DateTimeOffset.UtcNow,
            IsTracked = true,
            FeaturePoints3D = new Vector3[10], // Too few for pupils
        };

        Assert.Equal(Vector3.Zero, face.LeftPupil3D);
        Assert.Equal(Vector3.Zero, face.RightPupil3D);
    }

    [Fact]
    public void GazeEstimate_RequiredProperties_CanBeSet()
    {
        var estimate = new GazeEstimate
        {
            HeadGazeDirection = Vector3.UnitZ,
            EyeOffset = Vector3.Zero,
            HybridDirection = Vector3.UnitZ,
            Origin = new Vector3(0, 0, 0.5f),
            ClassifiedRegion = GazeRegion.LeftEye,
            Confidence = 0.95f,
        };

        Assert.Equal(GazeRegion.LeftEye, estimate.ClassifiedRegion);
        Assert.Equal(0.95f, estimate.Confidence);
    }

    [Fact]
    public void LlmExchange_CanBeCreated()
    {
        var exchange = new LlmExchange
        {
            UserMessage = "Test question",
            Response = "Test response",
            ModelId = "gpt-5-mini",
            IsLocal = false,
            Latency = TimeSpan.FromMilliseconds(250),
        };

        Assert.Equal("Test question", exchange.UserMessage);
        Assert.False(exchange.IsLocal);
    }

    [Fact]
    public void TrackedPerson_Defaults()
    {
        var person = new TrackedPerson
        {
            TrackingId = 42,
        };

        Assert.Equal(42, person.TrackingId);
        Assert.Null(person.IdentityName);
        Assert.Equal(TrackingState.NotTracked, person.State);
        Assert.Null(person.LatestHcep);
    }

    [Fact]
    public void SpeechResult_RequiredProperties()
    {
        var result = new SpeechResult
        {
            Text = "Hello world",
            IsFinal = true,
            Confidence = 0.88f,
            Language = "en",
        };

        Assert.Equal("Hello world", result.Text);
        Assert.True(result.IsFinal);
        Assert.Equal("en", result.Language);
    }

    [Theory]
    [InlineData(HcepMode.Logic)]
    [InlineData(HcepMode.Affect)]
    [InlineData(HcepMode.Spirit)]
    [InlineData(HcepMode.Heart)]
    [InlineData(HcepMode.Think)]
    public void HcepMode_AllValues_AreDefined(HcepMode mode)
    {
        Assert.True(Enum.IsDefined(mode));
    }

    [Theory]
    [InlineData(GazeRegion.LeftEye)]
    [InlineData(GazeRegion.RightEye)]
    [InlineData(GazeRegion.NasalBridge)]
    [InlineData(GazeRegion.Mouth)]
    [InlineData(GazeRegion.PeripheralLeft)]
    [InlineData(GazeRegion.Defocused)]
    public void GazeRegion_KeyValues_AreDefined(GazeRegion region)
    {
        Assert.True(Enum.IsDefined(region));
    }
}
