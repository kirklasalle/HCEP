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
using System.Collections.Immutable;
using System.Numerics;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.Kinect;

/// <summary>
/// Simulated sensor source for development/testing without a physical Kinect.
/// Generates synthetic skeleton, face, and audio frames at ~30 FPS.
/// Cycles through 5 gaze scenarios to exercise all HCEP modes:
///   Logic → Affect → Spirit → Heart → Think (5 seconds each, 25-second cycle).
/// </summary>
public sealed class SimulatedSensorSource : ISensorSource
{
    private readonly ILogger<SimulatedSensorSource> _logger;
    private volatile SensorState _state = SensorState.Disconnected;
    private CancellationTokenSource? _cts;
    private Task? _generatorTask;

    /// <summary>Frames per scenario cycle (~5 seconds at 30 FPS).</summary>
    private const int FramesPerScenario = 150;
    /// <summary>Total scenarios in one full cycle.</summary>
    private const int ScenarioCount = 5;

    public SimulatedSensorSource(ILogger<SimulatedSensorSource> logger)
    {
        _logger = logger;
    }

    public SensorState State => _state;

    public event Action<SkeletonFrame>? SkeletonFrameReady;
    public event Action<FaceFrame>? FaceFrameReady;
    public event Action<ColorFrame>? ColorFrameReady;
    public event Action<DepthFrame>? DepthFrameReady;
    public event Action<ColorFrame>? InfraredFrameReady;
    public event Action<AudioFrame>? AudioFrameReady;
    public event Action<SensorState>? StateChanged;

    public int ElevationAngle { get; set; }
    public bool SeatedMode { get; set; }

    public Task InitializeAsync(SensorStreamType streams, CancellationToken ct = default)
    {
        SetState(SensorState.Connected);
        _logger.LogInformation("Simulated sensor initialized (streams: {Streams})", streams);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _generatorTask = GenerateFramesAsync(_cts.Token);
        _logger.LogInformation("Simulated sensor streaming started — cycling through 5 HCEP scenarios");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        if (_generatorTask is not null)
        {
            try { await _generatorTask; }
            catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
        _cts = null;
        SetState(SensorState.Disconnected);
        _logger.LogInformation("Simulated sensor stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    // ── Frame Generation ───────────────────────────────────────

    private async Task GenerateFramesAsync(CancellationToken ct)
    {
        int frameNumber = 0;

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            frameNumber++;

            // Determine which HCEP scenario to simulate
            var (yaw, pitch, actionUnits, scenarioName) = GetScenario(frameNumber);

            // Gentle body sway independent of gaze scenario
            float sway = MathF.Sin(frameNumber * 0.02f) * 0.05f;

            // ── Skeleton ───────────────────────────────────────
            var joints = ImmutableDictionary.CreateBuilder<int, Vector3>();
            joints[3] = new Vector3(sway, 0.2f, 2.0f);          // Head
            joints[2] = new Vector3(sway, 0.0f, 2.0f);          // ShoulderCenter
            joints[0] = new Vector3(sway, -0.5f, 2.0f);         // HipCenter

            SkeletonFrameReady?.Invoke(new SkeletonFrame
            {
                Timestamp = now,
                TrackingId = 1,
                State = TrackingState.Tracked,
                Position = new Vector3(sway, -0.1f, 2.0f),
                Joints = joints.ToImmutable(),
            });

            // ── Face ───────────────────────────────────────────
            var featurePoints3D = new Vector3[87];
            featurePoints3D[69] = new Vector3(-31.5f, 30f, -15f);  // left pupil (mm)
            featurePoints3D[73] = new Vector3(31.5f, 30f, -15f);   // right pupil (mm)

            FaceFrameReady?.Invoke(new FaceFrame
            {
                Timestamp = now,
                TrackingId = 1,
                IsTracked = true,
                ActionUnits = actionUnits,
                FeaturePoints3D = featurePoints3D,
                FeaturePoints2D = new Vector2[87],
                HeadRotation = new Vector3(pitch, yaw, 0),
                HeadTranslation = new Vector3(sway * 100, 200, 2000),
                FaceRect = (280, 180, 80, 100),
            });

            // ── Color (synthetic depth-style camera view) ──────
            var colorData = RenderSyntheticColorFrame(frameNumber, sway, yaw, pitch, scenarioName);
            ColorFrameReady?.Invoke(new ColorFrame
            {
                Timestamp = now,
                PixelData = colorData,
                Width = 640,
                Height = 480,
                BytesPerPixel = 4,
                FrameNumber = frameNumber,
            });

            // ── Depth (synthetic depth map) ────────────────────
            var depthData = RenderSyntheticDepthFrame(frameNumber, sway);
            DepthFrameReady?.Invoke(new DepthFrame
            {
                Timestamp = now,
                DepthData = depthData,
                Width = 640,
                Height = 480,
                MinDepthMm = 800,
                MaxDepthMm = 4000,
                FrameNumber = frameNumber,
            });

            // ── Infrared (synthetic IR grayscale) ──────────────
            var irData = RenderSyntheticIRFrame(frameNumber, sway);
            InfraredFrameReady?.Invoke(new ColorFrame
            {
                Timestamp = now,
                PixelData = irData,
                Width = 640,
                Height = 480,
                BytesPerPixel = 4,
                FrameNumber = frameNumber,
            });

            // ── Audio (silent PCM, every other frame ≈ 15 fps) ─
            if (frameNumber % 2 == 0)
            {
                AudioFrameReady?.Invoke(new AudioFrame
                {
                    Timestamp = now,
                    PcmData = new byte[3200], // 100ms of silence at 16 kHz, 16-bit mono
                    ByteCount = 3200,
                    SampleRate = 16000,
                    BitsPerSample = 16,
                    Channels = 1,
                    BeamAngleDeg = 0,
                    SourceAngleDeg = 0,
                    SourceConfidence = 0.0,
                });
            }

            // Log scenario change at the first frame of each new scenario
            if ((frameNumber - 1) % FramesPerScenario == 0)
            {
                _logger.LogDebug("Simulation scenario: {Scenario} (yaw={Yaw:F1}°, pitch={Pitch:F1}°)",
                    scenarioName, yaw, pitch);
            }

            // ~30 FPS
            try { await Task.Delay(33, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── Scenario Definitions ───────────────────────────────────

    /// <summary>
    /// Returns (yaw, pitch, actionUnits, scenarioName) for the current frame.
    /// Each scenario runs for <see cref="FramesPerScenario"/> frames (~5 sec).
    /// The head rotations are calibrated against the interlocutor landmark
    /// positions used by <see cref="Spatial.ThreeStageGazeEstimator"/>:
    ///   FaceCenter = (  0,     0, 1m)
    ///   LeftEye    = (-32mm,   0, 1m)
    ///   Mouth      = (  0, -50mm, 1m)
    /// </summary>
    private static (float Yaw, float Pitch, float[] AUs, string Name) GetScenario(int frame)
    {
        int cycleFrame = frame % (FramesPerScenario * ScenarioCount);
        int scenario = cycleFrame / FramesPerScenario;
        float t = (cycleFrame % FramesPerScenario) / (float)FramesPerScenario; // 0..1

        return scenario switch
        {
            // ── LOGIC: Structured on-face gaze at face center, neutral AUs ──
            0 => (
                Yaw: MathF.Sin(t * MathF.PI * 2) * 0.3f,   // subtle drift ±0.3°
                Pitch: 1.7f + MathF.Sin(t * MathF.PI) * 0.2f,  // centered, tiny nod
                AUs: [0.1f, 0f, 0.05f, -0.1f, 0f, 0.02f],  // neutral → Engaged
                Name: "LOGIC"
            ),

            // ── AFFECT: Social Triangle (oscillate between eyes & mouth) ────
            1 => (
                Yaw: MathF.Sin(t * MathF.PI * 6) * 1.5f,    // ±1.5° yaw → eyes
                Pitch: 1.7f + MathF.Sin(t * MathF.PI * 3) * 3f, // sweeps toward mouth
                AUs: [0.1f, 0f, 0.35f, -0.1f, 0f, 0.02f],  // lipStretch→positive valence
                Name: "AFFECT"
            ),

            // ── SPIRIT: Sustained precise left-eye gaze, high confidence ────
            2 => (
                Yaw: -1.78f + MathF.Sin(t * MathF.PI * 2) * 0.1f, // locked on LeftEye
                Pitch: 1.7f + MathF.Sin(t * MathF.PI) * 0.1f,
                AUs: [0.1f, 0f, 0.1f, -0.1f, 0f, 0.02f],   // neutral, soft gaze
                Name: "SPIRIT"
            ),

            // ── HEART: Mouth/chin attention + positive valence ──────────────
            3 => (
                Yaw: MathF.Sin(t * MathF.PI * 2) * 0.2f,    // nearly straight
                Pitch: 4.5f + MathF.Sin(t * MathF.PI) * 0.5f,   // looking at mouth
                AUs: [0.1f, 0f, 0.40f, -0.1f, 0f, 0.02f],  // clear smile → positive
                Name: "HEART"
            ),

            // ── THINK: Gaze aversion, looking away (upper-right) ────────────
            4 => (
                Yaw: 20f + MathF.Sin(t * MathF.PI * 2) * 5f,   // far right
                Pitch: -3f + MathF.Sin(t * MathF.PI) * 2f,       // slightly upward
                AUs: [0.1f, 0f, 0.05f, -0.1f, 0f, 0.02f],  // neutral face
                Name: "THINK"
            ),

            _ => (0f, 1.7f, [0.1f, 0f, 0.05f, -0.1f, 0f, 0.02f], "UNKNOWN"),
        };
    }

    // ── Synthetic Color Frame Renderer ──────────────────────

    /// <summary>
    /// Renders a 640×480 BGRA32 frame simulating a depth/IR camera view.
    /// Shows a person silhouette with face region, skeleton wireframe,
    /// head pose arrow, and scenario HUD text.
    /// </summary>
    private static byte[] RenderSyntheticColorFrame(
        int frame, float sway, float yaw, float pitch, string scenario)
    {
        const int W = 640, H = 480, Bpp = 4;
        var buf = new byte[W * H * Bpp];

        // Dark gradient background (simulating depth camera)
        for (int y = 0; y < H; y++)
        {
            byte bg = (byte)(12 + y * 8 / H);
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * Bpp;
                buf[i] = bg;           // B
                buf[i + 1] = (byte)(bg + 2); // G
                buf[i + 2] = (byte)(bg + 4); // R
                buf[i + 3] = 255;      // A
            }
        }

        // Person center (shifts with sway)
        int cx = W / 2 + (int)(sway * 300);
        int headY = 160;
        int shoulderY = 220;
        int hipY = 340;

        // Body silhouette (filled ellipses with depth-camera green tint)
        FillEllipse(buf, W, H, cx, (shoulderY + hipY) / 2, 55, 80, 25, 55, 20);
        FillEllipse(buf, W, H, cx, headY, 35, 40, 30, 65, 25);

        // "Infrared" face region
        int faceTop = headY - 30;
        int faceBot = headY + 35;
        int faceLeft = cx - 28;
        int faceRight = cx + 28;
        DrawRect(buf, W, H, faceLeft, faceTop, faceRight, faceBot, 80, 180, 80);

        // Eye dots
        int eyeY = headY - 5;
        FillCircle(buf, W, H, cx - 12, eyeY, 4, 120, 220, 120);
        FillCircle(buf, W, H, cx + 12, eyeY, 4, 120, 220, 120);

        // Nose
        SetPixelSafe(buf, W, H, cx, headY + 5, 80, 180, 80);
        SetPixelSafe(buf, W, H, cx, headY + 6, 80, 180, 80);

        // Mouth
        for (int dx = -8; dx <= 8; dx++)
            SetPixelSafe(buf, W, H, cx + dx, headY + 15, 80, 180, 80);

        // Skeleton wireframe (bright green)
        DrawLine(buf, W, H, cx, headY, cx, shoulderY, 50, 200, 50);       // neck
        DrawLine(buf, W, H, cx, shoulderY, cx - 60, shoulderY + 40, 50, 200, 50); // L arm
        DrawLine(buf, W, H, cx, shoulderY, cx + 60, shoulderY + 40, 50, 200, 50); // R arm
        DrawLine(buf, W, H, cx, shoulderY, cx, hipY, 50, 200, 50);         // spine
        DrawLine(buf, W, H, cx, hipY, cx - 30, hipY + 80, 50, 200, 50);   // L leg
        DrawLine(buf, W, H, cx, hipY, cx + 30, hipY + 80, 50, 200, 50);   // R leg

        // Joint dots
        foreach (var (jx, jy) in new[] {
            (cx, headY), (cx, shoulderY), (cx - 60, shoulderY + 40),
            (cx + 60, shoulderY + 40), (cx, hipY),
            (cx - 30, hipY + 80), (cx + 30, hipY + 80) })
        {
            FillCircle(buf, W, H, jx, jy, 3, 100, 255, 100);
        }

        // Head pose arrow (shows gaze direction)
        int arrowLen = 40;
        float yawRad = yaw * MathF.PI / 180f;
        float pitchRad = pitch * MathF.PI / 180f;
        int ax = cx + (int)(MathF.Sin(yawRad) * arrowLen);
        int ay = headY + (int)(MathF.Sin(pitchRad) * arrowLen);
        DrawLine(buf, W, H, cx, headY, ax, ay, 0, 180, 220);
        FillCircle(buf, W, H, ax, ay, 3, 6, 182, 212);

        // Face bounding box label
        DrawRect(buf, W, H, faceLeft - 2, faceTop - 2, faceRight + 2, faceBot + 2, 50, 130, 50);

        // HUD: Scenario label at top-left (simple pixel font)
        DrawHudText(buf, W, H, 8, 8, $"SIM: {scenario}", 6, 182, 212);
        DrawHudText(buf, W, H, 8, 22, $"YAW:{yaw:F1} PITCH:{pitch:F1}", 100, 160, 100);
        DrawHudText(buf, W, H, 8, 36, $"FRAME:{frame}", 80, 120, 80);

        // Scan line effect (every 4th line slightly brighter)
        for (int y = 0; y < H; y += 4)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * Bpp;
                buf[i] = (byte)Math.Min(255, buf[i] + 3);
                buf[i + 1] = (byte)Math.Min(255, buf[i + 1] + 3);
                buf[i + 2] = (byte)Math.Min(255, buf[i + 2] + 3);
            }

        return buf;
    }

    // ── Pixel Drawing Helpers ──────────────────────────────────

    private static void SetPixelSafe(byte[] buf, int w, int h, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        int i = (y * w + x) * 4;
        buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255;
    }

    private static void FillCircle(byte[] buf, int w, int h, int cx, int cy, int r, byte cr, byte cg, byte cb)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    SetPixelSafe(buf, w, h, cx + dx, cy + dy, cr, cg, cb);
    }

    private static void FillEllipse(byte[] buf, int w, int h, int cx, int cy, int rx, int ry,
        byte cr, byte cg, byte cb)
    {
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
                if ((float)(dx * dx) / (rx * rx) + (float)(dy * dy) / (ry * ry) <= 1f)
                    SetPixelSafe(buf, w, h, cx + dx, cy + dy, cr, cg, cb);
    }

    private static void DrawRect(byte[] buf, int w, int h, int x0, int y0, int x1, int y1,
        byte cr, byte cg, byte cb)
    {
        for (int x = x0; x <= x1; x++) { SetPixelSafe(buf, w, h, x, y0, cr, cg, cb); SetPixelSafe(buf, w, h, x, y1, cr, cg, cb); }
        for (int y = y0; y <= y1; y++) { SetPixelSafe(buf, w, h, x0, y, cr, cg, cb); SetPixelSafe(buf, w, h, x1, y, cr, cg, cb); }
    }

    private static void DrawLine(byte[] buf, int w, int h, int x0, int y0, int x1, int y1,
        byte cr, byte cg, byte cb)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            SetPixelSafe(buf, w, h, x0, y0, cr, cg, cb);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Simple 3×5 pixel font for HUD text.</summary>
    private static void DrawHudText(byte[] buf, int w, int h, int startX, int startY,
        string text, byte cr, byte cg, byte cb)
    {
        int x = startX;
        foreach (char c in text.ToUpperInvariant())
        {
            if (c == ' ') { x += 4; continue; }
            var glyph = GetGlyph(c);
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if (((glyph >> (14 - row * 3 - col)) & 1) == 1)
                        SetPixelSafe(buf, w, h, x + col, startY + row, cr, cg, cb);
            x += 4;
        }
    }

    private static int GetGlyph(char c) => c switch
    {
        'A' => 0b_010_101_111_101_101,
        'B' => 0b_110_101_110_101_110,
        'C' => 0b_011_100_100_100_011,
        'D' => 0b_110_101_101_101_110,
        'E' => 0b_111_100_110_100_111,
        'F' => 0b_111_100_110_100_100,
        'G' => 0b_011_100_101_101_011,
        'H' => 0b_101_101_111_101_101,
        'I' => 0b_111_010_010_010_111,
        'J' => 0b_001_001_001_101_010,
        'K' => 0b_101_110_100_110_101,
        'L' => 0b_100_100_100_100_111,
        'M' => 0b_101_111_111_101_101,
        'N' => 0b_101_111_111_111_101,
        'O' => 0b_010_101_101_101_010,
        'P' => 0b_110_101_110_100_100,
        'Q' => 0b_010_101_101_011_001,
        'R' => 0b_110_101_110_101_101,
        'S' => 0b_011_100_010_001_110,
        'T' => 0b_111_010_010_010_010,
        'U' => 0b_101_101_101_101_010,
        'V' => 0b_101_101_101_010_010,
        'W' => 0b_101_101_111_111_101,
        'X' => 0b_101_101_010_101_101,
        'Y' => 0b_101_101_010_010_010,
        'Z' => 0b_111_001_010_100_111,
        '0' => 0b_010_101_101_101_010,
        '1' => 0b_010_110_010_010_111,
        '2' => 0b_110_001_010_100_111,
        '3' => 0b_110_001_010_001_110,
        '4' => 0b_101_101_111_001_001,
        '5' => 0b_111_100_110_001_110,
        '6' => 0b_011_100_111_101_010,
        '7' => 0b_111_001_010_010_010,
        '8' => 0b_010_101_010_101_010,
        '9' => 0b_010_101_011_001_110,
        ':' => 0b_000_010_000_010_000,
        '.' => 0b_000_000_000_000_010,
        '-' => 0b_000_000_111_000_000,
        _ => 0b_000_000_000_000_000,
    };

    // ── Synthetic Depth Frame Renderer ────────────────────

    /// <summary>
    /// Renders a 640×480 depth map (short[] in millimeters) simulating a
    /// person standing ~2 m from the sensor against a room background at ~3.5 m.
    /// </summary>
    private static short[] RenderSyntheticDepthFrame(int frame, float sway)
    {
        const int W = 640, H = 480;
        const short BgDepth = 3500; // room wall at 3.5 m
        const short PersonDepth = 2000; // person at 2 m

        var buf = new short[W * H];

        // Fill background
        Array.Fill(buf, BgDepth);

        // Person center (shifts with sway)
        int cx = W / 2 + (int)(sway * 300);
        int headY = 160;
        int shoulderY = 220;
        int hipY = 340;
        float breathe = 1.0f + MathF.Sin(frame * 0.05f) * 0.02f;

        // Head ellipse — closer to camera
        int headRx = (int)(35 * breathe), headRy = (int)(40 * breathe);
        for (int dy = -headRy; dy <= headRy; dy++)
            for (int dx = -headRx; dx <= headRx; dx++)
                if ((float)(dx * dx) / (headRx * headRx) + (float)(dy * dy) / (headRy * headRy) <= 1f)
                {
                    int x = cx + dx, y = headY + dy;
                    if (x >= 0 && x < W && y >= 0 && y < H)
                    {
                        // Depth varies slightly across face (convex surface)
                        float r = MathF.Sqrt((float)(dx * dx) / (headRx * headRx)
                                              + (float)(dy * dy) / (headRy * headRy));
                        buf[y * W + x] = (short)(PersonDepth - 80 + (int)(r * 80));
                    }
                }

        // Torso ellipse
        int torsoRx = (int)(55 * breathe), torsoRy = 80;
        int torsoCenter = (shoulderY + hipY) / 2;
        for (int dy = -torsoRy; dy <= torsoRy; dy++)
            for (int dx = -torsoRx; dx <= torsoRx; dx++)
                if ((float)(dx * dx) / (torsoRx * torsoRx) + (float)(dy * dy) / (torsoRy * torsoRy) <= 1f)
                {
                    int x = cx + dx, y = torsoCenter + dy;
                    if (x >= 0 && x < W && y >= 0 && y < H)
                    {
                        float r = MathF.Sqrt((float)(dx * dx) / (torsoRx * torsoRx)
                                              + (float)(dy * dy) / (torsoRy * torsoRy));
                        buf[y * W + x] = (short)(PersonDepth + (int)(r * 120));
                    }
                }

        // Arms
        SetDepthLine(buf, W, H, cx - 60, shoulderY + 40, cx, shoulderY, PersonDepth + 50, 8);
        SetDepthLine(buf, W, H, cx + 60, shoulderY + 40, cx, shoulderY, PersonDepth + 50, 8);

        // Legs
        SetDepthLine(buf, W, H, cx - 30, hipY + 80, cx, hipY, PersonDepth + 30, 6);
        SetDepthLine(buf, W, H, cx + 30, hipY + 80, cx, hipY, PersonDepth + 30, 6);

        // Floor gradient (near bottom of frame)
        for (int y = hipY + 100; y < H; y++)
        {
            short floorDepth = (short)(2500 + (y - hipY - 100) * 3);
            for (int x = 0; x < W; x++)
                if (buf[y * W + x] == BgDepth)
                    buf[y * W + x] = floorDepth;
        }

        return buf;
    }

    /// <summary>
    /// Draws a thick depth line (limb) between two points.
    /// </summary>
    private static void SetDepthLine(short[] buf, int w, int h,
        int x0, int y0, int x1, int y1, short depth, int thickness)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                {
                    int px = x0 + tx, py = y0 + ty;
                    if (px >= 0 && px < w && py >= 0 && py < h)
                        buf[py * w + px] = depth;
                }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    // ── Synthetic Infrared Frame Renderer ───────────────────

    /// <summary>
    /// Renders a 640×480 BGRA32 frame simulating a Kinect infrared camera view.
    /// IR images show specular reflections on skin, bright retro-reflective eyes,
    /// and visible structured light dot pattern.
    /// </summary>
    private static byte[] RenderSyntheticIRFrame(int frame, float sway)
    {
        const int W = 640, H = 480, Bpp = 4;
        var buf = new byte[W * H * Bpp];

        // Base IR background — low-intensity ambient with light noise
        var rng = new Random(frame); // deterministic per frame
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte bg = (byte)(20 + rng.Next(6)); // subtle noise
                int i = (y * W + x) * Bpp;
                buf[i] = bg; buf[i + 1] = bg; buf[i + 2] = bg; buf[i + 3] = 255;
            }

        // Person center (shifts with sway)
        int cx = W / 2 + (int)(sway * 300);
        int headY = 160;
        int shoulderY = 220;
        int hipY = 340;

        // Body — IR reflects off skin/clothing with medium brightness
        FillEllipseIR(buf, W, H, cx, (shoulderY + hipY) / 2, 55, 80, 90);
        FillEllipseIR(buf, W, H, cx, headY, 35, 40, 110);

        // Face region — brighter skin reflection
        for (int dy = -25; dy <= 30; dy++)
            for (int dx = -22; dx <= 22; dx++)
            {
                float r = MathF.Sqrt((float)(dx * dx) / (22 * 22) + (float)(dy * dy) / (28 * 28));
                if (r <= 1f)
                {
                    int x = cx + dx, y = headY + dy;
                    if (x >= 0 && x < W && y >= 0 && y < H)
                    {
                        byte v = (byte)(130 + (int)((1f - r) * 40));
                        int i = (y * W + x) * Bpp;
                        buf[i] = v; buf[i + 1] = v; buf[i + 2] = v;
                    }
                }
            }

        // Eyes — very bright retro-reflective IR dots (Kinect IR illuminator)
        FillCircleIR(buf, W, H, cx - 12, headY - 5, 5, 240);
        FillCircleIR(buf, W, H, cx + 12, headY - 5, 5, 240);

        // Nose highlight
        FillCircleIR(buf, W, H, cx, headY + 5, 3, 140);

        // Structured light dot pattern (Kinect projects grid of IR dots)
        for (int dy = 0; dy < H; dy += 12)
            for (int dx = 0; dx < W; dx += 12)
            {
                int x = dx + (dy / 12 % 2 == 0 ? 0 : 6); // staggered grid
                if (x < W)
                {
                    int idx = (dy * W + x) * Bpp;
                    // Dots are brighter on surfaces closer to camera
                    byte dotBright = (byte)Math.Min(255, buf[idx] + 35 + rng.Next(15));
                    buf[idx] = dotBright; buf[idx + 1] = dotBright; buf[idx + 2] = dotBright;
                }
            }

        // Skeleton overlay (dim wireframe)
        DrawLine(buf, W, H, cx, headY, cx, shoulderY, 60, 60, 60);
        DrawLine(buf, W, H, cx, shoulderY, cx - 60, shoulderY + 40, 60, 60, 60);
        DrawLine(buf, W, H, cx, shoulderY, cx + 60, shoulderY + 40, 60, 60, 60);
        DrawLine(buf, W, H, cx, shoulderY, cx, hipY, 60, 60, 60);
        DrawLine(buf, W, H, cx, hipY, cx - 30, hipY + 80, 60, 60, 60);
        DrawLine(buf, W, H, cx, hipY, cx + 30, hipY + 80, 60, 60, 60);

        // HUD
        DrawHudText(buf, W, H, 8, 8, $"IR: FRAME {frame}", 140, 140, 140);

        return buf;
    }

    /// <summary>Fill an ellipse with grayscale IR intensity.</summary>
    private static void FillEllipseIR(byte[] buf, int w, int h,
        int cx, int cy, int rx, int ry, byte intensity)
    {
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
                if ((float)(dx * dx) / (rx * rx) + (float)(dy * dy) / (ry * ry) <= 1f)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < w && y >= 0 && y < h)
                    {
                        int i = (y * w + x) * 4;
                        buf[i] = intensity; buf[i + 1] = intensity; buf[i + 2] = intensity;
                    }
                }
    }

    /// <summary>Fill a circle with grayscale IR intensity.</summary>
    private static void FillCircleIR(byte[] buf, int w, int h,
        int cx, int cy, int r, byte intensity)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && x < w && y >= 0 && y < h)
                    {
                        int i = (y * w + x) * 4;
                        buf[i] = intensity; buf[i + 1] = intensity; buf[i + 2] = intensity;
                    }
                }
    }

    private void SetState(SensorState newState)
    {
        if (_state == newState) return;
        _state = newState;
        StateChanged?.Invoke(newState);
    }
}
