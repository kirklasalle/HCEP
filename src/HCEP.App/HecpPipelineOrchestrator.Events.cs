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
using System.Threading;
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
        _vision.LatestColor = color;
        ColorFrameReady?.Invoke(color);
    }

    private void OnDepthFrameReady(DepthFrame depth) => DepthFrameReady?.Invoke(depth);

    private void OnInfraredFrameReady(ColorFrame ir) => InfraredFrameReady?.Invoke(ir);

    private void OnSkeletonFrameReady(SkeletonFrame skel)
    {
        _latestSkeleton = skel;
        SkeletonFrameReady?.Invoke(skel);
        var count = Interlocked.Increment(ref _skelFrameCount);
        if (count <= 5 || count % 300 == 0)
            _logger.LogInformation(
                "SkeletonFrame #{Count}: id={Id} state={State} joints={Joints} pos=({X:F2},{Y:F2},{Z:F2})",
                count, skel.TrackingId, skel.State, skel.Joints?.Count ?? 0,
                skel.Position.X, skel.Position.Y, skel.Position.Z);
    }
}
