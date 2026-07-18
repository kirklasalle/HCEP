// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
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
}

/// <summary>
/// First-stage avatar platform registry. Centralizes what avatar lines exist
/// today and what future lines are being reserved by the platform design.
/// </summary>
public sealed class AvatarCatalog : IAvatarCatalog
{
    private static readonly AvatarDescriptor[] _all =
    [
        new("2d-happy", "2D Happy", false, true,
            Summary: "Shipping vector-based 2D avatar with gaze, blinks, brows, smile, proxemics, viseme lip sync, and reciprocal gestures."),
        new("3d-wireframe", "3D Wireframe", true, true,
            Summary: "Shipping 3D wireframe avatar with neutralized mesh rendering, eye-socket gaze tracking, visemes, brows, and reciprocal gestures."),
        new("3d-textured", "3D Textured (future)", true, false,
            Summary: "Planned higher-fidelity textured avatar tier for richer facial presence and commercial presentation."),
        new("personalized-avatar", "Personalized Avatar (future)", false, false,
            Summary: "Planned consent-based user-derived avatar with stylized or semi-realistic likeness."),
        new("cloned-likeness-rnd", "Cloned Likeness R&D (future)", true, false,
            Summary: "Reserved research track for near-real-time cloned video/audio avatar systems under explicit ethics and consent controls.")
    ];

    public IReadOnlyList<AvatarDescriptor> GetSelectableAvatars() => _all.Where(a => a.IsImplemented).ToArray();
    public IReadOnlyList<AvatarDescriptor> GetPlannedAvatars() => _all.Where(a => !a.IsImplemented).ToArray();
}