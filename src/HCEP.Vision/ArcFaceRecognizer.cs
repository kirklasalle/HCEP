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
        _enrolledFaces[name] = normalized;
        _logger.LogInformation("Enrolled identity: {Name} (total: {Count})", name, _enrolledFaces.Count);
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
