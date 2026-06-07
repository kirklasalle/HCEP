// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
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
    /// Polls one color frame from the Kinect camera via native COM.
    /// NuiImageStreamGetNextFrame → INuiFrameTexture.LockRect → copy BGRX pixels.
    /// </summary>
    private void PollColorFrame()
    {
        int hr = _sensor!.NuiImageStreamGetNextFrame(_colorStreamHandle, 50, out NUI_IMAGE_FRAME frame);
        if (hr < 0) return; // No frame ready

        INuiFrameTexture? texture = null;
        try
        {
            if (frame.pFrameTexture == IntPtr.Zero) return;

            texture = (INuiFrameTexture)Marshal.GetObjectForIUnknown(frame.pFrameTexture);

            hr = texture.LockRect(0, out NUI_LOCKED_RECT lockedRect, IntPtr.Zero, 0);
            if (hr < 0 || lockedRect.pBits == IntPtr.Zero) return;

            try
            {
                // Kinect v1 color at 640×480 = BGRX 32bpp
                const int width = 640;
                const int height = 480;
                const int bpp = 4;
                int byteCount = width * height * bpp;

                var pixels = new byte[byteCount];

                if (lockedRect.Pitch == width * bpp)
                {
                    Marshal.Copy(lockedRect.pBits, pixels, 0, byteCount);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr src = lockedRect.pBits + y * lockedRect.Pitch;
                        Marshal.Copy(src, pixels, y * width * bpp, width * bpp);
                    }
                }

                // Kinect v1 outputs BGRX (alpha byte = 0x00).
                // Save raw BGRX for face tracking before modifying alpha
                if (_faceTrackingInitialized)
                    _lastColorPixels = (byte[])pixels.Clone();

                // WPF Bgra32 needs alpha = 0xFF for opaque pixels.
                for (int i = 3; i < byteCount; i += 4)
                    pixels[i] = 0xFF;

                int frameNum = Interlocked.Increment(ref _colorFrameNumber);

                ColorFrameReady?.Invoke(new ColorFrame
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    PixelData = pixels,
                    Width = width,
                    Height = height,
                    BytesPerPixel = bpp,
                    FrameNumber = frameNum,
                });

                if (frameNum <= 3)
                    _logger.LogInformation(
                        "REAL color frame #{N}: {W}×{H}, pitch={P}, {Len} bytes",
                        frameNum, width, height, lockedRect.Pitch, byteCount);
            }
            finally
            {
                texture.UnlockRect(0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing native color frame");
        }
        finally
        {
            if (texture is not null)
                Marshal.ReleaseComObject(texture);

            _sensor!.NuiImageStreamReleaseFrame(_colorStreamHandle, ref frame);
        }
    }
}
