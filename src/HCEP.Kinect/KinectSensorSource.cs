// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
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

using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
public sealed class KinectSensorSource : ISensorSource
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

    // Face tracking (FaceTrackLib.dll)
    private IntPtr _faceTrackerPtr;
    private IntPtr _faceResultPtr;
    // Raw COM IntPtrs — NOT wrapped in .NET RCW because FaceTrackLib COM objects
    // have broken QueryInterface (E_NOINTERFACE). Called via FtImageRaw vtable helper.
    private IntPtr _ftVideoImagePtr;
    private IntPtr _ftDepthImagePtr;
    private bool _faceTrackingInitialized;
    private bool _faceTrackingStarted;
    private byte[]? _lastColorPixels;
    private short[]? _lastDepthRaw;

    // Depth format tracking — NuiImageStreamOpen determines the raw format.
    // DepthAndPlayerIndex (type 0) produces D13P3; plain Depth (type 4) produces D16.
    private bool _depthIsD13P3;      // true when stream is DepthAndPlayerIndex format

    // Face model mesh (from FaceTrackLib SDK — like FaceTrackingBasics-WPF sample)
    private IFTModel? _faceModel;
    private (int First, int Second, int Third)[]? _cachedTriangles;
    private uint _meshVertexCount;
    private FT_CAMERA_CONFIG _videoConfig;
    private uint _suModelCount;      // IFTModel.GetSUCount() — fixed at model load
    private uint _lastMeshHr;        // last GetProjectedShape HRESULT (0 = success)

    // ── Mesh diagnostic trace counters ──
    private int _meshAttemptCount;    // total GetProjectedShape attempts
    private int _meshSuccessCount;    // successful GetProjectedShape calls
    private int _meshFailCount;       // failed GetProjectedShape calls
    private bool _meshFirstDiagLogged; // one-shot startup diagnostic
    private bool _meshFirstOkLogged;   // one-shot first success
    private bool _meshFirstFailLogged; // one-shot first failure
    private int _meshGuardSkipCount;   // times mesh guard prevented entry

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

    // ── Face Tracking Initialization ───────────────────────────

    /// <summary>
    /// Initializes the Kinect Face Tracking SDK via native COM interop
    /// to FaceTrackLib.dll. Falls back to skeleton-approximate faces
    /// if the face tracker cannot be loaded.
    /// </summary>
    private void InitializeFaceTracking()
    {
        try
        {
            if (!FaceTrackNative.TryLoad())
            {
                _logger.LogWarning("FaceTrackLib.dll not available — using skeleton-approximate face tracking");
                return;
            }

            _faceTrackerPtr = FaceTrackNative.CreateFaceTrackerRaw();
            if (_faceTrackerPtr == IntPtr.Zero)
            {
                _logger.LogWarning("FTCreateFaceTracker returned null");
                return;
            }

            _ftVideoImagePtr = FaceTrackNative.CreateImageRaw();
            _ftDepthImagePtr = FaceTrackNative.CreateImageRaw();
            if (_ftVideoImagePtr == IntPtr.Zero || _ftDepthImagePtr == IntPtr.Zero)
            {
                _logger.LogWarning("FTCreateImage returned null");
                DisposeFaceTracking();
                return;
            }

            // Pre-allocate IFTImage internal buffers — matches C++ reference pattern
            // (KinectSensor.cpp: m_VideoBuffer->Allocate / m_DepthBuffer->Allocate).
            // FaceTrackLib owns the buffer; each frame we Marshal.Copy data into it.
            int hr = FtImageRaw.Allocate(_ftVideoImagePtr, 640, 480, FTIMAGEFORMAT.UINT8_B8G8R8X8);
            if (hr < 0)
            {
                _logger.LogWarning("IFTImage.Allocate(video) failed hr=0x{HR:X8}", unchecked((uint)hr));
                DisposeFaceTracking();
                return;
            }

            var depthFmt = _depthIsD13P3 ? FTIMAGEFORMAT.UINT16_D13P3 : FTIMAGEFORMAT.UINT16_D16;
            hr = FtImageRaw.Allocate(_ftDepthImagePtr, 640, 480, depthFmt);
            if (hr < 0)
            {
                _logger.LogWarning("IFTImage.Allocate(depth) failed hr=0x{HR:X8} format={F}", unchecked((uint)hr), depthFmt);
                DisposeFaceTracking();
                return;
            }

            _logger.LogInformation(
                "IFTImage buffers allocated: video=640×480 BGRX, depth=640×480 {Fmt}",
                depthFmt);

            // Camera configs: Kinect v1 color 640×480, focal ~531.15 pixels
            // Depth 640×480: SDK nominal depth focal is 285.63 at 320×240.
            // For 640×480 we multiply by 2, matching C++ KinectSensor.cpp:
            //   focalLength = NUI_CAMERA_DEPTH_NOMINAL_FOCAL_LENGTH_IN_PIXELS * 2.f
            _videoConfig = new FT_CAMERA_CONFIG { Width = 640, Height = 480, FocalLength = 531.15f };
            var depthConfig = new FT_CAMERA_CONFIG { Width = 640, Height = 480, FocalLength = 571.26f };

            hr = FtFaceTrackerRaw.Initialize(_faceTrackerPtr, ref _videoConfig, ref depthConfig, IntPtr.Zero, null);
            if (hr < 0)
            {
                _logger.LogWarning("IFTFaceTracker.Initialize failed (hr=0x{HR:X8})", hr);
                DisposeFaceTracking();
                return;
            }

            hr = FtFaceTrackerRaw.CreateFTResult(_faceTrackerPtr, out _faceResultPtr);
            if (hr < 0 || _faceResultPtr == IntPtr.Zero)
            {
                _logger.LogWarning("IFTFaceTracker.CreateFTResult failed (hr=0x{HR:X8})", hr);
                DisposeFaceTracking();
                return;
            }

            _faceTrackingInitialized = true;
            _logger.LogInformation("Face tracking initialized via FaceTrackLib.dll — REAL face tracking enabled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face tracking initialization failed — using skeleton-approximate");
            DisposeFaceTracking();
        }
    }

    private void DisposeFaceTracking()
    {
        if (_faceModel is not null) { try { Marshal.ReleaseComObject(_faceModel); } catch { } _faceModel = null; }
        if (_faceResultPtr != IntPtr.Zero) { try { Marshal.Release(_faceResultPtr); } catch { } _faceResultPtr = IntPtr.Zero; }
        if (_ftVideoImagePtr != IntPtr.Zero) { try { Marshal.Release(_ftVideoImagePtr); } catch { } _ftVideoImagePtr = IntPtr.Zero; }
        if (_ftDepthImagePtr != IntPtr.Zero) { try { Marshal.Release(_ftDepthImagePtr); } catch { } _ftDepthImagePtr = IntPtr.Zero; }
        if (_faceTrackerPtr != IntPtr.Zero) { try { Marshal.Release(_faceTrackerPtr); } catch { } _faceTrackerPtr = IntPtr.Zero; }

        _cachedTriangles = null;
        _meshVertexCount = 0;
        _suModelCount = 0;
        _lastMeshHr = 0;
        _meshAttemptCount = 0;
        _meshSuccessCount = 0;
        _meshFailCount = 0;
        _meshGuardSkipCount = 0;
        _meshFirstDiagLogged = false;
        _meshFirstOkLogged = false;
        _meshFirstFailLogged = false;
        _faceTrackingInitialized = false;
        _faceTrackingStarted = false;
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

    /// <summary>
    /// Polls skeleton data via NuiSkeletonGetNextFrame.
    /// Uses manual marshaling (AllocHGlobal + PtrToStructure) to bypass
    /// COM interop issues with large nested ByValArray structs on .NET 9.
    /// </summary>
    private void PollSkeletonFrame()
    {
        int frameSize = NuiConstants.SizeOfSkeletonFrame;
        IntPtr pFrame = Marshal.AllocHGlobal(frameSize);

        try
        {
            // Zero the buffer — NuiSkeletonGetNextFrame expects a clean buffer
            unsafe
            {
                new Span<byte>((void*)pFrame, frameSize).Clear();
            }

            int hr = _sensor!.NuiSkeletonGetNextFrame(100, pFrame);

            int pollNum = Interlocked.Increment(ref _skelPollCount);

            if (hr < 0)
            {
                // Log at Info level for first few + periodically so we can see in production logs
                if (pollNum <= 3 || pollNum % 600 == 0)
                    _logger.LogInformation("Skeleton poll #{N}: no data (hr=0x{HR:X8})", pollNum, hr);
                return;
            }

            // Manually marshal the struct from unmanaged memory
            var skelFrame = Marshal.PtrToStructure<NUI_SKELETON_FRAME>(pFrame);

            if (skelFrame.SkeletonData is null)
            {
                if (pollNum <= 3)
                    _logger.LogInformation("Skeleton poll #{N}: frame received but SkeletonData is null", pollNum);
                return;
            }

            // Log tracking state of all 6 slots
            int skelNum = Interlocked.Increment(ref _skeletonFrameNumber);
            if (skelNum <= 10 || skelNum % 300 == 0)
            {
                var states = string.Join(", ", skelFrame.SkeletonData.Select(
                    (d, i) => $"[{i}]={(NUI_SKELETON_TRACKING_STATE)d.eTrackingState}"));
                _logger.LogInformation("SKEL frame #{N}: {States}", skelNum, states);
            }

            bool anyTracked = false;
            for (int s = 0; s < NuiConstants.NUI_SKELETON_COUNT; s++)
            {
                ref NUI_SKELETON_DATA skel = ref skelFrame.SkeletonData[s];

                // Accept both Tracked and PositionOnly — PositionOnly gives center-of-mass
                if (skel.eTrackingState == (int)NUI_SKELETON_TRACKING_STATE.NotTracked)
                    continue;

                bool fullyTracked = skel.eTrackingState == (int)NUI_SKELETON_TRACKING_STATE.Tracked;
                var joints = ImmutableDictionary.CreateBuilder<int, Vector3>();
                var jointStates = ImmutableDictionary.CreateBuilder<int, TrackingState>();

                if (fullyTracked && skel.SkeletonPositions is not null)
                {
                    int jointCount = Math.Min(skel.SkeletonPositions.Length,
                        NuiConstants.NUI_SKELETON_POSITION_COUNT);
                    for (int j = 0; j < jointCount; j++)
                    {
                        var pos = skel.SkeletonPositions[j];
                        joints[j] = new Vector3(pos.x, pos.y, pos.z);

                        var nativeState = skel.eSkeletonPositionTrackingState?[j] ?? 0;
                        jointStates[j] = nativeState switch
                        {
                            (int)NUI_SKELETON_POSITION_TRACKING_STATE.Tracked => TrackingState.Tracked,
                            (int)NUI_SKELETON_POSITION_TRACKING_STATE.Inferred => TrackingState.Inferred,
                            _ => TrackingState.NotTracked,
                        };
                    }
                }
                else
                {
                    // PositionOnly: only center-of-mass is available (joint index 0 = HipCenter)
                    joints[0] = new Vector3(skel.Position.x, skel.Position.y, skel.Position.z);
                    jointStates[0] = TrackingState.Inferred;
                }

                var now = DateTimeOffset.UtcNow;
                int trackId = (int)skel.dwTrackingID;
                anyTracked = true;

                if (skelNum <= 10 || skelNum % 300 == 0)
                    _logger.LogInformation("SKEL tracked person #{Id}: state={State}, pos=({X:F2},{Y:F2},{Z:F2}), joints={Jc}",
                        trackId, (NUI_SKELETON_TRACKING_STATE)skel.eTrackingState,
                        skel.Position.x, skel.Position.y, skel.Position.z, joints.Count);

                SkeletonFrameReady?.Invoke(new SkeletonFrame
                {
                    Timestamp = now,
                    TrackingId = trackId,
                    State = fullyTracked ? TrackingState.Tracked : TrackingState.Inferred,
                    Position = new Vector3(skel.Position.x, skel.Position.y, skel.Position.z),
                    Joints = joints.ToImmutable(),
                    JointStates = jointStates.ToImmutable(),
                });

                // Emit face frame: fully-tracked → real or approximate from joints
                // PositionOnly → minimal face from center-of-mass (enough for pipeline to flow)
                if (fullyTracked)
                {
                    // Try real face tracking first; fall back to skeleton-approximate
                    bool realFace = _faceTrackingInitialized && TryEmitRealFaceFrame(skel, joints, trackId, now);
                    if (!realFace)
                        EmitApproximateFaceFrame(skel, joints, trackId, now);
                }
                else
                {
                    // PositionOnly: emit minimal face frame from center-of-mass
                    EmitPositionOnlyFaceFrame(skel, trackId, now);
                }
            }

            if (!anyTracked && (skelNum <= 5 || skelNum % 600 == 0))
                _logger.LogInformation("SKEL frame #{N}: no tracked persons", skelNum);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing native skeleton frame");
        }
        finally
        {
            Marshal.FreeHGlobal(pFrame);
        }
    }

    /// <summary>
    /// Creates an approximate <see cref="FaceFrame"/> from skeleton joint data.
    /// Uses head/neck/shoulder vectors for head pose, and places approximate
    /// pupil positions. Enough to feed VisionPipeline for HCEP mode analysis.
    /// </summary>
    private void EmitApproximateFaceFrame(
        NUI_SKELETON_DATA skel,
        ImmutableDictionary<int, Vector3>.Builder joints,
        int trackingId,
        DateTimeOffset timestamp)
    {
        // Joint indices: 0=HipCenter, 2=ShoulderCenter, 3=Head
        // 4=ShoulderLeft, 8=ShoulderRight
        if (skel.SkeletonPositions is null || skel.SkeletonPositions.Length < 10)
            return;

        Vector3 head = joints.ContainsKey(3)
            ? joints[3]
            : new Vector3(skel.Position.x, skel.Position.y, skel.Position.z);
        Vector3 shoulderCenter = joints.ContainsKey(2)
            ? joints[2]
            : head - new Vector3(0, 0.2f, 0);
        Vector3 shoulderLeft = joints.ContainsKey(4)
            ? joints[4]
            : shoulderCenter - new Vector3(0.2f, 0, 0);
        Vector3 shoulderRight = joints.ContainsKey(8)
            ? joints[8]
            : shoulderCenter + new Vector3(0.2f, 0, 0);

        // ── Head Rotation (approximate) ──
        // Pitch: angle of head-neck vector relative to vertical
        Vector3 neckToHead = Vector3.Normalize(head - shoulderCenter);
        float pitchRad = MathF.Asin(MathF.Max(-1f, MathF.Min(1f, -neckToHead.Z)));
        float pitchDeg = pitchRad * (180f / MathF.PI);

        // Yaw: shoulder cross product gives facing direction
        Vector3 shoulderVec = Vector3.Normalize(shoulderRight - shoulderLeft);
        float yawRad = MathF.Atan2(shoulderVec.Z, shoulderVec.X);
        float yawDeg = yawRad * (180f / MathF.PI);

        // Roll: shoulder tilt
        float rollDeg = MathF.Atan2(shoulderRight.Y - shoulderLeft.Y,
            Vector3.Distance(shoulderRight, shoulderLeft)) * (180f / MathF.PI);

        // ── Head Translation (Kinect gives meters; FaceFrame uses mm) ──
        Vector3 headMm = head * 1000f;

        // ── Feature Points 3D (87 points; key: [69]=left pupil, [73]=right pupil) ──
        // Approximate pupil positions: ±31.5mm from midline, 30mm above, 15mm in front
        var points3D = new Vector3[87];
        points3D[69] = new Vector3(-31.5f, 30f, -15f);  // Left pupil (head-relative mm)
        points3D[73] = new Vector3(31.5f, 30f, -15f);   // Right pupil

        // Nose tip [30], chin [8] — approximate positions for face rect
        points3D[30] = new Vector3(0, 10f, -60f);  // Nose tip
        points3D[8] = new Vector3(0, -40f, -30f);  // Chin

        // ── Face Bounding Rect + 2D Feature Points ──
        int faceX = 280, faceY = 180, faceW = 80, faceH = 100;
        if (head.Z > 0.1f)
        {
            float fx = 525f, cx = 320f, cy = 240f;
            float hx = cx + head.X * fx / head.Z;
            float hy = cy - head.Y * fx / head.Z;
            float halfSize = 0.12f * fx / head.Z; // ~12cm face radius
            faceX = (int)(hx - halfSize);
            faceY = (int)(hy - halfSize);
            faceW = (int)(halfSize * 2);
            faceH = (int)(halfSize * 2.5f);
        }

        var points2D = GenerateApproxFacePoints2D(faceX, faceY, faceW, faceH);

        // ── Action Units (6 values, neutral = 0.0) ──
        var actionUnits = new float[6];

        FaceFrameReady?.Invoke(new FaceFrame
        {
            Timestamp = timestamp,
            TrackingId = trackingId,
            IsTracked = true,
            HeadRotation = new Vector3(pitchDeg, yawDeg, rollDeg),
            HeadTranslation = headMm,
            FeaturePoints3D = points3D,
            FeaturePoints2D = points2D,
            ActionUnits = actionUnits,
            FaceRect = (faceX, faceY, faceW, faceH),
        });
    }

    /// <summary>
    /// Creates a minimal <see cref="FaceFrame"/> from a PositionOnly skeleton.
    /// Uses center-of-mass position with zero rotation (facing forward).
    /// This is lower fidelity than the approximate face frame from full joints,
    /// but ensures the vision pipeline receives data even before full tracking.
    /// </summary>
    private void EmitPositionOnlyFaceFrame(
        NUI_SKELETON_DATA skel,
        int trackingId,
        DateTimeOffset timestamp)
    {
        // Estimate head position: center-of-mass + vertical offset (~0.3m above hip center)
        float headX = skel.Position.x;
        float headY = skel.Position.y + 0.3f;
        float headZ = skel.Position.z;

        if (headZ < 0.1f) return; // Too close or invalid

        // Head translation in mm
        Vector3 headMm = new Vector3(headX, headY, headZ) * 1000f;

        // Project to pixel coords
        float fx = 525f, fy = 525f, cx = 320f, cy = 240f;
        float px = cx + headX * fx / headZ;
        float py = cy - headY * fy / headZ;

        float halfSize = 0.12f * fx / headZ;
        int faceX = (int)(px - halfSize);
        int faceY = (int)(py - halfSize);
        int faceW = (int)(halfSize * 2);
        int faceH = (int)(halfSize * 2.5f);

        var points2D = GenerateApproxFacePoints2D(faceX, faceY, faceW, faceH);

        var points3D = new Vector3[87];
        points3D[69] = new Vector3(-31.5f, 30f, -15f);
        points3D[73] = new Vector3(31.5f, 30f, -15f);

        FaceFrameReady?.Invoke(new FaceFrame
        {
            Timestamp = timestamp,
            TrackingId = trackingId,
            IsTracked = true,
            HeadRotation = Vector3.Zero, // Unknown — assume facing camera
            HeadTranslation = headMm,
            FeaturePoints3D = points3D,
            FeaturePoints2D = points2D,
            ActionUnits = new float[6],
            FaceRect = (faceX, faceY, faceW, faceH),
        });
    }

    /// <summary>
    /// Attempts real face tracking via FaceTrackLib.dll.
    /// Attaches current color/depth frames to IFTImage wrappers,
    /// provides skeleton head hints, and calls StartTracking/ContinueTracking.
    /// Returns true if a real FaceFrame was emitted.
    /// </summary>
    // ── Approximate Face Points Generation ───────────────────

    /// <summary>
    /// Generates approximate 2D feature points for the 87-point FaceTrackLib model
    /// using standard facial proportions relative to the face bounding box.
    /// This enables the wireframe overlay to render even without the real FaceTrackLib.
    /// </summary>
    private static Vector2[] GenerateApproxFacePoints2D(int faceX, int faceY, int faceW, int faceH)
    {
        var pts = new Vector2[87];
        if (faceW <= 0 || faceH <= 0) return pts;

        // Face center and scale helpers
        float cx = faceX + faceW * 0.5f;
        float cy = faceY + faceH * 0.5f;
        float w = faceW;
        float h = faceH;

        // Helper: create point from fractional offsets relative to face center
        // dx, dy in range [-0.5, 0.5] where (0,0) = center
        Vector2 P(float dx, float dy) => new(cx + dx * w, cy + dy * h);

        // ── Forehead / Top of head ──
        pts[0] = P(0.00f, -0.48f);   // top of skull (center)
        pts[1] = P(-0.20f, -0.44f);  // right forehead
        pts[3] = P(0.20f, -0.44f);   // left forehead

        // ── Right eye (camera-right = person's left) ──
        pts[10] = P(-0.30f, -0.18f); // outer corner
        pts[11] = P(-0.23f, -0.23f); // mid top
        pts[9] = P(-0.18f, -0.23f); // above mid
        pts[13] = P(-0.10f, -0.18f); // inner corner
        pts[14] = P(-0.18f, -0.14f); // below mid
        pts[12] = P(-0.23f, -0.14f); // mid bottom

        // ── Left eye (camera-left = person's right) ──
        pts[31] = P(0.30f, -0.18f);  // outer corner
        pts[32] = P(0.23f, -0.23f);  // mid top
        pts[30] = P(0.18f, -0.23f);  // above mid
        pts[34] = P(0.10f, -0.18f);  // inner corner
        pts[35] = P(0.18f, -0.14f);  // below mid
        pts[33] = P(0.23f, -0.14f);  // mid bottom

        // ── Right eyebrow ──
        pts[5] = P(-0.35f, -0.30f);  // outer
        pts[6] = P(-0.25f, -0.34f);  // mid top
        pts[7] = P(-0.12f, -0.30f);  // inner
        pts[8] = P(-0.25f, -0.28f);  // mid bottom

        // ── Left eyebrow ──
        pts[29] = P(0.12f, -0.30f);  // inner (rightSide in chain)
        pts[28] = P(0.25f, -0.34f);  // mid top
        pts[27] = P(0.35f, -0.30f);  // outer (leftSide in chain)
        pts[26] = P(0.25f, -0.28f);  // mid bottom

        // ── Nose (detailed) ──
        pts[37] = P(0.00f, -0.12f);  // nose bridge top (between eyes)
        pts[38] = P(0.00f, -0.04f);  // nose bridge middle
        pts[39] = P(0.00f, 0.02f);   // nose tip
        pts[40] = P(-0.08f, 0.00f);  // right nostril top
        pts[41] = P(0.00f, 0.06f);   // nostril bottom center
        pts[42] = P(0.08f, 0.00f);   // left nostril top
        pts[43] = P(-0.10f, 0.04f);  // right nostril outer
        pts[44] = P(0.00f, 0.05f);   // nose bottom center
        pts[45] = P(0.10f, 0.04f);   // left nostril outer

        // ── Upper lip contour ──
        pts[16] = P(-0.18f, 0.18f);  // right corner
        pts[18] = P(-0.13f, 0.14f);  // right dip
        pts[19] = P(-0.08f, 0.12f);  // right top
        pts[20] = P(-0.04f, 0.11f);  // right upper
        pts[2] = P(0.00f, 0.12f);   // center (cupid's bow dip)
        pts[21] = P(0.04f, 0.11f);   // left upper
        pts[22] = P(0.08f, 0.12f);   // left top
        pts[23] = P(0.13f, 0.14f);   // left dip
        pts[24] = P(0.18f, 0.18f);   // left corner

        // ── Lower lip contour ──
        pts[46] = P(-0.14f, 0.21f);  // right inner
        pts[47] = P(-0.10f, 0.24f);  // right outer
        pts[48] = P(-0.05f, 0.26f);  // bottom right
        pts[36] = P(0.00f, 0.27f);   // lower lip center (bottom)
        pts[49] = P(0.05f, 0.26f);   // bottom left
        pts[50] = P(0.10f, 0.24f);   // left outer
        pts[51] = P(0.14f, 0.21f);   // left inner
        // Close lower lip at mouth corners
        pts[52] = P(0.00f, 0.19f);   // lower lip top center (inside mouth)

        // ── Jawline / Face outline (enhanced) ──
        pts[15] = P(-0.45f, 0.05f);  // right side of face
        pts[17] = P(-0.32f, 0.38f);  // right chin
        pts[4] = P(0.00f, 0.48f);   // chin bottom center
        pts[25] = P(0.32f, 0.38f);   // left chin

        // Extended face outline (right side: temple → jaw)
        pts[53] = P(-0.48f, -0.05f); // right temple lower
        pts[54] = P(-0.47f, 0.00f);  // right cheek upper
        pts[55] = P(-0.42f, 0.20f);  // right cheek lower
        pts[56] = P(-0.18f, 0.44f);  // right chin inner

        // Extended face outline (left side: jaw → temple)
        pts[57] = P(0.18f, 0.44f);   // left chin inner
        pts[58] = P(0.42f, 0.20f);   // left cheek lower
        pts[59] = P(0.47f, 0.00f);   // left cheek upper
        pts[60] = P(0.48f, -0.05f);  // left temple lower

        // Forehead-to-temple connectors (right side)
        pts[61] = P(-0.10f, -0.46f); // right upper forehead
        pts[62] = P(-0.35f, -0.38f); // right mid forehead
        pts[63] = P(-0.42f, -0.24f); // right outer brow
        pts[64] = P(-0.46f, -0.10f); // right temple upper

        // Forehead-to-temple connectors (left side)
        pts[65] = P(0.10f, -0.46f);  // left upper forehead
        pts[66] = P(0.35f, -0.38f);  // left mid forehead
        pts[67] = P(0.42f, -0.24f);  // left outer brow
        pts[68] = P(0.46f, -0.10f);  // left temple upper

        // ── Pupils / Eye centers ──
        pts[69] = P(-0.22f, -0.18f); // right pupil (camera-right)
        pts[73] = P(0.22f, -0.18f);  // left pupil (camera-left)

        return pts;
    }

    // One-shot flag so we only log the very first early-return reason once
    private bool _realFaceFirstBailLogged;

    private bool TryEmitRealFaceFrame(
        NUI_SKELETON_DATA skel,
        ImmutableDictionary<int, Vector3>.Builder joints,
        int trackingId,
        DateTimeOffset timestamp)
    {
        if (_faceTrackerPtr == IntPtr.Zero || _faceResultPtr == IntPtr.Zero ||
            _ftVideoImagePtr == IntPtr.Zero || _ftDepthImagePtr == IntPtr.Zero)
        {
            if (!_realFaceFirstBailLogged)
            {
                _realFaceFirstBailLogged = true;
                _logger.LogWarning(
                    "[REAL FACE BAIL] null guard: tracker={T} result={R} video={V} depth={D}",
                    _faceTrackerPtr != IntPtr.Zero ? "OK" : "NULL",
                    _faceResultPtr != IntPtr.Zero ? "OK" : "NULL",
                    _ftVideoImagePtr != IntPtr.Zero ? "OK" : "NULL",
                    _ftDepthImagePtr != IntPtr.Zero ? "OK" : "NULL");
            }
            return false;
        }

        var colorPixels = _lastColorPixels;
        var depthRaw = _lastDepthRaw;
        if (colorPixels is null || depthRaw is null)
        {
            if (!_realFaceFirstBailLogged)
            {
                _realFaceFirstBailLogged = true;
                _logger.LogWarning(
                    "[REAL FACE BAIL] frame data: colorPixels={C} depthRaw={D}",
                    colorPixels != null ? colorPixels.Length.ToString() : "NULL",
                    depthRaw != null ? depthRaw.Length.ToString() : "NULL");
            }
            return false;
        }

        try
        {
            // ── Copy frame data into pre-allocated IFTImage buffers ──────
            // Matches C++ reference pattern: KinectSensor::GetVideoBuffer()->CopyTo(m_colorImage)
            // IFTImage buffers were pre-allocated in InitializeFaceTracking
            // with Allocate(). Each frame we copy data into the owned buffer.

            // Color: BGRX 640×480 = 1,228,800 bytes
            IntPtr videoBuf = FtImageRaw.GetBuffer(_ftVideoImagePtr);
            if (videoBuf == IntPtr.Zero)
            {
                if (!_realFaceFirstBailLogged)
                {
                    _realFaceFirstBailLogged = true;
                    _logger.LogWarning("[REAL FACE BAIL] ftVideoImage.GetBuffer returned NULL — image not allocated?");
                }
                return false;
            }
            Marshal.Copy(colorPixels, 0, videoBuf, colorPixels.Length);

            // Depth: D13P3/D16 640×480 = 307,200 shorts = 614,400 bytes
            IntPtr depthBuf = FtImageRaw.GetBuffer(_ftDepthImagePtr);
            if (depthBuf == IntPtr.Zero)
            {
                if (!_realFaceFirstBailLogged)
                {
                    _realFaceFirstBailLogged = true;
                    _logger.LogWarning("[REAL FACE BAIL] ftDepthImage.GetBuffer returned NULL — image not allocated?");
                }
                return false;
            }
            Marshal.Copy(depthRaw, 0, depthBuf, depthRaw.Length);

            int hr;  // shared HRESULT for tracking and mesh calls below

            // Build sensor data struct — pass raw COM pointers directly.
            // AddRef so the pointers stay valid through the native call;
            // we Release in the finally block below.
            Marshal.AddRef(_ftVideoImagePtr);
            Marshal.AddRef(_ftDepthImagePtr);
            var sensorData = new FT_SENSOR_DATA
            {
                pVideoFrame = _ftVideoImagePtr,
                pDepthFrame = _ftDepthImagePtr,
                ZoomFactor = 1.0f,
                ViewOffsetX = 0,
                ViewOffsetY = 0,
            };

            // Provide skeleton head hints: [0]=neck, [1]=head center
            IntPtr headPointsPtr = IntPtr.Zero;
            try
            {
                Vector3 head = joints.ContainsKey(3) ? joints[3] : new Vector3(skel.Position.x, skel.Position.y, skel.Position.z);
                Vector3 neck = joints.ContainsKey(2) ? joints[2] : head - new Vector3(0, 0.1f, 0);

                var headPoints = new FT_VECTOR3D[2];
                headPoints[0] = new FT_VECTOR3D { x = neck.X, y = neck.Y, z = neck.Z };
                headPoints[1] = new FT_VECTOR3D { x = head.X, y = head.Y, z = head.Z };

                int hpSize = Marshal.SizeOf<FT_VECTOR3D>() * 2;
                headPointsPtr = Marshal.AllocHGlobal(hpSize);
                Marshal.StructureToPtr(headPoints[0], headPointsPtr, false);
                Marshal.StructureToPtr(headPoints[1], headPointsPtr + Marshal.SizeOf<FT_VECTOR3D>(), false);

                if (!_faceTrackingStarted)
                {
                    hr = FtFaceTrackerRaw.StartTracking(_faceTrackerPtr, ref sensorData, IntPtr.Zero, headPointsPtr, _faceResultPtr);
                    int startStatus = FtResultRaw.GetStatus(_faceResultPtr);
                    if (hr >= 0 && startStatus >= 0)
                    {
                        _faceTrackingStarted = true;
                        _logger.LogInformation("[REAL FACE] StartTracking SUCCEEDED (hr=0x{Hr:X8} status=0x{St:X8})", unchecked((uint)hr), unchecked((uint)startStatus));
                    }
                    else
                    {
                        // StartTracking often fails for the first few frames while it
                        // searches for a face. Log at Debug (not Warning) and keep retrying.
                        _logger.LogDebug("[REAL FACE] StartTracking not yet locked — hr=0x{Hr:X8} status=0x{St:X8}", unchecked((uint)hr), unchecked((uint)startStatus));
                        return false;  // retry next frame — do NOT set bail flag
                    }
                }
                else
                {
                    int contStatus;
                    hr = FtFaceTrackerRaw.ContinueTracking(_faceTrackerPtr, ref sensorData, headPointsPtr, _faceResultPtr);
                    contStatus = FtResultRaw.GetStatus(_faceResultPtr);
                    if (hr < 0 || contStatus < 0)
                    {
                        // Lost tracking — fall back to StartTracking next frame.
                        // Log at Debug, not Warning (temporary tracking loss is normal).
                        _logger.LogDebug("[REAL FACE] ContinueTracking lost face — hr=0x{Hr:X8} status=0x{St:X8}",
                            unchecked((uint)hr), unchecked((uint)contStatus));
                        _faceTrackingStarted = false;
                        return false;
                    }
                }
            }
            finally
            {
                // Release the IUnknown references we created for FT_SENSOR_DATA
                if (sensorData.pVideoFrame != IntPtr.Zero)
                    Marshal.Release(sensorData.pVideoFrame);
                if (sensorData.pDepthFrame != IntPtr.Zero)
                    Marshal.Release(sensorData.pDepthFrame);

                if (headPointsPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(headPointsPtr);
            }

            int postStatus = FtResultRaw.GetStatus(_faceResultPtr);
            if (postStatus < 0)
            {
                if (!_realFaceFirstBailLogged)
                {
                    _realFaceFirstBailLogged = true;
                    _logger.LogWarning("[REAL FACE BAIL] post-tracking GetStatus < 0 (status=0x{St:X8})", unchecked((uint)postStatus));
                }
                return false;
            }

            // ── Extract face tracking results ──

            // 3D Pose: scale, rotation (pitch/yaw/roll), translation
            float[] rotation = new float[3];
            float[] translation = new float[3];
            hr = FtResultRaw.Get3DPose(_faceResultPtr, out float scale, rotation, translation);
            if (hr < 0)
            {
                if (!_realFaceFirstBailLogged)
                {
                    _realFaceFirstBailLogged = true;
                    _logger.LogWarning("[REAL FACE BAIL] Get3DPose failed hr=0x{Hr:X8}", unchecked((uint)hr));
                }
                return false;
            }

            // Face rectangle
            hr = FtResultRaw.GetFaceRect(_faceResultPtr, out RECT faceRect);
            int faceX = faceRect.Left;
            int faceY = faceRect.Top;
            int faceW = faceRect.Right - faceRect.Left;
            int faceH = faceRect.Bottom - faceRect.Top;

            // Animation Units (6 values)
            float[] actionUnits = new float[6];
            hr = FtResultRaw.GetAUCoefficients(_faceResultPtr, out IntPtr auPtr, out uint auCount);
            if (hr >= 0 && auPtr != IntPtr.Zero && auCount > 0)
            {
                int copyCount = Math.Min((int)auCount, 6);
                Marshal.Copy(auPtr, actionUnits, 0, copyCount);
            }

            // 2D Shape Points (feature points for eye detection etc.)
            Vector2[] points2D = new Vector2[87];
            hr = FtResultRaw.Get2DShapePoints(_faceResultPtr, out IntPtr pts2DPtr, out uint pts2DCount);
            if (hr >= 0 && pts2DPtr != IntPtr.Zero && pts2DCount > 0)
            {
                int copyCount = Math.Min((int)pts2DCount, 87);
                for (int i = 0; i < copyCount; i++)
                {
                    IntPtr pVec = pts2DPtr + i * 8; // sizeof(FT_VECTOR2D) = 8
                    float x = Marshal.PtrToStructure<float>(pVec);
                    float y = Marshal.PtrToStructure<float>(pVec + 4);
                    points2D[i] = new Vector2(x, y);
                }
            }

            // ── Triangle Mesh (FaceTrackingBasics-WPF SDK sample approach) ──
            // Get the IFTModel to retrieve triangle topology and projected mesh vertices.
            // Triangles are static (cached after first retrieval).
            // Projected vertices are computed each frame using SU/AU coefficients and pose.
            Vector2[]? meshVertices = null;
            var meshTriangles = _cachedTriangles;

            // ── Unconditional trace: prove this code path is reached ──
            if (!_meshFirstDiagLogged)
            {
                _logger.LogInformation(
                "[MESH TRACE] ENTRY: faceTracker={FT} faceModel={FM} vertexCount={V} cachedTriangles={CT}",
                _faceTrackerPtr != IntPtr.Zero ? "OK" : "NULL",
                _faceModel != null ? "OK" : "NULL",
                _meshVertexCount,
                _cachedTriangles != null ? _cachedTriangles.Length.ToString() : "NULL");
            }

            try
            {
                // Get face model (first time only)
                if (_faceModel == null && _faceTrackerPtr != IntPtr.Zero)
                {
                    hr = FtFaceTrackerRaw.GetFaceModel(_faceTrackerPtr, out IntPtr pModel);
                    if (hr >= 0 && pModel != IntPtr.Zero)
                    {
                        _faceModel = (IFTModel)Marshal.GetObjectForIUnknown(pModel);
                        Marshal.Release(pModel); // GetObjectForIUnknown AddRefs, release ours
                        _meshVertexCount = _faceModel.GetVertexCount();
                        // Store model-level SU count (mirrors C++ pModel->GetSUCount()).
                        // This is the canonical count to pass to GetProjectedShape —
                        // NOT the runtime value from IFTFaceTracker.GetShapeUnits.
                        _suModelCount = _faceModel.GetSUCount();
                        _lastMeshHr = 0;
                        _logger.LogInformation(
                            "[MESH TRACE] Face model loaded: ptr=0x{Ptr:X} vertexCount={V} suCount={SU} auCount={AU}",
                            pModel, _meshVertexCount, _suModelCount, _faceModel.GetAUCount());

                        // Get triangle topology (static — only need once, like SDK sample)
                        hr = _faceModel.GetTriangles(out IntPtr triPtr, out uint triCount);
                        if (hr >= 0 && triPtr != IntPtr.Zero && triCount > 0)
                        {
                            _cachedTriangles = new (int, int, int)[triCount];
                            int triStructSize = Marshal.SizeOf<FT_TRIANGLE>();
                            for (uint i = 0; i < triCount; i++)
                            {
                                IntPtr p = triPtr + (int)i * triStructSize;
                                var tri = Marshal.PtrToStructure<FT_TRIANGLE>(p);
                                _cachedTriangles[i] = (tri.First, tri.Second, tri.Third);
                            }
                            meshTriangles = _cachedTriangles;
                            _logger.LogInformation(
                                "[MESH TRACE] Triangles loaded: {Count} tris, first=({A},{B},{C})",
                                triCount,
                                _cachedTriangles[0].First, _cachedTriangles[0].Second, _cachedTriangles[0].Third);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[MESH TRACE] GetTriangles FAILED: hr=0x{Hr:X8} ptr=0x{Ptr:X} count={N}",
                                unchecked((uint)hr), triPtr, triCount);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[MESH TRACE] GetFaceModel FAILED: hr=0x{Hr:X8} pModel=0x{Ptr:X}",
                            unchecked((uint)hr), pModel);
                    }
                }

                // Get projected shape vertices (each frame)
                // ── Exact C++ pattern from FTHelper::SubmitFraceTrackingResult ───────────────
                // FLOAT* pSU = NULL;  UINT numSU;  BOOL suConverged;
                // m_pFaceTracker->GetShapeUnits(NULL, &pSU, &numSU, &suConverged);
                //   → pSU is a pointer INTO the SDK's own internal array, or NULL if not ready
                // GetProjectedShape(config, 1.0, {0,0}, pSU, pModel->GetSUCount(), pAUs, auCount, ...)
                //   → SDK accepts NULL pSU and renders the neutral Candide-3 shape
                //
                // IMPORTANT: pass the raw SDK pointer directly — no managed copy/pin needed.
                // Pass _suModelCount (= pModel->GetSUCount()) as the count, NOT numSU.
                // ──────────────────────────────────────────────────────────────────────────────
                if (_faceModel != null && _meshVertexCount > 0 && meshTriangles != null)
                {
                    // Step 1: Get SU coef pointer straight from the tracker.
                    // suPtrDirect points into SDK-internal memory — valid for rest of this call.
                    uint suRuntime = 0;
                    hr = FtFaceTrackerRaw.GetShapeUnits(_faceTrackerPtr, out float headScale, out IntPtr suPtrDirect, ref suRuntime, out bool suConverged);
                    IntPtr suToPass = (hr >= 0) ? suPtrDirect : IntPtr.Zero;   // NULL = neutral shape; SDK handles it

                    // Use model-level SU count, exactly as C++ uses pModel->GetSUCount().
                    uint suPassCount = (_suModelCount > 0) ? _suModelCount : 11u;

                    if (!suConverged)
                        _logger.LogDebug("SU not yet converged (suRuntime={N}), using neutral shape", suRuntime);

                    // ── One-shot startup diagnostic (first time we reach this path) ──
                    if (!_meshFirstDiagLogged)
                    {
                        _meshFirstDiagLogged = true;
                        _logger.LogInformation(
                            "[MESH TRACE] === FIRST MESH ATTEMPT ==="
                            + " | faceModel=OK vertexCount={V} suModelCount={SuM}"
                            + " | GetShapeUnits: hr=0x{SuHr:X8} headScale={HS:F3} suPtr=0x{SuPtr:X} suRuntime={SuR} converged={Conv}"
                            + " | videoConfig: {CW}x{CH} focal={CF:F2}"
                            + " | triangles={TC}"
                            + " | pose: scale={S:F4} rot=({RX:F2},{RY:F2},{RZ:F2}) trans=({TX:F3},{TY:F3},{TZ:F3})",
                            _meshVertexCount, _suModelCount,
                            unchecked((uint)hr), headScale, suPtrDirect, suRuntime, suConverged,
                            _videoConfig.Width, _videoConfig.Height, _videoConfig.FocalLength,
                            meshTriangles.Length,
                            scale,
                            rotation[0], rotation[1], rotation[2],
                            translation[0], translation[1], translation[2]);
                    }

                    // Step 2: Re-read AU coefficients for this frame.
                    hr = FtResultRaw.GetAUCoefficients(_faceResultPtr, out IntPtr auPtrMesh, out uint auCountMesh);
                    if (hr >= 0 && auPtrMesh != IntPtr.Zero)
                    {
                        var rotVec = new FT_VECTOR3D { x = rotation[0], y = rotation[1], z = rotation[2] };
                        var transVec = new FT_VECTOR3D { x = translation[0], y = translation[1], z = translation[2] };
                        var viewOffset = new FT_POINT { X = 0, Y = 0 };

                        // Step 3: Allocate output buffer (FT_VECTOR2D = 8 bytes each).
                        int bufSize = (int)_meshVertexCount * 8;
                        IntPtr vertBuf = Marshal.AllocHGlobal(bufSize);
                        _meshAttemptCount++;
                        try
                        {
                            hr = _faceModel.GetProjectedShape(
                                ref _videoConfig,
                                1.0f,
                                viewOffset,
                                suToPass, suPassCount,   // raw SDK ptr + model SU count (C++ pattern)
                                auPtrMesh, auCountMesh,
                                scale,
                                ref rotVec,
                                ref transVec,
                                vertBuf,
                                _meshVertexCount);

                            if (hr >= 0)
                            {
                                _meshSuccessCount++;
                                meshVertices = new Vector2[_meshVertexCount];
                                for (int i = 0; i < (int)_meshVertexCount; i++)
                                {
                                    IntPtr p = vertBuf + i * 8;
                                    float vx = Marshal.PtrToStructure<float>(p);
                                    float vy = Marshal.PtrToStructure<float>(p + 4);
                                    meshVertices[i] = new Vector2(vx, vy);
                                }
                                _lastMeshHr = 0;  // success

                                // One-shot: log first successful mesh with sample vertices
                                if (!_meshFirstOkLogged)
                                {
                                    _meshFirstOkLogged = true;
                                    var v0 = meshVertices.Length > 0 ? meshVertices[0] : Vector2.Zero;
                                    var v1 = meshVertices.Length > 1 ? meshVertices[1] : Vector2.Zero;
                                    var vn = meshVertices.Length > 2 ? meshVertices[^1] : Vector2.Zero;
                                    _logger.LogInformation(
                                        "[MESH TRACE] === FIRST SUCCESS ==="
                                        + " | {V} vertices | v[0]=({X0:F1},{Y0:F1}) v[1]=({X1:F1},{Y1:F1}) v[last]=({XN:F1},{YN:F1})"
                                        + " | suPtr=0x{SuPtr:X} suCount={SuC} auPtr=0x{AuPtr:X} auCount={AuC}"
                                        + " | scale={S:F4} rot=({RX:F2},{RY:F2},{RZ:F2}) trans=({TX:F3},{TY:F3},{TZ:F3})",
                                        _meshVertexCount,
                                        v0.X, v0.Y, v1.X, v1.Y, vn.X, vn.Y,
                                        suToPass, suPassCount, auPtrMesh, auCountMesh,
                                        scale,
                                        rotation[0], rotation[1], rotation[2],
                                        translation[0], translation[1], translation[2]);
                                }
                            }
                            else
                            {
                                _meshFailCount++;
                                // Emit the HRESULT in the FaceFrame so AvatarWindow can show it in the MESH HUD.
                                _lastMeshHr = unchecked((uint)hr);

                                // One-shot: detailed failure with all parameter values
                                if (!_meshFirstFailLogged)
                                {
                                    _meshFirstFailLogged = true;
                                    _logger.LogWarning(
                                        "[MESH TRACE] === FIRST FAILURE ==="
                                        + " | hr=0x{Hr:X8}"
                                        + " | suPtr=0x{SuPtr:X} suCount={SuC} auPtr=0x{AuPtr:X} auCount={AuC}"
                                        + " | scale={S:F4} rot=({RX:F2},{RY:F2},{RZ:F2}) trans=({TX:F3},{TY:F3},{TZ:F3})"
                                        + " | videoConfig: {CW}x{CH} focal={CF:F2}"
                                        + " | vertexCount={V} bufSize={BS}",
                                        unchecked((uint)hr),
                                        suToPass, suPassCount, auPtrMesh, auCountMesh,
                                        scale,
                                        rotation[0], rotation[1], rotation[2],
                                        translation[0], translation[1], translation[2],
                                        _videoConfig.Width, _videoConfig.Height, _videoConfig.FocalLength,
                                        _meshVertexCount, bufSize);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(vertBuf);
                        }

                        // Periodic summary every 300 frames (~10 sec at 30fps)
                        if (_meshAttemptCount % 300 == 0)
                        {
                            _logger.LogInformation(
                                "[MESH TRACE] summary: {Attempts} attempts, {Ok} ok, {Fail} fail, lastHr=0x{Hr:X8}, guardSkips={GS}",
                                _meshAttemptCount, _meshSuccessCount, _meshFailCount, _lastMeshHr, _meshGuardSkipCount);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[MESH TRACE] GetAUCoefficients failed hr=0x{Hr:X8} — skipping GetProjectedShape", unchecked((uint)hr));
                    }
                }
                else
                {
                    // Log guard-failure once, then periodically
                    _meshGuardSkipCount++;
                    if (_meshGuardSkipCount == 1 || _meshGuardSkipCount % 300 == 0)
                    {
                        _logger.LogWarning(
                            "[MESH TRACE] Guard skipped (count={N}): faceModel={FM} vertexCount={V} triangles={T}",
                            _meshGuardSkipCount,
                            _faceModel != null ? "OK" : "NULL",
                            _meshVertexCount,
                            meshTriangles != null ? meshTriangles.Length.ToString() : "NULL");
                    }
                }
            }
            catch (Exception ex)
            {
                // Mesh extraction failure is non-fatal — we still emit the face frame
                // with feature points and fall back to edge chain rendering
                _logger.LogWarning(ex, "Face mesh extraction threw — using FP fallback");
            }

            // 3D Feature Points (approximate from 2D + depth)
            Vector3[] points3D = new Vector3[87];
            // Place pupils at standard indices
            Vector3 headPos = joints.ContainsKey(3) ? joints[3] : Vector3.Zero;
            points3D[69] = new Vector3(-31.5f, 30f, -15f);  // Left pupil (head-relative mm)
            points3D[73] = new Vector3(31.5f, 30f, -15f);   // Right pupil

            // Head translation in mm
            Vector3 headTranslation = new Vector3(translation[0] * 1000f, translation[1] * 1000f, translation[2] * 1000f);
            // Head rotation in degrees (FaceTrackLib returns pitch=X, yaw=Y, roll=Z)
            Vector3 headRotation = new Vector3(rotation[0], rotation[1], rotation[2]);

            FaceFrameReady?.Invoke(new FaceFrame
            {
                Timestamp = timestamp,
                TrackingId = trackingId,
                IsTracked = true,
                HeadRotation = headRotation,
                HeadTranslation = headTranslation,
                FeaturePoints3D = points3D,
                FeaturePoints2D = points2D,
                ActionUnits = actionUnits,
                FaceRect = (faceX, faceY, faceW, faceH),
                FaceMeshVertices2D = meshVertices,
                FaceMeshTriangles = meshTriangles,
                MeshHr = _lastMeshHr,
            });

            return true;
        }
        catch (Exception ex)
        {
            if (!_realFaceFirstBailLogged)
            {
                _realFaceFirstBailLogged = true;
                _logger.LogWarning(ex, "[REAL FACE BAIL] EXCEPTION in TryEmitRealFaceFrame");
            }
            return false;
        }
    }

    // ── Private Helpers ────────────────────────────────────────

    private void SetState(SensorState newState)
    {
        if (_state == newState) return;
        _state = newState;
        StateChanged?.Invoke(newState);
        _logger.LogInformation("Sensor state → {State}", newState);
    }
}
