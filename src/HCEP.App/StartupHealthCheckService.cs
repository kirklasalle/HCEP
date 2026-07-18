// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Interfaces;
using HCEP.Intelligence;
using Microsoft.Extensions.Logging;
using System.IO;

namespace HCEP.App;

public enum StartupHealthSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record StartupHealthCheckItem(StartupHealthSeverity Severity, string Title, string Detail);

public sealed record StartupHealthReport(IReadOnlyList<StartupHealthCheckItem> Items)
{
    public bool HasWarningsOrCritical => Items.Any(item => item.Severity != StartupHealthSeverity.Info);
}

/// <summary>
/// Performs an explicit startup health pass across the active sensor route,
/// settings persistence surface, active LLM routing configuration, and plugin
/// API configuration. The result is logged and can be surfaced to operators.
/// </summary>
public sealed class StartupHealthCheckService
{
    private readonly ISensorSource _sensor;
    private readonly ILlmEngine _llmEngine;
    private readonly ILogger<StartupHealthCheckService> _logger;

    public StartupHealthCheckService(
        ISensorSource sensor,
        ILlmEngine llmEngine,
        ILogger<StartupHealthCheckService> logger)
    {
        _sensor = sensor;
        _llmEngine = llmEngine;
        _logger = logger;
    }

    public async Task<StartupHealthReport> RunAsync(CancellationToken ct = default)
    {
        var items = new List<StartupHealthCheckItem>();

        // Settings path / directory
        try
        {
            string settingsPath = SettingsPersistence.GetSettingsFilePath();
            string dir = Path.GetDirectoryName(settingsPath) ?? settingsPath;
            Directory.CreateDirectory(dir);
            items.Add(new StartupHealthCheckItem(
                StartupHealthSeverity.Info,
                "Settings persistence",
                $"Resolved settings path: {settingsPath}"));
        }
        catch (Exception ex)
        {
            items.Add(new StartupHealthCheckItem(
                StartupHealthSeverity.Critical,
                "Settings persistence",
                $"Settings path could not be prepared: {ex.Message}"));
        }

        // Sensor route
        try
        {
            string sensorName = _sensor.GetType().Name;
            int elevation = _sensor.ElevationAngle;
            items.Add(new StartupHealthCheckItem(
                StartupHealthSeverity.Info,
                "Sensor route",
                $"Active sensor source: {sensorName}; elevation={elevation}°"));
        }
        catch (Exception ex)
        {
            items.Add(new StartupHealthCheckItem(
                StartupHealthSeverity.Warning,
                "Sensor route",
                $"Sensor source is registered but did not respond cleanly during startup probing: {ex.Message}"));
        }

        // LLM route readiness
        if (_llmEngine is HybridLlmEngine hybrid)
        {
            bool localAvailable = false;
            try
            {
                localAvailable = await hybrid.IsLocalAvailableAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                items.Add(new StartupHealthCheckItem(
                    StartupHealthSeverity.Warning,
                    "Local LLM route",
                    $"Local route check failed: {ex.Message}"));
            }

            string activeCloudKey = GetActiveCloudKey(hybrid);
            bool hasCloudKey = !string.IsNullOrWhiteSpace(activeCloudKey);

            if (!localAvailable && !hasCloudKey)
            {
                items.Add(new StartupHealthCheckItem(
                    StartupHealthSeverity.Critical,
                    "LLM routing",
                    "Neither a reachable local route nor a configured cloud API key is available. Chat will degrade to fallback responses."));
            }
            else if (!localAvailable && hybrid.Configuration.PreferLocal)
            {
                items.Add(new StartupHealthCheckItem(
                    StartupHealthSeverity.Warning,
                    "LLM routing",
                    $"PreferLocal is enabled, but the active local engine is unreachable. Cloud fallback {(hasCloudKey ? "is" : "is not")} configured."));
            }
            else
            {
                items.Add(new StartupHealthCheckItem(
                    StartupHealthSeverity.Info,
                    "LLM routing",
                    $"Local available={localAvailable}; cloud key present={hasCloudKey}; preferLocal={hybrid.Configuration.PreferLocal}."));
            }
        }

        // Plugin API config surface
        string pluginBind = Environment.GetEnvironmentVariable("HCEP_PLUGIN_BIND") ?? "0.0.0.0";
        string pluginPort = Environment.GetEnvironmentVariable("HCEP_PLUGIN_PORT") ?? "5000";
        bool hasPluginKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HCEP_PLUGIN_API_KEY"));

        if (!int.TryParse(pluginPort, out int parsedPort) || parsedPort is <= 0 or > 65535)
        {
            items.Add(new StartupHealthCheckItem(
                StartupHealthSeverity.Critical,
                "Plugin API config",
                $"HCEP_PLUGIN_PORT is invalid: '{pluginPort}'."));
        }
        else
        {
            items.Add(new StartupHealthCheckItem(
                hasPluginKey ? StartupHealthSeverity.Info : StartupHealthSeverity.Warning,
                "Plugin API config",
                $"Bind={pluginBind}; port={parsedPort}; auth={(hasPluginKey ? "enabled" : "disabled")}."));
        }

        foreach (var item in items)
        {
            switch (item.Severity)
            {
                case StartupHealthSeverity.Info:
                    _logger.LogInformation("Startup health — {Title}: {Detail}", item.Title, item.Detail);
                    break;
                case StartupHealthSeverity.Warning:
                    _logger.LogWarning("Startup health — {Title}: {Detail}", item.Title, item.Detail);
                    break;
                case StartupHealthSeverity.Critical:
                    _logger.LogCritical("Startup health — {Title}: {Detail}", item.Title, item.Detail);
                    break;
            }
        }

        return new StartupHealthReport(items);
    }

    private static string GetActiveCloudKey(HybridLlmEngine hybrid)
    {
        // Force the same runtime resolution path used by PromptAsync.
        return hybrid.Configuration.ActiveCloudProvider switch
        {
            HCEP.Core.Models.CloudProviderType.Anthropic => hybrid.Configuration.Anthropic.ApiKey,
            HCEP.Core.Models.CloudProviderType.Gemini => hybrid.Configuration.Gemini.ApiKey,
            HCEP.Core.Models.CloudProviderType.Mistral => hybrid.Configuration.Mistral.ApiKey,
            HCEP.Core.Models.CloudProviderType.xAI => hybrid.Configuration.xAI.ApiKey,
            HCEP.Core.Models.CloudProviderType.Cohere => hybrid.Configuration.Cohere.ApiKey,
            HCEP.Core.Models.CloudProviderType.OpenRouter => hybrid.Configuration.OpenRouter.ApiKey,
            HCEP.Core.Models.CloudProviderType.DeepSeek => hybrid.Configuration.DeepSeek.ApiKey,
            HCEP.Core.Models.CloudProviderType.Groq => hybrid.Configuration.Groq.ApiKey,
            HCEP.Core.Models.CloudProviderType.TogetherAI => hybrid.Configuration.TogetherAI.ApiKey,
            HCEP.Core.Models.CloudProviderType.FireworksAI => hybrid.Configuration.FireworksAI.ApiKey,
            HCEP.Core.Models.CloudProviderType.Perplexity => hybrid.Configuration.Perplexity.ApiKey,
            HCEP.Core.Models.CloudProviderType.AI21Labs => hybrid.Configuration.AI21Labs.ApiKey,
            HCEP.Core.Models.CloudProviderType.Replicate => hybrid.Configuration.Replicate.ApiKey,
            HCEP.Core.Models.CloudProviderType.HuggingFace => hybrid.Configuration.HuggingFace.ApiKey,
            HCEP.Core.Models.CloudProviderType.AzureOpenAI => hybrid.Configuration.AzureOpenAI.ApiKey,
            HCEP.Core.Models.CloudProviderType.AmazonBedrock => hybrid.Configuration.AmazonBedrock.ApiKey,
            HCEP.Core.Models.CloudProviderType.NvidiaNIM => hybrid.Configuration.NvidiaNIM.ApiKey,
            HCEP.Core.Models.CloudProviderType.Cerebras => hybrid.Configuration.Cerebras.ApiKey,
            HCEP.Core.Models.CloudProviderType.MoonshotAI => hybrid.Configuration.MoonshotAI.ApiKey,
            _ => hybrid.Configuration.OpenAI.ApiKey,
        };
    }
}