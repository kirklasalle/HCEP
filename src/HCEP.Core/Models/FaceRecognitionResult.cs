// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

namespace HCEP.Core.Models;

/// <summary>
/// Result from ArcFace face recognition — identity name, similarity score, and embedding.
/// </summary>
public sealed record FaceRecognitionResult
{
    /// <summary>Recognized identity name, or null if unknown.</summary>
    public string? IdentityName { get; init; }

    /// <summary>Cosine similarity to closest enrolled identity [0..1].</summary>
    public float Similarity { get; init; }

    /// <summary>512-d ArcFace embedding vector for this face.</summary>
    public float[]? Embedding { get; init; }

    /// <summary>Whether a positive match was found above the threshold.</summary>
    public bool IsMatch => IdentityName is not null;

    /// <summary>Timestamp of the recognition.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
