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
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HCEP.Core.Enums;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

#pragma warning disable CS0067 // Unused event warnings are expected as WebcamSensorSource only implements a subset of streams.

namespace HCEP.Kinect;

/// <summary>
/// Platform-agnostic webcam sensor source implementing ISensorSource.
/// Uses OpenCvSharp4 to capture RGB frames and run Haar-cascade face/eye landmark tracking.
/// Maps coordinates to Kinect-compatible FaceFrame indices.
/// </summary>
public sealed class WebcamSensorSource : ISensorSource
{
    private readonly ILogger<WebcamSensorSource> _logger;
    private volatile SensorState _state = SensorState.Disconnected;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private VideoCapture? _capture;

    // Haar Cascades
    private CascadeClassifier? _faceCascade;
    private CascadeClassifier? _eyeCascade;

    private readonly string _modelsDir;
    private readonly string _faceCascadePath;
    private readonly string _eyeCascadePath;

    public WebcamSensorSource(ILogger<WebcamSensorSource> logger)
    {
        _logger = logger;
        _modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
        _faceCascadePath = Path.Combine(_modelsDir, "haarcascade_frontalface_default.xml");
        _eyeCascadePath = Path.Combine(_modelsDir, "haarcascade_eye.xml");
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

    public async Task InitializeAsync(SensorStreamType streams, CancellationToken ct = default)
    {
        SetState(SensorState.Initializing);
        _logger.LogInformation("Webcam sensor initializing...");

        try
        {
            await EnsureCascadesExistAsync(ct);

            // Load Haar cascades
            _faceCascade = new CascadeClassifier(_faceCascadePath);
            _eyeCascade = new CascadeClassifier(_eyeCascadePath);

            if (_faceCascade.Empty() || _eyeCascade.Empty())
            {
                throw new InvalidOperationException("Failed to load Haar cascade classifiers.");
            }

            SetState(SensorState.Connected);
            _logger.LogInformation("Webcam sensor initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Webcam sensor.");
            SetState(SensorState.Error);
            throw;
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_state != SensorState.Connected)
        {
            _logger.LogWarning("Cannot start webcam sensor because state is {State}", _state);
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _capture = new VideoCapture(0); // Open default camera

        if (!_capture.IsOpened())
        {
            _logger.LogError("Could not open default camera (index 0).");
            SetState(SensorState.Error);
            return Task.CompletedTask;
        }

        // Configure default frame resolution
        _capture.FrameWidth = 640;
        _capture.FrameHeight = 480;

        _captureTask = CaptureLoopAsync(_cts.Token);
        _logger.LogInformation("Webcam sensor streaming started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();

        if (_captureTask is not null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException) { }
        }

        _capture?.Dispose();
        _capture = null;

        _cts?.Dispose();
        _cts = null;

        _faceCascade?.Dispose();
        _faceCascade = null;
        _eyeCascade?.Dispose();
        _eyeCascade = null;

        SetState(SensorState.Disconnected);
        _logger.LogInformation("Webcam sensor stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void SetState(SensorState newState)
    {
        if (_state == newState) return;
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    private async Task EnsureCascadesExistAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_modelsDir))
        {
            Directory.CreateDirectory(_modelsDir);
        }

        using var client = new HttpClient();

        if (!File.Exists(_faceCascadePath))
        {
            _logger.LogInformation("Downloading face detection cascade...");
            var url = "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml";
            var bytes = await client.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(_faceCascadePath, bytes, ct);
        }

        if (!File.Exists(_eyeCascadePath))
        {
            _logger.LogInformation("Downloading eye detection cascade...");
            var url = "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_eye.xml";
            var bytes = await client.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(_eyeCascadePath, bytes, ct);
        }
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        int frameNumber = 0;
        using var frame = new Mat();
        using var gray = new Mat();

        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            if (_capture is null || !_capture.Read(frame) || frame.Empty())
            {
                await Task.Delay(10, ct);
                continue;
            }

            frameNumber++;

            // ── Fire Color Frame Ready (Convert to BGRA) ────────────────
            using var bgraFrame = new Mat();
            Cv2.CvtColor(frame, bgraFrame, ColorConversionCodes.BGR2BGRA);
            byte[] pixelData = new byte[bgraFrame.Width * bgraFrame.Height * 4];
            Marshal.Copy(bgraFrame.Data, pixelData, 0, pixelData.Length);

            ColorFrameReady?.Invoke(new ColorFrame
            {
                Timestamp = now,
                PixelData = pixelData,
                Width = bgraFrame.Width,
                Height = bgraFrame.Height,
                BytesPerPixel = 4,
                FrameNumber = frameNumber,
            });

            // ── Face & Eye Detection ────────────────────────────────────
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            var faces = _faceCascade?.DetectMultiScale(gray, 1.1, 3, HaarDetectionTypes.ScaleImage, new Size(100, 100)) ?? Array.Empty<Rect>();

            if (faces.Length > 0)
            {
                var faceRect = faces[0];

                // Bounding boxes in face coordinates for upper-left (left eye) and upper-right (right eye)
                // Left side of face in image coords (person's right)
                var rightEyeRoi = new Rect(faceRect.X, faceRect.Y + (faceRect.Height / 5), faceRect.Width / 2, faceRect.Height / 3);
                // Right side of face in image coords (person's left)
                var leftEyeRoi = new Rect(faceRect.X + faceRect.Width / 2, faceRect.Y + (faceRect.Height / 5), faceRect.Width / 2, faceRect.Height / 3);

                using var rightEyeMat = new Mat(gray, rightEyeRoi);
                using var leftEyeMat = new Mat(gray, leftEyeRoi);

                var rightEyes = _eyeCascade?.DetectMultiScale(rightEyeMat, 1.1, 2, HaarDetectionTypes.ScaleImage, new Size(20, 20)) ?? Array.Empty<Rect>();
                var leftEyes = _eyeCascade?.DetectMultiScale(leftEyeMat, 1.1, 2, HaarDetectionTypes.ScaleImage, new Size(20, 20)) ?? Array.Empty<Rect>();

                // Default centers relative to ROI
                Vector2 leftEyePosLocal = new(leftEyeRoi.Width / 2f, leftEyeRoi.Height / 2f);
                Vector2 rightEyePosLocal = new(rightEyeRoi.Width / 2f, rightEyeRoi.Height / 2f);

                Vector2 leftPupilLocal = leftEyePosLocal;
                Vector2 rightPupilLocal = rightEyePosLocal;

                // Detect pupils in Left Eye
                if (leftEyes.Length > 0)
                {
                    var eye = leftEyes[0];
                    leftEyePosLocal = new Vector2(eye.X + eye.Width / 2f, eye.Y + eye.Height / 2f);
                    using var eyeCrop = new Mat(leftEyeMat, eye);
                    leftPupilLocal = FindPupilLocal(eyeCrop) + new Vector2(eye.X, eye.Y);
                }

                // Detect pupils in Right Eye
                if (rightEyes.Length > 0)
                {
                    var eye = rightEyes[0];
                    rightEyePosLocal = new Vector2(eye.X + eye.Width / 2f, eye.Y + eye.Height / 2f);
                    using var eyeCrop = new Mat(rightEyeMat, eye);
                    rightPupilLocal = FindPupilLocal(eyeCrop) + new Vector2(eye.X, eye.Y);
                }

                // Convert local coords back to frame pixel coords
                Vector2 leftEyeGlobal = new(leftEyeRoi.X + leftEyePosLocal.X, leftEyeRoi.Y + leftEyePosLocal.Y);
                Vector2 rightEyeGlobal = new(rightEyeRoi.X + rightEyePosLocal.X, rightEyeRoi.Y + rightEyePosLocal.Y);

                Vector2 leftPupilGlobal = new(leftEyeRoi.X + leftPupilLocal.X, leftEyeRoi.Y + leftPupilLocal.Y);
                Vector2 rightPupilGlobal = new(rightEyeRoi.X + rightPupilLocal.X, rightEyeRoi.Y + rightPupilLocal.Y);

                // ── Head Pose Estimation ──────────────────────────────
                // Simple geometric estimation:
                // Shift of eye center midpoint relative to face box center indicates yaw/pitch
                float faceCenterX = faceRect.X + faceRect.Width / 2f;
                float faceCenterY = faceRect.Y + faceRect.Height * 0.4f; // typical eye level
                float eyesMidX = (leftEyeGlobal.X + rightEyeGlobal.X) / 2f;
                float eyesMidY = (leftEyeGlobal.Y + rightEyeGlobal.Y) / 2f;

                float yaw = (eyesMidX - faceCenterX) / faceRect.Width * 90f;   // degrees
                float pitch = -(eyesMidY - faceCenterY) / faceRect.Height * 90f; // degrees

                // ── Generate 87 Landmarks ─────────────────────────────
                var featurePoints2D = new Vector2[87];
                var featurePoints3D = new Vector3[87];

                // Right eye contour (indices 9-14)
                float rEyeRadius = faceRect.Width * 0.04f;
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * MathF.PI / 3f;
                    Vector2 pt2D = rightEyeGlobal + new Vector2(MathF.Cos(angle) * rEyeRadius, MathF.Sin(angle) * rEyeRadius);
                    featurePoints2D[9 + i] = pt2D;
                    featurePoints3D[9 + i] = To3D(pt2D, faceRect.Width);
                }

                // Left eye contour (indices 30-35)
                float lEyeRadius = faceRect.Width * 0.04f;
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * MathF.PI / 3f;
                    Vector2 pt2D = leftEyeGlobal + new Vector2(MathF.Cos(angle) * lEyeRadius, MathF.Sin(angle) * lEyeRadius);
                    featurePoints2D[30 + i] = pt2D;
                    featurePoints3D[30 + i] = To3D(pt2D, faceRect.Width);
                }

                // Pupils
                featurePoints2D[69] = leftPupilGlobal;   // left pupil
                featurePoints3D[69] = To3D(leftPupilGlobal, faceRect.Width);

                featurePoints2D[73] = rightPupilGlobal;  // right pupil
                featurePoints3D[73] = To3D(rightPupilGlobal, faceRect.Width);

                // Head Translation (mm)
                // Use face box width to approximate depth (Z)
                float zDistanceMm = 450000f / faceRect.Width; // standard calibration
                float xTranslationMm = (faceCenterX - 320f) * (zDistanceMm / 525f);
                float yTranslationMm = -(faceCenterY - 240f) * (zDistanceMm / 525f);

                FaceFrameReady?.Invoke(new FaceFrame
                {
                    Timestamp = now,
                    TrackingId = 1,
                    IsTracked = true,
                    ActionUnits = Array.Empty<float>(),
                    FeaturePoints2D = featurePoints2D,
                    FeaturePoints3D = featurePoints3D,
                    HeadRotation = new Vector3(pitch, yaw, 0f),
                    HeadTranslation = new Vector3(xTranslationMm, yTranslationMm, zDistanceMm),
                    FaceRect = (faceRect.X, faceRect.Y, faceRect.Width, faceRect.Height),
                });
            }
            else
            {
                // Emit empty face frame
                FaceFrameReady?.Invoke(new FaceFrame
                {
                    Timestamp = now,
                    TrackingId = 0,
                    IsTracked = false,
                    FeaturePoints2D = Array.Empty<Vector2>(),
                    FeaturePoints3D = Array.Empty<Vector3>(),
                });
            }

            try
            {
                await Task.Delay(33, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static Vector2 FindPupilLocal(Mat eyeCrop)
    {
        // Find the darkest region in the cropped eye
        Cv2.MinMaxLoc(eyeCrop, out _, out _, out var minLoc, out _);
        return new Vector2(minLoc.X, minLoc.Y);
    }

    private static Vector3 To3D(Vector2 pt2D, float faceWidth)
    {
        // Simple projection back to 3D head-relative space (mm)
        // Assume z-depth based on face size
        float z = 450000f / faceWidth;
        float x = (pt2D.X - 320f) * (z / 525f);
        float y = -(pt2D.Y - 240f) * (z / 525f);
        return new Vector3(x, y, z);
    }
}
