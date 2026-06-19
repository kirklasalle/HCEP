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

namespace HCEP.App;

/// <summary>
/// Dedicated window displaying all three Kinect sensor streams:
/// RGB, Infrared, and Depth side-by-side.
/// </summary>
public partial class SensorViewWindow : Window
{
    private readonly SensorViewViewModel _viewModel;

    public SensorViewWindow(SensorViewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => _viewModel.Subscribe();
        Closing += (_, _) => _viewModel.Unsubscribe();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
