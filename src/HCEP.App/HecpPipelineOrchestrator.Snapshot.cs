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
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Spatial;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

public sealed partial class HCEPPipelineOrchestrator
{
    /// <summary>
    /// Consumes HcepReadings from the vision pipeline in a background task,
    /// storing the latest reading for the snapshot timer to pick up.
    /// </summary>
    private async Task ConsumeHcepReadingsAsync(CancellationToken ct)
    {
        long hcepFrameCount = 0;
        try
        {
            await foreach (var reading in _vision.HcepOutput.ReadAllAsync(ct))
            {
                _latestHcep = reading;
                _hcepFpsCounter.Tick();
                hcepFrameCount++;

                if (hcepFrameCount <= 5 || hcepFrameCount % 150 == 0)
                    _logger.LogInformation(
                        "HCEP reading #{Frame}: mode={Mode} region={Region} conf={Conf:F3} hcepFps={Fps:F1}",
                        hcepFrameCount, reading.Mode, reading.Region, reading.Confidence, _hcepFpsCounter.Fps);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HCEP consumer error");
        }
    }

    /// <summary>
    /// Timer-driven snapshot loop at ~10 Hz. Always produces snapshots
    /// so the main window updates even when no HCEP data is available.
    /// This prevents the UI from staying blank while waiting for face tracking.
    /// </summary>
    private async Task RunSnapshotLoopAsync(CancellationToken ct)
    {
        long frameNumber = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); // ~10 Hz

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _fpsCounter.Tick();
                frameNumber++;

                // ── Phase 6: advance micro-saccade timer (loop runs at ~10 Hz)
                _saccade.Update(0.1);

                var hcep = _latestHcep;
                var latestSkel = _latestSkeleton;
                var latestFace = _latestFace;
                var recognition = _vision.LatestRecognition;

                // Build TrackedPerson from whatever data is available
                TrackedPerson? person = null;
                if (hcep is not null || latestFace is not null || latestSkel is not null)
                {
                    var headPos = hcep?.GazeOrigin
                            ?? (latestSkel?.Joints?.ContainsKey(3) == true ? latestSkel.Joints[3] : default);

                    // ── Eye Location Computation ──────────────────
                    // Derive 3D camera-space positions for each eye from head position
                    // and inter-ocular offset (~32mm half-distance), applying yaw rotation.
                    var (leftEye, rightEye) = ComputeEyePositions(headPos, latestFace);

                    // ── Phase 6: calibrated gaze + IK target ──────────
                    Vector3 calibratedGaze = Vector3.Zero;
                    Vector3? avatarIkTarget = null;

                    if (hcep is not null)
                    {
                        // Dynamic parallax correction using live user depth (mm)
                        float userDepthMm = hcep.GazeOrigin.Z * 1000f; // metres → mm
                        calibratedGaze = _calibration.ApplyCalibration(
                            hcep.GazeDirection, userDepthMm);
                    }

                    if (latestFace is not null && latestFace.IsTracked)
                    {
                        avatarIkTarget = _saccade.GetFocusPoint3D(latestFace);
                    }

                    // ── Phase 3: World-Space Gaze Vector → Avatar ─────────
                    // High-fidelity path : IsTracked = true  → precise eye-socket position.
                    // Bounding-box fallback: IsTracked = false → HeadTranslation centre.
                    if (latestFace is not null && _avatarEyeProvider is not null && _avatarScreenWidthPx > 0)
                    {
                        Vector3 userEyeM;
                        bool isPrecision;
                        if (latestFace.IsTracked && avatarIkTarget.HasValue)
                        {
                            // avatarIkTarget is already in Camera Space metres (from GetFocusPoint3D).
                            userEyeM = avatarIkTarget.Value;
                            isPrecision = true;
                        }
                        else
                        {
                            // HeadTranslation is in Camera Space mm → convert to metres.
                            // Reset EMA so stale high-fidelity values don't bleed into fallback.
                            _gazeEngine.Reset();
                            userEyeM = latestFace.HeadTranslation / 1000f;
                            isPrecision = false;
                        }

                        float distanceM = userEyeM.Z; // Camera Space +Z = toward user

                        var (leftPx, rightPx) = _avatarEyeProvider();

                        // Mirror the saccade: if fixating user's LEFT eye, use Avatar LEFT eye socket.
                        Vector2 avatarEyePx = _saccade.CurrentTarget == EyeSocketTarget.Left
                            ? leftPx : rightPx;

                        var cal = _calibration; // thread-safe snapshot
                        Vector3 avatarEyeWorldMm = GazeVectorEngine.AvatarEyeScreenToWorldMm(
                            avatarEyePx,
                            new Vector2(_avatarScreenWidthPx, _avatarScreenHeightPx),
                            new Vector2(ScreenWidthMm, ScreenHeightMm),
                            cal.KinectOffsetFromScreenCentreMm);

                        // userEyeM is in Camera Space metres — GazeVectorEngine converts to mm internally.
                        var (pitch, yaw) = _gazeEngine.Compute(userEyeM, avatarEyeWorldMm);

                        GazeVectorReady?.Invoke(pitch, yaw, distanceM, isPrecision);
                    }

                    person = new TrackedPerson
                    {
                        TrackingId = hcep?.PersonId ?? latestSkel?.TrackingId ?? 0,
                        State = hcep is not null ? TrackingState.Tracked : TrackingState.PositionOnly,
                        LatestHcep = hcep,
                        IdentityName = recognition?.IdentityName,
                        FaceEmbedding = recognition?.Embedding,
                        IdentityConfidence = recognition?.Similarity ?? 0f,
                        HeadPosition = headPos,
                        LeftEyePosition = leftEye,
                        RightEyePosition = rightEye,
                        LastSeen = hcep?.Timestamp ?? DateTimeOffset.UtcNow,
                        JointPositions = latestSkel?.Joints,
                        JointStates = latestSkel?.JointStates,
                        DistanceM = latestSkel?.Position.Z ?? hcep?.GazeOrigin.Z ?? 0,
                        Face = latestFace,
                        CalibratedGazeDirection = calibratedGaze,
                        AvatarIkTarget = avatarIkTarget,
                        Torso = _torsoAnalyzer.Analyze(latestSkel),
                    };

                    // Knowledge Store integration (M1.2)
                    try { _personKnowledge.RecordSighting(person); }
                    catch (Exception ex)
                    {
                        if (frameNumber <= 3)
                            _logger.LogWarning(ex, "PersonKnowledge.RecordSighting failed (frame {Frame})", frameNumber);
                    }

                    // Agentic Tool State update (M1.3)
                    if (hcep is not null)
                        _toolExecutor.UpdateState(hcep, person);
                }

                var snapshot = new SceneSnapshot
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    FrameNumber = frameNumber,
                    Persons = person is not null
                        ? ImmutableArray.Create(person)
                        : ImmutableArray<TrackedPerson>.Empty,
                    PrimaryPersonIndex = person is not null ? 0 : -1,
                    LatestSpeech = _latestSpeech,
                    ActiveStreams = SensorStreamType.All,
                    PipelineLatency = hcep is not null
                        ? DateTimeOffset.UtcNow - hcep.Timestamp
                        : TimeSpan.Zero,
                };

                _latestSnapshot = snapshot;
                _telemetry.RecordGauge("pipeline.fps", _hcepFpsCounter.Fps);
                if (hcep is not null)
                    _telemetry.RecordTiming("pipeline.latency_ms", snapshot.PipelineLatency.TotalMilliseconds);

                // ── Auto-fallback: full-body → seated if no detection ──
                if (person is not null)
                {
                    _lastPersonSeenAt = DateTimeOffset.UtcNow;
                    // Person detected — stay in current mode.
                }
                else if (!_sensor.SeatedMode
                         && !_autoFellBackToSeated
                         && _lastPersonSeenAt != DateTimeOffset.MinValue
                         && (DateTimeOffset.UtcNow - _lastPersonSeenAt).TotalSeconds > AutoFallbackSeconds)
                {
                    _sensor.SeatedMode = true;
                    _autoFellBackToSeated = true;
                    _logger.LogWarning(
                        "No person detected for {Sec}s in full-body mode — auto-switching to SEATED mode",
                        AutoFallbackSeconds);
                    SeatedModeChanged?.Invoke(true);
                }
                else if (!_sensor.SeatedMode
                         && !_autoFellBackToSeated
                         && _lastPersonSeenAt == DateTimeOffset.MinValue
                         && frameNumber >= 50)
                {
                    _sensor.SeatedMode = true;
                    _autoFellBackToSeated = true;
                    _logger.LogWarning(
                        "No person detected after {Frames} snapshots — auto-switching to SEATED mode",
                        frameNumber);
                    SeatedModeChanged?.Invoke(true);
                }

                SnapshotReady?.Invoke(snapshot);

                if (frameNumber <= 5 || frameNumber % 300 == 0)
                    _logger.LogInformation(
                        "Snapshot #{Frame}: persons={Persons} hcepMode={Mode} hcepFps={HcepFps:F1} hasFace={HasFace} hasSkel={HasSkel}",
                        frameNumber, snapshot.Persons.Length,
                        hcep?.Mode.ToString() ?? "None",
                        _hcepFpsCounter.Fps,
                        latestFace is not null,
                        latestSkel is not null);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot loop error");
        }
    }

    // ── Eye Location Helpers ───────────────────────────────────

    /// <summary>
    /// Average adult inter-ocular half-distance in meters (~32mm from midline to each eye).
    /// Total inter-pupillary distance ~63mm.
    /// </summary>
    private const float EyeHalfDistanceM = 0.032f;

    /// <summary>
    /// Computes 3D camera-space positions of both eyes from head position
    /// and current face tracking data. Applies yaw rotation so eye positions
    /// track correctly when the head turns.
    /// </summary>
    private static (Vector3 Left, Vector3 Right) ComputeEyePositions(
        Vector3 headPos, FaceFrame? face)
    {
        if (headPos == default) return (default, default);

        // Yaw angle from face tracking (degrees → radians)
        float yawRad = (face?.HeadRotation.Y ?? 0f) * MathF.PI / 180f;
        float cosY = MathF.Cos(yawRad);
        float sinY = MathF.Sin(yawRad);

        // Lateral offset rotated by yaw (X-Z plane)
        // Left eye: -X in head space
        var leftOffset = new Vector3(
            -EyeHalfDistanceM * cosY,
            0,
            EyeHalfDistanceM * sinY);

        // Right eye: +X in head space
        var rightOffset = new Vector3(
            EyeHalfDistanceM * cosY,
            0,
            -EyeHalfDistanceM * sinY);

        return (headPos + leftOffset, headPos + rightOffset);
    }
}
