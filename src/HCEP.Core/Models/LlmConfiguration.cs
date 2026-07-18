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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace HCEP.Core.Models;

/// <summary>
/// Supported local inference engine types.
/// </summary>
public enum LocalEngineType
{
    Ollama = 0,
    LlamaCpp = 1,
    LMStudio = 2,
    Jan = 3,
    GPT4All = 4,
    LocalAI = 5,
    vLLM = 6,
    Oobabooga = 7,
    KoboldCpp = 8,
    BitNet = 9,
    Custom = 10
}

/// <summary>
/// Supported frontier cloud LLM providers.
/// Ordinal values MUST match the ComboBox SelectedIndex order in SettingsWindow.xaml.
/// </summary>
public enum CloudProviderType
{
    OpenAI = 0,
    Anthropic = 1,
    Gemini = 2,
    Mistral = 3,
    xAI = 4,
    Cohere = 5,
    OpenRouter = 6,   // Aggregator — routes to 100+ models via single API
    DeepSeek = 7,   // High-performance Chinese lab (OpenAI-compat)
    Groq = 8,   // Ultra-fast LPU inference
    TogetherAI = 9,   // Open-source model hosting
    FireworksAI = 10,  // Low-latency inference platform
    Perplexity = 11,  // Search-augmented LLM
    AI21Labs = 12,  // Jamba hybrid SSM
    Replicate = 13,  // Model-hosting platform
    HuggingFace = 14,  // Inference Endpoints
    AzureOpenAI = 15,  // Microsoft Azure OpenAI Service
    AmazonBedrock = 16, // AWS Bedrock gateway
    NvidiaNIM = 17,  // NVIDIA Inference Microservices
    Cerebras = 18,  // Wafer-scale ultra-fast inference
    MoonshotAI = 19,  // Kimi long-context Chinese lab
}

/// <summary>
/// Unified configuration model for all HCEP local and cloud LLM/SLM engines.
/// </summary>
public sealed class LlmConfiguration
{
    // Configuration schema version. Used by SettingsPersistence to migrate
    // older settings payloads forward as the model evolves.
    public int SchemaVersion { get; set; } = 2;

    // Global Routing Policy
    public bool PreferLocal { get; set; } = true;
    public LocalEngineType ActiveLocalEngine { get; set; } = LocalEngineType.Ollama;
    public CloudProviderType ActiveCloudProvider { get; set; } = CloudProviderType.OpenAI;

    // Context Settings (Phase 14)
    // Persisted here so the Settings window can round-trip the Context tab
    // through the same JSON settings file used by the rest of the runtime
    // configuration, then rehydrate TimeContextProvider at startup.
    public EnvironmentType ContextEnvironment { get; set; } = EnvironmentType.Unknown;
    public SituationActivity ContextActivity { get; set; } = SituationActivity.Unknown;
    public SituationPrivacy ContextPrivacy { get; set; } = SituationPrivacy.Private;
    public string? ContextUserDefinedLocation { get; set; }

    // Chat Telemetry Harness
    public int ChatTelemetryWindowSeconds { get; set; } = 2;
    public int ChatTelemetryDensityLevel { get; set; } = 2;
    public bool ChatTelemetryDebugExpanded { get; set; }
    public bool ChatSystemPromptDebugExpanded { get; set; }

    // Somatic Emulation & Mirroring (Fast AI Reflection)
    public float EmulationBlendWeight { get; set; } = 0.5f;
    public int ReflectionDelayMs { get; set; } = 300;
    public bool SyncBlinksToUser { get; set; } = true;

    // ── Local Engines ──────────────────────────────────────────
    public OllamaSettings Ollama { get; set; } = new();
    public LlamaCppSettings LlamaCpp { get; set; } = new();
    public GenericLocalSettings LMStudio { get; set; } = new() { BaseUrl = "http://localhost:1234", Model = "local-model" };
    public GenericLocalSettings Jan { get; set; } = new() { BaseUrl = "http://localhost:1337", Model = "local-model" };
    public GenericLocalSettings GPT4All { get; set; } = new() { BaseUrl = "http://localhost:4891", Model = "local-model" };
    public GenericLocalSettings LocalAI { get; set; } = new() { BaseUrl = "http://localhost:8080", Model = "local-model" };
    public GenericLocalSettings vLLM { get; set; } = new() { BaseUrl = "http://localhost:8000", Model = "local-model" };
    public GenericLocalSettings Oobabooga { get; set; } = new() { BaseUrl = "http://localhost:5000", Model = "local-model" };
    public GenericLocalSettings KoboldCpp { get; set; } = new() { BaseUrl = "http://localhost:5001", Model = "local-model" };
    public GenericLocalSettings BitNet { get; set; } = new() { BaseUrl = "http://localhost:8080", Model = "local-model" };
    public GenericLocalSettings CustomLocal { get; set; } = new() { BaseUrl = "http://localhost:8000", Model = "local-model" };

    // ── Frontier Cloud Providers ────────────────────────────────
    public CloudProviderSettings OpenAI { get; set; } = new()
    {
        BaseUrl = "https://api.openai.com/v1",
        Model = "gpt-5-mini"
    };

    public CloudProviderSettings Anthropic { get; set; } = new()
    {
        BaseUrl = "https://api.anthropic.com/v1",
        Model = "claude-3-5-sonnet-20241022"
    };

    public CloudProviderSettings Gemini { get; set; } = new()
    {
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
        Model = "gemini-1.5-flash"
    };

    public CloudProviderSettings Mistral { get; set; } = new()
    {
        BaseUrl = "https://api.mistral.ai/v1",
        Model = "mistral-large-latest"
    };

    public CloudProviderSettings xAI { get; set; } = new()
    {
        BaseUrl = "https://api.x.ai/v1",
        Model = "grok-beta"
    };

    public CloudProviderSettings Cohere { get; set; } = new()
    {
        BaseUrl = "https://api.cohere.ai/v1",
        Model = "command-r-plus"
    };

    // ── Extended Frontier Providers (Phase 8+) ───────────────────────
    public CloudProviderSettings OpenRouter { get; set; } = new()
    {
        BaseUrl = "https://openrouter.ai/api/v1",
        Model = "meta-llama/llama-3.3-70b-instruct"
    };

    public CloudProviderSettings DeepSeek { get; set; } = new()
    {
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-chat"
    };

    public CloudProviderSettings Groq { get; set; } = new()
    {
        BaseUrl = "https://api.groq.com/openai/v1",
        Model = "llama-3.3-70b-versatile"
    };

    public CloudProviderSettings TogetherAI { get; set; } = new()
    {
        BaseUrl = "https://api.together.xyz/v1",
        Model = "meta-llama/Llama-3-70b-chat-hf"
    };

    public CloudProviderSettings FireworksAI { get; set; } = new()
    {
        BaseUrl = "https://api.fireworks.ai/inference/v1",
        Model = "accounts/fireworks/models/llama-v3p3-70b-instruct"
    };

    public CloudProviderSettings Perplexity { get; set; } = new()
    {
        BaseUrl = "https://api.perplexity.ai",
        Model = "llama-3.1-sonar-large-128k-online"
    };

    public CloudProviderSettings AI21Labs { get; set; } = new()
    {
        BaseUrl = "https://api.ai21.com/studio/v1",
        Model = "jamba-1.5-large"
    };

    public CloudProviderSettings Replicate { get; set; } = new()
    {
        BaseUrl = "https://api.replicate.com/v1",
        Model = "meta/llama-3-70b-instruct"
    };

    public CloudProviderSettings HuggingFace { get; set; } = new()
    {
        BaseUrl = "https://api-inference.huggingface.co/v1",
        Model = "meta-llama/Llama-3.3-70B-Instruct"
    };

    public CloudProviderSettings AzureOpenAI { get; set; } = new()
    {
        BaseUrl = "https://YOUR_RESOURCE.openai.azure.com/openai/deployments/YOUR_DEPLOYMENT",
        Model = "gpt-4o"
    };

    public CloudProviderSettings AmazonBedrock { get; set; } = new()
    {
        BaseUrl = "https://bedrock-runtime.us-east-1.amazonaws.com",
        Model = "anthropic.claude-3-5-sonnet-20241022-v2:0"
    };

    public CloudProviderSettings NvidiaNIM { get; set; } = new()
    {
        BaseUrl = "https://integrate.api.nvidia.com/v1",
        Model = "meta/llama-3.3-70b-instruct"
    };

    public CloudProviderSettings Cerebras { get; set; } = new()
    {
        BaseUrl = "https://api.cerebras.ai/v1",
        Model = "llama3.3-70b"
    };

    public CloudProviderSettings MoonshotAI { get; set; } = new()
    {
        BaseUrl = "https://api.moonshot.cn/v1",
        Model = "moonshot-v1-32k"
    };
}

/// <summary>
/// Specific settings for Ollama local instances.
/// </summary>
public sealed class OllamaSettings
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3:8b";
    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// Specific settings for Llama.cpp local server instances.
/// </summary>
public sealed class LlamaCppSettings
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string Model { get; set; } = "local-model";
    public float Temperature { get; set; } = 0.7f;
    public bool UseOaiCompatibleEndpoint { get; set; } = true; // Use /v1/chat/completions vs native /completion
}

/// <summary>
/// Settings for a frontier cloud LLM provider.
/// </summary>
public sealed class CloudProviderSettings
{
    private byte[]? _encryptedApiKey;

    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// API key for this provider.  In-memory only — protected with DPAPI for the
    /// current session.  NEVER serialized to disk; keys are persisted via Windows
    /// Credential Manager (see <c>WindowsCredentialStore</c>).
    /// </summary>
    [JsonIgnore]
    public string ApiKey
    {
        get
        {
            if (_encryptedApiKey == null || _encryptedApiKey.Length == 0)
                return string.Empty;
            try
            {
                byte[] decrypted = ProtectedData.Unprotect(_encryptedApiKey, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _encryptedApiKey = null;
            }
            else
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(value);
                _encryptedApiKey = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            }
        }
    }

    public float Temperature { get; set; } = 0.7f;
}

/// <summary>
/// Settings for generic local engines (LM Studio, Jan, GPT4All, LocalAI, vLLM, Oobabooga, KoboldCpp, BitNet, Custom).
/// </summary>
public sealed class GenericLocalSettings
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float Temperature { get; set; } = 0.7f;
}
