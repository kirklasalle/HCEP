// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.Security.Cryptography;
using System.Text;

namespace HCEP.Core.Models;

/// <summary>
/// Supported local inference engine types.
/// </summary>
public enum LocalEngineType
{
    Ollama,
    LlamaCpp
}

/// <summary>
/// Supported frontier cloud LLM providers.
/// </summary>
public enum CloudProviderType
{
    OpenAI,
    Anthropic,
    Gemini,
    Mistral,
    xAI,
    Cohere
}

/// <summary>
/// Unified configuration model for all HCEP local and cloud LLM/SLM engines.
/// </summary>
public sealed class LlmConfiguration
{
    // Global Routing Policy
    public bool PreferLocal { get; set; } = true;
    public LocalEngineType ActiveLocalEngine { get; set; } = LocalEngineType.Ollama;
    public CloudProviderType ActiveCloudProvider { get; set; } = CloudProviderType.OpenAI;

    // Somatic Emulation & Mirroring (Fast AI Reflection)
    public float EmulationBlendWeight { get; set; } = 0.5f;
    public int ReflectionDelayMs { get; set; } = 300;
    public bool SyncBlinksToUser { get; set; } = true;

    // ── Local Engines ──────────────────────────────────────────
    public OllamaSettings Ollama { get; set; } = new();
    public LlamaCppSettings LlamaCpp { get; set; } = new();

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
