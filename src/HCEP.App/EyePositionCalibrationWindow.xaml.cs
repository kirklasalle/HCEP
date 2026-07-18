// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
using System.Windows;

namespace HCEP.App;

/// <summary>
/// Interactive live-preview calibration window for adjusting the 3D Wireframe avatar's
/// proportional eye socket positions. Adjustments are written to <see cref="EyePositionCalibration"/>,
/// which signals changes to the 3D avatar control to redraw in real time.
/// </summary>
public partial class EyePositionCalibrationWindow : Window
{
    private bool _initialized;

    public EyePositionCalibrationWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RightEyeXSlider.Value = EyePositionCalibration.RightEyeX;
            RightEyeYSlider.Value = EyePositionCalibration.RightEyeY;
            LeftEyeXSlider.Value = EyePositionCalibration.LeftEyeX;
            LeftEyeYSlider.Value = EyePositionCalibration.LeftEyeY;
            UpdateReadouts();
            _initialized = true;
        };
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;

        if (sender == RightEyeXSlider)
        {
            EyePositionCalibration.RightEyeX = e.NewValue;
            RightEyeXReadout.Text = e.NewValue.ToString("F3");
        }
        else if (sender == RightEyeYSlider)
        {
            EyePositionCalibration.RightEyeY = e.NewValue;
            RightEyeYReadout.Text = e.NewValue.ToString("F3");
        }
        else if (sender == LeftEyeXSlider)
        {
            EyePositionCalibration.LeftEyeX = e.NewValue;
            LeftEyeXReadout.Text = e.NewValue.ToString("F3");
        }
        else if (sender == LeftEyeYSlider)
        {
            EyePositionCalibration.LeftEyeY = e.NewValue;
            LeftEyeYReadout.Text = e.NewValue.ToString("F3");
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        EyePositionCalibration.ResetToDefaults();
        RightEyeXSlider.Value = EyePositionCalibration.RightEyeX;
        RightEyeYSlider.Value = EyePositionCalibration.RightEyeY;
        LeftEyeXSlider.Value = EyePositionCalibration.LeftEyeX;
        LeftEyeYSlider.Value = EyePositionCalibration.LeftEyeY;
        UpdateReadouts();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        EyePositionCalibration.Save();
        MessageBox.Show(this, "Eye calibration settings saved to disk.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateReadouts()
    {
        RightEyeXReadout.Text = RightEyeXSlider.Value.ToString("F3");
        RightEyeYReadout.Text = RightEyeYSlider.Value.ToString("F3");
        LeftEyeXReadout.Text = LeftEyeXSlider.Value.ToString("F3");
        LeftEyeYReadout.Text = LeftEyeYSlider.Value.ToString("F3");
    }
}
