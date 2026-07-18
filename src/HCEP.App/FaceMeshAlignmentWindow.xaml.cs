// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Windows;

namespace HCEP.App;

/// <summary>
/// Interactive live-preview tool for aligning the video overlay (face mesh,
/// face rectangle, skeleton, gaze markers) with the real color feed.
///
/// <para>
/// Adjustments are written directly to <see cref="OverlayAlignment"/>, which
/// broadcasts a <see cref="OverlayAlignment.Changed"/> event so every
/// active overlay control invalidates and re-renders. Values persist to
/// <c>%LocalAppData%\HCEP\overlay-alignment.json</c> when the user clicks Save.
/// </para>
///
/// <para>
/// This window is additive — the existing full-screen Gaze Calibration
/// window is unchanged and remains fully functional.
/// </para>
/// </summary>
public partial class FaceMeshAlignmentWindow : Window
{
    private bool _initialized;

    public FaceMeshAlignmentWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            VerticalSlider.Value = OverlayAlignment.VerticalOffsetPx;
            HorizontalSlider.Value = OverlayAlignment.HorizontalOffsetPx;
            ScaleSlider.Value = OverlayAlignment.MeshScale;
            UpdateReadouts();
            _initialized = true;
        };
    }

    private void OnVerticalChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.VerticalOffsetPx = e.NewValue;
        VerticalReadout.Text = e.NewValue.ToString("F1");
    }

    private void OnHorizontalChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.HorizontalOffsetPx = e.NewValue;
        HorizontalReadout.Text = e.NewValue.ToString("F1");
    }

    private void OnScaleChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.MeshScale = e.NewValue;
        ScaleReadout.Text = e.NewValue.ToString("F2");
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        OverlayAlignment.ResetFaceToDefaults();
        VerticalSlider.Value = OverlayAlignment.VerticalOffsetPx;
        HorizontalSlider.Value = OverlayAlignment.HorizontalOffsetPx;
        ScaleSlider.Value = OverlayAlignment.MeshScale;
        UpdateReadouts();
        SaveStatus.Text = "Reset to defaults (unsaved).";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        OverlayAlignment.Save();
        SaveStatus.Text = "Saved.";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateReadouts()
    {
        VerticalReadout.Text = VerticalSlider.Value.ToString("F1");
        HorizontalReadout.Text = HorizontalSlider.Value.ToString("F1");
        ScaleReadout.Text = ScaleSlider.Value.ToString("F2");
    }
}
