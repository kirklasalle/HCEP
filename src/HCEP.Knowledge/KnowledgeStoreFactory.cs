// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using HCEP.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HCEP.Knowledge;

/// <summary>
/// Factory for constructing the appropriate <see cref="IKnowledgeStore"/> implementation.
/// Implements Strategy D — tries to instantiate the UKS-backed adapter first;
/// falls back to the standalone <see cref="InMemoryKnowledgeStore"/> if UKS is
/// unavailable or fails to initialize.
/// </summary>
public static class KnowledgeStoreFactory
{
    /// <summary>
    /// Registers the knowledge store with the DI container.
    /// Uses <see cref="UksKnowledgeAdapter"/> (Strategy D hybrid) which
    /// auto-detects UKS.dll presence and falls back to in-memory store.
    /// </summary>
    public static IServiceCollection AddHCEPKnowledge(this IServiceCollection services)
    {
        // Register the encrypted storage provider
        services.AddSingleton<EncryptedStorageProvider>();

        // Register the fallback store (always needed — used as mirror by the adapter)
        services.AddSingleton<InMemoryKnowledgeStore>();

        // Register the UKS adapter (Strategy D hybrid)
        services.AddSingleton<UksKnowledgeAdapter>();

        // IKnowledgeStore resolves to the UKS adapter (which delegates to fallback internally)
        services.AddSingleton<IKnowledgeStore>(sp => sp.GetRequiredService<UksKnowledgeAdapter>());

        // Person knowledge manager
        services.AddSingleton<PersonKnowledgeManager>();

        return services;
    }

    /// <summary>
    /// Creates a standalone <see cref="IKnowledgeStore"/> without DI.
    /// Useful for testing or CLI scenarios.
    /// </summary>
    public static IKnowledgeStore CreateStandalone(ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        var fallback = new InMemoryKnowledgeStore(
            loggerFactory.CreateLogger<InMemoryKnowledgeStore>());

        return new UksKnowledgeAdapter(
            loggerFactory.CreateLogger<UksKnowledgeAdapter>(),
            fallback);
    }
}
