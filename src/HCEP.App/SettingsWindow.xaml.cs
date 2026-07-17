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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private LocalEngineType _currentSelectedLocalEngine;
    private readonly TimeContextProvider? _contextProvider;
    /// <summary>True while Loaded is populating controls; suppresses SelectionChanged save.</summary>
    private bool _isInitializing;
    private bool _isInitializingPreset;
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
        LocalEngineCombo.SelectionChanged += LocalEngineCombo_SelectionChanged;
        PresetCombo.SelectionChanged += PresetCombo_SelectionChanged;

        // Wire preset dynamic triggers
        EmuWeightSlider.ValueChanged += Control_ValueChanged;
        DelaySlider.ValueChanged += Control_ValueChanged;
        SyncBlinksCheck.Checked += Control_CheckChanged;
        SyncBlinksCheck.Unchecked += Control_CheckChanged;
        PreferLocalCheck.Checked += RoutingControl_Changed;
        PreferLocalCheck.Unchecked += RoutingControl_Changed;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            // 1. Local Engines Selection
            _currentSelectedLocalEngine = _configCopy.ActiveLocalEngine;
            LocalEngineCombo.SelectedIndex = (int)_configCopy.ActiveLocalEngine;
            LoadLocalEngineFields(_currentSelectedLocalEngine);

            // 2. Cloud Provider Selection
            _currentSelectedCloudProvider = _configCopy.ActiveCloudProvider;
            CloudProviderCombo.SelectedIndex = (int)_configCopy.ActiveCloudProvider;
            LoadCloudProviderFields(_currentSelectedCloudProvider);

            // 3. Happyface & Emulation settings
            PreferLocalCheck.IsChecked = _configCopy.PreferLocal;
            AgenticToolCheck.IsChecked = _llmEngine?.AgenticToolUseEnabled ?? true;
            EmuWeightSlider.Value = _configCopy.EmulationBlendWeight;
            DelaySlider.Value = _configCopy.ReflectionDelayMs;
            SyncBlinksCheck.IsChecked = _configCopy.SyncBlinksToUser;
            SetActivePresetFromValues();

            // 4. Context (Phase 14)
            if (_contextProvider is not null)
            {
                EnvironmentCombo.SelectedIndex = (int)_contextProvider.Environment;
                ActivityCombo.SelectedIndex = (int)_contextProvider.Activity;
                PrivacyCombo.SelectedIndex = (int)_contextProvider.Privacy;
                LocationLabelText.Text = _contextProvider.UserDefinedLocation ?? string.Empty;
            }
        }
        finally
        {
            _isInitializing = false;
        }

        UpdateRoutingSummary();
        await RefreshRuntimeStatusAsync(includeModelDiscovery: true);
    }

    private async void CloudProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
        UpdateRoutingSummary();
        await RefreshCloudProviderStatusAsync(includeModelDiscovery: true);
    }

    private void LoadCloudProviderFields(CloudProviderType provider)
    {
        CloudProviderSettings settings = GetCloudSettings(provider);
        SetComboText(CloudModelCombo, settings.Model);
        CloudUrlText.Text = settings.BaseUrl;
        // API key: read from Windows Credential Manager (persistent across sessions)
        // Falls back to the in-memory configCopy value (from a previous Save this session)
        string? wcmKey = WindowsCredentialStore.LoadApiKey(WindowsCredentialStore.GetWcmTarget(provider));
        CloudApiKeyText.Password = wcmKey ?? settings.ApiKey;
    }

    private void SaveCloudProviderFields(CloudProviderType provider)
    {
        CloudProviderSettings settings = GetCloudSettings(provider);
        settings.Model = GetComboText(CloudModelCombo);
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

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string originalSaveLabel = SaveButton.Content?.ToString() ?? "Save Settings";
        SaveButton.IsEnabled = false;
        SaveButton.Content = "Saving...";
        SetSaveStatus("Saving settings and validating the active providers...", Brushes.Goldenrod);

        _logger.LogDebug("SettingsWindow.Save_Click started — provider={Provider} preferLocal={PreferLocal}",
            _currentSelectedCloudProvider, PreferLocalCheck.IsChecked);

        if (_llmEngine is null)
        {
            _logger.LogWarning("Save_Click: _llmEngine is null — closing without saving");
            SetSaveStatus("Settings could not be saved because the LLM engine is unavailable.", Brushes.OrangeRed);
            SaveButton.IsEnabled = true;
            SaveButton.Content = originalSaveLabel;
            return;
        }

        try
        {
            // Save current cloud provider fields
            SaveCloudProviderFields(_currentSelectedCloudProvider);

            // Save current local engine fields
            SaveLocalEngineFields(_currentSelectedLocalEngine);

            // Apply active selections
            _configCopy.ActiveLocalEngine = (LocalEngineType)LocalEngineCombo.SelectedIndex;
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
                "localEngine={LocalEngine}",
                _configCopy.ActiveCloudProvider,
                activeSettings.Model,
                activeSettings.BaseUrl,
                !string.IsNullOrEmpty(activeSettings.ApiKey),
                _configCopy.PreferLocal,
                _llmEngine.AgenticToolUseEnabled,
                _configCopy.EmulationBlendWeight,
                _configCopy.ReflectionDelayMs,
                _configCopy.SyncBlinksToUser,
                _configCopy.ActiveLocalEngine);

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

            await RefreshRuntimeStatusAsync(includeModelDiscovery: true);

            SaveButton.Content = "Saved";
            SetSaveStatus("Settings saved. See the connectivity summary in the confirmation dialog.", Brushes.LimeGreen);
            MessageBox.Show(
                BuildSaveSummary(),
                "HCEP Settings Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CloseCompat(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings save failed");
            SetSaveStatus($"Save failed: {ex.Message}", Brushes.OrangeRed);
            MessageBox.Show(
                $"Settings could not be saved.\n\n{ex.Message}",
                "HCEP Settings Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsLoaded)
            {
                SaveButton.IsEnabled = true;
                SaveButton.Content = originalSaveLabel;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseCompat(false);
    }

    private async void LocalEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        // Save fields of the previous local engine before switching
        SaveLocalEngineFields(_currentSelectedLocalEngine);

        // Switch to the newly selected local engine
        var newEngine = (LocalEngineType)LocalEngineCombo.SelectedIndex;
        _currentSelectedLocalEngine = newEngine;
        LoadLocalEngineFields(newEngine);
        UpdateRoutingSummary();
        await RefreshLocalEngineStatusAsync(includeModelDiscovery: true);
    }

    private void LoadLocalEngineFields(LocalEngineType engine)
    {
        LlamaCppCompatCheck.Visibility = (engine == LocalEngineType.LlamaCpp) ? Visibility.Visible : Visibility.Collapsed;

        if (engine == LocalEngineType.Ollama)
        {
            LocalUrlText.Text = _configCopy.Ollama.BaseUrl;
            SetComboText(LocalModelCombo, _configCopy.Ollama.Model);
            LocalTempSlider.Value = _configCopy.Ollama.Temperature;
        }
        else if (engine == LocalEngineType.LlamaCpp)
        {
            LocalUrlText.Text = _configCopy.LlamaCpp.BaseUrl;
            SetComboText(LocalModelCombo, _configCopy.LlamaCpp.Model);
            LocalTempSlider.Value = _configCopy.LlamaCpp.Temperature;
            LlamaCppCompatCheck.IsChecked = _configCopy.LlamaCpp.UseOaiCompatibleEndpoint;
        }
        else
        {
            var settings = GetGenericLocalSettings(engine);
            LocalUrlText.Text = settings.BaseUrl;
            SetComboText(LocalModelCombo, settings.Model);
            LocalTempSlider.Value = settings.Temperature;
        }
    }

    private void SaveLocalEngineFields(LocalEngineType engine)
    {
        if (engine == LocalEngineType.Ollama)
        {
            _configCopy.Ollama.BaseUrl = LocalUrlText.Text.Trim();
            _configCopy.Ollama.Model = GetComboText(LocalModelCombo);
            _configCopy.Ollama.Temperature = (float)LocalTempSlider.Value;
        }
        else if (engine == LocalEngineType.LlamaCpp)
        {
            _configCopy.LlamaCpp.BaseUrl = LocalUrlText.Text.Trim();
            _configCopy.LlamaCpp.Model = GetComboText(LocalModelCombo);
            _configCopy.LlamaCpp.Temperature = (float)LocalTempSlider.Value;
            _configCopy.LlamaCpp.UseOaiCompatibleEndpoint = LlamaCppCompatCheck.IsChecked == true;
        }
        else
        {
            var settings = GetGenericLocalSettings(engine);
            settings.BaseUrl = LocalUrlText.Text.Trim();
            settings.Model = GetComboText(LocalModelCombo);
            settings.Temperature = (float)LocalTempSlider.Value;
        }
    }

    private async void RefreshLocalModels_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLocalEngineStatusAsync(includeModelDiscovery: true, forceModelRefresh: true);
    }

    private async void RefreshCloudModels_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudProviderStatusAsync(includeModelDiscovery: true, forceModelRefresh: true);
    }

    private GenericLocalSettings GetGenericLocalSettings(LocalEngineType engine)
    {
        return engine switch
        {
            LocalEngineType.LMStudio => _configCopy.LMStudio,
            LocalEngineType.Jan => _configCopy.Jan,
            LocalEngineType.GPT4All => _configCopy.GPT4All,
            LocalEngineType.LocalAI => _configCopy.LocalAI,
            LocalEngineType.vLLM => _configCopy.vLLM,
            LocalEngineType.Oobabooga => _configCopy.Oobabooga,
            LocalEngineType.KoboldCpp => _configCopy.KoboldCpp,
            LocalEngineType.BitNet => _configCopy.BitNet,
            _ => _configCopy.CustomLocal
        };
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingPreset) return;

        int index = PresetCombo.SelectedIndex;
        if (index == 4) return; // Custom - do nothing

        _isInitializingPreset = true;
        try
        {
            switch (index)
            {
                case 0: // Attentive Listener
                    EmuWeightSlider.Value = 0.70;
                    DelaySlider.Value = 200;
                    SyncBlinksCheck.IsChecked = true;
                    break;
                case 1: // Warm Companion
                    EmuWeightSlider.Value = 0.90;
                    DelaySlider.Value = 350;
                    SyncBlinksCheck.IsChecked = true;
                    break;
                case 2: // Silent Observer
                    EmuWeightSlider.Value = 0.15;
                    DelaySlider.Value = 700;
                    SyncBlinksCheck.IsChecked = false;
                    break;
                case 3: // Professional Assistant
                    EmuWeightSlider.Value = 0.40;
                    DelaySlider.Value = 150;
                    SyncBlinksCheck.IsChecked = true;
                    break;
            }
        }
        finally
        {
            _isInitializingPreset = false;
        }
    }

    private void Control_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetCustomPreset();
    }

    private void Control_CheckChanged(object sender, RoutedEventArgs e)
    {
        SetCustomPreset();
    }

    private void RoutingControl_Changed(object sender, RoutedEventArgs e)
    {
        UpdateRoutingSummary();
    }

    private void SetCustomPreset()
    {
        if (_isInitializing || _isInitializingPreset) return;
        PresetCombo.SelectedIndex = 4; // Select "Custom"
    }

    private void SetActivePresetFromValues()
    {
        _isInitializingPreset = true;
        try
        {
            float emu = _configCopy.EmulationBlendWeight;
            int delay = _configCopy.ReflectionDelayMs;
            bool blinks = _configCopy.SyncBlinksToUser;

            if (Math.Abs(emu - 0.70f) < 0.01f && delay == 200 && blinks)
            {
                PresetCombo.SelectedIndex = 0;
            }
            else if (Math.Abs(emu - 0.90f) < 0.01f && delay == 350 && blinks)
            {
                PresetCombo.SelectedIndex = 1;
            }
            else if (Math.Abs(emu - 0.15f) < 0.01f && delay == 700 && !blinks)
            {
                PresetCombo.SelectedIndex = 2;
            }
            else if (Math.Abs(emu - 0.40f) < 0.01f && delay == 150 && blinks)
            {
                PresetCombo.SelectedIndex = 3;
            }
            else
            {
                PresetCombo.SelectedIndex = 4; // Custom
            }
        }
        finally
        {
            _isInitializingPreset = false;
        }
    }

    private async Task RefreshRuntimeStatusAsync(bool includeModelDiscovery)
    {
        await RefreshLocalEngineStatusAsync(includeModelDiscovery);
        await RefreshCloudProviderStatusAsync(includeModelDiscovery);
        UpdateRoutingSummary();
    }

    private async Task RefreshLocalEngineStatusAsync(bool includeModelDiscovery, bool forceModelRefresh = false)
    {
        if (_llmEngine is null)
        {
            SetStatus(LocalConnectionStatusText, "Local engine diagnostics unavailable because the runtime engine is missing.", Brushes.OrangeRed);
            return;
        }

        try
        {
            bool available = await _llmEngine.IsLocalAvailableAsync();
            IReadOnlyList<string> models = Array.Empty<string>();

            if (includeModelDiscovery && (available || forceModelRefresh))
            {
                models = await _llmEngine.GetAvailableLocalModelsAsync();
                PopulateModelChoices(LocalModelCombo, models, GetComboText(LocalModelCombo));
            }

            if (available)
            {
                string message = models.Count switch
                {
                    > 0 => $"Connected. {models.Count} local model(s) discovered from {LocalUrlText.Text.Trim()}.",
                    _ => $"Connected to {LocalUrlText.Text.Trim()}, but the engine did not report any model names."
                };
                SetStatus(LocalConnectionStatusText, message, Brushes.LimeGreen);
            }
            else
            {
                SetStatus(LocalConnectionStatusText, $"Unavailable. HCEP could not reach the local engine at {LocalUrlText.Text.Trim()}.", Brushes.OrangeRed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh local engine diagnostics");
            SetStatus(LocalConnectionStatusText, $"Local model lookup failed: {ex.Message}", Brushes.OrangeRed);
        }
    }

    private async Task RefreshCloudProviderStatusAsync(bool includeModelDiscovery, bool forceModelRefresh = false)
    {
        if (_llmEngine is null)
        {
            SetStatus(CloudConnectionStatusText, "Cloud diagnostics unavailable because the runtime engine is missing.", Brushes.OrangeRed);
            return;
        }

        string apiKey = CloudApiKeyText.Password.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            SetStatus(CloudConnectionStatusText, "No API key is currently loaded for the selected provider.", Brushes.OrangeRed);
            return;
        }

        try
        {
            IReadOnlyList<string> models = Array.Empty<string>();
            bool supportsDiscovery = SupportsCloudModelDiscovery(_currentSelectedCloudProvider);

            if (includeModelDiscovery)
            {
                models = await _llmEngine.GetAvailableCloudModelsAsync(_currentSelectedCloudProvider);
                if (supportsDiscovery || forceModelRefresh || models.Count > 1)
                {
                    PopulateModelChoices(CloudModelCombo, models, GetComboText(CloudModelCombo));
                }
            }

            if (supportsDiscovery)
            {
                string message = models.Count switch
                {
                    > 0 => $"Connected. {models.Count} cloud model(s) discovered for {GetSelectionLabel(CloudProviderCombo)}.",
                    _ => $"Connected to {GetSelectionLabel(CloudProviderCombo)}, but no model names were returned."
                };
                SetStatus(CloudConnectionStatusText, message, Brushes.LimeGreen);
            }
            else
            {
                SetStatus(
                    CloudConnectionStatusText,
                    $"API key loaded for {GetSelectionLabel(CloudProviderCombo)}. This provider does not expose a standard model-list endpoint, so HCEP uses the configured model name.",
                    Brushes.Goldenrod);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh cloud provider diagnostics for {Provider}", _currentSelectedCloudProvider);
            SetStatus(CloudConnectionStatusText, $"Cloud model lookup failed: {ex.Message}", Brushes.OrangeRed);
        }
    }

    private void UpdateRoutingSummary()
    {
        string localModel = GetComboText(LocalModelCombo);
        string cloudModel = GetComboText(CloudModelCombo);
        string summary = PreferLocalCheck.IsChecked == true
            ? $"Chat and the system prompt share one route: local '{localModel}' first, then cloud '{cloudModel}' only if local is unavailable and a cloud API key is present."
            : $"Chat and the system prompt share one route: cloud '{cloudModel}' first, with local '{localModel}' used only when forced or when cloud credentials are unavailable.";

        SetSaveStatus(summary, Brushes.LightSteelBlue);
    }

    private string BuildSaveSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Settings saved.");
        builder.AppendLine();
        builder.AppendLine($"Local engine: {GetSelectionLabel(LocalEngineCombo)}");
        builder.AppendLine($"Local model: {GetComboText(LocalModelCombo)}");
        builder.AppendLine($"Local status: {LocalConnectionStatusText.Text}");
        builder.AppendLine();
        builder.AppendLine($"Cloud provider: {GetSelectionLabel(CloudProviderCombo)}");
        builder.AppendLine($"Cloud model: {GetComboText(CloudModelCombo)}");
        builder.AppendLine($"Cloud status: {CloudConnectionStatusText.Text}");
        builder.AppendLine();
        builder.AppendLine(SaveStatusText.Text);
        builder.AppendLine();
        builder.AppendLine("There is no separate chat-model or system-model selector in the current architecture; both use the same active routing shown above.");
        return builder.ToString();
    }

    private static void PopulateModelChoices(ComboBox comboBox, IEnumerable<string> models, string selectedModel)
    {
        var distinctModels = models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        comboBox.Items.Clear();
        foreach (string model in distinctModels)
        {
            comboBox.Items.Add(model);
        }

        SetComboText(comboBox, selectedModel);
    }

    private static void SetComboText(ComboBox comboBox, string? value)
    {
        comboBox.Text = value ?? string.Empty;
    }

    private static string GetComboText(ComboBox comboBox) => comboBox.Text.Trim();

    private static string GetSelectionLabel(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
        ?? comboBox.Text.Trim();

    private static bool SupportsCloudModelDiscovery(CloudProviderType provider) =>
        provider is not CloudProviderType.Anthropic
        and not CloudProviderType.AzureOpenAI
        and not CloudProviderType.AmazonBedrock;

    private static void SetStatus(TextBlock target, string message, Brush brush)
    {
        target.Text = message;
        target.Foreground = brush;
    }

    private void SetSaveStatus(string message, Brush brush)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = brush;
    }

    private void CloseCompat(bool dialogResult)
    {
        try
        {
            DialogResult = dialogResult;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
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
        CloneSettings(src.LMStudio, copy.LMStudio);
        CloneSettings(src.Jan, copy.Jan);
        CloneSettings(src.GPT4All, copy.GPT4All);
        CloneSettings(src.LocalAI, copy.LocalAI);
        CloneSettings(src.vLLM, copy.vLLM);
        CloneSettings(src.Oobabooga, copy.Oobabooga);
        CloneSettings(src.KoboldCpp, copy.KoboldCpp);
        CloneSettings(src.BitNet, copy.BitNet);
        CloneSettings(src.CustomLocal, copy.CustomLocal);
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
        CloneSettings(src.LMStudio, dest.LMStudio);
        CloneSettings(src.Jan, dest.Jan);
        CloneSettings(src.GPT4All, dest.GPT4All);
        CloneSettings(src.LocalAI, dest.LocalAI);
        CloneSettings(src.vLLM, dest.vLLM);
        CloneSettings(src.Oobabooga, dest.Oobabooga);
        CloneSettings(src.KoboldCpp, dest.KoboldCpp);
        CloneSettings(src.BitNet, dest.BitNet);
        CloneSettings(src.CustomLocal, dest.CustomLocal);
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

    private static void CloneSettings(GenericLocalSettings src, GenericLocalSettings dest)
    {
        dest.Enabled = src.Enabled;
        dest.BaseUrl = src.BaseUrl;
        dest.Model = src.Model;
        dest.Temperature = src.Temperature;
    }
}
