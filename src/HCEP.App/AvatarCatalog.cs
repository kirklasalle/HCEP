// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HCEP.App;

/// <summary>
/// Descriptor for a selectable avatar implementation or planned avatar line.
/// The current app only instantiates entries where <see cref="IsImplemented"/>
/// is true; the rest document forward-compatible avatar platform slots.
/// </summary>
public sealed record AvatarDescriptor(
    string Key,
    string DisplayName,
    bool Use3DMode,
    bool IsImplemented,
    string Summary);

public interface IAvatarCatalog
{
    IReadOnlyList<AvatarDescriptor> GetSelectableAvatars();
    IReadOnlyList<AvatarDescriptor> GetPlannedAvatars();
    void RegisterCustomAvatar(AvatarDescriptor descriptor, Func<IAvatarComponent>? factory = null);
    IAvatarComponent? CreateAvatarInstance(string key);
    event Action? CatalogChanged;
}

/// <summary>
/// First-stage avatar platform registry. Centralizes what avatar lines exist
/// today and what future lines are being reserved by the platform design.
/// Supports dynamic registration from HCEP Avatar Studio.
/// </summary>
public sealed class AvatarCatalog : IAvatarCatalog
{
    private static readonly List<AvatarDescriptor> _builtIn =
    [
        new("2d-happy", "2D Happy", false, true,
            Summary: "Shipping vector-based 2D avatar with gaze, blinks, brows, smile, proxemics, viseme lip sync, and reciprocal gestures."),
        new("3d-wireframe", "3D Wireframe", true, true,
            Summary: "Shipping 3D wireframe avatar with neutralized mesh rendering, eye-socket gaze tracking, visemes, brows, and reciprocal gestures."),
        new("3d-highpoly-wireframe", "3D High-Poly Wireframe", true, true,
            Summary: "Procedural high-density head-and-shoulders wireframe avatar with HCEP eye spheres, brows, visemes, proxemics, and reciprocal gestures."),
        new("3d-textured", "3D Textured (future)", true, false,
            Summary: "Planned higher-fidelity textured avatar tier for richer facial presence and commercial presentation."),
        new("personalized-avatar", "Personalized Avatar (future)", false, false,
            Summary: "Planned consent-based user-derived avatar with stylized or semi-realistic likeness."),
        new("cloned-likeness-rnd", "Cloned Likeness R&D (future)", true, false,
            Summary: "Reserved research track for near-real-time cloned video/audio avatar systems under explicit ethics and consent controls.")
    ];

    private readonly List<AvatarDescriptor> _customAvatars = new();
    private readonly ConcurrentDictionary<string, Func<IAvatarComponent>> _factories = new();

    public event Action? CatalogChanged;

    public IReadOnlyList<AvatarDescriptor> GetSelectableAvatars() =>
        _builtIn.Where(a => a.IsImplemented).Concat(_customAvatars).ToArray();

    public IReadOnlyList<AvatarDescriptor> GetPlannedAvatars() =>
        _builtIn.Where(a => !a.IsImplemented).ToArray();

    public void RegisterCustomAvatar(AvatarDescriptor descriptor, Func<IAvatarComponent>? factory = null)
    {
        _customAvatars.RemoveAll(a => a.Key.Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase));
        _customAvatars.Add(descriptor);

        if (factory is not null)
        {
            _factories[descriptor.Key] = factory;
        }

        CatalogChanged?.Invoke();
    }

    public IAvatarComponent? CreateAvatarInstance(string key)
    {
        if (_factories.TryGetValue(key, out var factory))
        {
            return factory();
        }
        return null;
    }
}