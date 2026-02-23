// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────

using System.Diagnostics;
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
/// View model for the dedicated Kinect RGB video window.
/// Receives ColorFrame events from the pipeline and renders to a WriteableBitmap.
/// </summary>
public partial class KinectVideoViewModel : ObservableObject
{
    private readonly IPipelineOrchestrator _pipeline;
    private readonly ILogger<KinectVideoViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    // FPS tracking
    private readonly Stopwatch _fpsWatch = new();
    private int _frameCount;
    private double _fps;

    public KinectVideoViewModel(
        IPipelineOrchestrator pipeline,
        ILogger<KinectVideoViewModel> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
    }

    // ── Observable Properties ──────────────────────────────────

    private WriteableBitmap? _videoFrame;
    public WriteableBitmap? VideoFrame
    {
        get => _videoFrame;
        private set => SetProperty(ref _videoFrame, value);
    }

    [ObservableProperty] private Brush _statusColor = Brushes.Red;
    [ObservableProperty] private string _sourceLabel = "WAITING…";
    [ObservableProperty] private string _fpsLabel = "— fps";
    [ObservableProperty] private string _overlayText = "Waiting for Kinect RGB signal…";
    [ObservableProperty] private Visibility _overlayVisibility = Visibility.Visible;
    [ObservableProperty] private string _resolutionLabel = "—";

    // View mode: Uniform (fit) vs None (actual size)
    [ObservableProperty] private Stretch _videoStretch = Stretch.Uniform;
    [ObservableProperty] private bool _fitToWindow = true;

    partial void OnFitToWindowChanged(bool value)
        => VideoStretch = value ? Stretch.Uniform : Stretch.None;

    // ── Lifecycle ──────────────────────────────────────────────

    public void Subscribe()
    {
        _pipeline.ColorFrameReady += OnColorFrameReady;
        _fpsWatch.Start();
        _logger.LogInformation("KinectVideoViewModel subscribed to ColorFrameReady");
    }

    public void Unsubscribe()
    {
        _pipeline.ColorFrameReady -= OnColorFrameReady;
        _fpsWatch.Stop();
        _logger.LogInformation("KinectVideoViewModel unsubscribed from ColorFrameReady");
    }

    // ── Pipeline Callback ──────────────────────────────────────

    private void OnColorFrameReady(ColorFrame frame)
    {
        _dispatcher.InvokeAsync(() =>
        {
            // Ensure WriteableBitmap matches frame dimensions
            if (_videoFrame is null ||
                _videoFrame.PixelWidth != frame.Width ||
                _videoFrame.PixelHeight != frame.Height)
            {
                _videoFrame = new WriteableBitmap(
                    frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
                OnPropertyChanged(nameof(VideoFrame));
            }

            // Write pixels
            _videoFrame.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.PixelData,
                frame.Width * frame.BytesPerPixel,
                0);

            // Update status on first frame
            if (OverlayVisibility == Visibility.Visible)
            {
                OverlayVisibility = Visibility.Collapsed;
                StatusColor = Brushes.LimeGreen;
                SourceLabel = "KINECT v1 — LIVE";
                ResolutionLabel = $"{frame.Width}×{frame.Height} BGRA32";
                _logger.LogInformation(
                    "First Kinect RGB frame received: {W}×{H}", frame.Width, frame.Height);
            }

            // FPS calculation (update every 30 frames)
            _frameCount++;
            if (_frameCount % 30 == 0)
            {
                double elapsed = _fpsWatch.Elapsed.TotalSeconds;
                _fps = _frameCount / elapsed;
                FpsLabel = $"{_fps:F1} fps";
                _frameCount = 0;
                _fpsWatch.Restart();
            }
        }, DispatcherPriority.Render);
    }
}
