// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HCEP.App;

/// <summary>
/// Runtime-adjustable overlay-alignment settings shared by overlay
/// renderers (VideoOverlayControl, SensorViewWindow, avatar overlays).
///
/// <para>
/// These values compensate for the physical offset between the Kinect v1
/// depth/IR sensor and the color camera, plus per-user fine-tuning
/// determined interactively in the Face Mesh Alignment and Skeletal
/// Alignment calibration windows.
/// </para>
///
/// <para>
/// Values are persisted to <c>%LocalAppData%\HCEP\overlay-alignment.json</c>
/// and reloaded at startup. The file only contains alignment numbers — no
/// user secrets — and is safe to migrate across HCEP upgrades.
/// </para>
///
/// <para>
/// This class is additive: legacy renderers that never call
/// <see cref="Load"/> or subscribe to <see cref="Changed"/> continue to
/// work with the compiled defaults.
/// </para>
/// </summary>
public static class OverlayAlignment
{
    private const double DefaultVerticalOffsetPx = 48.0;
    private const double DefaultHorizontalOffsetPx = 0.0;
    private const double DefaultMeshScale = 1.0;
    private const double DefaultSkeletonVerticalOffsetPx = 48.0;
    private const double DefaultSkeletonHorizontalOffsetPx = 0.0;
    private const double DefaultSkeletonScale = 1.0;

    private static double _verticalOffsetPx = DefaultVerticalOffsetPx;
    private static double _horizontalOffsetPx = DefaultHorizontalOffsetPx;
    private static double _meshScale = DefaultMeshScale;
    private static double _skeletonVerticalOffsetPx = DefaultSkeletonVerticalOffsetPx;
    private static double _skeletonHorizontalOffsetPx = DefaultSkeletonHorizontalOffsetPx;
    private static double _skeletonScale = DefaultSkeletonScale;

    /// <summary>
    /// Vertical pixel offset (in Kinect 640×480 pixel space) applied to every
    /// overlay element. Positive = shift the overlay DOWN. Default 48 px.
    /// </summary>
    public static double VerticalOffsetPx
    {
        get => _verticalOffsetPx;
        set
        {
            if (Math.Abs(_verticalOffsetPx - value) < 1e-6) return;
            _verticalOffsetPx = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Horizontal pixel offset (in Kinect 640×480 pixel space) applied to every
    /// overlay element. Positive = shift the overlay RIGHT. Default 0 px.
    /// </summary>
    public static double HorizontalOffsetPx
    {
        get => _horizontalOffsetPx;
        set
        {
            if (Math.Abs(_horizontalOffsetPx - value) < 1e-6) return;
            _horizontalOffsetPx = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Uniform scale factor applied around the face centroid when drawing the
    /// facial feature mesh. 1.0 = no scaling. Useful when the FaceTrackLib
    /// mesh is slightly larger or smaller than the actual face on the color
    /// feed. Clamped to [0.6 .. 1.6].
    /// </summary>
    public static double MeshScale
    {
        get => _meshScale;
        set
        {
            var clamped = Math.Clamp(value, 0.6, 1.6);
            if (Math.Abs(_meshScale - clamped) < 1e-6) return;
            _meshScale = clamped;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Vertical pixel offset applied only to skeleton joints and bones.
    /// Positive = shift the skeleton DOWN. Default 48 px.
    /// </summary>
    public static double SkeletonVerticalOffsetPx
    {
        get => _skeletonVerticalOffsetPx;
        set
        {
            if (Math.Abs(_skeletonVerticalOffsetPx - value) < 1e-6) return;
            _skeletonVerticalOffsetPx = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Horizontal pixel offset applied only to skeleton joints and bones.
    /// Positive = shift the skeleton RIGHT. Default 0 px.
    /// </summary>
    public static double SkeletonHorizontalOffsetPx
    {
        get => _skeletonHorizontalOffsetPx;
        set
        {
            if (Math.Abs(_skeletonHorizontalOffsetPx - value) < 1e-6) return;
            _skeletonHorizontalOffsetPx = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Uniform scale factor for the skeleton overlay around the 640x480 image
    /// centre. 1.0 = no scaling. Clamped to [0.6 .. 1.6].
    /// </summary>
    public static double SkeletonScale
    {
        get => _skeletonScale;
        set
        {
            var clamped = Math.Clamp(value, 0.6, 1.6);
            if (Math.Abs(_skeletonScale - clamped) < 1e-6) return;
            _skeletonScale = clamped;
            Changed?.Invoke();
        }
    }

    /// <summary>Fires whenever any alignment value changes. Renderers should invalidate visuals on this event.</summary>
    public static event Action? Changed;

    /// <summary>Resolves the persistence file path under %LocalAppData%\HCEP\.</summary>
    public static string GetPersistencePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "HCEP");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "overlay-alignment.json");
    }

    /// <summary>Restore alignment values from disk. Missing/invalid files are silently ignored.</summary>
    public static void Load()
    {
        try
        {
            var path = GetPersistencePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto is null) return;

            _verticalOffsetPx = dto.VerticalOffsetPx;
            _horizontalOffsetPx = dto.HorizontalOffsetPx;
            _meshScale = Math.Clamp(dto.MeshScale <= 0 ? DefaultMeshScale : dto.MeshScale, 0.6, 1.6);
            _skeletonVerticalOffsetPx = dto.SkeletonVerticalOffsetPx;
            _skeletonHorizontalOffsetPx = dto.SkeletonHorizontalOffsetPx;
            _skeletonScale = Math.Clamp(dto.SkeletonScale <= 0 ? DefaultSkeletonScale : dto.SkeletonScale, 0.6, 1.6);
            Changed?.Invoke();
        }
        catch
        {
            // Defensive: never let a corrupt alignment file crash startup.
        }
    }

    /// <summary>Persist current alignment values to disk. Errors are swallowed by design.</summary>
    public static void Save()
    {
        try
        {
            var path = GetPersistencePath();
            var dto = new Dto
            {
                VerticalOffsetPx = _verticalOffsetPx,
                HorizontalOffsetPx = _horizontalOffsetPx,
                MeshScale = _meshScale,
                SkeletonVerticalOffsetPx = _skeletonVerticalOffsetPx,
                SkeletonHorizontalOffsetPx = _skeletonHorizontalOffsetPx,
                SkeletonScale = _skeletonScale,
            };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal — alignment simply won't persist across restarts.
        }
    }

    /// <summary>Reset face/mesh alignment values to the compiled defaults.</summary>
    public static void ResetFaceToDefaults()
    {
        _verticalOffsetPx = DefaultVerticalOffsetPx;
        _horizontalOffsetPx = DefaultHorizontalOffsetPx;
        _meshScale = DefaultMeshScale;
        Changed?.Invoke();
    }

    /// <summary>Reset skeleton alignment values to the compiled defaults.</summary>
    public static void ResetSkeletonToDefaults()
    {
        _skeletonVerticalOffsetPx = DefaultSkeletonVerticalOffsetPx;
        _skeletonHorizontalOffsetPx = DefaultSkeletonHorizontalOffsetPx;
        _skeletonScale = DefaultSkeletonScale;
        Changed?.Invoke();
    }

    /// <summary>Reset every value to the compiled default.</summary>
    public static void ResetToDefaults()
    {
        ResetFaceToDefaults();
        ResetSkeletonToDefaults();
        Changed?.Invoke();
    }

    private sealed class Dto
    {
        [JsonPropertyName("verticalOffsetPx")]
        public double VerticalOffsetPx { get; set; } = DefaultVerticalOffsetPx;

        [JsonPropertyName("horizontalOffsetPx")]
        public double HorizontalOffsetPx { get; set; } = DefaultHorizontalOffsetPx;

        [JsonPropertyName("meshScale")]
        public double MeshScale { get; set; } = DefaultMeshScale;

        [JsonPropertyName("skeletonVerticalOffsetPx")]
        public double SkeletonVerticalOffsetPx { get; set; } = DefaultSkeletonVerticalOffsetPx;

        [JsonPropertyName("skeletonHorizontalOffsetPx")]
        public double SkeletonHorizontalOffsetPx { get; set; } = DefaultSkeletonHorizontalOffsetPx;

        [JsonPropertyName("skeletonScale")]
        public double SkeletonScale { get; set; } = DefaultSkeletonScale;
    }
}
