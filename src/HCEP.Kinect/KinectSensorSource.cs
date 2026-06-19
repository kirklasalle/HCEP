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
//
// Kinect v1 sensor source — NATIVE COM interop via Kinect10.dll.
//
// This completely bypasses the managed Microsoft.Kinect.dll which cannot
// run on .NET 9 (TypeLoadException for System.Diagnostics.Eventing.EventDescriptor).
//
// Instead we P/Invoke the native Kinect10.dll directly:
//   NuiGetSensorCount → NuiCreateSensorByIndex → INuiSensor COM interface
//   INuiSensor.NuiInitialize → NuiImageStreamOpen → poll NuiImageStreamGetNextFrame
//   INuiFrameTexture.LockRect → memcpy pixel data → fire events
//
// This gives us REAL video from the REAL Kinect hardware.
// ──────────────────────────────────────────────────────────────

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Kinect.Native;
using Microsoft.Extensions.Logging;

namespace HCEP.Kinect;

/// <summary>
/// Kinect v1 sensor source using direct native COM interop to Kinect10.dll.
/// No managed Microsoft.Kinect.dll dependency — works on .NET 9.
/// </summary>
public sealed partial class KinectSensorSource : ISensorSource
{
    private readonly ILogger<KinectSensorSource> _logger;
    private volatile SensorState _state = SensorState.Disconnected;

    // Native COM sensor
    private INuiSensor? _sensor;

    // Stream handles
    private IntPtr _colorStreamHandle;
    private IntPtr _depthStreamHandle;

    // Polling thread
    private CancellationTokenSource? _pollCts;
    private Thread? _pollThread;

    // Frame counters
    private int _colorFrameNumber;
    private int _depthFrameNumber;
    private int _skeletonFrameNumber;
    private int _skelPollCount;

    // Depth format tracking — NuiImageStreamOpen determines the raw format.
    // DepthAndPlayerIndex (type 0) produces D13P3; plain Depth (type 4) produces D16.
    private bool _depthIsD13P3;      // true when stream is DepthAndPlayerIndex format

    public KinectSensorSource(ILogger<KinectSensorSource> logger)
    {
        _logger = logger;
    }

    // ── ISensorSource ──────────────────────────────────────────

    public SensorState State => _state;

    public event Action<SkeletonFrame>? SkeletonFrameReady;
    public event Action<FaceFrame>? FaceFrameReady;
    public event Action<ColorFrame>? ColorFrameReady;
    public event Action<DepthFrame>? DepthFrameReady;
    public event Action<ColorFrame>? InfraredFrameReady;
#pragma warning disable CS0067 // AudioFrameReady is part of ISensorSource but raised by Kinect v1 via a different path
    public event Action<AudioFrame>? AudioFrameReady;
#pragma warning restore CS0067
    public event Action<SensorState>? StateChanged;

    public int ElevationAngle
    {
        get
        {
            if (_sensor is null) return 0;
            try
            {
                int hr = _sensor.NuiCameraElevationGetAngle(out int angle);
                return hr >= 0 ? angle : 0;
            }
            catch { return 0; }
        }
        set
        {
            if (_sensor is null) return;
            try { _sensor.NuiCameraElevationSetAngle(Math.Clamp(value, -27, 27)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to set elevation angle"); }
        }
    }

    private bool _seatedMode;
    public bool SeatedMode
    {
        get => _seatedMode;
        set
        {
            if (_seatedMode == value) return;
            _seatedMode = value;
            if (_sensor is null) return;
            try
            {
                uint flags = value
                    ? NuiConstants.NUI_SKELETON_TRACKING_FLAG_ENABLE_SEATED_SUPPORT
                    : 0u;
                int hr = _sensor.NuiSkeletonTrackingEnable(IntPtr.Zero, flags);
                if (hr >= 0)
                    _logger.LogInformation("Skeleton tracking mode changed: {Mode} (flags=0x{Flags:X})",
                        value ? "SEATED" : "FULL BODY", flags);
                else
                    _logger.LogWarning("Failed to change skeleton tracking mode (hr=0x{HR:X8})", hr);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to set skeleton tracking mode"); }
        }
    }

    // ── Lifecycle ──────────────────────────────────────────────

    public Task InitializeAsync(SensorStreamType streams, CancellationToken ct = default)
    {
        SetState(SensorState.Initializing);

        try
        {
            // Check how many Kinect sensors are connected
            int hr = KinectNative.NuiGetSensorCount(out int sensorCount);
            if (hr < 0 || sensorCount == 0)
            {
                _logger.LogWarning("No Kinect sensors detected (count={Count}, hr=0x{HR:X8})",
                    sensorCount, hr);
                SetState(SensorState.Disconnected);
                return Task.CompletedTask;
            }

            _logger.LogInformation("Found {Count} Kinect sensor(s)", sensorCount);

            // Create sensor by index 0
            hr = KinectNative.NuiCreateSensorByIndex(0, out _sensor);
            if (hr < 0 || _sensor is null)
            {
                _logger.LogError("Failed to create Kinect sensor (hr=0x{HR:X8})", hr);
                SetState(SensorState.Error);
                return Task.CompletedTask;
            }

            // Check sensor status
            int status = _sensor.NuiStatus();
            _logger.LogInformation("Kinect sensor status: 0x{Status:X8}", status);

            // Build initialization flags.
            // Use DEPTH_AND_PLAYER_INDEX — this is what the Kinect v1 SDK
            // and FaceTrackLib expect (D13P3 format = 13-bit depth + 3-bit player index).
            // Near Mode flag is only applied at NuiImageStreamOpen level, not here.
            uint initFlags = 0;
            if (streams.HasFlag(SensorStreamType.Color))
                initFlags |= NuiConstants.NUI_INITIALIZE_FLAG_USES_COLOR;
            if (streams.HasFlag(SensorStreamType.Depth))
                initFlags |= NuiConstants.NUI_INITIALIZE_FLAG_USES_DEPTH_AND_PLAYER_INDEX;
            if (streams.HasFlag(SensorStreamType.Skeleton))
                initFlags |= NuiConstants.NUI_INITIALIZE_FLAG_USES_SKELETON;

            // Initialize the sensor
            hr = _sensor.NuiInitialize(initFlags);
            if (hr < 0)
            {
                _logger.LogError("NuiInitialize failed (hr=0x{HR:X8})", hr);
                SetState(SensorState.Error);
                return Task.CompletedTask;
            }

            _logger.LogInformation("Kinect NuiInitialize succeeded (flags=0x{Flags:X})", initFlags);

            // Open color stream: 640×480 RGB 30fps
            if (streams.HasFlag(SensorStreamType.Color))
            {
                hr = _sensor.NuiImageStreamOpen(
                    NUI_IMAGE_TYPE.Color,
                    NUI_IMAGE_RESOLUTION.Res640x480,
                    0,    // dwImageFrameFlags
                    2,    // dwFrameLimit (double-buffer)
                    IntPtr.Zero, // no event handle — we poll
                    out _colorStreamHandle);

                if (hr < 0)
                {
                    _logger.LogError("Failed to open color stream (hr=0x{HR:X8})", hr);
                    SetState(SensorState.Error);
                    return Task.CompletedTask;
                }

                _logger.LogInformation("Color stream opened: RGB 640×480 @ 30fps (handle=0x{H:X})",
                    _colorStreamHandle);
            }

            // Open depth stream: 640×480.
            // Fallback chain to handle different Kinect v1 hardware variants:
            //   1. DepthAndPlayerIndex (matches NuiInitialize flag) — D13P3 format.
            //      This is the standard format FaceTrackLib expects.
            //   2. DepthAndPlayerIndex + Near Mode (Kinect for Windows v1 only).
            //   3. Plain Depth + Near Mode (some firmware).
            //   4. Plain Depth (last resort).
            // The previous code used Depth+NearMode but initialized with
            // DEPTH_AND_PLAYER_INDEX flag — a mismatch that caused E_INVALIDARG.
            if (streams.HasFlag(SensorStreamType.Depth))
            {
                _depthIsD13P3 = false;
                bool depthOpened = false;

                // Attempt 1: DepthAndPlayerIndex, no Near Mode (safest, universal)
                hr = _sensor.NuiImageStreamOpen(
                    NUI_IMAGE_TYPE.DepthAndPlayerIndex,
                    NUI_IMAGE_RESOLUTION.Res640x480,
                    0,    // no flags
                    2,
                    IntPtr.Zero,
                    out _depthStreamHandle);
                if (hr >= 0)
                {
                    _depthIsD13P3 = true;
                    depthOpened = true;
                    _logger.LogInformation(
                        "Depth stream opened: DepthAndPlayerIndex 640×480 (D13P3, handle=0x{H:X})",
                        _depthStreamHandle);
                }
                else
                {
                    _logger.LogWarning(
                        "DepthAndPlayerIndex failed (hr=0x{HR:X8}), trying plain Depth...", hr);

                    // Attempt 2: Plain Depth, no flags
                    hr = _sensor.NuiImageStreamOpen(
                        NUI_IMAGE_TYPE.Depth,
                        NUI_IMAGE_RESOLUTION.Res640x480,
                        0,    // no flags
                        2,
                        IntPtr.Zero,
                        out _depthStreamHandle);
                    if (hr >= 0)
                    {
                        _depthIsD13P3 = false;  // D16 format
                        depthOpened = true;
                        _logger.LogInformation(
                            "Depth stream opened: plain Depth 640×480 (D16, handle=0x{H:X})",
                            _depthStreamHandle);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "All depth stream open attempts failed (last hr=0x{HR:X8}) — " +
                            "face mesh will be unavailable", hr);
                    }
                }

                if (!depthOpened)
                    _logger.LogWarning("No depth stream available — face tracking requires depth data!");
            }

            // Enable skeleton tracking — default to FULL BODY mode
            if (streams.HasFlag(SensorStreamType.Skeleton))
            {
                // Start with full-body tracking (no seated flag).
                // Can be switched at runtime via SeatedMode property.
                // Always include ENABLE_IN_NEAR_RANGE so skeleton is tracked down to ~40 cm.
                uint skelFlags = _seatedMode
                    ? NuiConstants.NUI_SKELETON_TRACKING_FLAG_ENABLE_SEATED_SUPPORT
                    : 0u;
                skelFlags |= NuiConstants.NUI_SKELETON_TRACKING_FLAG_ENABLE_IN_NEAR_RANGE;
                hr = _sensor.NuiSkeletonTrackingEnable(IntPtr.Zero, skelFlags);
                if (hr < 0)
                    _logger.LogWarning("Failed to enable skeleton tracking (hr=0x{HR:X8})", hr);
                else
                    _logger.LogInformation("Skeleton tracking enabled (flags=0x{Flags:X} — {Mode})",
                        skelFlags, _seatedMode ? "SEATED" : "FULL BODY");
            }

            // Get device ID
            try
            {
                string? connId = _sensor.NuiDeviceConnectionId();
                _logger.LogInformation("Kinect device: {Id}", connId ?? "(unknown)");
            }
            catch { /* non-critical */ }

            // Initialize face tracking via FaceTrackLib.dll
            InitializeFaceTracking();

            SetState(SensorState.Connected);
            _logger.LogInformation("Kinect sensor initialized via native COM — READY FOR REAL VIDEO");
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogWarning(ex, "Kinect10.dll not found — Kinect SDK not installed");
            SetState(SensorState.Disconnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Kinect sensor");
            SetState(SensorState.Error);
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_sensor is null || _state != SensorState.Connected)
        {
            _logger.LogWarning("Cannot start — no connected Kinect sensor");
            return Task.CompletedTask;
        }

        try
        {
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _pollThread = new Thread(PollFrames)
            {
                IsBackground = true,
                Name = "KinectNativePoller",
                Priority = ThreadPriority.AboveNormal,
            };
            _pollThread.Start();

            _logger.LogInformation("Kinect native polling started — REAL VIDEO streaming");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Kinect polling");
            SetState(SensorState.Error);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        try
        {
            _pollCts?.Cancel();
            _pollThread?.Join(timeout: TimeSpan.FromSeconds(3));
            _pollCts?.Dispose();
            _pollCts = null;
            _pollThread = null;

            _sensor?.NuiShutdown();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping Kinect sensor");
        }

        SetState(SensorState.Disconnected);
        _logger.LogInformation("Kinect sensor stopped");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_sensor is not null)
        {
            try { Marshal.ReleaseComObject(_sensor); } catch { }
            _sensor = null;
        }

        // Release face tracking COM objects
        DisposeFaceTracking();
    }

    // ── Native Polling Loop ────────────────────────────────────

    /// <summary>
    /// Background thread that polls the Kinect native API for frames.
    /// Uses INuiSensor.NuiImageStreamGetNextFrame with a timeout.
    /// </summary>
    private void PollFrames()
    {
        _logger.LogInformation("Kinect native polling thread STARTED");

        while (_pollCts is { IsCancellationRequested: false })
        {
            try
            {
                // Poll color (primary — this is the REAL VIDEO)
                if (_colorStreamHandle != IntPtr.Zero)
                    PollColorFrame();

                // Poll depth
                if (_depthStreamHandle != IntPtr.Zero)
                    PollDepthFrame();

                // Poll skeleton
                PollSkeletonFrame();
            }
            catch (Exception ex) when (ex is not ThreadInterruptedException)
            {
                _logger.LogDebug(ex, "Polling loop error");
                Thread.Sleep(10);
            }
        }

        _logger.LogInformation("Kinect native polling thread EXITING");
    }

    private void SetState(SensorState newState)
    {
        if (_state == newState) return;
        _state = newState;
        StateChanged?.Invoke(newState);
        _logger.LogInformation("Sensor state → {State}", newState);
    }
}
