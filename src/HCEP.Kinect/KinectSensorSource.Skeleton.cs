// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using HCEP.Core.Enums;
using HCEP.Core.Models;
using HCEP.Kinect.Native;
using Microsoft.Extensions.Logging;

namespace HCEP.Kinect;

public sealed partial class KinectSensorSource
{
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
}
