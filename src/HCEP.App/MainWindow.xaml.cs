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

// --------------------------------------------------------------
// HCEP — Human Communication Eye Protocol
// Copyright — 2026 Kirk LaSalle. All rights reserved.
// --------------------------------------------------------------

using System.Windows;
using System.Windows.Input;

namespace HCEP.App;

/// <summary>
/// Main window — hosts the HCEP real-time dashboard.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closing += async (_, _) => await viewModel.ShutdownAsync();
    }

    private void ChatInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (!_viewModel.SendCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        _viewModel.SendCommand.Execute(null);
    }
}