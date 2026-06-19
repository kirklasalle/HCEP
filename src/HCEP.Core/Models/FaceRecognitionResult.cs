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
