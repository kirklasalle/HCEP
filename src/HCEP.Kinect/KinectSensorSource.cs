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
    private IFTFaceTracker? _faceTracker;
    private IFTResult? _faceResult;
    private IFTImage? _ftVideoImage;
    private IFTImage? _ftDepthImage;
    private bool _faceTrackingInitialized;
    private bool _faceTrackingStarted;
    private byte[]? _lastColorPixels;
    private short[]? _lastDepthRaw;
    private GCHandle _colorPinHandle;
    private GCHandle _depthPinHandle;

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
    public event Action<AudioFrame>? AudioFrameReady;
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

            // Build initialization flags
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

            // Open depth stream: 640×480 with player index
            if (streams.HasFlag(SensorStreamType.Depth))
            {
                hr = _sensor.NuiImageStreamOpen(
                    NUI_IMAGE_TYPE.DepthAndPlayerIndex,
                    NUI_IMAGE_RESOLUTION.Res640x480,
                    0,    // dwImageFrameFlags
                    2,    // dwFrameLimit
                    IntPtr.Zero,
                    out _depthStreamHandle);

                if (hr < 0)
                {
                    _logger.LogWarning("Failed to open depth stream (hr=0x{HR:X8})", hr);
                    // Non-fatal — we can still do color
                }
                else
                {
                    _logger.LogInformation("Depth stream opened: 640×480 (handle=0x{H:X})",
                        _depthStreamHandle);
                }
            }

            // Enable skeleton tracking — default to FULL BODY mode
            if (streams.HasFlag(SensorStreamType.Skeleton))
            {
                // Start with full-body tracking (no seated flag).
                // Can be switched at runtime via SeatedMode property.
                uint skelFlags = _seatedMode
                    ? NuiConstants.NUI_SKELETON_TRACKING_FLAG_ENABLE_SEATED_SUPPORT
                    : 0u;
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

            _faceTracker = FaceTrackNative.CreateFaceTracker();
            if (_faceTracker is null)
            {
                _logger.LogWarning("FTCreateFaceTracker returned null");
                return;
            }

            _ftVideoImage = FaceTrackNative.CreateImage();
            _ftDepthImage = FaceTrackNative.CreateImage();
            if (_ftVideoImage is null || _ftDepthImage is null)
            {
                _logger.LogWarning("FTCreateImage returned null");
                DisposeFaceTracking();
                return;
            }

            // Camera configs: Kinect v1 color 640×480, focal ~531.15 pixels
            // Depth 640×480, focal ~285.63 pixels (after NUI_IMAGE_PLAYER_INDEX_SHIFT)
            var videoConfig = new FT_CAMERA_CONFIG { Width = 640, Height = 480, FocalLength = 531.15f };
            var depthConfig = new FT_CAMERA_CONFIG { Width = 640, Height = 480, FocalLength = 285.63f };

            int hr = _faceTracker.Initialize(ref videoConfig, ref depthConfig, IntPtr.Zero, null);
            if (hr < 0)
            {
                _logger.LogWarning("IFTFaceTracker.Initialize failed (hr=0x{HR:X8})", hr);
                DisposeFaceTracking();
                return;
            }

            hr = _faceTracker.CreateFTResult(out _faceResult!);
            if (hr < 0 || _faceResult is null)
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
        if (_colorPinHandle.IsAllocated) _colorPinHandle.Free();
        if (_depthPinHandle.IsAllocated) _depthPinHandle.Free();

        if (_faceResult is not null) { try { Marshal.ReleaseComObject(_faceResult); } catch { } _faceResult = null; }
        if (_ftVideoImage is not null) { try { Marshal.ReleaseComObject(_ftVideoImage); } catch { } _ftVideoImage = null; }
        if (_ftDepthImage is not null) { try { Marshal.ReleaseComObject(_ftDepthImage); } catch { } _ftDepthImage = null; }
        if (_faceTracker is not null) { try { Marshal.ReleaseComObject(_faceTracker); } catch { } _faceTracker = null; }

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

        // ── Feature Points 2D (project head to pixel coords) ──
        // Simple pinhole model: fx≈525, fy≈525, cx=320, cy=240
        var points2D = new Vector2[87];
        if (head.Z > 0.1f)
        {
            float fx = 525f, fy = 525f, cx = 320f, cy = 240f;
            float px = cx + head.X * fx / head.Z;
            float py = cy - head.Y * fy / head.Z;
            points2D[69] = new Vector2(px - 15f, py - 8f);
            points2D[73] = new Vector2(px + 15f, py - 8f);
        }

        // ── Face Bounding Rect (approximate from head projection) ──
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

        var points2D = new Vector2[87];
        points2D[69] = new Vector2(px - 15f, py - 8f); // Left pupil
        points2D[73] = new Vector2(px + 15f, py - 8f); // Right pupil

        var points3D = new Vector3[87];
        points3D[69] = new Vector3(-31.5f, 30f, -15f);
        points3D[73] = new Vector3(31.5f, 30f, -15f);

        float halfSize = 0.12f * fx / headZ;
        int faceX = (int)(px - halfSize);
        int faceY = (int)(py - halfSize);
        int faceW = (int)(halfSize * 2);
        int faceH = (int)(halfSize * 2.5f);

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
    private bool TryEmitRealFaceFrame(
        NUI_SKELETON_DATA skel,
        ImmutableDictionary<int, Vector3>.Builder joints,
        int trackingId,
        DateTimeOffset timestamp)
    {
        if (_faceTracker is null || _faceResult is null ||
            _ftVideoImage is null || _ftDepthImage is null)
            return false;

        var colorPixels = _lastColorPixels;
        var depthRaw = _lastDepthRaw;
        if (colorPixels is null || depthRaw is null)
            return false;

        try
        {
            // Free previous pin handles
            if (_colorPinHandle.IsAllocated) _colorPinHandle.Free();
            if (_depthPinHandle.IsAllocated) _depthPinHandle.Free();

            // Pin managed arrays so native code can read them
            _colorPinHandle = GCHandle.Alloc(colorPixels, GCHandleType.Pinned);
            _depthPinHandle = GCHandle.Alloc(depthRaw, GCHandleType.Pinned);

            // Attach color pixels (BGRX 32bpp) to IFTImage
            int hr = _ftVideoImage.Attach(640, 480,
                _colorPinHandle.AddrOfPinnedObject(),
                FTIMAGEFORMAT.UINT8_B8G8R8X8,
                640 * 4);
            if (hr < 0) return false;

            // Attach depth data (D13P3 = 16-bit with 3-bit player index)
            hr = _ftDepthImage.Attach(640, 480,
                _depthPinHandle.AddrOfPinnedObject(),
                FTIMAGEFORMAT.UINT16_D13P3,
                640 * 2);
            if (hr < 0) return false;

            // Build sensor data struct
            var sensorData = new FT_SENSOR_DATA
            {
                pVideoFrame = Marshal.GetIUnknownForObject(_ftVideoImage),
                pDepthFrame = Marshal.GetIUnknownForObject(_ftDepthImage),
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

                // Reset result before use
                _faceResult.Reset();

                if (!_faceTrackingStarted)
                {
                    hr = _faceTracker.StartTracking(ref sensorData, IntPtr.Zero, headPointsPtr, _faceResult);
                    if (hr >= 0 && _faceResult.GetStatus() >= 0)
                        _faceTrackingStarted = true;
                }
                else
                {
                    hr = _faceTracker.ContinueTracking(ref sensorData, headPointsPtr, _faceResult);
                    if (hr < 0 || _faceResult.GetStatus() < 0)
                    {
                        // Lost tracking — try StartTracking again next frame
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

            if (_faceResult.GetStatus() < 0)
                return false;

            // ── Extract face tracking results ──

            // 3D Pose: scale, rotation (pitch/yaw/roll), translation
            float[] rotation = new float[3];
            float[] translation = new float[3];
            hr = _faceResult.Get3DPose(out float scale, rotation, translation);
            if (hr < 0) return false;

            // Face rectangle
            hr = _faceResult.GetFaceRect(out RECT faceRect);
            int faceX = faceRect.Left;
            int faceY = faceRect.Top;
            int faceW = faceRect.Right - faceRect.Left;
            int faceH = faceRect.Bottom - faceRect.Top;

            // Animation Units (6 values)
            float[] actionUnits = new float[6];
            hr = _faceResult.GetAUCoefficients(out IntPtr auPtr, out uint auCount);
            if (hr >= 0 && auPtr != IntPtr.Zero && auCount > 0)
            {
                int copyCount = Math.Min((int)auCount, 6);
                Marshal.Copy(auPtr, actionUnits, 0, copyCount);
            }

            // 2D Shape Points
            Vector2[] points2D = new Vector2[87];
            hr = _faceResult.Get2DShapePoints(out IntPtr pts2DPtr, out uint pts2DCount);
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
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Real face tracking frame error");
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
