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
using System.Collections.Concurrent;
using HCEP.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HCEP.Vision;

/// <summary>
/// ArcFace ONNX-based face recognition engine.
/// Generates 512-d embeddings and matches against enrolled identities
/// using cosine similarity.
/// </summary>
public sealed class ArcFaceRecognizer : IFaceRecognizer, IDisposable
{
    private readonly ILogger<ArcFaceRecognizer> _logger;
    private InferenceSession? _session;
    private readonly ConcurrentDictionary<string, float[]> _enrolledFaces = new();

    /// <summary>Input image dimensions expected by ArcFace (112×112 RGB).</summary>
    public const int InputSize = 112;
    private const int EmbeddingDim = 512;

    public ArcFaceRecognizer(ILogger<ArcFaceRecognizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads the ArcFace ONNX model from the specified path.
    /// </summary>
    public void LoadModel(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning("ArcFace model not found at {Path}", modelPath);
            return;
        }

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        try
        {
            _session = new InferenceSession(modelPath, options);
            _logger.LogInformation("ArcFace model loaded from {Path}", modelPath);
        }
        catch (Exception ex)
        {
            _session = null;
            _logger.LogError(ex, "Failed to load ArcFace ONNX model from {Path} — face recognition disabled", modelPath);
        }
    }

    /// <inheritdoc />
    public float[] GenerateEmbedding(ReadOnlySpan<byte> faceImage, int width, int height)
    {
        if (_session is null)
            return Array.Empty<float>();

        // Preprocess: BGR→RGB, normalize to [-1,1], resize to 112×112
        var inputTensor = PreprocessImage(faceImage, width, height);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("data", inputTensor),
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();

        // L2-normalize the embedding
        Normalize(output);

        return output;
    }

    /// <inheritdoc />
    public float[] GenerateAlignedEmbedding(
        ReadOnlySpan<byte> colorFrame,
        int frameWidth,
        int frameHeight,
        ReadOnlySpan<System.Numerics.Vector2> landmarks5Pt,
        int bytesPerPixel = 4)
    {
        if (_session is null || colorFrame.IsEmpty || frameWidth <= 0 || frameHeight <= 0)
            return Array.Empty<float>();

        DenseTensor<float> inputTensor;
        if (landmarks5Pt.Length >= 5)
        {
            inputTensor = PreprocessAlignedImage(colorFrame, frameWidth, frameHeight, landmarks5Pt, bytesPerPixel);
        }
        else
        {
            // Fallback to unaligned preprocessing if 5-point landmarks not provided
            inputTensor = PreprocessImage(colorFrame, frameWidth, frameHeight);
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("data", inputTensor),
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsEnumerable<float>().ToArray();

        // L2-normalize the embedding
        Normalize(output);

        return output;
    }

    /// <inheritdoc />
    public (string Name, float Similarity)? Match(ReadOnlySpan<float> embedding)
    {
        if (_enrolledFaces.IsEmpty || embedding.Length != EmbeddingDim)
            return null;

        string? bestName = null;
        float bestSim = float.MinValue;

        foreach (var (name, enrolled) in _enrolledFaces)
        {
            float sim = CosineSimilarity(embedding, enrolled);
            if (sim > bestSim)
            {
                bestSim = sim;
                bestName = name;
            }
        }

        if (bestName is not null && bestSim >= MatchThreshold)
            return (bestName, bestSim);

        return null;
    }

    /// <inheritdoc />
    public void Enroll(string name, float[] embedding)
    {
        if (embedding.Length != EmbeddingDim)
            throw new ArgumentException($"Embedding must be {EmbeddingDim}-dimensional", nameof(embedding));

        var normalized = (float[])embedding.Clone();
        Normalize(normalized);

        // If person already enrolled, perform running centroid update for multi-pose robustness
        if (_enrolledFaces.TryGetValue(name, out var existing))
        {
            for (int i = 0; i < EmbeddingDim; i++)
            {
                // 70% existing centroid + 30% new sample
                normalized[i] = existing[i] * 0.7f + normalized[i] * 0.3f;
            }
            Normalize(normalized);
        }

        _enrolledFaces[name] = normalized;
        _logger.LogInformation("Enrolled/Updated identity: {Name} (total: {Count})", name, _enrolledFaces.Count);
    }

    /// <inheritdoc />
    public float MatchThreshold { get; set; } = 0.45f;

    /// <inheritdoc />
    public int EnrolledCount => _enrolledFaces.Count;

    /// <inheritdoc />
    public bool IsModelLoaded => _session is not null;

    public void Dispose()
    {
        _session?.Dispose();
    }

    // ── Private Helpers ────────────────────────────────────────

    // Standard ArcFace 112×112 reference landmarks (Umeyama target)
    private static readonly System.Numerics.Vector2[] CanonicalLandmarks =
    [
        new(38.2946f, 51.6963f), // Left eye
        new(73.5318f, 51.5014f), // Right eye
        new(56.0252f, 71.7366f), // Nose tip
        new(41.5493f, 92.3655f), // Left mouth corner
        new(70.7299f, 92.2041f), // Right mouth corner
    ];

    private static DenseTensor<float> PreprocessAlignedImage(
        ReadOnlySpan<byte> imageData,
        int width,
        int height,
        ReadOnlySpan<System.Numerics.Vector2> landmarks,
        int bpp)
    {
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

        // Compute similarity transform from landmarks to CanonicalLandmarks
        if (!TryComputeInverseSimilarityTransform(landmarks, CanonicalLandmarks, out float invA, out float invB, out float invTx, out float invTy))
        {
            // Fallback to basic resize if singular
            return PreprocessImage(imageData, width, height);
        }

        int stride = width * bpp;

        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                // Map destination pixel (x, y) to source pixel (srcXf, srcYf)
                float srcXf = invA * x - invB * y + invTx;
                float srcYf = invB * x + invA * y + invTy;

                int x0 = Math.Clamp((int)MathF.Floor(srcXf), 0, width - 1);
                int y0 = Math.Clamp((int)MathF.Floor(srcYf), 0, height - 1);
                int x1 = Math.Min(x0 + 1, width - 1);
                int y1 = Math.Min(y0 + 1, height - 1);

                float fx = Math.Clamp(srcXf - x0, 0f, 1f);
                float fy = Math.Clamp(srcYf - y0, 0f, 1f);

                float w00 = (1f - fx) * (1f - fy);
                float w10 = fx * (1f - fy);
                float w01 = (1f - fx) * fy;
                float w11 = fx * fy;

                int idx00 = y0 * stride + x0 * bpp;
                int idx10 = y0 * stride + x1 * bpp;
                int idx01 = y1 * stride + x0 * bpp;
                int idx11 = y1 * stride + x1 * bpp;

                for (int c = 0; c < 3; c++)
                {
                    // Source channel index (BGRA/BGR input → RGB output: channel 0=R reads [+2], 1=G reads [+1], 2=B reads [+0])
                    int srcC = 2 - c;
                    float val = 0f;
                    if (idx00 + srcC < imageData.Length) val += imageData[idx00 + srcC] * w00;
                    if (idx10 + srcC < imageData.Length) val += imageData[idx10 + srcC] * w10;
                    if (idx01 + srcC < imageData.Length) val += imageData[idx01 + srcC] * w01;
                    if (idx11 + srcC < imageData.Length) val += imageData[idx11 + srcC] * w11;

                    tensor[0, c, y, x] = (val / 255f - 0.5f) / 0.5f;
                }
            }
        }

        return tensor;
    }

    private static bool TryComputeInverseSimilarityTransform(
        ReadOnlySpan<System.Numerics.Vector2> src,
        ReadOnlySpan<System.Numerics.Vector2> dst,
        out float invA, out float invB, out float invTx, out float invTy)
    {
        invA = invB = invTx = invTy = 0f;
        int n = Math.Min(src.Length, dst.Length);
        if (n < 3) return false;

        float srcMeanX = 0f, srcMeanY = 0f;
        float dstMeanX = 0f, dstMeanY = 0f;
        for (int i = 0; i < n; i++)
        {
            srcMeanX += src[i].X;
            srcMeanY += src[i].Y;
            dstMeanX += dst[i].X;
            dstMeanY += dst[i].Y;
        }
        srcMeanX /= n; srcMeanY /= n;
        dstMeanX /= n; dstMeanY /= n;

        float srcVar = 0f;
        float cxx = 0f, cxy = 0f;
        for (int i = 0; i < n; i++)
        {
            float dxS = src[i].X - srcMeanX;
            float dyS = src[i].Y - srcMeanY;
            float dxD = dst[i].X - dstMeanX;
            float dyD = dst[i].Y - dstMeanY;

            srcVar += dxS * dxS + dyS * dyS;
            cxx += dxS * dxD + dyS * dyD;
            cxy += dxS * dyD - dyS * dxD;
        }

        if (srcVar < 1e-6f) return false;

        // Forward transform: dst = [a, -b; b, a] * src + [tx, ty]
        float a = cxx / srcVar;
        float b = cxy / srcVar;
        float tx = dstMeanX - (a * srcMeanX - b * srcMeanY);
        float ty = dstMeanY - (b * srcMeanX + a * srcMeanY);

        float det = a * a + b * b;
        if (det < 1e-8f) return false;

        // Inverse transform: src = [invA, -invB; invB, invA] * dst + [invTx, invTy]
        invA = a / det;
        invB = -b / det;
        invTx = (a * (-tx) - b * (-ty)) / det;
        invTy = (b * (-tx) + a * (-ty)) / det;

        return true;
    }

    private static DenseTensor<float> PreprocessImage(ReadOnlySpan<byte> imageData, int width, int height)
    {
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);

        // Bilinear interpolation resize + normalize to [-1, 1]
        float scaleX = (float)width / InputSize;
        float scaleY = (float)height / InputSize;

        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                float srcXf = x * scaleX;
                float srcYf = y * scaleY;

                int x0 = Math.Min((int)srcXf, width - 1);
                int y0 = Math.Min((int)srcYf, height - 1);
                int x1 = Math.Min(x0 + 1, width - 1);
                int y1 = Math.Min(y0 + 1, height - 1);

                float fx = srcXf - x0;
                float fy = srcYf - y0;

                // Bilinear weights
                float w00 = (1f - fx) * (1f - fy);
                float w10 = fx * (1f - fy);
                float w01 = (1f - fx) * fy;
                float w11 = fx * fy;

                int idx00 = (y0 * width + x0) * 3;
                int idx10 = (y0 * width + x1) * 3;
                int idx01 = (y1 * width + x0) * 3;
                int idx11 = (y1 * width + x1) * 3;

                for (int c = 0; c < 3; c++)
                {
                    // Source channel index (BGR input → RGB output: channel 0=R reads [+2], 1=G reads [+1], 2=B reads [+0])
                    int srcC = 2 - c;
                    float val = 0f;
                    if (idx00 + srcC < imageData.Length) val += imageData[idx00 + srcC] * w00;
                    if (idx10 + srcC < imageData.Length) val += imageData[idx10 + srcC] * w10;
                    if (idx01 + srcC < imageData.Length) val += imageData[idx01 + srcC] * w01;
                    if (idx11 + srcC < imageData.Length) val += imageData[idx11 + srcC] * w11;

                    tensor[0, c, y, x] = (val / 255f - 0.5f) / 0.5f;
                }
            }
        }

        return tensor;
    }

    private static void Normalize(float[] vector)
    {
        float sumSq = 0;
        for (int i = 0; i < vector.Length; i++)
            sumSq += vector[i] * vector[i];

        float norm = MathF.Sqrt(sumSq);
        if (norm > 1e-10f)
        {
            for (int i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 1e-10f ? dot / denom : 0f;
    }
}
