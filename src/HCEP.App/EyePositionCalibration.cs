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
/// Runtime-adjustable eye position calibration settings for the 3D Wireframe avatar.
/// These values define the proportional placement of the left and right eye sockets
/// as fractions of the face mesh bounding box.
///
/// Values are persisted to %LocalAppData%\HCEP\eye-position-calibration.json
/// and reloaded at startup.
/// </summary>
public static class EyePositionCalibration
{
    public const double DefaultRightEyeX = 0.323;
    public const double DefaultRightEyeY = 0.418;
    public const double DefaultLeftEyeX = 0.677;
    public const double DefaultLeftEyeY = 0.418;

    private static double _rightEyeX = DefaultRightEyeX;
    private static double _rightEyeY = DefaultRightEyeY;
    private static double _leftEyeX = DefaultLeftEyeX;
    private static double _leftEyeY = DefaultLeftEyeY;

    public static double RightEyeX
    {
        get => _rightEyeX;
        set
        {
            if (Math.Abs(_rightEyeX - value) < 1e-6) return;
            _rightEyeX = Math.Clamp(value, 0.0, 1.0);
            Changed?.Invoke();
        }
    }

    public static double RightEyeY
    {
        get => _rightEyeY;
        set
        {
            if (Math.Abs(_rightEyeY - value) < 1e-6) return;
            _rightEyeY = Math.Clamp(value, 0.0, 1.0);
            Changed?.Invoke();
        }
    }

    public static double LeftEyeX
    {
        get => _leftEyeX;
        set
        {
            if (Math.Abs(_leftEyeX - value) < 1e-6) return;
            _leftEyeX = Math.Clamp(value, 0.0, 1.0);
            Changed?.Invoke();
        }
    }

    public static double LeftEyeY
    {
        get => _leftEyeY;
        set
        {
            if (Math.Abs(_leftEyeY - value) < 1e-6) return;
            _leftEyeY = Math.Clamp(value, 0.0, 1.0);
            Changed?.Invoke();
        }
    }

    /// <summary>Fires whenever any calibration value changes.</summary>
    public static event Action? Changed;

    // ── Offsets from defaults ──────────────────────────────────────
    // These deltas are applied additively (as fractions of the face mesh
    // bounding box) in ALL Avatar3DControl eye-rendering paths — including
    // the feature-point-anchored and FP-only fallback paths. This ensures
    // slider adjustments always have a visible effect, even when the
    // Candide-3 projected mesh is unavailable.

    /// <summary>Right eye X shift as fraction of mesh width (0 when at default).</summary>
    public static double RightEyeOffsetX => _rightEyeX - DefaultRightEyeX;

    /// <summary>Right eye Y shift as fraction of mesh height (0 when at default).</summary>
    public static double RightEyeOffsetY => _rightEyeY - DefaultRightEyeY;

    /// <summary>Left eye X shift as fraction of mesh width (0 when at default).</summary>
    public static double LeftEyeOffsetX => _leftEyeX - DefaultLeftEyeX;

    /// <summary>Left eye Y shift as fraction of mesh height (0 when at default).</summary>
    public static double LeftEyeOffsetY => _leftEyeY - DefaultLeftEyeY;

    /// <summary>Resolves the persistence file path under %LocalAppData%\HCEP\.</summary>
    public static string GetPersistencePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "HCEP");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "eye-position-calibration.json");
    }

    /// <summary>Restore calibration values from disk. Missing/invalid files are silently ignored.</summary>
    public static void Load()
    {
        try
        {
            var path = GetPersistencePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto is null) return;

            _rightEyeX = Math.Clamp(dto.RightEyeX, 0.10, 0.90);
            _rightEyeY = Math.Clamp(dto.RightEyeY, 0.10, 0.70);
            _leftEyeX = Math.Clamp(dto.LeftEyeX, 0.10, 0.90);
            _leftEyeY = Math.Clamp(dto.LeftEyeY, 0.10, 0.70);
            Changed?.Invoke();
        }
        catch
        {
            // Defensive: never let a corrupt calibration file crash startup.
        }
    }

    /// <summary>Persist current calibration values to disk. Errors are swallowed by design.</summary>
    public static void Save()
    {
        try
        {
            var path = GetPersistencePath();
            var dto = new Dto
            {
                RightEyeX = _rightEyeX,
                RightEyeY = _rightEyeY,
                LeftEyeX = _leftEyeX,
                LeftEyeY = _leftEyeY
            };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal
        }
    }

    /// <summary>Reset alignment values to the compiled defaults.</summary>
    public static void ResetToDefaults()
    {
        _rightEyeX = DefaultRightEyeX;
        _rightEyeY = DefaultRightEyeY;
        _leftEyeX = DefaultLeftEyeX;
        _leftEyeY = DefaultLeftEyeY;
        Changed?.Invoke();
    }

    private sealed class Dto
    {
        [JsonPropertyName("rightEyeX")]
        public double RightEyeX { get; set; } = DefaultRightEyeX;

        [JsonPropertyName("rightEyeY")]
        public double RightEyeY { get; set; } = DefaultRightEyeY;

        [JsonPropertyName("leftEyeX")]
        public double LeftEyeX { get; set; } = DefaultLeftEyeX;

        [JsonPropertyName("leftEyeY")]
        public double LeftEyeY { get; set; } = DefaultLeftEyeY;
    }
}
