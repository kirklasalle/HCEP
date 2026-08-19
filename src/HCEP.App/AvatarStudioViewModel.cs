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
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HCEP.Core.Interfaces;
using HCEP.Core.Models;
using HCEP.Spatial;
using HCEP.Speech;
using Microsoft.Extensions.Logging;

namespace HCEP.App;

/// <summary>
/// ViewModel for the HCEP Avatar Studio window.
/// Powers 2D SVG parametric avatar authoring, 3D Kinect Fusion volumetric scanning,
/// live kinematics testing sandbox, and dynamic one-click catalog publishing.
/// </summary>
public partial class AvatarStudioViewModel : ObservableObject
{
    private readonly IAvatarCatalog _avatarCatalog;
    private readonly KinectFusionHeadScanner _fusionScanner;
    private readonly HCEPPipelineOrchestrator _orchestrator;
    private readonly ILogger<AvatarStudioViewModel>? _logger;

    public SvgAvatarControl PreviewSvgAvatar { get; } = new();

    public AvatarStudioViewModel(
        IAvatarCatalog avatarCatalog,
        HCEPPipelineOrchestrator orchestrator,
        ILogger<AvatarStudioViewModel>? logger = null)
    {
        _avatarCatalog = avatarCatalog;
        _orchestrator = orchestrator;
        _logger = logger;
        _fusionScanner = new KinectFusionHeadScanner();

        _fusionScanner.StateChanged += OnFusionStateChanged;
        _fusionScanner.MeshReady += OnFusionMeshReady;

        ApplyParametersToPreview();
        UpdateSvgMarkup();

        // Subscribe to live sensor snapshots for Live Mirror mode
        _orchestrator.SnapshotReady += OnSnapshotReady;
    }

    // ── Observable Properties ──

    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private string _statusMessage = "Ready — Design, Scan, Test, and Deploy HCEP Avatars";
    [ObservableProperty] private string _avatarName = "Cyber Partner Alpha";

    // ── 2D SVG Parameters ──
    [ObservableProperty] private byte _skinR = 15;
    [ObservableProperty] private byte _skinG = 23;
    [ObservableProperty] private byte _skinB = 42;

    [ObservableProperty] private byte _glowR = 0;
    [ObservableProperty] private byte _glowG = 229;
    [ObservableProperty] private byte _glowB = 255;

    [ObservableProperty] private byte _irisR = 0;
    [ObservableProperty] private byte _irisG = 255;
    [ObservableProperty] private byte _irisB = 194;

    [ObservableProperty] private double _eyeRadiusX = 28;
    [ObservableProperty] private double _eyeRadiusY = 38;
    [ObservableProperty] private double _pupilRadius = 12;
    [ObservableProperty] private double _eyeSpacing = 70;
    [ObservableProperty] private double _browThickness = 3.5;
    [ObservableProperty] private bool _showCyberneticAccents = true;
    [ObservableProperty] private string _generatedSvgMarkup = "";

    // ── 3D Kinect Fusion Parameters ──
    [ObservableProperty] private string _fusionStatusText = "Kinect Fusion Ready (Voxel TSDF Engine)";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _scannedVerticesCount;
    [ObservableProperty] private int _scannedTrianglesCount;
    [ObservableProperty] private double _headScaleWidth = 0.16;
    [ObservableProperty] private double _headScaleHeight = 0.22;
    [ObservableProperty] private double _headScaleDepth = 0.18;

    // ── Testing Sandbox Sliders ──
    [ObservableProperty] private double _testGazeYaw; // degrees [-45..+45]
    [ObservableProperty] private double _testGazePitch; // degrees [-35..+35]
    [ObservableProperty] private double _testGazeDistance = 1.5; // meters
    [ObservableProperty] private double _testSmileIntensity; // [0..1]
    [ObservableProperty] private double _testBrowRaise; // [0..1]
    [ObservableProperty] private double _testBrowFurrow; // [0..1]
    [ObservableProperty] private bool _isLiveMirrorEnabled;

    public ObservableCollection<string> AvailableVisemes { get; } =
    [
        "Silence", "Ah", "Oh", "Ee", "Fv", "Mbp", "L", "Th", "W"
    ];
    [ObservableProperty] private string _selectedViseme = "Silence";

    // ── Parameter Change Handlers ──

    partial void OnSkinRChanged(byte value) => Refresh2D();
    partial void OnSkinGChanged(byte value) => Refresh2D();
    partial void OnSkinBChanged(byte value) => Refresh2D();

    partial void OnGlowRChanged(byte value) => Refresh2D();
    partial void OnGlowGChanged(byte value) => Refresh2D();
    partial void OnGlowBChanged(byte value) => Refresh2D();

    partial void OnIrisRChanged(byte value) => Refresh2D();
    partial void OnIrisGChanged(byte value) => Refresh2D();
    partial void OnIrisBChanged(byte value) => Refresh2D();

    partial void OnEyeRadiusXChanged(double value) => Refresh2D();
    partial void OnEyeRadiusYChanged(double value) => Refresh2D();
    partial void OnPupilRadiusChanged(double value) => Refresh2D();
    partial void OnEyeSpacingChanged(double value) => Refresh2D();
    partial void OnBrowThicknessChanged(double value) => Refresh2D();
    partial void OnShowCyberneticAccentsChanged(bool value) => Refresh2D();

    partial void OnTestGazeYawChanged(double value) => ApplyTestKinematics();
    partial void OnTestGazePitchChanged(double value) => ApplyTestKinematics();
    partial void OnTestGazeDistanceChanged(double value) => ApplyTestKinematics();
    partial void OnTestSmileIntensityChanged(double value) => ApplyTestKinematics();
    partial void OnTestBrowRaiseChanged(double value) => ApplyTestKinematics();
    partial void OnTestBrowFurrowChanged(double value) => ApplyTestKinematics();
    partial void OnSelectedVisemeChanged(string value) => ApplyTestKinematics();

    private void Refresh2D()
    {
        ApplyParametersToPreview();
        UpdateSvgMarkup();
    }

    private void ApplyParametersToPreview()
    {
        PreviewSvgAvatar.SkinColor = Color.FromRgb(SkinR, SkinG, SkinB);
        PreviewSvgAvatar.AccentGlowColor = Color.FromRgb(GlowR, GlowG, GlowB);
        PreviewSvgAvatar.IrisColor = Color.FromRgb(IrisR, IrisG, IrisB);
        PreviewSvgAvatar.EyeRadiusX = EyeRadiusX;
        PreviewSvgAvatar.EyeRadiusY = EyeRadiusY;
        PreviewSvgAvatar.PupilRadius = PupilRadius;
        PreviewSvgAvatar.EyeSpacing = EyeSpacing;
        PreviewSvgAvatar.BrowThickness = BrowThickness;
        PreviewSvgAvatar.ShowCyberneticAccents = ShowCyberneticAccents;
        PreviewSvgAvatar.InvalidateVisual();
    }

    private void UpdateSvgMarkup()
    {
        GeneratedSvgMarkup = PreviewSvgAvatar.GenerateSvgMarkup(512, 512);
    }

    private void ApplyTestKinematics()
    {
        if (IsLiveMirrorEnabled) return;

        float yawRad = (float)(TestGazeYaw * Math.PI / 180.0);
        float pitchRad = (float)(TestGazePitch * Math.PI / 180.0);

        PreviewSvgAvatar.SetGaze(pitchRad, yawRad, (float)TestGazeDistance);
        PreviewSvgAvatar.SetSmile((float)TestSmileIntensity);
        PreviewSvgAvatar.SetBrows((float)TestBrowRaise, (float)TestBrowFurrow);

        var viseme = SelectedViseme switch
        {
            "Ah" => new VisemeData { JawOpen = 0.8f, LipRound = 0.2f },
            "Oh" => new VisemeData { JawOpen = 0.6f, LipRound = 0.9f },
            "Ee" => new VisemeData { JawOpen = 0.3f, LipRound = 0.0f },
            "Fv" => new VisemeData { JawOpen = 0.2f, LipRound = 0.1f },
            "Mbp" => new VisemeData { JawOpen = 0.0f, LipRound = 0.0f },
            "L" => new VisemeData { JawOpen = 0.4f, LipRound = 0.3f },
            _ => VisemeData.Silence
        };
        PreviewSvgAvatar.SetViseme(viseme);
    }

    private void OnSnapshotReady(SceneSnapshot snapshot)
    {
        if (!IsLiveMirrorEnabled) return;

        var person = snapshot.PrimaryPerson;
        if (person?.Face is { IsTracked: true } face)
        {
            float yawRad = face.HeadRotation.Y * MathF.PI / 180f;
            float pitchRad = face.HeadRotation.X * MathF.PI / 180f;
            float smile = face.ActionUnits.Length > 0 ? Math.Clamp(face.ActionUnits[0], 0f, 1f) : 0f;
            float browRaise = face.ActionUnits.Length > 5 ? Math.Clamp(face.ActionUnits[5], 0f, 1f) : 0f;
            float browLower = face.ActionUnits.Length > 3 ? Math.Clamp(-face.ActionUnits[3], 0f, 1f) : 0f;

            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                PreviewSvgAvatar.SetGaze(pitchRad, yawRad, person.DistanceM > 0 ? person.DistanceM : 1.5f);
                PreviewSvgAvatar.SetSmile(smile);
                PreviewSvgAvatar.SetBrows(browRaise, browLower);
            }));

            if (IsScanning)
            {
                _fusionScanner.IntegrateFrame(snapshot.Depth, face);
            }
        }
    }

    // ── Commands ──

    [RelayCommand]
    private void TriggerTestBlink()
    {
        PreviewSvgAvatar.TriggerBlink();
    }

    [RelayCommand]
    private void TriggerTestNod()
    {
        PreviewSvgAvatar.TriggerNod();
    }

    [RelayCommand]
    private void TriggerTestTilt()
    {
        PreviewSvgAvatar.TriggerTilt(8f);
    }

    [RelayCommand]
    private void ResetTestSliders()
    {
        TestGazeYaw = 0;
        TestGazePitch = 0;
        TestGazeDistance = 1.5;
        TestSmileIntensity = 0;
        TestBrowRaise = 0;
        TestBrowFurrow = 0;
        SelectedViseme = "Silence";
        ApplyTestKinematics();
    }

    [RelayCommand]
    private void StartFusionScan()
    {
        IsScanning = true;
        _fusionScanner.StartScan();
        FusionStatusText = "Scanning volumetric depth stream… Look straight, then turn head slowly.";
    }

    [RelayCommand]
    private void CompleteFusionScan()
    {
        IsScanning = false;
        var mesh = _fusionScanner.CompleteScan(_orchestrator.LatestFaceFrame);
        ScannedVerticesCount = mesh.Vertices.Length;
        ScannedTrianglesCount = mesh.Indices.Length / 3;
        FusionStatusText = $"Reconstruction Complete! {ScannedVerticesCount} vertices, {ScannedTrianglesCount} triangles.";
    }

    [RelayCommand]
    private void ResetFusionScan()
    {
        IsScanning = false;
        _fusionScanner.Reset();
        ScannedVerticesCount = 0;
        ScannedTrianglesCount = 0;
        FusionStatusText = "Kinect Fusion Volume Reset.";
    }

    [RelayCommand]
    private void PushToCatalog()
    {
        string key = "custom-" + Guid.NewGuid().ToString("N")[..8];
        string name = string.IsNullOrWhiteSpace(AvatarName) ? "Custom Avatar" : AvatarName.Trim();

        // Capture current parametric configuration
        var skin = Color.FromRgb(SkinR, SkinG, SkinB);
        var glow = Color.FromRgb(GlowR, GlowG, GlowB);
        var iris = Color.FromRgb(IrisR, IrisG, IrisB);
        double eyeRx = EyeRadiusX;
        double eyeRy = EyeRadiusY;
        double pupilR = PupilRadius;
        double eyeSp = EyeSpacing;
        double browTh = BrowThickness;
        bool cyber = ShowCyberneticAccents;

        var descriptor = new AvatarDescriptor(
            Key: key,
            DisplayName: $"⭐ {name}",
            Use3DMode: false,
            IsImplemented: true,
            Summary: $"Custom HCEP Avatar created in Avatar Studio on {DateTime.Now:g}");

        _avatarCatalog.RegisterCustomAvatar(descriptor, () =>
        {
            return new SvgAvatarControl
            {
                AvatarName = name,
                SkinColor = skin,
                AccentGlowColor = glow,
                IrisColor = iris,
                EyeRadiusX = eyeRx,
                EyeRadiusY = eyeRy,
                PupilRadius = pupilR,
                EyeSpacing = eyeSp,
                BrowThickness = browTh,
                ShowCyberneticAccents = cyber
            };
        });

        // Save local preset to AppData
        try
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HCEP", "custom-avatars");
            Directory.CreateDirectory(appData);
            string filePath = Path.Combine(appData, $"{key}.svg");
            File.WriteAllText(filePath, GeneratedSvgMarkup);
        }
        catch { }

        StatusMessage = $"🚀 Successfully published '{name}' to the Official Avatar Catalog!";
        _logger?.LogInformation("Custom avatar '{Name}' published with key {Key}", name, key);
    }

    [RelayCommand]
    private void ExportSvg()
    {
        try
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HCEP", "exports");
            Directory.CreateDirectory(appData);
            string safeName = string.Join("_", AvatarName.Split(Path.GetInvalidFileNameChars()));
            string filePath = Path.Combine(appData, $"{safeName}.svg");
            File.WriteAllText(filePath, GeneratedSvgMarkup);
            StatusMessage = $"💾 Exported SVG to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void OnFusionStateChanged(FusionScanState state)
    {
        App.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            FusionStatusText = state switch
            {
                FusionScanState.Scanning => "Scanning in progress… Integrating depth frames.",
                FusionScanState.Reconstructing => "Reconstructing 3D Marching-Cubes Surface…",
                FusionScanState.Completed => $"3D Surface Ready ({ScannedVerticesCount} vertices)",
                _ => "Kinect Fusion Engine Ready"
            };
        }));
    }

    private void OnFusionMeshReady(FusionMesh mesh)
    {
        App.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            ScannedVerticesCount = mesh.Vertices.Length;
            ScannedTrianglesCount = mesh.Indices.Length / 3;
            FusionStatusText = $"3D Model Complete: {ScannedVerticesCount} vertices, {ScannedTrianglesCount} triangles.";
        }));
    }
}
