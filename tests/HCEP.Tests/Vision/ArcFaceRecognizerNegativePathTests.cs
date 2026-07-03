// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: ArcFaceRecognizer negative-path (corrupted/missing model)
// ──────────────────────────────────────────────────────────────
using HCEP.Vision;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Vision;

/// <summary>
/// Negative-path tests for <see cref="ArcFaceRecognizer"/>.
/// Validates graceful handling of missing, corrupted, and zero-byte model files.
/// </summary>
public sealed class ArcFaceRecognizerNegativePathTests
{
    [Fact]
    public void LoadModel_MissingFile_IsModelLoadedFalse()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        recognizer.LoadModel(@"C:\nonexistent\path\model.onnx");
        Assert.False(recognizer.IsModelLoaded);
    }

    [Fact]
    public void LoadModel_CorruptedFile_DoesNotThrow_IsModelLoadedFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hcep_bad_model_{Guid.NewGuid()}.onnx");
        try
        {
            // Write garbage bytes — not a valid ONNX file
            File.WriteAllBytes(path, [0x00, 0xFF, 0xAB, 0xCD, 0x12, 0x34]);

            var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);

            // Must NOT throw — fix verified by audit 2026-07-03
            var ex = Record.Exception(() => recognizer.LoadModel(path));
            Assert.Null(ex);
            Assert.False(recognizer.IsModelLoaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadModel_ZeroByteFile_DoesNotThrow_IsModelLoadedFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hcep_empty_model_{Guid.NewGuid()}.onnx");
        try
        {
            File.WriteAllBytes(path, []);

            var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
            var ex = Record.Exception(() => recognizer.LoadModel(path));
            Assert.Null(ex);
            Assert.False(recognizer.IsModelLoaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GenerateEmbedding_ModelNotLoaded_ReturnsEmptyArray()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        // Do not load any model
        var result = recognizer.GenerateEmbedding(new byte[112 * 112 * 3], 112, 112);
        Assert.Empty(result);
    }

    [Fact]
    public void Match_ModelNotLoaded_ReturnsNull()
    {
        var recognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        var dummyEmbedding = new float[512];
        var result = recognizer.Match(dummyEmbedding);
        Assert.Null(result);
    }
}
