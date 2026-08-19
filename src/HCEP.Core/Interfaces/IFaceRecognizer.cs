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
namespace HCEP.Core.Interfaces;

/// <summary>
/// Face recognition engine using ArcFace ONNX embeddings.
/// Handles enrollment, matching, and embedding persistence.
/// </summary>
public interface IFaceRecognizer
{
    /// <summary>
    /// Generates a 512-dimensional ArcFace embedding from a face crop.
    /// </summary>
    /// <param name="faceImage">Aligned, cropped face image (112x112 RGB).</param>
    /// <returns>Normalized 512-d embedding vector.</returns>
    float[] GenerateEmbedding(ReadOnlySpan<byte> faceImage, int width, int height);

    /// <summary>
    /// Generates a 512-dimensional ArcFace embedding from a full color frame using
    /// 5-point landmark similarity transformation for optimal recognition precision.
    /// </summary>
    /// <param name="colorFrame">Full BGRA/BGR color frame bytes.</param>
    /// <param name="frameWidth">Color frame width.</param>
    /// <param name="frameHeight">Color frame height.</param>
    /// <param name="landmarks5Pt">5 detected landmarks (left eye, right eye, nose, left mouth, right mouth).</param>
    /// <param name="bytesPerPixel">Bytes per pixel (typically 4 for BGRA32, 3 for BGR24).</param>
    /// <returns>Normalized 512-d embedding vector.</returns>
    float[] GenerateAlignedEmbedding(
        ReadOnlySpan<byte> colorFrame,
        int frameWidth,
        int frameHeight,
        ReadOnlySpan<System.Numerics.Vector2> landmarks5Pt,
        int bytesPerPixel = 4);

    /// <summary>
    /// Finds the best-matching enrolled identity for an embedding.
    /// </summary>
    /// <param name="embedding">Query embedding.</param>
    /// <returns>Identity name and cosine similarity, or null if no match above threshold.</returns>
    (string Name, float Similarity)? Match(ReadOnlySpan<float> embedding);

    /// <summary>
    /// Enrolls a new identity with the given embedding.
    /// </summary>
    /// <param name="name">Person's name.</param>
    /// <param name="embedding">512-d embedding vector.</param>
    void Enroll(string name, float[] embedding);

    /// <summary>
    /// Cosine similarity threshold for a positive match.
    /// </summary>
    float MatchThreshold { get; set; }

    /// <summary>
    /// Number of enrolled identities.
    /// </summary>
    int EnrolledCount { get; }

    /// <summary>
    /// Whether the recognition model has been loaded and is ready.
    /// </summary>
    bool IsModelLoaded { get; }
}
