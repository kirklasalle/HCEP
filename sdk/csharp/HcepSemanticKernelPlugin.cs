// ──────────────────────────────────────────────────────────────
// HCEP SDK — Semantic Kernel Plugin
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace HCEP.Sdk.SemanticKernel;

/// <summary>
/// Semantic Kernel Plugin exposing the HCEP state engine.
/// Allows Semantic Kernel agentic workflows to inspect real-time gaze and cognitive modes.
/// </summary>
public sealed class HcepSemanticKernelPlugin
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HcepSemanticKernelPlugin(HttpClient httpClient, string baseUrl = "http://localhost:5000")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUrl = baseUrl.TrimEnd('/');
    }

    [KernelFunction]
    [Description("Queries the Human Communication Eye Protocol (HCEP) engine to get the active user's real-time gaze details, head positions, and cognitive-emotional state classification.")]
    public async Task<string> GetHcepStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/state", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            return $"Error: HCEP returned status code {(int)response.StatusCode} ({response.ReasonPhrase})";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to connect to HCEP Plugin API. {ex.Message}";
        }
    }
}
