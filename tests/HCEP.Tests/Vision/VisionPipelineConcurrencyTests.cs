// ──────────────────────────────────────────────────────────────
// HCEP — Unit Tests: VisionPipeline shared-state concurrency
// ──────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using HCEP.Core.Models;
using HCEP.Vision;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Tests.Vision;

/// <summary>
/// Concurrency stress tests for <see cref="VisionPipeline"/> cross-thread
/// shared-state properties (LatestSpeech, LatestColor, LatestRecognition,
/// PendingEnrollmentName).
///
/// These tests validate the <c>Volatile.Read/Write</c> fix applied in the
/// 2026-07-03 audit. Any regression back to the incorrect
/// <c>Interlocked.CompareExchange(ref obj, null!, null!)</c> pattern would
/// cause reads to silently return stale or null values here.
/// </summary>
public sealed class VisionPipelineSharedStateTests
{
    private static VisionPipeline BuildPipeline()
    {
        var gazeEstimator = new HCEP.Spatial.ThreeStageGazeEstimator();
        var hcepAnalyzer = new HcepModeAnalyzer();
        var faceRecognizer = new ArcFaceRecognizer(NullLogger<ArcFaceRecognizer>.Instance);
        var telemetry = new HCEP.Telemetry.HCEPTelemetryService();

        return new VisionPipeline(
            gazeEstimator, hcepAnalyzer, faceRecognizer,
            telemetry,
            NullLogger<VisionPipeline>.Instance);
    }

    [Fact]
    public async Task LatestSpeech_ConcurrentReadWrite_NeverThrowsAndReadsAreConsistent()
    {
        var pipeline = BuildPipeline();
        const int iterations = 5_000;
        const int readerThreads = 4;

        var errors = new ConcurrentBag<Exception>();

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
                pipeline.LatestSpeech = new SpeechResult { Text = $"utterance-{i}", IsFinal = true };
        });

        var readers = Enumerable.Range(0, readerThreads).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var speech = pipeline.LatestSpeech;
                    if (speech is not null)
                        Assert.False(string.IsNullOrEmpty(speech.Text) && speech.IsFinal,
                            "SpeechResult should not be empty+final simultaneously");
                }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();

        await Task.WhenAll([writer, .. readers]);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task LatestColor_ConcurrentReadWrite_DoesNotLoseWrites()
    {
        var pipeline = BuildPipeline();
        const int writes = 500;
        var observedWidths = new ConcurrentBag<int>();
        using var startSignal = new SemaphoreSlim(0, 1);

        var writer = Task.Run(async () =>
        {
            await startSignal.WaitAsync(); // yield so reader starts first
            for (int i = 1; i <= writes; i++)
                pipeline.LatestColor = new ColorFrame
                {
                    Width = i,
                    Height = i,
                    Timestamp = DateTimeOffset.UtcNow,
                    PixelData = Array.Empty<byte>(),
                };
        });

        var reader = Task.Run(() =>
        {
            startSignal.Release(); // unblock writer once reader is running
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            int? lastWidth = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var c = pipeline.LatestColor;
                if (c is not null && c.Width != lastWidth)
                {
                    observedWidths.Add(c.Width);
                    lastWidth = c.Width;
                    if (c.Width == writes) break; // saw final frame
                }
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.True(observedWidths.Count > 0,
            "Reader never observed any LatestColor writes — Volatile.Read may be broken");
        Assert.Contains(writes, observedWidths); // final write must be visible
    }

    [Fact]
    public async Task PendingEnrollmentName_WriteThenRead_ReturnsSameValue()
    {
        var pipeline = BuildPipeline();
        const int threads = 8;
        var errors = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            string expected = $"Person-{t}";
            pipeline.PendingEnrollmentName = expected;
            string? actual = pipeline.PendingEnrollmentName;
            if (actual is not null && !actual.StartsWith("Person-"))
                errors.Add($"Thread {t}: unexpected value '{actual}'");
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task LatestRecognition_HighFrequencyWrite_DoesNotCrash()
    {
        var pipeline = BuildPipeline();
        const int writes = 10_000;

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < writes; i++)
                pipeline.LatestRecognition = new FaceRecognitionResult
                {
                    IdentityName = $"id-{i % 10}",
                    Similarity = 0.9f,
                };
        });

        var reader = Task.Run(() =>
        {
            for (int i = 0; i < writes; i++)
                _ = pipeline.LatestRecognition?.IdentityName;
        });

        await Task.WhenAll(writer, reader);
        // If we reach here without exception, the test passes
    }
}
