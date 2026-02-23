// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
//
// Native COM interop for Kinect v1 Face Tracking via FaceTrackLib.dll.
//
// FaceTrackLib.dll is part of the Kinect Developer Toolkit v1.8.0
// and lives in: %FTSDK_DIR%\Redist\amd64\FaceTrackLib.dll
//
// All definitions are derived from the official header:
//   FaceTrackLib.h (Developer Toolkit v1.8.0\inc)
//
// Interfaces: IFTFaceTracker, IFTResult, IFTImage, IFTModel
// Factory:    FTCreateFaceTracker(), FTCreateImage()
// ──────────────────────────────────────────────────────────────

using System.Runtime.InteropServices;

namespace HCEP.Kinect.Native;

// ── Enums ──────────────────────────────────────────────────────

internal enum FTIMAGEFORMAT
{
    Invalid = 0,
    UINT8_GR8 = 1,            // Grayscale 8bpp
    UINT8_R8G8B8 = 2,         // RGB 24bpp
    UINT8_X8R8G8B8 = 3,       // XRGB 32bpp (alpha unused)
    UINT8_A8R8G8B8 = 4,       // ARGB 32bpp
    UINT8_B8G8R8X8 = 5,       // BGRX 32bpp (alpha unused)
    UINT8_B8G8R8A8 = 6,       // BGRA 32bpp
    UINT16_D16 = 7,            // 16-bit depth (mm)
    UINT16_D13P3 = 8,          // 16-bit depth + 3-bit player index
}

// ── Structs ────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct FT_CAMERA_CONFIG
{
    public uint Width;
    public uint Height;
    public float FocalLength;   // pixels; 0 = use estimated default
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_VECTOR2D
{
    public float x;
    public float y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_VECTOR3D
{
    public float x;
    public float y;
    public float z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_SENSOR_DATA
{
    public IntPtr pVideoFrame;   // IFTImage*
    public IntPtr pDepthFrame;   // IFTImage*
    public float ZoomFactor;
    public int ViewOffsetX;      // POINT.x
    public int ViewOffsetY;      // POINT.y
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_WEIGHTED_RECT
{
    public float Weight;
    public int Left;    // RECT fields
    public int Top;
    public int Right;
    public int Bottom;
}

// ── COM Interface: IFTImage ────────────────────────────────────
//
// MIDL_INTERFACE("1A00A7BC-C217-11E0-AC90-0024811441FD")
// Helper interface for wrapping image buffers.
// Vtable after IUnknown (slots 3+):
//   0: Allocate  1: Attach  2: Reset  3: GetWidth  4: GetHeight
//   5: GetStride  6: GetBytesPerPixel  7: GetBufferSize  8: GetFormat
//   9: GetBuffer  10: IsAttached  11: CopyTo  12: DrawLine

[ComImport]
[Guid("1A00A7BC-C217-11E0-AC90-0024811441FD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFTImage
{
    // ── Slot 0: Allocate ──
    [PreserveSig]
    int Allocate(uint width, uint height, FTIMAGEFORMAT format);

    // ── Slot 1: Attach ──
    [PreserveSig]
    int Attach(uint width, uint height, IntPtr pData, FTIMAGEFORMAT format, uint stride);

    // ── Slot 2: Reset ──
    [PreserveSig]
    int Reset();

    // ── Slot 3: GetWidth ──
    [PreserveSig]
    uint GetWidth();

    // ── Slot 4: GetHeight ──
    [PreserveSig]
    uint GetHeight();

    // ── Slot 5: GetStride ──
    [PreserveSig]
    uint GetStride();

    // ── Slot 6: GetBytesPerPixel ──
    [PreserveSig]
    uint GetBytesPerPixel();

    // ── Slot 7: GetBufferSize ──
    [PreserveSig]
    uint GetBufferSize();

    // ── Slot 8: GetFormat ──
    [PreserveSig]
    FTIMAGEFORMAT GetFormat();

    // ── Slot 9: GetBuffer ──
    [PreserveSig]
    IntPtr GetBuffer();

    // ── Slot 10: IsAttached ──
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.Bool)]
    bool IsAttached();

    // ── Slot 11: CopyTo ──
    [PreserveSig]
    int CopyTo(
        [MarshalAs(UnmanagedType.Interface)] IFTImage pDestImage,
        IntPtr pSrcRect,     // RECT*, NULL = whole image
        uint destRow,
        uint destColumn);

    // ── Slot 12: DrawLine ──
    [PreserveSig]
    int DrawLine(long startPoint, long endPoint, uint color, uint lineWidthPx);
}

// ── COM Interface: IFTResult ───────────────────────────────────
//
// MIDL_INTERFACE("1A00A7BB-C217-11E0-AC90-0024811441FD")
// Represents the result of a face tracking operation.
// Vtable after IUnknown (slots 3+):
//   0: Reset  1: CopyTo  2: GetStatus  3: GetFaceRect
//   4: Get2DShapePoints  5: Get3DPose  6: GetAUCoefficients

[ComImport]
[Guid("1A00A7BB-C217-11E0-AC90-0024811441FD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFTResult
{
    // ── Slot 0: Reset ──
    [PreserveSig]
    int Reset();

    // ── Slot 1: CopyTo ──
    [PreserveSig]
    int CopyTo([MarshalAs(UnmanagedType.Interface)] IFTResult pFTResultDst);

    // ── Slot 2: GetStatus ──
    // Returns S_OK if face is tracked, or FT_ERROR_* codes
    [PreserveSig]
    int GetStatus();

    // ── Slot 3: GetFaceRect ──
    [PreserveSig]
    int GetFaceRect(out RECT pRect);

    // ── Slot 4: Get2DShapePoints ──
    // Returns pointer to internal FT_VECTOR2D array
    [PreserveSig]
    int Get2DShapePoints(out IntPtr ppPoints, out uint pPointCount);

    // ── Slot 5: Get3DPose ──
    [PreserveSig]
    int Get3DPose(
        out float pScale,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] float[] rotationXYZ,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] float[] translationXYZ);

    // ── Slot 6: GetAUCoefficients ──
    // Returns pointer to internal float array of AU coefficients
    [PreserveSig]
    int GetAUCoefficients(out IntPtr ppCoefficients, out uint pAUCount);
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

// ── COM Interface: IFTFaceTracker ──────────────────────────────
//
// MIDL_INTERFACE("1A00A7BA-C217-11E0-AC90-0024811441FD")
// Main face tracking interface.
// Vtable after IUnknown (slots 3+):
//   0: Initialize  1: Reset  2: CreateFTResult  3: SetShapeUnits
//   4: GetShapeUnits  5: SetShapeComputationState  6: GetShapeComputationState
//   7: GetFaceModel  8: StartTracking  9: ContinueTracking  10: DetectFaces

[ComImport]
[Guid("1A00A7BA-C217-11E0-AC90-0024811441FD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFTFaceTracker
{
    // ── Slot 0: Initialize ──
    [PreserveSig]
    int Initialize(
        ref FT_CAMERA_CONFIG pVideoCameraConfig,
        ref FT_CAMERA_CONFIG pDepthCameraConfig,
        IntPtr depthToColorMappingFunc,    // FTRegisterDepthToColor, NULL = use default
        [MarshalAs(UnmanagedType.LPWStr)] string? pszModelPath);

    // ── Slot 1: Reset ──
    [PreserveSig]
    int Reset();

    // ── Slot 2: CreateFTResult ──
    [PreserveSig]
    int CreateFTResult([MarshalAs(UnmanagedType.Interface)] out IFTResult ppFTResult);

    // ── Slot 3: SetShapeUnits ──
    [PreserveSig]
    int SetShapeUnits(float headScale, IntPtr pSUCoefs, uint suCount);

    // ── Slot 4: GetShapeUnits ──
    [PreserveSig]
    int GetShapeUnits(
        out float pHeadScale,
        out IntPtr ppSUCoefs,
        ref uint pSUCount,
        [MarshalAs(UnmanagedType.Bool)] out bool pHaveConverged);

    // ── Slot 5: SetShapeComputationState ──
    [PreserveSig]
    int SetShapeComputationState([MarshalAs(UnmanagedType.Bool)] bool isEnabled);

    // ── Slot 6: GetShapeComputationState ──
    [PreserveSig]
    int GetShapeComputationState([MarshalAs(UnmanagedType.Bool)] out bool pIsEnabled);

    // ── Slot 7: GetFaceModel ──
    [PreserveSig]
    int GetFaceModel(out IntPtr ppModel);

    // ── Slot 8: StartTracking ──
    [PreserveSig]
    int StartTracking(
        ref FT_SENSOR_DATA pSensorData,
        IntPtr pRoi,                       // RECT*, NULL = full frame
        IntPtr headPoints,                 // FT_VECTOR3D[2]: [neck, head], NULL = auto
        [MarshalAs(UnmanagedType.Interface)] IFTResult pFTResult);

    // ── Slot 9: ContinueTracking ──
    [PreserveSig]
    int ContinueTracking(
        ref FT_SENSOR_DATA pSensorData,
        IntPtr headPoints,                 // FT_VECTOR3D[2]: [neck, head], NULL = auto
        [MarshalAs(UnmanagedType.Interface)] IFTResult pFTResult);

    // ── Slot 10: DetectFaces ──
    [PreserveSig]
    int DetectFaces(
        ref FT_SENSOR_DATA pSensorData,
        IntPtr pRoi,
        IntPtr pFaces,
        ref uint pFaceCount);
}

// ── Factory Function Delegates ─────────────────────────────────
//
// These are exported from FaceTrackLib.dll as __stdcall functions.
// We obtain function pointers via NativeLibrary.GetExport().

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr FTCreateFaceTrackerDelegate(IntPtr reserved);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate IntPtr FTCreateImageDelegate();

// ── Native Library Loader ──────────────────────────────────────

internal static class FaceTrackNative
{
    private static IntPtr _ftLibHandle;
    private static FTCreateFaceTrackerDelegate? _createTracker;
    private static FTCreateImageDelegate? _createImage;
    private static bool _loaded;
    private static readonly object _lock = new();

    /// <summary>
    /// Tries to load FaceTrackLib.dll from the Kinect Developer Toolkit.
    /// Returns false if the DLL cannot be found or loaded.
    /// </summary>
    public static bool TryLoad()
    {
        if (_loaded) return _ftLibHandle != IntPtr.Zero;

        lock (_lock)
        {
            if (_loaded) return _ftLibHandle != IntPtr.Zero;
            _loaded = true;

            string? redistPath = FindRedistPath();
            if (redistPath is null) return false;

            string ftLibPath = Path.Combine(redistPath, "FaceTrackLib.dll");
            if (!File.Exists(ftLibPath)) return false;

            // Add the Redist directory so FaceTrackLib can find FaceTrackData.dll
            SetDllDirectoryW(redistPath);

            try
            {
                _ftLibHandle = NativeLibrary.Load(ftLibPath);

                IntPtr createTrackerPtr = NativeLibrary.GetExport(_ftLibHandle, "FTCreateFaceTracker");
                IntPtr createImagePtr = NativeLibrary.GetExport(_ftLibHandle, "FTCreateImage");

                _createTracker = Marshal.GetDelegateForFunctionPointer<FTCreateFaceTrackerDelegate>(createTrackerPtr);
                _createImage = Marshal.GetDelegateForFunctionPointer<FTCreateImageDelegate>(createImagePtr);

                return true;
            }
            catch
            {
                _ftLibHandle = IntPtr.Zero;
                return false;
            }
            finally
            {
                // Restore default DLL search path
                SetDllDirectoryW(null!);
            }
        }
    }

    /// <summary>Creates a new IFTFaceTracker COM instance.</summary>
    public static IFTFaceTracker? CreateFaceTracker()
    {
        if (_createTracker is null) return null;

        IntPtr pTracker = _createTracker(IntPtr.Zero);
        if (pTracker == IntPtr.Zero) return null;

        return (IFTFaceTracker)Marshal.GetObjectForIUnknown(pTracker);
    }

    /// <summary>Creates a new IFTImage COM instance.</summary>
    public static IFTImage? CreateImage()
    {
        if (_createImage is null) return null;

        IntPtr pImage = _createImage();
        if (pImage == IntPtr.Zero) return null;

        return (IFTImage)Marshal.GetObjectForIUnknown(pImage);
    }

    /// <summary>
    /// Finds the Redist\amd64 path containing FaceTrackLib.dll.
    /// Checks FTSDK_DIR env var first, then probes common locations.
    /// </summary>
    private static string? FindRedistPath()
    {
        // 1. Check FTSDK_DIR environment variable (set by Developer Toolkit installer)
        string? ftSdkDir = Environment.GetEnvironmentVariable("FTSDK_DIR");
        if (!string.IsNullOrEmpty(ftSdkDir))
        {
            string redistPath = Path.Combine(ftSdkDir, "Redist", "amd64");
            if (File.Exists(Path.Combine(redistPath, "FaceTrackLib.dll")))
                return redistPath;
        }

        // 2. Probe common Developer Toolkit locations
        string[] probePaths =
        [
            @"C:\Program Files\Microsoft SDKs\Kinect\Developer Toolkit v1.8.0\Redist\amd64",
            @"C:\Program Files (x86)\Microsoft SDKs\Kinect\Developer Toolkit v1.8.0\Redist\amd64",
        ];

        foreach (string path in probePaths)
        {
            if (File.Exists(Path.Combine(path, "FaceTrackLib.dll")))
                return path;
        }

        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string? lpPathName);
}
