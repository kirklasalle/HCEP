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
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Intelligence;

/// <summary>
/// Agentic hybrid LLM engine routing between local inference engines (Ollama, Llama.cpp)
/// and frontier cloud providers (OpenAI, Anthropic, Gemini, Mistral, xAI, Cohere).
/// Injects HCEP context into prompts for mode-aware, agentic responses.
/// </summary>
public sealed class HybridLlmEngine : ILlmEngine
{
    private readonly HttpClient _httpClient;
    private readonly IKnowledgeStore _knowledge;
    private readonly AgenticToolExecutor _toolExecutor;
    private readonly ILogger<HybridLlmEngine> _logger;

    /// <summary>Maximum agentic reasoning steps before forcing a final answer.</summary>
    public int MaxAgenticSteps { get; set; } = 5;

    /// <summary>Whether to enable agentic tool-use on cloud requests.</summary>
    public bool AgenticToolUseEnabled { get; set; } = true;

    /// <summary>
    /// Current contextual snapshot (time/space/situation).
    /// Set externally; injected into every LLM system prompt.
    /// Update via <see cref="TimeContextProvider.BuildSnapshot()"/> approximately once per second.
    /// </summary>
    public ContextSnapshot? CurrentContext { get; set; }

    /// <summary>Unified configuration for local and cloud LLM engines.</summary>
    public LlmConfiguration Configuration { get; set; } = new();

    // ── Circuit Breaker (cloud provider) ────────────────────────
    /// <summary>Number of consecutive cloud failures before the breaker opens.</summary>
    public int CircuitBreakerThreshold { get; set; } = 3;
    /// <summary>How long the breaker stays open before allowing a retry.</summary>
    public TimeSpan CircuitBreakerCoolDown { get; set; } = TimeSpan.FromSeconds(30);

    private int _cloudConsecutiveFailures;
    private DateTimeOffset _cloudBreakerOpenedAt = DateTimeOffset.MinValue;

    private bool IsCloudCircuitOpen =>
        _cloudConsecutiveFailures >= CircuitBreakerThreshold &&
        DateTimeOffset.UtcNow - _cloudBreakerOpenedAt < CircuitBreakerCoolDown;

    // ── Backward-Compatibility Mappings ─────────────────────────
    public string OllamaBaseUrl
    {
        get => Configuration.Ollama.BaseUrl;
        set => Configuration.Ollama.BaseUrl = value;
    }

    public string OllamaModel
    {
        get => Configuration.Ollama.Model;
        set => Configuration.Ollama.Model = value;
    }

    public string OpenAiBaseUrl
    {
        get => Configuration.OpenAI.BaseUrl;
        set => Configuration.OpenAI.BaseUrl = value;
    }

    public string OpenAiModel
    {
        get => Configuration.OpenAI.Model;
        set => Configuration.OpenAI.Model = value;
    }

    public string OpenAiApiKey
    {
        get => Configuration.OpenAI.ApiKey;
        set => Configuration.OpenAI.ApiKey = value;
    }

    public int LatencyThresholdMs { get; set; } = 3000;

    public HybridLlmEngine(
        HttpClient httpClient,
        IKnowledgeStore knowledge,
        AgenticToolExecutor toolExecutor,
        ILogger<HybridLlmEngine> logger)
    {
        _httpClient = httpClient;
        _knowledge = knowledge;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    // ── ILlmEngine ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LlmExchange> PromptAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(hcepContext);
        var start = DateTimeOffset.UtcNow;

        bool useLocal = forceLocal || Configuration.PreferLocal || string.IsNullOrEmpty(GetActiveCloudApiKey());

        if (useLocal)
        {
            if (await IsLocalAvailableAsync(ct))
            {
                try
                {
                    var localConfig = GetLocalEngineConfig();
                    string response = Configuration.ActiveLocalEngine switch
                    {
                        LocalEngineType.Ollama => await CallOllamaAsync(systemPrompt, userMessage, ct),
                        LocalEngineType.LlamaCpp => await CallLlamaCppAsync(systemPrompt, userMessage, ct),
                        _ => await CallOpenAiCompatibleApiAsync(localConfig.BaseUrl, string.Empty, localConfig.Model, systemPrompt, userMessage, ct)
                    };

                    return new LlmExchange
                    {
                        SystemPrompt = systemPrompt,
                        UserMessage = userMessage,
                        HcepContext = hcepContext?.ToString(),
                        Response = response,
                        ModelId = GetActiveLocalModel(),
                        IsLocal = true,
                        Latency = DateTimeOffset.UtcNow - start,
                        Timestamp = start,
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Local inference engine failed, falling back to cloud");
                }
            }
        }

        // Cloud provider execution
        string activeApiKey = GetActiveCloudApiKey();
        if (!string.IsNullOrEmpty(activeApiKey))
        {
            if (IsCloudCircuitOpen)
            {
                var remaining = CircuitBreakerCoolDown - (DateTimeOffset.UtcNow - _cloudBreakerOpenedAt);
                _logger.LogWarning(
                    "Cloud circuit breaker is OPEN after {Failures} consecutive failures — skipping cloud call, retry in {Remaining:F0}s",
                    _cloudConsecutiveFailures, remaining.TotalSeconds);
            }
            else
            {
                var activeSettings = GetActiveCloudProviderSettings();
                _logger.LogDebug(
                    "Cloud call → provider={Provider} model={Model} url={BaseUrl} hasKey={HasKey} promptLen={PromptLen}",
                    Configuration.ActiveCloudProvider,
                    activeSettings.Model,
                    activeSettings.BaseUrl,
                    !string.IsNullOrEmpty(activeApiKey),
                    userMessage.Length);

                try
                {
                    string response = Configuration.ActiveCloudProvider switch
                    {
                        CloudProviderType.Anthropic => await CallAnthropicAsync(systemPrompt, userMessage, ct),
                        CloudProviderType.Gemini => await CallGeminiAsync(systemPrompt, userMessage, ct),
                        // All other providers use the OpenAI-compatible /v1/chat/completions endpoint
                        CloudProviderType.Mistral => await CallOpenAiCompatibleApiAsync(Configuration.Mistral.BaseUrl, Configuration.Mistral.ApiKey, Configuration.Mistral.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.xAI => await CallOpenAiCompatibleApiAsync(Configuration.xAI.BaseUrl, Configuration.xAI.ApiKey, Configuration.xAI.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.Cohere => await CallOpenAiCompatibleApiAsync(Configuration.Cohere.BaseUrl, Configuration.Cohere.ApiKey, Configuration.Cohere.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.OpenRouter => await CallOpenAiCompatibleApiAsync(Configuration.OpenRouter.BaseUrl, Configuration.OpenRouter.ApiKey, Configuration.OpenRouter.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.DeepSeek => await CallOpenAiCompatibleApiAsync(Configuration.DeepSeek.BaseUrl, Configuration.DeepSeek.ApiKey, Configuration.DeepSeek.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.Groq => await CallOpenAiCompatibleApiAsync(Configuration.Groq.BaseUrl, Configuration.Groq.ApiKey, Configuration.Groq.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.TogetherAI => await CallOpenAiCompatibleApiAsync(Configuration.TogetherAI.BaseUrl, Configuration.TogetherAI.ApiKey, Configuration.TogetherAI.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.FireworksAI => await CallOpenAiCompatibleApiAsync(Configuration.FireworksAI.BaseUrl, Configuration.FireworksAI.ApiKey, Configuration.FireworksAI.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.Perplexity => await CallOpenAiCompatibleApiAsync(Configuration.Perplexity.BaseUrl, Configuration.Perplexity.ApiKey, Configuration.Perplexity.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.AI21Labs => await CallOpenAiCompatibleApiAsync(Configuration.AI21Labs.BaseUrl, Configuration.AI21Labs.ApiKey, Configuration.AI21Labs.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.Replicate => await CallOpenAiCompatibleApiAsync(Configuration.Replicate.BaseUrl, Configuration.Replicate.ApiKey, Configuration.Replicate.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.HuggingFace => await CallOpenAiCompatibleApiAsync(Configuration.HuggingFace.BaseUrl, Configuration.HuggingFace.ApiKey, Configuration.HuggingFace.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.AzureOpenAI => await CallOpenAiCompatibleApiAsync(Configuration.AzureOpenAI.BaseUrl, Configuration.AzureOpenAI.ApiKey, Configuration.AzureOpenAI.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.AmazonBedrock => await CallOpenAiCompatibleApiAsync(Configuration.AmazonBedrock.BaseUrl, Configuration.AmazonBedrock.ApiKey, Configuration.AmazonBedrock.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.NvidiaNIM => await CallOpenAiCompatibleApiAsync(Configuration.NvidiaNIM.BaseUrl, Configuration.NvidiaNIM.ApiKey, Configuration.NvidiaNIM.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.Cerebras => await CallOpenAiCompatibleApiAsync(Configuration.Cerebras.BaseUrl, Configuration.Cerebras.ApiKey, Configuration.Cerebras.Model, systemPrompt, userMessage, ct),
                        CloudProviderType.MoonshotAI => await CallOpenAiCompatibleApiAsync(Configuration.MoonshotAI.BaseUrl, Configuration.MoonshotAI.ApiKey, Configuration.MoonshotAI.Model, systemPrompt, userMessage, ct),
                        _ => await CallOpenAiCompatibleApiAsync(Configuration.OpenAI.BaseUrl, Configuration.OpenAI.ApiKey, Configuration.OpenAI.Model, systemPrompt, userMessage, ct)
                    };

                    // Success — reset circuit breaker
                    _cloudConsecutiveFailures = 0;
                    var latency = DateTimeOffset.UtcNow - start;
                    _logger.LogDebug(
                        "Cloud call succeeded — provider={Provider} model={Model} responseLen={ResponseLen} latency={Latency}ms",
                        Configuration.ActiveCloudProvider, activeSettings.Model,
                        response.Length, (int)latency.TotalMilliseconds);

                    return new LlmExchange
                    {
                        SystemPrompt = systemPrompt,
                        UserMessage = userMessage,
                        HcepContext = hcepContext?.ToString(),
                        Response = response,
                        ModelId = GetActiveCloudModel(),
                        IsLocal = false,
                        Latency = latency,
                        Timestamp = start,
                    };
                }
                catch (Exception ex)
                {
                    _cloudConsecutiveFailures++;
                    if (_cloudConsecutiveFailures >= CircuitBreakerThreshold)
                    {
                        _cloudBreakerOpenedAt = DateTimeOffset.UtcNow;
                        _logger.LogError(ex,
                            "Cloud provider call failed ({Failures}/{Threshold}) — circuit breaker OPENED, will retry after {CoolDown}s",
                            _cloudConsecutiveFailures, CircuitBreakerThreshold, CircuitBreakerCoolDown.TotalSeconds);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "Frontier cloud provider call failed ({Failures}/{Threshold})",
                            _cloudConsecutiveFailures, CircuitBreakerThreshold);
                    }
                }
            }
        }

        _logger.LogError(
            "All LLM providers exhausted (local available={Local}, cloud key present={Cloud}) — returning no-LLM fallback response",
            await IsLocalAvailableAsync(ct), !string.IsNullOrEmpty(GetActiveCloudApiKey()));

        return new LlmExchange
        {
            SystemPrompt = systemPrompt,
            UserMessage = userMessage,
            Response = "[No LLM available - check local server status or cloud API configurations]",
            Latency = DateTimeOffset.UtcNow - start,
            Timestamp = start,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        string userMessage,
        HcepReading? hcepContext = null,
        bool forceLocal = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(hcepContext);

        if (forceLocal || Configuration.PreferLocal)
        {
            if (Configuration.ActiveLocalEngine == LocalEngineType.Ollama)
            {
                var request = new OllamaRequest
                {
                    Model = Configuration.Ollama.Model,
                    System = systemPrompt,
                    Prompt = userMessage,
                    Stream = true,
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"{Configuration.Ollama.BaseUrl}/api/generate")
                    {
                        Content = content,
                    },
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) continue;

                    var chunk = JsonSerializer.Deserialize<OllamaStreamResponse>(line);
                    if (chunk?.Response is not null)
                        yield return chunk.Response;

                    if (chunk?.Done == true)
                        break;
                }
                yield break;
            }
        }

        // Streaming fallback to non-stream prompt response for simplified compatibility cross-providers
        var result = await PromptAsync(userMessage, hcepContext, forceLocal, ct);
        yield return result.Response ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> IsLocalAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(2000);

            var localConfig = GetLocalEngineConfig();

            if (Configuration.ActiveLocalEngine == LocalEngineType.LlamaCpp)
            {
                // Verify llama.cpp health endpoint
                var response = await _httpClient.GetAsync($"{localConfig.BaseUrl}/health", cts.Token);
                return response.IsSuccessStatusCode;
            }
            else if (Configuration.ActiveLocalEngine == LocalEngineType.Ollama)
            {
                // Verify Ollama tags endpoint
                var response = await _httpClient.GetAsync($"{localConfig.BaseUrl}/api/tags", cts.Token);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // For generic OpenAI-compatible local engines, require an explicit
                // success response from /v1/models to avoid false positives.
                var response = await _httpClient.GetAsync($"{localConfig.BaseUrl}/v1/models", cts.Token);
                return response.IsSuccessStatusCode;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsCloudAvailableAsync(CancellationToken ct = default)
    {
        string apiKey = GetActiveCloudApiKey();
        if (string.IsNullOrEmpty(apiKey)) return false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(3000);

            // Fast diagnostic check depending on the provider
            if (Configuration.ActiveCloudProvider == CloudProviderType.OpenAI)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Configuration.OpenAI.BaseUrl}/models");
                request.Headers.Authorization = new("Bearer", apiKey);
                var response = await _httpClient.SendAsync(request, cts.Token);
                return response.IsSuccessStatusCode;
            }

            return true; // Assume available if API key is populated for other frontier providers
        }
        catch
        {
            return false;
        }
    }

    // ── Private Inference Helpers ────────────────────────────────

    private string BuildSystemPrompt(HcepReading? hcep)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are HCEP — Human Communication Eye Protocol assistant.");
        sb.AppendLine("You analyze human communication through eye contact patterns, facial expressions, and speech.");

        // Inject secure, cryptographically verified Permanent Active Directives (AI-Facing Safeguard)
        sb.AppendLine();
        sb.AppendLine("=== SYSTEM CORE DIRECTIVES (IMMUTABLE & AUDITED) ===");
        sb.AppendLine(ActiveDirectivesManager.LoadAndVerifyDirectives());
        sb.AppendLine("====================================================");

        if (hcep is not null && hcep.Mode != HcepMode.Unknown)
        {
            sb.AppendLine();
            sb.AppendLine("=== Current HCEP State ===");
            sb.AppendLine($"Mode: {hcep.Mode}");
            sb.AppendLine($"Gaze Region: {hcep.Region}");
            sb.AppendLine($"Cognitive State: {hcep.Cognitive}");
            sb.AppendLine($"Emotional Valence: {hcep.Valence}");
            sb.AppendLine($"Confidence: {hcep.Confidence:F2}");
            sb.AppendLine();

            sb.AppendLine(hcep.Mode switch
            {
                HcepMode.Logic => "The person is in analytical/logical mode. Respond with structured, precise information.",
                HcepMode.Affect => "The person is emotionally engaged. Be empathetic and emotionally aware in your response.",
                HcepMode.Spirit => "Deep rapport detected. Respond authentically and personally.",
                HcepMode.Heart => "Empathic resonance detected. Use warm, supportive language.",
                HcepMode.Think => "The person is internally processing. Keep your response concise to not interrupt their thought.",
                _ => "",
            });
        }

        // ── Contextual Intelligence Injection (Phase 14) ─────────────────
        // Time × Space × Situation modulates the AI register and silence protocol.
        if (CurrentContext is not null)
        {
            sb.AppendLine();
            sb.AppendLine("=== Contextual State ===");
            sb.AppendLine(CurrentContext.ToPromptString());
            if (CurrentContext.SilenceProtocolActive)
                sb.AppendLine("SILENCE PROTOCOL: ACTIVE — be present but do not initiate. Wait for the person to invite response.");
            else
                sb.AppendLine($"Communication register: {CurrentContext.Register}. Temporal urgency: {CurrentContext.TemporalUrgency:F1}.");
        }

        return sb.ToString();
    }

    private (string BaseUrl, string Model, float Temperature) GetLocalEngineConfig()
    {
        return Configuration.ActiveLocalEngine switch
        {
            LocalEngineType.Ollama => (Configuration.Ollama.BaseUrl, Configuration.Ollama.Model, Configuration.Ollama.Temperature),
            LocalEngineType.LlamaCpp => (Configuration.LlamaCpp.BaseUrl, Configuration.LlamaCpp.Model, Configuration.LlamaCpp.Temperature),
            LocalEngineType.LMStudio => (Configuration.LMStudio.BaseUrl, Configuration.LMStudio.Model, Configuration.LMStudio.Temperature),
            LocalEngineType.Jan => (Configuration.Jan.BaseUrl, Configuration.Jan.Model, Configuration.Jan.Temperature),
            LocalEngineType.GPT4All => (Configuration.GPT4All.BaseUrl, Configuration.GPT4All.Model, Configuration.GPT4All.Temperature),
            LocalEngineType.LocalAI => (Configuration.LocalAI.BaseUrl, Configuration.LocalAI.Model, Configuration.LocalAI.Temperature),
            LocalEngineType.vLLM => (Configuration.vLLM.BaseUrl, Configuration.vLLM.Model, Configuration.vLLM.Temperature),
            LocalEngineType.Oobabooga => (Configuration.Oobabooga.BaseUrl, Configuration.Oobabooga.Model, Configuration.Oobabooga.Temperature),
            LocalEngineType.KoboldCpp => (Configuration.KoboldCpp.BaseUrl, Configuration.KoboldCpp.Model, Configuration.KoboldCpp.Temperature),
            LocalEngineType.BitNet => (Configuration.BitNet.BaseUrl, Configuration.BitNet.Model, Configuration.BitNet.Temperature),
            LocalEngineType.Custom => (Configuration.CustomLocal.BaseUrl, Configuration.CustomLocal.Model, Configuration.CustomLocal.Temperature),
            _ => (Configuration.Ollama.BaseUrl, Configuration.Ollama.Model, Configuration.Ollama.Temperature)
        };
    }

    private string GetActiveLocalModel() => GetLocalEngineConfig().Model;

    /// <summary>Returns the active cloud provider's CloudProviderSettings for trace logging.</summary>
    private HCEP.Core.Models.CloudProviderSettings GetActiveCloudProviderSettings() =>
        Configuration.ActiveCloudProvider switch
        {
            CloudProviderType.Anthropic => Configuration.Anthropic,
            CloudProviderType.Gemini => Configuration.Gemini,
            CloudProviderType.Mistral => Configuration.Mistral,
            CloudProviderType.xAI => Configuration.xAI,
            CloudProviderType.Cohere => Configuration.Cohere,
            CloudProviderType.OpenRouter => Configuration.OpenRouter,
            CloudProviderType.DeepSeek => Configuration.DeepSeek,
            CloudProviderType.Groq => Configuration.Groq,
            CloudProviderType.TogetherAI => Configuration.TogetherAI,
            CloudProviderType.FireworksAI => Configuration.FireworksAI,
            CloudProviderType.Perplexity => Configuration.Perplexity,
            CloudProviderType.AI21Labs => Configuration.AI21Labs,
            CloudProviderType.Replicate => Configuration.Replicate,
            CloudProviderType.HuggingFace => Configuration.HuggingFace,
            CloudProviderType.AzureOpenAI => Configuration.AzureOpenAI,
            CloudProviderType.AmazonBedrock => Configuration.AmazonBedrock,
            CloudProviderType.NvidiaNIM => Configuration.NvidiaNIM,
            CloudProviderType.Cerebras => Configuration.Cerebras,
            CloudProviderType.MoonshotAI => Configuration.MoonshotAI,
            _ => Configuration.OpenAI,
        };

    private string GetActiveCloudModel() => Configuration.ActiveCloudProvider switch
    {
        CloudProviderType.Anthropic => Configuration.Anthropic.Model,
        CloudProviderType.Gemini => Configuration.Gemini.Model,
        CloudProviderType.Mistral => Configuration.Mistral.Model,
        CloudProviderType.xAI => Configuration.xAI.Model,
        CloudProviderType.Cohere => Configuration.Cohere.Model,
        CloudProviderType.OpenRouter => Configuration.OpenRouter.Model,
        CloudProviderType.DeepSeek => Configuration.DeepSeek.Model,
        CloudProviderType.Groq => Configuration.Groq.Model,
        CloudProviderType.TogetherAI => Configuration.TogetherAI.Model,
        CloudProviderType.FireworksAI => Configuration.FireworksAI.Model,
        CloudProviderType.Perplexity => Configuration.Perplexity.Model,
        CloudProviderType.AI21Labs => Configuration.AI21Labs.Model,
        CloudProviderType.Replicate => Configuration.Replicate.Model,
        CloudProviderType.HuggingFace => Configuration.HuggingFace.Model,
        CloudProviderType.AzureOpenAI => Configuration.AzureOpenAI.Model,
        CloudProviderType.AmazonBedrock => Configuration.AmazonBedrock.Model,
        CloudProviderType.NvidiaNIM => Configuration.NvidiaNIM.Model,
        CloudProviderType.Cerebras => Configuration.Cerebras.Model,
        CloudProviderType.MoonshotAI => Configuration.MoonshotAI.Model,
        _ => Configuration.OpenAI.Model
    };

    private string GetActiveCloudApiKey()
    {
        // Priority: Windows Credential Manager → LlmConfiguration property → empty string.
        // WCM keys are stored under "HCEP/<ProviderName>" and are never visible in
        // process listings or environment dumps.
        return Configuration.ActiveCloudProvider switch
        {
            CloudProviderType.Anthropic =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Anthropic, "ANTHROPIC_API_KEY", _logger)
                ?? Configuration.Anthropic.ApiKey,
            CloudProviderType.Gemini =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Gemini, "GEMINI_API_KEY", _logger)
                ?? Configuration.Gemini.ApiKey,
            CloudProviderType.Mistral =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Mistral, "MISTRAL_API_KEY", _logger)
                ?? Configuration.Mistral.ApiKey,
            CloudProviderType.xAI =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.xAI, "XAI_API_KEY", _logger)
                ?? Configuration.xAI.ApiKey,
            CloudProviderType.Cohere =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Cohere, "COHERE_API_KEY", _logger)
                ?? Configuration.Cohere.ApiKey,
            CloudProviderType.OpenRouter =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.OpenRouter, "OPENROUTER_API_KEY", _logger)
                ?? Configuration.OpenRouter.ApiKey,
            CloudProviderType.DeepSeek =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.DeepSeek, "DEEPSEEK_API_KEY", _logger)
                ?? Configuration.DeepSeek.ApiKey,
            CloudProviderType.Groq =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Groq, "GROQ_API_KEY", _logger)
                ?? Configuration.Groq.ApiKey,
            CloudProviderType.TogetherAI =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.TogetherAI, "TOGETHERAI_API_KEY", _logger)
                ?? Configuration.TogetherAI.ApiKey,
            CloudProviderType.FireworksAI =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.FireworksAI, "FIREWORKS_API_KEY", _logger)
                ?? Configuration.FireworksAI.ApiKey,
            CloudProviderType.Perplexity =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Perplexity, "PERPLEXITY_API_KEY", _logger)
                ?? Configuration.Perplexity.ApiKey,
            CloudProviderType.AI21Labs =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.AI21Labs, "AI21_API_KEY", _logger)
                ?? Configuration.AI21Labs.ApiKey,
            CloudProviderType.Replicate =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Replicate, "REPLICATE_API_TOKEN", _logger)
                ?? Configuration.Replicate.ApiKey,
            CloudProviderType.HuggingFace =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.HuggingFace, "HF_API_TOKEN", _logger)
                ?? Configuration.HuggingFace.ApiKey,
            CloudProviderType.AzureOpenAI =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.AzureOpenAI, "AZURE_OPENAI_API_KEY", _logger)
                ?? Configuration.AzureOpenAI.ApiKey,
            CloudProviderType.AmazonBedrock =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.AmazonBedrock, "AWS_BEARER_TOKEN", _logger)
                ?? Configuration.AmazonBedrock.ApiKey,
            CloudProviderType.NvidiaNIM =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.NvidiaNIM, "NVIDIA_API_KEY", _logger)
                ?? Configuration.NvidiaNIM.ApiKey,
            CloudProviderType.Cerebras =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.Cerebras, "CEREBRAS_API_KEY", _logger)
                ?? Configuration.Cerebras.ApiKey,
            CloudProviderType.MoonshotAI =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.MoonshotAI, "MOONSHOT_API_KEY", _logger)
                ?? Configuration.MoonshotAI.ApiKey,
            _ =>
                WindowsCredentialStore.LoadWithFallback(WindowsCredentialStore.OpenAI, "OPENAI_API_KEY", _logger)
                ?? Configuration.OpenAI.ApiKey,
        };
    }

    // ── Local Engine Client Methods ──────────────────────────────

    private async Task<string> CallOllamaAsync(string system, string prompt, CancellationToken ct)
    {
        var request = new OllamaRequest
        {
            Model = Configuration.Ollama.Model,
            System = system,
            Prompt = prompt,
            Stream = false,
        };

        var response = await _httpClient.PostAsJsonAsync($"{Configuration.Ollama.BaseUrl}/api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result?.Response ?? string.Empty;
    }

    private async Task<string> CallLlamaCppAsync(string system, string prompt, CancellationToken ct)
    {
        if (Configuration.LlamaCpp.UseOaiCompatibleEndpoint)
        {
            return await CallOpenAiCompatibleApiAsync(Configuration.LlamaCpp.BaseUrl, string.Empty, Configuration.LlamaCpp.Model, system, prompt, ct);
        }

        // Native llama.cpp /completion endpoint
        var formattedPrompt = $"{system}\n\nUser: {prompt}\nAssistant:";
        var request = new LlamaCppNativeRequest
        {
            Prompt = formattedPrompt,
            Temperature = Configuration.LlamaCpp.Temperature,
            Stream = false
        };

        var response = await _httpClient.PostAsJsonAsync($"{Configuration.LlamaCpp.BaseUrl}/completion", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LlamaCppNativeResponse>(ct);
        return result?.Content ?? string.Empty;
    }

    // ── Frontier Cloud Client Methods ────────────────────────────

    private async Task<string> CallAnthropicAsync(string system, string prompt, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{Configuration.Anthropic.BaseUrl}/v1/messages");
        requestMessage.Headers.Add("x-api-key", Configuration.Anthropic.ApiKey);
        requestMessage.Headers.Add("anthropic-version", "2023-06-01");

        var payload = new AnthropicRequest
        {
            Model = Configuration.Anthropic.Model,
            System = system,
            Messages = new() { new() { Role = "user", Content = prompt } },
            Temperature = Configuration.Anthropic.Temperature
        };

        requestMessage.Content = JsonContent.Create(payload);
        var response = await _httpClient.SendAsync(requestMessage, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(ct);
        return result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
    }

    private async Task<string> CallGeminiAsync(string system, string prompt, CancellationToken ct)
    {
        string url = $"{Configuration.Gemini.BaseUrl}/models/{Configuration.Gemini.Model}:generateContent?key={Configuration.Gemini.ApiKey}";

        var payload = new GeminiRequest
        {
            SystemInstruction = new()
            {
                Parts = new() { new() { Text = system } }
            },
            Contents = new()
            {
                new()
                {
                    Role = "user",
                    Parts = new() { new() { Text = prompt } }
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(ct);
        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
    }

    /// <summary>
    /// Reusable agentic execution loop for all OpenAI-compatible endpoints.
    /// Supports tool use/function calling patterns.
    /// </summary>
    private async Task<string> CallOpenAiCompatibleApiAsync(string baseUrl, string apiKey, string model, string system, string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogError(
                "CallOpenAiCompatibleApiAsync called with empty baseUrl — model={Model} provider={Provider}. " +
                "Open Settings and verify the Base URL for this provider.",
                model, Configuration.ActiveCloudProvider);
            throw new InvalidOperationException(
                $"Cloud provider '{Configuration.ActiveCloudProvider}' has an empty Base URL. " +
                "Open Settings > Frontier Cloud and enter the correct endpoint.");
        }

        _logger.LogTrace(
            "OAI-compat request — url={BaseUrl}/chat/completions model={Model} hasKey={HasKey}",
            baseUrl, model, !string.IsNullOrEmpty(apiKey));

        var messages = new List<OpenAiMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "user", Content = userMessage },
        };

        int steps = 0;
        while (steps < MaxAgenticSteps)
        {
            steps++;

            var request = new OpenAiRequest
            {
                Model = model,
                Messages = messages,
            };

            if (AgenticToolUseEnabled && !string.IsNullOrEmpty(apiKey))
            {
                request.Tools = AgenticToolDefinitions.GetHCEPTools()
                    .Select(t => new OpenAiToolDef
                    {
                        Type = t.Type,
                        Function = new OpenAiFunctionDef
                        {
                            Name = t.Function.Name,
                            Description = t.Function.Description,
                            Parameters = t.Function.Parameters,
                        },
                    })
                    .ToList();
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpRequest.Headers.Authorization = new("Bearer", apiKey);
            }
            httpRequest.Content = JsonContent.Create(request);

            var response = await _httpClient.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(ct);
            var choice = result?.Choices?.FirstOrDefault();

            if (choice?.Message is null)
                return string.Empty;

            if (choice.Message.ToolCalls is { Count: > 0 } toolCalls)
            {
                _logger.LogDebug("Agentic step {Step}: {Count} tool call(s) requested", steps, toolCalls.Count);

                messages.Add(new OpenAiMessage
                {
                    Role = "assistant",
                    Content = choice.Message.Content ?? "",
                    ToolCalls = toolCalls,
                });

                var agenticCalls = toolCalls.Select(tc => new AgenticToolCall
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new AgenticToolCallFunction
                    {
                        Name = tc.Function?.Name ?? "",
                        Arguments = tc.Function?.Arguments ?? "{}",
                    },
                }).ToList();

                var toolResults = await _toolExecutor.ExecuteAsync(agenticCalls, ct);

                foreach (var tr in toolResults)
                {
                    messages.Add(new OpenAiMessage
                    {
                        Role = "tool",
                        Content = tr.Content,
                        ToolCallId = tr.ToolCallId,
                    });
                }

                continue;
            }

            return choice.Message.Content ?? string.Empty;
        }

        return messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "[Max agentic steps reached]";
    }

}
