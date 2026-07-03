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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Intelligence;

/// <summary>
/// Serializes and deserializes <see cref="LlmConfiguration"/> to a JSON settings file.
///
/// Security model
/// ──────────────
/// API keys are NEVER written to the settings file.  The <c>ApiKey</c> property
/// on <see cref="CloudProviderSettings"/> is decorated with <c>[JsonIgnore]</c>,
/// so it is silently skipped during both serialization and deserialization.
/// Keys are persisted exclusively via <see cref="WindowsCredentialStore"/>
/// (Windows Credential Manager), which encrypts them at rest under the user's
/// DPAPI master key.
///
/// Settings file location (first match wins)
/// ──────────────────────────────────────────
///   1. <c>HCEP_SETTINGS_DIR</c> environment variable
///   2. <c>config/hcep-settings.json</c> adjacent to <c>HCEP.sln</c> (dev tree)
///   3. <c>%LocalAppData%\HCEP\hcep-settings.json</c> (release / installed)
/// </summary>
public static class SettingsPersistence
{
    private const string SettingsFileName = "hcep-settings.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters             = { new JsonStringEnumConverter() },
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes <paramref name="config"/> to the HCEP settings file.
    /// API keys are excluded — they are managed by <see cref="WindowsCredentialStore"/>.
    /// </summary>
    public static void Save(LlmConfiguration config, ILogger? logger = null)
    {
        try
        {
            string path = GetSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
            logger?.LogInformation("Settings persisted to {Path}", path);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save settings — configuration changes will be lost on restart");
        }
    }

    /// <summary>
    /// Deserializes <see cref="LlmConfiguration"/> from the HCEP settings file.
    /// Returns <c>null</c> if no file is found (first run) or if deserialization fails.
    /// API keys in the returned object are empty; load them separately from WCM.
    /// </summary>
    public static LlmConfiguration? Load(ILogger? logger = null)
    {
        try
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path))
            {
                logger?.LogDebug("No settings file found at {Path} — using defaults", path);
                return null;
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<LlmConfiguration>(json, _jsonOptions);

            if (config is null)
            {
                logger?.LogWarning("Settings file at {Path} deserialized to null — using defaults", path);
                return null;
            }

            logger?.LogInformation("Settings loaded from {Path}", path);
            return config;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load settings — using defaults");
            return null;
        }
    }

    /// <summary>Returns the resolved settings file path.</summary>
    public static string GetSettingsFilePath()
    {
        string? envOverride = Environment.GetEnvironmentVariable("HCEP_SETTINGS_DIR");
        if (!string.IsNullOrEmpty(envOverride))
            return Path.Combine(envOverride, SettingsFileName);

        // Walk up from AppContext.BaseDirectory looking for HCEP.sln
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir, "HCEP.sln")))
                return Path.Combine(dir, "config", SettingsFileName);
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: %LocalAppData%\HCEP\hcep-settings.json
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HCEP",
            SettingsFileName);
    }
}
