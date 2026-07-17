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
using System.Runtime.InteropServices;
using HCEP.Core.Models;
using HCEP.Kinect.Native;
using Microsoft.Extensions.Logging;

namespace HCEP.Kinect;

public sealed partial class KinectSensorSource
{
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

    // Face model mesh (from FaceTrackLib SDK — like FaceTrackingBasics-WPF sample)
    private IFTModel? _faceModel;
    private (int First, int Second, int Third)[]? _cachedTriangles;
    private Vector2[]? _cachedNeutralVertices;   // last successful neutral mesh (fallback for failed frames)
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

    // One-shot flag so we only log the very first early-return reason once
    private bool _realFaceFirstBailLogged;

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
        catch (DllNotFoundException ex)
        {
            _logger.LogWarning(ex, "FaceTrackLib.dll not found (Kinect SDK not installed) — using skeleton-approximate face tracking");
            DisposeFaceTracking();
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
            Vector2[]? neutralMeshVertices = null;
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

                                // Project neutral face model shape (no expressions, no head pose)
                                // Use actual tracked scale and Z-distance for consistent sizing.
                                // Only zero out rotation and AUs — face stays front-facing
                                // but at the same screen scale as the live mesh.
                                IntPtr neutralVertBuf = Marshal.AllocHGlobal(bufSize);
                                try
                                {
                                    var neutralRotVec = new FT_VECTOR3D { x = 0, y = 0, z = 0 };
                                    var neutralTransVec = new FT_VECTOR3D { x = 0, y = 0, z = translation[2] };
                                    int hrNeutral = _faceModel.GetProjectedShape(
                                        ref _videoConfig,
                                        1.0f,
                                        viewOffset,
                                        suToPass, suPassCount,
                                        IntPtr.Zero, 0, // no AUs (neutral expression)
                                        scale, // use actual tracked scale for consistent sizing
                                        ref neutralRotVec,
                                        ref neutralTransVec,
                                        neutralVertBuf,
                                        _meshVertexCount);

                                    if (hrNeutral >= 0)
                                    {
                                        neutralMeshVertices = new Vector2[_meshVertexCount];
                                        for (int i = 0; i < (int)_meshVertexCount; i++)
                                        {
                                            IntPtr p = neutralVertBuf + i * 8;
                                            float vx = Marshal.PtrToStructure<float>(p);
                                            float vy = Marshal.PtrToStructure<float>(p + 4);
                                            neutralMeshVertices[i] = new Vector2(vx, vy);
                                        }
                                        // Cache the last successful neutral mesh for fallback
                                        _cachedNeutralVertices = neutralMeshVertices;
                                    }
                                    else
                                    {
                                        // Use cached neutral mesh if this frame's projection failed
                                        neutralMeshVertices = _cachedNeutralVertices;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "Failed to project neutral face shape");
                                    neutralMeshVertices = _cachedNeutralVertices;
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal(neutralVertBuf);
                                }

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
                NeutralFaceMeshVertices2D = neutralMeshVertices,
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
}
