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
using System.Runtime.InteropServices;
using System.Threading;
using HCEP.Core.Models;
using HCEP.Kinect.Native;
using Microsoft.Extensions.Logging;

namespace HCEP.Kinect;

public sealed partial class KinectSensorSource
{
    /// <summary>
    /// Polls one depth frame via native COM.
    /// Raw data is packed: (depth_mm &lt;&lt; 3) | playerIndex.
    /// Also generates an IR-like grayscale from depth intensity.
    /// </summary>
    private void PollDepthFrame()
    {
        int hr = _sensor!.NuiImageStreamGetNextFrame(_depthStreamHandle, 0, out NUI_IMAGE_FRAME frame);
        if (hr < 0) return;

        INuiFrameTexture? texture = null;
        try
        {
            if (frame.pFrameTexture == IntPtr.Zero) return;

            texture = (INuiFrameTexture)Marshal.GetObjectForIUnknown(frame.pFrameTexture);

            hr = texture.LockRect(0, out NUI_LOCKED_RECT lockedRect, IntPtr.Zero, 0);
            if (hr < 0 || lockedRect.pBits == IntPtr.Zero) return;

            try
            {
                const int width = 640;
                const int height = 480;
                int pixelCount = width * height;

                // Depth data is 16-bit per pixel (USHORT)
                var rawDepth = new short[pixelCount];
                Marshal.Copy(lockedRect.pBits, rawDepth, 0, pixelCount);

                // Save raw depth (D13P3 format) for face tracking
                if (_faceTrackingInitialized)
                    _lastDepthRaw = rawDepth;

                int frameNum = Interlocked.Increment(ref _depthFrameNumber);

                // Extract real depth in mm
                const int shift = NuiConstants.NUI_IMAGE_PLAYER_INDEX_SHIFT;
                var depthMm = new short[pixelCount];
                for (int i = 0; i < pixelCount; i++)
                    depthMm[i] = (short)(rawDepth[i] >> shift);

                const int minDepth = 800;
                const int maxDepth = 4000;

                DepthFrameReady?.Invoke(new DepthFrame
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    DepthData = depthMm,
                    Width = width,
                    Height = height,
                    MinDepthMm = minDepth,
                    MaxDepthMm = maxDepth,
                    FrameNumber = frameNum,
                });

                // Generate IR-like grayscale
                var irPixels = new byte[pixelCount * 4];
                float range = maxDepth - minDepth;
                for (int i = 0; i < pixelCount; i++)
                {
                    short d = depthMm[i];
                    byte intensity;
                    if (d <= 0 || d < minDepth)
                        intensity = 10;
                    else if (d > maxDepth)
                        intensity = 5;
                    else
                        intensity = (byte)(255 - (int)((d - minDepth) / range * 230));

                    int j = i * 4;
                    irPixels[j] = intensity;
                    irPixels[j + 1] = intensity;
                    irPixels[j + 2] = intensity;
                    irPixels[j + 3] = 255;
                }

                InfraredFrameReady?.Invoke(new ColorFrame
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    PixelData = irPixels,
                    Width = width,
                    Height = height,
                    BytesPerPixel = 4,
                    FrameNumber = frameNum,
                });

                if (frameNum <= 3)
                    _logger.LogInformation(
                        "REAL depth frame #{N}: {W}×{H}",
                        frameNum, width, height);
            }
            finally
            {
                texture.UnlockRect(0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing native depth frame");
        }
        finally
        {
            if (texture is not null)
                Marshal.ReleaseComObject(texture);

            _sensor!.NuiImageStreamReleaseFrame(_depthStreamHandle, ref frame);
        }
    }
}
