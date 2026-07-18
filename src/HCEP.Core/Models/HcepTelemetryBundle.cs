// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using HCEP.Core.Enums;

namespace HCEP.Core.Models;

/// <summary>
/// Immutable, LLM-facing snapshot of every live HCEP sensor signal at chat
/// send time. Populated by the UI before each <c>PromptAsync</c> call and
/// serialized verbatim into the system prompt so the LLM can "see" via
/// telemetry rather than hallucinating a visual sensory channel it does
/// not possess.
///
/// <para>
/// This record is additive — no legacy code has to consume it. Fields are
/// nullable so a partially-connected pipeline (e.g., no Kinect, no speech)
/// simply omits sections rather than emitting misleading placeholder data.
/// </para>
/// </summary>
public sealed record HcepTelemetryBundle
{
    /// <summary>Correlation ID for the chat request that emitted this bundle.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Wall-clock capture time (UTC).</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Whether the sensor pipeline is currently running.</summary>
    public bool PipelineRunning { get; init; }

    /// <summary>Approximate perception pipeline FPS.</summary>
    public double PipelineFps { get; init; }

    /// <summary>Number of currently tracked persons in the scene.</summary>
    public int TrackedPersons { get; init; }

    /// <summary>Latest fused HCEP reading for the primary tracked person.</summary>
    public HcepReading? PrimaryHcep { get; init; }

    /// <summary>Human-friendly identity label for the primary person, or null if unknown.</summary>
    public string? PrimaryIdentity { get; init; }

    /// <summary>Distance from sensor to primary person in metres.</summary>
    public float? PrimaryDistanceM { get; init; }

    /// <summary>Left eye 3D position (metres, camera space).</summary>
    public Vector3? LeftEyePosition { get; init; }

    /// <summary>Right eye 3D position (metres, camera space).</summary>
    public Vector3? RightEyePosition { get; init; }

    /// <summary>Inter-ocular distance in millimetres.</summary>
    public float? InterOcularDistanceMm { get; init; }

    /// <summary>Head rotation (pitch, yaw, roll) in degrees.</summary>
    public Vector3? HeadRotationDeg { get; init; }

    /// <summary>Live time/space/situation context, or null if unavailable.</summary>
    public ContextSnapshot? Context { get; init; }

    /// <summary>Latest speech cadence estimate, or null if speech pipeline is idle.</summary>
    public SpeechCadenceProfile? Cadence { get; init; }

    /// <summary>Text of the most recent finalized speech segment, or null.</summary>
    public string? LatestSpeech { get; init; }

    /// <summary>
    /// Size of the rolling telemetry window in seconds. 0 = snapshot-only.
    /// </summary>
    public int TelemetryWindowSeconds { get; init; }

    /// <summary>
    /// Bounded rolling telemetry history used to summarize recent trends for
    /// the LLM. This is intentionally compact and should be kept short.
    /// </summary>
    public IReadOnlyList<HcepTelemetrySample> History { get; init; } = Array.Empty<HcepTelemetrySample>();

    /// <summary>
    /// Number of sampled anchor points emitted in the timeline section of the
    /// rolling telemetry window. Higher values give a denser timeline.
    /// </summary>
    public int TelemetryTimelineSampleCount { get; init; } = 5;

    /// <summary>
    /// The user's requested density before any automatic prompt-budget
    /// protection coarsens it.
    /// </summary>
    public int RequestedTelemetryTimelineSampleCount { get; init; } = 5;

    /// <summary>
    /// True when HCEP automatically reduced timeline density to keep the
    /// prompt bounded for longer or speech-heavy telemetry windows.
    /// </summary>
    public bool TelemetryTimelineAutoCoarsened { get; init; }

    /// <summary>Whether a Kinect (or webcam) sensor is currently connected.</summary>
    public bool SensorConnected { get; init; }

    /// <summary>Whether calibration has been applied at least once this session.</summary>
    public bool CalibrationApplied { get; init; }

    /// <summary>Empty telemetry bundle — used when the pipeline hasn't yet produced a snapshot.</summary>
    public static HcepTelemetryBundle Empty { get; } = new()
    {
        CapturedAt = DateTimeOffset.MinValue,
    };

    /// <summary>
    /// Emit a stable, low-noise textual representation of the bundle suitable
    /// for direct inclusion in an LLM system prompt. Every field is labeled
    /// so the model can quote or ignore them accurately. Missing values are
    /// rendered as <c>unavailable</c> rather than a fake number.
    /// </summary>
    public string ToPromptString()
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("=== HCEP LIVE SENSORY TELEMETRY (this is how you 'see' the user) ===");
        sb.AppendLine($"Captured at: {CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrWhiteSpace(CorrelationId))
            sb.AppendLine($"Correlation ID: {CorrelationId}");
        sb.AppendLine($"Pipeline: {(PipelineRunning ? "running" : "stopped")}  " +
                      $"Sensor: {(SensorConnected ? "connected" : "disconnected")}  " +
                      $"FPS: {(PipelineFps > 0 ? PipelineFps.ToString("F1") : "unavailable")}  " +
                      $"Calibration: {(CalibrationApplied ? "applied" : "defaults")}");
        sb.AppendLine($"Tracked persons: {TrackedPersons}");

        if (TrackedPersons == 0 || PrimaryHcep is null)
        {
            sb.AppendLine("Primary person: none — you cannot see anyone right now.");
        }
        else
        {
            var name = string.IsNullOrWhiteSpace(PrimaryIdentity) ? "unknown (not enrolled)" : PrimaryIdentity;
            sb.AppendLine($"Primary identity: {name}");
            sb.AppendLine($"HCEP mode: {PrimaryHcep.Mode}  " +
                          $"Gaze region: {PrimaryHcep.Region}  " +
                          $"Cognitive: {PrimaryHcep.Cognitive}  " +
                          $"Valence: {PrimaryHcep.Valence}  " +
                          $"Confidence: {PrimaryHcep.Confidence:F2}");
            sb.AppendLine($"Distance from sensor: {(PrimaryDistanceM.HasValue ? $"{PrimaryDistanceM.Value:F2} m" : "unavailable")}");

            if (HeadRotationDeg is { } r)
                sb.AppendLine($"Head pose: pitch={r.X:+0.0;-0.0;0.0}°  yaw={r.Y:+0.0;-0.0;0.0}°  roll={r.Z:+0.0;-0.0;0.0}°");
            else
                sb.AppendLine("Head pose: unavailable");

            if (LeftEyePosition is { } le && RightEyePosition is { } re)
            {
                sb.AppendLine($"Left eye (m): ({le.X:F3}, {le.Y:F3}, {le.Z:F3})");
                sb.AppendLine($"Right eye (m): ({re.X:F3}, {re.Y:F3}, {re.Z:F3})");
            }
            if (InterOcularDistanceMm is { } iod && iod > 0)
                sb.AppendLine($"Inter-ocular distance: {iod:F1} mm");

            var gd = PrimaryHcep.GazeDirection;
            sb.AppendLine($"Gaze direction (unit vec): ({gd.X:F3}, {gd.Y:F3}, {gd.Z:F3})");
        }

        if (Context is not null)
        {
            sb.AppendLine();
            sb.AppendLine("=== Contextual Snapshot ===");
            sb.AppendLine(Context.ToPromptString());
        }

        if (Cadence is not null && Cadence.IsFresh)
        {
            sb.AppendLine();
            sb.AppendLine($"Speech cadence: {Cadence.SyllablesPerSecond:F2} syl/s  " +
                          $"avg pause: {Cadence.AveragePauseDurationMs:F0} ms  " +
                          $"last burst: {Cadence.LastSpeechBurstMs:F0} ms");
        }

        if (!string.IsNullOrWhiteSpace(LatestSpeech))
        {
            sb.AppendLine();
            sb.AppendLine("Most recent transcribed speech (last finalized segment):");
            sb.AppendLine($"  \"{LatestSpeech.Trim()}\"");
        }

        AppendTelemetryWindow(sb);

        sb.AppendLine("=== END TELEMETRY ===");
        return sb.ToString();
    }

    private void AppendTelemetryWindow(StringBuilder sb)
    {
        if (TelemetryWindowSeconds <= 0 || History.Count <= 1)
        {
            sb.AppendLine();
            sb.AppendLine("Telemetry window: snapshot only.");
            return;
        }

        var ordered = History
            .Where(sample => sample.Timestamp > DateTimeOffset.MinValue)
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        if (ordered.Length <= 1)
        {
            sb.AppendLine();
            sb.AppendLine("Telemetry window: snapshot only.");
            return;
        }

        var first = ordered[0];
        var last = ordered[^1];
        var nonUnknownModes = ordered.Where(sample => sample.Mode != HcepMode.Unknown).ToArray();
        var nonUnknownRegions = ordered.Where(sample => sample.Region != GazeRegion.Unknown).ToArray();
        var nonUnknownValence = ordered.Where(sample => sample.Valence != EmotionalValence.Unknown).ToArray();
        var distances = ordered.Where(sample => sample.DistanceM.HasValue).Select(sample => sample.DistanceM!.Value).ToArray();
        var rotations = ordered.Where(sample => sample.HeadRotationDeg.HasValue).Select(sample => sample.HeadRotationDeg!.Value).ToArray();
        var speechEvents = ordered.Where(sample => !string.IsNullOrWhiteSpace(sample.LatestSpeech)).ToArray();
        var identities = ordered
            .Select(sample => sample.PrimaryIdentity)
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Select(identity => identity!)
            .ToArray();

        sb.AppendLine();
        sb.AppendLine($"=== Telemetry Window ({TelemetryWindowSeconds}s rolling context) ===");
        sb.AppendLine($"Samples: {ordered.Length}  Range: {first.Timestamp.ToLocalTime():HH:mm:ss} → {last.Timestamp.ToLocalTime():HH:mm:ss}");
        if (TelemetryTimelineAutoCoarsened)
            sb.AppendLine($"Timeline density: {TelemetryTimelineSampleCount} sampled anchor point(s) (auto-coarsened from {RequestedTelemetryTimelineSampleCount} to protect prompt budget)");
        else
            sb.AppendLine($"Timeline density: {TelemetryTimelineSampleCount} sampled anchor point(s)");
        sb.AppendLine($"Dominant mode: {DescribeDominant(nonUnknownModes.Select(sample => sample.Mode.ToString()))}");
        sb.AppendLine($"Dominant gaze region: {DescribeDominant(nonUnknownRegions.Select(sample => sample.Region.ToString()))}");
        sb.AppendLine($"Dominant valence: {DescribeDominant(nonUnknownValence.Select(sample => sample.Valence.ToString()))}");
        sb.AppendLine($"Confidence trend: avg={ordered.Average(sample => sample.Confidence):F2}  min={ordered.Min(sample => sample.Confidence):F2}  max={ordered.Max(sample => sample.Confidence):F2}");

        if (distances.Length > 0)
            sb.AppendLine($"Distance trend: {distances.Min():F2} m → {distances.Max():F2} m");

        if (rotations.Length > 0)
        {
            sb.AppendLine(
                $"Head-pose range: pitch {rotations.Min(rot => rot.X):+0.0;-0.0;0.0}°..{rotations.Max(rot => rot.X):+0.0;-0.0;0.0}°  " +
                $"yaw {rotations.Min(rot => rot.Y):+0.0;-0.0;0.0}°..{rotations.Max(rot => rot.Y):+0.0;-0.0;0.0}°");
        }

        if (identities.Length > 0)
            sb.AppendLine($"Identity stability: {DescribeDominant(identities)}");

        sb.AppendLine($"Speech activity in window: {speechEvents.Length} sampled moment(s) with transcript activity.");
        sb.AppendLine("Sampled timeline:");

        foreach (var sample in SampleTimeline(ordered, Math.Clamp(TelemetryTimelineSampleCount, 3, 9)))
        {
            string distance = sample.DistanceM.HasValue ? $"{sample.DistanceM.Value:F2}m" : "unavailable";
            string head = sample.HeadRotationDeg is { } rot
                ? $"pitch={rot.X:+0.0;-0.0;0.0}° yaw={rot.Y:+0.0;-0.0;0.0}°"
                : "head=unavailable";
            string speech = string.IsNullOrWhiteSpace(sample.LatestSpeech)
                ? "speech=none"
                : $"speech=\"{TrimToSingleLine(sample.LatestSpeech!, 64)}\"";
            sb.AppendLine(
                $"- {sample.Timestamp.ToLocalTime():HH:mm:ss}: mode={sample.Mode} region={sample.Region} valence={sample.Valence} conf={sample.Confidence:F2} dist={distance} {head} {speech}");
        }
    }

    private static string DescribeDominant(IEnumerable<string> values)
    {
        var grouped = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return grouped is null ? "unavailable" : $"{grouped.Key} ({grouped.Count()})";
    }

    private static IEnumerable<HcepTelemetrySample> SampleTimeline(HcepTelemetrySample[] ordered, int maxEntries)
    {
        if (ordered.Length <= maxEntries)
            return ordered;

        var result = new List<HcepTelemetrySample>(maxEntries);
        double step = (ordered.Length - 1d) / (maxEntries - 1d);
        for (int i = 0; i < maxEntries; i++)
        {
            int index = (int)Math.Round(i * step, MidpointRounding.AwayFromZero);
            result.Add(ordered[Math.Clamp(index, 0, ordered.Length - 1)]);
        }
        return result;
    }

    private static string TrimToSingleLine(string value, int maxLength)
    {
        string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;
        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
