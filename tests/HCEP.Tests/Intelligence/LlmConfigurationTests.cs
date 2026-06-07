// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Enums;
using HCEP.Core.Models;
using Xunit;

namespace HCEP.Tests.Intelligence;

public sealed class LlmConfigurationTests
{
    [Fact]
    public void LlmConfiguration_DefaultValues_AreCorrect()
    {
        var config = new LlmConfiguration();

        Assert.True(config.PreferLocal);
        Assert.Equal(LocalEngineType.Ollama, config.ActiveLocalEngine);
        Assert.Equal(CloudProviderType.OpenAI, config.ActiveCloudProvider);
        Assert.Equal(0.5f, config.EmulationBlendWeight);
        Assert.Equal(300, config.ReflectionDelayMs);
        Assert.True(config.SyncBlinksToUser);
    }

    [Fact]
    public void LlmConfiguration_CanModifyProperties()
    {
        var config = new LlmConfiguration
        {
            PreferLocal = false,
            ActiveLocalEngine = LocalEngineType.LlamaCpp,
            ActiveCloudProvider = CloudProviderType.Gemini,
            EmulationBlendWeight = 0.85f,
            ReflectionDelayMs = 450,
            SyncBlinksToUser = false
        };

        Assert.False(config.PreferLocal);
        Assert.Equal(LocalEngineType.LlamaCpp, config.ActiveLocalEngine);
        Assert.Equal(CloudProviderType.Gemini, config.ActiveCloudProvider);
        Assert.Equal(0.85f, config.EmulationBlendWeight);
        Assert.Equal(450, config.ReflectionDelayMs);
        Assert.False(config.SyncBlinksToUser);
    }
}
