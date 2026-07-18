// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Windows;

namespace HCEP.App;

/// <summary>
/// Interactive live-preview tool for aligning only the skeleton overlay.
/// </summary>
public partial class SkeletalAlignmentWindow : Window
{
    private bool _initialized;

    public SkeletalAlignmentWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            VerticalSlider.Value = OverlayAlignment.SkeletonVerticalOffsetPx;
            HorizontalSlider.Value = OverlayAlignment.SkeletonHorizontalOffsetPx;
            ScaleSlider.Value = OverlayAlignment.SkeletonScale;
            UpdateReadouts();
            _initialized = true;
        };
    }

    private void OnVerticalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.SkeletonVerticalOffsetPx = e.NewValue;
        VerticalReadout.Text = e.NewValue.ToString("F1");
    }

    private void OnHorizontalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.SkeletonHorizontalOffsetPx = e.NewValue;
        HorizontalReadout.Text = e.NewValue.ToString("F1");
    }

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        OverlayAlignment.SkeletonScale = e.NewValue;
        ScaleReadout.Text = e.NewValue.ToString("F2");
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        OverlayAlignment.ResetSkeletonToDefaults();
        VerticalSlider.Value = OverlayAlignment.SkeletonVerticalOffsetPx;
        HorizontalSlider.Value = OverlayAlignment.SkeletonHorizontalOffsetPx;
        ScaleSlider.Value = OverlayAlignment.SkeletonScale;
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