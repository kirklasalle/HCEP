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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// View model for the dedicated Kinect sensor stream window.
/// Displays RGB, Infrared, and Depth frames as WriteableBitmaps.
/// </summary>
public partial class SensorViewViewModel : ObservableObject
{
    private readonly IPipelineOrchestrator _pipeline;
    private readonly ILogger<SensorViewViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private int _colorThrottle;
    private int _irThrottle;
    private int _depthThrottle;

    public SensorViewViewModel(
        IPipelineOrchestrator pipeline,
        ILogger<SensorViewViewModel> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
    }

    // ── Observable Properties ──────────────────────────────────

    private WriteableBitmap? _rgbFrame;
    public WriteableBitmap? RgbFrame
    {
        get => _rgbFrame;
        private set => SetProperty(ref _rgbFrame, value);
    }

    private WriteableBitmap? _irFrame;
    public WriteableBitmap? IrFrame
    {
        get => _irFrame;
        private set => SetProperty(ref _irFrame, value);
    }

    private WriteableBitmap? _depthFrame;
    public WriteableBitmap? DepthFrame
    {
        get => _depthFrame;
        private set => SetProperty(ref _depthFrame, value);
    }

    [ObservableProperty] private string _rgbLabel = "RGB 640×480";
    [ObservableProperty] private string _irLabel = "IR 640×480";
    [ObservableProperty] private string _depthLabel = "DEPTH 640×480";

    // Stream visibility toggles (bound from View menu)
    [ObservableProperty] private bool _showRgbStream = true;
    [ObservableProperty] private bool _showIrStream = true;
    [ObservableProperty] private bool _showDepthStream = true;

    partial void OnShowRgbStreamChanged(bool value)
        => RgbVisibility = value ? Visibility.Visible : Visibility.Collapsed;
    partial void OnShowIrStreamChanged(bool value)
        => IrVisibility = value ? Visibility.Visible : Visibility.Collapsed;
    partial void OnShowDepthStreamChanged(bool value)
        => DepthVisibility = value ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private Visibility _rgbVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _irVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _depthVisibility = Visibility.Visible;

    // ── Lifecycle ──────────────────────────────────────────────

    public void Subscribe()
    {
        _pipeline.ColorFrameReady += OnColorFrameReady;
        _pipeline.DepthFrameReady += OnDepthFrameReady;
        _pipeline.InfraredFrameReady += OnInfraredFrameReady;
        _logger.LogInformation("SensorViewViewModel subscribed to pipeline events");
    }

    public void Unsubscribe()
    {
        _pipeline.ColorFrameReady -= OnColorFrameReady;
        _pipeline.DepthFrameReady -= OnDepthFrameReady;
        _pipeline.InfraredFrameReady -= OnInfraredFrameReady;
        _logger.LogInformation("SensorViewViewModel unsubscribed from pipeline events");
    }

    // ── Pipeline Callbacks ─────────────────────────────────────

    private void OnColorFrameReady(ColorFrame frame)
    {
        // Throttle to ~15 fps
        if (Interlocked.Increment(ref _colorThrottle) % 2 != 0) return;

        _dispatcher.InvokeAsync(() =>
        {
            EnsureBitmap(ref _rgbFrame, frame.Width, frame.Height, nameof(RgbFrame));

            _rgbFrame!.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.PixelData,
                frame.Width * frame.BytesPerPixel,
                0);
        }, DispatcherPriority.Render);
    }

    private void OnInfraredFrameReady(ColorFrame frame)
    {
        // Throttle to ~15 fps
        if (Interlocked.Increment(ref _irThrottle) % 2 != 0) return;

        _dispatcher.InvokeAsync(() =>
        {
            EnsureBitmap(ref _irFrame, frame.Width, frame.Height, nameof(IrFrame));

            _irFrame!.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.PixelData,
                frame.Width * frame.BytesPerPixel,
                0);
        }, DispatcherPriority.Render);
    }

    private void OnDepthFrameReady(DepthFrame frame)
    {
        // Throttle to ~15 fps
        if (Interlocked.Increment(ref _depthThrottle) % 2 != 0) return;

        // Colorize depth data → BGRA32 for display
        var colorized = ColorizeDepth(frame);

        _dispatcher.InvokeAsync(() =>
        {
            EnsureBitmap(ref _depthFrame, frame.Width, frame.Height, nameof(DepthFrame));

            _depthFrame!.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                colorized,
                frame.Width * 4,
                0);
        }, DispatcherPriority.Render);
    }

    // ── Helpers ────────────────────────────────────────────────

    private void EnsureBitmap(ref WriteableBitmap? bitmap, int width, int height, string propertyName)
    {
        if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            OnPropertyChanged(propertyName);
        }
    }

    /// <summary>
    /// Converts depth data (short[] mm) to a colorized BGRA32 buffer using
    /// a rainbow-like heat map: near → red/orange, mid → green/yellow, far → blue/purple.
    /// </summary>
    private static byte[] ColorizeDepth(DepthFrame frame)
    {
        int w = frame.Width, h = frame.Height;
        var buf = new byte[w * h * 4];
        int min = frame.MinDepthMm, max = frame.MaxDepthMm;
        float range = max - min;

        for (int idx = 0; idx < frame.DepthData.Length; idx++)
        {
            short depth = frame.DepthData[idx];
            int i = idx * 4;

            if (depth <= 0 || depth < min)
            {
                // Unknown / too close — dark blue
                buf[i] = 80; buf[i + 1] = 0; buf[i + 2] = 0; buf[i + 3] = 255;
                continue;
            }

            if (depth > max)
            {
                // Too far — dark gray
                buf[i] = 30; buf[i + 1] = 30; buf[i + 2] = 30; buf[i + 3] = 255;
                continue;
            }

            // Normalize 0..1 (0 = near, 1 = far)
            float t = (depth - min) / range;

            // 5-stop color ramp: Red → Yellow → Green → Cyan → Blue
            byte r, g, b;
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                r = 255; g = (byte)(s * 255); b = 0;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                r = (byte)((1f - s) * 255); g = 255; b = 0;
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                r = 0; g = 255; b = (byte)(s * 255);
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                r = 0; g = (byte)((1f - s) * 255); b = 255;
            }

            // BGRA order
            buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255;
        }

        return buf;
    }
}
