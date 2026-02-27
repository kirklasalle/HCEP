// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
//
// Native COM interop for Kinect v1 (Xbox 360) via Kinect10.dll.
//
// This bypasses the managed Microsoft.Kinect.dll entirely, which cannot
// run on .NET 9 due to System.Diagnostics.Eventing.EventDescriptor not
// being type-forwarded from .NET Framework 4's System.Core.
//
// All definitions are derived from the official SDK 1.8 headers:
//   NuiSensor.h, NuiImageCamera.h, NuiSkeleton.h, NuiApi.h
// ──────────────────────────────────────────────────────────────

using System.Runtime.InteropServices;

namespace HCEP.Kinect.Native;

// ── Enums ──────────────────────────────────────────────────────

internal enum NUI_IMAGE_TYPE
{
    DepthAndPlayerIndex = 0,
    Color = 1,
    ColorYuv = 2,
    ColorRawYuv = 3,
    Depth = 4,
    ColorInfrared = 5,
    ColorRawBayer = 6,
}

internal enum NUI_IMAGE_RESOLUTION
{
    Invalid = -1,
    Res80x60 = 0,
    Res320x240 = 1,
    Res640x480 = 2,
    Res1280x960 = 3,
}

internal enum NUI_SKELETON_TRACKING_STATE
{
    NotTracked = 0,
    PositionOnly = 1,
    Tracked = 2,
}

internal enum NUI_SKELETON_POSITION_TRACKING_STATE
{
    NotTracked = 0,
    Inferred = 1,
    Tracked = 2,
}

// ── Structs ────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_VECTOR4
{
    public float x, y, z, w;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_IMAGE_VIEW_AREA
{
    public int eDigitalZoom;
    public int lCenterX;
    public int lCenterY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_IMAGE_FRAME
{
    public long liTimeStamp;
    public uint dwFrameNumber;
    public int eImageType;        // NUI_IMAGE_TYPE
    public int eResolution;       // NUI_IMAGE_RESOLUTION
    public IntPtr pFrameTexture;     // INuiFrameTexture*
    public uint dwFrameFlags;
    public NUI_IMAGE_VIEW_AREA ViewArea;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_LOCKED_RECT
{
    public int Pitch;
    public int size;
    public IntPtr pBits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_SKELETON_DATA
{
    public int eTrackingState;      // NUI_SKELETON_TRACKING_STATE
    public uint dwTrackingID;
    public uint dwEnrollmentIndex;
    public uint dwUserIndex;
    public NUI_VECTOR4 Position;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public NUI_VECTOR4[] SkeletonPositions;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public int[] eSkeletonPositionTrackingState;

    public uint dwQualityFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_SKELETON_FRAME
{
    public long liTimeStamp;
    public uint dwFrameNumber;
    public uint dwFlags;
    public NUI_VECTOR4 vFloorClipPlane;
    public NUI_VECTOR4 vNormalToGravity;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public NUI_SKELETON_DATA[] SkeletonData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NUI_TRANSFORM_SMOOTH_PARAMETERS
{
    public float fSmoothing;
    public float fCorrection;
    public float fPrediction;
    public float fJitterRadius;
    public float fMaxDeviationRadius;
}

// ── Constants ──────────────────────────────────────────────────

internal static class NuiConstants
{
    public const uint NUI_INITIALIZE_FLAG_USES_DEPTH_AND_PLAYER_INDEX = 0x00000001;
    public const uint NUI_INITIALIZE_FLAG_USES_COLOR = 0x00000002;
    public const uint NUI_INITIALIZE_FLAG_USES_SKELETON = 0x00000008;
    public const uint NUI_INITIALIZE_FLAG_USES_DEPTH = 0x00000020;

    // Skeleton tracking flags (for NuiSkeletonTrackingEnable)
    public const uint NUI_SKELETON_TRACKING_FLAG_SUPPRESS_NO_FRAME_DATA = 0x00000001;
    public const uint NUI_SKELETON_TRACKING_FLAG_ENABLE_SEATED_SUPPORT = 0x00000004;
    public const uint NUI_SKELETON_TRACKING_FLAG_ENABLE_IN_NEAR_RANGE = 0x00000008;

    // Image stream flags (for NuiImageStreamOpen dwImageFrameFlags)
    // SAFETY: Tilt motor commands (NuiCameraElevationSetAngle auto-centering) are DISABLED
    //         per hardware safety directive 2026-02-27. ElevationAngle setter remains for
    //         manual UI use only; no automated motor movement is implemented.
    public const uint NUI_IMAGE_STREAM_FLAG_ENABLE_NEAR_MODE = 0x00020000; // extends range to ~40cm

    public const int NUI_IMAGE_PLAYER_INDEX_SHIFT = 3;

    public const int NUI_SKELETON_COUNT = 6;
    public const int NUI_SKELETON_POSITION_COUNT = 20;

    // HRESULT codes
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NUI_FRAME_NO_DATA = unchecked((int)0x83010001);

    // Struct sizes for manual marshaling (verified against native SDK headers)
    // NUI_VECTOR4:       4*4 = 16
    // NUI_SKELETON_DATA: 4+4+4+4+16+320+80+4 = 436
    // NUI_SKELETON_FRAME: 8+4+4+16+16+6*436 = 2664
    public static readonly int SizeOfSkeletonFrame = Marshal.SizeOf<NUI_SKELETON_FRAME>();
}

// ── COM Interface: INuiFrameTexture ────────────────────────────
//
// MIDL_INTERFACE("13ea17f5-ff2e-4670-9ee5-1297a6e880d1")
// Vtable after IUnknown: BufferLen, Pitch, LockRect, GetLevelDesc, UnlockRect

[ComImport]
[Guid("13ea17f5-ff2e-4670-9ee5-1297a6e880d1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INuiFrameTexture
{
    [PreserveSig] int BufferLen();
    [PreserveSig] int Pitch();

    [PreserveSig]
    int LockRect(
        uint Level,
        out NUI_LOCKED_RECT pLockedRect,
        IntPtr pRect,   // NULL = entire surface
        uint Flags);

    [PreserveSig]
    int GetLevelDesc(uint Level, IntPtr pDesc);

    [PreserveSig]
    int UnlockRect(uint Level);
}

// ── COM Interface: INuiSensor ──────────────────────────────────
//
// MIDL_INTERFACE("d3d9ab7b-31ba-44ca-8cc0-d42525bbea43")
// Full vtable after IUnknown — all 33 methods in exact order.
//
// Methods we actively use are documented; placeholders maintain vtable alignment.

[ComImport]
[Guid("d3d9ab7b-31ba-44ca-8cc0-d42525bbea43")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INuiSensor
{
    // ── Slot 0: NuiInitialize ──
    [PreserveSig]
    int NuiInitialize(uint dwFlags);

    // ── Slot 1: NuiShutdown ──
    [PreserveSig]
    void NuiShutdown();

    // ── Slot 2: NuiSetFrameEndEvent ──
    [PreserveSig]
    int NuiSetFrameEndEvent(IntPtr hEvent, uint dwFrameEventFlag);

    // ── Slot 3: NuiImageStreamOpen ──
    [PreserveSig]
    int NuiImageStreamOpen(
        NUI_IMAGE_TYPE eImageType,
        NUI_IMAGE_RESOLUTION eResolution,
        uint dwImageFrameFlags,
        uint dwFrameLimit,
        IntPtr hNextFrameEvent,
        out IntPtr phStreamHandle);

    // ── Slot 4: NuiImageStreamSetImageFrameFlags ──
    [PreserveSig]
    int NuiImageStreamSetImageFrameFlags(IntPtr hStream, uint dwImageFrameFlags);

    // ── Slot 5: NuiImageStreamGetImageFrameFlags ──
    [PreserveSig]
    int NuiImageStreamGetImageFrameFlags(IntPtr hStream, out uint pdwImageFrameFlags);

    // ── Slot 6: NuiImageStreamGetNextFrame ──
    [PreserveSig]
    int NuiImageStreamGetNextFrame(
        IntPtr hStream,
        uint dwMillisecondsToWait,
        out NUI_IMAGE_FRAME pImageFrame);

    // ── Slot 7: NuiImageStreamReleaseFrame ──
    [PreserveSig]
    int NuiImageStreamReleaseFrame(IntPtr hStream, ref NUI_IMAGE_FRAME pImageFrame);

    // ── Slot 8: NuiImageGetColorPixelCoordinatesFromDepthPixel ──
    [PreserveSig]
    int NuiImageGetColorPixelCoordinatesFromDepthPixel(
        NUI_IMAGE_RESOLUTION eColorResolution,
        ref NUI_IMAGE_VIEW_AREA pcViewArea,
        int lDepthX, int lDepthY, ushort usDepthValue,
        out int plColorX, out int plColorY);

    // ── Slot 9: NuiImageGetColorPixelCoordinatesFromDepthPixelAtResolution ──
    [PreserveSig]
    int NuiImageGetColorPixelCoordinatesFromDepthPixelAtResolution(
        NUI_IMAGE_RESOLUTION eColorResolution,
        NUI_IMAGE_RESOLUTION eDepthResolution,
        ref NUI_IMAGE_VIEW_AREA pcViewArea,
        int lDepthX, int lDepthY, ushort usDepthValue,
        out int plColorX, out int plColorY);

    // ── Slot 10: NuiImageGetColorPixelCoordinateFrameFromDepthPixelFrameAtResolution ──
    [PreserveSig]
    int NuiImageGetColorPixelCoordinateFrameFromDepthPixelFrameAtResolution(
        NUI_IMAGE_RESOLUTION eColorResolution,
        NUI_IMAGE_RESOLUTION eDepthResolution,
        uint cDepthValues, IntPtr pDepthValues,
        uint cColorCoordinates, IntPtr pColorCoordinates);

    // ── Slot 11: NuiCameraElevationSetAngle ──
    [PreserveSig]
    int NuiCameraElevationSetAngle(int lAngleDegrees);

    // ── Slot 12: NuiCameraElevationGetAngle ──
    [PreserveSig]
    int NuiCameraElevationGetAngle(out int plAngleDegrees);

    // ── Slot 13: NuiSkeletonTrackingEnable ──
    [PreserveSig]
    int NuiSkeletonTrackingEnable(IntPtr hNextFrameEvent, uint dwFlags);

    // ── Slot 14: NuiSkeletonTrackingDisable ──
    [PreserveSig]
    int NuiSkeletonTrackingDisable();

    // ── Slot 15: NuiSkeletonSetTrackedSkeletons ──
    [PreserveSig]
    int NuiSkeletonSetTrackedSkeletons(IntPtr TrackingIDs);

    // ── Slot 16: NuiSkeletonGetNextFrame ──
    // Uses IntPtr instead of ref NUI_SKELETON_FRAME to avoid COM marshaling
    // issues with large nested ByValArray structs on .NET 9.
    [PreserveSig]
    int NuiSkeletonGetNextFrame(
        uint dwMillisecondsToWait,
        IntPtr pSkeletonFrame);

    // ── Slot 17: NuiTransformSmooth ──
    [PreserveSig]
    int NuiTransformSmooth(IntPtr pSkeletonFrame, IntPtr pSmoothingParams);

    // ── Slot 18: NuiGetAudioSource ──
    [PreserveSig]
    int NuiGetAudioSource(out IntPtr ppDmo);

    // ── Slot 19: NuiInstanceIndex ──
    [PreserveSig]
    int NuiInstanceIndex();

    // ── Slot 20: NuiDeviceConnectionId ──
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.BStr)]
    string NuiDeviceConnectionId();

    // ── Slot 21: NuiUniqueId ──
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.BStr)]
    string NuiUniqueId();

    // ── Slot 22: NuiAudioArrayId ──
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.BStr)]
    string NuiAudioArrayId();

    // ── Slot 23: NuiStatus ──
    [PreserveSig]
    int NuiStatus();

    // ── Slot 24: NuiInitializationFlags ──
    [PreserveSig]
    uint NuiInitializationFlags();

    // ── Slot 25: NuiGetCoordinateMapper ──
    [PreserveSig]
    int NuiGetCoordinateMapper(out IntPtr pMapping);

    // ── Slot 26: NuiImageFrameGetDepthImagePixelFrameTexture ──
    [PreserveSig]
    int NuiImageFrameGetDepthImagePixelFrameTexture(
        IntPtr hStream,
        ref NUI_IMAGE_FRAME pImageFrame,
        out int pNearMode,
        out IntPtr ppFrameTexture);

    // ── Slot 27: NuiGetColorCameraSettings ──
    [PreserveSig]
    int NuiGetColorCameraSettings(out IntPtr pCameraSettings);

    // ── Slot 28: NuiGetForceInfraredEmitterOff ──
    [PreserveSig]
    int NuiGetForceInfraredEmitterOff();

    // ── Slot 29: NuiSetForceInfraredEmitterOff ──
    [PreserveSig]
    int NuiSetForceInfraredEmitterOff(int fForceInfraredEmitterOff);

    // ── Slot 30: NuiAccelerometerGetCurrentReading ──
    [PreserveSig]
    int NuiAccelerometerGetCurrentReading(out NUI_VECTOR4 pReading);

    // ── Slot 31: NuiSetDepthFilter ──
    [PreserveSig]
    int NuiSetDepthFilter(IntPtr pDepthFilter);

    // ── Slot 32: NuiGetDepthFilter ──
    [PreserveSig]
    int NuiGetDepthFilter(out IntPtr ppDepthFilter);

    // ── Slot 33: NuiGetDepthFilterForTimeStamp ──
    [PreserveSig]
    int NuiGetDepthFilterForTimeStamp(long liTimeStamp, out IntPtr ppDepthFilter);
}

// ── P/Invoke Exports from Kinect10.dll ─────────────────────────

internal static class KinectNative
{
    private const string Dll = "Kinect10.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int NuiGetSensorCount(out int pCount);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern int NuiCreateSensorByIndex(
        int index,
        [MarshalAs(UnmanagedType.Interface)]
        out INuiSensor ppNuiSensor);
}
