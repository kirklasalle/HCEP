// --------------------------------------------------------------
// HCEP — Human Communication Eye Protocol
// Copyright — 2026 Kirk LaSalle. All rights reserved.
// --------------------------------------------------------------

using System.Windows;

namespace HCEP.App;

/// <summary>
/// Main window — hosts the HCEP real-time dashboard.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closing += async (_, _) => await viewModel.ShutdownAsync();
    }
}