// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Intelligence;

/// <summary>
/// Centralized settings-schema migration pipeline for <see cref="LlmConfiguration"/>.
///
/// Older settings files may not contain newly-added properties. This migrator
/// normalizes and forward-fills those values, then stamps the configuration
/// with the current schema version before it is used by the runtime.
/// </summary>
public static class ConfigurationMigration
{
    public const int CurrentSchemaVersion = 2;

    public static LlmConfiguration Apply(LlmConfiguration config, int detectedSchemaVersion, ILogger? logger = null)
    {
        int version = detectedSchemaVersion;

        if (version <= 0)
        {
            logger?.LogInformation("Migrating legacy HCEP settings payload with no schema version → v1");
            version = 1;
        }

        if (version < 2)
        {
            // v2 introduces chat telemetry harness persistence + debug pane state.
            config.ChatTelemetryWindowSeconds = Math.Clamp(config.ChatTelemetryWindowSeconds, 0, 5);
            config.ChatTelemetryDensityLevel = config.ChatTelemetryDensityLevel switch
            {
                < 1 => 2,
                > 3 => 3,
                _ => config.ChatTelemetryDensityLevel
            };
            config.ChatTelemetryDebugExpanded = false;
            config.ChatSystemPromptDebugExpanded = false;
            version = 2;
            logger?.LogInformation("Migrated HCEP settings schema → v2 (chat telemetry harness normalization)");
        }

        // Defensive normalization for all versions.
        config.ChatTelemetryWindowSeconds = Math.Clamp(config.ChatTelemetryWindowSeconds, 0, 5);
        config.ChatTelemetryDensityLevel = Math.Clamp(config.ChatTelemetryDensityLevel, 1, 3);
        config.SchemaVersion = CurrentSchemaVersion;
        return config;
    }
}