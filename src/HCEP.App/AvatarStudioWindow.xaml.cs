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
/// Code-behind for the HCEP Avatar Studio Window.
/// Hosts the interactive preview and connects to the AvatarStudioViewModel.
/// </summary>
public partial class AvatarStudioWindow : Window
{
    private readonly AvatarStudioViewModel _viewModel;

    public AvatarStudioWindow(AvatarStudioViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            AvatarPreviewHost.Content = _viewModel.PreviewSvgAvatar;
        };
    }
}
