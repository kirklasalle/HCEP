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
using System.Linq;
using System.Threading;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

public sealed partial class HCEPPipelineOrchestrator
{
    private void WireSensorEvents(ISensorSource sensor)
    {
        sensor.FaceFrameReady += OnFaceFrameReady;
        sensor.AudioFrameReady += OnAudioFrameReady;
        sensor.ColorFrameReady += OnColorFrameReady;
        sensor.DepthFrameReady += OnDepthFrameReady;
        sensor.InfraredFrameReady += OnInfraredFrameReady;
        sensor.SkeletonFrameReady += OnSkeletonFrameReady;
    }

    private void UnwireSensorEvents(ISensorSource sensor)
    {
        sensor.FaceFrameReady -= OnFaceFrameReady;
        sensor.AudioFrameReady -= OnAudioFrameReady;
        sensor.ColorFrameReady -= OnColorFrameReady;
        sensor.DepthFrameReady -= OnDepthFrameReady;
        sensor.InfraredFrameReady -= OnInfraredFrameReady;
        sensor.SkeletonFrameReady -= OnSkeletonFrameReady;
    }

    private void OnFaceFrameReady(FaceFrame face)
    {
        _latestFace = face;
        bool written = _vision.FaceInput.TryWrite(face);
        var count = Interlocked.Increment(ref _faceFrameCount);
        if (AppLog.ShouldTraceFrame(count))
            _logger.LogTrace(
                "Pipeline face frame received — count={Count} trackingId={TrackingId} tracked={Tracked} ageMs={AgeMs:F1} writtenToVision={Written} yaw={Yaw:F1} pitch={Pitch:F1} meshVertices={MeshVertices}",
                count,
                face.TrackingId,
                face.IsTracked,
                (DateTimeOffset.UtcNow - face.Timestamp).TotalMilliseconds,
                written,
                face.HeadRotation.Y,
                face.HeadRotation.X,
                face.FaceMeshVertices2D?.Length ?? 0);
        if (!written)
            _logger.LogWarning("Face frame #{Count} dropped — FaceInput channel is full (back-pressure)", count);
        else if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "FaceFrame #{Count}: written={Written} tracked={IsTracked} yaw={Yaw:F1} pitch={Pitch:F1}",
                count, written, face.IsTracked, face.HeadRotation.Y, face.HeadRotation.X);
    }

    private void OnAudioFrameReady(AudioFrame audio)
    {
        if (!_audio.AudioInput.TryWrite(audio))
            _logger.LogWarning("Audio frame dropped — AudioInput channel is full (back-pressure)");
    }

    private void OnColorFrameReady(ColorFrame color)
    {
        var count = Interlocked.Increment(ref _colorFrameCount);
        _vision.LatestColor = color;
        if (AppLog.ShouldTraceFrame(count))
            _logger.LogTrace(
                "Pipeline color frame publishing — count={Count} frame={Frame} ageMs={AgeMs:F1} subscribers={Subscribers} size={Width}x{Height} bytes={Bytes}",
                count,
                color.FrameNumber,
                (DateTimeOffset.UtcNow - color.Timestamp).TotalMilliseconds,
                ColorFrameReady?.GetInvocationList().Length ?? 0,
                color.Width,
                color.Height,
                color.PixelData.Length);
        ColorFrameReady?.Invoke(color);
        if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "ColorFrame #{Count}: frame={Frame} size={Width}x{Height} subscribers={Subscribers}",
                count,
                color.FrameNumber,
                color.Width,
                color.Height,
                ColorFrameReady?.GetInvocationList().Length ?? 0);
    }

    private void OnDepthFrameReady(DepthFrame depth)
    {
        var count = Interlocked.Increment(ref _depthFrameCount);
        if (AppLog.ShouldTraceFrame(count))
            _logger.LogTrace(
                "Pipeline depth frame publishing — count={Count} frame={Frame} ageMs={AgeMs:F1} subscribers={Subscribers} size={Width}x{Height} samples={Samples}",
                count,
                depth.FrameNumber,
                (DateTimeOffset.UtcNow - depth.Timestamp).TotalMilliseconds,
                DepthFrameReady?.GetInvocationList().Length ?? 0,
                depth.Width,
                depth.Height,
                depth.DepthData.Length);
        DepthFrameReady?.Invoke(depth);
        if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "DepthFrame #{Count}: frame={Frame} size={Width}x{Height} subscribers={Subscribers}",
                count,
                depth.FrameNumber,
                depth.Width,
                depth.Height,
                DepthFrameReady?.GetInvocationList().Length ?? 0);
    }

    private void OnInfraredFrameReady(ColorFrame ir)
    {
        var count = Interlocked.Increment(ref _infraredFrameCount);
        if (AppLog.ShouldTraceFrame(count))
            _logger.LogTrace(
                "Pipeline infrared frame publishing — count={Count} frame={Frame} ageMs={AgeMs:F1} subscribers={Subscribers} size={Width}x{Height} bytes={Bytes}",
                count,
                ir.FrameNumber,
                (DateTimeOffset.UtcNow - ir.Timestamp).TotalMilliseconds,
                InfraredFrameReady?.GetInvocationList().Length ?? 0,
                ir.Width,
                ir.Height,
                ir.PixelData.Length);
        InfraredFrameReady?.Invoke(ir);
        if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "InfraredFrame #{Count}: frame={Frame} size={Width}x{Height} subscribers={Subscribers}",
                count,
                ir.FrameNumber,
                ir.Width,
                ir.Height,
                InfraredFrameReady?.GetInvocationList().Length ?? 0);
    }

    private void OnSkeletonFrameReady(SkeletonFrame skel)
    {
        _latestSkeleton = skel;
        SkeletonFrameReady?.Invoke(skel);
        var count = Interlocked.Increment(ref _skelFrameCount);
        if (AppLog.ShouldTraceFrame(count))
            _logger.LogTrace(
                "Pipeline skeleton frame publishing — count={Count} id={Id} state={State} ageMs={AgeMs:F1} subscribers={Subscribers} joints={Joints} trackedJoints={TrackedJoints} inferredJoints={InferredJoints} pos=({X:F2},{Y:F2},{Z:F2})",
                count,
                skel.TrackingId,
                skel.State,
                (DateTimeOffset.UtcNow - skel.Timestamp).TotalMilliseconds,
                SkeletonFrameReady?.GetInvocationList().Length ?? 0,
                skel.Joints?.Count ?? 0,
                skel.JointStates.Values.Count(state => state == TrackingState.Tracked),
                skel.JointStates.Values.Count(state => state == TrackingState.Inferred),
                skel.Position.X,
                skel.Position.Y,
                skel.Position.Z);
        if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "SkeletonFrame #{Count}: id={Id} state={State} joints={Joints} pos=({X:F2},{Y:F2},{Z:F2})",
                count, skel.TrackingId, skel.State, skel.Joints?.Count ?? 0,
                skel.Position.X, skel.Position.Y, skel.Position.Z);
    }
}
