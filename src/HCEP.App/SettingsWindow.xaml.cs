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
using System.Windows;
using System.Windows.Controls;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Intelligence;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// Interaction logic for SettingsWindow.xaml
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly HybridLlmEngine? _llmEngine;
    private readonly LlmConfiguration _configCopy;
    private CloudProviderType _currentSelectedCloudProvider;
    private readonly TimeContextProvider? _contextProvider;
    /// <summary>True while Loaded is populating controls; suppresses SelectionChanged save.</summary>
    private bool _isInitializing;
    private readonly ILogger<SettingsWindow> _logger;

    public SettingsWindow(ILlmEngine llmEngine, TimeContextProvider contextProvider, ILogger<SettingsWindow> logger)
    {
        InitializeComponent();
        _llmEngine = llmEngine as HybridLlmEngine;
        _contextProvider = contextProvider;
        _logger = logger;

        // Create a deep copy of the configuration so we can discard edits on Cancel
        if (_llmEngine is not null)
        {
            _configCopy = CloneConfig(_llmEngine.Configuration);
        }
        else
        {
            _configCopy = new LlmConfiguration();
        }

        Loaded += SettingsWindow_Loaded;
        CloudProviderCombo.SelectionChanged += CloudProviderCombo_SelectionChanged;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Local Engines
        LocalEngineCombo.SelectedIndex = _configCopy.ActiveLocalEngine == LocalEngineType.LlamaCpp ? 1 : 0;
        OllamaUrlText.Text = _configCopy.Ollama.BaseUrl;
        OllamaModelText.Text = _configCopy.Ollama.Model;
        LlamaCppUrlText.Text = _configCopy.LlamaCpp.BaseUrl;

        // 2. Cloud Provider Selection
        _currentSelectedCloudProvider = _configCopy.ActiveCloudProvider;
        _isInitializing = true;         // prevent SelectionChanged from wiping fields
        try
        {
            CloudProviderCombo.SelectedIndex = (int)_configCopy.ActiveCloudProvider;
        }
        finally
        {
            _isInitializing = false;
        }
        LoadCloudProviderFields(_currentSelectedCloudProvider);

        // 3. Happyface & Emulation
        PreferLocalCheck.IsChecked = _configCopy.PreferLocal;
        AgenticToolCheck.IsChecked = _llmEngine?.AgenticToolUseEnabled ?? true;
        EmuWeightSlider.Value = _configCopy.EmulationBlendWeight;
        DelaySlider.Value = _configCopy.ReflectionDelayMs;
        SyncBlinksCheck.IsChecked = _configCopy.SyncBlinksToUser;

        // 4. Context (Phase 14)
        if (_contextProvider is not null)
        {
            EnvironmentCombo.SelectedIndex = (int)_contextProvider.Environment;
            ActivityCombo.SelectedIndex = (int)_contextProvider.Activity;
            PrivacyCombo.SelectedIndex = (int)_contextProvider.Privacy;
            LocationLabelText.Text = _contextProvider.UserDefinedLocation ?? string.Empty;
        }
    }

    private void CloudProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: during Loaded, SelectedIndex is set programmatically.
        // Without this guard, SaveCloudProviderFields runs with empty text boxes
        // and overwrites the model / API key / base URL to empty strings.
        if (_isInitializing) return;

        // Save fields of the previous provider before switching
        SaveCloudProviderFields(_currentSelectedCloudProvider);

        // Switch to the newly selected provider
        var newProvider = (CloudProviderType)CloudProviderCombo.SelectedIndex;
        _currentSelectedCloudProvider = newProvider;
        LoadCloudProviderFields(newProvider);
    }

    private void LoadCloudProviderFields(CloudProviderType provider)
    {
        CloudProviderSettings settings = GetCloudSettings(provider);
        CloudModelText.Text  = settings.Model;
        CloudUrlText.Text    = settings.BaseUrl;
        // API key: read from Windows Credential Manager (persistent across sessions)
        // Falls back to the in-memory configCopy value (from a previous Save this session)
        string? wcmKey = WindowsCredentialStore.LoadApiKey(WindowsCredentialStore.GetWcmTarget(provider));
        CloudApiKeyText.Password = wcmKey ?? settings.ApiKey;
    }

    private void SaveCloudProviderFields(CloudProviderType provider)
    {
        CloudProviderSettings settings = GetCloudSettings(provider);
        settings.Model   = CloudModelText.Text.Trim();
        settings.BaseUrl = CloudUrlText.Text.Trim();

        // Persist API key to Windows Credential Manager (encrypted, survives restarts)
        string apiKey = CloudApiKeyText.Password.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            string target = WindowsCredentialStore.GetWcmTarget(provider);
            WindowsCredentialStore.SaveApiKey(target, apiKey);
            settings.ApiKey = apiKey; // mirror into in-memory config for this session
            _logger.LogDebug("API key saved to WCM for provider={Provider} target={Target}",
                provider, target);
        }
    }

    private CloudProviderSettings GetCloudSettings(CloudProviderType provider)
    {
        return provider switch
        {
            CloudProviderType.Anthropic => _configCopy.Anthropic,
            CloudProviderType.Gemini => _configCopy.Gemini,
            CloudProviderType.Mistral => _configCopy.Mistral,
            CloudProviderType.xAI => _configCopy.xAI,
            CloudProviderType.Cohere => _configCopy.Cohere,
            CloudProviderType.OpenRouter => _configCopy.OpenRouter,
            CloudProviderType.DeepSeek => _configCopy.DeepSeek,
            CloudProviderType.Groq => _configCopy.Groq,
            CloudProviderType.TogetherAI => _configCopy.TogetherAI,
            CloudProviderType.FireworksAI => _configCopy.FireworksAI,
            CloudProviderType.Perplexity => _configCopy.Perplexity,
            CloudProviderType.AI21Labs => _configCopy.AI21Labs,
            CloudProviderType.Replicate => _configCopy.Replicate,
            CloudProviderType.HuggingFace => _configCopy.HuggingFace,
            CloudProviderType.AzureOpenAI => _configCopy.AzureOpenAI,
            CloudProviderType.AmazonBedrock => _configCopy.AmazonBedrock,
            CloudProviderType.NvidiaNIM => _configCopy.NvidiaNIM,
            CloudProviderType.Cerebras => _configCopy.Cerebras,
            CloudProviderType.MoonshotAI => _configCopy.MoonshotAI,
            _ => _configCopy.OpenAI
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("SettingsWindow.Save_Click started — provider={Provider} preferLocal={PreferLocal}",
            _currentSelectedCloudProvider, PreferLocalCheck.IsChecked);

        if (_llmEngine is null)
        {
            _logger.LogWarning("Save_Click: _llmEngine is null — closing without saving");
            Close();
            return;
        }

        // Save current cloud provider fields
        SaveCloudProviderFields(_currentSelectedCloudProvider);

        // Apply local engine settings
        _configCopy.ActiveLocalEngine = LocalEngineCombo.SelectedIndex == 1 ? LocalEngineType.LlamaCpp : LocalEngineType.Ollama;
        _configCopy.Ollama.BaseUrl = OllamaUrlText.Text.Trim();
        _configCopy.Ollama.Model = OllamaModelText.Text.Trim();
        _configCopy.LlamaCpp.BaseUrl = LlamaCppUrlText.Text.Trim();

        // Apply cloud settings
        _configCopy.ActiveCloudProvider = (CloudProviderType)CloudProviderCombo.SelectedIndex;

        // Apply Happyface & Emulation settings
        _configCopy.PreferLocal = PreferLocalCheck.IsChecked == true;
        _llmEngine.AgenticToolUseEnabled = AgenticToolCheck.IsChecked == true;
        _configCopy.EmulationBlendWeight = (float)EmuWeightSlider.Value;
        _configCopy.ReflectionDelayMs = (int)DelaySlider.Value;
        _configCopy.SyncBlinksToUser = SyncBlinksCheck.IsChecked == true;

        // Log every setting at trace level for diagnostics
        var activeSettings = GetCloudSettings(_configCopy.ActiveCloudProvider);
        _logger.LogTrace(
            "Settings being saved: provider={Provider} model={Model} url={BaseUrl} " +
            "hasKey={HasKey} preferLocal={PreferLocal} agentic={Agentic} " +
            "emulation={Emulation:F2} delay={Delay}ms syncBlinks={SyncBlinks} " +
            "localEngine={LocalEngine} ollamaModel={OllamaModel}",
            _configCopy.ActiveCloudProvider,
            activeSettings.Model,
            activeSettings.BaseUrl,
            !string.IsNullOrEmpty(activeSettings.ApiKey),
            _configCopy.PreferLocal,
            _llmEngine.AgenticToolUseEnabled,
            _configCopy.EmulationBlendWeight,
            _configCopy.ReflectionDelayMs,
            _configCopy.SyncBlinksToUser,
            _configCopy.ActiveLocalEngine,
            _configCopy.Ollama.Model);

        // Apply context settings (Phase 14)
        if (_contextProvider is not null)
        {
            _contextProvider.Environment = (HCEP.Core.Models.EnvironmentType)EnvironmentCombo.SelectedIndex;
            _contextProvider.Activity = (HCEP.Core.Models.SituationActivity)ActivityCombo.SelectedIndex;
            _contextProvider.Privacy = (HCEP.Core.Models.SituationPrivacy)PrivacyCombo.SelectedIndex;
            _contextProvider.UserDefinedLocation = LocationLabelText.Text.Trim().Length > 0
                ? LocationLabelText.Text.Trim() : null;
        }

        // Copy everything back to the engine
        CopyConfig(_configCopy, _llmEngine.Configuration);

        _logger.LogInformation(
            "Settings saved — provider={Provider} model={Model} preferLocal={PreferLocal}",
            _configCopy.ActiveCloudProvider,
            GetCloudSettings(_configCopy.ActiveCloudProvider).Model,
            _configCopy.PreferLocal);

        // Persist settings to disk so they survive app restarts
        HCEP.Intelligence.SettingsPersistence.Save(_llmEngine.Configuration, _logger);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ── Helper Cloning Operations ───────────────────────────────

    private static LlmConfiguration CloneConfig(LlmConfiguration src)
    {
        var copy = new LlmConfiguration
        {
            PreferLocal = src.PreferLocal,
            ActiveLocalEngine = src.ActiveLocalEngine,
            ActiveCloudProvider = src.ActiveCloudProvider,
            EmulationBlendWeight = src.EmulationBlendWeight,
            ReflectionDelayMs = src.ReflectionDelayMs,
            SyncBlinksToUser = src.SyncBlinksToUser
        };

        CloneSettings(src.Ollama, copy.Ollama);
        CloneSettings(src.LlamaCpp, copy.LlamaCpp);
        CloneSettings(src.OpenAI, copy.OpenAI);
        CloneSettings(src.Anthropic, copy.Anthropic);
        CloneSettings(src.Gemini, copy.Gemini);
        CloneSettings(src.Mistral, copy.Mistral);
        CloneSettings(src.xAI, copy.xAI);
        CloneSettings(src.Cohere, copy.Cohere);
        CloneSettings(src.OpenRouter, copy.OpenRouter);
        CloneSettings(src.DeepSeek, copy.DeepSeek);
        CloneSettings(src.Groq, copy.Groq);
        CloneSettings(src.TogetherAI, copy.TogetherAI);
        CloneSettings(src.FireworksAI, copy.FireworksAI);
        CloneSettings(src.Perplexity, copy.Perplexity);
        CloneSettings(src.AI21Labs, copy.AI21Labs);
        CloneSettings(src.Replicate, copy.Replicate);
        CloneSettings(src.HuggingFace, copy.HuggingFace);
        CloneSettings(src.AzureOpenAI, copy.AzureOpenAI);
        CloneSettings(src.AmazonBedrock, copy.AmazonBedrock);
        CloneSettings(src.NvidiaNIM, copy.NvidiaNIM);
        CloneSettings(src.Cerebras, copy.Cerebras);
        CloneSettings(src.MoonshotAI, copy.MoonshotAI);

        return copy;
    }

    private static void CopyConfig(LlmConfiguration src, LlmConfiguration dest)
    {
        dest.PreferLocal = src.PreferLocal;
        dest.ActiveLocalEngine = src.ActiveLocalEngine;
        dest.ActiveCloudProvider = src.ActiveCloudProvider;
        dest.EmulationBlendWeight = src.EmulationBlendWeight;
        dest.ReflectionDelayMs = src.ReflectionDelayMs;
        dest.SyncBlinksToUser = src.SyncBlinksToUser;

        CloneSettings(src.Ollama, dest.Ollama);
        CloneSettings(src.LlamaCpp, dest.LlamaCpp);
        CloneSettings(src.OpenAI, dest.OpenAI);
        CloneSettings(src.Anthropic, dest.Anthropic);
        CloneSettings(src.Gemini, dest.Gemini);
        CloneSettings(src.Mistral, dest.Mistral);
        CloneSettings(src.xAI, dest.xAI);
        CloneSettings(src.Cohere, dest.Cohere);
        CloneSettings(src.OpenRouter, dest.OpenRouter);
        CloneSettings(src.DeepSeek, dest.DeepSeek);
        CloneSettings(src.Groq, dest.Groq);
        CloneSettings(src.TogetherAI, dest.TogetherAI);
        CloneSettings(src.FireworksAI, dest.FireworksAI);
        CloneSettings(src.Perplexity, dest.Perplexity);
        CloneSettings(src.AI21Labs, dest.AI21Labs);
        CloneSettings(src.Replicate, dest.Replicate);
        CloneSettings(src.HuggingFace, dest.HuggingFace);
        CloneSettings(src.AzureOpenAI, dest.AzureOpenAI);
        CloneSettings(src.AmazonBedrock, dest.AmazonBedrock);
        CloneSettings(src.NvidiaNIM, dest.NvidiaNIM);
        CloneSettings(src.Cerebras, dest.Cerebras);
        CloneSettings(src.MoonshotAI, dest.MoonshotAI);
    }

    private static void CloneSettings(OllamaSettings src, OllamaSettings dest)
    {
        dest.Enabled = src.Enabled;
        dest.BaseUrl = src.BaseUrl;
        dest.Model = src.Model;
        dest.Temperature = src.Temperature;
    }

    private static void CloneSettings(LlamaCppSettings src, LlamaCppSettings dest)
    {
        dest.Enabled = src.Enabled;
        dest.BaseUrl = src.BaseUrl;
        dest.Model = src.Model;
        dest.Temperature = src.Temperature;
        dest.UseOaiCompatibleEndpoint = src.UseOaiCompatibleEndpoint;
    }

    private static void CloneSettings(CloudProviderSettings src, CloudProviderSettings dest)
    {
        dest.Enabled = src.Enabled;
        dest.BaseUrl = src.BaseUrl;
        dest.Model = src.Model;
        dest.ApiKey = src.ApiKey;
        dest.Temperature = src.Temperature;
    }
}
